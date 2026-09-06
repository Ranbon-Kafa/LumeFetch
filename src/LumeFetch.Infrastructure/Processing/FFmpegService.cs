using System.Diagnostics;
using LumeFetch.Core.Processing;
using LumeFetch.Infrastructure.Tools;

namespace LumeFetch.Infrastructure.Processing;

public sealed class FFmpegService : IFFmpegService
{
    public FFmpegService(string? executablePath = null)
    {
        ExecutablePath = executablePath ?? ExternalToolLocator.Find("ffmpeg", Path.Combine(Environment.CurrentDirectory, ".tools", "ffmpeg-lumefetch"));
    }

    public string? ExecutablePath { get; }

    public bool IsAvailable => ExecutablePath is not null;

    public async Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        if (ExecutablePath is null)
        {
            return null;
        }

        var result = await RunAsync(["-version"], cancellationToken).ConfigureAwait(false);
        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }

    public async Task MuxAsync(
        string videoPath,
        string audioPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(videoPath);
        ValidateInput(audioPath);
        EnsureOutputDirectory(outputPath);

        await RunAsync(
            ["-hide_banner", "-nostdin", "-n", "-i", videoPath, "-i", audioPath, "-map", "0:v:0", "-map", "1:a:0", "-c", "copy", outputPath],
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ExtractAudioAsync(
        string inputPath,
        string outputPath,
        string codec,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(inputPath);
        EnsureOutputDirectory(outputPath);

        var arguments = new List<string> { "-hide_banner", "-nostdin", "-n", "-i", inputPath, "-map", "0:a:0", "-vn", "-c:a", codec };
        if (codec == "libmp3lame") arguments.AddRange(["-q:a", "0", "-id3v2_version", "3"]);
        arguments.Add(outputPath);
        await RunAsync(arguments, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (ExecutablePath is null)
        {
            throw new FileNotFoundException(
                "FFmpeg was not found. Add it to PATH or place it in the application's tools/ffmpeg directory.");
        }

        var startInfo = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("FFmpeg could not be started.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            throw;
        }

        var result = new ProcessResult(
            process.ExitCode,
            await standardOutputTask.ConfigureAwait(false),
            await standardErrorTask.ConfigureAwait(false));

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg failed: {LastUsefulLine(result.StandardError)}");
        }

        return result;
    }

    private static void ValidateInput(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Input media was not found.", path);
        }
    }

    private static void EnsureOutputDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string LastUsefulLine(string value) => value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .LastOrDefault() ?? "Unknown process error.";

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
