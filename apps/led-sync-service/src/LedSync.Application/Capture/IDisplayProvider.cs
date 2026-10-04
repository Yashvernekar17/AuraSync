using LedSync.Domain.Models;

namespace LedSync.Application.Capture;

/// <summary>Port for enumerating displays without exposing operating-system types to the application layer.</summary>
public interface IDisplayProvider
{
    Task<IReadOnlyList<DisplaySource>> GetDisplaysAsync(CancellationToken cancellationToken);
}
