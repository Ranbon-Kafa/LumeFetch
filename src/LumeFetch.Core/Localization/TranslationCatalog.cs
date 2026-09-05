using System.Globalization;
using System.Text.Json;

namespace LumeFetch.Core.Localization;

public sealed class TranslationCatalog
{
    private readonly Dictionary<string, string> _fallback = Read("en");
    private readonly Dictionary<string, string> _strings;
    public string Language { get; }
    public CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "tr" ? "tr-TR" : "en-US");
    public TranslationCatalog(string language)
    {
        Language = language is "tr" ? "tr" : "en";
        _strings = Read(Language);
    }
    public string this[string key] => _strings.GetValueOrDefault(key) ?? _fallback.GetValueOrDefault(key) ?? key;
    public string Format(string key, params object[] arguments) => string.Format(Culture, this[key], arguments);
    public IReadOnlyCollection<string> Keys => _strings.Keys;
    private static Dictionary<string, string> Read(string language)
    {
        using var stream = typeof(TranslationCatalog).Assembly.GetManifestResourceStream("LumeFetch.Core.Localization." + language + ".json")
            ?? throw new InvalidOperationException("Missing translation resource.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
