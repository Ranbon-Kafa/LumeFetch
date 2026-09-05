namespace LumeFetch.Core.Media;

public sealed class MediaAnalysisException : Exception
{
    public MediaAnalysisException(string message)
        : base(message)
    {
    }

    public MediaAnalysisException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
