using System.Diagnostics;
using LumeFetch.Core.Processing;
using LumeFetch.Infrastructure.Tools;

namespace LumeFetch.Infrastructure.Processing;

public sealed class FFmpegService : IFFmpegService
{
    private readonly ToolCommand? _command;
    private readonly ToolCommand? _probeCommand;

    public FFmpegService(string? executablePath = null, ToolCommand? command = null, ToolCommand? probeCommand = null)
    {
        ExecutablePath = command?.ExecutablePath ?? executablePath ?? ExternalToolLocator.Find("ffmpeg", Path.Combine(Environment.CurrentDirectory, ".tools", "ffmpeg-lumefetch"));
        _command = command ?? (ExecutablePath is null ? null : new ToolCommand(ExecutablePath));
        _probeCommand = probeCommand;
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
        var version = ReadVersion(result.StandardOutput, "ffmpeg");
        if (_probeCommand is not null)
        {
            var probe = await RunAsync(["-version"], cancellationToken, _probeCommand, "FFprobe").ConfigureAwait(false);
            version += "\n" + ReadVersion(probe.StandardOutput, "ffprobe");
        }
        return version;
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
        CancellationToken cancellationToken,
        ToolCommand? command = null,
        string toolName = "FFmpeg")
    {
        command ??= _command;
        if (command is null)
        {
            throw new FileNotFoundException(
                "FFmpeg was not found. Add it to PATH or place it in the application's tools/ffmpeg directory.");
        }

        var startInfo = command.CreateStartInfo(arguments);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Process.Start returned false.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException($"{toolName} could not start: {exception.Message}", exception);
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
                command.Terminate(process);
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
            throw new InvalidOperationException($"{toolName} exited with code {result.ExitCode}: {ErrorDetails(result.StandardError)}");
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

    private static string ReadVersion(string output, string name) => output
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .FirstOrDefault(line => line.StartsWith(name + " version ", StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"{name} returned no recognizable version information.");

    private static string ErrorDetails(string value)
    {
        // Linker errors can span several lines. Keep the exit code even if a native crash produced no stderr.
        var details = string.Join("\n", value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(8));
        if (details.Length == 0) return "No error output was produced.";
        return details.Length > 2000 ? details[^2000..] : details;
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
