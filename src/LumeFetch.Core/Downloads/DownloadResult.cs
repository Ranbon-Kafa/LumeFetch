namespace LumeFetch.Core.Downloads;

public sealed record DownloadResult(string OutputPath, long BytesWritten);
