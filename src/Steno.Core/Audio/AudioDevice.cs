namespace Steno.Core.Audio;

public enum AudioDeviceKind
{
    /// <summary>A microphone / capture endpoint. Carries the local user's voice.</summary>
    Capture,

    /// <summary>A render endpoint (speakers/headset). Captured via loopback; carries the remote party.</summary>
    Render
}

/// <param name="Id">Opaque, platform-specific endpoint id. Stable across restarts.</param>
public sealed record AudioDevice(string Id, string Name, AudioDeviceKind Kind, bool IsDefault)
{
    public override string ToString() => IsDefault ? $"{Name} (default)" : Name;
}
