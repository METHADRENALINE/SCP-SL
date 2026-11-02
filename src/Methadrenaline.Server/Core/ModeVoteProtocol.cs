using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Methadrenaline.Server.Core;

public sealed class ModeVoteRequest
{
    [JsonPropertyName("boot_id")] public string BootId { get; set; } = "";
    [JsonPropertyName("poll_id")] public string PollId { get; set; } = "";
    [JsonPropertyName("discord_user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    [JsonIgnore]
    public bool Valid => Guid.TryParseExact(BootId, "D", out _) && Guid.TryParseExact(PollId, "D", out _)
        && ModeVotePolicy.ValidUser(UserId) && ModeVotePolicy.TryMode(Mode, out _);
}

public sealed class ModeVoteStatus
{
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("boot_id")] public string BootId { get; set; } = "";
    [JsonPropertyName("heartbeat_sequence")] public long HeartbeatSequence { get; set; }
    [JsonPropertyName("heartbeat_at")] public string HeartbeatAt { get; set; } = "";
    [JsonPropertyName("online_names")] public string[] OnlineNames { get; set; } = Array.Empty<string>();
    [JsonPropertyName("phase")] public string Phase { get; set; } = "empty";
    [JsonPropertyName("mode")] public string? Mode { get; set; }
    [JsonPropertyName("mode_name_ru")] public string? ModeNameRu { get; set; }
    [JsonPropertyName("available_modes")] public ModeVoteOption[] AvailableModes { get; set; } = Array.Empty<ModeVoteOption>();
    [JsonPropertyName("poll")] public ModeVotePoll Poll { get; set; } = new();
}

public sealed class ModeVoteOption
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name_ru")] public string NameRu { get; set; } = "";
}

public sealed class ModeVotePoll
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("deadline")] public string? Deadline { get; set; }
    [JsonPropertyName("open")] public bool Open { get; set; }
}

public sealed class ModeVoteResult
{
    public ModeVoteResult(string result) { Result = result; }
    [JsonPropertyName("result")] public string Result { get; set; }
}

public sealed class ModeVoteError
{
    public ModeVoteError(string error) { Error = error; }
    [JsonPropertyName("error")] public string Error { get; set; }
}

public static class ModeVoteJson
{
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 4 };
    public static byte[] Write<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static ModeVoteRequest Read(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid vote.");
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if ((property.Name != "boot_id" && property.Name != "poll_id" && property.Name != "discord_user_id" && property.Name != "mode")
                || !fields.Add(property.Name) || property.Value.ValueKind != JsonValueKind.String || property.Value.GetString()!.Length > 128)
                throw new JsonException("Invalid vote.");
        }
        if (fields.Count != 4) throw new JsonException("Invalid vote.");
        var request = new ModeVoteRequest
        {
            BootId = root.GetProperty("boot_id").GetString()!,
            PollId = root.GetProperty("poll_id").GetString()!,
            UserId = root.GetProperty("discord_user_id").GetString()!,
            Mode = root.GetProperty("mode").GetString()!
        };
        if (!request.Valid) throw new JsonException("Invalid vote.");
        return request;
    }
}

public static class ModeVoteNames
{
    public static string PlayerName(string? nickname, string? displayName)
    {
        string name = Clean(nickname);
        if (name.Length == 0) name = Clean(displayName);
        return name.Length == 0 ? "?" : name;
    }

    private static string Clean(string? source)
    {
        if (string.IsNullOrEmpty(source)) return "";
        var result = new StringBuilder(100);
        for (int i = 0; i < source!.Length && result.Length < 100; i++)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(source, i);
            int length = char.IsHighSurrogate(source[i]) && i + 1 < source.Length && char.IsLowSurrogate(source[i + 1]) ? 2 : 1;
            if (category != UnicodeCategory.Control && category != UnicodeCategory.Format && category != UnicodeCategory.Surrogate
                && category != UnicodeCategory.OtherNotAssigned && category != UnicodeCategory.PrivateUse
                && category != UnicodeCategory.LineSeparator && category != UnicodeCategory.ParagraphSeparator && result.Length + length <= 100)
            {
                if (category == UnicodeCategory.SpaceSeparator) result.Append(' ');
                else result.Append(source, i, length);
            }
            i += length - 1;
        }
        return result.ToString().Trim();
    }
}
