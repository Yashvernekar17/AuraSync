using LedSync.Domain.Models;

namespace LedSync.Api.Contracts;

/// <summary>Request body for creating or updating a saved profile.</summary>
public sealed record ProfileRequest(string? Name, ProfileSettings? Settings);
