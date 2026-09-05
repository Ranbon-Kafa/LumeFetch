namespace LumeFetch.Core.Downloads;

internal sealed class PauseController : IDownloadControl
{
    private readonly object _sync = new();
    private TaskCompletionSource _resumeSource = CreateCompletedSource();

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        lock (_sync)
        {
            if (IsPaused)
            {
                return;
            }

            IsPaused = true;
            _resumeSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (!IsPaused)
            {
                return;
            }

            IsPaused = false;
            _resumeSource.TrySetResult();
        }
    }

    public ValueTask WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        Task waitTask;
        lock (_sync)
        {
            waitTask = _resumeSource.Task;
        }

        return waitTask.IsCompletedSuccessfully
            ? ValueTask.CompletedTask
            : new ValueTask(waitTask.WaitAsync(cancellationToken));
    }

    private static TaskCompletionSource CreateCompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
