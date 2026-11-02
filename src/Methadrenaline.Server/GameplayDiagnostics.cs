using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CustomPlayerEffects;
using Footprinting;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using MEC;
using Methadrenaline.Server.Core;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerStatsSystem;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;
using NativeRadio = InventorySystem.Items.Radio.RadioItem;

namespace Methadrenaline.Server;

internal static class GameplayDiagnostics
{
    private static bool running;
    internal static string Run(ServerPlugin plugin)
    {
        if (running || EventDirector.Participants.Any() || Round.IsRoundInProgress) return "Gameplay diagnostics require an empty waiting server.";
        running = true;
        bool lobby = Round.IsLobbyLocked, locked = Round.IsLocked, paused = plugin.Director.Paused;
        var hubs = new List<ReferenceHub>();
        int passed = 0, failed = 0;
        void Check(string name, bool result)
        {
            if (result) passed++; else failed++;
            Logger.Info($"[METHADRENALINE] Gameplay check {name}: {(result ? "PASS" : "FAIL")}");
        }
        Timing.RunCoroutine(Probe());
        return "Gameplay diagnostics started.";

        IEnumerator<float> Probe()
        {
            try
            {
                Round.IsLobbyLocked = Round.IsLocked = true;
                plugin.Director.Pause(true);
                for (int i = 0; i < 3; i++)
                {
                    var hub = DummyUtils.SpawnDummy("GameplayProbe" + i);
                    hub.authManager.UserId = "ID_Dummy";
                    hubs.Add(hub);
                }
                var players = hubs.Select(h => Player.Get(h)!).ToArray();
                plugin.Paravoz!.BeginRound(players);
                // Native SCP-1509 protects all hubs for the first three scene
                // seconds, independently of the SpawnProtected status effect.
                // Also satisfy the native escape path's ten-second life minimum.
                yield return Timing.WaitForSeconds(10.5f);
                if (EventDirector.Participants.Any()) yield break;
                Player carrier = players.Single(plugin.Paravoz.IsParavoz);
                var runners = players.Where(p => p != carrier).ToArray();
                foreach (var voiceCheck in ParavozVoiceDiagnostics.Run(plugin.Paravoz, carrier, runners[0]))
                    Check(voiceCheck.Key, voiceCheck.Value);
                foreach (var presentationCheck in ParavozPresentationDiagnostics.Run(plugin.Paravoz, carrier, runners[0], runners[1]))
                    Check(presentationCheck.Key, presentationCheck.Value);
                Check("all_class_d", players.All(p => p.Role == RoleTypeId.ClassD));
                float nativeHealth = ((IHealthbarRole)PlayerRoleLoader.AllRoles[RoleTypeId.Scp106]).MaxHealth;
                Logger.Info($"[METHADRENALINE] Native carrier HP={carrier.Health}/{carrier.MaxHealth}; template={nativeHealth}");
                Check("native_106_health", carrier.Health == nativeHealth && carrier.MaxHealth == nativeHealth && !carrier.IsGodModeEnabled);
                Check("season_independent_abilities", plugin.Paravoz.Abilities.Enabled(carrier) && carrier.GetEffect<MovementBoost>()!.Intensity == 50);
                Check("invigorated_audio_filter_absent", !carrier.GetEffect<Invigorated>()!.IsEnabled);
                Check("seasonal_music_effect_inactive", carrier.GetEffect<SugarRush>()?.IsEnabled != true);
                carrier.DisableEffect<MovementBoost>();
                Check("carrier_abilities_cannot_be_cleared", carrier.GetEffect<MovementBoost>()!.Intensity == 50);
                var stamina = carrier.ReferenceHub.playerStats.GetModule<StaminaStat>();
                var runnerStamina = runners[0].ReferenceHub.playerStats.GetModule<StaminaStat>();
                float runnerStaminaBefore = runnerStamina.CurValue;
                var carrierProcessor = new FpcStateProcessor(carrier.ReferenceHub, ((IFpcRole)carrier.RoleBase).FpcModule, 0.05f, 0, 1, 0.126f, 3.11f);
                var runnerProcessor = new FpcStateProcessor(runners[0].ReferenceHub, ((IFpcRole)runners[0].RoleBase).FpcModule, 0.05f, 0, 1, 0.126f, 3.11f);
                bool sprinting = true;
                for (int i = 0; i < 1000; i++) sprinting &= carrierProcessor.UpdateMovementState(PlayerMovementState.Sprinting) == PlayerMovementState.Sprinting;
                for (int i = 0; i < 100; i++) runnerProcessor.UpdateMovementState(PlayerMovementState.Sprinting);
                Check("native_sprint_has_no_stamina_drain_without_effect", sprinting && stamina.CurValue == stamina.MaxValue && !carrier.GetEffect<Invigorated>()!.IsEnabled);
                Check("native_runner_stamina_still_drains", runnerStamina.CurValue < runnerStaminaBefore);
                runnerStamina.CurValue = runnerStaminaBefore;
                Check("room_locked", plugin.Paravoz.Status.Contains("isolation=True"));
                var chamber = plugin.Paravoz.IsolationDoor!;
                var cells = ParavozController.ClassDCellDoors();
                Logger.Info($"[METHADRENALINE] Isolation cell: doors={cells.Length}; type={chamber.Base.GetType().Name}; spawn_distance={Vector3.Distance(carrier.Position, chamber.Position):0.00}; carrier={carrier.Position}; door={chamber.Position}");
                Check("random_native_class_d_cell", chamber.Rooms.Length == 1 && chamber.Rooms[0].Name == MapGeneration.RoomName.LczClassDSpawn && cells.Length >= 2 && cells.OrderBy(d => (d.Position - carrier.Position).sqrMagnitude).First() == chamber);
                Check("runners_outside_locked_cell", runners.All(p => cells.OrderBy(d => (d.Position - p.Position).sqrMagnitude).First() != chamber));
                string waiting = plugin.Paravoz.WaitingHud(carrier);
                Check("waiting_timer_in_bracelet_hud", waiting.Contains("Release in: 00:") && plugin.Bracelets!.Hud(carrier).Contains(waiting));
                Check("waiting_timer_private", runners.All(p => plugin.Paravoz.WaitingHud(p) == ""));
                int beforeCountdown = ParavozPolicy.WaitingSeconds(plugin.Paravoz.Elapsed);
                plugin.Paravoz.AdvanceDiagnostic(2);
                Check("waiting_timer_counts_down", ParavozPolicy.WaitingSeconds(plugin.Paravoz.Elapsed) == beforeCountdown - 2);
                if (chamber.Base is Interactables.Interobjects.BreakableDoor cellDoor)
                {
                    cellDoor.ServerDamage(float.MaxValue, Interactables.Interobjects.DoorUtils.DoorDamageType.Grenade);
                    Check("isolation_door_resists_damage", !cellDoor.IsDestroyed);
                }
                plugin.Paravoz.YieldDoor(carrier, chamber.Base);
                Check("isolation_cannot_be_breached", !chamber.IsOpened && chamber.IsLocked);
                var hudPlugin = UltimateHUD.Plugin.Instance;
                Check("own_class_hud_enabled", hudPlugin.Config.EnablePlayerHud && hudPlugin.Config.PlayerHud == "<size=25>{role}</size>");
                string ownInfo = (string)AccessTools.Method(typeof(UltimateHUD.Hints), "BuildPlayerInfo").Invoke(null, new object[] { carrier });
                Check("carrier_class_visible", ownInfo.Contains("Paravoz"));
                var languageMap = (Dictionary<ReferenceHub, ServerLanguage>)AccessTools.Field(typeof(LocalizationService), "languages").GetValue(plugin.Localization!);
                bool hadLanguage = languageMap.TryGetValue(carrier.ReferenceHub, out var oldLanguage);
                try
                {
                    foreach (ServerLanguage language in new[] { ServerLanguage.English, ServerLanguage.Ukrainian, ServerLanguage.Russian })
                    {
                        languageMap[carrier.ReferenceHub] = language;
                        string info = (string)AccessTools.Method(typeof(UltimateHUD.Hints), "BuildPlayerInfo").Invoke(null, new object[] { carrier });
                        string roleLabel = plugin.Localization!.Catalog.Translate("Paravoz", language);
                        Check("carrier_role_red_" + language, info.Contains("<color=" + ParavozRoleDisplayPatch.Color + ">") && info.Contains("<noparse>" + roleLabel + "</noparse>"));
                    }
                }
                finally
                {
                    if (hadLanguage) languageMap[carrier.ReferenceHub] = oldLanguage;
                    else languageMap.Remove(carrier.ReferenceHub);
                }
                Check("runner_role_color_unchanged", UltimateHUD.Options.GetRoleColor(runners[0]) == "#" + ColorUtility.ToHtmlStringRGB(runners[0].RoleBase.RoleColor));

                var radio = (LabApi.Features.Wrappers.RadioItem)runners[0].AddItem(ItemType.Radio)!;
                Check("radio_full", radio.Base.BatteryPercent == 100);
                byte battery = radio.Base.BatteryPercent;
                for (int i = 0; i < 10000; i++) AccessTools.Method(typeof(NativeRadio), "Update").Invoke(radio.Base, null);
                Check("radio_does_not_drain", radio.Base.BatteryPercent == battery);

                float hp = carrier.Health;
                carrier.Damage(new UniversalDamageHandler(50, DeathTranslations.Falldown));
                Check("world_damage_blocked", carrier.Health == hp);
                carrier.Damage(new ExplosionDamageHandler(new Footprint(runners[0].ReferenceHub), Vector3.zero, 50, 100, ExplosionType.Custom));
                Check("runner_damage_blocked", carrier.Health == hp);
                runners[0].Position = Escape.DefaultEscapeZone.center;
                Physics.SyncTransforms();
                AccessTools.Method(typeof(Escape), "ServerHandlePlayer").Invoke(null, new object[] { runners[0].ReferenceHub });
                yield return Timing.WaitForSeconds(0.3f);
                Check("hunter_registered", plugin.Paravoz.IsHunter(runners[0]));
                Check("native_escape_sets_hunter_body", runners[0].Role == RoleTypeId.NtfPrivate);
                Check("native_escape_clears_hunter_protection", !runners[0].IsGodModeEnabled && runners[0].GetEffect<SpawnProtected>()?.IsEnabled != true);
                Check("carrier_hunter_runner_keeps_round_open", plugin.Paravoz.Result == ParavozWinner.None && plugin.Paravoz.Policy.Winner == ParavozWinner.None);
                var mixedConditions = new RoundEndingConditionsCheckEventArgs(true);
                ServerEvents.OnRoundEndingConditionsCheck(mixedConditions);
                Check("mixed_roster_overrides_native_early_win", !mixedConditions.CanEnd);
                string hunterInfo = (string)AccessTools.Method(typeof(UltimateHUD.Hints), "BuildPlayerInfo").Invoke(null, new object[] { runners[0] });
                Check("escaped_class_visible", hunterInfo.Contains("Paravoz Hunter") && !hunterInfo.Contains("Class-D"));
                carrier.DisableEffect<SpawnProtected>();
                var hunterHit = new ExplosionDamageHandler(new Footprint(runners[0].ReferenceHub), Vector3.zero, 50, 100, ExplosionType.Custom);
                carrier.Damage(hunterHit);
                Logger.Info($"[METHADRENALINE] Hunter damage result: hp={carrier.Health}; before={hp}; attacker={runners[0].NetworkId}; god={runners[0].IsGodModeEnabled}; protected={runners[0].GetEffect<SpawnProtected>()?.Intensity}; damage_after={hunterHit.Damage}; absorbed_ahp={hunterHit.AbsorbedAhpDamage}; absorbed_hs={hunterHit.AbsorbedHumeDamage}; target_stats_match={((IHealthbarRole)carrier.RoleBase).TargetStats == carrier.ReferenceHub.playerStats}; effects={string.Join(",", carrier.ReferenceHub.playerEffectsController.AllEffects.Where(e => e.IsEnabled).Select(e => e.GetType().Name + ":" + e.Intensity))}");
                Check("hunter_damage_allowed", carrier.Health < hp && carrier.IsAlive);
                hp = carrier.Health;
                var rifle = (InventorySystem.Items.Firearms.Firearm)runners[0].Items.First(x => x.Type == ItemType.GunE11SR).Base;
                carrier.Damage(new FirearmDamageHandler(rifle, 50, 1));
                Check("escaped_hunter_real_firearm_damages_carrier", carrier.Health < hp && carrier.IsAlive);
                FireHits(runners[0], "fire_hurts_escaped_hunter");
                FireHits(runners[1], "fire_hurts_unescaped_runner");
                var attackPositions = players.Select(p => p.Position).ToArray();
                var barrier = new GameObject("ParavozAttackBarrierProbe");
                try
                {
                    foreach (var other in players.Where(p => p != carrier)) other.Position = carrier.Position + Vector3.right * 30;
                    var target = runners[1];
                    float before = target.Health;
                    target.Position = carrier.Position + Vector3.forward * 6;
                    Physics.SyncTransforms();
                    plugin.Paravoz.Abilities.FireTick(carrier);
                    Check("fire_respects_five_meter_range", target.Health == before);
                    target.Position = carrier.Position + Vector3.back * 3;
                    Physics.SyncTransforms();
                    plugin.Paravoz.Abilities.FireTick(carrier);
                    Check("fire_respects_forward_cone", target.Health == before);
                    target.Position = carrier.Position + Vector3.forward * 0.4f;
                    int mask = PlayerRolesUtils.AttackMask;
                    barrier.layer = Enumerable.Range(0, 32).First(layer => (mask & (1 << layer)) != 0);
                    barrier.transform.position = carrier.Position + Vector3.forward * 0.2f;
                    var collider = barrier.AddComponent<BoxCollider>();
                    collider.size = new Vector3(2, 4, 0.06f);
                    Physics.SyncTransforms();
                    plugin.Paravoz.Abilities.FireTick(carrier);
                    Check("fire_does_not_pass_through_walls", target.Health == before);
                    collider.enabled = false;
                    Physics.SyncTransforms();
                    plugin.Paravoz.Abilities.FireTick(carrier);
                    Check("fire_hits_after_obstruction_removed", target.Health < before);
                }
                finally
                {
                    UnityEngine.Object.Destroy(barrier);
                    for (int i = 0; i < players.Length; i++) if (players[i].IsAlive) players[i].Position = attackPositions[i];
                    Physics.SyncTransforms();
                }
                float runnerHp = runners[1].Health;
                runners[1].Damage(new UniversalDamageHandler(5, DeathTranslations.Falldown));
                Check("runner_world_damage_not_immune", runners[1].Health < runnerHp);
                hp = carrier.Health;
                carrier.Heal(50);
                carrier.Health = hp + 50;
                Check("all_health_restoration_blocked", carrier.Health == hp);

                plugin.Paravoz.AdvanceDiagnostic(60);
                yield return Timing.WaitForSeconds(0.3f);
                Check("isolation_released", plugin.Paravoz.Status.Contains("isolation=False"));
                Check("waiting_timer_removed_on_release", plugin.Paravoz.WaitingHud(carrier) == "" && !plugin.Bracelets!.Hud(carrier).Contains("Release in:"));
                if (chamber.Base is Interactables.Interobjects.BreakableDoor releasedCell)
                    Check("cell_damage_restored", releasedCell.IgnoredDamageSources != (Interactables.Interobjects.DoorUtils.DoorDamageType)byte.MaxValue);
                var breakable = Door.List.Select(d => d.Base).OfType<Interactables.Interobjects.BreakableDoor>().First(d => !d.IsDestroyed);
                try
                {
                    plugin.Paravoz.YieldDoor(carrier, breakable);
                    Check("carrier_breaks_doors", breakable.IsDestroyed);
                }
                finally { if (breakable.IsDestroyed) breakable.ServerRepair(); }
                var gate = Door.Get(LabApi.Features.Enums.DoorName.Lcz914Gate)!;
                bool gateWasOpened = gate.IsOpened;
                try
                {
                    gate.IsOpened = false;
                    plugin.Paravoz.YieldDoor(carrier, gate.Base);
                    Check("carrier_opens_indestructible_door", gate.IsOpened);
                }
                finally { gate.IsOpened = gateWasOpened; }
                using (var elevatorProbe = ElevatorDiagnostics.Probe(plugin.Paravoz, carrier, runners[0], Check))
                    while (elevatorProbe.MoveNext()) yield return elevatorProbe.Current;
                Vector3 origin = carrier.Position;
                runners[1].Position = origin + Vector3.right * 15;
                plugin.Paravoz.AdvanceDiagnostic(350.1f - plugin.Paravoz.Elapsed);
                Check("gather_countdown_ten_seconds", plugin.Paravoz.GatheringHud(runners[1]).Contains("10"));
                Check("gather_countdown_private", plugin.Paravoz.GatheringHud(carrier) == "" && plugin.Paravoz.GatheringHud(runners[0]) == "");
                Check("gather_countdown_in_hud", plugin.Bracelets!.Hud(runners[1]).Contains("10"));
                plugin.Paravoz.AdvanceDiagnostic(10);
                yield return Timing.WaitForSeconds(0.3f);
                Check("six_minute_gather", Vector3.Distance(runners[1].Position, carrier.Position) < 3);
                Check("gather_countdown_removed_after_teleport", plugin.Paravoz.GatheringHud(runners[1]) == "");

                // Use the production attack, not a fabricated death event.
                runners[0].Position = carrier.Position + Vector3.right * 30;
                runners[1].Position = carrier.Position + Vector3.forward * 0.4f;
                runners[1].IsGodModeEnabled = false;
                runners[1].DisableEffect<SpawnProtected>();
                runners[1].Health = 1;
                Physics.SyncTransforms();
                for (int i = 0; i < 8 && runners[1].IsAlive; i++) plugin.Paravoz.Abilities.FireTick(carrier);
                Logger.Info($"[METHADRENALINE] Fire tick result: victim_hp={runners[1].Health}; role={runners[1].Role}; carrier_pos={carrier.Position}; victim_pos={runners[1].Position}");
                yield return Timing.WaitForSeconds(0.4f);
                Check("native_fire_infects", plugin.Paravoz.IsParavoz(runners[1]) && runners[1].IsAlive && runners[1].Role == RoleTypeId.ClassD);
                plugin.Paravoz.Presentation.RefreshNow();
                Check("infected_player_gets_visuals_and_shared_music", plugin.Paravoz.Presentation.Emitters.ContainsKey(runners[1].NetworkId) && plugin.Paravoz.Presentation.Playback?.ClipsById.Count == 1);
                Check("human_escape_winner", plugin.Paravoz.Result == ParavozWinner.Humans);
                var winningConditions = new RoundEndingConditionsCheckEventArgs(false);
                ServerEvents.OnRoundEndingConditionsCheck(winningConditions);
                Check("custom_win_survives_native_faction_recount", winningConditions.CanEnd);
                var endingEvent = new RoundEndingEventArgs(RoundSummary.LeadingTeam.Anomalies);
                ServerEvents.OnRoundEnding(endingEvent);
                Check("custom_win_uses_human_team", endingEvent.IsAllowed && endingEvent.LeadingTeam == RoundSummary.LeadingTeam.ChaosInsurgency);
                // This is where 1.9.5 blocked every hit indefinitely.
                FireHits(runners[0], "fire_still_damages_after_winner_selected");
                hp = carrier.Health;
                carrier.Damage(new FirearmDamageHandler(rifle, 20, 1));
                Check("hunter_still_damages_after_winner_selected", carrier.Health < hp);

                plugin.Paravoz.Reset();
                Check("carrier_abilities_cleaned_on_reset", !plugin.Paravoz.Abilities.Enabled(carrier) && carrier.GetEffect<MovementBoost>()!.Intensity == 0 && !carrier.GetEffect<Invigorated>()!.IsEnabled);
                Check("presentation_and_music_cleaned_on_reset", plugin.Paravoz.Presentation.Emitters.Count == 0 && plugin.Paravoz.Presentation.Playback == null);
                var grenade = LabApi.Features.Wrappers.TimedGrenadeProjectile.SpawnActive(runners[0].Position + Vector3.down * 0.7f, ItemType.GrenadeHE, owner: runners[0]);
                Check("active_native_grenade", grenade != null && grenade.Base.TargetTime > NetworkTime.time);
                float grenadeHealth = runners[0].Health;
                grenade?.FuseEnd();
                Check("grenade_damages_its_target", runners[0].Health < grenadeHealth);
                if (grenade != null && !grenade.IsDestroyed) grenade.Destroy();

                // A respawned body may remain in a native spectator cache; the
                // public getter must never expose an alive player as a viewer.
                Check("alive_not_spectators", players.All(p => p.CurrentSpectators.All(s => !s.IsAlive)));

                void FireHits(Player target, string name)
                {
                    var positions = players.Select(p => p.Position).ToArray();
                    try
                    {
                        foreach (var other in players.Where(p => p != carrier)) other.Position = carrier.Position + Vector3.right * 30;
                        target.Position = carrier.Position + Vector3.forward * 0.4f;
                        Physics.SyncTransforms();
                        float before = target.Health;
                        for (int i = 0; i < 5 && target.Health == before; i++)
                            plugin.Paravoz.Abilities.FireTick(carrier);
                        Check(name, target.IsAlive && target.Health < before);
                    }
                    finally
                    {
                        for (int i = 0; i < players.Length; i++) if (players[i].IsAlive) players[i].Position = positions[i];
                        Physics.SyncTransforms();
                    }
                }
            }
            finally
            {
                plugin.Paravoz!.Reset();
                foreach (var hub in hubs) if (hub != null) NetworkServer.Destroy(hub.gameObject);
                plugin.Director.Pause(paused);
                Round.IsLobbyLocked = lobby;
                Round.IsLocked = locked;
                running = false;
                Logger.Info($"[METHADRENALINE] Gameplay diagnostics finished: {passed} PASS, {failed} FAIL; test NPCs and temporary locks cleaned up.");
            }
        }
    }
}
