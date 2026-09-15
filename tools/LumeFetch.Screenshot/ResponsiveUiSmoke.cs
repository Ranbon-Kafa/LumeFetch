using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LumeFetch.Core.Downloads;
using LumeFetch.Core.Providers;
using LumeFetch.Core.Services;
using LumeFetch.Core.Settings;
using LumeFetch.Infrastructure.Processing;
using LumeFetch.Infrastructure.Settings;
using LumeFetch.Presentation.Controls;
using LumeFetch.Presentation.Localization;
using LumeFetch.Presentation.ViewModels;
using LumeFetch.Presentation.Views;

namespace LumeFetch.Screenshot;

internal static partial class Program
{
    // These are host-independent UI checks, not a claim of Android/iOS download support.
    private static async Task VerifyResponsiveUiAsync(string output)
    {
        Require(!typeof(MainView).Assembly.GetReferencedAssemblies().Any(assembly =>
            assembly.Name is "LumeFetch.Desktop" or "LumeFetch.Infrastructure" or "Avalonia.Desktop"),
            "Shared UI has no desktop/backend dependencies");
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "mobile-ui-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settings = new AppSettings { DownloadDirectory = directory, SmartPaste = false, Language = "en" };
        var store = new JsonSettingsStore(Path.Combine(directory, "settings.json"), settings);
        var registry = new ProviderRegistry([new PreviewProvider()]);
        await using var manager = new DownloadManager(registry);
        using var vm = new MainWindowViewModel(new MediaAnalysisService(registry), manager,
            new FFmpegService(), store, settings, collections: new PreviewCatalog(), platformName: "Android",
            applicationVersion: "1.1.0-beta.1");
        var view = new MainView { DataContext = vm };
        var host = new Window { Width = 390, Height = 844, Content = view };
        host.Show();
        await vm.AnalyzeFromPasteAsync("https://example.org/preview.mp4");
        var formats = view.FindControl<ListBox>("FormatOptions")!;
        vm.SelectedOption = vm.Options.Single(option => option.Container == "MP3");
        Dispatcher.UIThread.RunJobs();
        var selected = formats.SelectedItem;
        Require(ReferenceEquals(selected, vm.SelectedOption), "Compiled format selection binding");
        var input = view.FindControl<TextBox>("UrlInput")!;
        Require(input.Text == vm.Url, "Compiled URL binding");

        foreach (var width in new[] { 320, 360, 390, 430, 768, 1220, 390 })
        {
            host.Width = width;
            Dispatcher.UIThread.RunJobs();
            CaptureResponsive(host, view, vm, Path.Combine(output, $"responsive-{width}-home.png"));
            Require(view.Classes.Contains("compact") == (width < 760), "Navigation follows viewport width");
            var page = (StackPanel)view.FindControl<ScrollViewer>("HomeScrollView")!.Content!;
            Require(page.Margin.Left == (width < 760 ? 14 : 44), "Phone spacing applies without altering desktop spacing");
            Require(ReferenceEquals(formats.SelectedItem, selected) && formats.ItemCount == 4,
                "All formats and MP3 selection survive resizing");
            Require(vm.HasResult && vm.EnqueueCommand.CanExecute(null), "Analysis survives resizing");
            AssertHorizontalLayout(view);
        }

        foreach (var language in vm.Languages)
        {
            vm.SelectedLanguage = language;
            vm.ShowSettingsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var settingsScroll = view.FindControl<ScrollViewer>("SettingsScrollView")!;
            host.Width = 320;
            settingsScroll.Offset = default;
            CaptureResponsive(host, view, vm, Path.Combine(output, $"responsive-320-settings-{language.Code}.png"));
            AssertHorizontalLayout(view);
            host.Width = 390;
            settingsScroll.Offset = default;
            CaptureResponsive(host, view, vm, Path.Combine(output, $"responsive-390-settings-{language.Code}.png"));
            AssertHorizontalLayout(view);
            Require(view.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                text.IsEffectivelyVisible && text.Text == Localizer.Current["SettingsTitle"]), "Live translated settings title");
            settingsScroll.Offset = new Vector(0, settingsScroll.Extent.Height);
            CaptureResponsive(host, view, vm, Path.Combine(output, $"responsive-390-settings-bottom-{language.Code}.png"));
            Require(!vm.IsSpotifyAvailable, "Mobile layout does not re-enable Spotify");
        }
        vm.ShowHomeCommand.Execute(null);
        vm.EnqueueCommand.Execute(null);
        await UntilAsync(() => vm.Downloads.Count == 1 && vm.Downloads[0].ProgressValue > 0);
        var job = vm.Downloads[0];
        job.PrimaryActionCommand.Execute(null);
        await UntilAsync(() => manager.Jobs[0].Status == DownloadStatus.Paused);
        Dispatcher.UIThread.RunJobs();
        job.PrimaryActionCommand.Execute(null);
        await UntilAsync(() => manager.Jobs[0].Status == DownloadStatus.Downloading);
        Dispatcher.UIThread.RunJobs();
        var home = view.FindControl<ScrollViewer>("HomeScrollView")!;
        home.Offset = new Vector(0, home.Extent.Height);
        CaptureResponsive(host, view, vm, Path.Combine(output, "responsive-390-queue.png"));
        AssertHorizontalLayout(view);
        job.CancelCommand.Execute(null);
        await UntilAsync(() => manager.Jobs[0].Status == DownloadStatus.Canceled);
        Dispatcher.UIThread.RunJobs();
        job.PrimaryActionCommand.Execute(null);
        await UntilAsync(() => manager.Jobs[0].Status == DownloadStatus.Downloading);

        await vm.AnalyzeFromPasteAsync("https://www.youtube.com/playlist?list=fixture");
        home.Offset = new Vector(0, 420);
        CaptureResponsive(host, view, vm, Path.Combine(output, "responsive-390-playlist.png"));
        Require(vm.CollectionRows.Count == 4 && vm.BatchQualities.Any(choice => choice.Value == LumeFetch.Core.Collections.CollectionQuality.Mp3),
            "Playlist entries and batch MP3 choice preserved");
        AssertHorizontalLayout(view);
        vm.RightsConfirmed = true;
        vm.BatchEnqueueCommand.Execute(null);
        await UntilAsync(() => !vm.IsBatchBusy);
        Require(vm.CollectionRows[0].IsEnqueued && vm.CollectionRows[2].IsEnqueued, "Phone layout uses the original batch queue");
        await vm.ShutdownAsync();
        host.Close();
        Console.WriteLine("PASS: shared UI dependency boundary; 320/360/390/430/768/1220px layouts; unchanged MP3/playlist/queue commands; pause/resume/cancel/retry; live TR/EN; Spotify disabled.");
    }

    private static void CaptureResponsive(Window host, MainView view, MainWindowViewModel vm, string path)
    {
        var actualDirectory = vm.DestinationDirectory;
        var location = view.FindControl<TextBlock>("SettingsLocationLabel")!;
        var actualLocation = location.Text;
        try
        {
            vm.DestinationDirectory = Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!, "Downloads", "LumeFetch");
            location.SetCurrentValue(TextBlock.TextProperty, "LumeFetch · settings.json");
            Dispatcher.UIThread.RunJobs();
            using var frame = host.CaptureRenderedFrame() ?? throw new InvalidOperationException("No responsive frame.");
            frame.Save(path, PngBitmapEncoderOptions.Default);
        }
        finally
        {
            vm.DestinationDirectory = actualDirectory;
            location.SetCurrentValue(TextBlock.TextProperty, actualLocation);
        }
    }

    private static void AssertHorizontalLayout(MainView view)
    {
        foreach (var scroll in view.GetVisualDescendants().OfType<ScrollViewer>().Where(scroll => scroll.IsEffectivelyVisible))
        {
            // TextBox's internal horizontal scrolling is intentional; page/list horizontal overflow is not.
            if (scroll.Name is not ("HomeScrollView" or "SettingsScrollView")) continue;
            Require(scroll.Extent.Width <= scroll.Viewport.Width + 1, "No page horizontal overflow: " + scroll.Name);
        }
        foreach (var grid in view.GetVisualDescendants().OfType<AdaptiveGrid>().Where(grid => grid.IsEffectivelyVisible && grid.Bounds.Width > 0))
            foreach (var child in grid.Children.Where(child => child.IsVisible && child.Bounds.Width > 0))
                Require(child.Bounds.Right <= grid.Bounds.Width + 1 && child.Bounds.X >= -1,
                    $"No clipped action/format: {child.GetType().Name}, right={child.Bounds.Right}, width={grid.Bounds.Width}");
        foreach (var text in view.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && text.Bounds.Width > 0))
        {
            var origin = text.TranslatePoint(default, view);
            if (origin is { } point)
                Require(point.X >= -1 && point.X + text.Bounds.Width <= view.Bounds.Width + 1,
                    "No clipped label: " + text.Text);
        }
    }
}
