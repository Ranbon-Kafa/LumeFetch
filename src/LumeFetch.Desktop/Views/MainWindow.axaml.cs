using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LumeFetch.Desktop.Localization;
using LumeFetch.Desktop.ViewModels;

namespace LumeFetch.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private bool _shutdownComplete;
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosingAsync;
    }

    private async void OnClosingAsync(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdownComplete || DataContext is not MainWindowViewModel viewModel) return;
        e.Cancel = true;
        IsEnabled = false;
        await viewModel.ShutdownAsync();
        _shutdownComplete = true;
        Close();
    }

    private void DownloadsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel) viewModel.IsSettingsOpen = false;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => DownloadsHeading.BringIntoView());
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private async void PasteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        if (!string.IsNullOrWhiteSpace(text))
        {
            await viewModel.AnalyzeFromPasteAsync(text);
        }
    }

    private async void BrowseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localizer.Current["ChooseFolder"],
            AllowMultiple = false,
        });

        var selectedPath = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            viewModel.DestinationDirectory = selectedPath;
        }
    }

    private async void ConnectSpotifyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        await viewModel.ConnectSpotifyAsync(async uri =>
        {
            var result = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Launcher.LaunchUriAsync(uri));
            if (!result) throw new InvalidOperationException("Could not open the browser.");
        });
    }

    private async void SourceLink_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CollectionRowViewModel row } button) return;
        var uri = button.Tag as string == "spotify" ? row.OriginalUri : row.DownloadUri;
        if (uri is null || uri.Scheme != "https") return;
        try
        {
            if (!await Launcher.LaunchUriAsync(uri) && DataContext is MainWindowViewModel vm) vm.ReportBrowserFailure();
        }
        catch (Exception)
        {
            if (DataContext is MainWindowViewModel vm) vm.ReportBrowserFailure();
        }
    }

    private void UrlTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainWindowViewModel viewModel && viewModel.AnalyzeCommand.CanExecute(null))
        {
            viewModel.AnalyzeCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }
}
