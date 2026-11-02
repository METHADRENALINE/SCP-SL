using System;
using System.Collections.Generic;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Methadrenaline.Server.Core;
using UnityEngine;
using VoiceChat.Codec;
using VoiceChat.Codec.Enums;
using VoiceChat.Networking;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal sealed class ParavozVoice : IDisposable
{
    private readonly ParavozController mode;
    private readonly Dictionary<uint, Stream> streams = new();
    private readonly Dictionary<uint, float> retryAfter = new();
    internal int StreamCount => streams.Count;
    internal ParavozVoice(ParavozController mode) => this.mode = mode;

    internal void Process(PlayerSendingVoiceMessageEventArgs ev)
    {
        var player = ev.Player;
        if (!ev.IsAllowed || player == null) return;
        if (!player.IsAlive || !mode.IsParavoz(player)) { Forget(player.NetworkId); return; }
        ref VoiceMessage message = ref ev.Message;
        // Never reinterpret another plugin's replacement speaker or null packet.
        if (message.SpeakerNull || message.Speaker != player.ReferenceHub || message.Data == null ||
            message.DataLength <= 0 || message.DataLength > 512 || message.DataLength > message.Data.Length) return;
        uint id = player.NetworkId;
        float now = Time.realtimeSinceStartup;
        if (retryAfter.TryGetValue(id, out float retry) && now < retry) return;
        try
        {
            if (streams.TryGetValue(id, out var stream) && stream.Life != player.LifeId)
            { Forget(id); stream = null; }
            if (stream == null) streams.Add(id, stream = new Stream(player.LifeId));
            stream.Transform(ref message, now);
        }
        catch (Exception error)
        {
            // Leave the original packet intact if the codec fails. Back off so
            // a malformed stream cannot flood the log or interrupt voice chat.
            Forget(id);
            retryAfter[id] = now + 10;
            Logger.Warn("[METHADRENALINE] Paravoz voice processing suspended for 10s: " + error.GetType().Name);
        }
    }

    internal void Forget(uint id)
    {
        if (streams.TryGetValue(id, out var stream)) { streams.Remove(id); stream.Dispose(); }
        retryAfter.Remove(id);
    }
    internal void Reset()
    {
        foreach (var stream in streams.Values) stream.Dispose();
        streams.Clear();
        retryAfter.Clear();
    }
    public void Dispose() => Reset();

    private sealed class Stream : IDisposable
    {
        private readonly OpusDecoder decoder = new();
        private readonly OpusEncoder encoder;
        private readonly ParavozVoiceDsp effect = new();
        private readonly float[] samples = new float[480];
        private readonly byte[] encoded = new byte[512];
        private float lastPacket = float.NegativeInfinity;
        private bool disposed;
        internal int Life { get; }
        internal Stream(int life)
        {
            Life = life;
            try { encoder = new OpusEncoder(OpusApplicationType.Voip); }
            catch { decoder.Dispose(); throw; }
        }

        internal void Transform(ref VoiceMessage message, float now)
        {
            int count = decoder.Decode(message.Data, message.DataLength, samples);
            // Stock clients produce 480-sample packets. Opus also supports the
            // shorter 120/240 frames; larger frames exceed the native buffer.
            if (count != 120 && count != 240 && count != 480) return;
            if (now - lastPacket > 0.5f) effect.Reset();
            lastPacket = now;
            effect.Process(samples, count);
            int length = encoder.Encode(samples, encoded, count);
            if (length <= 0 || length > encoded.Length) return;
            // Incoming network buffers may be pooled/shared. Own the outgoing
            // bytes; never modify Talky's source data or a previous packet.
            var output = new byte[length];
            Buffer.BlockCopy(encoded, 0, output, 0, length);
            message.Data = output;
            message.DataLength = length;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            decoder.Dispose();
            encoder.Dispose();
        }
    }
}

// After all sending listeners (including Talky's analysis of the original
// microphone stream), but before native per-listener validation and fanout.
[HarmonyPatch(typeof(PlayerEvents), nameof(PlayerEvents.OnSendingVoiceMessage))]
internal static class ParavozVoicePatch
{
    [HarmonyPostfix]
    private static void Postfix([HarmonyArgument(0)] PlayerSendingVoiceMessageEventArgs e)
        => ServerPlugin.Instance?.Paravoz?.Voice.Process(e);
}
