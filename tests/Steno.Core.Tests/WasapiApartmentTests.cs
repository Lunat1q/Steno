using Steno.Core.Audio;
using Steno.Core.Platform.Windows;
using Xunit;

namespace Steno.Core.Tests;

/// <summary>
/// Regression: starting a capture from Avalonia's STA UI thread used to blow up with
///   "Unable to cast COM object … to IMMDevice … E_NOINTERFACE"
/// because the MMDevice was created on the STA thread and then used from an MTA thread-pool
/// thread after the first `await`. IMMDevice has no marshaller, so it cannot cross apartments
/// (ADR 0009). These tests drive the capture the way the app does — from an STA thread.
/// </summary>
public class WasapiApartmentTests
{
    private static readonly WasapiDeviceProvider Provider = new();

    /// <summary>Runs on a real STA thread, exactly like the Avalonia UI thread.</summary>
    private static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));

        if (failure is not null)
            throw failure;

        return result;
    }

    [Fact]
    public void Thread_pool_threads_are_mta()
    {
        // The whole fix rests on this: WASAPI work is pushed onto the thread pool precisely
        // because those threads share one MTA. If this ever stopped holding, the E_NOINTERFACE
        // crash would come straight back.
        var apartment = Task.Run(() => Thread.CurrentThread.GetApartmentState()).Result;

        Assert.Equal(ApartmentState.MTA, apartment);
    }

    [Fact]
    public void Capture_can_be_started_and_stopped_from_an_sta_thread()
    {
        var device = Provider.GetDevices(AudioDeviceKind.Render).FirstOrDefault();
        if (device is null)
            return; // headless box with no audio endpoint — nothing to exercise

        var frames = 0;

        OnStaThread(() =>
        {
            // Create on the STA thread — this is what the UI does via the composition root.
            var source = Provider.Create(device!);
            source.FrameAvailable += _ => Interlocked.Increment(ref frames);

            // ...then start it, which lands the COM work on the pool. This is the exact
            // sequence that used to throw.
            source.StartAsync(DateTimeOffset.UtcNow).GetAwaiter().GetResult();
            Assert.True(source.IsCapturing);

            Thread.Sleep(300);

            source.StopAsync().GetAwaiter().GetResult();
            source.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Assert.False(source.IsCapturing);

            return true;
        });
    }

    [Fact]
    public void Loopback_delivers_frames_even_when_nothing_is_playing()
    {
        // A silent render device delivers no WASAPI callbacks at all. The source must
        // synthesise silence anyway, or the remote channel's clock drifts away from the mic's
        // and every later line is timestamped too early.
        var device = Provider.GetDevices(AudioDeviceKind.Render).FirstOrDefault();
        if (device is null)
            return; // headless box with no audio endpoint — nothing to exercise

        var frames = 0;

        OnStaThread(() =>
        {
            var source = Provider.Create(device!);
            source.FrameAvailable += _ => Interlocked.Increment(ref frames);

            source.StartAsync(DateTimeOffset.UtcNow).GetAwaiter().GetResult();
            Thread.Sleep(1_000);
            source.StopAsync().GetAwaiter().GetResult();
            source.DisposeAsync().AsTask().GetAwaiter().GetResult();

            return true;
        });

        Assert.True(frames > 0, "loopback produced no frames in a second — the timeline would drift");
    }
}
