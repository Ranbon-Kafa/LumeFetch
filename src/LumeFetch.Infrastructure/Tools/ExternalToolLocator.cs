namespace LumeFetch.Infrastructure.Tools;

public static class ExternalToolLocator
{
    public static string? Find(string toolName, params string[] additionalDirectories)
    {
        var executableName = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;
        var appDirectory = AppContext.BaseDirectory;

        var candidateDirectories = additionalDirectories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Prepend(Path.Combine(appDirectory, "tools", toolName))
            .Prepend(appDirectory)
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var directory in candidateDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
