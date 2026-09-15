using Avalonia.Controls;
using LumeFetch.Presentation.ViewModels;

namespace LumeFetch.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private bool _shutdownComplete;

    public MainWindow()
    {
        InitializeComponent();
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

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable) disposable.Dispose();
        base.OnClosed(e);
    }
}
