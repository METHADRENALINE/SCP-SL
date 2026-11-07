using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using MEC;
using Methadrenaline.Server.Core;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal static class ParavozEnvironmentDiagnostics
{
    private static bool running;
    internal static string Run(ServerPlugin plugin)
    {
        if (running || Round.IsRoundInProgress || EventDirector.Participants.Any())
            return "Environment diagnostics require an empty waiting server.";
        running = true;
        Timing.RunCoroutine(Probe());
        return "Environment diagnostics started.";

        IEnumerator<float> Probe()
        {
            ReferenceHub? hub = null, runnerHub = null;
            GameObject? pane = null, wall = null;
            bool lobby = Round.IsLobbyLocked, locked = Round.IsLocked, completed = false;
            int passed = 0, failed = 0;
            void Check(string name, bool result)
            {
                if (result) passed++; else failed++;
                Logger.Info("[METHADRENALINE] Environment check " + name + ": " + (result ? "PASS" : "FAIL"));
            }
            try
            {
                Round.IsLobbyLocked = Round.IsLocked = true;
                hub = DummyUtils.SpawnDummy("EnvironmentProbe"); hub.authManager.UserId = "ID_Dummy";
                var carrier = Player.Get(hub)!; carrier.SetRole(RoleTypeId.ClassD);
                carrier.Position = new Vector3(0, 900, 0);
                hub.PlayerCameraReference.rotation = Quaternion.identity;
                plugin.Paravoz!.Abilities.Apply(carrier);
                yield return Timing.WaitForSeconds(.2f);
                Vector3 origin = ((IFpcRole)carrier.RoleBase).FpcModule.Position;
                pane = new GameObject("FlameWindowProbe");
                pane.layer = 14;
                pane.AddComponent<NetworkIdentity>();
                var collider = pane.AddComponent<BoxCollider>(); collider.size = new Vector3(1, 2, .05f);
                var native = pane.AddComponent<BreakableWindow>(); collider.enabled = true;
                var window = Window.Get(native)!;
                void Place(Vector3 delta) { pane.transform.position = origin + delta; Physics.SyncTransforms(); }
                void Fire() => plugin.Paravoz.Abilities.FireTick(carrier);
                Place(Vector3.forward * 6); Fire(); Check("glass_outside_five_meters_untouched", native.Health == 30);
                Place(Vector3.back * 3); Fire(); Check("glass_behind_carrier_untouched", native.Health == 30);
                Place(Vector3.forward * 3);
                wall = new GameObject("FlameWallProbe");
                wall.layer = Enumerable.Range(0, 32).First(layer => (PlayerRolesUtils.AttackMask & (1 << layer)) != 0);
                wall.transform.position = origin + Vector3.forward;
                wall.AddComponent<BoxCollider>().size = new Vector3(3, 3, .1f);
                Physics.SyncTransforms(); Fire(); Check("wall_blocks_glass_damage", native.Health == 30);
                wall.GetComponent<Collider>().enabled = false;
                var child = new GameObject("SecondCollider"); child.transform.SetParent(pane.transform, false);
                child.layer = 14; child.AddComponent<BoxCollider>().size = new Vector3(1, 2, .04f);
                Physics.SyncTransforms(); Fire();
                Check("glass_takes_one_native_hit_per_tick", native.Health == 15 && !native.NetworkIsBroken);
                Fire(); Check("glass_breaks_after_two_hits", native.NetworkIsBroken && native.Health == 0);
                Check("glass_keeps_carrier_attribution", native.LastAttacker.Hub == hub);
                plugin.Paravoz.Abilities.Forget(carrier.NetworkId);

                plugin.SiteMaps!.BeginRound();
                yield return Timing.WaitForSeconds(.3f);
                Check("normal_round_has_maps", Pickup.List.Count(p => plugin.SiteMaps.IsMap(p.Type, p.Serial)) == 15);
                Check("normal_round_map_equips", plugin.SiteMaps.GiveTo(carrier) == "Site-02 Map equipped.");
                ushort serial = carrier.CurrentItem!.Serial;
                runnerHub = DummyUtils.SpawnDummy("EnvironmentRunnerProbe"); runnerHub.authManager.UserId = "ID_Dummy";
                var runner = Player.Get(runnerHub)!; runner.SetRole(RoleTypeId.ClassD);
                plugin.PerkRounds!.Policy.BeginMode(() => CommunityRoundMode.Paravoz);
                plugin.Paravoz.BeginRound(new[] { carrier, runner }, carrier);
                plugin.SiteMaps.BeginRound();
                Check("paravoz_removes_existing_maps", !Pickup.List.Any(p => plugin.SiteMaps.IsMap(p.Type, p.Serial)) &&
                    !carrier.Items.Any(item => item.Serial == serial));
                Check("paravoz_blocks_map_command", plugin.SiteMaps.GiveTo(runner) == "Site-02 Maps are unavailable in Paravoz mode.");
                completed = true;
            }
            finally
            {
                if (!completed) Check("probe_completed", false);
                plugin.Paravoz?.Reset(); plugin.PerkRounds?.Reset(); plugin.SiteMaps?.Reset();
                if (pane) UnityEngine.Object.Destroy(pane);
                if (wall) UnityEngine.Object.Destroy(wall);
                if (hub) NetworkServer.Destroy(hub.gameObject);
                if (runnerHub) NetworkServer.Destroy(runnerHub.gameObject);
                Round.IsLobbyLocked = lobby; Round.IsLocked = locked; running = false;
                Logger.Info($"[METHADRENALINE] Environment diagnostics finished: {passed} PASS, {failed} FAIL.");
            }
        }
    }
}
