using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LumeFetch.Core.Downloads;
using LumeFetch.Core.Processing;
using LumeFetch.Core.Providers;
using LumeFetch.Core.Services;
using LumeFetch.Core.Settings;
using LumeFetch.Infrastructure.Settings;
using LumeFetch.Presentation.ViewModels;
using LumeFetch.Presentation.Views;

namespace LumeFetch.Screenshot;

internal static partial class Program
{
    private static async Task VerifyProcessingAvailabilityAsync(string output)
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "tool-diagnostics-smoke", Guid.NewGuid().ToString("N"));
        var settings = new AppSettings { DownloadDirectory = directory, SmartPaste = false, Language = "tr" };
        var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"), settings);
        var registry = new ProviderRegistry([new PreviewProvider()]);
        await using var manager = new DownloadManager(registry);
        var processing = new DiagnosticsFixture();
        using var vm = new MainWindowViewModel(new MediaAnalysisService(registry), manager, processing, store, settings,
            platformName: "Android", applicationVersion: "1.1.0-beta.1",
            processingContext: "Android API 36 · Arm64 · 16384 B pages\nLumeFetch test · build 10");
        var view = new MainView { DataContext = vm };
        var host = new Window { Width = 320, Height = 844, Content = view };
        host.Show();
        await vm.InitializeAsync();
        Require(vm.HasProcessingIssue && vm.ToolDiagnostics.Contains("FFprobe exited with code 127", StringComparison.Ordinal),
            "Startup failure preserves real native error");
        vm.ShowSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Require(view.FindControl<TextBox>("ToolDiagnosticsText") is null, "Developer diagnostics panel is removed");
        Require(!view.GetVisualDescendants().OfType<TextBox>().Any(field => field.Text == vm.ToolDiagnostics),
            "Internal diagnostics are not displayed in end-user settings");
        foreach (var language in vm.Languages)
        {
            vm.SelectedLanguage = language;
            CaptureResponsive(host, view, vm, Path.Combine(output, $"clean-settings-{language.Code}.png"));
            AssertHorizontalLayout(view);
            vm.ShowHomeCommand.Execute(null);
            CaptureResponsive(host, view, vm, Path.Combine(output, $"processing-unavailable-{language.Code}.png"));
            AssertHorizontalLayout(view);
            vm.ShowSettingsCommand.Execute(null);
        }
        processing.Fail = false;
        vm.CheckToolsCommand.Execute(null);
        await UntilAsync(() => !vm.CheckToolsCommand.IsRunning);
        Require(!vm.HasProcessingIssue && vm.ToolDiagnostics.Contains("ffprobe version fixture", StringComparison.Ordinal),
            "Successful retry replaces the stale failure and clears the banner");
        await vm.ShutdownAsync();
        host.Close();
        Console.WriteLine("PASS: developer diagnostics removed; startup error detection, friendly retry, recovery and 320px TR/EN layouts retained.");
    }

    private sealed class DiagnosticsFixture : IFFmpegService
    {
        public bool Fail { get; set; } = true;
        public string? ExecutablePath => "bundled-ffmpeg";
        public bool IsAvailable => true;
        public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default) => Fail
            ? throw new InvalidOperationException("FFprobe exited with code 127: CANNOT LINK EXECUTABLE\nlibfixture.so not found")
            : Task.FromResult<string?>("ffmpeg version fixture\nffprobe version fixture");
        public Task MuxAsync(string videoPath, string audioPath, string outputPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ExtractAudioAsync(string inputPath, string outputPath, string codec, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
