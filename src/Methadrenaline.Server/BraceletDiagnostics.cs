using System;
using System.Collections.Generic;
using System.Linq;
using CustomPlayerEffects;
using Footprinting;
using HarmonyLib;
using InventorySystem;
using InventorySystem.Disarming;
using InventorySystem.Items.ThrowableProjectiles;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using MEC;
using Methadrenaline.Server.Core;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.Ragdolls;
using PlayerRoles.Spectating;
using PlayerStatsSystem;
using SwiftArcadeMode.Features;
using SwiftArcadeMode.Features.Humans.Perks.Content;
using UnityEngine;
using ThrowableItem = InventorySystem.Items.ThrowableProjectiles.ThrowableItem;

namespace Methadrenaline.Server;

internal static class BraceletDiagnostics
{
    private static bool running;
    internal static string Run(BraceletController controller)
    {
        if (running || Round.IsRoundInProgress || Player.List.Any(p => !p.IsHost && !p.IsNpc && !p.IsDummy)) return "Bracelet diagnostics require an empty waiting server.";
        running = true;
        var probes = new Dictionary<string, ReferenceHub>();
        try
        {
            foreach (var pair in new[] { ("BraceletCaptor", RoleTypeId.NtfPrivate), ("BraceletPrisoner", RoleTypeId.ClassD), ("BraceletWitness", RoleTypeId.ClassD) })
            {
                ReferenceHub hub = DummyUtils.SpawnDummy(pair.Item1);
                probes.Add(pair.Item1, hub);
                hub.authManager.UserId = "ID_Dummy";
                Player.Get(hub)!.SetRole(pair.Item2);
            }
            // Unity Start initializes the character manager and human hitboxes
            // after the command returns. The native SCP-1509 damage modifier also
            // protects every hub for the first three seconds of a new scene.
            Timing.CallDelayed(4f, () =>
            {
                try
                {
                    if (ServerPlugin.Instance?.Bracelets != controller || Round.IsRoundInProgress || Player.List.Any(p => !p.IsHost && !p.IsNpc && !p.IsDummy)) return;
                    LabApi.Features.Console.Logger.Info("[METHADRENALINE] Bracelet diagnostics:\n" + RunReady(controller, probes));
                }
                finally
                {
                    foreach (var hub in probes.Values) if (hub != null) NetworkServer.Destroy(hub.gameObject);
                    running = false;
                }
            });
            return "Bracelet diagnostics started; results will appear in the server console.";
        }
        catch
        {
            foreach (var hub in probes.Values) if (hub != null) NetworkServer.Destroy(hub.gameObject);
            running = false;
            throw;
        }
    }

    private static string RunReady(BraceletController controller, Dictionary<string, ReferenceHub> probes)
    {
        var checks = new List<string>();
        var hubs = new List<ReferenceHub>();
        var arcade = ServerPlugin.Instance!.PerkRounds!;
        bool wasActive = arcade.Active;
        void Check(string name, bool success) => checks.Add(name + "=" + (success ? "PASS" : "FAIL"));
        Player Make(string name, RoleTypeId role)
        {
            ReferenceHub hub = probes[name];
            hubs.Add(hub);
            hub.authManager.UserId = "ID_Dummy";
            var player = Player.Get(hub)!;
            if (player.Role != role) player.SetRole(role);
            player.DisableEffect<SpawnProtected>();
            return player;
        }
        void Cuff(Player captor, Player prisoner)
        {
            prisoner.DisarmedBy = captor;
            PlayerEvents.OnCuffed(new PlayerCuffedEventArgs(captor.ReferenceHub, prisoner.ReferenceHub));
        }
        void Free(Player captor, Player prisoner)
        {
            prisoner.IsDisarmed = false;
            PlayerEvents.OnUncuffed(new PlayerUncuffedEventArgs(captor.ReferenceHub, prisoner.ReferenceHub, true));
        }
        void FinishBurst(AudioPlayer player)
        {
            foreach (var clip in player.ClipsById.Values) clip.ReadPosition = clip.Samples.Length;
            // Exercise the library's real end-of-stream cleanup, not a fake
            // elapsed time or a copy of our queue implementation.
            AccessTools.Method(typeof(AudioPlayer), "SendAudioData").Invoke(player, null);
        }
        try
        {
            controller.Reset();
            controller.Diagnostic = true;
            Player captor = Make("BraceletCaptor", RoleTypeId.NtfPrivate);
            Vector3 origin = captor.Position + Vector3.up * 40;
            Player prisoner = Make("BraceletPrisoner", RoleTypeId.ClassD);
            Player witness = Make("BraceletWitness", RoleTypeId.ClassD);
            captor.Position = origin;
            prisoner.Position = origin + Vector3.forward * 2;
            witness.Position = origin + Vector3.right * 50;
            captor.CurrentItem = captor.AddItem(ItemType.GunCOM15);
            prisoner.CurrentItem = null;
            Physics.SyncTransforms();
            var process = AccessTools.Method(typeof(DisarmingHandlers), "ServerProcessDisarmMessage");
            Check("native_cuff_prerequisites", captor.ReferenceHub.CanStartDisarming(prisoner.ReferenceHub) && (captor.ReferenceHub.transform.position - prisoner.ReferenceHub.transform.position).sqrMagnitude < 20);
            process.Invoke(null, new object[] { captor.Connection, new DisarmMessage(prisoner.ReferenceHub, true, false) });
            Check("native_cuff_arms", controller.Ledger.Active.Count == 1 && prisoner.IsDisarmed);
            int activationSignals = controller.Audio.SignalsPlayed;
            Cuff(captor, prisoner);
            Check("duplicate_cuff_does_not_replay_activation", controller.Audio.SignalsPlayed == activationSignals);
            var activation = controller.Audio.Playback(BraceletController.Life(prisoner));
            Check("activation_one_second_spatial_cue", activation != null && activation.SendSoundGlobally && activation.Owners.Count == 0 && activation.SpeakersByName.Count == 1 && activation.ClipsById.Count == 1 && activation.ClipsById.Values.Single().Samples.Length >= BraceletSignal.SampleRate);
            Check("hud_middle_dot", controller.Hud(prisoner).Contains(" · ") && !controller.Hud(prisoner).Contains(" — "));
            Check("both_huds", controller.Hud(prisoner).Contains(captor.Nickname) && controller.Hud(captor).Contains(prisoner.Nickname));
            Free(captor, prisoner);
            Check("uncuff_releases_without_explosion", controller.Ledger.Active.Count == 0 && controller.Ledger.Notices.Count == 0);

            foreach (RoleTypeId role in new[] { RoleTypeId.FacilityGuard, RoleTypeId.NtfPrivate, RoleTypeId.NtfSpecialist, RoleTypeId.NtfSergeant, RoleTypeId.NtfCaptain })
            {
                captor.SetRole(role);
                Cuff(captor, prisoner);
                Check("eligible_captor_" + role, controller.Ledger.Active.Count == 1);
                Free(captor, prisoner);
            }
            foreach (RoleTypeId role in new[] { RoleTypeId.Scientist, RoleTypeId.ClassD, RoleTypeId.ChaosRifleman })
            {
                captor.SetRole(role);
                Cuff(captor, prisoner);
                Check("eligible_captor_" + role, controller.Ledger.Active.Count == 1);
                Free(captor, prisoner);
            }
            captor.SetRole(RoleTypeId.FacilityGuard);
            foreach (RoleTypeId role in new[] { RoleTypeId.ClassD, RoleTypeId.Scientist, RoleTypeId.ChaosRifleman, RoleTypeId.NtfPrivate })
            {
                prisoner.SetRole(role);
                Cuff(captor, prisoner);
                Check("human_prisoner_" + role, controller.Ledger.Active.Count == 1);
                Free(captor, prisoner);
            }
            prisoner.SetRole(RoleTypeId.ClassD);
            Cuff(captor, prisoner);
            Cuff(captor, witness);
            int blasts = controller.ExplosionCount;
            captor.Kill();
            controller.CheckNow();
            Check("captor_death_releases_all", controller.Ledger.Active.Count == 0 && prisoner.IsAlive && witness.IsAlive && controller.ExplosionCount == blasts);

            captor.SetRole(RoleTypeId.FacilityGuard);
            Cuff(captor, prisoner);
            prisoner.IsDisarmed = false; // Native/admin removal without Uncuffed.
            controller.CheckNow();
            Check("silent_uncuff_releases", controller.Ledger.Active.Count == 0 && prisoner.IsAlive && controller.ExplosionCount == blasts);
            Cuff(captor, prisoner);
            PlayerEvents.OnLeft(new PlayerLeftEventArgs(captor.ReferenceHub));
            Check("captor_disconnect_releases", controller.Ledger.Active.Count == 0 && prisoner.IsAlive);
            Cuff(captor, prisoner);
            captor.SetRole(RoleTypeId.Scientist);
            Check("captor_new_life_releases", controller.Ledger.Active.Count == 0 && prisoner.IsAlive);

            captor.SetRole(RoleTypeId.FacilityGuard);
            captor.Position = origin + Vector3.right * 50;
            prisoner.Position = origin;
            Cuff(captor, prisoner);
            Vector3 deathPosition = prisoner.Position;
            prisoner.Kill();
            controller.DrainExplosions();
            Check("external_death_explodes_once", controller.ExplosionCount == blasts + 1);
            Check("corpse_position_not_spectator_spawn", Vector3.Distance(controller.LastExplosionPosition, deathPosition + Vector3.up * 0.5f) < 0.02f);
            string deathHud = controller.Hud(captor);
            Check("death_hud_has_no_distance", deathHud.Contains("eliminated") && !deathHud.Contains(" m"));
            controller.CheckNow();
            Check("death_does_not_repeat", controller.ExplosionCount == blasts + 1);
            controller.Ledger.Prune(Time.unscaledTime + 10);
            Check("death_hud_expires", controller.Hud(captor) == "");

            prisoner.SetRole(RoleTypeId.ClassD);
            captor.Position = origin;
            prisoner.Position = origin + Vector3.right * 2;
            Cuff(captor, prisoner);
            int signals = controller.Audio.SignalsPlayed;
            var life = BraceletController.Life(prisoner);
            var playback = controller.Audio.Playback(life);
            if (playback == null) throw new InvalidOperationException("Activation audio was not created.");
            controller.Audio.Play(prisoner, 1);
            controller.Audio.Play(prisoner, 2);
            controller.Audio.Play(prisoner, 3);
            Check("confirmation_finishes_before_pending_warnings", controller.Audio.SignalsPlayed == signals && playback.ClipsById.Values.Single().Samples.Length >= BraceletSignal.SampleRate);
            FinishBurst(playback);
            controller.Audio.Flush(life, 18.1);
            Check("queue_keeps_only_highest_crossed_warning", controller.Audio.SignalsPlayed == signals + 1 && playback.ClipsById.Values.Single().Samples.Length == BraceletSignal.CreateWarning(3).Length);
            controller.Audio.Play(prisoner, 4);
            controller.Audio.CancelPendingWarning(life);
            var runningBurst = playback.ClipsById.Values.Single();
            Check("return_preserves_current_burst", ReferenceEquals(runningBurst, playback.ClipsById.Values.Single()) && playback.SendSoundGlobally);
            FinishBurst(playback);
            controller.Audio.Flush(life, 17);
            Check("return_cancels_only_pending_warning", playback.ClipsById.Count == 0 && controller.Audio.SignalsPlayed == signals + 1);
            Check("idle_speaker_survives_client_audio_tail", !playback.DestroyWhenAllClipsPlayed && controller.Audio.Playback(life) != null);
            signals = controller.Audio.SignalsPlayed;
            for (int level = 1; level <= 5; level++)
            {
                FinishBurst(playback);
                prisoner.Position = origin + Vector3.right * (float)(BraceletPolicy.WarningDistance(level) + 0.1);
                controller.CheckNow();
                Check("warning_at_" + BraceletPolicy.WarningDistance(level) + "_meters", controller.Audio.SignalsPlayed == signals + level);
                Check("one_controller_reused_level_" + level, ReferenceEquals(playback, controller.Audio.Playback(life)));
            }
            if (playback == null) throw new InvalidOperationException("Warning audio was not created.");
            var speaker = playback.SpeakersByName["bracelet"];
            Check("warning_one_spatial_path_for_everyone", playback.SendSoundGlobally && playback.Condition == null && playback.Owners.Count == 0 && playback.SpeakersByName.Count == 1 && speaker.IsSpatial && speaker.MaxDistance == BraceletAudio.AudibleRadius && playback.ClipsById.Count == 1);
            prisoner.Position = origin + Vector3.right * 24;
            controller.Audio.UpdatePositions();
            Check("speaker_follows_wearer_and_syncs_position", Vector3.Distance(speaker.Position, BraceletAudio.Position(prisoner)) < 0.01f && Vector3.Distance(speaker.Base.NetworkPosition, speaker.Position) < 0.01f && !speaker.Base.IsStatic);
            var nativePlayback = speaker.Base.Playback;
            var playbackType = typeof(VoiceChat.Playbacks.SpeakerToyPlaybackBase);
            var validatePlayback = AccessTools.Method(playbackType, "ValidatePlayback");
            bool AudibleAt(Vector3 listener)
            {
                nativePlayback.Source.maxDistance = speaker.MaxDistance;
                AccessTools.Field(playbackType, "<LastPosition>k__BackingField").SetValue(nativePlayback, speaker.Position);
                AccessTools.Field(playbackType, "<Culled>k__BackingField").SetValue(nativePlayback, true);
                validatePlayback.Invoke(null, new object[] { listener, nativePlayback });
                return !nativePlayback.Culled;
            }
            Check("wearer_passes_native_client_culling", AudibleAt(prisoner.ReferenceHub.PlayerCameraReference.position));
            Check("nearby_listener_passes_native_client_culling", AudibleAt(speaker.Position + Vector3.right * 10));
            Check("distant_listener_is_culled", !AudibleAt(speaker.Position + Vector3.right * 100));
            prisoner.Position = origin + Vector3.right * 21.4f;
            controller.CheckNow();
            Check("returning_does_not_cut_warning", ReferenceEquals(playback, controller.Audio.Playback(life)) && playback.SendSoundGlobally && playback.ClipsById.Count == 1 && speaker.Volume > 0);
            prisoner.Position = origin + Vector3.right * 22.1f;
            controller.CheckNow();
            Check("boundary_cooldown_is_silent", controller.Audio.SignalsPlayed == signals + 5);
            FinishBurst(playback);
            controller.AdvanceDiagnosticTime(1.1f);
            prisoner.Position = origin + Vector3.right * 21.4f;
            controller.CheckNow();
            prisoner.Position = origin + Vector3.right * 22.1f;
            controller.CheckNow();
            Check("retreat_and_outward_recrossing_warn_again", controller.Audio.SignalsPlayed == signals + 6);
            controller.Audio.Play(prisoner, 4);
            controller.Audio.PlaySequence(prisoner);
            controller.Audio.Play(prisoner, 5);
            Check("irreversible_alarm_preempts_burst_and_discards_queue", playback.ClipsById.Count == 1 && playback.ClipsById.Values.Single().Samples.Length == BraceletSignal.CreateSequence().Length);
            Free(captor, prisoner);
            controller.Reset();

            prisoner.Position = origin + Vector3.right * 23;
            Cuff(captor, prisoner);
            int beforeSequence = controller.ExplosionCount;
            controller.CheckNow();
            Check("sequence_starts_without_instant_death", prisoner.IsAlive && controller.IsCommitted(prisoner));
            Check("filling_text_first_three_seconds", controller.Hud(prisoner).Contains("Filling the chamber with explosives"));
            var alarmLife = BraceletController.Life(prisoner);
            var alarm = controller.Audio.Playback(alarmLife);
            Check("sequence_alarm_single_spatial_and_finite", alarm != null && alarm.SendSoundGlobally && alarm.Owners.Count == 0 && alarm.SpeakersByName.Count == 1 && alarm.SpeakersByName["bracelet"].IsSpatial && alarm.ClipsById.Count == 1 && !alarm.ClipsById.Values.Single().Loop && alarm.ClipsById.Values.Single().Samples.Length == BraceletSignal.SampleRate * BraceletPolicy.SequenceSeconds);
            prisoner.SetRole(RoleTypeId.Scientist);
            Check("sequence_blocks_role_escape", prisoner.Role == RoleTypeId.ClassD && controller.IsCommitted(prisoner));
            Free(captor, prisoner);
            Check("uncuff_cannot_cancel_sequence", controller.IsCommitted(prisoner));
            captor.Kill();
            prisoner.Position = origin;
            controller.CheckNow();
            Check("captor_death_and_return_cannot_cancel_sequence", prisoner.IsAlive && controller.IsCommitted(prisoner));
            Check("alarm_survives_uncuff_and_captor_death", alarm != null && ReferenceEquals(alarm, controller.Audio.Playback(BraceletController.Life(prisoner))));
            // Sample just after the boundary: the native float clock can round
            // an exact 3-second step down. Core tests cover exact phase edges.
            controller.AdvanceDiagnosticTime(3.01f);
            Check("detonation_text_from_fourth_second", prisoner.IsAlive && controller.Hud(prisoner).Contains("Detonation"));
            Check("detonation_alarm_continues_without_restarting", alarm != null && ReferenceEquals(alarm, controller.Audio.Playback(BraceletController.Life(prisoner))));
            controller.AdvanceDiagnosticTime(4);
            Check("sequence_detonates_after_seven_seconds", !prisoner.IsAlive && controller.ExplosionCount == beforeSequence + 1);
            Check("alarm_stops_on_explosion", controller.Audio.Playback(alarmLife) == null);
            controller.CheckNow();
            Check("sequence_explosion_is_one_shot", controller.ExplosionCount == beforeSequence + 1);
            controller.Reset();

            captor.SetRole(RoleTypeId.FacilityGuard);
            prisoner.SetRole(RoleTypeId.ClassD);
            captor.Position = origin;
            prisoner.Position = origin + Vector3.right * 22.85f;
            Cuff(captor, prisoner);
            controller.CheckNow();
            Check("inside_limit_survives", prisoner.IsAlive && controller.Ledger.Active.Count == 1);
            Free(captor, prisoner);
            controller.Reset();

            // Compare collateral damage to the game's own HE explosion at the
            // same location, with the same attacker and unmodified prefab.
            if (!InventoryItemLoader.TryGetItem(ItemType.GrenadeHE, out ThrowableItem grenadeItem) || grenadeItem.Projectile is not ExplosionGrenade grenade)
                throw new InvalidOperationException("Native HE prefab unavailable.");
            Vector3 blastPosition = origin + Vector3.up * 0.5f;
            captor.DisableEffect<SpawnProtected>();
            captor.Position = origin + Vector3.right * 50;
            prisoner.Position = origin + Vector3.forward * 50;
            witness.Position = origin + Vector3.right * 3;
            witness.MaxHealth = 5000;
            witness.Health = 5000;
            witness.ArtificialHealth = 0;
            witness.HumeShield = 0;
            witness.IsGodModeEnabled = false;
            witness.DisableEffect<SpawnProtected>();
            // Network dummies do not submit the next movement packet that
            // normally updates the server's third-person hitbox transform.
            var model = ((IFpcRole)witness.RoleBase).FpcModule.CharacterModelInstance;
            model.transform.position = witness.Position;
            foreach (var hitbox in model.Hitboxes) hitbox.SetColliders(true);
            Physics.SyncTransforms();
            ExplosionGrenade.Explode(new Footprint(captor.ReferenceHub), blastPosition, grenade, ExplosionType.Grenade);
            float nativeDamage = 5000 - witness.Health;
            Check("native_he_hits_probe", nativeDamage > 0);
            // The first HE applies Burned, which increases subsequent damage.
            // Restore the same unburned target conditions for the comparison.
            witness.DisableEffect<Burned>();
            witness.DisableEffect<Deafened>();
            witness.DisableEffect<Concussed>();
            witness.Health = 5000;
            prisoner.Position = origin;
            prisoner.DisableEffect<SpawnProtected>();
            prisoner.IsGodModeEnabled = false;
            Cuff(captor, prisoner);
            Physics.SyncTransforms();
            int before = controller.ExplosionCount;
            controller.CheckNow();
            Check("range_has_seven_second_fuse", prisoner.IsAlive && controller.IsCommitted(prisoner));
            DamageHandlerBase? collateralHandler = null;
            void ObserveCollateral(PlayerHurtingEventArgs ev) { if (ev.Player == witness) collateralHandler = ev.DamageHandler; }
            PlayerEvents.Hurting += ObserveCollateral;
            try { controller.AdvanceDiagnosticTime(7.01f); }
            finally { PlayerEvents.Hurting -= ObserveCollateral; }
            Check("range_kills_and_explodes", !prisoner.IsAlive && controller.ExplosionCount == before + 1);
            Check("actual_blast_marks_collateral_without_replacing_he", collateralHandler is ExplosionDamageHandler && BraceletDeathPresentation.IsBracelet(collateralHandler));
            float braceletDamage = 5000 - witness.Health;
            Check("collateral_matches_native_he", nativeDamage > 0 && Mathf.Abs(braceletDamage - nativeDamage) < 0.05f);
            checks.Add($"HE comparison: native={nativeDamage:0.##}; bracelet={braceletDamage:0.##}");

            controller.Reset();
            prisoner.SetRole(RoleTypeId.ClassD);
            prisoner.Position = origin;
            captor.Position = origin + Vector3.right * 20;
            witness.Position = origin + Vector3.right;
            witness.DisableEffect<Burned>();
            witness.Health = 100;
            model.transform.position = witness.Position;
            Physics.SyncTransforms();
            Cuff(captor, prisoner);
            Cuff(captor, witness);
            before = controller.ExplosionCount;
            prisoner.Kill();
            controller.DrainExplosions();
            Check("collateral_death_chains_exactly_once", !witness.IsAlive && controller.ExplosionCount == before + 2 && controller.Ledger.Active.Count == 0);

            prisoner.SetRole(RoleTypeId.ClassD);
            prisoner.Position = origin;
            captor.Position = origin + Vector3.right * 50;
            Cuff(captor, prisoner);
            prisoner.IsGodModeEnabled = true;
            prisoner.EnableEffect<SpawnProtected>(1, 30);
            arcade.Resume();
            Check("saving_perk_fixture", prisoner.GivePerk(typeof(Phoenix)));
            controller.CheckNow();
            controller.AdvanceDiagnosticTime(7.01f);
            Check("range_bypasses_protection_and_phoenix", !prisoner.IsAlive);

            prisoner.SetRole(RoleTypeId.ClassD);
            captor.Position = origin;
            prisoner.Position = origin + Vector3.right * 120;
            Cuff(captor, prisoner);
            var validate = AccessTools.Method(typeof(DisarmedPlayers), "ValidateEntry");
            var entry = DisarmedPlayers.Entries.Single(e => e.DisarmedPlayer == prisoner.NetworkId);
            Check("long_teleport_cannot_auto_release", (bool)validate.Invoke(null, new object[] { entry })!);
            controller.CheckNow();
            controller.AdvanceDiagnosticTime(7.01f);
            Check("long_teleport_detonates", !prisoner.IsAlive);

            prisoner.SetRole(RoleTypeId.ClassD);
            prisoner.IsGodModeEnabled = false;
            Cuff(captor, prisoner);
            prisoner.SetRole(RoleTypeId.Scientist);
            Check("prisoner_role_change_releases", controller.Ledger.Active.Count == 0);
            controller.Reset();
            Check("reset_cleans_state", controller.Ledger.Active.Count == 0 && controller.Ledger.Notices.Count == 0 && controller.Hud(captor) == "");
            foreach (var language in new[] { ServerLanguage.English, ServerLanguage.Ukrainian, ServerLanguage.Russian })
            {
                var localization = ServerPlugin.Instance!.Localization!;
                var languageMap = (Dictionary<ReferenceHub, ServerLanguage>)AccessTools.Field(typeof(LocalizationService), "languages").GetValue(localization);
                languageMap[prisoner.ReferenceHub] = language;
                try
                {
                    prisoner.SetRole(RoleTypeId.ClassD);
                    var handler = BraceletDeathPresentation.WearerDeath();
                    var info = new RagdollData(prisoner.ReferenceHub, handler, prisoner.Role, new RelativePositioning.RelativePosition(prisoner.Position), Quaternion.identity, Vector3.one, prisoner.Nickname, 0, 1);
                    using var writer = NetworkWriterPool.Get();
                    var previousViewer = BraceletDeathPresentation.Viewer;
                    try { BraceletDeathPresentation.Viewer = prisoner.ReferenceHub; writer.WriteRagdollData(info); }
                    finally { BraceletDeathPresentation.Viewer = previousViewer; }
                    var decoded = new NetworkReader(writer.ToArraySegment()).ReadRagdollData();
                    Check("native_corpse_localization_" + language, decoded.Handler is CustomReasonDamageHandler && decoded.Handler.RagdollInspectText == localization.Text(prisoner.ReferenceHub, BraceletDeathPresentation.WearerBodyText) && ReferenceEquals(info.Handler, handler));
                    prisoner.ReferenceHub.playerStats.DealDamage(handler);
                    var spectator = (SpectatorRole)prisoner.RoleBase;
                    spectator.ServerSetData(handler);
                    using var death = NetworkWriterPool.Get();
                    using var expected = NetworkWriterPool.Get();
                    spectator.WritePrivateSpawnData(death);
                    new CustomReasonDamageHandler(localization.Text(prisoner.ReferenceHub, BraceletDeathPresentation.DeathText)).WriteDeathScreen(expected);
                    Check("native_death_screen_localization_" + language, death.ToArraySegment().SequenceEqual(expected.ToArraySegment()));
                    var nearby = new ExplosionDamageHandler(new Footprint(captor.ReferenceHub), Vector3.zero, 100, 100, ExplosionType.Grenade);
                    BraceletDeathPresentation.Exploding = true;
                    try { BraceletDeathPresentation.MarkExplosion(nearby); }
                    finally { BraceletDeathPresentation.Exploding = false; }
                    spectator.ServerSetData(nearby);
                    using var mysterious = NetworkWriterPool.Get();
                    using var expectedMysterious = NetworkWriterPool.Get();
                    spectator.WritePrivateSpawnData(mysterious);
                    new CustomReasonDamageHandler(localization.Text(prisoner.ReferenceHub, BraceletDeathPresentation.UnknownDeathText)).WriteDeathScreen(expectedMysterious);
                    Check("collateral_unknown_death_localization_" + language, mysterious.ToArraySegment().SequenceEqual(expectedMysterious.ToArraySegment()) && BraceletDeathPresentation.BodyHandler(nearby, prisoner.ReferenceHub).RagdollInspectText == localization.Text(prisoner.ReferenceHub, BraceletDeathPresentation.UnknownDeathText));
                    // Start registers corpses on the next frame; the native
                    // KillPlayer path has instantiated this one already.
                    var body = UnityEngine.Object.FindObjectsByType<BasicRagdoll>(FindObjectsSortMode.None).Single(r => ReferenceEquals(r.Info.Handler, handler));
                    using var delta = NetworkWriterPool.Get();
                    BraceletDeathPresentation.WriteBodyUpdate(delta, body, prisoner.ReferenceHub);
                    var reader = new NetworkReader(delta.ToArraySegment());
                    ulong mask = Compression.DecompressVarUInt(reader);
                    byte safety = reader.ReadByte();
                    int start = reader.Position;
                    body.OnDeserialize(reader, false);
                    Check("native_localized_corpse_delta_" + language, mask == 1UL << Array.IndexOf(body.netIdentity.NetworkBehaviours, body) && ((reader.Position - start) & 0xff) == safety && reader.Remaining == 0 && body.Info.Handler.RagdollInspectText == localization.Text(prisoner.ReferenceHub, BraceletDeathPresentation.WearerBodyText));
                }
                finally { localization.Forget(prisoner.ReferenceHub); }
            }
            var ordinary = new ExplosionDamageHandler(new Footprint(captor.ReferenceHub), Vector3.zero, 100, 100, ExplosionType.Grenade);
            var originalAttacker = ordinary.Attacker;
            Check("ordinary_grenades_keep_native_presentation", !BraceletDeathPresentation.IsBracelet(ordinary) && ReferenceEquals(ordinary, BraceletDeathPresentation.BodyHandler(ordinary, null)));
            BraceletDeathPresentation.Exploding = true;
            try { BraceletDeathPresentation.MarkExplosion(ordinary); }
            finally { BraceletDeathPresentation.Exploding = false; }
            Check("collateral_is_recognized", BraceletDeathPresentation.IsBracelet(ordinary));
            Check("collateral_keeps_real_handler_and_attacker", ordinary.Attacker.Equals(originalAttacker) && ordinary.Damage == 100);
            Check("collateral_body_identifies_nearby_bracelet", BraceletDeathPresentation.BodyHandler(ordinary, null).RagdollInspectText == BraceletDeathPresentation.BystanderBodyText);
        }
        catch (Exception error) { checks.Add("FAIL: " + error); }
        finally
        {
            controller.Reset();
            controller.Diagnostic = false;
            if (!wasActive) arcade.Suspend();
            foreach (var hub in hubs)
                if (hub != null) NetworkServer.Destroy(hub.gameObject);
        }
        return string.Join("\n", checks);
    }
}
