using MapGeneration;

namespace Methadrenaline.Server;

internal static class SiteMapNames
{
    internal static string MapLabel(RoomIdentifier room, System.Func<string, string> localize)
    {
        string name = Room(room);
        // SCP designations and the signs already printed above LCZ doors are
        // language-independent; the full localized name remains in the header.
        if (name.StartsWith("SCP-", System.StringComparison.Ordinal)) return name.Split(' ')[0];
        return room.Name switch
        {
            RoomName.LczComputerRoom => localize("Map: Office") + " PC15",
            RoomName.LczCheckpointA => localize("Map: Checkpoint") + " EX-A",
            RoomName.LczCheckpointB => localize("Map: Checkpoint") + " EX-B",
            RoomName.LczClassDSpawn => localize("Map: D cells"),
            RoomName.LczToilets => localize("Map: Restrooms"),
            RoomName.LczArmory or RoomName.HczArmory => localize("Map: Armory"),
            RoomName.LczGlassroom => localize("Map: Glass room"),
            RoomName.LczGreenhouse => localize("Map: Greenhouse"),
            RoomName.LczAirlock => localize("Map: Airlock"),
            RoomName.HczCheckpointToEntranceZone => localize(room.Zone == FacilityZone.Entrance ? "Map: HCZ checkpoint" : "Map: EZ checkpoint"),
            RoomName.HczCheckpointA => localize("Map: Elevators") + " A",
            RoomName.HczCheckpointB => localize("Map: Elevators") + " B",
            RoomName.HczMicroHID => "Micro H.I.D.",
            RoomName.HczWarhead => localize("Map: Warhead"),
            RoomName.HczServers => localize("Map: Servers"),
            RoomName.HczRampTunnel => localize("Map: Ramp"),
            RoomName.HczTestroom => localize("Map: Test room"),
            RoomName.EzOfficeLarge or RoomName.EzOfficeSmall or RoomName.EzOfficeStoried => localize(name),
            RoomName.EzIntercom => localize("Map: Intercom"),
            RoomName.Unnamed => localize("Map: Corridor"),
            _ => localize(name)
        };
    }

    internal static string Room(RoomIdentifier room) => room.Name switch
    {
        RoomName.LczClassDSpawn => "Class D cells",
        RoomName.LczComputerRoom => "Computer office PC15",
        RoomName.LczCheckpointA => "Checkpoint EX-A",
        RoomName.LczCheckpointB => "Checkpoint EX-B",
        RoomName.LczToilets => "Restrooms WC00",
        RoomName.LczArmory => "Light armory",
        RoomName.Lcz173 => "Testing chamber PT00",
        RoomName.LczGlassroom => "Glass room GR18",
        RoomName.Lcz330 => "SCP-330 chamber TC01",
        RoomName.Lcz914 => "SCP-914 chamber",
        RoomName.LczGreenhouse => "Greenhouse VT00",
        RoomName.LczAirlock => "Airlock",
        RoomName.HczCheckpointToEntranceZone when room.Zone == FacilityZone.Entrance => "Heavy containment checkpoint",
        RoomName.HczCheckpointToEntranceZone => "Entrance checkpoint",
        RoomName.HczCheckpointA => "Elevator system A",
        RoomName.HczCheckpointB => "Elevator system B",
        RoomName.HczWarhead => "Alpha Warhead silo",
        RoomName.Hcz049 => "SCP-049/173 chambers",
        RoomName.Hcz079 => "SCP-079 chamber",
        RoomName.Hcz096 => "SCP-096 chamber",
        RoomName.Hcz106 => "SCP-106 chamber",
        RoomName.Hcz939 => "SCP-939 chamber",
        RoomName.HczMicroHID => "Micro H.I.D. armory",
        RoomName.HczArmory => "Heavy armory",
        RoomName.HczServers => "Server room",
        RoomName.HczTesla => "Tesla gate",
        RoomName.EzCollapsedTunnel => "Collapsed tunnel",
        RoomName.EzGateA => "Gate A",
        RoomName.EzGateB => "Gate B",
        RoomName.EzRedroom => "Red room",
        RoomName.EzEvacShelter => "Evacuation shelter",
        RoomName.EzIntercom => "Intercom room",
        RoomName.EzOfficeStoried => "Two-story office",
        RoomName.EzOfficeLarge => "Cubical office",
        RoomName.EzOfficeSmall => "Side office",
        RoomName.HczTestroom => "Test room",
        RoomName.Hcz127 => "SCP-127 chamber",
        RoomName.HczAcroamaticAbatement => "Acroamatic abatement",
        RoomName.HczWaysideIncinerator => "Wayside incinerator",
        RoomName.HczRampTunnel => "Ramp tunnel",
        RoomName.Unnamed => room.Shape switch
        {
            RoomShape.Straight => "Straight corridor",
            RoomShape.Curve => "Corner corridor",
            RoomShape.TShape => "T junction",
            RoomShape.XShape => "Crossroads",
            RoomShape.Endroom => "Dead end",
            _ => "Passage"
        },
        _ => room.Name.ToString()
    };

    internal static string Zone(FacilityZone zone) => zone switch
    {
        FacilityZone.LightContainment => "Light Containment Zone",
        FacilityZone.HeavyContainment => "Heavy Containment Zone",
        _ => "Entrance Zone (Offices)"
    };
}
