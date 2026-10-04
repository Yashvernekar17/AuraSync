namespace LedSync.Domain.Models;

/// <summary>Service-reported sync state and the settings currently applied to the runtime.</summary>
public sealed record SyncStatus(
    bool IsRunning,
    string State,
    string? ActiveProfileId,
    string? SelectedDisplayId,
    int FramesPerSecond,
    string? Message)
{
    public LedDeviceSettings LedDevice { get; init; } = LedDeviceSettings.Default;
    public string CaptureMode { get; init; } = "screen";
}
