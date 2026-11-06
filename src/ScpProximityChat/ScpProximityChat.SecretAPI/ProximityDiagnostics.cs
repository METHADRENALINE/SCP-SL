using System.Linq;
using System.Reflection;
using CommandSystem;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using PlayerRoles.Voice;
using RueI.API;
using RueI.API.Elements;
using RueI.API.Elements.Parameters;
using SecretLabNAudio.Core;
using UserSettings.ServerSpecific;
using Mirror;

namespace ScpProximityChat.SecretAPI;

/// <summary>Empty-server integration checks for local voice settings.</summary>
[CommandHandler(typeof(GameConsoleCommandHandler))]
[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class ProximityDiagnostics : ICommand
{
    /// <inheritdoc/>
    public string Command => "scpproxcheck";
    /// <inheritdoc/>
    public string[] Aliases => [];
    /// <inheritdoc/>
    public string Description => "Validate SCP voice volume and spatial settings on an empty server.";

    /// <inheritdoc/>
    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission(PlayerPermissions.FacilityManagement)) { response = "FacilityManagement permission required."; return false; }
        if (Player.List.Any(p => !p.IsHost && !p.IsNpc && !p.IsDummy)) { response = "SCP proximity diagnostics require an empty server."; return false; }
        var checks = new List<string>();
        var hubs = new List<ReferenceHub>();
        Player? source = null;
        SpeakerToy? speaker = null;
        void Check(string name, bool value) => checks.Add(name + "=" + (value ? "PASS" : "FAIL"));
        Player Make(string name, RoleTypeId role)
        {
            var hub = DummyUtils.SpawnDummy(name);
            hubs.Add(hub);
            hub.authManager.UserId = name + "@dummy";
            var player = Player.Get(hub)!;
            player.SetRole(role);
            return player;
        }
        void Receive(Player player, ServerSpecificSettingBase setting)
            => typeof(ServerSpecificSettingsSync).GetMethod("ServerProcessClientResponseMsg", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [player.Connection, new SSSClientResponse(setting)]);
        void Volume(Player player, int id, float value)
            => Receive(player, new SSSliderSetting(id, "volume", 0, 200, 100, true) { SyncFloatValue = value });
        object? VoiceTip(Player player)
        {
            var tag = typeof(SecretApiProximityChatPlugin).GetField("VoiceTipTag", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            var entries = typeof(RueDisplay).GetField("elements", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(RueDisplay.Get(player))!;
            try { return entries.GetType().GetProperty("Item")!.GetValue(entries, [tag]); }
            catch (TargetInvocationException error) when (error.InnerException is KeyNotFoundException) { return null; }
        }
        try
        {
            source = Make("SCPVolumeSource", RoleTypeId.Scp173);
            var listener = Make("SCPVolumeListener", RoleTypeId.ClassD);
            var other = Make("SCPVolumeOther", RoleTypeId.ClassD);
            Check("scp_proximity_enabled_on_spawn", source.IsProximityChatEnabled() && !listener.IsProximityChatEnabled());
            var tip = VoiceTip(source)!;
            var element = (DynamicElement)tip.GetType().GetField("Element")!.GetValue(tip)!;
            var expiry = tip.GetType().GetField("ExpiryNode")!.GetValue(tip)!;
            var expiryValue = expiry.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(expiry)!;
            float expiryTime = (float)expiryValue.GetType().GetField("ExpiresAt")!.GetValue(expiryValue)!;
            Check("scp_bind_tip_private_for_thirty_seconds", VoiceTip(listener) == null &&
                element.VerticalPosition == 58 && !element.ShowToSpectators &&
                Mathf.Abs(expiryTime - Time.timeSinceLevelLoad - 30f) < 1 &&
                element.Parameters?.Single() is KeybindParameter bind && bind.Id == ToggleSettingId);
            Vector3 cameraOffsetAtSpawn = listener.ReferenceHub.PlayerCameraReference.position - listener.Position;
            listener.Position = source.ReferenceHub.PlayerCameraReference.position + Vector3.right * 0.5f - cameraOffsetAtSpawn;
            var role = (IVoiceRole)PlayerRoleLoader.AllRoles[RoleTypeId.ClassD];
            var human = ((HumanVoiceModule)role.VoiceModule).FirstProxPlayback.GetComponent<AudioSource>();
            var settings = ProximityChatPlugin.Cfg.AudioSettings;
            Check("configured_audible_range", settings.MinDistance > 0 && settings.MaxDistance >= settings.MinDistance &&
                (!ProximityChatPlugin.Cfg.MatchHumanVoiceDistances ||
                 Mathf.Approximately(settings.MinDistance, human.maxDistance) && Mathf.Approximately(settings.MaxDistance, human.maxDistance)));
            Check("configured_base_gain", !float.IsNaN(settings.Volume) && !float.IsInfinity(settings.Volume) && settings.Volume > 0);
            Check("slider_range_and_default", SettingsRegistry.Master.MinimumValue == 0 && SettingsRegistry.Master.MaximumValue == 200 && SettingsRegistry.Master.DefaultValue == 100);
            Check("saved_master_setting_id_preserved", SettingsRegistry.Master.Id == MasterVolume.Text.GetStableHashCode());
            Check("ready_source_fixture", source.IsReady);
            Check("unconfigured_listener_uses_normal_gain", Mathf.Approximately(VolumeHelpers.Master(other), settings.Volume));
            Receive(listener, SettingsRegistry.PersonalizationVisibility.Base);
            PersonalizationManager.Register(source);
            Volume(listener, SettingsRegistry.Master.Id, 100);
            Check("native_master_100", Mathf.Approximately(VolumeHelpers.Master(listener), settings.Volume));
            Volume(listener, SettingsRegistry.Master.Id, 150);
            Check("native_master_boost", Mathf.Approximately(VolumeHelpers.Master(listener), settings.Volume * 1.5f));
            Volume(listener, PersonalizationManager.IdFor(source.UserId), 50);
            Check("native_personal_volume_multiplies_master_once", Mathf.Approximately(VolumeHelpers.Personalized(listener, source.UserId), settings.Volume * 0.75f));
            Check("other_listener_unchanged", Mathf.Approximately(VolumeHelpers.Personalized(other, source.UserId), settings.Volume));
            Check("scp_default_speaker_is_not_duplicated", !source.EnableProximityChat());
            // Native ReadyList excludes dummies. Exercise the same refresh a
            // real listener receives after joining/updating their settings.
            PersonalizationManager.ConfigureAll(listener);
            var active = (Dictionary<Player, SpeakerToy>)typeof(ProximityChatState).GetProperty("ActiveSpeakers", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            speaker = active[source];
            Check("native_speaker_uses_configured_range", Mathf.Approximately(speaker.MinDistance, settings.MinDistance) && Mathf.Approximately(speaker.MaxDistance, settings.MaxDistance));
            var personal = speaker.GameObject.GetComponent<SpeakerPersonalization>();
            Check("one_moving_spatial_source_at_head", speaker.IsSpatial && !speaker.IsStatic && speaker.Parent == source.ReferenceHub.transform && Vector3.Distance(speaker.Transform.position, source.ReferenceHub.PlayerCameraReference.position) < 0.01f);
            Check("spawn_position_synced_in_parent_space", Vector3.Distance(speaker.Base.NetworkPosition, speaker.Transform.localPosition) < 0.01f);
            var synchronize = typeof(ProximityChatState).GetMethod("SynchronizePositions", BindingFlags.NonPublic | BindingFlags.Static)!;
            var parentId = typeof(AdminToys.AdminToyBase).GetMethod("ServerParentId", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Check("camera_parent_is_not_network_addressable", (uint)parentId.Invoke(speaker.Base, [source.ReferenceHub.PlayerCameraReference])! == 0);
            // Exercise the native spawn serializer. Its final uint is the
            // client parent ID; a server-only transform comparison missed this.
            using (var writer = NetworkWriterPool.Get())
            {
                speaker.Base.OnSerialize(writer, true);
                byte[] bytes = writer.ToArray();
                Check("spawn_wire_contains_player_parent", source.NetworkId != 0 && BitConverter.ToUInt32(bytes, bytes.Length - 4) == source.NetworkId);
            }
            var originalPosition = source.Position;
            foreach (var position in new[] { new Vector3(90, 1000, -45), new Vector3(-80, -990, 120) })
            {
                source.Position = position;
                synchronize.Invoke(null, null);
                Vector3 clientPosition = source.ReferenceHub.transform.TransformPoint(speaker.Base.NetworkPosition);
                Check("client_reconstructed_head_at_" + position.y, Vector3.Distance(clientPosition, source.ReferenceHub.PlayerCameraReference.position) < 0.01f);
                Check("client_audio_not_culled_at_" + position.y, Vector3.Distance(clientPosition, source.ReferenceHub.PlayerCameraReference.position + Vector3.right * 2) < speaker.MaxDistance);
            }
            source.Position = originalPosition;
            synchronize.Invoke(null, null);
            // Run the real LabAPI voice event and the client's audio decoder.
            // This complements spatial checks; it still needs listening on a client.
            var samples = new float[480];
            for (int i = 0; i < samples.Length; i++) samples[i] = 0.2f * Mathf.Sin(i * 2 * Mathf.PI * 440 / 48000);
            using (var encoder = new VoiceChat.Codec.OpusEncoder(VoiceChat.Codec.Enums.OpusApplicationType.Voip))
            {
                var encoded = new byte[512];
                int count = encoder.Encode(samples, encoded);
                var message = new VoiceChat.Networking.VoiceMessage(source.ReferenceHub, VoiceChat.VoiceChatChannel.ScpChat, encoded, count, false);
                var sending = new LabApi.Events.Arguments.PlayerEvents.PlayerSendingVoiceMessageEventArgs(ref message);
                LabApi.Events.Handlers.PlayerEvents.OnSendingVoiceMessage(sending);
                Check("scp_packet_intercepted_for_local_route", !sending.IsAllowed && source.VoiceModule!.CurrentChannel == VoiceChat.VoiceChatChannel.Proximity);
                Check("opus_payload_preserved", sending.Message.Data == encoded && sending.Message.DataLength == count);
                var playback = speaker.Base.Playback;
                int before = playback.Buffer.Length;
                playback.DecodeSamples(new VoiceChat.Networking.AudioMessage(speaker.ControllerId, sending.Message.Data, sending.Message.DataLength));
                Check("client_audio_decoder_accepts_routed_packet", playback.Buffer.Length > before);
            }
            float attenuation = HumanVoiceProfile.GainFor(speaker, listener);
            Check("near_field_probe_has_nonzero_audio", attenuation > 0.9f);
            Check("receiver_override_reaches_speaker", personal != null && personal[listener].HasValue && Mathf.Approximately(personal[listener]!.Value.Volume, settings.Volume * 0.75f * attenuation));
            Volume(listener, PersonalizationManager.IdFor(source.UserId), 200);
            Check("native_personal_boost_200", personal != null && Mathf.Approximately(personal[listener]!.Value.Volume, settings.Volume * 3 * attenuation));
            Volume(listener, SettingsRegistry.Master.Id, 0);
            Check("native_master_zero_mutes", personal != null && personal[listener]!.Value.Volume == 0);
            Volume(listener, SettingsRegistry.Master.Id, 100);
            Volume(listener, PersonalizationManager.IdFor(source.UserId), 0);
            Check("native_personal_zero_mutes", personal != null && personal[listener]!.Value.Volume == 0);
            Volume(listener, PersonalizationManager.IdFor(source.UserId), 100);
            var curve = human.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            foreach (float distance in new[] { 2f, 5f, 10f, 18f, 23f })
            {
                Vector3 cameraOffset = listener.ReferenceHub.PlayerCameraReference.position - listener.Position;
                listener.Position = speaker.Transform.position + Vector3.right * distance - cameraOffset;
                PersonalizationManager.ConfigureAll(listener);
                float actualDistance = Vector3.Distance(speaker.Transform.position, listener.ReferenceHub.PlayerCameraReference.position);
                // Human matching applies attenuation through the listener's
                // volume override. Custom ranges leave it to the native toy.
                float expected = ProximityChatPlugin.Cfg.MatchHumanVoiceDistances
                    ? (actualDistance >= human.maxDistance ? 0 : settings.Volume * Mathf.Clamp01(curve.Evaluate(actualDistance / human.maxDistance)))
                    : settings.Volume;
                Check("native_listener_gain_at_" + distance + "m", Mathf.Abs(actualDistance - distance) < 0.01f && personal != null && Mathf.Abs(personal[listener]!.Value.Volume - expected) < 0.001f);
            }
            source.Position += Vector3.right * 4;
            Check("speaker_tracks_moving_head", Vector3.Distance(speaker.Transform.position, source.ReferenceHub.PlayerCameraReference.position) < 0.01f);
            source.DisableProximityChat();
            Check("disable_returns_source", !source.IsProximityChatEnabled());
            PersonalizationManager.Configure(listener, source.UserId, 2);
            Check("pooled_source_ignores_stale_volume_updates", personal != null && !personal[listener].HasValue);
            checks.Add($"profile: human min={human.minDistance}; max={human.maxDistance}; rolloff={human.rolloffMode}; SCP gain={settings.Volume}; min={settings.MinDistance}; max={settings.MaxDistance}; rolloff={speaker.Base.Playback.Source.rolloffMode}");
            checks.Add("human rolloff keys: " + string.Join("; ", human.GetCustomCurve(AudioSourceCurveType.CustomRolloff).keys.Select(k => $"{k.time}:{k.value}:{k.inTangent}:{k.outTangent}")));
            source.SetRole(RoleTypeId.ClassD);
            Check("scp_bind_tip_removed_on_role_change", VoiceTip(source) == null);
            var input = new SSTwoButtonsSetting(SettingsRegistry.Input.Id, "input", "toggle", "hold");
            typeof(SSTwoButtonsSetting).GetProperty(nameof(SSTwoButtonsSetting.SyncIsB))!.SetValue(input, true);
            Receive(source, input);
            source.SetRole(RoleTypeId.Scp173);
            Check("scp_saved_hold_mode_starts_in_team_chat", InputMethod.UsesHold(source) && !source.IsProximityChatEnabled());
        }
        catch (Exception error) { checks.Add("FAIL: " + error); }
        finally
        {
            if (source != null) { source.DisableProximityChat(); PersonalizationManager.Unregister(source); }
            foreach (var hub in hubs) if (hub != null) NetworkServer.Destroy(hub.gameObject);
        }
        response = string.Join("\n", checks);
        return !response.Contains("FAIL");
    }
}
