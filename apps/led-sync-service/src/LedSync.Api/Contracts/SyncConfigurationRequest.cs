using LedSync.Domain.Models;

namespace LedSync.Api.Contracts;

/// <summary>Request body shared by runtime configuration and synchronization start operations.</summary>
public sealed record SyncConfigurationRequest(
    string? DisplayId,
    int FramesPerSecond,
    string CaptureMode,
    string? AudioDeviceId,
    string SoundMode)
{
    public LedDeviceSettings LedDevice { get; init; } = LedDeviceSettings.Default;
    public string[] AudioColors { get; init; } = ["#8D7CFF", "#54C8D9", "#56D39A"];
    public string CustomEffect { get; init; } = "static";
    public string[] CustomColors { get; init; } = ["#8D7CFF"];
    public int CustomSpeed { get; init; } = 50;
}
