using System;
using System.Collections.Generic;
using System.Linq;
using CustomPlayerEffects;
using Interactables.Interobjects;
using LabApi.Features.Wrappers;
using MEC;
using PlayerRoles.FirstPersonControl;
using UnityEngine;
using NativeElevator = Interactables.Interobjects.ElevatorChamber;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal static class ElevatorDiagnostics
{
    internal static IEnumerator<float> Probe(ParavozController mode, Player carrier, Player human, Action<string, bool> check)
    {
        var lifts = NativeElevator.AllChambers.Where(c => c.IsReady && c.FloorDoors.Count >= 2 && c.ActiveLocksAnyDoors == 0).ToArray();
        if (lifts.Length == 0) { check("elevator_groups_available", false); yield break; }
        var levels = lifts.ToDictionary(c => c, c => c.DestinationLevel);
        var doors = lifts.SelectMany(c => c.FloorDoors).Distinct().ToArray();
        var targets = doors.ToDictionary(d => d, d => d.TargetState);
        var locks = doors.ToDictionary(d => d, d => d.ActiveLocks);
        var carrierPosition = carrier.Position;
        var humanPosition = human.Position;
        bool humanGod = human.IsGodModeEnabled;
        bool unmodified = true;
        bool arrivalDoorsOpen = true, departureDoorsClosed = true;
        var arrived = new HashSet<NativeElevator>();
        int samples = 0;
        var cabin = lifts.First(c => !ParavozBoundaries.SurfaceLift(c));
        try
        {
            foreach (var door in doors)
            {
                door.NetworkTargetState = false;
                mode.YieldDoor(carrier, door);
                unmodified &= !door.TargetState && door.ActiveLocks == locks[door];
                door.NetworkTargetState = targets[door];
            }
            check("elevator_empty_shafts_protected", unmodified);

            human.IsGodModeEnabled = true;
            Vector3 center = cabin.WorldspaceBounds.Bounds.center;
            carrier.Position = center + cabin.transform.right * 0.5f;
            human.Position = center - cabin.transform.right * 0.5f;
            yield return Timing.WaitForSeconds(0.3f);
            var carrierFpc = ((IFpcRole)carrier.RoleBase).FpcModule;
            var humanFpc = ((IFpcRole)human.RoleBase).FpcModule;
            check("elevator_passengers_attach_to_native_waypoint", carrierFpc.RelativePosition.WaypointId == cabin.Network_waypointId && humanFpc.RelativePosition.WaypointId == cabin.Network_waypointId);

            bool surfaceBlocked = true, interiorAllowed = true;
            foreach (var lift in lifts)
            {
                var panel = ElevatorPanel.AllPanels.First(p => p.AssignedChamber == lift);
                lift.ServerInteract(carrier.ReferenceHub, panel.ColliderId);
                if (ParavozBoundaries.SurfaceLift(lift))
                {
                    surfaceBlocked &= lift.DestinationLevel == levels[lift];
                    lift.ServerInteract(human.ReferenceHub, panel.ColliderId);
                }
                else interiorAllowed &= lift.DestinationLevel != levels[lift];
            }
            check("surface_elevator_buttons_reject_paravoz", lifts.Any(ParavozBoundaries.SurfaceLift) && surfaceBlocked);
            check("interior_elevator_buttons_accept_paravoz", interiorAllowed);
            check("surface_elevator_buttons_accept_humans", lifts.Where(ParavozBoundaries.SurfaceLift).All(c => c.DestinationLevel != levels[c]));
            float deadline = Time.realtimeSinceStartup + 60;
            bool carrierInside = true, humanInside = true;
            do
            {
                yield return Timing.WaitForSeconds(0.05f);
                Observe();
                Bounds bounds = cabin.WorldspaceBounds.Bounds;
                bounds.Expand(0.5f);
                carrierInside &= bounds.Contains(carrier.Position);
                humanInside &= bounds.Contains(human.Position);
            } while (arrived.Count < lifts.Length && Time.realtimeSinceStartup < deadline);
            check("elevator_all_outbound_trips_complete", arrived.Count == lifts.Length);
            check("elevator_carrier_stays_in_cabin", carrierInside);
            check("elevator_normal_passenger_stays_in_cabin", humanInside);
            check("elevator_native_arrival_doors_open", arrivalDoorsOpen);
            check("elevator_departure_doors_stay_closed", departureDoorsClosed);

            foreach (var lift in lifts) lift.ServerSetDestination(levels[lift], allowQueueing: false);
            yield return Timing.WaitForSeconds(0.1f);
            deadline = Time.realtimeSinceStartup + 60;
            do
            {
                yield return Timing.WaitForSeconds(0.05f);
                Observe();
            } while (lifts.Any(c => !c.IsReady) && Time.realtimeSinceStartup < deadline);
            check("elevator_all_return_trips_complete", lifts.All(c => c.IsReady && c.DestinationLevel == levels[c]));
            check("elevator_breach_never_changes_native_doors_or_locks", unmodified);
            check("elevator_paravoz_effects_preserved", mode.Abilities.Enabled(carrier) && carrier.GetEffect<MovementBoost>()!.Intensity >= 50 && !carrier.GetEffect<Invigorated>()!.IsEnabled);
            Logger.Info($"[METHADRENALINE] Elevator probe: {lifts.Length} groups, {doors.Length} landing doors, {samples} native-state observations; groups={string.Join(",", lifts.Select(c => c.AssignedGroup))}");
        }
        finally
        {
            carrier.Position = carrierPosition;
            human.Position = humanPosition;
            human.IsGodModeEnabled = humanGod;
            foreach (var lift in lifts) if (lift) lift.ServerSetDestination(levels[lift], allowQueueing: false);
            foreach (var door in doors) if (door) door.NetworkTargetState = targets[door];
        }

        void Observe()
        {
            foreach (var lift in lifts.Where(c => c.IsReady && c.DestinationLevel != levels[c]))
            {
                arrived.Add(lift);
                arrivalDoorsOpen &= lift.DestinationDoor.TargetState;
                departureDoorsClosed &= lift.PreviousDoor == lift.DestinationDoor || !lift.PreviousDoor.TargetState;
            }
            foreach (var door in doors)
            {
                bool opened = door.TargetState;
                ushort locked = door.ActiveLocks;
                mode.YieldDoor(carrier, door);
                unmodified &= door.TargetState == opened && door.ActiveLocks == locked;
                samples++;
            }
        }
    }
}
