using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia.Android;
using AndroidUri = Android.Net.Uri;

namespace LumeFetch.Android;

[Activity(Label = "LumeFetch", Theme = "@style/LumeFetchTheme", Icon = "@drawable/lumefetch",
    MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop, WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "text/plain")]
public sealed class MainActivity : AvaloniaMainActivity
{
    private const int FolderRequest = 3101;
    private static WeakReference<MainActivity>? _current;
    private TaskCompletionSource<AndroidUri?>? _folderSelection;
    private bool _notificationAsked;

    internal static void RequestTransferNotifications()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) || _current?.TryGetTarget(out var activity) != true || activity is null || activity._notificationAsked) return;
        activity._notificationAsked = true;
        if (activity.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != Permission.Granted)
            activity.RequestPermissions([global::Android.Manifest.Permission.PostNotifications], 3102);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _current = new(this);
        AcceptSharedUrl(Intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        _current = new(this);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        AcceptSharedUrl(intent);
    }

    protected override void OnDestroy()
    {
        _folderSelection?.TrySetResult(null);
        base.OnDestroy();
    }

    internal static Task<AndroidUri?> PickFolderAsync()
    {
        if (_current?.TryGetTarget(out var activity) != true || activity is null || activity.IsFinishing)
            throw new InvalidOperationException("The Android window is unavailable.");
        if (activity._folderSelection is not null) return activity._folderSelection.Task;
        activity._folderSelection = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = activity._folderSelection.Task;
        try
        {
            var intent = new Intent(Intent.ActionOpenDocumentTree);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPersistableUriPermission);
            activity.StartActivityForResult(intent, FolderRequest);
        }
        catch { activity._folderSelection = null; throw; }
        return task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != FolderRequest) return;
        var completion = _folderSelection;
        _folderSelection = null;
        try
        {
            if (resultCode != Result.Ok || data?.Data is not { } uri) { completion?.TrySetResult(null); return; }
            var flags = data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            if (!flags.HasFlag(ActivityFlags.GrantWriteUriPermission)) throw new IOException("The folder did not grant write access.");
            ContentResolver!.TakePersistableUriPermission(uri, flags);
            completion?.TrySetResult(uri);
        }
        catch (Exception exception) { completion?.TrySetException(exception); }
    }

    private static void AcceptSharedUrl(Intent? intent)
    {
#if DEBUG
        if (intent?.GetBooleanExtra("lumefetch.runtime-smoke", false) == true && Avalonia.Application.Current is MobileApp testApp)
            testApp.RequestRuntimeSmoke();
#endif
        if (intent?.Action != Intent.ActionSend || intent.Type != "text/plain") return;
        var text = intent.GetStringExtra(Intent.ExtraText);
        if (text is null || text.Length > 16384) return;
        // Only take an HTTP(S) URL; never forward intent extras as executable options.
        var url = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => Uri.TryCreate(part, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
        if (url is not null && Avalonia.Application.Current is MobileApp app) app.ReceiveUrl(url);
    }
}
