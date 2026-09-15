using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using LumeFetch.Presentation.Localization;
using DownloadStatus = LumeFetch.Core.Downloads.DownloadStatus;

namespace LumeFetch.Android.Services;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class DownloadForegroundService : Service
{
    private const string Channel = "lumefetch-transfers";
    private const int NotificationId = 1201;
    private static DownloadForegroundService? _running;
    private PowerManager.WakeLock? _wakeLock;
    private long _lastNotification;

    public override void OnCreate()
    {
        base.OnCreate();
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        using var channel = new NotificationChannel(Channel, "LumeFetch downloads", NotificationImportance.Low);
        manager.CreateNotificationChannel(channel);
        _running = this;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        using var notification = BuildNotification();
        if (OperatingSystem.IsAndroidVersionAtLeast(29)) StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        else StartForeground(NotificationId, notification);
        var app = Avalonia.Application.Current as MobileApp;
        if (app?.Queue?.Jobs.Any(job => job.Status is DownloadStatus.Downloading or DownloadStatus.Processing or DownloadStatus.Queued) != true)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }
        if (_wakeLock is null)
        {
            var power = (PowerManager)GetSystemService(PowerService)!;
            _wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "LumeFetch:transfers");
            // Bounded even if an unexpected platform lifecycle error prevents OnDestroy.
            _wakeLock!.Acquire((long)TimeSpan.FromHours(6).TotalMilliseconds);
        }
        return StartCommandResult.NotSticky;
    }

    internal static void RefreshNotification()
    {
        var service = _running;
        if (service is null || SystemClock.ElapsedRealtime() - service._lastNotification < 1000) return;
        service._lastNotification = SystemClock.ElapsedRealtime();
        using var notification = service.BuildNotification();
        var manager = (NotificationManager)service.GetSystemService(NotificationService)!;
        manager.Notify(NotificationId, notification);
    }

    private Notification BuildNotification()
    {
        var jobs = (Avalonia.Application.Current as MobileApp)?.Queue?.Jobs ?? [];
        var active = jobs.Count(job => job.Status is DownloadStatus.Downloading or DownloadStatus.Processing);
        var waiting = jobs.Count(job => job.Status == DownloadStatus.Queued);
        using var intent = new Intent(this, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        using var open = PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        using var builder = new Notification.Builder(this, Channel);
        return builder.SetSmallIcon(Resource.Drawable.ic_download)!.SetContentTitle("LumeFetch")!
            .SetContentText(Localizer.Current.Format("ActiveTransfers", active, waiting))!
            .SetOngoing(true)!.SetOnlyAlertOnce(true)!.SetContentIntent(open)!.Build();
    }

    public override void OnTimeout(int startId, ForegroundService fgsType)
    {
        (Avalonia.Application.Current as MobileApp)?.SuspendTransfers("BackgroundTimeLimit");
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    public override void OnDestroy()
    {
        if (ReferenceEquals(_running, this)) _running = null;
        if (_wakeLock?.IsHeld == true) _wakeLock.Release();
        _wakeLock?.Dispose();
        _wakeLock = null;
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;
}
