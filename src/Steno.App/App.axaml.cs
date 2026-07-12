using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Steno.App.Composition;
using Steno.App.ViewModels;
using Steno.App.Views;
using Steno.Core.Session;
using Steno.Core.Whisper;

namespace Steno.App;

public partial class App : Application
{
    private IServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _services = ServiceRegistration.Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = _services.GetRequiredService<MainViewModel>();

            // Model downloads happen inside the factory; the bar lives in the view model.
            _services.GetRequiredService<WhisperTranscriberFactory>().DownloadProgress =
                viewModel.ModelDownloadProgress;

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += async (_, _) =>
                await _services.GetRequiredService<ITranscriptionSession>().DisposeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
