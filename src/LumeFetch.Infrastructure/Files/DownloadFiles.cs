using System.Globalization;

namespace LumeFetch.Infrastructure.Files;

public static class DownloadFiles
{
    public static string WorkingDirectory(string destination, Guid jobId)
    {
        var path = Path.Combine(Path.GetFullPath(destination), ".lumefetch", jobId.ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Atomic no-overwrite move, including concurrent jobs with identical titles.</summary>
    public static string Commit(string source, string destination, string fileName)
    {
        var stem = SafeFileName.Create(Path.GetFileNameWithoutExtension(fileName));
        var extension = Path.GetExtension(fileName);
        for (var index = 1; index < 10000; index++)
        {
            var suffix = index == 1 ? string.Empty : " (" + index.ToString(CultureInfo.InvariantCulture) + ")";
            var output = Path.Combine(destination, stem + suffix + extension);
            try { File.Move(source, output, overwrite: false); return output; }
            catch (IOException) when (File.Exists(output)) { }
        }
        throw new IOException("Too many files share this name. Choose another folder.");
    }
}
