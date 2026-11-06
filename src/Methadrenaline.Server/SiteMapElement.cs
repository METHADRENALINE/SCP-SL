using System;
using System.Linq;
using System.Reflection;
using RueI.API.Elements;
using RueI.API.Parsing;

namespace Methadrenaline.Server;

// A self-contained native TMP drawing: its final baseline returns to its start.
// RueI's ordinary parser deliberately rejects voffset. Supply a trusted parsed
// element instead; keep all other HUD elements on RueI's normal rendering path.
internal sealed class SiteMapElement : Element
{
    private static readonly ConstructorInfo Constructor = typeof(ParsedData)
        .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
    private static readonly Type Offset = Constructor.GetParameters()[1].ParameterType;
    private static readonly Type Modifications = Constructor.GetParameters()[2].ParameterType;
    private readonly ParsedData data;

    internal SiteMapElement(string content) : base(875)
    {
        ZIndex = 20;
        data = (ParsedData)Constructor.Invoke(new[] { (object)content,
            Activator.CreateInstance(Offset)!, Activator.CreateInstance(Modifications)! });
    }

    protected override ParsedData GetParsedData(ReferenceHub hub) => data;
}
