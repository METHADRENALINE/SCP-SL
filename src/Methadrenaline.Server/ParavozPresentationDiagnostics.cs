using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using InventorySystem.Items.Radio;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using Methadrenaline.Server.Core;
using Mirror;
using PlayerRoles;
using PlayerRoles.Voice;
using UnityEngine;
using VoiceChat;
using VoiceChat.Codec;
using VoiceChat.Codec.Enums;
using VoiceChat.Networking;

namespace Methadrenaline.Server;

internal static class ParavozPresentationDiagnostics
{
    internal static IReadOnlyDictionary<string, bool> Run(ParavozController mode, Player carrier, Player second, Player listener)
    {
        var checks = new Dictionary<string, bool>();
        var presentation = mode.Presentation;
        var original = new[] { carrier.Position, second.Position, listener.Position };
        var players = new[] { carrier, second, listener };
        var audio = presentation.Playback!;
        try
        {
            presentation.RefreshNow();
            var emitter = presentation.Emitters[carrier.NetworkId];
            var clip = audio.ClipsById.Values.Single();
            var data = AudioClipStorage.AudioClips[presentation.ClipName];
            checks["music_mono_48k_and_packet_aligned"] = data.SampleRate == 48000 && data.Channels == 1 && data.Samples.Length % 480 == 0;
            checks["music_one_independent_loop"] = clip.Loop && !clip.DestroyOnEnd && !audio.DestroyWhenAllClipsPlayed && !audio.SendSoundGlobally && audio.Condition != null;
            var braceletAudio = ServerPlugin.Instance!.Bracelets!.Audio;
            var life = BraceletController.Life(carrier);
            try
            {
                braceletAudio.PlayConfirmation(carrier);
                var bracelet = braceletAudio.Playback(life);
                checks["music_and_bracelet_use_separate_audio_controllers"] = bracelet != null && bracelet.ControllerID != audio.ControllerID && audio.ClipsById.Count == 1;
            }
            finally { braceletAudio.Stop(life); }
            checks["music_owner_hears_own_source"] = emitter.Personalization[carrier]?.Volume == ParavozPresentationPolicy.MusicVolume;
            checks["visuals_use_registered_noncolliding_toys"] = emitter.Embers.Count == ParavozPresentationPolicy.EmberCount &&
                emitter.Embers.All(e => e.Flags == AdminToys.PrimitiveFlags.Visible && e.GameObject.GetComponents<Collider>().All(c => !c.enabled)) &&
                emitter.Light.ShadowType == LightShadows.None;
            checks["visuals_stay_below_first_person_camera"] = emitter.Embers.All(e => e.Transform.position.y < carrier.ReferenceHub.PlayerCameraReference.position.y - 0.3f);
            CheckFlames(checks, emitter, presentation);
            bool parentWire = true;
            foreach (var toy in emitter.Toys)
            {
                using var writer = NetworkWriterPool.Get();
                toy.Base.OnSerialize(writer, true);
                var bytes = writer.ToArray();
                parentWire &= toy.Parent == carrier.ReferenceHub.transform && BitConverter.ToUInt32(bytes, bytes.Length - 4) == carrier.NetworkId;
            }
            checks["all_presentation_spawns_reference_player_network_parent"] = parentWire;
            foreach (float height in new[] { 1000f, -990f })
            {
                carrier.Position = new Vector3(90, height, -45);
                presentation.RefreshNow();
                Vector3 reconstructed = carrier.ReferenceHub.transform.TransformPoint(emitter.Speaker.Base.NetworkPosition);
                checks["music_client_position_at_" + height] = Vector3.Distance(reconstructed, carrier.ReferenceHub.PlayerCameraReference.position) < 0.01f;
                checks["visual_client_position_at_" + height] = emitter.Embers.All(e => Vector3.Distance(carrier.ReferenceHub.transform.TransformPoint(e.Base.NetworkPosition), e.Transform.position) < 0.01f);
            }
            carrier.Position = original[0];
            second.Position = carrier.Position + Vector3.right * 20;
            presentation.Apply(second);
            listener.Position = carrier.Position + Vector3.right;
            presentation.RefreshNow();
            var other = presentation.Emitters[second.NetworkId];
            checks["multiple_carriers_share_one_encoded_track"] = ReferenceEquals(audio, presentation.Playback) && audio.ClipsById.Count == 1 && emitter.Speaker.ControllerId == other.Speaker.ControllerId;
            checks["listener_hears_one_nearest_carrier"] = emitter.Personalization[listener]?.Volume > 0 && other.Personalization[listener]?.Volume == 0;
            checks["each_carrier_hears_only_own_music"] = emitter.Personalization[carrier]?.Volume > 0 && other.Personalization[carrier]?.Volume == 0 && other.Personalization[second]?.Volume > 0 && emitter.Personalization[second]?.Volume == 0;
            listener.Position = second.Position + Vector3.right;
            presentation.RefreshNow();
            checks["nearest_handover_does_not_restart_track"] = other.Personalization[listener]?.Volume > 0 && emitter.Personalization[listener]?.Volume == 0 && ReferenceEquals(audio.ClipsById.Values.Single(), clip);
            listener.Position = carrier.Position + Vector3.right * 100;
            presentation.RefreshNow();
            checks["out_of_range_listener_receives_no_music"] = !audio.Condition!(listener.ReferenceHub) && emitter.Personalization[listener]?.Volume == 0 && other.Personalization[listener]?.Volume == 0;

            int read = clip.ReadPosition;
            bool destroyed = false;
            try
            {
                clip.ReadPosition = clip.Samples.Length - 480;
                float[] tail = clip.ReadPcmChunk(ref destroyed);
                float[] head = clip.ReadPcmChunk(ref destroyed);
                checks["native_music_loop_has_full_continuous_packets"] = !destroyed && tail.Length == 480 && head.Length == 480 && clip.ReadPosition == 480 && Math.Abs(tail[479] - head[0]) < 0.15f;
                using var encoder = new OpusEncoder(OpusApplicationType.Audio);
                using var decoder = new OpusDecoder();
                var encoded = new byte[512];
                var decoded = new float[480];
                int peak = 0;
                for (int i = 1; i < data.Samples.Length; i++) if (Math.Abs(data.Samples[i]) > Math.Abs(data.Samples[peak])) peak = i;
                var frame = new float[480];
                Array.Copy(data.Samples, peak / 480 * 480, frame, 0, 480);
                int size = 0, decodedCount = 0;
                for (int i = 0; i < 3; i++)
                {
                    size = encoder.Encode(frame, encoded);
                    decodedCount = decoder.Decode(encoded, size, decoded);
                }
                checks["custom_music_decodes_with_native_opus"] = size > 0 && decodedCount == 480 && decoded.Any(v => Math.Abs(v) > 0.00001f) && decoded.All(v => !float.IsNaN(v) && !float.IsInfinity(v));
                var radio = carrier.Items.OfType<LabApi.Features.Wrappers.RadioItem>().FirstOrDefault() ?? (LabApi.Features.Wrappers.RadioItem)carrier.AddItem(ItemType.Radio)!;
                radio.BatteryPercent = 100;
                radio.Base.ServerProcessCmd(RadioMessages.RadioCommand.Enable);
                checks["carrier_radio_bind_still_validates_natively"] = radio.IsUsable && ((HumanVoiceModule)carrier.VoiceModule!).ValidateSend(VoiceChatChannel.Radio) == VoiceChatChannel.Radio;
                var message = new VoiceMessage(carrier.ReferenceHub, VoiceChatChannel.Radio, encoded, size, false);
                var ev = new PlayerSendingVoiceMessageEventArgs(ref message);
                PlayerEvents.OnSendingVoiceMessage(ev);
                checks["carrier_radio_channel_is_not_replaced_by_music"] = ev.IsAllowed && ev.Message.Channel == VoiceChatChannel.Radio && ev.Message.Speaker == carrier.ReferenceHub;
                checks["radio_transmission_keeps_music_playback_and_position"] = ReferenceEquals(presentation.Playback, audio) && ReferenceEquals(audio.ClipsById.Values.Single(), clip) && clip.ReadPosition == 480 && clip.Loop;
            }
            finally { clip.ReadPosition = read; }
            uint[] flameIds = other.Flames.Select(f => f.Base.netId).ToArray();
            presentation.Forget(second.NetworkId);
            presentation.RefreshNow();
            checks["removed_carrier_does_not_leave_an_audio_source"] = presentation.Emitters.Count == 1 && !presentation.Emitters.ContainsKey(second.NetworkId) && audio.ClipsById.Count == 1;
            checks["removed_carrier_leaves_no_flame_objects"] = flameIds.All(id => !NetworkServer.spawned.ContainsKey(id));
        }
        finally
        {
            presentation.Forget(second.NetworkId);
            for (int i = 0; i < players.Length; i++) if (players[i].IsAlive) players[i].Position = original[i];
            presentation.RefreshNow();
        }
        return checks;
    }

    private static void CheckFlames(Dictionary<string, bool> checks, ParavozPresentation.Emitter emitter, ParavozPresentation presentation)
    {
        var player = emitter.Player;
        Transform camera = player.ReferenceHub.PlayerCameraReference;
        Vector3 position = player.Position;
        Quaternion rotation = camera.rotation;
        var wall = new GameObject("ParavozFlameProbeWall");
        try
        {
            player.Position = new Vector3(80, 1000, 80);
            camera.rotation = Quaternion.Euler(-35, 65, 0);
            Physics.SyncTransforms();
            presentation.RefreshNow();
            Vector3 mouth = ParavozFlame.Mouth(camera);
            var flames = emitter.Flames;
            checks["mouth_flame_uses_bounded_noncolliding_network_toys"] = flames.Count == ParavozFlame.Segments * 2 &&
                flames.All(f => f.Flags == AdminToys.PrimitiveFlags.Visible && f.Color.a < 1 && f.GameObject.GetComponents<Collider>().All(c => !c.enabled));
            checks["mouth_flame_follows_camera_pitch_and_yaw"] = flames.All(f => Vector3.Dot(f.Transform.forward, camera.forward) > 0.999f);
            checks["mouth_flame_origin_below_eyes"] = Vector3.Dot(camera.position - mouth, camera.up) > 0.3f;
            checks["mouth_flame_five_meter_extent"] = Within(ParavozFlame.Range) && flames.Any(f => f.Scale.z > 0);

            wall.layer = Enumerable.Range(0, 32).First(layer => (PlayerRolesUtils.AttackMask & (1 << layer)) != 0);
            wall.transform.SetPositionAndRotation(mouth + camera.forward * 1.7f, camera.rotation);
            var collider = wall.AddComponent<BoxCollider>();
            collider.size = new Vector3(3, 3, 0.1f);
            Physics.SyncTransforms();
            presentation.RefreshNow();
            float reach = ParavozFlame.ClearRange(mouth, camera.forward);
            checks["mouth_flame_stops_before_solid_wall"] = reach > 0.5f && reach < 1.4f && Within(reach);
            wall.transform.position = mouth;
            Physics.SyncTransforms();
            presentation.RefreshNow();
            checks["mouth_flame_hidden_inside_obstruction"] = flames.All(f => f.Scale == Vector3.zero);
            collider.enabled = false;
            Physics.SyncTransforms();
            presentation.RefreshNow();
            checks["mouth_flame_resumes_after_wall_removed"] = ParavozFlame.ClearRange(mouth, camera.forward) == ParavozFlame.Range && flames.Any(f => f.Scale.z > 0);

            bool Within(float limit) => flames.Where(f => f.Scale.z > 0).All(f =>
            {
                float distance = Vector3.Dot(f.Transform.position - mouth, camera.forward);
                return distance - f.Scale.z * 0.5f >= -0.01f && distance + f.Scale.z * 0.5f <= limit + 0.01f;
            });
        }
        finally
        {
            UnityEngine.Object.Destroy(wall);
            player.Position = position;
            camera.rotation = rotation;
            Physics.SyncTransforms();
            presentation.RefreshNow();
        }
    }
}
