using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LabApi.Features.Wrappers;
using MEC;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using RueI.API;
using RueI.API.Parsing;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal static class SiteMapOnlyDiagnostics
{
    private static bool running;
    internal static string Run(ServerPlugin plugin)
    {
        if (running || Round.IsRoundInProgress || System.Linq.Enumerable.Any(EventDirector.Participants))
            return "Site map diagnostics require an empty waiting server.";
        running = true; Timing.RunCoroutine(Probe()); return "Site map diagnostics started.";
        IEnumerator<float> Probe()
        {
            ReferenceHub? hub = null; bool lobby = Round.IsLobbyLocked, locked = Round.IsLocked;
            int passed = 0, failed = 0; bool completed = false;
            void Check(string name, bool result)
            { if (result) passed++; else failed++; Logger.Info("[METHADRENALINE] Map check " + name + ": " + (result ? "PASS" : "FAIL")); }
            string Hud(Player player, Type controller, string field)
            {
                var tag = AccessTools.Field(controller, field).GetValue(null);
                var entries = AccessTools.Field(typeof(RueDisplay), "elements").GetValue(RueDisplay.Get(player));
                object entry;
                try { entry = AccessTools.Property(entries.GetType(), "Item").GetValue(entries, new[] { tag }); }
                catch (TargetInvocationException exception) when (exception.InnerException is KeyNotFoundException) { return ""; }
                var element = AccessTools.Field(entry.GetType(), "Element").GetValue(entry);
                var parsed = AccessTools.Method(element.GetType(), "GetParsedData").Invoke(element, new object[] { player.ReferenceHub });
                return (string)(AccessTools.Property(typeof(ParsedData), "ParsedString")?.GetValue(parsed) ?? AccessTools.Field(typeof(ParsedData), "ParsedString").GetValue(parsed));
            }
            try
            {
                Round.IsLobbyLocked = Round.IsLocked = true;
                hub = DummyUtils.SpawnDummy("MapGeometryProbe"); hub.authManager.UserId = "ID_Dummy";
                var player = Player.Get(hub)!; player.SetRole(RoleTypeId.ClassD);
                plugin.SiteMaps!.BeginRound(); yield return Timing.WaitForSeconds(2);
                var basic = SiteMapDiagnostics.Probe(plugin.SiteMaps, plugin.Localization!, player, Hud, Check);
                while (basic.MoveNext()) yield return basic.Current;
                var floors = SiteMapFloorDiagnostics.Probe(plugin.Localization!, player, Check);
                while (floors.MoveNext()) yield return floors.Current;
                completed = true;
            }
            finally
            {
                if (!completed) Check("map_probe_completed", false);
                plugin.SiteMaps?.Reset(); if (hub) NetworkServer.Destroy(hub.gameObject);
                Round.IsLobbyLocked = lobby; Round.IsLocked = locked; running = false;
                Logger.Info($"[METHADRENALINE] Map diagnostics finished: {passed} PASS, {failed} FAIL.");
            }
        }
    }
}
