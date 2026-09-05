namespace LumeFetch.Core.Downloads;

public enum DownloadStatus
{
    Queued = 0,
    Downloading = 1,
    Paused = 2,
    Processing = 3,
    Completed = 4,
    Failed = 5,
    Canceled = 6,
}
