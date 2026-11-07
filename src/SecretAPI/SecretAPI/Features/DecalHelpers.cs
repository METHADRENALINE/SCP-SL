namespace SecretAPI.Features;

using System;
using Decals;
using InventorySystem.Items;
using InventorySystem.Items.Autosync;
using InventorySystem.Items.Firearms.Modules;
using Mirror;
using RelativePositioning;
using UnityEngine;
using Utils.Networking;

public static class DecalHelpers
{
    private static AutoSyncData? autoSyncData;

    public static AutosyncMessage GetDecalMessage(Vector3 position, Vector3 startPosition, DecalPoolType type)
    {
        RelativePosition hitPoint = new(position);
        RelativePosition startRaycastPoint = new(startPosition);

        AutoSyncData data = GetAutoSyncData();

        using NetworkWriterPooled writer = NetworkWriterPool.Get();
        writer.WriteByte(data.SubcomponentIndex);
        writer.WriteSubheader(ImpactEffectsModule.RpcType.ImpactDecal);
        writer.WriteByte((byte)type);
        writer.WriteRelativePosition(hitPoint);
        writer.WriteRelativePosition(startRaycastPoint);

        return new AutosyncMessage(writer, new ItemIdentifier(data.ItemType, 0));
    }

    public static void SpawnDecalFromPosition(Vector3 position, Vector3 startPosition, DecalPoolType type = DecalPoolType.Blood)
        => GetDecalMessage(position, startPosition, type).SendToAuthenticated();

    public static void SpawnDecalFromNormal(Vector3 position, Vector3 normal, DecalPoolType type = DecalPoolType.Blood)
        => GetDecalMessage(position, position + normal, type).SendToAuthenticated();

    public static void SpawnDecalFromNormal(Vector3 position, Quaternion normal, DecalPoolType type = DecalPoolType.Blood)
        => GetDecalMessage(position, position + (normal * Vector3.forward), type).SendToAuthenticated();

    public static void SpawnDecalFromDirection(Vector3 position, Vector3 direction, DecalPoolType type = DecalPoolType.Blood)
        => GetDecalMessage(position, position - direction, type).SendToAuthenticated();

    public static void SpawnDecalFromDirection(Vector3 position, Quaternion direction, DecalPoolType type = DecalPoolType.Blood)
        => GetDecalMessage(position, position - (direction * Vector3.forward), type).SendToAuthenticated();

    private static AutoSyncData GetAutoSyncData()
    {
        if (autoSyncData != null)
            return autoSyncData;

        foreach (ModularAutosyncItem autoItem in ModularAutosyncItem.AllTemplates)
        {
            for (byte b = 0; b < autoItem.AllSubcomponents.Length; b++)
            {
                if (autoItem.AllSubcomponents[b] is not ImpactEffectsModule)
                    continue;

                return autoSyncData = new AutoSyncData(autoItem.ItemTypeId, b);
            }
        }

        throw new InvalidOperationException("Couldn't find the `InventorySystem.Items.Firearms.Modules.ImpactEffectsModule` in the any ModularAutosyncItem!");
    }

    private sealed record AutoSyncData(ItemType ItemType, byte SubcomponentIndex);
}
