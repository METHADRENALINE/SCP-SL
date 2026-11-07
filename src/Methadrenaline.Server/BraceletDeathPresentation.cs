using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mirror;
using PlayerRoles.Ragdolls;
using PlayerRoles.Spectating;
using PlayerStatsSystem;

namespace Methadrenaline.Server;

internal static class BraceletDeathPresentation
{
    internal const string DeathText = "Death was caused by the detonation of an explosive bracelet.";
    internal const string WearerBodyText = "Severe blast injuries are present. The explosion originated at the wrist.";
    internal const string UnknownDeathText = "The cause of death could not be determined.";
    internal const string BystanderBodyText = UnknownDeathText;
    private sealed class Cause(bool wearer) { internal readonly bool Wearer = wearer; }
    private static readonly ConditionalWeakTable<DamageHandlerBase, Cause> Causes = new();
    private static readonly FieldInfo SpectatorHandler = AccessTools.Field(typeof(SpectatorRole), "_damageHandler");
    private static readonly Dictionary<NetworkConnectionToClient, HashSet<BasicRagdoll>> Pending = new();
    [ThreadStatic] internal static ReferenceHub? Viewer;
    [ThreadStatic] internal static bool Exploding;

    internal static CustomReasonDamageHandler WearerDeath()
    {
        var handler = new CustomReasonDamageHandler(DeathText);
        Causes.Add(handler, new Cause(true));
        return handler;
    }
    internal static void MarkExplosion(DamageHandlerBase handler)
    {
        if (Exploding && handler is ExplosionDamageHandler && !Causes.TryGetValue(handler, out _)) Causes.Add(handler, new Cause(false));
    }
    internal static bool IsBracelet(DamageHandlerBase handler) => handler != null && Causes.TryGetValue(handler, out _);
    internal static DamageHandlerBase BodyHandler(DamageHandlerBase handler, ReferenceHub? viewer)
        => Causes.TryGetValue(handler, out var cause)
            ? new CustomReasonDamageHandler(Localizer.Text(viewer, cause.Wearer ? WearerBodyText : BystanderBodyText)) : handler;

    internal static bool WriteDeath(SpectatorRole spectator, NetworkWriter writer)
    {
        var handler = SpectatorHandler.GetValue(spectator) as DamageHandlerBase;
        if (handler == null || !Causes.TryGetValue(handler, out var cause)) return false;
        spectator.TryGetOwner(out var hub);
        new CustomReasonDamageHandler(Localizer.Text(hub, cause.Wearer ? DeathText : UnknownDeathText)).WriteDeathScreen(writer);
        SpectatorHandler.SetValue(spectator, null);
        return true;
    }

    internal static void QueueUpdate(NetworkIdentity identity, NetworkConnectionToClient connection)
    {
        var body = identity.GetComponent<BasicRagdoll>();
        if (body == null || !IsBracelet(body.Info.Handler)) return;
        if (!Pending.TryGetValue(connection, out var bodies)) Pending[connection] = bodies = new HashSet<BasicRagdoll>();
        bodies.Add(body);
    }
    internal static void Flush(NetworkConnectionToClient connection)
    {
        if (!Pending.TryGetValue(connection, out var bodies)) return;
        Pending.Remove(connection);
        foreach (var body in bodies) if (body != null && connection.observing.Contains(body.netIdentity)) SendBody(body, connection);
    }
    internal static void Refresh(ReferenceHub hub)
    {
        var connection = hub.connectionToClient;
        if (connection == null) return;
        foreach (var identity in connection.observing)
        {
            var body = identity.GetComponent<BasicRagdoll>();
            if (body != null && IsBracelet(body.Info.Handler)) SendBody(body, connection);
        }
    }
    private static void SendBody(BasicRagdoll body, NetworkConnectionToClient connection)
    {
        using var writer = NetworkWriterPool.Get();
        WriteBodyUpdate(writer, body, Localizer.Hub(connection));
        connection.Send(new EntityStateMessage { netId = body.netId, payload = writer.ToArraySegment() });
    }
    internal static void WriteBodyUpdate(NetworkWriter writer, BasicRagdoll body, ReferenceHub? viewer)
    {
        int component = Array.IndexOf(body.netIdentity.NetworkBehaviours, body);
        if (component < 0 || component >= 64) throw new InvalidOperationException("The ragdoll has no network component index.");
        Compression.CompressVarUInt(writer, 1UL << component);
        int marker = writer.Position;
        writer.WriteByte(0);
        int start = writer.Position;
        writer.WriteULong(0);
        writer.WriteULong(1);
        var previous = Viewer;
        try { Viewer = viewer; body.SerializeSyncVars(writer, true); }
        finally { Viewer = previous; }
        int end = writer.Position;
        writer.Position = marker;
        writer.WriteByte((byte)((end - start) & 0xff));
        writer.Position = end;
    }
    internal static void Reset() { Pending.Clear(); Viewer = null; Exploding = false; }
}

[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.DealDamage))]
internal static class BraceletExplosionCausePatch
{
    private static void Prefix(DamageHandlerBase handler) => BraceletDeathPresentation.MarkExplosion(handler);
}

[HarmonyPatch(typeof(SpectatorRole), nameof(SpectatorRole.WritePrivateSpawnData))]
internal static class BraceletDeathScreenPatch
{
    private static bool Prefix(SpectatorRole __instance, NetworkWriter writer) => !BraceletDeathPresentation.WriteDeath(__instance, writer);
}

[HarmonyPatch(typeof(NetworkServer), "SendSpawnMessage")]
internal static class BraceletBodyViewerPatch
{
    private static void Prefix(NetworkConnection conn, out ReferenceHub? __state)
    {
        __state = BraceletDeathPresentation.Viewer;
        BraceletDeathPresentation.Viewer = Localizer.Hub(conn);
    }
    private static Exception? Finalizer(Exception? __exception, ReferenceHub? __state)
    {
        BraceletDeathPresentation.Viewer = __state;
        return __exception;
    }
}

[HarmonyPatch(typeof(RagdollDataReaderWriter), nameof(RagdollDataReaderWriter.WriteRagdollData))]
internal static class BraceletBodyTextPatch
{
    private static void Prefix(ref RagdollData info)
    {
        var handler = BraceletDeathPresentation.BodyHandler(info.Handler, BraceletDeathPresentation.Viewer);
        if (!ReferenceEquals(handler, info.Handler)) info = new RagdollData(info.OwnerHub, handler, info.RoleType,
            info.StartRelativePosition, info.StartRelativeRotation, info.Scale, info.Nickname, info.CreationTime, info.Serial);
    }
}

[HarmonyPatch(typeof(NetworkServer), "SerializeForConnection")]
internal static class BraceletBodyUpdatePatch
{
    private static void Postfix(NetworkIdentity identity, NetworkConnectionToClient connection, NetworkWriter? __result)
    {
        if (__result != null) BraceletDeathPresentation.QueueUpdate(identity, connection);
    }
}

[HarmonyPatch(typeof(NetworkServer), "BroadcastToConnection")]
internal static class BraceletBodyUpdateFlushPatch
{
    private static void Postfix(NetworkConnectionToClient connection) => BraceletDeathPresentation.Flush(connection);
}
