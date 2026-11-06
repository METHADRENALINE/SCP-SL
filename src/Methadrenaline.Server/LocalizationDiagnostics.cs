using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using Hints;
using LabApi.Features.Wrappers;
using Methadrenaline.Server.Core;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using RueI.API.Elements;
using RueI.API.Parsing;
using UnityEngine;
using UserSettings.ServerSpecific;
using NativeCardSync = InventorySystem.Items.Keycards.KeycardDetailSynchronizer;

namespace Methadrenaline.Server;

internal static class LocalizationDiagnostics
{
    internal static string Run(LocalizationService localization)
    {
        if (Player.List.Any(p => !p.IsHost && !p.IsNpc)) return "Localization diagnostics require an empty server.";
        var checks = new List<string>();
        var hubs = new List<ReferenceHub>();
        var hudConfig = UltimateHUD.Plugin.Instance.Config;
        string originalPlayerHud = hudConfig.PlayerHud, originalSpectatorHud = hudConfig.SpectatorHud;
        bool originalSpectatorEnabled = hudConfig.EnableSpectatorHud;
        void Check(string name, bool passed) => checks.Add(name + "=" + (passed ? "PASS" : "FAIL"));
        try
        {
            ReferenceHub a = DummyUtils.SpawnDummy("Class-D"); hubs.Add(a);
            ReferenceHub b = DummyUtils.SpawnDummy("LocaleProbeB"); hubs.Add(b);
            void Select(ReferenceHub hub, int index)
            {
                var setting = new SSDropdownSetting(LocalizationService.LanguageSettingId, "language", new[] { "English", "Українська", "Русский" });
                AccessTools.Property(typeof(SSDropdownSetting), "SyncSelectionIndexRaw").SetValue(setting, index);
                AccessTools.Method(typeof(ServerSpecificSettingsSync), "ServerProcessClientResponseMsg").Invoke(null, new object[] { hub.connectionToClient, new SSSClientResponse(setting) });
            }
            Select(a, 1); Select(b, 2);
            Check("native_selection", localization.Language(a) == ServerLanguage.Ukrainian && localization.Language(b) == ServerLanguage.Russian);
            var localizedSettings = localization.SettingsFor(b, ServerSpecificSettingsSync.DefinedSettings);
            const string headingSpace = "<space=9em><color=#00000000>.</color><pos=0>";
            Check("localization_header_cyrillic_width", localizedSettings[0].Label == headingSpace + "<nobr><size=90%>Локализация</size></nobr>" &&
                localization.SettingsFor(a, ServerSpecificSettingsSync.DefinedSettings)[0].Label == headingSpace + "<nobr><size=90%>Локалізація</size></nobr>");
            var selector = localizedSettings.Single(s => s.SettingId == LocalizationService.LanguageSettingId);
            Check("language_tooltip_is_concise", selector.Label == "🌐" && selector.HintDescription == "Язык перков, сообщений и настроек сервера.");
            var settingSource = new ServerSpecificSettingBase[] { new SSKeybindSetting(12341234, "See Perks", hint: "Shows you your perks."), new SSDropdownSetting(12341235, "Input Method", new[] { "Toggle", "Hold" }) };
            var uk = localization.SettingsFor(a, settingSource);
            var ru = localization.SettingsFor(b, settingSource);
            Check("per_player_settings", uk[0].Label == "Мої перки" && ru[0].Label == "Мои перки" && settingSource[0].Label == "See Perks" && uk[0].SettingId == settingSource[0].SettingId);
            Check("options_isolated", ((SSDropdownSetting)uk[1]).Options[1] == "Утримання" && ((SSDropdownSetting)ru[1]).Options[1] == "Удержание" && ((SSDropdownSetting)settingSource[1]).Options[1] == "Hold");
            var registry = ServerSpecificSettingsSync.DefinedSettings;
            try
            {
                ServerSpecificSettingsSync.DefinedSettings = new ServerSpecificSettingBase[] { new SSGroupHeader("MapEditorReborn"), new SSDropdownSetting(0, "Schematic Name", new[] { "SAM-Fireball" }, isServerOnly: true) };
                Check("map_editor_keeps_settings", registry.All(s => ServerSpecificSettingsSync.DefinedSettings.Contains(s)) && ServerSpecificSettingsSync.DefinedSettings.Any(s => s.SettingId == 0));
            }
            finally { ServerSpecificSettingsSync.DefinedSettings = registry; }
            var hint = new TextHint("<b>Medic</b>\nShooting a teammate will heal them by 15 HP.", null, null, 7);
            var textProperty = AccessTools.Property(typeof(TextHint), "Text");
            var ukHint = localization.HintFor(a, hint);
            var ruHint = localization.HintFor(b, hint);
            Check("shared_hint_isolated", ((string)textProperty.GetValue(ukHint)!).Contains("Постріл") && ((string)textProperty.GetValue(ruHint)!).Contains("Выстрел") && ((string)textProperty.GetValue(hint)!).Contains("Shooting"));
            var basic = new BasicElement(100, "<b>Medic</b>\nShooting a teammate will heal them by 15 HP.");
            string Render(Element element, ReferenceHub hub)
            {
                object parsed = AccessTools.Method(element.GetType(), "GetParsedData").Invoke(element, new object[] { hub })!;
                return (string)(AccessTools.Property(typeof(ParsedData), "ParsedString")?.GetValue(parsed) ?? AccessTools.Field(typeof(ParsedData), "ParsedString").GetValue(parsed))!;
            }
            Check("shared_ruei_element", Render(basic, a).Contains("Постріл") && Render(basic, b).Contains("Выстрел"));
            var voiceTip = new BasicElement(58, "<align=center><size=34><nobr>Press {0} to switch between nearby players and the SCP team.</nobr></size></align>");
            Check("scp_bind_tip_localized", Render(voiceTip, a).Contains("між гравцями поруч") &&
                Render(voiceTip, b).Contains("между игроками рядом"));
            var dynamic = new DynamicElement(100, () => "Generators: 1/3 | Warhead: Armed");
            var cached = new CachedElement(100, TimeSpan.FromSeconds(10), () => "Generators: 1/3 | Warhead: Armed");
            Check("dynamic_and_cached_hud", Render(dynamic, a).Contains("Генератори") && Render(cached, b).Contains("Генераторы"));
            var registered = (IDictionary)AccessTools.Field(AccessTools.TypeByName("SwiftArcadeMode.Features.PerkManager"), "RegisteredPerks").GetValue(null)!;
            var missing = new List<string>();
            foreach (object perk in registered.Values)
            {
                object profile = AccessTools.Field(perk.GetType(), "Profile").GetValue(perk)!;
                string name = (string)AccessTools.Field(profile.GetType(), "Name").GetValue(profile)!;
                string description = (string)AccessTools.Field(profile.GetType(), "Description").GetValue(profile)!;
                foreach (var language in new[] { ServerLanguage.Ukrainian, ServerLanguage.Russian })
                {
                    string translated = localization.Catalog.Translate(description, language);
                    if (localization.Catalog.Translate(name, language) == name || Regex.IsMatch(translated, @"\b(?:Allows|Gives|Cooldown|Taking|When|Every|However|Focuses|Press|chance|health)\b")) missing.Add(name + ":" + language);
                }
            }
            Check("all_live_perks_" + registered.Count, registered.Count >= 50 && missing.Count == 0);
            if (missing.Count > 0) checks.Add("untranslated_profiles: " + string.Join(", ", missing));
            Player player = Player.Get(a)!;
            player.SetRole(RoleTypeId.Tutorial);
            Type? talkyType = AccessTools.TypeByName("Talky.Plugin");
            if (talkyType != null)
            {
                object? talky = AccessTools.Property(talkyType, "Instance").GetValue(null);
                object? runtime = talky == null ? null : AccessTools.Property(talkyType, "Runtime").GetValue(talky);
                Check("talky_enabled", runtime != null && (bool)AccessTools.Field(talkyType, "_fullyEnabled").GetValue(talky)!);
                object settings = AccessTools.Field(talkyType, "Settings").GetValue(talky)!;
                var owned = (ServerSpecificSettingBase[])AccessTools.Field(settings.GetType(), "_ownedSettings").GetValue(settings)!;
                Check("talky_settings_registered", owned.Length == 4 && owned.All(s => ServerSpecificSettingsSync.DefinedSettings.Contains(s)) && owned.All(s => s.SettingId != LocalizationService.LanguageSettingId));
                var translatedUk = localization.SettingsFor(a, owned);
                var translatedRu = localization.SettingsFor(b, owned);
                Check("talky_settings_localized", translatedUk.OfType<SSKeybindSetting>().Single().Label == "Жест рукою" && translatedRu.OfType<SSKeybindSetting>().Single().Label == "Жест рукой" && translatedUk.OfType<SSDropdownSetting>().Single().Options[2] == "Незручна усмішка" && translatedRu.OfType<SSTwoButtonsSetting>().Single().OptionB == "Включено");
                object[] trackerArgs = { player.NetworkId, null! };
                Check("talky_player_tracked", runtime != null && (bool)AccessTools.Method(runtime.GetType(), "TryGetSpeechTracker").Invoke(runtime, trackerArgs)!);
            }
            var card = KeycardItem.CreateCustomKeycardTaskForce(player, "Fireball", "Fireball", default, Color.red, Color.blue, "123456789012", 0);
            var database = (Dictionary<ushort, ArraySegment<byte>>)AccessTools.Field(typeof(NativeCardSync), "Database").GetValue(null)!;
            var original = database[card!.Serial];
            var ukCard = CustomCardLocalization.Translate(original, ServerLanguage.Ukrainian);
            var ruCard = CustomCardLocalization.Translate(original, ServerLanguage.Russian);
            string CardName(ArraySegment<byte> payload) { using var reader = NetworkReaderPool.Get(payload); return reader.ReadString(); }
            Check("spell_card_names", CardName(original) == "Fireball" && CardName(ukCard) == "Вогняна куля" && CardName(ruCard) == "Огненный шар");
            Check("canonical_spell_unchanged", database[card.Serial].SequenceEqual(original) && card.Permissions == Interactables.Interobjects.DoorUtils.DoorPermissionFlags.None);
            Select(a, 2);
            Check("language_switch", localization.Language(a) == ServerLanguage.Russian && Render(basic, a).Contains("Выстрел"));
            Select(a, 0);
            Check("return_to_english", localization.Language(a) == ServerLanguage.English && Render(basic, a).Contains("Shooting"));
            string StoredHud(ReferenceHub hub, string tagField)
            {
                object entries = AccessTools.Field(typeof(RueI.API.RueDisplay), "elements").GetValue(RueI.API.RueDisplay.Get(hub));
                object tag = AccessTools.Field(typeof(UltimateHUD.Hints), tagField).GetValue(null);
                object stored = AccessTools.Property(entries.GetType(), "Item").GetValue(entries, new[] { tag })!;
                var element = (Element)AccessTools.Field(stored.GetType(), "Element").GetValue(stored);
                return Render(element, hub);
            }
            // Exercise the actual third-party BasicElement creation and RueI
            // parsing. A nickname identical to a role must stay untranslated.
            hudConfig.PlayerHud = "{nickname} | {role}";
            hudConfig.SpectatorHud = "{nickname} | {role}";
            hudConfig.EnableSpectatorHud = true;
            player.SetRole(RoleTypeId.ClassD);
            foreach (int language in new[] { 0, 1, 2 })
            {
                Select(a, language);
                RoleHudLocalization.Refresh(a);
                string ownHud = StoredHud(a, "PlayerInfoTag");
                string role = localization.Catalog.Translate("Class-D", LocalizationCatalog.FromIndex(language));
                Check("native_role_hud_" + language, ownHud.Contains("<noparse>" + role + "</noparse>") && ownHud.Contains("<noparse>Class-D</noparse>"));
                Check("role_viewer_scope_restored_" + language, RoleHudLocalization.Viewer == null);
            }
            Select(a, 1);
            Select(b, 2);
            Player observer = Player.Get(b)!;
            observer.SetRole(RoleTypeId.Spectator);
            AccessTools.Property(typeof(PlayerRoles.Spectating.SpectatorRole), "SyncedSpectatedNetId").SetValue(observer.RoleBase, player.NetworkId);
            RoleHudLocalization.Refresh(b);
            Check("spectator_role_uses_viewer_language", StoredHud(b, "SpectatorPlayerInfoTag").Contains("<noparse>D-класс</noparse>") && localization.Language(a) == ServerLanguage.Ukrainian);
            Select(b, 0);
            RoleHudLocalization.Refresh(b);
            RoleHudLocalization.Refresh(a);
            Check("spectator_language_switch_isolated", StoredHud(b, "SpectatorPlayerInfoTag").Contains("<noparse>Class-D</noparse>") && StoredHud(a, "PlayerInfoTag").Contains("<noparse>D-клас</noparse>") && RoleHudLocalization.Viewer == null);
            var roleLabels = UltimateHUD.Plugin.Instance.Translation.GameRoles.Select(r => r.Name).Where(r => r != "No role")
                .Concat(new[] { "Paravoz", "Paravoz Hunter", "Spectator", "Custom role", "Destroyed role", "Flamingo", "Alpha Flamingo", "Zombie Flamingo", "MTF Flamingo", "CI Flamingo" });
            Check("all_hud_role_labels_covered", roleLabels.All(label => label.StartsWith("SCP-", StringComparison.Ordinal) || localization.Catalog.Entries.Any(e => e.Source == label)));
            player.SetRole(RoleTypeId.None);
            Check("unassigned_role_has_no_label", player.Role == RoleTypeId.None && RoleHudLocalization.Translate(player, "No role") == "");
        }
        catch (Exception error) { checks.Add("FAIL: " + error); }
        finally
        {
            hudConfig.PlayerHud = originalPlayerHud;
            hudConfig.SpectatorHud = originalSpectatorHud;
            hudConfig.EnableSpectatorHud = originalSpectatorEnabled;
            foreach (var hub in hubs)
            {
                localization.Forget(hub);
                if (hub != null) NetworkServer.Destroy(hub.gameObject);
            }
        }
        return string.Join("\n", checks);
    }
}
