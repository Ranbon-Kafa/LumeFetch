using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Android.Content;
using LumeFetch.Infrastructure.Tools;

namespace LumeFetch.Android.Runtime;

internal sealed partial class AndroidTools(string nativeDirectory, string runtimeDirectory, Dictionary<string, string> environment)
{
    public string FFmpegPath => Path.Combine(nativeDirectory, "libffmpeg.so");
    public string FFprobePath => Path.Combine(nativeDirectory, "libffprobe.so");
    public string JavaScriptRuntime => "quickjs:" + Path.Combine(nativeDirectory, "libqjs.so");
    public string ExtractorPath => Path.Combine(runtimeDirectory, "yt-dlp");
    public ToolCommand CreateFFmpegCommand(string? configured = null) => new(
        configured ?? FFmpegPath, environment: environment, terminate: process => process.Kill());
    public ToolCommand CreateYtDlpCommand(string? configured = null) => new(
        Path.Combine(nativeDirectory, "libpython.so"),
        [Path.Combine(runtimeDirectory, "bootstrap.py"), configured ?? ExtractorPath],
        environment, TerminateGroup);

    public static async Task<AndroidTools> PrepareAsync(Context context, CancellationToken token = default)
    {
        var assets = context.Assets ?? throw new InvalidOperationException("Android assets unavailable.");
        var native = context.ApplicationInfo?.NativeLibraryDir ?? throw new InvalidOperationException("Native library directory unavailable.");
        var abi = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64-v8a",
            Architecture.X64 => "x86_64",
            _ => throw new PlatformNotSupportedException("This APK requires a 64-bit Android device."),
        };
        using var manifestInput = assets.Open("tools/manifest.json");
        using var manifestBuffer = new MemoryStream();
        await manifestInput.CopyToAsync(manifestBuffer, token).ConfigureAwait(false);
        using var bootstrapInput = assets.Open("tools/bootstrap.py");
        using var bootstrapBuffer = new MemoryStream();
        await bootstrapInput.CopyToAsync(bootstrapBuffer, token).ConfigureAwait(false);
        var manifestBytes = manifestBuffer.ToArray();
        var bootstrapBytes = bootstrapBuffer.ToArray();
        var identity = Convert.ToHexString(SHA256.HashData(manifestBytes.Concat(bootstrapBytes).ToArray())).ToLowerInvariant();
        var root = Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "runtime", identity[..24]);
        using var manifest = JsonDocument.Parse(manifestBytes);
        var files = manifest.RootElement.GetProperty("files").EnumerateArray()
            .Where(file => file.GetProperty("abi").GetString() is "any" || file.GetProperty("abi").GetString() == abi).ToArray();
        // Native entry points run only from Android's installer-owned, read-only library directory.
        foreach (var file in files.Where(file => file.GetProperty("kind").GetString() == "native"))
            await VerifyAsync(Path.Combine(native, file.GetProperty("name").GetString()!), file.GetProperty("sha256").GetString()!, token).ConfigureAwait(false);

        var ready = Path.Combine(root, "ready.sha256");
        if (!File.Exists(ready) || await File.ReadAllTextAsync(ready, token).ConfigureAwait(false) != identity)
        {
            // A fresh sibling staging directory prevents exposing a half-extracted runtime.
            var staging = root + ".staging-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            try
            {
                foreach (var file in files.Where(file => file.GetProperty("kind").GetString() != "native"))
                {
                    token.ThrowIfCancellationRequested();
                    var name = file.GetProperty("name").GetString()!;
                    var archive = file.GetProperty("kind").GetString() == "archive";
                    var path = Path.Combine(staging, name + (archive ? ".zip" : string.Empty));
                    using (var source = assets.Open(file.GetProperty("asset").GetString()!))
                    await using (var destination = File.Create(path))
                        await source.CopyToAsync(destination, token).ConfigureAwait(false);
                    await VerifyAsync(path, file.GetProperty("sha256").GetString()!, token).ConfigureAwait(false);
                    if (archive)
                    {
                        ExtractArchive(path, Path.Combine(staging, name), token);
                        File.Delete(path);
                    }
                }
                await File.WriteAllBytesAsync(Path.Combine(staging, "bootstrap.py"), bootstrapBytes, token).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(staging, "ready.sha256"), identity, token).ConfigureAwait(false);
                // Existing corrupt cache is not overwritten; report it instead of mutating unrelated files.
                Directory.Move(staging, root);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
        }
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LD_LIBRARY_PATH"] = Path.Combine(root, "python/usr/lib") + ":" + Path.Combine(root, "ffmpeg/usr/lib") + ":" + native,
            ["SSL_CERT_FILE"] = Path.Combine(root, "python/usr/etc/tls/cert.pem"),
            ["PYTHONHOME"] = Path.Combine(root, "python/usr"),
            ["PYTHONNOUSERSITE"] = "1",
            ["TMPDIR"] = context.CacheDir!.AbsolutePath,
            ["PATH"] = native + ":/system/bin",
        };
        return new AndroidTools(native, root, environment);
    }

    private static async Task VerifyAsync(string path, string expected, CancellationToken token)
    {
        await using var input = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bundled tool verification failed: " + Path.GetFileName(path));
    }

    private static void ExtractArchive(string path, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(path);
        var links = new List<(string Path, string Target)>();
        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            totalBytes += entry.Length;
            if (archive.Entries.Count > 30_000 || totalBytes > 1024L * 1024 * 1024) throw new InvalidDataException("Unexpected runtime archive size.");
            var output = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!output.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException("Unsafe runtime archive entry.");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(output); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var mode = (entry.ExternalAttributes >> 16) & 0xffff;
            if ((mode & 0xf000) == 0xa000)
            {
                if (entry.Length > 4096) throw new InvalidDataException("Unexpected runtime link.");
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var target = reader.ReadToEnd();
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(output)!, target));
                if (!resolved.StartsWith(root, StringComparison.Ordinal)) throw new InvalidDataException("Runtime link escapes its package.");
                links.Add((output, target));
            }
            else
            {
                entry.ExtractToFile(output);
                File.SetUnixFileMode(output, (UnixFileMode)(mode & 0x1ff));
            }
        }
        foreach (var link in links) File.CreateSymbolicLink(link.Path, link.Target);
    }

    private static void TerminateGroup(Process process)
    {
        // bootstrap.py establishes this private group before extraction starts. Never signal an inherited group.
        if (GetProcessGroup(process.Id) == process.Id) _ = Kill(-process.Id, 9);
        else process.Kill();
    }

    [LibraryImport("libc", EntryPoint = "getpgid")]
    private static partial int GetProcessGroup(int processId);
    [LibraryImport("libc", EntryPoint = "kill")]
    private static partial int Kill(int processId, int signal);
}
