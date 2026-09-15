namespace LumeFetch.Core.Settings;

/// <summary>Settings persistence supplied by the host; no desktop path assumptions in shared UI.</summary>
public interface ISettingsStore
{
    string FilePath { get; }
    string? LoadWarning { get; }
    AppSettings Load();
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
