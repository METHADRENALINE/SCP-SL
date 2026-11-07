using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using VoiceChat;
using VoiceChat.Codec;
using VoiceChat.Codec.Enums;
using VoiceChat.Networking;

namespace Methadrenaline.Server;

internal static class ParavozVoiceDiagnostics
{
    internal static IReadOnlyDictionary<string, bool> Run(ParavozController mode, Player carrier, Player runner)
    {
        var results = new Dictionary<string, bool>();
        using var source = new OpusEncoder(OpusApplicationType.Voip);
        using var receiver = new OpusDecoder();
        using var reference = new OpusDecoder();
        var pcm = new float[480];
        var encoded = new byte[512];
        var decoded = new float[480];
        var original = new float[480];
        bool valid = true, metadata = true, untouched = true;
        double outputEnergy = 0, inputEnergy = 0;
        byte[]? retained = null, retainedCopy = null;
        try
        {
            for (int frame = 0; frame < 100; frame++)
            {
                for (int i = 0; i < pcm.Length; i++)
                {
                    double time = (frame * 480 + i) / 48000d;
                    pcm[i] = (float)(0.02 * Math.Sin(2 * Math.PI * 120 * time) + 0.005 * Math.Sin(2 * Math.PI * 1000 * time));
                }
                int length = source.Encode(pcm, encoded);
                var copy = (byte[])encoded.Clone();
                var message = new VoiceMessage(carrier.ReferenceHub, VoiceChatChannel.Proximity, encoded, length, false);
                var ev = new PlayerSendingVoiceMessageEventArgs(ref message);
                PlayerEvents.OnSendingVoiceMessage(ev);
                valid &= !ReferenceEquals(ev.Message.Data, encoded) && receiver.Decode(ev.Message.Data, ev.Message.DataLength, decoded) == 480;
                valid &= reference.Decode(encoded, length, original) == 480;
                metadata &= ev.IsAllowed && ev.Message.Speaker == carrier.ReferenceHub && ev.Message.Channel == VoiceChatChannel.Proximity && !ev.Message.SpeakerNull;
                untouched &= encoded.SequenceEqual(copy);
                if (frame > 10)
                    for (int i = 0; i < 480; i++)
                    {
                        outputEnergy += decoded[i] * decoded[i];
                        inputEnergy += original[i] * original[i];
                        valid &= !float.IsNaN(decoded[i]) && !float.IsInfinity(decoded[i]);
                    }
                if (frame == 10) { retained = ev.Message.Data; retainedCopy = (byte[])retained.Clone(); }
            }
            results["voice_native_opus_roundtrip"] = valid;
            results["voice_amplified_after_opus"] = inputEnergy > 0 && Math.Sqrt(outputEnergy / inputEnergy) > 8;
            results["voice_original_and_previous_packets_untouched"] = untouched && retained != null && retained.SequenceEqual(retainedCopy!);
            results["voice_preserves_speaker_channel_and_allowance"] = metadata;

            var normal = new VoiceMessage(runner.ReferenceHub, VoiceChatChannel.Radio, encoded, 1, false);
            var normalEvent = new PlayerSendingVoiceMessageEventArgs(ref normal);
            PlayerEvents.OnSendingVoiceMessage(normalEvent);
            results["voice_runner_unmodified"] = ReferenceEquals(normalEvent.Message.Data, encoded) && normalEvent.Message.DataLength == 1 && normalEvent.Message.Channel == VoiceChatChannel.Radio;

            var cancelled = new VoiceMessage(carrier.ReferenceHub, VoiceChatChannel.Proximity, encoded, 1, false);
            var cancelledEvent = new PlayerSendingVoiceMessageEventArgs(ref cancelled) { IsAllowed = false };
            mode.Voice.Process(cancelledEvent);
            results["voice_cancellation_preserved"] = !cancelledEvent.IsAllowed && ReferenceEquals(cancelledEvent.Message.Data, encoded);

            int streams = mode.Voice.StreamCount;
            var invalid = new VoiceMessage(carrier.ReferenceHub, VoiceChatChannel.Proximity, encoded, encoded.Length + 1, false);
            var invalidEvent = new PlayerSendingVoiceMessageEventArgs(ref invalid);
            mode.Voice.Process(invalidEvent);
            results["voice_invalid_length_ignored"] = ReferenceEquals(invalidEvent.Message.Data, encoded) && mode.Voice.StreamCount == streams;
            results["voice_stream_created_only_for_carrier"] = streams == 1;
        }
        finally { mode.Voice.Reset(); }
        results["voice_codec_state_released"] = mode.Voice.StreamCount == 0;
        return results;
    }
}
