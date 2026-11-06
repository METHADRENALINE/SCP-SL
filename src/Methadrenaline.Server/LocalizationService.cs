using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hints;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using MEC;
using Methadrenaline.Server.Core;
using SecretAPI.Features.UserSettings;
using UserSettings.ServerSpecific;

namespace Methadrenaline.Server;

internal sealed class LocalizationService : IDisposable
{
    // Stable IDs and option order are part of the client's saved preferences.
    internal const int LanguageSettingId = 2026102600;
    private const int HeaderId = 2026102599;
    private static readonly MethodInfo CloneObject = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly Dictionary<ReferenceHub, ServerLanguage> languages = new();
    private readonly ServerSpecificSettingBase[] ownSettings;
    private bool disposed;
    internal LocalizationCatalog Catalog { get; }

    internal LocalizationService()
    {
        using (var stream = typeof(ServerPlugin).Assembly.GetManifestResourceStream("Methadrenaline.Server.Localization.catalog.xml") ?? throw new InvalidOperationException("Localization resource is missing."))
            Catalog = LocalizationCatalog.Read(stream);
        ownSettings = new ServerSpecificSettingBase[]
        {
            new SSGroupHeader(HeaderId, "Localization"),
            new SSDropdownSetting(LanguageSettingId, "🌐", new[] { "English", "Українська", "Русский" }, hint: "Language for perks, messages and server settings.")
        };
    }

    internal void Enable()
    {
        var existing = ServerSpecificSettingsSync.DefinedSettings ?? Array.Empty<ServerSpecificSettingBase>();
        if (existing.Any(s => s.SettingId == LanguageSettingId || s.SettingId == HeaderId) || CustomSetting.CustomSettings.Any(s => s.Id == LanguageSettingId || s.Id == HeaderId))
            throw new InvalidOperationException("Localization setting ID is already registered.");
        ServerSpecificSettingsSync.DefinedSettings = ownSettings.Concat(existing).ToArray();
        ServerSpecificSettingsSync.ServerOnSettingValueReceived += Received;
        PlayerEvents.Left += Left;
        foreach (Player player in Player.List.Where(p => !p.IsHost && !p.IsNpc)) Refresh(player.ReferenceHub);
    }

    internal ServerLanguage Language(ReferenceHub? hub) => hub != null && languages.TryGetValue(hub, out var language) ? language : ServerLanguage.English;
    internal string Text(ReferenceHub? hub, string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Catalog.Translate(text, Language(hub), Player.List.SelectMany(p => new[] { p.Nickname, p.DisplayName }));
    }

    private void Received(ReferenceHub hub, ServerSpecificSettingBase setting)
    {
        if (setting.SettingId != LanguageSettingId || setting is not SSDropdownSetting dropdown) return;
        ServerLanguage next = LocalizationCatalog.FromIndex(dropdown.SyncSelectionIndexRaw);
        if (languages.TryGetValue(hub, out var current) && current == next) return;
        languages[hub] = next;
        // Defer until native and other plugins have finished processing this packet.
        Timing.CallDelayed(0.1f, () =>
        {
            if (disposed || hub == null) return;
            Refresh(hub);
            CustomCardLocalization.Resend(hub);
            BraceletDeathPresentation.Refresh(hub);
            RoleHudLocalization.Refresh(hub);
            RueI.API.RueDisplay.Get(hub).Update();
        });
    }

    private void Refresh(ReferenceHub hub)
    {
        Player? player = Player.Get(hub);
        if (player != null && !player.IsHost && !player.IsNpc) CustomSetting.SendSettingsToPlayer(player);
    }

    private void Left(PlayerLeftEventArgs ev) => languages.Remove(ev.Player.ReferenceHub);
    internal void Forget(ReferenceHub hub) => languages.Remove(hub);

    internal Hint HintFor(ReferenceHub? hub, Hint hint)
    {
        if (hint is not TextHint textHint) return hint;
        string source = (string)typeof(TextHint).GetProperty("Text", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(textHint)!;
        string target = Text(hub, source);
        if (target == source) return hint;
        var clone = (TextHint)CloneObject.Invoke(hint, null)!;
        typeof(TextHint).GetProperty("Text", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.SetValue(clone, target);
        return clone;
    }

    internal ServerSpecificSettingBase[] SettingsFor(ReferenceHub hub, ServerSpecificSettingBase[] source)
    {
        if (source == null) return Array.Empty<ServerSpecificSettingBase>();
        return OrderForDisplay(source).Select(setting =>
        {
            var clone = (ServerSpecificSettingBase)CloneObject.Invoke(setting, null)!;
            if (setting.SettingId == LanguageSettingId)
            {
                SetProperty(clone, "HintDescription", Text(hub, setting.HintDescription));
                return clone;
            }
            string label = Text(hub, setting.Label);
            if (setting.SettingId == HeaderId)
            {
                label = "<nobr><size=90%>" + label + "</size></nobr>";
                // The client's ContentSizeFitter can leave too little space for the final Cyrillic glyph.
                // Measure a wider invisible prefix, then reset the pen before drawing the centered title.
                if (Language(hub) is ServerLanguage.Ukrainian or ServerLanguage.Russian)
                    label = "<space=9em><color=#00000000>.</color><pos=0>" + label;
            }
            SetProperty(clone, "Label", label);
            SetProperty(clone, "HintDescription", Text(hub, setting.HintDescription));
            if (setting is SSDropdownSetting dropdown && !(setting.SettingId == 0 && setting.Label == "Schematic Name"))
                SetProperty(clone, "Options", dropdown.Options.Select(option => Text(hub, option)).ToArray());
            foreach (string name in new[] { "OptionA", "OptionB", "ButtonText", "Placeholder" })
                if (setting.GetType().GetProperty(name)?.GetValue(setting) is string value) SetProperty(clone, name, Text(hub, value));
            return clone;
        }).ToArray();
    }

    // Sort whole sections before translating their labels. Keep each section's
    // controls, IDs and saved values together, including dynamic player sliders.
    internal static ServerSpecificSettingBase[] OrderForDisplay(ServerSpecificSettingBase[] source)
    {
        var sections = new List<List<ServerSpecificSettingBase>>();
        foreach (var setting in source)
        {
            if (setting is SSGroupHeader || sections.Count == 0)
                sections.Add(new List<ServerSpecificSettingBase>());
            sections[sections.Count - 1].Add(setting);
        }
        int Priority(List<ServerSpecificSettingBase> section)
        {
            var header = section[0];
            if (header.SettingId == HeaderId) return 0;
            if (header is not SSGroupHeader) return 3;
            if (header.Label == "Proximity Chat") return 1;
            if (header.Label == "Individual SCP voice volumes" || header.Label == "Proximity Volume Personalization") return 2;
            return header.Label == "Talky Settings" ? 4 : 3;
        }
        return sections.OrderBy(Priority).SelectMany(section => section).ToArray();
    }

    private static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    internal string Diagnostics()
    {
        var sample = "<size=36><color=#00FF00><b>Wizard</b></color>\n</size><size=24>Allows you to cast Wizard spells.\nDrop the keycard to change spell, inspect to cast.\nCooldown: 12s.</size>";
        bool en = Catalog.Translate(sample, ServerLanguage.English).Contains("Wizard");
        bool uk = Catalog.Translate(sample, ServerLanguage.Ukrainian).Contains("Маг") && !Catalog.Translate(sample, ServerLanguage.Ukrainian).Contains("Allows you");
        bool ru = Catalog.Translate(sample, ServerLanguage.Russian).Contains("Маг") && !Catalog.Translate(sample, ServerLanguage.Russian).Contains("Allows you");
        bool registered = ServerSpecificSettingsSync.DefinedSettings.Count(s => s.SettingId == LanguageSettingId) == 1;
        return $"localization: {(en && uk && ru && registered ? "PASS" : "FAIL")}; entries={Catalog.Count}; EN={en}, UK={uk}, RU={ru}; selector={registered}; sessions={languages.Count}";
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ServerSpecificSettingsSync.ServerOnSettingValueReceived -= Received;
        PlayerEvents.Left -= Left;
        ServerSpecificSettingsSync.DefinedSettings = (ServerSpecificSettingsSync.DefinedSettings ?? Array.Empty<ServerSpecificSettingBase>()).Where(s => !ownSettings.Contains(s)).ToArray();
        languages.Clear();
    }
}
