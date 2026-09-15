using System.Text.Json;
using LumeFetch.Core.Settings;

namespace LumeFetch.Infrastructure.Settings;

public sealed class JsonSettingsStore(string filePath, AppSettings? defaults = null) : ISettingsStore
{
    private readonly AppSettings _defaults = defaults ?? new AppSettings();
    public string FilePath { get; } = Path.GetFullPath(filePath);
    public string? LoadWarning { get; private set; }

    public static JsonSettingsStore CreateDefault()
    {
        var directory = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"))
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumeFetch");
        return new JsonSettingsStore(Path.Combine(directory, "settings.json"));
    }

    public AppSettings Load()
    {
        LoadWarning = null;
        if (!File.Exists(FilePath)) return _defaults;
        try
        {
            var settings = JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJsonContext.Default.AppSettings)
                ?? throw new InvalidDataException("Empty settings.");
            settings.Validate();
            return settings;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            LoadWarning = "Settings could not be loaded; defaults are in use. The original file is unchanged. " + exception.Message;
            return _defaults;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
