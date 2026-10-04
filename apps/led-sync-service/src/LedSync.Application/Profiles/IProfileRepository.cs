using LedSync.Domain.Models;

namespace LedSync.Application.Profiles;

/// <summary>Persistence port for replacing and retrieving the complete saved profile collection.</summary>
public interface IProfileRepository
{
    Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken);
    Task ReplaceAllAsync(IReadOnlyList<Profile> profiles, CancellationToken cancellationToken);
}
