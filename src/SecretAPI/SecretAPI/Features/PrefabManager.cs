namespace SecretAPI.Features;

using System;
using System.Linq;
using AdminToys;
using Interactables.Interobjects;
using MapGeneration;
using Mirror;
using UnityEngine;

public static class PrefabManager
{
    private const string LczDoorName = "LCZ BreakableDoor";
    private const string HczDoorName = "HCZ BreakableDoor";
    private const string HczBulkDoorName = "HCZ BulkDoor";
    private const string EzDoorName = "EZ BreakableDoor";

    private const string EzArmCameraToyName = "EzArmCameraToy";
    private const string EzCameraToyName = "EzCameraToy";
    private const string LczCameraToyName = "LczCameraToy";
    private const string HczCameraToyName = "HczCameraToy";
    private const string SzCameraToyName = "SzCameraToy";

    public static ReferenceHub PlayerPrefab => PrefabStore<ReferenceHub>.Prefab;

    public static BasicDoor LczDoorPrefab => field ??= GetOrThrow<BasicDoor>(LczDoorName);

    public static BasicDoor HczDoorPrefab => field ??= GetOrThrow<BasicDoor>(HczDoorName);

    public static BasicDoor HczBulkDoorPrefab => field ??= GetOrThrow<BasicDoor>(HczBulkDoorName);

    public static BasicDoor EzDoorPrefab => field ??= GetOrThrow<BasicDoor>(EzDoorName);

    public static Scp079CameraToy EzArmCameraToyPrefab => field ??= GetOrThrow<Scp079CameraToy>(EzArmCameraToyName);

    public static Scp079CameraToy EzCameraToyPrefab => field ??= GetOrThrow<Scp079CameraToy>(EzCameraToyName);

    public static Scp079CameraToy LczCameraToyPrefab => field ??= GetOrThrow<Scp079CameraToy>(LczCameraToyName);

    public static Scp079CameraToy HczCameraToyPrefab => field ??= GetOrThrow<Scp079CameraToy>(HczCameraToyName);

    public static Scp079CameraToy SzCameraToyPrefab => field ??= GetOrThrow<Scp079CameraToy>(SzCameraToyName);

    private static T GetOrThrow<T>(string name)
        where T : NetworkBehaviour => PrefabStore<T>.AllComponentPrefabs.FirstOrDefault(c => c.name == name)
                                      ?? throw new InvalidOperationException($"[PrefabManager] Failed to get component ({typeof(T).Name}) by name {name} | Report this as a bug");
}
