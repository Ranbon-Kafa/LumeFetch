using System.Diagnostics;
using System.Text;

namespace LumeFetch.Infrastructure.Tools;

/// <summary>A packaged executable plus immutable host configuration. Never invokes a shell.</summary>
public sealed class ToolCommand
{
    private readonly string[] _prefixArguments;
    private readonly Dictionary<string, string> _environment;
    private readonly Action<Process>? _terminate;

    public ToolCommand(string executablePath, IEnumerable<string>? prefixArguments = null,
        IReadOnlyDictionary<string, string>? environment = null, Action<Process>? terminate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ExecutablePath = executablePath;
        _prefixArguments = prefixArguments?.ToArray() ?? [];
        _environment = environment is null ? [] : new Dictionary<string, string>(environment, StringComparer.Ordinal);
        _terminate = terminate;
    }

    public string ExecutablePath { get; }

    public ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in _prefixArguments) info.ArgumentList.Add(argument);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        foreach (var (key, value) in _environment) info.Environment[key] = value;
        return info;
    }

    public void Terminate(Process process)
    {
        if (process.HasExited) return;
        if (_terminate is not null) _terminate(process);
        else process.Kill(entireProcessTree: true);
    }
}
