using System;
using System.Linq;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features;
using LabApi.Features.Wrappers;
using MEC;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal sealed class BraceletPlaytest(BraceletController controller) : IDisposable
{
    private Player? prisoner;
    private ReferenceHub? ownerHub;
    private Vector3 origin;
    private int life;
    private float started;
    private float deathTime;
    private bool lobbyWasLocked;
    private bool roundWasLocked;
    private bool active;
    private bool captorTest;
    private int generation;

    internal string Start(int playerId)
    {
        if (active) return "A bracelet playtest is already running.";
        var target = Player.List.FirstOrDefault(p => p.PlayerId == playerId && !p.IsHost && !p.IsNpc && !p.IsDummy);
        if (target == null) return "Player not found.";
        if (Player.List.Any(p => p != target && !p.IsHost && !p.IsNpc && !p.IsDummy))
            return "The solo bracelet playtest requires only its participant online.";
        if (controller.Ledger.Active.Count > 0) return "Finish existing bracelet bindings first.";
        lobbyWasLocked = Round.IsLobbyLocked;
        roundWasLocked = Round.IsLocked;
        active = true;
        captorTest = false;
        int run = ++generation;
        try
        {
            Round.IsLobbyLocked = true;
            Round.IsLocked = true;
            prisoner = target;
            if (!target.IsHuman) target.SetRole(RoleTypeId.ClassD);
            life = target.LifeId;
            ownerHub = DummyUtils.SpawnDummy("Bracelet test guard");
            ownerHub.authManager.UserId = "ID_Dummy";
            Player.Get(ownerHub)!.SetRole(RoleTypeId.FacilityGuard);
            Timing.CallDelayed(2f, () =>
            {
                if (!active || generation != run) return;
                try
                {
                    if (prisoner == null || prisoner.IsDestroyed || !prisoner.IsAlive || prisoner.LifeId != life) { Dispose(); return; }
                    var owner = Player.Get(ownerHub)!;
                    owner.IsGodModeEnabled = true;
                    origin = prisoner.Position + Vector3.forward * 2;
                    owner.Position = origin;
                    owner.CurrentItem = owner.AddItem(ItemType.GunCOM15);
                    prisoner.DisarmedBy = owner;
                    bool diagnostic = controller.Diagnostic;
                    try
                    {
                        controller.Diagnostic = true;
                        PlayerEvents.OnCuffed(new PlayerCuffedEventArgs(owner.ReferenceHub, prisoner.ReferenceHub));
                    }
                    finally { controller.Diagnostic = diagnostic; }
                    if (!controller.Ledger.TryGet(BraceletController.Life(prisoner), out _))
                        throw new InvalidOperationException("The test bracelet did not arm.");
                    started = Time.unscaledTime;
                    deathTime = 0;
                    StaticUnityMethods.OnFixedUpdate += Move;
                    Logger.Info("[METHADRENALINE] Bracelet playtest armed; guard starts moving in 8 seconds at 0.6 m/s.");
                }
                catch (Exception error) { Logger.Error("[METHADRENALINE] Bracelet playtest failed: " + error); Dispose(); }
            });
            return "Bracelet playtest preparing.";
        }
        catch { Dispose(); throw; }
    }

    internal bool AllowsPair(Player captor, Player captive) => active && captorTest && prisoner == captor &&
        captor.LifeId == life && captive.ReferenceHub == ownerHub;

    internal string StartCaptor(int playerId)
    {
        if (active) return "A bracelet playtest is already running.";
        var target = Player.List.FirstOrDefault(p => p.PlayerId == playerId && !p.IsHost && !p.IsNpc && !p.IsDummy);
        if (target == null) return "Player not found.";
        if (Player.List.Any(p => p != target && !p.IsHost && !p.IsNpc && !p.IsDummy))
            return "The solo bracelet playtest requires only its participant online.";
        if (controller.Ledger.Active.Count > 0) return "Finish existing bracelet bindings first.";
        lobbyWasLocked = Round.IsLobbyLocked;
        roundWasLocked = Round.IsLocked;
        active = captorTest = true;
        int run = ++generation;
        try
        {
            Round.IsLobbyLocked = Round.IsLocked = true;
            prisoner = target;
            target.SetRole(RoleTypeId.FacilityGuard);
            life = target.LifeId;
            target.CurrentItem = target.AddItem(ItemType.GunCOM15);
            ownerHub = DummyUtils.SpawnDummy("Bracelet test Class-D");
            ownerHub.authManager.UserId = "ID_Dummy";
            Player.Get(ownerHub)!.SetRole(RoleTypeId.ClassD);
            Timing.CallDelayed(2f, () =>
            {
                if (!active || generation != run) return;
                try
                {
                    if (target.IsDestroyed || !target.IsAlive || target.LifeId != life || ownerHub == null) { Dispose(); return; }
                    Vector3 direction = target.ReferenceHub.PlayerCameraReference.forward;
                    direction.y = 0;
                    if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
                    Player.Get(ownerHub)!.Position = target.Position + direction.normalized * 1.5f;
                    started = Time.unscaledTime;
                    deathTime = 0;
                    StaticUnityMethods.OnFixedUpdate += Move;
                    Logger.Info("[METHADRENALINE] Captor playtest ready; cuff the Class-D manually. Timeout: 180 seconds.");
                }
                catch (Exception error) { Logger.Error("[METHADRENALINE] Captor playtest failed: " + error); Dispose(); }
            });
            return "Captor bracelet playtest preparing.";
        }
        catch { Dispose(); throw; }
    }

    private void Move()
    {
        if (prisoner == null || prisoner.IsDestroyed || ownerHub == null) { Dispose(); return; }
        float elapsed = Time.unscaledTime - started;
        if (captorTest)
        {
            if (elapsed > 180 || !prisoner.IsAlive || prisoner.LifeId != life) { Dispose(); return; }
            var captive = Player.Get(ownerHub);
            if (captive == null || !captive.IsAlive)
            {
                if (deathTime == 0) deathTime = Time.unscaledTime;
                if (Time.unscaledTime - deathTime > 10) Dispose();
            }
            return;
        }
        if (elapsed > 80 || (prisoner.IsAlive && prisoner.LifeId != life)) { Dispose(); return; }
        if (!prisoner.IsAlive)
        {
            if (deathTime == 0)
            {
                deathTime = Time.unscaledTime;
                Logger.Info("[METHADRENALINE] Bracelet playtest wearer died; " + controller.Status);
            }
            if (Time.unscaledTime - deathTime > 10) Dispose();
            return;
        }
        if (!prisoner.IsDisarmed) { Dispose(); return; }
        Player.Get(ownerHub)!.Position = origin + Vector3.right * (Mathf.Max(0, elapsed - 8) * 0.6f);
    }

    public void Dispose()
    {
        if (!active) return;
        active = false;
        generation++;
        StaticUnityMethods.OnFixedUpdate -= Move;
        if (captorTest && ownerHub != null && Player.Get(ownerHub) is { } captive)
        {
            captive.IsDisarmed = false;
            controller.CheckNow();
        }
        if (prisoner != null && !prisoner.IsDestroyed && prisoner.LifeId == life && prisoner.DisarmedBy?.ReferenceHub == ownerHub)
        {
            prisoner.IsDisarmed = false;
            controller.CheckNow();
        }
        if (ownerHub != null) NetworkServer.Destroy(ownerHub.gameObject);
        ownerHub = null;
        prisoner = null;
        captorTest = false;
        Round.IsLobbyLocked = lobbyWasLocked;
        Round.IsLocked = roundWasLocked;
        Logger.Info("[METHADRENALINE] Bracelet playtest cleaned up.");
    }
}
