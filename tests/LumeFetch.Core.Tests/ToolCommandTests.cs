using LumeFetch.Infrastructure.Tools;

namespace LumeFetch.Core.Tests;

public sealed class ToolCommandTests
{
    [Fact]
    public void HostArgumentsAndEnvironmentAreCopiedWithoutAShell()
    {
        var prefix = new[] { "bootstrap.py", "yt-dlp" };
        var environment = new Dictionary<string, string> { ["PYTHONHOME"] = "/private/runtime/usr" };
        var command = new ToolCommand("/native/libpython.so", prefix, environment);
        prefix[0] = "changed";
        environment["PYTHONHOME"] = "changed";
        var info = command.CreateStartInfo(["--output", "name with spaces & $(text).mp3", "--", "https://example.org/?a=1&b=2"]);
        Assert.False(info.UseShellExecute);
        Assert.True(info.RedirectStandardOutput);
        Assert.Equal("bootstrap.py", info.ArgumentList[0]);
        Assert.Equal("name with spaces & $(text).mp3", info.ArgumentList[3]);
        Assert.Equal("/private/runtime/usr", info.Environment["PYTHONHOME"]);
        info.Environment["PYTHONHOME"] = "modified per process";
        Assert.Equal("/private/runtime/usr", command.CreateStartInfo([]).Environment["PYTHONHOME"]);
    }
}
