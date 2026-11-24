using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using PlayerRoles.Voice;
using VoiceChat;
using VoiceChat.Codec;
using VoiceChat.Codec.Enums;
using VoiceChat.Networking;
using Logger = LabApi.Features.Console.Logger;

namespace ScpProximityChat.Core;

internal static class IntercomSpeech
{
    private static readonly Vector3 RelayPosition = new(0f, 1024f, 0f);
    private static readonly Dictionary<ReferenceHub, Broadcast> Broadcasts = new();
    private static readonly List<ReferenceHub> Finished = new();
    private static float retryAfter;

    internal static void Begin(ReferenceHub hub)
    {
        if (Player.Get(hub) is { IsSCP: true } player && player.IsProximityChatEnabled())
            GetBroadcast(player);
    }

    internal static bool Process(Player source, ref VoiceMessage message)
    {
        if (message.SpeakerNull || message.Speaker != source.ReferenceHub || message.Data == null ||
            message.DataLength <= 0 || message.DataLength > 512 || message.DataLength > message.Data.Length) return false;
        var broadcast = GetBroadcast(source);
        return broadcast != null && broadcast.Process(ref message);
    }

    internal static void Send(NetworkConnection connection, VoiceMessage message, int channel)
    {
        if (Broadcasts.TryGetValue(message.Speaker, out var current) && current.IsIntercomFrame(message))
        {
            if (message.Channel != VoiceChatChannel.Intercom || !current.Ready) return;
            message.Speaker = current.Relay;
        }
        else if (message.Channel == VoiceChatChannel.Intercom && message.Speaker.IsSCP())
        {
            return;
        }
        connection.Send(message, channel);
    }

    internal static void Update()
    {
        foreach (var hub in Broadcasts.Keys)
        {
            if (hub != null && Intercom.State != IntercomState.NotFound &&
                (Intercom.HasOverride(hub) || (Intercom.State is IntercomState.Starting or IntercomState.InUse &&
                Intercom._singleton._curSpeaker == hub))) continue;
            Finished.Add(hub!);
        }
        foreach (var hub in Finished) Remove(hub);
        Finished.Clear();
    }

    internal static void Forget(Player source) => Remove(source.ReferenceHub);

    private static void Remove(ReferenceHub hub)
    {
        if (Broadcasts.Remove(hub, out var broadcast)) broadcast.Dispose();
    }

    internal static void Reset()
    {
        foreach (var hub in new List<ReferenceHub>(Broadcasts.Keys)) Remove(hub);
        retryAfter = 0f;
    }

    private static Broadcast? GetBroadcast(Player source)
    {
        var hub = source.ReferenceHub;
        if (Broadcasts.TryGetValue(hub, out var existing))
        {
            if (existing.Ready) return existing;
            Remove(hub);
        }
        if (Time.unscaledTime < retryAfter) return null;
        try { return Broadcasts[hub] = new Broadcast(source); }
        catch (Exception error)
        {
            retryAfter = Time.unscaledTime + 2f;
            Logger.Warn("[SCP Proximity Chat] Could not prepare intercom relay: " + error.GetType().Name);
            return null;
        }
    }

    private sealed class Broadcast : IDisposable
    {
        internal ReferenceHub Relay { get; }
        internal bool Ready => Relay && Relay.roleManager.CurrentRole is IVoiceRole { VoiceModule: HumanVoiceModule };
        private readonly OpusDecoder decoder = new();
        private readonly OpusEncoder encoder;
        private readonly IntercomSound sound = new();
        private readonly float[] samples = new float[480];
        private readonly byte[] encoded = new byte[512];
        private float lastPacket = float.NegativeInfinity;
        private float retrySoundAfter;

        internal bool IsIntercomFrame(VoiceMessage message) => ReferenceEquals(message.Data, encoded);

        internal Broadcast(Player source)
        {
            try
            {
                encoder = new OpusEncoder(OpusApplicationType.Voip);
                try
                {
                    Relay = DummyUtils.SpawnDummy(source.Nickname)
                        ?? throw new InvalidOperationException("Intercom relay did not spawn.");
                    Relay.authManager.UserId = "ID_Dummy";
                    Relay.roleManager.ServerSetRole(RoleTypeId.Tutorial, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.None);
                    var relay = Player.Get(Relay);
                    if (relay == null || Relay.roleManager.CurrentRole is not IVoiceRole { VoiceModule: HumanVoiceModule })
                        throw new InvalidOperationException("Intercom relay has no human playback.");
                    relay.Position = RelayPosition;
                }
                catch
                {
                    if (Relay) NetworkServer.Destroy(Relay.gameObject);
                    encoder.Dispose();
                    throw;
                }
            }
            catch { decoder.Dispose(); throw; }
        }

        internal bool Process(ref VoiceMessage message)
        {
            float now = Time.unscaledTime;
            if (now >= retrySoundAfter)
            {
                try
                {
                    int count = decoder.Decode(message.Data, message.DataLength, samples);
                    if (count is not (120 or 240 or 480)) return false;
                    if (now - lastPacket > .35f) sound.Reset();
                    lastPacket = now;
                    sound.Process(samples, count);
                    int length = encoder.Encode(samples, encoded, count);
                    if (length <= 0 || length > encoded.Length) return false;
                    message.Data = encoded;
                    message.DataLength = length;
                    return true;
                }
                catch (Exception error)
                {
                    retrySoundAfter = now + 1f;
                    Logger.Warn("[SCP Proximity Chat] Intercom audio processing interrupted: " + error.GetType().Name);
                }
            }
            Buffer.BlockCopy(message.Data, 0, encoded, 0, message.DataLength);
            message.Data = encoded;
            return true;
        }

        public void Dispose()
        {
            if (Relay && NetworkServer.active) NetworkServer.Destroy(Relay.gameObject);
            encoder.Dispose();
            decoder.Dispose();
        }
    }
}
