using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Steno.App.ViewModels;

namespace Steno.App.Views;

public partial class LiveView : UserControl
{
    public LiveView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Follow();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Follow()
    {
        if (ViewModel is not null)
            ViewModel.Entries.CollectionChanged += ScrollToLatest;
    }

    /// <summary>A live transcript that doesn't follow the conversation is a transcript you have to chase.</summary>
    private void ScrollToLatest(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            TranscriptScroll.ScrollToEnd();
    }

    /// <summary>
    /// File pickers are a view concern; the view model stays free of Avalonia's storage API
    /// and testable.
    /// </summary>
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var preferred = ViewModel.Exporters.FirstOrDefault();
        if (preferred is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save transcript",
            SuggestedFileName = $"call-{DateTime.Now:yyyy-MM-dd-HHmm}",
            DefaultExtension = preferred.Extension.TrimStart('.'),
            FileTypeChoices = ViewModel.Exporters
                .Select(x => new FilePickerFileType(x.Name) { Patterns = [$"*{x.Extension}"] })
                .ToList()
        });

        if (file?.TryGetLocalPath() is not { } path)
            return;

        // Honour whichever format the user actually picked in the dialog.
        var chosen = ViewModel.Exporters.FirstOrDefault(
            x => path.EndsWith(x.Extension, StringComparison.OrdinalIgnoreCase)) ?? preferred;

        await ViewModel.ExportAsync(path, chosen);
    }
}
