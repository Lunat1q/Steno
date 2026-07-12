using Microsoft.Extensions.Logging.Abstractions;
using Steno.App.Composition;
using Steno.App.ViewModels;
using Steno.Core.Abstractions;
using Steno.Core.Audio;
using Steno.Core.Transcription;
using Xunit;

namespace Steno.App.Tests;

public class UserSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"steno-settings-{Guid.NewGuid():N}.json");

    private JsonUserSettingsStore Store() =>
        new(NullLogger<JsonUserSettingsStore>.Instance, _path);

    [Fact]
    public void Choices_survive_a_restart()
    {
        var first = new SetupViewModel(new FakeDevices(), new FakeModels(), Store());
        first.Quality = QualityChoice.All.Single(q => q.Name == "Best");
        first.Language = LanguageChoice.All.Single(l => l.Code == "en");
        first.TranslateToEnglish = true;

        // A fresh view model is what the next launch builds.
        var next = new SetupViewModel(new FakeDevices(), new FakeModels(), Store());

        Assert.Equal("Best", next.Quality.Name);
        Assert.Equal("en", next.Language.Code);
        Assert.True(next.TranslateToEnglish);
        Assert.Equal(WhisperModel.LargeV3, next.ToSessionOptions().Transcription.Model);
    }

    [Fact]
    public void The_chosen_devices_survive_a_restart()
    {
        var first = new SetupViewModel(new FakeDevices(), new FakeModels(), Store());
        first.Microphone = first.Microphones.Single(d => d.Id == "mic-2");

        var next = new SetupViewModel(new FakeDevices(), new FakeModels(), Store());

        Assert.Equal("mic-2", next.Microphone?.Id);
    }

    [Fact]
    public void A_device_that_is_gone_falls_back_to_the_default()
    {
        Store().Save(new UserSettings { MicrophoneId = "a-headset-that-was-unplugged" });

        var setup = new SetupViewModel(new FakeDevices(), new FakeModels(), Store());

        // Not null, not throwing: the user must still be able to press Start.
        Assert.NotNull(setup.Microphone);
        Assert.Equal("mic-1", setup.Microphone!.Id);
    }

    [Fact]
    public void A_corrupt_settings_file_does_not_stop_the_app_from_starting()
    {
        File.WriteAllText(_path, "{ this is not json");

        var setup = new SetupViewModel(new FakeDevices(), new FakeModels(), Store());

        Assert.Equal(QualityChoice.Default.Name, setup.Quality.Name);
    }

    [Fact]
    public void Starting_fresh_does_not_clobber_saved_settings_before_reading_them()
    {
        Store().Save(new UserSettings { QualityName = "Best" });

        // Constructing the view model enumerates devices, which raises property changes. If those
        // were saved, they would overwrite the file before it was ever read.
        var setup = new SetupViewModel(new FakeDevices(), new FakeModels(), Store());

        Assert.Equal("Best", setup.Quality.Name);
        Assert.Equal("Best", Store().Load().QualityName);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private sealed class FakeDevices : IAudioDeviceProvider
    {
        public IReadOnlyList<AudioDevice> GetDevices(AudioDeviceKind kind) => kind switch
        {
            AudioDeviceKind.Capture =>
            [
                new AudioDevice("mic-1", "Default mic", kind, true),
                new AudioDevice("mic-2", "Headset", kind, false)
            ],
            _ => [new AudioDevice("out-1", "Speakers", kind, true)]
        };
    }

    private sealed class FakeModels : IWhisperModelProvider
    {
        public bool IsDownloaded(WhisperModel model) => true;

        public Task<string> GetModelPathAsync(
            WhisperModel model,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("model.bin");
    }
}
