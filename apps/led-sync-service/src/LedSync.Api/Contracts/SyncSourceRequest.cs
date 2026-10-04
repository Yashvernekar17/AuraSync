namespace LedSync.Api.Contracts;

/// <summary>Request body for selecting or clearing the current display source.</summary>
public sealed record SyncSourceRequest(string? DisplayId);
