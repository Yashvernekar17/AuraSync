namespace LedSync.Domain.Models;

/// <summary>A named, persisted set of preferences with stable identity and activation timestamps.</summary>
public sealed record Profile(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ProfileSettings Settings,
    bool IsActive);

/// <summary>Capture, audio, effect, and controller preferences associated with a profile.</summary>
public sealed record ProfileSettings(
    string? DisplayId,
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
