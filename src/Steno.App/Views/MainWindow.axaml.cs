using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Steno.App.ViewModels;

namespace Steno.App.Views;

public partial class MainWindow : Window
{
    private bool _closeRequested;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        Loaded += OnLoaded;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>
    /// The setup card is a fixed amount of content and a hardcoded window height cannot fit it
    /// at every DPI and font size — at the old 700 px it was ~8 px short, so the app opened with
    /// a scrollbar. So: size to the content once, then hand height back to the user, because
    /// leaving SizeToContent on would fight the transcript view, which is meant to stretch.
    /// </summary>
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var height = Height;
        SizeToContent = SizeToContent.Manual;

        // Never open taller than the screen it opens on — a laptop would otherwise get a window
        // with its buttons below the taskbar.
        var workingArea = Screens.ScreenFromWindow(this)?.WorkingArea;
        if (workingArea is { } area)
        {
            var scaling = Screens.ScreenFromWindow(this)?.Scaling ?? 1d;
            height = Math.Min(height, area.Height / scaling * 0.92);
        }

        Height = height;
    }

    /// <summary>
    /// Closing the window is the last chance to lose a transcript that was never written to
    /// disk. Cancel the close, surface the same save/erase prompt the rest of the app uses, and
    /// let the window go once the transcript is either saved or explicitly thrown away.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeRequested || ViewModel is not { HasUnsavedTranscript: true } viewModel)
            return;

        e.Cancel = true;
        _closeRequested = true;

        viewModel.IsConfirmingDiscard = true;
        viewModel.PropertyChanged += CloseOnceResolved;
    }

    private void CloseOnceResolved(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;

        // User backed out of the prompt: forget the pending close, or the *next* close would
        // sail straight through and take the unsaved transcript with it.
        if (e.PropertyName == nameof(MainViewModel.IsConfirmingDiscard) &&
            !viewModel.IsConfirmingDiscard &&
            viewModel.HasUnsavedTranscript)
        {
            _closeRequested = false;
            viewModel.PropertyChanged -= CloseOnceResolved;
            return;
        }

        if (e.PropertyName != nameof(MainViewModel.HasUnsavedTranscript) || viewModel.HasUnsavedTranscript)
            return;

        // Saved, or deliberately erased. Either way the transcript is no longer at risk.
        viewModel.PropertyChanged -= CloseOnceResolved;
        Close();
    }
}
