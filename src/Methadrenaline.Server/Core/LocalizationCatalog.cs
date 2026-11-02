using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Methadrenaline.Server.Core;

public enum ServerLanguage { English, Ukrainian, Russian }

public sealed class LocalizedText
{
    public string Source { get; set; } = "";
    public string English { get; set; } = "";
    public string Ukrainian { get; set; } = "";
    public string Russian { get; set; } = "";
    public int[] VerbatimArguments { get; set; } = Array.Empty<int>();
    public string In(ServerLanguage language) => language == ServerLanguage.Ukrainian ? Ukrainian : language == ServerLanguage.Russian ? Russian : English;
}

/// <summary>Translates presentation strings only; never changes plugin identifiers or saved data.</summary>
public sealed class LocalizationCatalog
{
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.CultureInvariant);
    private static readonly Regex Tags = new(@"<\/?(?:align|alpha|b|br|color|cspace|font|i|indent|line-height|lowercase|margin(?:-left|-right)?|mark|mspace|nobr|noparse|pos|rotate|s|scale|size|smallcaps|space|sprite|style|sub|sup|u|uppercase|voffset|width)(?:=[^>]*|\s[^>]*)?>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private readonly Dictionary<string, LocalizedText> exact;
    private readonly Rule[] templates;
    private readonly Regex literals;
    private readonly Dictionary<string, string> cache = new(StringComparer.Ordinal);
    public IReadOnlyList<LocalizedText> Entries { get; }
    public int Count => Entries.Count;

    public LocalizationCatalog(IEnumerable<LocalizedText> entries)
    {
        Entries = entries.ToArray();
        exact = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
        var dynamic = new List<Rule>();
        foreach (LocalizedText entry in Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Source) || string.IsNullOrWhiteSpace(entry.English) || string.IsNullOrWhiteSpace(entry.Ukrainian) || string.IsNullOrWhiteSpace(entry.Russian))
                throw new InvalidDataException("Incomplete localization entry: " + entry.Source);
            int[] slots = Slots(entry.Source);
            if (!slots.SequenceEqual(Slots(entry.English)) || !slots.SequenceEqual(Slots(entry.Ukrainian)) || !slots.SequenceEqual(Slots(entry.Russian)))
                throw new InvalidDataException("Localization placeholders differ: " + entry.Source);
            if (slots.Length == 0)
            {
                string key = entry.Source.Trim();
                if (exact.ContainsKey(key)) throw new InvalidDataException("Duplicate localization key: " + key);
                exact.Add(key, entry);
            }
            else if (Placeholder.Replace(entry.Source, "").Length >= 3)
                dynamic.Add(new Rule(entry));
        }
        templates = dynamic.OrderByDescending(r => r.Specificity).ToArray();
        string pattern = string.Join("|", exact.Keys.Where(k => k.Length >= 2 && !Tags.IsMatch(k)).OrderByDescending(k => k.Length).Select(k =>
            (char.IsLetterOrDigit(k[0]) ? @"(?<![\p{L}\p{N}_])" : "") + Regex.Escape(k) +
            (char.IsLetterOrDigit(k[k.Length - 1]) ? @"(?![\p{L}\p{N}_])" : "")));
        literals = new Regex(pattern.Length == 0 ? "(?!)" : pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    }

    public static LocalizationCatalog Read(Stream stream) => new(XDocument.Load(stream).Root!.Elements("entry").Select(e => new LocalizedText { Source = (string?)e.Element("source") ?? "", English = (string?)e.Element("en") ?? "", Ukrainian = (string?)e.Element("uk") ?? "", Russian = (string?)e.Element("ru") ?? "", VerbatimArguments = ((string?)e.Element("verbatim") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray() }));
    public static ServerLanguage FromIndex(int index) => index == 1 ? ServerLanguage.Ukrainian : index == 2 ? ServerLanguage.Russian : ServerLanguage.English;
    public static int[] Slots(string text) => Placeholder.Matches(text).Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).OrderBy(i => i).ToArray();

    public string Translate(string text, ServerLanguage language, IEnumerable<string>? playerNames = null)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var names = new List<string>();
        string protectedText = text;
        if (playerNames != null)
            foreach (string name in playerNames.Where(n => !string.IsNullOrEmpty(n)).Distinct().OrderByDescending(n => n.Length))
            {
                string key = "\uE000" + names.Count + "\uE001";
                string pattern = (char.IsLetterOrDigit(name[0]) ? @"(?<![\p{L}\p{N}_])" : "") + Regex.Escape(name) + (char.IsLetterOrDigit(name[name.Length - 1]) ? @"(?![\p{L}\p{N}_])" : "");
                string next = Regex.Replace(protectedText, pattern, _ => key);
                if (next == protectedText) continue;
                names.Add(name);
                protectedText = next;
            }
        // Names use stable placeholders, so cached text never stores a player's name.
        string cacheKey = (int)language + ":" + protectedText;
        if (!cache.TryGetValue(cacheKey, out var translated))
        {
            translated = TranslatePart(protectedText, language, 0);
            if (cache.Count >= 1024) cache.Clear();
            if (protectedText.Length <= 8192) cache[cacheKey] = translated;
        }
        for (int i = 0; i < names.Count; i++) translated = translated.Replace("\uE000" + i + "\uE001", names[i]);
        return translated;
    }

    private string TranslatePart(string text, ServerLanguage language, int depth)
    {
        if (depth > 10 || string.IsNullOrWhiteSpace(text)) return text;
        string key = text.Trim();
        int leading = text.Length - text.TrimStart().Length;
        int trailing = text.Length - text.TrimEnd().Length;
        string Wrap(string result) => text.Substring(0, leading) + result + (trailing == 0 ? "" : text.Substring(text.Length - trailing));
        if (exact.TryGetValue(key, out var entry)) return Wrap(entry.In(language).Trim());
        foreach (Rule rule in templates)
        {
            Match match = rule.Pattern.Match(key);
            if (!match.Success) continue;
            string target = rule.Entry.In(language).Trim();
            return Wrap(Placeholder.Replace(target, slot =>
            {
                string argument = match.Groups["p" + slot.Groups[1].Value].Value;
                return rule.Entry.VerbatimArguments.Contains(int.Parse(slot.Groups[1].Value)) ? argument : TranslatePart(argument, language, depth + 1);
            }));
        }
        // Parse only real rich-text tags. Comparisons such as <50 HP and console
        // arguments such as <number> remain ordinary text.
        if (Tags.IsMatch(text))
        {
            var output = new System.Text.StringBuilder();
            int offset = 0;
            bool noparse = false;
            foreach (Match tag in Tags.Matches(text))
            {
                string node = text.Substring(offset, tag.Index - offset);
                output.Append(noparse ? node : TranslatePart(node, language, depth + 1));
                output.Append(tag.Value);
                if (tag.Value.Equals("<noparse>", StringComparison.OrdinalIgnoreCase)) noparse = true;
                if (tag.Value.Equals("</noparse>", StringComparison.OrdinalIgnoreCase)) noparse = false;
                offset = tag.Index + tag.Length;
            }
            string end = text.Substring(offset);
            output.Append(noparse ? end : TranslatePart(end, language, depth + 1));
            return output.ToString();
        }
        if (text.Contains("\n")) return string.Join("\n", text.Split('\n').Select(line => TranslatePart(line, language, depth + 1)));
        return literals.Replace(text, match => exact[match.Value].In(language).Trim());
    }

    private sealed class Rule
    {
        public LocalizedText Entry { get; }
        public Regex Pattern { get; }
        public int Specificity { get; }
        public Rule(LocalizedText entry)
        {
            Entry = entry;
            string source = entry.Source.Trim();
            Specificity = Placeholder.Replace(source, "").Length;
            var pattern = new System.Text.StringBuilder(@"\A");
            int offset = 0;
            var seen = new HashSet<string>();
            foreach (Match slot in Placeholder.Matches(source))
            {
                pattern.Append(Regex.Escape(source.Substring(offset, slot.Index - offset)));
                string name = "p" + slot.Groups[1].Value;
                pattern.Append(seen.Add(name) ? "(?<" + name + ">.*?)" : @"\k<" + name + ">");
                offset = slot.Index + slot.Length;
            }
            pattern.Append(Regex.Escape(source.Substring(offset))).Append(@"\z");
            Pattern = new Regex(pattern.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50));
        }
    }
}
