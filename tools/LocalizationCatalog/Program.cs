using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Xml.Linq;

if (args.Length == 3 && args[0] == "--compile-catalog")
{
    using var document = JsonDocument.Parse(File.ReadAllText(args[1]));
    var xml = new XElement("catalog", document.RootElement.EnumerateArray().Select(entry => new XElement("entry",
        new[] { "source", "en", "uk", "ru" }.Select(key => new XElement(key, entry.GetProperty(key).GetString())),
        entry.TryGetProperty("verbatim", out var slots) ? new XElement("verbatim", string.Join(",", slots.EnumerateArray().Select(s => s.GetInt32()))) : null)));
    xml.Save(args[2]);
    Console.WriteLine($"Compiled {xml.Elements().Count()} localization entries.");
    return;
}

if (args.Length != 2) throw new ArgumentException("Usage: LocalizationCatalog <source-folder> <output-json>");
var root = args[0] == "-" ? "stdin" : Path.GetFullPath(args[0]);
var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
IEnumerable<KeyValuePair<string, string>> sources = args[0] == "-"
    ? JsonSerializer.Deserialize<Dictionary<string, string>>(Console.In.ReadToEnd())!
    : Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(file => !file.Split(Path.DirectorySeparatorChar).Any(p => p is ".git" or "bin" or "obj")).Select(file => new KeyValuePair<string, string>(Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllText(file)));
foreach (var (file, source) in sources)
{
    var tree = CSharpSyntaxTree.ParseText(source);
    foreach (var node in tree.GetRoot().DescendantNodes())
    {
        string? text = null;
        if (node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)) text = literal.Token.ValueText;
        if (node is InterpolatedStringExpressionSyntax interpolation)
        {
            var builder = new StringBuilder();
            int index = 0;
            foreach (var content in interpolation.Contents)
                if (content is InterpolatedStringTextSyntax part) builder.Append(part.TextToken.ValueText);
                else builder.Append('{').Append(index++).Append('}');
            text = builder.ToString();
        }
        if (text == null || !text.Any(char.IsLetter)) continue;
        var property = node.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
        // Retain even single-word strings for review: UI includes Heads/Tails and
        // item names, while resource IDs are explicitly excluded in the catalog.
        var parent = node.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        var position = tree.GetLineSpan(node.Span).StartLinePosition;
        var site = new Site(file, position.Line + 1, property?.Identifier.ValueText, parent?.Expression.ToString());
        if (!entries.TryGetValue(text, out var entry)) entries[text] = entry = new(text, new());
        entry.Sites.Add(site);
    }
}
var result = entries.Values.OrderBy(e => e.Source, StringComparer.Ordinal).ToArray();
File.WriteAllText(args[1], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"Extracted {result.Length} distinct text templates from {root}.");

record Site(string File, int Line, string? Property, string? Call);
record Entry(string Source, List<Site> Sites);
