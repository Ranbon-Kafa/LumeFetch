using LumeFetch.Core.Media;
using LumeFetch.Presentation.Localization;

namespace LumeFetch.Presentation.ViewModels;

public sealed class DownloadOptionViewModel : ObservableObject
{
    public DownloadOptionViewModel(DownloadOption option)
    {
        Model = option;
    }

    public DownloadOption Model { get; }

    public string Container => Model.Container.ToUpperInvariant();

    public string Quality => Model.Height is > 0 ? Model.QualityLabel : Localizer.Current[Model.QualityLabel];

    public string Details
    {
        get
        {
            var codecs = new[] { Model.VideoCodec, Model.AudioCodec }
                .Where(value => !string.IsNullOrWhiteSpace(value));
            var text = string.Join(" + ", codecs);
            return string.IsNullOrWhiteSpace(text) ? Localizer.Current[Model.Kind.ToString()] : text;
        }
    }

    public string Size => FormatBytes(Model.EstimatedBytes);
    public void RefreshLanguage() => OnPropertyChanged(string.Empty);

    private static string FormatBytes(long? value)
    {
        if (value is null)
        {
            return Localizer.Current["SizeUnknown"];
        }

        var bytes = value.Value;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var amount = (double)bytes;
        var unit = 0;
        while (amount >= 1024 && unit < units.Length - 1)
        {
            amount /= 1024;
            unit++;
        }

        return $"{amount:0.#} {units[unit]}";
    }
}
