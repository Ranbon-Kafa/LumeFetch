using System.Diagnostics;
using LumeFetch.Infrastructure.Processing;
using LumeFetch.Infrastructure.Settings;
using LumeFetch.Infrastructure.Tools;

namespace LumeFetch.Desktop;

internal static class RuntimeSelfCheck
{
    public static async Task<int> RunAsync()
    {
        try
        {
            var store = JsonSettingsStore.CreateDefault();
            var settings = store.Load();
            var ffmpeg = new FFmpegService(settings.FFmpegPath);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var version = await ffmpeg.GetVersionAsync(timeout.Token);
            if (version is null) throw new FileNotFoundException("FFmpeg not found.");
            Console.WriteLine(version);
            foreach (var name in new[] { "yt-dlp", "deno" })
            {
                var path = name == "yt-dlp" ? settings.YtDlpPath ?? ExternalToolLocator.Find(name) : ExternalToolLocator.Find(name);
                if (path is null) throw new FileNotFoundException(name + " not found.");
                var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add("--version");
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start " + name);
                var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
                var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
                try { await process.WaitForExitAsync(timeout.Token); }
                catch
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                    await Task.WhenAll(output, error);
                    throw;
                }
                if (process.ExitCode != 0) throw new InvalidOperationException(await error);
                Console.WriteLine(name + ": " + (await output).Trim());
            }
            Console.WriteLine("Settings: " + store.FilePath);
            Console.WriteLine("PASS: self-contained runtime and bundled tools.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 1; }
    }
}
