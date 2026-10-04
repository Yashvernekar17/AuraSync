namespace LedSync.Domain.Models;

/// <summary>Immutable runtime configuration consumed by the sync engine.</summary>
public sealed record SyncConfiguration(
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

    /// <summary>Initial stopped configuration used before a profile is selected.</summary>
    public static SyncConfiguration Default { get; } = new(
        DisplayId: null,
        FramesPerSecond: 30,
        CaptureMode: "screen",
        AudioDeviceId: null,
        SoundMode: "off");
}
