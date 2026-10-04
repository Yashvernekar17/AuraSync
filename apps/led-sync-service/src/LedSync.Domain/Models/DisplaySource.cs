namespace LedSync.Domain.Models;

/// <summary>A display's stable service ID, friendly name, and virtual-desktop bounds.</summary>
public sealed record DisplaySource(
    string Id,
    string Name,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsPrimary);
