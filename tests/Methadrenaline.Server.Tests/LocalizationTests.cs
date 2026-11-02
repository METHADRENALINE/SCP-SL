using Methadrenaline.Server.Core;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData(ServerLanguage.English, "Release in: 00:42", "Paravoz Hunter")]
    [InlineData(ServerLanguage.Ukrainian, "До виходу: 00:42", "Мисливець на Паровозів")]
    [InlineData(ServerLanguage.Russian, "До выхода: 00:42", "Паравозо-убийца")]
    public void ParavozCountdownAndEscapedClassAreLocalized(ServerLanguage language, string timer, string hunter)
    {
        Assert.Equal(timer, Catalog().Translate(string.Format(ParavozPolicy.WaitingMessage, "00:42"), language));
        Assert.Equal(hunter, Catalog().Translate("Paravoz Hunter", language));
    }

    [Fact]
    public void CompiledCatalogMatchesCurrentTranslations()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("catalog-source.json")!;
        using var source = System.Text.Json.JsonDocument.Parse(stream);
        var entries = Catalog().Entries;
        Assert.Equal(source.RootElement.GetArrayLength(), entries.Count);
        foreach (var row in source.RootElement.EnumerateArray())
        {
            var entry = Assert.Single(entries, e => e.Source == row.GetProperty("source").GetString());
            Assert.Equal(row.GetProperty("en").GetString(), entry.English);
            Assert.Equal(row.GetProperty("uk").GetString(), entry.Ukrainian);
            Assert.Equal(row.GetProperty("ru").GetString(), entry.Russian);
            Assert.Equal(row.TryGetProperty("verbatim", out var slots) ? slots.EnumerateArray().Select(s => s.GetInt32()).ToArray() : Array.Empty<int>(), entry.VerbatimArguments);
        }
    }
    private static LocalizationCatalog Catalog()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("catalog.xml")!;
        return LocalizationCatalog.Read(stream);
    }

    [Fact]
    public void EveryEntryHasAllLanguagesAndMatchingPlaceholders()
    {
        var catalog = Catalog();
        Assert.True(catalog.Count > 400);
        foreach (var entry in catalog.Entries)
        {
            foreach (ServerLanguage language in Enum.GetValues<ServerLanguage>())
            {
                string input = Regex.Replace(entry.Source, @"\{(\d+)\}", m => "98765" + m.Groups[1].Value);
                string expected = Regex.Replace(entry.In(language), @"\{(\d+)\}", m => "98765" + m.Groups[1].Value);
                Assert.True(expected.Trim() == catalog.Translate(input, language).Trim(), $"{language}: {entry.Source}\nExpected: {expected}\nActual: {catalog.Translate(input, language)}");
            }
        }
    }

    [Fact]
    public void NestedPerkHintPreservesFormattingAndTranslatesDescriptions()
    {
        string input = "<size=36><color=#00FF00><b>Wizard</b></color>\n</size><size=24>Allows you to cast Wizard spells.\nDrop the keycard to change spell, inspect to cast.\nCooldown: 12s.</size>";
        var catalog = Catalog();
        var uk = catalog.Translate(input, ServerLanguage.Ukrainian);
        var ru = catalog.Translate(input, ServerLanguage.Russian);
        Assert.Contains("закляття", uk);
        Assert.Contains("заклинания", ru);
        Assert.DoesNotContain("Allows", uk);
        Assert.DoesNotContain("Cooldown", ru);
        Assert.Equal(Regex.Matches(input, "<[^>]+>").Select(m => m.Value), Regex.Matches(uk, "<[^>]+>").Select(m => m.Value));
        Assert.Contains("12", uk);
    }

    [Fact]
    public void LanguagesAreIndependentAndNamesAndProtocolFieldsRemainIntact()
    {
        var catalog = Catalog();
        const string source = "Wizard has eaten your pizza.";
        Assert.Equal("Гравець Wizard з'їв твою піцу.", catalog.Translate(source, ServerLanguage.Ukrainian, new[] { "Wizard" }));
        Assert.Equal("Игрок Medic съел твою пиццу.", catalog.Translate("Medic has eaten your pizza.", ServerLanguage.Russian, new[] { "Medic" }));
        Assert.Equal(source, catalog.Translate(source, ServerLanguage.English, new[] { "Wizard" }));
        Assert.Equal("<color=#00FF00>Медик</color>", catalog.Translate("<color=#00FF00>Medic</color>", ServerLanguage.Russian));
        Assert.Equal(".c <number>", catalog.Translate(".c <number>", ServerLanguage.Russian));
        Assert.Equal("SAM-Fireball", catalog.Translate("SAM-Fireball", ServerLanguage.English));
    }

    [Fact]
    public void ServerSettingsAndHudUseTheViewersLanguage()
    {
        var catalog = Catalog();
        Assert.Equal("Обрати покращення 2", catalog.Translate("Choose Upgrade 2", ServerLanguage.Ukrainian));
        Assert.Contains("За тобой наблюдают: (3)", catalog.Translate("<size=28>👥 Watching you: (3)</size>", ServerLanguage.Russian));
        Assert.Contains("Боєголовка:", catalog.Translate("<b>Warhead:</b> Not Armed", ServerLanguage.Ukrainian));
        Assert.DoesNotContain("Armed", catalog.Translate("<b>Warhead:</b> Not Armed", ServerLanguage.Russian));
        Assert.Equal("Натисни <b>V</b>, щоб перемкнути.", catalog.Translate("Press <b>V</b> to toggle.", ServerLanguage.Ukrainian));
    }

    [Fact]
    public void NativePreferencesAlwaysDefaultSafely()
    {
        Assert.Equal(ServerLanguage.English, LocalizationCatalog.FromIndex(-1));
        Assert.Equal(ServerLanguage.English, LocalizationCatalog.FromIndex(0));
        Assert.Equal(ServerLanguage.Ukrainian, LocalizationCatalog.FromIndex(1));
        Assert.Equal(ServerLanguage.Russian, LocalizationCatalog.FromIndex(2));
        Assert.Equal(ServerLanguage.English, LocalizationCatalog.FromIndex(999));
    }

    [Fact]
    public void RoundAnnouncementsKeepExactRussianAndHaveEnglishAndUkrainianVersions()
    {
        var catalog = Catalog();
        foreach (string source in new[] { PerkRoundPolicy.WithPerksMessage, PerkRoundPolicy.WithoutPerksMessage })
        {
            Assert.Equal(source, catalog.Translate(source, ServerLanguage.Russian));
            Assert.NotEqual(source, catalog.Translate(source, ServerLanguage.English));
            Assert.NotEqual(source, catalog.Translate(source, ServerLanguage.Ukrainian));
        }
        Assert.Contains("Perks", catalog.Translate(PerkRoundPolicy.WithPerksMessage, ServerLanguage.English));
    }

    [Fact]
    public void FileNamesCommandIdsAndPlayerNamesAreVerbatimArguments()
    {
        var catalog = Catalog();
        Assert.Equal("Перк додано: Medic.049 для Fireball", catalog.Translate("Added perk: Medic.049 to Fireball", ServerLanguage.Ukrainian));
        Assert.Equal("Не удалось загрузить карту: файла SAM-Fireball.yml нет!", catalog.Translate("Failed to load map data: File SAM-Fireball.yml does not exist!", ServerLanguage.Russian));
        Assert.Equal("Ти з'їв піцу гравця Wizard.", catalog.Translate("You've eaten Wizard's pizza.", ServerLanguage.Ukrainian));
    }

    [Fact]
    public void BraceletHudTemplatesTranslateWithoutChangingNamesOrDistances()
    {
        var catalog = Catalog();
        const string active = "Captor: {0} · {1} m";
        string ru = string.Format(catalog.Translate(active, ServerLanguage.Russian), "<noparse>Wizard</noparse>", BraceletPolicy.FormatDistance(18.42, ServerLanguage.Russian));
        Assert.Equal("Конвоир: <noparse>Wizard</noparse> · 18,42 м", ru);
        Assert.Equal(ru, catalog.Translate(ru, ServerLanguage.Russian));
        Assert.Equal("Пленный: TestPlayer (ликвидирован)", string.Format(catalog.Translate("Prisoner: {0} (eliminated)", ServerLanguage.Russian), "TestPlayer"));
        Assert.Equal("Полонений: TestPlayer (ліквідований)", string.Format(catalog.Translate("Prisoner: {0} (eliminated)", ServerLanguage.Ukrainian), "TestPlayer"));
    }

    [Fact]
    public void BraceletSequenceKeepsExactRussianAndTranslatesOtherLanguages()
    {
        var catalog = Catalog();
        const string filling = "Сливание взрывчатых веществ в камеру";
        Assert.Equal(filling, catalog.Translate(filling, ServerLanguage.Russian));
        Assert.Equal("Filling the chamber with explosives", catalog.Translate(filling, ServerLanguage.English));
        Assert.Equal("Заливання вибухових речовин у камеру", catalog.Translate(filling, ServerLanguage.Ukrainian));
        Assert.Equal("детонация", catalog.Translate("детонация", ServerLanguage.Russian));
        Assert.Equal("Detonation", catalog.Translate("детонация", ServerLanguage.English));
        Assert.Equal("детонація", catalog.Translate("детонация", ServerLanguage.Ukrainian));
    }

    [Fact]
    public void NoParseNamesAndUnknownTextStayUnchanged()
    {
        var catalog = Catalog();
        Assert.Equal("<noparse>Fireball Wizard</noparse>", catalog.Translate("<noparse>Fireball Wizard</noparse>", ServerLanguage.Russian));
        Assert.Equal("A future unknown message 123.", catalog.Translate("A future unknown message 123.", ServerLanguage.Ukrainian));
    }
}
