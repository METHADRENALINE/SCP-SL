using System;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using HarmonyLib;
using Hints;
using LabApi.Features.Wrappers;
using Mirror;
using RueI.API.Elements;
using TMPro;
using UnityEngine;

namespace Methadrenaline.Server;

internal static class SiteMapRenderingDiagnostics
{
    private static TMP_FontAsset? validationFont;
    private static TMP_FontAsset? validationSecondary;
    // Exercise the installed HUD serializer and the game's actual text engine,
    // rather than assuming that a valid source string reaches the client intact.
    internal static void Probe(Player player, string source, string name, Action<string, bool> check)
    {
        var identity = player.ReferenceHub.networkIdentity;
        var previous = identity.connectionToClient;
        var capture = new CaptureConnection();
        try
        {
            AccessTools.Property(typeof(NetworkIdentity), nameof(NetworkIdentity.connectionToClient)).SetValue(identity, capture);
            var combiner = AccessTools.TypeByName("RueI.API.Parsing.ElementCombiner");
            AccessTools.Method(combiner, "Combine").Invoke(null,
                new object[] { player.ReferenceHub, new Element[] { new SiteMapElement(source) } });
        }
        finally
        {
            AccessTools.Property(typeof(NetworkIdentity), nameof(NetworkIdentity.connectionToClient)).SetValue(identity, previous);
        }
        using var reader = NetworkReaderPool.Get(capture.Frame);
        reader.ReadUShort();
        bool nativeText = reader.ReadByte() == 1;
        var hint = TextHint.FromNetwork(reader);
        var effects = (HintEffect[])AccessTools.Field(typeof(Hint), "_effects").GetValue(hint);
        check("site_map_has_no_frame_effect_" + name, effects == null || effects.Length == 0);
        var parameters = (HintParameter[])AccessTools.Property(typeof(Hint), "Parameters").GetValue(hint);
        foreach (var parameter in parameters) parameter.Update(0);
        check("site_map_parameters_stop_updating_" + name, parameters.All(parameter => !parameter.Update(.5f)));
        string format = (string)AccessTools.Property(typeof(TextHint), "Text").GetValue(hint);
        string received = string.Format(CultureInfo.InvariantCulture, format, parameters.Select(p => (object)p.Formatted).ToArray());
        check("site_map_native_hint_roundtrip_" + name,
            nativeText && reader.Remaining == 0 && !received.Contains('\uFFFD') && received.Contains(source) &&
            received.EndsWith("<line-height=0>\n<alpha=#00><scale=0>.", StringComparison.Ordinal));

        var go = new GameObject("SiteMapRenderingDiagnostic");
        TMP_FontAsset? testFont = null;
        System.Collections.Generic.List<TMP_FontAsset>? previousFallbacks = null;
        var settings = AccessTools.Property(typeof(TMP_Settings), "instance").GetValue(null);
        var matchPreset = AccessTools.Field(typeof(TMP_Settings), "m_matchMaterialPreset");
        bool previousMatch = (bool)matchPreset.GetValue(settings);
        try
        {
            var text = go.AddComponent<TextMeshPro>();
            text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            string testFontPath = Environment.GetEnvironmentVariable("MD_SITE_MAP_TEST_FONT");
            if (!string.IsNullOrEmpty(testFontPath) && File.Exists(testFontPath))
            {
                var shaderField = AccessTools.Field(typeof(ShaderUtilities), "k_ShaderRef_MobileSDF");
                var previousShader = shaderField.GetValue(null);
                try
                {
                    // The dedicated build strips the mobile shader used by the
                    // font factory. Its bundled SDF font still has a valid one.
                    shaderField.SetValue(null, text.font.material.shader);
                    testFont = validationFont ??= TMP_FontAsset.CreateFontAsset(testFontPath, 0, 64, 6,
                        UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
                    string secondary = Environment.GetEnvironmentVariable("MD_SITE_MAP_TEST_FONT_SECONDARY");
                    if (!string.IsNullOrEmpty(secondary) && File.Exists(secondary))
                    {
                        validationSecondary ??= TMP_FontAsset.CreateFontAsset(secondary, 0, 64, 6,
                            UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024);
                        testFont.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { validationSecondary };
                    }
                }
                finally { shaderField.SetValue(null, previousShader); }
                previousFallbacks = text.font.fallbackFontAssetTable;
                text.font.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { testFont };
                // Headless shaders have no renderable fallback atlas material.
                // Use the fallback's own material when testing text geometry.
                matchPreset.SetValue(settings, false);
            }
            text.fontSize = 36;
            text.enableAutoSizing = false;
            text.isOrthographic = true;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.fontStyle = FontStyles.SmallCaps;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = new Vector2(1200, 1000);
            var info = text.GetTextInfo(received);
            if (testFont)
                check("site_map_native_fallback_has_every_character_" + name,
                    info.characterInfo.Take(info.characterCount).All(character => character.character != '\u25A1'));
            var caption = Regex.Match(received, @"<size=19><color=#FFC75B>([^<]+)");
            if (caption.Success)
            {
                var value = caption.Groups[1];
                var characters = info.characterInfo.Take(info.characterCount)
                    .Where(character => character.index >= value.Index && character.index < value.Index + value.Length).ToArray();
                check("site_map_header_does_not_wrap_" + name, characters.Length > 0 &&
                    characters.All(character => character.lineNumber == characters[0].lineNumber &&
                        Math.Abs(character.baseLine - characters[0].baseLine) < .1f) &&
                    characters.All(character => character.origin >= characters[0].origin - 1 &&
                        character.xAdvance <= characters[0].origin + 1110));
            }
            check("site_map_captions_have_separate_material_" + name,
                info.characterInfo.Take(info.characterCount).Where(c => c.isVisible && c.color.a != 0 && c.character > 32 && c.character < 127)
                    .All(c => c.materialReferenceIndex != 0));
            check("site_map_native_text_mesh_" + name, info.characterCount > 0 &&
                info.characterInfo.Take(info.characterCount).All(c => c.character != '\uFFFD' && c.character != '<' && c.character != '>' &&
                    (c.character != '\u00AD' || !c.isVisible) &&
                    (!c.isVisible || !float.IsNaN(c.bottomLeft.x) && !float.IsInfinity(c.bottomLeft.x) && !float.IsNaN(c.topRight.y) && !float.IsInfinity(c.topRight.y))) &&
                info.meshInfo.All(mesh => mesh.vertexCount <= 65532));

            var sample = SiteMapPainter.Paint(text.font,
                new[] { new SiteMapInk(new Rect(100, 200, 120, 40), 211) }, "ABCDEF");
            info = text.GetTextInfo("<align=left><width=32767>" + sample + "</width></align>");
            var vertices = info.meshInfo.SelectMany(mesh => Enumerable.Range(0, mesh.vertexCount)
                .Where(index => mesh.colors32[index].r == 171 && mesh.colors32[index].g == 205 &&
                    mesh.colors32[index].b == 239 && mesh.colors32[index].a == 211)
                .Select(index => mesh.vertices[index])).ToArray();
            check("site_map_quad_geometry_" + name, vertices.Length == 4 &&
                Math.Abs(vertices.Max(vertex => vertex.x) - vertices.Min(vertex => vertex.x) - 120) < .25 &&
                Math.Abs(vertices.Max(vertex => vertex.y) - vertices.Min(vertex => vertex.y) - 40) < .25);
        }
        finally
        {
            if (testFont)
            {
                Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF").fallbackFontAssetTable = previousFallbacks;
            }
            matchPreset.SetValue(settings, previousMatch);
            UnityEngine.Object.Destroy(go);
        }
    }

    private sealed class CaptureConnection : NetworkConnectionToClient
    {
        internal ArraySegment<byte> Frame;
        internal CaptureConnection() : base(-17778) { }
        public override void Send(ArraySegment<byte> segment, int channelId = 0)
        {
            var bytes = new byte[segment.Count];
            Array.Copy(segment.Array!, segment.Offset, bytes, 0, bytes.Length);
            Frame = new ArraySegment<byte>(bytes);
        }
    }
}
