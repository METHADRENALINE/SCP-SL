using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using InventorySystem;
using InventorySystem.Items.Keycards;
using Methadrenaline.Server.Core;
using Mirror;
using NativeCard = InventorySystem.Items.Keycards.KeycardItem;

namespace Methadrenaline.Server;

// Translate custom names in the Task Force card payload for each recipient.
internal static class CustomCardLocalization
{
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "Bolt of Darkness", "Curse of Pain", "Elemental Bolt", "Fire Arrow", "Fireball", "Ice Bolt", "Light Arrow", "Magic Missile", "Orb of Nature", "Ray of Darkness", "Summon Altar", "Summon Arcane Golem", "Summon Pylon", "Summon Rock Golem", "Summon Thorn Shooter", "Thorn Shot", "Thorn Volley"
    };
    private static Dictionary<ushort, ArraySegment<byte>> Database => (Dictionary<ushort, ArraySegment<byte>>)AccessTools.Field(typeof(KeycardDetailSynchronizer), "Database").GetValue(null)!;
    private static bool warned;

    private static bool ShouldLocalize(ArraySegment<byte> payload)
    {
        if (payload.Count < 2) return false;
        using var reader = NetworkReaderPool.Get(payload);
        try
        {
            string name = reader.ReadString() ?? "";
            return name == SiteMapController.CardName || Names.Contains(name);
        }
        catch (System.IO.EndOfStreamException) { return false; }
        catch (ArgumentException) { return false; }
    }

    internal static ArraySegment<byte> Translate(ArraySegment<byte> payload, ServerLanguage language)
    {
        if (language == ServerLanguage.English || !ShouldLocalize(payload)) return payload;
        if (!ItemType.KeycardCustomTaskForce.TryGetTemplate(out NativeCard template)) return payload;
        using var reader = NetworkReaderPool.Get(payload);
        using var writer = NetworkWriterPool.Get();
        foreach (var detail in template.Details.OfType<SyncedDetail>())
        {
            if (detail is CustomItemNameDetail || detail is NametagDetail)
                writer.WriteString(Localizer.Current!.Catalog.Translate(reader.ReadString(), language));
            else if (detail is CustomPermsDetail)
            {
                writer.WriteUShort(reader.ReadUShort());
                writer.WriteColor32Nullable(reader.ReadColor32Nullable());
            }
            else if (detail is CustomTintDetail) writer.WriteColor32(reader.ReadColor32());
            else if (detail is CustomRankDetail || detail is CustomWearDetail) writer.WriteByte(reader.ReadByte());
            else if (detail is CustomSerialNumberDetail) writer.WriteString(reader.ReadString());
            else throw new InvalidOperationException("Unsupported custom keycard detail: " + detail.GetType().Name);
        }
        if (reader.Remaining != 0) throw new InvalidOperationException("Unexpected custom keycard payload layout.");
        return new ArraySegment<byte>(writer.ToArray());
    }

    internal static void Resend(ReferenceHub hub)
    {
        if (hub.connectionToClient == null) return;
        foreach (var entry in Database.Where(e => ShouldLocalize(e.Value)))
            hub.connectionToClient.Send(new KeycardDetailSynchronizer.DetailsSyncMsg { Serial = entry.Key, Payload = Translate(entry.Value, Localizer.Current!.Language(hub)) });
    }

    [HarmonyPatch(typeof(KeycardDetailSynchronizer), "ServerAddDatabaseEntry")]
    private static class AddCardPatch
    {
        private static bool Prefix(ushort serial, ArraySegment<byte> entry)
        {
            if (Localizer.Current == null || !ShouldLocalize(entry)) return true;
            // Prepare and validate all variants before touching the native database.
            ArraySegment<byte> uk, ru;
            try
            {
                uk = Translate(entry, ServerLanguage.Ukrainian);
                ru = Translate(entry, ServerLanguage.Russian);
            }
            catch (Exception error)
            {
                if (!warned) LabApi.Features.Console.Logger.Error("[METHADRENALINE] Custom keycard localization failed: " + error);
                warned = true;
                return true;
            }
            Database.Add(serial, entry);
            foreach (var connection in NetworkServer.connections.Values)
            {
                var language = Localizer.Current.Language(Localizer.Hub(connection));
                connection.Send(new KeycardDetailSynchronizer.DetailsSyncMsg { Serial = serial, Payload = language == ServerLanguage.Ukrainian ? uk : language == ServerLanguage.Russian ? ru : entry });
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(KeycardDetailSynchronizer), "OnPlayerConnected")]
    private static class JoinPatch
    {
        private static void Postfix(ReferenceHub hub)
        {
            if (Localizer.Current != null && Localizer.Current.Language(hub) != ServerLanguage.English) Resend(hub);
        }
    }
}
