namespace LedSync.Application.Capture;

/// <summary>Discovers output endpoints and streams their loopback audio levels to the sync runtime.</summary>
public interface IAudioCaptureProvider : IAudioDeviceProvider
{
    void Start(string deviceId, Action<AudioLevels> onLevels, Action<Exception> onError);
    void Stop();
}

/// <summary>Normalized overall and frequency-band energy values for one analysis window.</summary>
public readonly record struct AudioLevels(float Overall, float Low, float Mid, float High);
