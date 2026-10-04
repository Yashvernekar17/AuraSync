namespace LedSync.Domain.Models;

/// <summary>Validation rules for effect names, RGB palettes, and animation speed.</summary>
public static class CustomEffectSettings
{
    /// <summary>Rejects unsupported effects, malformed colors, or speed values outside 1-100.</summary>
    public static void Validate(string? effect, string[]? colors, int speed)
    {
        if (effect is not ("static" or "blink" or "fade" or "rainbow" or "fire" or "color-cycle" or
            "color-wipe" or "theater-chase" or "breathing"))
            throw new ArgumentException("Choose a supported custom RGB effect.", nameof(effect));

        if (colors is null || colors.Length is < 1 or > 8 ||
            colors.Any(color => color is null || color.Length != 7 || color[0] != '#' ||
                !color.AsSpan(1).ToString().All(Uri.IsHexDigit)))
            throw new ArgumentException("Choose between 1 and 8 valid RGB colors.", nameof(colors));

        if (speed is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(speed), "Custom effect speed must be between 1 and 100.");
    }
}
