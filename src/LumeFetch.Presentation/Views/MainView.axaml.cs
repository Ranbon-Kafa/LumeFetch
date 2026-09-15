using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LumeFetch.Presentation.Localization;
using LumeFetch.Presentation.ViewModels;

namespace LumeFetch.Presentation.Views;

/// <summary>The same feature surface is hosted in a desktop Window or mobile single-view lifetime.</summary>
public sealed partial class MainView : UserControl
{
    private MainWindowViewModel? _initializedViewModel;
    private bool? _compact;
    public Func<Task<string?>>? FolderPicker { get; init; }

    public MainView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        Loaded += OnOpened;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 760;
        if (_compact == compact) return;
        _compact = compact;
        Classes.Set("compact", compact);
        Sidebar.IsVisible = !compact;
        MobileNavigation.IsVisible = compact;
        Shell.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "228,*");
        Grid.SetColumnSpan(MobileNavigation, compact ? 1 : 2);
        foreach (var scroll in new[] { HomeScrollView, SettingsScrollView })
        {
            Grid.SetColumn(scroll, compact ? 0 : 1);
            Grid.SetRow(scroll, compact ? 1 : 0);
            Grid.SetRowSpan(scroll, compact ? 1 : 2);
        }
    }

    private async Task<bool> OpenUriAsync(Uri uri)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        return launcher is not null && await launcher.LaunchUriAsync(uri);
    }

    private void DownloadsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel) viewModel.IsSettingsOpen = false;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // BringIntoView does nothing when only the heading is visible at the bottom of a phone.
            if (HomeScrollView.Content is Control content && DownloadsHeading.TranslatePoint(default, content) is { } position)
                HomeScrollView.Offset = new Vector(0, position.Y);
            else DownloadsHeading.BringIntoView();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private async void OnOpened(object? sender, RoutedEventArgs e)
        => await InitializeViewModelAsync();

    private async void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (IsLoaded) await InitializeViewModelAsync();
    }

    private async Task InitializeViewModelAsync()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            if (ReferenceEquals(_initializedViewModel, viewModel)) return;
            _initializedViewModel = viewModel;
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

        try
        {
            var text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrWhiteSpace(text)) await viewModel.AnalyzeFromPasteAsync(text);
        }
        catch (Exception) { viewModel.ReportPlatformFailure("ClipboardFailed"); }
    }

    private async void BrowseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            if (FolderPicker is not null)
            {
                var destination = await FolderPicker();
                if (destination is not null) viewModel.DestinationDirectory = destination;
                return;
            }
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Localizer.Current["ChooseFolder"],
                AllowMultiple = false,
            });
            var selectedPath = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (!string.IsNullOrWhiteSpace(selectedPath)) viewModel.DestinationDirectory = selectedPath;
            else if (folders.Count > 0) viewModel.ReportPlatformFailure("FolderAccessUnavailable");
        }
        catch (Exception) { viewModel.ReportPlatformFailure("FolderPickerFailed"); }
    }

    private async void ConnectSpotifyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel) return;
        await viewModel.ConnectSpotifyAsync(async uri =>
        {
            var result = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => OpenUriAsync(uri));
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
            if (!await OpenUriAsync(uri) && DataContext is MainWindowViewModel vm) vm.ReportBrowserFailure();
        }
        catch (Exception)
        {
            if (DataContext is MainWindowViewModel vm) vm.ReportBrowserFailure();
        }
    }

    private async void NoticesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!await OpenUriAsync(new Uri("https://github.com/Ranbon-Kafa/LumeFetch/blob/main/THIRD_PARTY_NOTICES.md")) &&
                DataContext is MainWindowViewModel vm) vm.ReportBrowserFailure();
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
}
