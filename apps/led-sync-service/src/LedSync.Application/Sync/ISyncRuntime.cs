using LedSync.Domain.Models;

namespace LedSync.Application.Sync;

/// <summary>Application-facing lifecycle and frame-writing boundary for synchronization.</summary>
public interface ISyncRuntime
{
    SyncConfiguration Configuration { get; }
    SyncStatus GetStatus();
    void SetConfiguration(SyncConfiguration configuration);
    void SetActiveProfile(Profile? profile);
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task WriteFrameAsync(ReadOnlyMemory<byte> rgbFrame, CancellationToken cancellationToken);
}

/// <summary>Port implemented by the infrastructure adapter that writes encoded frames to a controller.</summary>
public interface ILedOutput
{
    Task OpenAsync(LedDeviceSettings settings, CancellationToken cancellationToken);
    Task WriteFrameAsync(
        ReadOnlyMemory<byte> rgbFrame,
        CancellationToken cancellationToken,
        bool ensureTransmitted = false);
    Task CloseAsync(CancellationToken cancellationToken);
}
