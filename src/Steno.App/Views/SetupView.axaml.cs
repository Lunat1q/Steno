using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Steno.App.ViewModels;

namespace Steno.App.Views;

public partial class SetupView : UserControl
{
    public SetupView() => InitializeComponent();

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>File pickers are a view concern; the view model stays free of Avalonia's storage API.</summary>
    private async void OnTranscribeFileClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Transcribe a recording",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Audio") { Patterns = ["*.wav", "*.mp3", "*.m4a", "*.flac"] }
            ]
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
            return;

        await ViewModel.TranscribeRecordingAsync(path);
    }
}
