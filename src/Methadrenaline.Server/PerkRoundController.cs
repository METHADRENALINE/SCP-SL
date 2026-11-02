using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LabApi.Features.Wrappers;
using MEC;
using Methadrenaline.Server.Core;
using SwiftArcadeMode.Features;
using SwiftArcadeMode.Features.Game.Modes;
using SwiftArcadeMode.Features.Humans.Perks;
using SwiftArcadeMode.Features.Humans.Perks.Content.Gambler;
using SwiftArcadeMode.Features.Humans.Perks.Content.Shopkeeper;
using SwiftArcadeMode.Features.Humans.Perks.Crafting;
using SwiftArcadeMode.Features.Scoring;
using SwiftArcadeMode.Features.SCPs.Upgrades;
using SwiftArcadeMode.Features.ServerSpecificSettings;
using SwiftArcadeMode.Utils.Deployable;
using SwiftArcadeMode.Utils.Effects;
using SwiftArcadeMode.Utils.Projectiles;
using UserSettings.ServerSpecific;
using ArcadeCore = SwiftArcadeMode.Core;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

/// <summary>Owns the entire SwiftArcade lifecycle. Keeping the assembly loaded
/// preserves its assets and commands while its gameplay is suspended.</summary>
internal sealed class PerkRoundController : IDisposable
{
    internal const string DisabledMessage = "This round has no perks.";
    internal const ushort AnnouncementDuration = 10;
    private readonly ArcadeCore arcade;
    private readonly Random random = new();
    internal PerkRoundPolicy Policy { get; } = new();
    internal bool Active { get; private set; }
    internal int Epoch { get; private set; }
    internal bool Cleaning { get; private set; }
    private bool disposed;
    private bool upstreamEnabled = true;
    internal CommunityRoundMode? NextMode { get; set; }
    internal string Status => $"perks={(Policy.HasPerks.HasValue ? Policy.HasPerks.Value ? "on" : "off" : "waiting")}; arcade_active={Active}; perk_pickups={PerkSpawner.PerkPickups.Count}; arcade_handlers={ArcadeSubscriptions.Count()}";

    internal PerkRoundController()
    {
        arcade = ArcadeCore.Instance ?? throw new InvalidOperationException("SwiftArcade must be loaded before METHADRENALINE.");
        if (arcade.Version != new Version(2, 4, 0)) throw new InvalidOperationException("The round integration requires SwiftArcade 2.4.0.");
    }

    internal void Initialize()
    {
        Suspend();
        ArcadeContentPolicy.Apply();
    }

    internal void BeginRound()
    {
        if (disposed || Policy.HasPerks.HasValue) return;
        Epoch++;
        var mode = Policy.BeginMode(() =>
        {
            var chosenMode = ServerPlugin.Instance?.ModeVoting?.SelectedMode() ?? NextMode ?? (CommunityRoundMode)random.Next(EventDirector.Participants.Count() >= 2 ? 3 : 2);
            NextMode = null;
            if (chosenMode == CommunityRoundMode.Paravoz && EventDirector.Participants.Count() < 2) chosenMode = CommunityRoundMode.Plain;
            return chosenMode;
        });
        bool chosen = mode == CommunityRoundMode.Perks;
        if (chosen) Resume(); else Suspend();
        Logger.Info("[METHADRENALINE] Round mode=" + ModeVotePolicy.Label(mode) + "; discord_voting=" + (ServerPlugin.Instance?.ModeVoting != null) + "; " + Status);
    }

    internal void Announce()
    {
        int epoch = Epoch;
        // SwiftArcade spawns its coins after 0.1s. Its optional mode also clears
        // broadcasts during RoundStarted, so announce after both have completed.
        Timing.CallDelayed(0.35f, () =>
        {
            if (disposed || epoch != Epoch || !Policy.HasPerks.HasValue || !Round.IsRoundInProgress) return;
            LabApi.Features.Wrappers.Server.SendBroadcast(Policy.Message, AnnouncementDuration, Broadcast.BroadcastFlags.Normal, true);
        });
    }

    internal void Reset()
    {
        Suspend();
        Policy.Reset();
    }

    internal bool IsCurrent(int epoch) => !disposed && Active && epoch == Epoch;

    internal void Resume()
    {
        if (Active) return;
        // Upstream Enable appends to these registries. Fresh registries prevent
        // duplicate perks, recipes, modes and score handlers after OFF -> ON.
        PerkManager.RegisteredPerks.Clear();
        UpgradePathManager.RegisteredUpgrades.Clear();
        Gambler.PositiveEffects.Clear();
        Gambler.NegativeEffects.Clear();
        RecipeManager.All.Clear();
        GameModeManager.Registry.Clear();
        ScoringManager.Events.Clear();
        AccessTools.Field(typeof(UpgradePathGiver), "_scpLevel").SetValue(null, 1);
        AccessTools.Field(typeof(UpgradePathGiver), "_scpTeamExperience").SetValue(null, 0);
        Active = true;
        upstreamEnabled = true;
        try
        {
            arcade.Enable();
            ArcadeContentPolicy.Apply();
            foreach (Player player in Player.List.Where(p => !p.IsHost && !p.IsNpc))
                player.GetPerkInventory().BaseLimit = player.IsSCP ? 2 : 5;
        }
        catch
        {
            Suspend();
            throw;
        }
    }

    internal void Suspend()
    {
        Epoch++;
        Active = false;
        if (Cleaning) return;
        Cleaning = true;
        var failures = new List<Exception>();
        void Clean(Action action)
        {
            try { action(); }
            catch (Exception error) { failures.Add(error); }
        }
        try
        {
            if (upstreamEnabled)
            {
                upstreamEnabled = false;
                Clean(arcade.Disable);
                // SwiftArcade 2.4 Disable forgets this subscription and leaves
                // its settings registered. Remove both without touching others.
                var handler = (Action<ReferenceHub, ServerSpecificSettingBase>)Delegate.CreateDelegate(typeof(Action<ReferenceHub, ServerSpecificSettingBase>), AccessTools.Method(typeof(SSSManager), "OnPressKeybind"));
                ServerSpecificSettingsSync.ServerOnSettingValueReceived -= handler;
            }
            PerkSpawner.AllowSpawn = false;
            UpgradePathGiver.AllowLeveling = false;
            GameModeManager.Allow = false;
            Clean(() => GameModeManager.Current = null);
            PerkSpawner.PerkSpawnRules = PerkSpawner.DefaultSpawnRules;
            GameModeManager.ForcedRound = false;
            foreach (var inventory in PerkManager.Inventories.Values.ToArray())
                foreach (var perk in inventory.Perks.ToArray()) Clean(() => inventory.RemovePerk(perk));
            PerkManager.Inventories.Clear();
            foreach (var container in CustomEffectManager.Containers.Values.ToArray()) Clean(container.ClearEffects);
            CustomEffectManager.Containers.Clear();
            foreach (var projectile in ProjectileManager.All.ToArray()) Clean(projectile.Destroy);
            ProjectileManager.All.Clear();
            foreach (var deployable in DeployableManager.AllDeployables.ToArray()) Clean(deployable.Destroy);
            DeployableManager.AllDeployables.Clear();
            foreach (var pickup in Pickup.List.Where(p => PerkSpawner.PerkPickups.ContainsKey(p.Serial)).ToArray()) Clean(pickup.Destroy);
            PerkSpawner.PerkPickups.Clear();
            foreach (var light in PerkSpawner.LightSources.Values.ToArray())
                if (light.Base != null) Clean(light.Destroy);
            PerkSpawner.LightSources.Clear();
            Shopkeeper.ClaimStatus.Clear();
            RecipeManager.All.Clear();
            ScoringManager.Events.Clear();
            Clean(ArcadeSubscriptions.Clear);
            var settings = ServerSpecificSettingsSync.DefinedSettings ?? Array.Empty<ServerSpecificSettingBase>();
            var filtered = settings.Where(s => !IsArcadeSetting(s)).ToArray();
            if (filtered.Length != settings.Length)
            {
                ServerSpecificSettingsSync.DefinedSettings = filtered;
                ServerSpecificSettingsSync.SendToAll();
            }
        }
        finally { Cleaning = false; }
        if (failures.Count > 0) throw new AggregateException("SwiftArcade cleanup failed.", failures);
    }

    internal static bool IsArcadeSetting(ServerSpecificSettingBase setting) => setting.SettingId >= 20070923 && setting.SettingId <= 20070927 || setting is SSGroupHeader && setting.Label == "SwiftKraft's Arcade Mode";

    public void Dispose()
    {
        if (disposed) return;
        Suspend();
        disposed = true;
    }
}
