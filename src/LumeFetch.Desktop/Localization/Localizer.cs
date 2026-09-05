using Avalonia.Data;
using Avalonia.Markup.Xaml;
using LumeFetch.Core.Localization;
using LumeFetch.Desktop.ViewModels;

namespace LumeFetch.Desktop.Localization;

public sealed class Localizer : ObservableObject
{
    private TranslationCatalog _catalog = new("en");
    public static Localizer Current { get; } = new();
    public string this[string key] => _catalog[key];
    public string Language => _catalog.Language;
    public string Format(string key, params object[] arguments) => _catalog.Format(key, arguments);
    public void SetLanguage(string language)
    {
        if (Language == language) return;
        _catalog = new TranslationCatalog(language);
        System.Globalization.CultureInfo.CurrentCulture = _catalog.Culture;
        System.Globalization.CultureInfo.CurrentUICulture = _catalog.Culture;
        OnPropertyChanged(string.Empty);
        OnPropertyChanged("Item");
        OnPropertyChanged("Item[]");
    }
}

public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding("[" + key + "]") { Source = Localizer.Current };
}

public sealed record LanguageChoice(string Code, string Name)
{
    public override string ToString() => Name;
}
