namespace LedSync.Domain.Models;

/// <summary>An active audio endpoint that can be selected for loopback capture.</summary>
public sealed record AudioDevice(string Id, string Name, string Direction);
