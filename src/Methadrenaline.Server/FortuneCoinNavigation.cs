using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Interactables.Interobjects.DoorUtils;
using InventorySystem.Items.Keycards;
using InventorySystem;
using LabApi.Features.Enums;
using LabApi.Features.Wrappers;
using MapGeneration;
using PlayerRoles;
using UnityEngine;
using NativeKeycard = InventorySystem.Items.Keycards.KeycardItem;

namespace Methadrenaline.Server;

internal sealed class FortuneCoinNavigation
{
    private sealed class Passage(int to, Vector3 position)
    {
        internal readonly int To = to;
        internal readonly Vector3 Position = position;
        internal readonly Vector3 ArrivalPosition = position;
        internal readonly Door? Entry;
        internal readonly Door? Arrival;
        internal readonly Elevator? Lift;

        internal Passage(int to, Door entry, Door? arrival = null, Elevator? lift = null) : this(to, entry.Position)
        {
            Entry = entry;
            Arrival = arrival;
            ArrivalPosition = arrival?.Position ?? entry.Position;
            Lift = lift;
        }
    }

    internal sealed class Guidance(string speech, Pickup? target = null)
    {
        internal readonly string Speech = speech;
        internal readonly Pickup? Target = target;
    }

    private sealed class Access(DoorPermissionFlags[] cards, bool pass)
    {
        internal readonly DoorPermissionFlags[] Cards = cards;
        internal readonly bool Pass = pass;
    }

    private sealed class Card(Pickup pickup, int room, DoorPermissionFlags permissions, LockerChamber? locker)
    {
        internal readonly Pickup Pickup = pickup;
        internal readonly int Room = room;
        internal readonly DoorPermissionFlags Permissions = permissions;
        internal readonly LockerChamber? Locker = locker;
        internal bool IsPass => Pickup.Type == ItemType.SurfaceAccessPass;
        internal Vector3 Position => Locker?.Base.transform.position ?? Pickup.Position;
    }

    private sealed class Search(int rooms)
    {
        internal readonly float[] Costs = Enumerable.Repeat(float.PositiveInfinity, rooms * 2).ToArray();
        internal readonly Passage?[] First = new Passage?[rooms * 2];
        internal readonly Vector3[] Arrival = new Vector3[rooms * 2];

        internal int Best(int room) => Costs[room * 2] <= Costs[room * 2 + 1] ? room * 2 : room * 2 + 1;
    }

    private sealed class Goal(Card first, Passage? firstStep, Access access, Search route, float cost, Card last)
    {
        internal readonly Card First = first;
        internal readonly Passage? FirstStep = firstStep;
        internal readonly Access Access = access;
        internal readonly Search Route = route;
        internal readonly float Cost = cost;
        internal readonly Card Last = last;
    }

    private static readonly Dictionary<ushort, DoorPermissionFlags>? CustomPermissions =
        AccessTools.Field(typeof(CustomPermsDetail), "CustomPermissions")?.GetValue(null) as Dictionary<ushort, DoorPermissionFlags>;
    private readonly SingleUseKeycardItem? passTemplate;
    private readonly FortuneCoinText text = new();
    private readonly Dictionary<RoomIdentifier, string> signs;
    private readonly Room[] rooms;
    private readonly Dictionary<RoomIdentifier, int> ids;
    private readonly Dictionary<RoomIdentifier, (float Height, int Index)[]> landings = new();
    private readonly List<Passage>[] passages;
    private readonly Door[][] securityDoors;
    private readonly Dictionary<int, float> fights = new();
    private readonly int surface;

    internal FortuneCoinNavigation()
    {
        var facility = Room.List.Where(room => room.Zone is FacilityZone.LightContainment or FacilityZone.HeavyContainment
            or FacilityZone.Entrance or FacilityZone.Surface).ToArray();
        ids = facility.Select((room, index) => (room.Base, index)).ToDictionary(entry => entry.Base, entry => entry.index);
        var lifts = Elevator.List.Select(lift => (Lift: lift, Doors: lift.Doors.ToArray())).ToArray();
        rooms = SplitLiftFloors(facility, lifts.Select(entry => entry.Doors));
        passages = rooms.Select(_ => new List<Passage>()).ToArray();
        securityDoors = rooms.Select((room, index) => room.Doors.Where(door => (door.DoorName is
            DoorName.LczCheckpointA or DoorName.LczCheckpointB or DoorName.HczCheckpoint or DoorName.EzGateA or DoorName.EzGateB) &&
            door.Rooms.Count() == 1 && Index(room, door.Position) == index).ToArray()).ToArray();
        surface = Array.FindIndex(rooms, room => room.Zone == FacilityZone.Surface);
        signs = FortuneCoinSigns.Read();
        if (ItemType.SurfaceAccessPass.TryGetTemplate(out NativeKeycard template)) passTemplate = template as SingleUseKeycardItem;
        foreach (Door door in Door.List)
        {
            if (door is ElevatorDoor) continue;
            int[] neighbors = door.Rooms.Select(room => Index(room, door.Position)).Where(id => id >= 0).Distinct().ToArray();
            foreach (int from in neighbors)
                foreach (int to in neighbors)
                    if (from != to) passages[from].Add(new Passage(to, door));
        }
        for (int from = 0; from < facility.Length; from++)
            foreach (RoomIdentifier neighbor in facility[from].ConnectedRooms)
                if (ids.TryGetValue(neighbor, out int to) && from != to &&
                    !HasPhysicalPassage(facility[from], neighbor))
                {
                    Vector3 position = (rooms[from].Position + rooms[to].Position) * .5f;
                    passages[Index(rooms[from], position)].Add(new Passage(Index(rooms[to], position), position));
                }
        foreach (var entry in lifts)
        {
            var floors = entry.Doors.SelectMany(door => door.Rooms
                    .Select(room => (Door: (Door)door, Room: Index(room, door.Position))))
                .Where(floor => floor.Room >= 0).Distinct().ToArray();
            foreach (var from in floors)
                foreach (var to in floors)
                    if (from.Door != to.Door && from.Room != to.Room)
                        passages[from.Room].Add(new Passage(to.Room, from.Door, to.Door, entry.Lift));
        }
    }

    private Room[] SplitLiftFloors(Room[] facility, IEnumerable<ElevatorDoor[]> lifts)
    {
        var heights = new Dictionary<Room, List<float>>();
        foreach (var doors in lifts)
            foreach (var group in doors.SelectMany(door => door.Rooms
                .Where(room => room != null && ids.ContainsKey(room.Base))
                .Select(room => (Room: room, Y: door.Position.y))).GroupBy(stop => stop.Room))
            {
                var stops = group.Select(stop => stop.Y).ToArray();
                if (stops.Max() - stops.Min() < 3f) continue;
                if (!heights.TryGetValue(group.Key, out var all)) heights[group.Key] = all = new List<float>();
                all.AddRange(stops);
            }

        var result = new List<Room>(facility);
        foreach (var entry in heights)
        {
            var levels = new List<float>();
            foreach (float height in entry.Value.OrderBy(y => y))
                if (levels.Count == 0 || height - levels[levels.Count - 1] >= 3f) levels.Add(height);
            float main = levels.OrderBy(y => Mathf.Abs(y - entry.Key.Position.y)).First();
            var indices = new (float Height, int Index)[levels.Count];
            for (int i = 0; i < levels.Count; i++)
            {
                bool primary = levels[i] == main;
                indices[i] = (levels[i], primary ? ids[entry.Key.Base] : result.Count);
                if (!primary) result.Add(entry.Key);
            }
            landings.Add(entry.Key.Base, indices);
        }
        return result.ToArray();
    }

    private bool HasPhysicalPassage(Room from, RoomIdentifier to)
    {
        if (!landings.TryGetValue(from.Base, out var floors))
            return passages[ids[from.Base]].Any(passage => rooms[passage.To].Base == to);
        foreach (var floor in floors)
            if (passages[floor.Index].Any(passage => rooms[passage.To].Base == to)) return true;
        return false;
    }

    internal void Hurt(Player victim, Player attacker)
    {
        if (victim == attacker || !victim.IsAlive || !attacker.IsAlive) return;
        int room = Index(victim.Room, victim.Position);
        if (room >= 0) fights[room] = Time.unscaledTime + 9f;
    }

    internal Guidance NextStep(Player viewer, Room currentRoom, bool includeSituation)
    {
        int start = Index(currentRoom, viewer.Position);
        if (start < 0) return new Guidance("Я потеряла тебя. Вернись в комплекс.");
        var danger = new float[rooms.Length];
        var scps = new List<(int Room, Vector3 Position, string Role)>();
        foreach (Player player in Player.ReadyList)
        {
            if (player == viewer || !player.IsAlive || player.IsHost || player.IsTutorial ||
                !IsHostile(viewer, player) || PocketDimension.IsPlayerInside(player)) continue;
            int room = Index(player.Room, player.Position);
            if (room < 0) continue;
            bool isScp = player.IsSCP || IsTrain(player);
            danger[room] += isScp ? 100 : 28;
            if (isScp) scps.Add((room, player.Position, SpeakName(RoleName(player, text))));
        }
        float now = Time.unscaledTime;
        foreach (var fight in fights)
            if (fight.Value > now) danger[fight.Key] += 30;
        if (ServerPlugin.Instance?.Blackout.Active != true)
            for (int i = 0; i < rooms.Length; i++)
                if (rooms[i].Name == MapGeneration.RoomName.HczTesla) danger[i] += 18;

        var plan = Plan(viewer, start, danger, scps);
        if (plan.Warning != null) return new Guidance(plan.Warning + " " + plan.Step, plan.Target);
        if (!includeSituation) return new Guidance(plan.Step, plan.Target);
        var threat = scps.Where(scp => rooms[scp.Room].Zone == currentRoom.Zone)
            .OrderBy(scp => (scp.Position - viewer.Position).sqrMagnitude).FirstOrDefault();
        if (threat.Role == null) return new Guidance(plan.Step, plan.Target);
        string warning = $"В этой зоне {threat.Role}.";
        if (threat.Room == start) warning = $"Осторожно. Здесь {threat.Role}.";
        else if ((threat.Position - viewer.Position).sqrMagnitude < 18 * 18)
            warning = $"Рядом {threat.Role}.";
        return new Guidance(warning + " " + plan.Step, plan.Target);
    }

    private (string Step, Pickup? Target, string? Warning) Plan(Player viewer, int start, float[] danger,
        List<(int Room, Vector3 Position, string Role)> scps)
    {
        if (surface < 0 || AlphaWarheadController.Detonated && start != surface)
            return ("Здесь выхода уже нет.", null, null);
        if (start == surface)
        {
            Vector3 closest = Escape.EscapeZones.Select(zone => zone.ClosestPoint(viewer.Position))
                .DefaultIfEmpty(Escape.DefaultEscapeZone.ClosestPoint(viewer.Position))
                .OrderBy(point => (point - viewer.Position).sqrMagnitude).First();
            return ((closest - viewer.Position).sqrMagnitude < 1
                ? "Ты у точки эвакуации. Проходи дальше."
                : $"Точка эвакуации {Direction(viewer, closest)}. Иди вдоль дороги.", null, null);
        }

        var items = viewer.Items.OfType<LabApi.Features.Wrappers.KeycardItem>().ToArray();
        var access = new Access(items.Where(card => card.Type != ItemType.SurfaceAccessPass && card.Permissions != 0)
            .Select(card => card.Permissions).Distinct().ToArray(),
            items.Any(card => card.Type == ItemType.SurfaceAccessPass && card.Base is SingleUseKeycardItem pass &&
                securityDoors.SelectMany(doors => doors).Any(door => IsGate(door) && pass.GetPermissions(door.Base) != 0)));
        var current = Find(start, viewer.Position, viewer, access, danger);
        if (current.First[current.Best(surface)] is { } direct) return (Step(viewer, start, direct), null, null);

        if (!viewer.IsDisarmed)
        {
            var loot = ReadLoot();
            var selected = ChooseLoot(viewer, current, access, danger, scps, loot);
            if (selected != null)
            {
                var card = selected.First;
                string label = CardName(card.Pickup.Type);
                string instruction = selected.FirstStep == null
                    ? PickupStep(label, card.Locker is { IsOpen: false }, viewer.Position.y, card.Position.y,
                        Direction(viewer, card.Position), viewer.IsInventoryFull)
                    : $"Пойдём за {CardDestination(card.Pickup.Type)}. {Step(viewer, start, selected.FirstStep)}";
                string? threat = LootThreat(card, scps);
                string? warning = threat == null ? null : $"Осторожно. Рядом с { (card.IsPass ? "пропуском" : "картой") } {threat}.";
                return (instruction, card.Pickup, warning);
            }
        }

        int machine = Array.FindIndex(rooms, room => room.Name == MapGeneration.RoomName.Lcz914);
        bool machineReachable = machine >= 0 && !float.IsPositiveInfinity(current.Costs[current.Best(machine)]);
        bool hasGateCard = access.Cards.Any(card => (card & DoorPermissionFlags.ExitGates) != 0);
        bool canUseMachine = access.Cards.Any(card => (card & DoorPermissionFlags.ContainmentLevelOne) != 0);
        if (rooms[start].Zone == FacilityZone.LightContainment && canUseMachine && !hasGateCard && !viewer.IsDisarmed && machineReachable)
            return (machine == start
                ? "Положи карту во входную кабину SCP девятьсот четырнадцать и выбери тонкую настройку."
                : Step(viewer, start, current.First[current.Best(machine)]!), null, null);

        int exit = ReachableExit(start, current);
        if (exit != start && current.First[current.Best(exit)] is { } corridor) return (Step(viewer, start, corridor), null, null);
        if (viewer.IsDisarmed) return ("Дальше нужен конвоир. Пусть откроет дверь.", null, null);
        if (hasGateCard || access.Pass) return ("Допуск есть, но путь пока перекрыт.", null, null);
        return (rooms[start].Zone == FacilityZone.Entrance
            ? "Для ворот нужна карта с допуском к выходу. Доступной поблизости не вижу."
            : "Доступной карты для следующего КПП не вижу.", null, null);
    }

    private Goal? ChooseLoot(Player viewer, Search current, Access held, float[] danger,
        List<(int Room, Vector3 Position, string Role)> scps, List<Card> loot)
    {
        Goal? best = null, fallback = null;
        float bestCost = float.PositiveInfinity, bestProgress = 0;
        int startingZone = FurthestZone(current);
        var frontier = new List<Goal>();
        var seen = new HashSet<(Pickup, bool, string)>();
        int searches = 0;
        void Expand(Search path, Access access, Goal? previous, int limit)
        {
            var candidates = loot.Where(card => (previous == null || card.Pickup != previous.First.Pickup && card.Pickup != previous.Last.Pickup) &&
                    (card.IsPass ? !access.Pass && !access.Cards.Any(HasExitAccess) : !access.Cards.Any(permissions =>
                        (permissions & card.Permissions) == card.Permissions)))
                .Select(card => (Card: card, State: path.Best(card.Room), Risk: LootThreat(card, scps) == null ? 0 : 140))
                .Where(entry => !float.IsPositiveInfinity(path.Costs[entry.State]))
                .OrderBy(entry => path.Costs[entry.State] + entry.Risk);
            int expanded = 0;
            foreach (var entry in candidates)
            {
                if (expanded >= limit || searches >= 16) break;
                var card = entry.Card;
                bool pass = (entry.State & 1) != 0;
                if (card.Locker is { IsOpen: false } locker &&
                    !access.Cards.Any(permissions => locker.Base.PermissionsPolicy.CheckPermissions(permissions)) &&
                    locker.RequiredPermissions != 0 && !viewer.IsBypassEnabled) continue;
                var before = new Access(access.Cards, pass);
                if (BehindLockedDoor(card, path.Arrival[entry.State], viewer, before)) continue;
                var after = new Access(card.IsPass ? access.Cards : access.Cards.Concat(new[] { card.Permissions }).ToArray(), pass || card.IsPass);
                string signature = string.Join(",", after.Cards.OrderBy(level => level).Select(level => (ushort)level));
                if (!seen.Add((card.Pickup, after.Pass, signature))) continue;
                expanded++;
                searches++;
                var remaining = Find(card.Room, card.Position, viewer, after, danger);
                float cost = (previous?.Cost ?? 0) + path.Costs[entry.State] + entry.Risk + 6;
                var candidate = new Goal(previous?.First ?? card, previous == null ? path.First[entry.State] : previous.FirstStep,
                    after, remaining, cost, card);
                float finish = cost + remaining.Costs[remaining.Best(surface)];
                if (finish < bestCost)
                {
                    best = candidate;
                    bestCost = finish;
                }
                int reached = FurthestZone(remaining);
                float progress = (reached - startingZone) * 1000 - cost;
                if (reached > startingZone && reached > ZoneLevel(rooms[card.Room].Zone) && progress > bestProgress)
                {
                    fallback = candidate;
                    bestProgress = progress;
                }
                if (cost < bestCost) frontier.Add(candidate);
            }
        }

        Expand(current, held, null, 8);
        for (int depth = 1; depth <= 2 && frontier.Count != 0 && searches < 16; depth++)
        {
            var paths = frontier.OrderBy(goal => goal.Cost).Take(4).ToArray();
            frontier.Clear();
            foreach (var goal in paths)
                if (goal.Cost < bestCost) Expand(goal.Route, goal.Access, goal, depth == 1 ? 2 : 1);
        }
        return best ?? fallback;
    }

    private static bool HasExitAccess(DoorPermissionFlags permissions) => (permissions & DoorPermissionFlags.ExitGates) != 0;

    private string? LootThreat(Card card, List<(int Room, Vector3 Position, string Role)> scps)
    {
        foreach (var scp in scps)
            if (scp.Room == card.Room || rooms[scp.Room].Zone == rooms[card.Room].Zone &&
                (scp.Position - card.Position).sqrMagnitude <= 16 * 16) return scp.Role;
        return null;
    }

    private int FurthestZone(Search path)
    {
        int furthest = -1;
        for (int i = 0; i < rooms.Length; i++)
            if (!float.IsPositiveInfinity(path.Costs[path.Best(i)]))
                furthest = Math.Max(furthest, ZoneLevel(rooms[i].Zone));
        return furthest;
    }

    private int ReachableExit(int start, Search current)
    {
        int selected = start;
        int bestZone = -1;
        float bestCost = float.PositiveInfinity;
        for (int i = 0; i < rooms.Length; i++)
        {
            float cost = current.Costs[current.Best(i)];
            if (float.IsPositiveInfinity(cost) || rooms[i].Name is not
                (MapGeneration.RoomName.LczCheckpointA or MapGeneration.RoomName.LczCheckpointB or
                 MapGeneration.RoomName.HczCheckpointToEntranceZone or MapGeneration.RoomName.EzGateA or MapGeneration.RoomName.EzGateB))
                continue;
            int zone = ZoneLevel(rooms[i].Zone);
            if (zone < ZoneLevel(rooms[start].Zone) || zone < bestZone ||
                zone == bestZone && cost >= bestCost) continue;
            selected = i;
            bestZone = zone;
            bestCost = cost;
        }
        return selected;
    }

    private List<Card> ReadLoot()
    {
        var found = new List<Card>();
        var stored = new HashSet<InventorySystem.Items.Pickups.ItemPickupBase>();
        foreach (var locker in Locker.List)
            foreach (var chamber in locker.Chambers)
                foreach (var item in chamber.Base.Content)
                {
                    if (!item || item is not InventorySystem.Items.Keycards.KeycardPickup || !stored.Add(item)) continue;
                    var pickup = Pickup.Get(item);
                    if (pickup != null) AddCard(pickup, chamber);
                }
        foreach (var pickup in LabApi.Features.Wrappers.KeycardPickup.List)
            if (!stored.Contains(pickup.Base)) AddCard(pickup, null);
        return found;

        void AddCard(Pickup pickup, LockerChamber? locker)
        {
            if (pickup.IsDestroyed || pickup.IsInUse || pickup.IsLocked && locker == null ||
                !pickup.Type.TryGetTemplate(out NativeKeycard template) ||
                ServerPlugin.Instance?.SiteMaps?.IsMap(pickup.Type, pickup.Serial) == true) return;
            Vector3 position = locker?.Base.transform.position ?? pickup.Position;
            int room = Index(locker != null ? Room.GetRoomAtPosition(position) : pickup.Room, position);
            if (room < 0) return;
            var perms = template is SingleUseKeycardItem ? DoorPermissionFlags.ExitGates :
                template.Customizable && CustomPermissions != null && CustomPermissions.TryGetValue(pickup.Serial, out var custom)
                    ? custom : template.GetPermissions(null);
            const DoorPermissionFlags escape = DoorPermissionFlags.ExitGates | DoorPermissionFlags.Checkpoints | DoorPermissionFlags.ContainmentLevelOne;
            if ((perms & escape) != 0) found.Add(new Card(pickup, room, perms, locker));
        }
    }

    private static string CardName(ItemType type) => type switch
    {
        ItemType.SurfaceAccessPass => "Одноразовый пропуск",
        ItemType.KeycardZoneManager => "Бирюзовая карта",
        ItemType.KeycardScientist => "Карта учёного",
        ItemType.KeycardFacilityManager => "Карта управляющего комплексом",
        ItemType.KeycardO5 => "Карта совета о пять",
        ItemType.KeycardMTFPrivate or ItemType.KeycardMTFOperative or ItemType.KeycardMTFCaptain => "Синяя карта",
        _ => "Ключ-карта"
    };

    private static string CardDestination(ItemType type) => type switch
    {
        ItemType.SurfaceAccessPass => "одноразовым пропуском",
        ItemType.KeycardZoneManager => "бирюзовой картой",
        ItemType.KeycardScientist => "картой учёного",
        ItemType.KeycardFacilityManager => "картой управляющего комплексом",
        ItemType.KeycardO5 => "картой совета о пять",
        ItemType.KeycardMTFPrivate or ItemType.KeycardMTFOperative or ItemType.KeycardMTFCaptain => "синей картой",
        _ => "ключ-картой"
    };

    internal static string PickupStep(string name, bool inLocker, float playerY, float targetY,
        string direction, bool fullInventory)
    {
        string action = fullInventory ? "Сначала освободи место." : "Забери.";
        string where = inLocker ? "в шкафчике" : "здесь";
        float height = targetY - playerY;
        if (height >= 1.8f) return $"{name} выше тебя. " + (fullInventory ? action : "Поднимись и забери.");
        if (height <= -2.2f) return $"{name} ниже тебя. " + (fullInventory ? action : "Спустись и забери.");
        return $"{name} {where}, {direction}. {action}";
    }

    private bool BehindLockedDoor(Card card, Vector3 from, Player viewer, Access access)
    {
        foreach (var door in rooms[card.Room].Doors)
        {
            bool pass = access.Pass;
            if (door.Rooms.Count() == 1 && Index(rooms[card.Room], door.Position) == card.Room &&
                (card.Position - door.Position).sqrMagnitude < 196 &&
                Crosses(door, from, card.Position) && !CanUse(door, viewer, access.Cards, ref pass)) return true;
        }
        return false;
    }

    private Search Find(int start, Vector3 origin, Player viewer, Access access, float[] danger)
    {
        var path = new Search(rooms.Length);
        int initial = start * 2 + (access.Pass ? 1 : 0);
        path.Costs[initial] = 0;
        path.Arrival[initial] = origin;
        var pending = new SortedSet<(float Cost, int State)> { (0, initial) };
        while (pending.Count != 0)
        {
            var node = pending.Min;
            pending.Remove(node);
            int room = node.State / 2;
            if (room == surface) continue;
            foreach (var passage in passages[room])
            {
                bool pass = (node.State & 1) != 0;
                Vector3 point = path.Arrival[node.State], target = passage.Position;
                if (passage.Lift?.DynamicAdminLock == true ||
                    !CanCrossSecurity(room, passage, point, node.State == initial, viewer, access.Cards, ref pass) ||
                    passage.Entry != null && !CanUse(passage.Entry, viewer, access.Cards, ref pass) ||
                    passage.Arrival != null && !CanUse(passage.Arrival, viewer, access.Cards, ref pass)) continue;
                float length = Mathf.Clamp(Vector3.Distance(point, target), 2, 35);
                float backtrack = ZoneLevel(rooms[room].Zone) > ZoneLevel(rooms[passage.To].Zone) ? 85 : 0;
                float cost = node.Cost + length + danger[passage.To] + (passage.Lift == null ? 4 : 28) + backtrack;
                int next = passage.To * 2 + (pass ? 1 : 0);
                if (cost >= path.Costs[next]) continue;
                pending.Remove((path.Costs[next], next));
                path.Costs[next] = cost;
                path.First[next] = path.First[node.State] ?? passage;
                path.Arrival[next] = passage.ArrivalPosition;
                pending.Add((cost, next));
            }
        }
        return path;
    }

    private bool CanCrossSecurity(int room, Passage passage, Vector3 from, bool initial,
        Player viewer, DoorPermissionFlags[] cards, ref bool pass)
    {
        var source = rooms[room];
        var destination = rooms[passage.To];
        bool checkpoint = source.Name == MapGeneration.RoomName.HczCheckpointToEntranceZone &&
            destination.Name == MapGeneration.RoomName.HczCheckpointToEntranceZone && source.Zone != destination.Zone;
        bool gateLift = passage.Lift != null && destination.Zone == FacilityZone.Surface;
        bool lightLift = passage.Lift != null && source.Zone == FacilityZone.LightContainment;
        foreach (var door in securityDoors[room])
        {
            bool mandatory = !initial && (gateLift && IsGate(door) || lightLift && door.DoorName is
                DoorName.LczCheckpointA or DoorName.LczCheckpointB);
            if ((mandatory || checkpoint || Crosses(door, from, passage.Position)) &&
                !CanUse(door, viewer, cards, ref pass)) return false;
        }
        if (checkpoint && source.Zone == FacilityZone.Entrance)
            foreach (var door in securityDoors[passage.To])
                if (!CanUse(door, viewer, cards, ref pass)) return false;
        return true;
    }

    private static int ZoneLevel(FacilityZone zone) => zone switch
    {
        FacilityZone.LightContainment => 0,
        FacilityZone.HeavyContainment => 1,
        FacilityZone.Entrance => 2,
        _ => 3
    };

    private static bool Crosses(Door door, Vector3 from, Vector3 to)
    {
        Vector3 normal = door.Rotation * Vector3.forward;
        Vector3 center = door.Position;
        return Vector3.Dot(normal, from - center) * Vector3.Dot(normal, to - center) < -.01f;
    }

    private bool CanUse(Door door, Player viewer, DoorPermissionFlags[] cards, ref bool pass)
    {
        if (door.IsDestroyed) return false;
        if (door is BreakableDoor { IsBroken: true } || door.IsOpened && door.ExactState >= .85f || viewer.IsBypassEnabled) return true;
        if (door.IsLocked && (DoorLockUtils.GetMode(door.LockReason) & DoorLockMode.CanOpen) == 0) return false;
        var policy = door.Base.PermissionsPolicy;
        if (policy.RequiredPermissions == 0) return true;
        if (viewer.IsDisarmed) return false;
        foreach (var card in cards)
            if (policy.CheckPermissions(card)) return true;
        if (!pass || !IsGate(door) || passTemplate == null || passTemplate.GetPermissions(door.Base) == 0) return false;
        pass = false;
        return true;
    }

    private static bool IsGate(Door door) => door.DoorName is DoorName.EzGateA or DoorName.EzGateB;

    private int Index(Room? room, Vector3 position)
    {
        if (room == null || !ids.TryGetValue(room.Base, out int id)) return -1;
        if (!landings.TryGetValue(room.Base, out var floors)) return id;
        float nearest = float.PositiveInfinity;
        foreach (var floor in floors)
        {
            float distance = Mathf.Abs(floor.Height - position.y);
            if (distance >= nearest) continue;
            id = floor.Index;
            nearest = distance;
        }
        return id;
    }
    private string Step(Player viewer, int room, Passage passage)
    {
        string direction = Direction(viewer, passage.Position);
        var target = rooms[passage.To];
        if (passage.Lift is { } lift)
        {
            bool upward = passage.ArrivalPosition.y > passage.Position.y;
            string stop = rooms[room].Base == target.Base
                ? upward ? "на верхний этаж" : "на нижний этаж"
                : target.Zone == FacilityZone.Surface ? "на поверхность" : $"в зону {ZoneName(target.Zone)}";
            if (lift.WorldSpaceRelativeBounds.Bounds.Contains(viewer.Position))
            {
                if (!lift.IsReady) return "Лифт уже в пути. Дождись остановки.";
                return $"Нажми кнопку в кабине. Тебе {stop}.";
            }
            var gate = securityDoors[room].FirstOrDefault(door => !door.IsOpened &&
                Crosses(door, viewer.Position, passage.Position));
            if (gate != null)
            {
                string gateDirection = Direction(viewer, gate.Position);
                if (IsGate(gate) && !viewer.Items.OfType<LabApi.Features.Wrappers.KeycardItem>()
                        .Any(card => card.Type != ItemType.SurfaceAccessPass && gate.Base.PermissionsPolicy.CheckPermissions(card.Permissions)) &&
                    viewer.Items.Any(item => item.Type == ItemType.SurfaceAccessPass))
                    return $"Приложи одноразовый пропуск к воротам {gateDirection}. Дальше садись в лифт.";
                return $"Открой проход {gateDirection}, дальше садись в лифт.";
            }
            return lift.IsReady && lift.CurrentDestination == passage.Entry && passage.Entry?.IsOpened == true
                ? $"Входи в лифт {direction}. Тебе {stop}."
                : $"Вызови лифт {direction}. Тебе {stop}.";
        }
        if (signs.TryGetValue(target.Base, out string code))
            return $"Дверь {direction}. На табличке {FortuneCoinSigns.Pronounce(code)}.";
        if (target.Name == MapGeneration.RoomName.HczTesla && ServerPlugin.Instance?.Blackout.Active != true)
            return $"Проход {direction}. Там Тесла. Пропусти разряд.";
        string destination = SpeakName(text[SiteMapNames.Room(target.Base)]);
        string opening = passage.Entry == null ? "Проход" : "Дверь";
        return $"{opening} {direction}. Дальше {destination}.";
    }

    private static string ZoneName(FacilityZone zone) => zone switch
    {
        FacilityZone.LightContainment => "лёгкого содержания",
        FacilityZone.HeavyContainment => "тяжёлого содержания",
        FacilityZone.Entrance => "входа",
        _ => "комплекса"
    };

    private static string SpeakName(string name) => name.Replace("SCP-", "эс си пи ").Replace("SCP ", "эс си пи ");

    private string Direction(Player viewer, Vector3 point)
    {
        Vector3 forward = Quaternion.Euler(0, viewer.Camera.eulerAngles.y, 0) * Vector3.forward;
        Vector3 target = Vector3.ProjectOnPlane(point - viewer.Position, Vector3.up);
        float angle = Vector3.SignedAngle(forward, target, Vector3.up);
        return text[Mathf.Abs(angle) >= 145 ? "behind you" : angle >= 39 ? "on your right" :
            angle <= -39 ? "on your left" : "ahead of you"];
    }

    private static bool IsTrain(Player player) => ServerPlugin.Instance?.Paravoz?.IsParavoz(player) == true;
    private static bool IsHostile(Player viewer, Player other)
    {
        if (IsTrain(other) || other.IsSCP) return true;
        if (ServerPlugin.Instance?.Paravoz?.Active == true || ServerPlugin.Instance?.Escort.Active == true) return false;
        bool classD = viewer.Role == RoleTypeId.ClassD || viewer.IsChaos;
        bool otherClassD = other.Role == RoleTypeId.ClassD || other.IsChaos;
        return classD != otherClassD;
    }

    private static string RoleName(Player player, FortuneCoinText text)
    {
        if (IsTrain(player)) return text["Paravoz"];
        if (ServerPlugin.Instance?.Paravoz?.IsHunter(player) == true) return text["Paravoz Hunter"];
        if (ServerPlugin.Instance?.Escort.IsGuard(player) == true) return text["Escort"];
        return text[player.RoleBase.RoleName];
    }
}
