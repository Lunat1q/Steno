using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Steno.App.Updates;
using Steno.App.ViewModels;
using Steno.Core.Abstractions;
using Steno.Core.Export;
using Steno.Core.Platform.Windows;
using Steno.Core.Session;
using Steno.Core.Whisper;

namespace Steno.App.Composition;

/// <summary>
/// The one place concrete types are named (ADR 0007). Everything else takes interfaces,
/// which is what keeps whisper.cpp and WASAPI swappable.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder
            .AddDebug()
            .SetMinimumLevel(LogLevel.Information));

        // Audio: WASAPI (Windows). Replace this pair to port to another OS.
        services.AddSingleton<WasapiDeviceProvider>();
        services.AddSingleton<IAudioDeviceProvider>(sp => sp.GetRequiredService<WasapiDeviceProvider>());
        services.AddSingleton<IAudioCaptureSourceFactory>(sp => sp.GetRequiredService<WasapiDeviceProvider>());

        // Transcription: ggml-org/whisper.cpp via Whisper.net (ADR 0001).
        services.AddSingleton<IWhisperModelProvider, WhisperModelProvider>();
        services.AddSingleton<WhisperTranscriberFactory>();
        services.AddSingleton<ITranscriberFactory>(sp => sp.GetRequiredService<WhisperTranscriberFactory>());
        services.AddSingleton<ITranscriptionBackend>(sp => sp.GetRequiredService<WhisperTranscriberFactory>());

        services.AddSingleton<ITranscriptionSession, TranscriptionSession>();

        services.AddSingleton<ITranscriptExporter, MarkdownTranscriptExporter>();
        services.AddSingleton<ITranscriptExporter, JsonTranscriptExporter>();

        services.AddSingleton<IUserSettingsStore, JsonUserSettingsStore>();

        // Updates: GitHub Releases is the feed, an MSI is the payload (ADR 0018).
        services.AddSingleton<IUpdateChecker, GitHubUpdateChecker>();
        services.AddSingleton<IUpdateInstaller, MsiUpdateInstaller>();
        services.AddSingleton<UpdateViewModel>();

        services.AddSingleton<SetupViewModel>();
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider();
    }
}
