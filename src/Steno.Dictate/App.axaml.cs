using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Steno.Core.Abstractions;
using Steno.Core.Platform.Windows;
using Steno.Core.Session;
using Steno.Core.Whisper;

namespace Steno.Dictate;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // A trimmed copy of Steno.App's registration: dictation needs the microphone, the model and
        // the session — none of the call machinery (loopback, recording, exporters, updates).
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<WasapiDeviceProvider>();
        services.AddSingleton<IAudioDeviceProvider>(sp => sp.GetRequiredService<WasapiDeviceProvider>());
        services.AddSingleton<IAudioCaptureSourceFactory>(sp => sp.GetRequiredService<WasapiDeviceProvider>());
        services.AddSingleton<IWhisperModelProvider, WhisperModelProvider>();
        services.AddSingleton<WhisperTranscriberFactory>();
        services.AddSingleton<ITranscriberFactory>(sp => sp.GetRequiredService<WhisperTranscriberFactory>());
        services.AddSingleton<ITranscriptionSession, TranscriptionSession>();

        var provider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new DictateViewModel(
                provider.GetRequiredService<IAudioDeviceProvider>(),
                provider.GetRequiredService<IWhisperModelProvider>(),
                provider.GetRequiredService<ITranscriptionSession>(),
                provider.GetRequiredService<WhisperTranscriberFactory>());

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += async (_, _) =>
                await provider.GetRequiredService<ITranscriptionSession>().DisposeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
