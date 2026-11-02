using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Methadrenaline.Server.Core;

namespace Methadrenaline.Server.Voting;

public sealed class ModeVoteHttpService : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly ModeVoteBridge bridge;
    private readonly byte[] authorizationHash;
    private readonly Action<Exception>? reportError;
    private readonly SemaphoreSlim slots = new(8);
    private readonly object rateLock = new();
    private double tokens = 40;
    private long lastRequest = Stopwatch.GetTimestamp();
    private Task? worker;
    private volatile bool stopped;

    public ModeVoteHttpService(string address, int port, string token, ModeVoteBridge bridge, Action<Exception>? reportError = null)
    {
        if (!HttpListener.IsSupported) throw new PlatformNotSupportedException("HttpListener is unavailable.");
        if (!PrivateAddress(address)) throw new ArgumentException("Voting must bind a specific private IPv4 address.");
        this.bridge = bridge;
        this.reportError = reportError;
        using var hash = SHA256.Create();
        authorizationHash = hash.ComputeHash(Encoding.UTF8.GetBytes("Bearer " + token));
        listener.Prefixes.Add($"http://{address}:{port}/");
        listener.IgnoreWriteExceptions = true;
    }

    public static bool PrivateAddress(string value)
    {
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 127 || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31);
    }

    public void Start()
    {
        listener.Start();
        worker = Task.Run(Listen);
    }

    private bool Allow()
    {
        lock (rateLock)
        {
            long now = Stopwatch.GetTimestamp();
            tokens = Math.Min(40, tokens + (now - lastRequest) * 20.0 / Stopwatch.Frequency);
            lastRequest = now;
            if (tokens < 1) return false;
            tokens--;
            return true;
        }
    }

    private async Task Listen()
    {
        while (!stopped)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) when (stopped || !listener.IsListening) { break; }
            catch (Exception error) { reportError?.Invoke(error); break; }
            if (!Allow() || !slots.Wait(0))
            {
                context.Response.StatusCode = 429;
                context.Response.Close();
                continue;
            }
            _ = Handle(context);
        }
    }

    private async Task Handle(HttpListenerContext context)
    {
        using var timeout = new Timer(_ => { try { context.Response.Abort(); } catch { } }, null, 4000, Timeout.Infinite);
        try
        {
            var request = context.Request;
            string authorization = request.Headers["Authorization"] ?? "";
            using var hash = SHA256.Create();
            byte[] supplied = hash.ComputeHash(Encoding.UTF8.GetBytes(authorization.Length > 512 ? "" : authorization));
            int mismatch = 0;
            for (int i = 0; i < supplied.Length; i++) mismatch |= supplied[i] ^ authorizationHash[i];
            if (mismatch != 0) { await Error(context, 401, "unauthorized"); return; }
            if (!string.IsNullOrEmpty(request.Url?.Query)) { await Error(context, 400, "query_not_supported"); return; }
            string path = request.Url?.AbsolutePath ?? "";
            if (path != "/status" && path != "/vote") { await Error(context, 404, "not_found"); return; }
            if (request.HttpMethod != (path == "/status" ? "GET" : "POST")) { await Error(context, 405, "method_not_allowed"); return; }
            if (path == "/status")
            {
                var snapshot = bridge.Snapshot;
                if (snapshot == null) { await Error(context, 503, "unavailable"); return; }
                await Reply(context, snapshot.Fresh ? 200 : 503, snapshot.Json);
                return;
            }
            if (!(request.ContentType ?? "").Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase))
            { await Error(context, 415, "json_required"); return; }
            if (request.ContentLength64 > 1024) { await Error(context, 413, "body_too_large"); return; }
            if (request.ContentLength64 < 0) { await Error(context, 411, "content_length_required"); return; }
            var bytes = new byte[(int)request.ContentLength64];
            int read = 0;
            while (read < bytes.Length)
            {
                int count = await request.InputStream.ReadAsync(bytes, read, bytes.Length - read).ConfigureAwait(false);
                if (count == 0) { await Error(context, 400, "invalid_body"); return; }
                read += count;
            }
            ModeVoteRequest vote;
            try { vote = ModeVoteJson.Read(bytes); }
            catch (Exception error) when (error is JsonException || error is ArgumentException)
            { await Error(context, 400, "invalid_vote"); return; }
            var pending = bridge.Enqueue(vote);
            if (pending == null) { await Error(context, 503, "unavailable"); return; }
            var finished = await Task.WhenAny(pending.Completion, Task.Delay(2000)).ConfigureAwait(false);
            if (finished != pending.Completion) pending.Cancel();
            string? result = finished == pending.Completion ? await pending.Completion.ConfigureAwait(false) : null;
            if (result == null) { await Error(context, 503, "main_thread_timeout"); return; }
            await Reply(context, 200, ModeVoteJson.Write(new ModeVoteResult(result)));
        }
        catch (Exception error)
        {
            if (!(error is IOException) && !(error is HttpListenerException) && !(error is ObjectDisposedException)) reportError?.Invoke(error);
            try { context.Response.Abort(); } catch { }
        }
        finally { slots.Release(); }
    }

    private static Task Error(HttpListenerContext context, int code, string error) => Reply(context, code, ModeVoteJson.Write(new ModeVoteError(error)));
    private static async Task Reply(HttpListenerContext context, int code, byte[] json)
    {
        context.Response.StatusCode = code;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.KeepAlive = false;
        context.Response.ContentLength64 = json.Length;
        await context.Response.OutputStream.WriteAsync(json, 0, json.Length).ConfigureAwait(false);
        context.Response.Close();
    }

    public void Dispose()
    {
        stopped = true;
        listener.Close();
        worker?.Wait(1000);
    }
}
