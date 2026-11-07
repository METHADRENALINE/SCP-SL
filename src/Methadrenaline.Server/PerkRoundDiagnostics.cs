using System;
using System.Collections.Generic;
using System.Linq;
using CommandSystem;
using HarmonyLib;
using LabApi.Features.Wrappers;
using MEC;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using SwiftArcadeMode.Features;
using SwiftArcadeMode.Features.Game.Modes;
using SwiftArcadeMode.Features.Humans.Perks;
using SwiftArcadeMode.Features.Humans.Perks.Content;
using SwiftArcadeMode.Features.Humans.Perks.Content.Caster;
using SwiftArcadeMode.Features.Humans.Perks.Crafting;
using SwiftArcadeMode.Features.Scoring;
using SwiftArcadeMode.Features.SCPs.Upgrades;
using SwiftArcadeMode.Utils.Deployable;
using SwiftArcadeMode.Utils.Effects;
using SwiftArcadeMode.Utils.Projectiles;
using UserSettings.ServerSpecific;
using UnityEngine;

namespace Methadrenaline.Server;

internal static class PerkRoundDiagnostics
{
    internal static string Run(PerkRoundController controller)
    {
        if (Round.IsRoundInProgress || Player.List.Any(p => !p.IsHost && !p.IsNpc)) return "Perk diagnostics require an empty waiting server.";
        var checks = new List<string>();
        ReferenceHub? hub = null;
        bool removed = false;
        void Check(string name, bool success) => checks.Add(name + "=" + (success ? "PASS" : "FAIL"));
        try
        {
            for (int cycle = 0; cycle < 3; cycle++)
            {
                controller.Resume();
                Check("enabled_registry_cycle_" + cycle, PerkManager.RegisteredPerks.Count == 54 && RecipeManager.All.Count == 5 && GameModeManager.Registry.Count == 0 && ScoringManager.Events.Count > 0 && ServerSpecificSettingsSync.DefinedSettings.Count(PerkRoundController.IsArcadeSetting) == 6);
                int events = ScoringManager.Events.Count;
                controller.Resume();
                Check("enable_idempotent_" + cycle, ScoringManager.Events.Count == events && PerkManager.RegisteredPerks.Count == 54);
                Check("class_perks_absent_cycle_" + cycle, !PerkManager.RegisteredPerks.Values.Any(p => ArcadeContentPolicy.IsExcluded(p.Perk)));
                GameModeManager.Current = new CasterBattle();
                Check("caster_battle_blocked_cycle_" + cycle, GameModeManager.Current == null);
                if (cycle == 0)
                {
                    hub = DummyUtils.SpawnDummy("PerkRoundProbe");
                    var player = Player.Get(hub)!;
                    player.SetRole(RoleTypeId.Tutorial);
                    foreach (Type type in new[] { typeof(Wizard), typeof(Sorcerer), typeof(Warlock), typeof(Druid) })
                    {
                        var stale = new PerkAttribute(type.Name) { Perk = type };
                        Check("class_grants_blocked_" + type.Name, !player.GivePerk(type) && !player.GivePerk(stale) && !player.GetPerkInventory().AddPerk(stale));
                        Check("class_pickups_blocked_" + type.Name, PerkSpawner.SpawnPerk(stale, player.Position) == null);
                    }
                    Check("vanilla_role_preserved", player.Role == RoleTypeId.Tutorial);
                    Check("perk_grant_enabled", player.GivePerk(typeof(Turret)));
                    PerkSpawner.SpawnPerks();
                    Check("perk_pickups_spawn", PerkSpawner.PerkPickups.Count > 0 && PerkSpawner.LightSources.Count > 0);
                    Check("spawned_perks_have_no_classes", PerkSpawner.PerkPickups.Values.All(p => !ArcadeContentPolicy.IsExcluded(p.Perk)));
                    player.AddCustomEffect(new ProbeEffect(() => removed = true));
                    Check("custom_effect_added", CustomEffectManager.Containers[player].ActiveEffects.Count == 1);
                    var spell = new Fireball();
                    spell.Init(new Wizard(player.GetPerkInventory()));
                    new Fireball.Projectile(spell, player.Camera.position + Vector3.up, Quaternion.identity, Vector3.zero, 15, player);
                    Check("projectile_created", ProjectileManager.All.Count > 0);
                    new SummonArcaneGolem.Golem(spell, "PerkRoundProbe Golem", "SAM-ArcaneGolem", player.Role, new Vector3(1, 0.5f, 1), player.Position + Vector3.forward * 2, Quaternion.identity) { Owner = player };
                    Check("deployable_created", DeployableManager.AllDeployables.Count > 0);
                    var tank = new SwiftArcadeMode.Features.SCPs.Upgrades.Content.SCP173.Tank.Tank(player.GetPerkInventory());
                    player.GetPerkInventory().Perks.Add(tank);
                    tank.Init();
                    tank.Progress = 1;
                    Timing.CallDelayed(0.05f, RecipeManager.Enable);
                }
                controller.Suspend();
                Check("disabled_clean_cycle_" + cycle, !controller.Active && PerkManager.Inventories.Count == 0 && PerkSpawner.PerkPickups.Count == 0 && PerkSpawner.LightSources.Count == 0 && ProjectileManager.All.Count == 0 && DeployableManager.AllDeployables.Count == 0 && CustomEffectManager.Containers.Count == 0 && RecipeManager.All.Count == 0 && ScoringManager.Events.Count == 0 && GameModeManager.Current == null && !PerkSpawner.AllowSpawn && !UpgradePathGiver.AllowLeveling && !GameModeManager.Allow && !ServerSpecificSettingsSync.DefinedSettings.Any(PerkRoundController.IsArcadeSetting));
                Check("all_arcade_subscriptions_removed_" + cycle, ArcadeSubscriptions.Count() == 0);
                if (cycle == 0)
                {
                    var player = Player.Get(hub)!;
                    Check("custom_effect_removed", removed);
                    Check("perk_grant_blocked", !player.GivePerk(typeof(Turret)) && PerkManager.Inventories.Count == 0);
                    Check("pickup_spawn_blocked", PerkSpawner.SpawnPerk(PerkManager.GetPerk(typeof(Turret)), Vector3.zero) == null && PerkSpawner.PerkPickups.Count == 0);
                    var command = (ICommand)Activator.CreateInstance(AccessTools.TypeByName("SwiftArcadeMode.Commands.RA.AddPerkCommand"))!;
                    bool result = command.Execute(new ArraySegment<string>(new[] { "turret" }), new RemoteAdmin.PlayerCommandSender(hub), out string response);
                    Check("arcade_command_blocked", !result && response == PerkRoundController.DisabledMessage);
                }
                controller.Suspend();
                Check("disable_idempotent_" + cycle, PerkSpawner.PerkPickups.Count == 0 && !controller.Active);
            }
            Check("other_settings_preserved", ServerSpecificSettingsSync.DefinedSettings.Any(s => s.SettingId == LocalizationService.LanguageSettingId));
            Timing.CallDelayed(0.2f, () => LabApi.Features.Console.Logger.Info("[METHADRENALINE] Deferred arcade callback cancellation: " + (RecipeManager.All.Count == 0 && !controller.Active ? "PASS" : "FAIL")));
        }
        catch (Exception error) { checks.Add("FAIL: " + error); }
        finally
        {
            controller.Reset();
            if (hub != null) NetworkServer.Destroy(hub.gameObject);
        }
        return string.Join("\n", checks);
    }

    private sealed class ProbeEffect(Action onRemoved) : CustomEffectBase(20)
    {
        public override void Add() { }
        public override void Tick() { }
        public override void Remove() => onRemoved();
    }
}
