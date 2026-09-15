using System.Security.Cryptography;
using System.Text;
using Android.Content;
using Android.Provider;
using LumeFetch.Core.Downloads;
using LumeFetch.Presentation.Localization;
using AndroidUri = Android.Net.Uri;

namespace LumeFetch.Android.Storage;

/// <summary>SAF grants are private to this application. No broad storage permission is needed.</summary>
internal sealed class AndroidDownloadOutput : IDownloadOutput, IDisposable
{
    private readonly ContentResolver _resolver;
    private readonly ISharedPreferences _preferences;
    private readonly string _workingRoot;

    public AndroidDownloadOutput(Context context)
    {
        _resolver = context.ContentResolver!;
        _preferences = context.GetSharedPreferences("download-folders", FileCreationMode.Private)!;
        _workingRoot = Path.Combine(context.FilesDir!.AbsolutePath, "transfers");
    }

    public string DefaultDirectory => Path.Combine(_workingRoot, "unselected");

    public string GetLabel(string directory) =>
        _preferences.GetString(Key(directory) + ".label", null) ?? Localizer.Current["ChooseFolder"];

    public async Task<string?> PickFolderAsync()
    {
        var selection = await MainActivity.PickFolderAsync();
        if (selection is null) return null;
        return await Task.Run(() => RegisterFolder(selection));
    }

    private string RegisterFolder(AndroidUri tree)
    {
        var uri = tree.ToString() ?? throw new IOException("The folder did not return an address.");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri))).ToLowerInvariant();
        var directory = Path.Combine(_workingRoot, key);
        var document = DocumentsContract.BuildDocumentUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))
            ?? throw new IOException("The folder is unavailable.");
        var label = "LumeFetch";
        using (var cursor = _resolver.Query(document, [DocumentsContract.Document.ColumnDisplayName], null, null, null))
            if (cursor?.MoveToFirst() == true) label = cursor.GetString(0) ?? label;
        using var editor = _preferences.Edit()!;
        editor.PutString(Key(directory) + ".uri", uri);
        editor.PutString(Key(directory) + ".label", label);
        if (!editor.Commit()) throw new IOException("The selected folder could not be saved.");
        ValidateDestination(directory);
        return directory;
    }

    public void ValidateDestination(string destinationDirectory)
    {
        var tree = GetTree(destinationDirectory);
        if (!_resolver.PersistedUriPermissions!.Any(permission => permission.IsWritePermission && permission.Uri?.ToString() == tree.ToString()))
            throw new IOException(Localizer.Current["FolderPermissionLost"]);
    }

    public void ValidateWorkingDirectory(string directory)
    {
        var path = Path.GetFullPath(directory);
        var key = Path.GetFileName(path);
        if (!string.Equals(Path.GetDirectoryName(path), _workingRoot, StringComparison.Ordinal) ||
            key.Length != 64 || key.Any(character => !char.IsAsciiHexDigit(character)))
            throw new IOException("Invalid saved transfer directory.");
    }

    public async Task<DownloadResult> PublishAsync(DownloadResult localFile, string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ValidateDestination(destinationDirectory);
        var localRoot = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(localFile.OutputPath).StartsWith(localRoot, StringComparison.Ordinal))
            throw new IOException("The completed file is outside its transfer directory.");
        var tree = GetTree(destinationDirectory);
        var parent = DocumentsContract.BuildDocumentUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))!;
        // An unpredictable suffix avoids collisions with files created by another app/provider.
        var name = Path.GetFileNameWithoutExtension(localFile.OutputPath) + "-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(localFile.OutputPath);
        AndroidUri? created = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            created = DocumentsContract.CreateDocument(_resolver, parent, MimeType(Path.GetExtension(name)), name)
                ?? throw new IOException(Localizer.Current["FolderWriteFailed"]);
            await using (var source = File.OpenRead(localFile.OutputPath))
            await using (var target = _resolver.OpenOutputStream(created, "w") ?? throw new IOException(Localizer.Current["FolderWriteFailed"]))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (source.Position != localFile.BytesWritten) throw new IOException("The completed file size changed during export.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            var result = new DownloadResult(created.ToString()!, localFile.BytesWritten, GetLabel(destinationDirectory) + "/" + name);
            // Committed: never report failure or delete the user's destination due to private-cache cleanup.
            created = null;
            try { File.Delete(localFile.OutputPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return result;
        }
        finally
        {
            if (created is not null)
            {
                // Only the fresh document created by this attempt may be removed.
                try { DocumentsContract.DeleteDocument(_resolver, created); }
                catch (Exception exception) { global::Android.Util.Log.Warn("LumeFetch", "Partial export cleanup failed: " + exception.GetType().Name); }
            }
        }
    }

    public void Dispose() => _preferences.Dispose();

    private AndroidUri GetTree(string directory)
    {
        var uri = _preferences.GetString(Key(directory) + ".uri", null);
        return uri is null ? throw new IOException(Localizer.Current["ChooseFolderBeforeDownload"]) : AndroidUri.Parse(uri)!;
    }

    private string Key(string directory)
    {
        // The unselected default is allowed only for displaying the initial folder label.
        if (string.Equals(directory, DefaultDirectory, StringComparison.Ordinal)) return "unselected";
        ValidateWorkingDirectory(directory);
        var path = Path.GetFullPath(directory);
        if (!path.StartsWith(_workingRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("Choose a destination using Android's folder picker.");
        return Path.GetFileName(path);
    }

    private static string MimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".opus" => "audio/ogg",
        ".mp4" => "video/mp4",
        ".mkv" => "video/x-matroska",
        ".webm" => "video/webm",
        _ => "application/octet-stream",
    };
}
