using System.Text.Json;
using System.Text.Json.Serialization;
using LumeFetch.Core.Downloads;

namespace LumeFetch.Infrastructure.Downloads;

/// <summary>Same-directory replacement, flushed before commit. Never repairs or overwrites an unreadable journal.</summary>
public sealed class JsonDownloadQueueStore(string filePath) : IDownloadQueueStore
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private readonly string _path = Path.GetFullPath(filePath);

    public DownloadQueueCheckpoint Load()
    {
        if (!File.Exists(_path)) return new(1, []);
        using var stream = File.OpenRead(_path);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Queue journal is too large.");
        var checkpoint = JsonSerializer.Deserialize(stream, QueueJsonContext.Default.DownloadQueueCheckpoint)
            ?? throw new InvalidDataException("Queue journal is empty.");
        if (checkpoint.SchemaVersion != 1 || checkpoint.Jobs is null || checkpoint.Jobs.Count > 2000)
            throw new InvalidDataException("Unsupported queue journal.");
        return checkpoint;
    }

    public void Save(DownloadQueueCheckpoint checkpoint)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(checkpoint, QueueJsonContext.Default.DownloadQueueCheckpoint);
        if (bytes.Length > MaximumBytes || checkpoint.Jobs.Count > 2000) throw new IOException("Queue journal capacity exceeded.");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            // A cleanup error must not turn a committed checkpoint into a reported failure.
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(DownloadQueueCheckpoint))]
internal sealed partial class QueueJsonContext : JsonSerializerContext;
