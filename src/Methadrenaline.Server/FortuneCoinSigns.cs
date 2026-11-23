using System.Collections.Generic;
using LabApi.Features.Wrappers;
using MapGeneration;
using UnityEngine;

namespace Methadrenaline.Server;

internal static class FortuneCoinSigns
{
    private static readonly string[] Digits =
        { "ноль", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять" };
    private static readonly string[] Letters =
    {
        "эй", "би", "си", "ди", "и", "эф", "джи", "эйч", "ай", "джей", "кей", "эл", "эм",
        "эн", "оу", "пи", "кью", "ар", "эс", "ти", "ю", "ви", "дабл ю", "икс", "уай", "зет"
    };

    internal static Dictionary<RoomIdentifier, string> Read()
    {
        var signs = new Dictionary<RoomIdentifier, string>();
        foreach (var label in LCZ_Label.AllLabels)
        {
            if (!label || !label.chRend || !label.numRend) continue;
            var marker = label.transform;
            var target = Room.GetRoomAtPosition(marker.position + marker.forward * 8);
            if (target?.Zone != FacilityZone.LightContainment || signs.ContainsKey(target.Base)) continue;
            string? code = Code(label.chRend.sharedMaterial?.name, label.numRend.sharedMaterial?.name);
            if (code != null) signs.Add(target.Base, code);
        }
        return signs;
    }

    internal static string? Code(string? symbol, string? number)
    {
        if (symbol == "#914") return "#914";
        if (string.IsNullOrEmpty(number)) return null;
        string? prefix = symbol switch
        {
            "Straight" => "HS",
            "CurvedHallway" => "HC",
            "Intersection3" => "IT",
            "Intersection4" => "IX",
            "ClassD" => "CD",
            "TestChamber" => "TC",
            "Airlock" => "AL",
            "Vent" => "VT",
            "##" => "##",
            "WC" => "WC",
            "#173" => "PT",
            "Cafe" => "PC",
            "Checkpoint" => "EX",
            "#372" => "GR",
            _ => null
        };
        return prefix == null ? null : prefix + number;
    }

    internal static string Pronounce(string code)
    {
        if (code == "#914") return "решётка девятьсот четырнадцать";
        var parts = new List<string>(code.Length);
        foreach (char character in code)
        {
            if (character >= '0' && character <= '9') parts.Add(Digits[character - '0']);
            else if (character >= 'A' && character <= 'Z') parts.Add(Letters[character - 'A']);
            else if (character == '#') parts.Add("решётка");
        }
        return string.Join(" ", parts);
    }
}
