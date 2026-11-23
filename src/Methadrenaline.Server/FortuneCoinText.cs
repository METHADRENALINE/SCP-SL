using Methadrenaline.Server.Core;

namespace Methadrenaline.Server;

internal sealed class FortuneCoinText
{
    private readonly LocalizationCatalog? catalog = Localizer.Current?.Catalog;

    internal string this[string key] => catalog?.Translate(key, ServerLanguage.Russian) ?? key;
}
