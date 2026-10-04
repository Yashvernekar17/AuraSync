using System.Text.RegularExpressions;
using LedSync.Domain.Models;

namespace LedSync.Application.Profiles;

public static partial class LedDeviceSettingsValidator
{
    private static readonly HashSet<int> SupportedBaudRates =
    [
        9600, 57600, 115200, 128000, 153600, 230400, 256000,
        460800, 500000, 921600, 1000000, 1500000, 2000000
    ];

    public static void Validate(LedDeviceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var maxLeds = settings.ControllerType switch
        {
            "adalight" => 255,
            "ardulight" => 255,
            _ => throw new ArgumentException("Controller type must be 'adalight' or 'ardulight'.", nameof(settings))
        };

        if (settings.SerialPort is not null && !ComPortRegex().IsMatch(settings.SerialPort))
            throw new ArgumentException("Serial port must be a Windows COM port, such as COM3.", nameof(settings));
        if (!SupportedBaudRates.Contains(settings.BaudRate))
            throw new ArgumentOutOfRangeException(nameof(settings), "Select a supported serial baud rate.");
        if (settings.LedCount is < 1 || settings.LedCount > maxLeds)
            throw new ArgumentOutOfRangeException(nameof(settings), $"This controller supports between 1 and {maxLeds} LEDs.");
        if (settings.TopLeds < 0 || settings.SideLeds < 0 || settings.BottomLeds < 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "LED counts on each edge cannot be negative.");
        if (settings.TopLeds + (2 * settings.SideLeds) + settings.BottomLeds != settings.LedCount)
            throw new ArgumentException("Top, both sides, and bottom must add up to the total LED count.", nameof(settings));
        ValidatePercent(settings.TopMarginPercent, "Top margin", 0, 40);
        ValidatePercent(settings.SideMarginPercent, "Side margin", 0, 40);
        ValidatePercent(settings.BottomMarginPercent, "Bottom margin", 0, 40);
        ValidatePercent(settings.CardSizePercent, "Card size", 1, 50);
        ValidatePercent(settings.BottomGapPercent, "Bottom gap", 0, 60);
        if (settings.TopMarginPercent + settings.BottomMarginPercent >= 90)
            throw new ArgumentOutOfRangeException(nameof(settings), "Top and bottom margins must leave room for the screen area.");
        if (settings.SideMarginPercent >= 45)
            throw new ArgumentOutOfRangeException(nameof(settings), "Side margin must leave room for the screen area.");
        if (settings.NumberingOffset is < -999 or > 999)
            throw new ArgumentOutOfRangeException(nameof(settings), "LED numbering offset must be between -999 and 999.");
    }

    private static void ValidatePercent(double value, string name, double minimum, double maximum)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name, $"{name} must be between {minimum} and {maximum} percent.");
    }

    [GeneratedRegex(@"^COM[1-9][0-9]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ComPortRegex();
}
