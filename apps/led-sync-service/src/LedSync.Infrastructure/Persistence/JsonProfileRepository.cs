using System.Text.Json;
using LedSync.Application.Profiles;
using LedSync.Domain.Models;

namespace LedSync.Infrastructure.Persistence;

/// <summary>Stores profiles as JSON under the user's local application data directory.</summary>
public sealed class JsonProfileRepository : IProfileRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;

    public JsonProfileRepository()
    {
        var dataDirectory = Environment.GetEnvironmentVariable("LEDSYNC_DATA_DIRECTORY");
        if (string.IsNullOrWhiteSpace(dataDirectory))
            dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LedSync");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "profiles.json");
    }

    public async Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
                return [];

            await using var file = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<Profile>>(file, JsonOptions, cancellationToken)
                   ?? throw new InvalidDataException($"The profile file '{_filePath}' contains invalid JSON.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReplaceAllAsync(IReadOnlyList<Profile> profiles, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            // Write and close a complete temporary document before replacing the live profile file.
            await using (var file = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(file, profiles, JsonOptions, cancellationToken);

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            _gate.Release();
        }
    }
}
