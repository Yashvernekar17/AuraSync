namespace LedSync.Domain.Models;

/// <summary>Serial controller settings and edge layout for the configured LED strip.</summary>
public sealed record LedDeviceSettings(
    string ControllerType,
    string? SerialPort,
    int BaudRate,
    int LedCount,
    int TopLeds,
    int SideLeds,
    int BottomLeds)
{
    public double TopMarginPercent { get; init; }
    public double SideMarginPercent { get; init; }
    public double BottomMarginPercent { get; init; }
    public double CardSizePercent { get; init; } = 15;
    public double BottomGapPercent { get; init; }
    public int NumberingOffset { get; init; }
    public bool SkipCorners { get; init; }
    public bool InvertOrder { get; init; }

    /// <summary>Safe initial profile values; no serial port is selected until the user chooses one.</summary>
    public static LedDeviceSettings Default { get; } = new(
        ControllerType: "adalight",
        SerialPort: null,
        BaudRate: 115200,
        LedCount: 108,
        TopLeds: 45,
        SideLeds: 9,
        BottomLeds: 45);
}
