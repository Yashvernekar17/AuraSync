using LedSync.Domain.Models;

namespace LedSync.Application.Capture;

/// <summary>Port for listing active audio endpoints without depending on a platform API.</summary>
public interface IAudioDeviceProvider
{
    Task<IReadOnlyList<AudioDevice>> GetAudioDevicesAsync(CancellationToken cancellationToken);
}
