using NAudio.CoreAudioApi;
using Steno.Core.Abstractions;
using Steno.Core.Audio;

namespace Steno.Core.Platform.Windows;

/// <summary>Enumerates WASAPI endpoints and turns a chosen one into a live capture source.</summary>
public sealed class WasapiDeviceProvider : IAudioDeviceProvider, IAudioCaptureSourceFactory
{
    public IReadOnlyList<AudioDevice> GetDevices(AudioDeviceKind kind)
    {
        using var enumerator = new MMDeviceEnumerator();
        var flow = ToDataFlow(kind);
        var defaultId = TryGetDefaultId(enumerator, flow);

        var devices = new List<AudioDevice>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
                devices.Add(new AudioDevice(device.ID, device.FriendlyName, kind, device.ID == defaultId));
        }

        // Default first: it is what the user wants ~always.
        return devices.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name).ToList();
    }

    /// <summary>
    /// Hands over the endpoint *id*, not a live MMDevice. This method is called from the UI
    /// thread (STA) but capture runs in the MTA, and IMMDevice cannot be marshalled between
    /// apartments — so the COM object must be born where it is used, not here.
    /// </summary>
    public IAudioCaptureSource Create(AudioDevice device) =>
        new WasapiCaptureSource(device.Id, device.Kind);

    private static string? TryGetDefaultId(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        if (!enumerator.HasDefaultAudioEndpoint(flow, Role.Console))
            return null;

        using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Console);
        return device.ID;
    }

    private static DataFlow ToDataFlow(AudioDeviceKind kind) => kind switch
    {
        AudioDeviceKind.Capture => DataFlow.Capture,
        AudioDeviceKind.Render => DataFlow.Render,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}
