using Android.Content;
using Avalonia.Threading;
using LumeFetch.Core.Downloads;
using DownloadManager = LumeFetch.Core.Downloads.DownloadManager;
using DownloadStatus = LumeFetch.Core.Downloads.DownloadStatus;

namespace LumeFetch.Android.Services;

internal sealed class AndroidQueueLifetime : IDisposable
{
    private readonly Context _context;
    private readonly DownloadManager _manager;
    private readonly Action<string> _report;
    private bool _requested;
    private bool _disposed;
    private int _scheduled;

    public AndroidQueueLifetime(Context context, DownloadManager manager, Action<string> report)
    {
        _context = context;
        _manager = manager;
        _report = report;
        manager.JobChanged += OnJobChanged;
    }

    private void OnJobChanged(object? sender, DownloadJobSnapshot snapshot)
    {
        if (Interlocked.Exchange(ref _scheduled, 1) != 0) return;
        Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        Interlocked.Exchange(ref _scheduled, 0);
        if (_disposed) return;
        var busy = _manager.Jobs.Any(job => job.Status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Processing);
        if (!busy)
        {
            if (_requested) _context.StopService(new Intent(_context, typeof(DownloadForegroundService)));
            _requested = false;
            return;
        }
        try
        {
            if (!_requested)
            {
                MainActivity.RequestTransferNotifications();
                _context.StartForegroundService(new Intent(_context, typeof(DownloadForegroundService)));
                _requested = true;
            }
            DownloadForegroundService.RefreshNotification();
        }
        catch (Exception)
        {
            _requested = false;
            Suspend("BackgroundUnavailable");
        }
    }

    public void Suspend(string message)
    {
        // Free all slots first so pausing one transfer cannot start a waiting transfer.
        foreach (var job in _manager.Jobs.Where(job => job.Status == DownloadStatus.Queued)) _manager.Pause(job.Id);
        foreach (var job in _manager.Jobs)
        {
            if (job.Status == DownloadStatus.Processing) _manager.Cancel(job.Id);
            else if (job.Status == DownloadStatus.Downloading) _manager.Pause(job.Id);
        }
        _report(message);
    }

    public void Dispose()
    {
        _disposed = true;
        _manager.JobChanged -= OnJobChanged;
        if (_requested) _context.StopService(new Intent(_context, typeof(DownloadForegroundService)));
    }
}
