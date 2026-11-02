using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Methadrenaline.Server.Core;
using Methadrenaline.Server.Voting;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class ModeVoteHttpTests
{
    private sealed class Service : IDisposable
    {
        public readonly string Boot = Guid.NewGuid().ToString("D");
        public readonly ModeVotePolicy Policy = new();
        public readonly ModeVoteBridge Bridge = new();
        public readonly HttpClient Client;
        private readonly ModeVoteHttpService server;
        public Service()
        {
            var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            int port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            server = new ModeVoteHttpService("127.0.0.1", port, token, Bridge);
            Policy.Tick(2, true, 100, _ => 0);
            Bridge.Publish(new ModeVoteStatus { BootId = Boot, HeartbeatSequence = 17, HeartbeatAt = DateTimeOffset.UtcNow.ToString("O"), OnlineNames = new[] { "Test" }, Phase = "voting", Poll = new ModeVotePoll { Id = Policy.PollId, Open = true, Deadline = DateTimeOffset.UtcNow.AddSeconds(15).ToString("O") } });
            server.Start();
            Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(6) };
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        public Task<HttpResponseMessage> Vote(string? boot = null, string? poll = null, string mode = "perks") => Client.PostAsync("/vote", new ByteArrayContent(ModeVoteJson.Write(new ModeVoteRequest { BootId = boot ?? Boot, PollId = poll ?? Policy.PollId!, UserId = "123456789012345678", Mode = mode })) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } });
        public async Task<string> VoteAndPump(string? boot = null, string? poll = null, string mode = "perks")
        {
            var responseTask = Vote(boot, poll, mode);
            for (int i = 0; i < 200 && !responseTask.IsCompleted; i++)
            {
                Bridge.Drain(v => v.BootId == Boot ? Policy.Vote(v.PollId, v.UserId, ModeVotePolicy.TryMode(v.Mode, out var chosen) ? chosen : throw new Exception(), 101) : "closed");
                await Task.Delay(5);
            }
            using var response = await responseTask;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("result").GetString()!;
        }
        public void Dispose() { Client.Dispose(); Bridge.Dispose(); server.Dispose(); }
    }

    [Fact]
    public async Task RealHttpSnapshotAndAuthoritativeVoteRoundTrip()
    {
        using var service = new Service();
        using var response = await service.Client.GetAsync("/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(service.Boot, json.RootElement.GetProperty("boot_id").GetString());
        Assert.Equal(service.Policy.PollId, json.RootElement.GetProperty("poll").GetProperty("id").GetString());
        Assert.Equal("accepted", await service.VoteAndPump());
        Assert.Equal("duplicate", await service.VoteAndPump(mode: "plain"));
        Assert.Equal("closed", await service.VoteAndPump(boot: Guid.NewGuid().ToString("D")));
        Assert.Equal("closed", await service.VoteAndPump(poll: Guid.NewGuid().ToString("D")));
    }

    [Theory]
    [InlineData("/status", "GET")]
    [InlineData("/vote", "POST")]
    public async Task BothRoutesRequireBearer(string path, string method)
    {
        using var service = new Service();
        service.Client.DefaultRequestHeaders.Authorization = null;
        using var response = await service.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        service.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        using var wrong = await service.Client.GetAsync("/status");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Theory]
    [InlineData("/other", "GET", 404)]
    [InlineData("/status", "POST", 405)]
    [InlineData("/vote", "GET", 405)]
    [InlineData("/status?token=forbidden", "GET", 400)]
    public async Task InvalidRoutesAndMethodsAreRejected(string path, string method, int expected)
    {
        using var service = new Service();
        using var response = await service.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(expected, (int)response.StatusCode);
    }

    [Theory]
    [InlineData("{}", "application/json", 400)]
    [InlineData("null", "application/json", 400)]
    [InlineData("malformed", "application/json", 400)]
    [InlineData("{}", "text/plain", 415)]
    public async Task MalformedBodiesReturnExplicitErrors(string body, string type, int expected)
    {
        using var service = new Service();
        using var response = await service.Client.PostAsync("/vote", new StringContent(body, Encoding.UTF8, type));
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Contains("error", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OversizedBodyIsRejected()
    {
        using var service = new Service();
        using var response = await service.Client.PostAsync("/vote", new StringContent(new string('x', 1025), Encoding.UTF8, "application/json"));
        Assert.Equal(413, (int)response.StatusCode);
    }

    [Fact]
    public async Task MissingGameThreadReturnsUnavailableAndDoesNotApplyVoteLater()
    {
        using var service = new Service();
        using var response = await service.Vote();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        service.Bridge.Drain(_ => throw new Exception("Timed out vote applied."));
        service.Policy.Tick(2, true, 130, _ => 0);
        Assert.Equal(CommunityRoundMode.Plain, service.Policy.Selected);
    }

    [Fact]
    public async Task FrozenSnapshotReturns503WithTheOriginalHeartbeat()
    {
        using var service = new Service();
        await Task.Delay(5100);
        using var response = await service.Client.GetAsync("/status");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(17, json.RootElement.GetProperty("heartbeat_sequence").GetInt64());
        using var vote = await service.Vote();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, vote.StatusCode);
    }

    [Fact]
    public void DisabledByDefaultAndWildcardsOrPublicAddressesAreRefused()
    {
        var config = new Config();
        Assert.False(config.ModeVotingEnabled);
        Assert.Equal("127.0.0.1", config.ModeVotingBindAddress);
        Assert.Equal(8790, config.ModeVotingPort);
        Assert.Empty(config.ModeVotingTokenFile);
        foreach (string address in new[] { "0.0.0.0", "+", "*", "::", "8.8.8.8", "localhost" })
            Assert.False(ModeVoteHttpService.PrivateAddress(address));
        Assert.True(ModeVoteHttpService.PrivateAddress("192.168.1.10"));
        Assert.True(ModeVoteHttpService.PrivateAddress("127.0.0.1"));
    }
}
