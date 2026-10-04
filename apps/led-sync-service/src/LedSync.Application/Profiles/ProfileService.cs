using LedSync.Domain.Models;

namespace LedSync.Application.Profiles;

/// <summary>Validates and coordinates profile changes while preserving a single active profile.</summary>
public sealed class ProfileService(IProfileRepository repository)
{
    // Serialize read-modify-write operations so simultaneous requests cannot overwrite each other's changes.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await repository.GetAllAsync(cancellationToken);
    }

    public async Task<Profile> CreateAsync(string? name, ProfileSettings? settings, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = (await repository.GetAllAsync(cancellationToken)).ToList();
            var normalizedName = ValidateName(name);
            EnsureUniqueName(profiles, normalizedName, null);

            var now = DateTimeOffset.UtcNow;
            var profile = new Profile(
                Guid.NewGuid(),
                normalizedName,
                now,
                now,
                ValidateSettings(settings ?? new ProfileSettings(null, "screen", null, "off")),
                profiles.Count == 0);
            profiles.Add(profile);
            await repository.ReplaceAllAsync(profiles, cancellationToken);
            return profile;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Profile?> UpdateAsync(
        Guid id,
        string? name,
        ProfileSettings settings,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = (await repository.GetAllAsync(cancellationToken)).ToList();
            var index = profiles.FindIndex(profile => profile.Id == id);
            if (index < 0)
                return null;

            var normalizedName = ValidateName(name);
            EnsureUniqueName(profiles, normalizedName, id);
            var updated = profiles[index] with
            {
                Name = normalizedName,
                Settings = ValidateSettings(settings),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            profiles[index] = updated;
            await repository.ReplaceAllAsync(profiles, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = (await repository.GetAllAsync(cancellationToken)).ToList();
            var profile = profiles.FirstOrDefault(candidate => candidate.Id == id);
            if (profile is null)
                return false;

            profiles.Remove(profile);
            if (profile.IsActive && profiles.Count > 0)
                profiles[0] = profiles[0] with { IsActive = true, UpdatedAt = DateTimeOffset.UtcNow };

            await repository.ReplaceAllAsync(profiles, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Profile?> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = (await repository.GetAllAsync(cancellationToken)).ToList();
            if (!profiles.Any(profile => profile.Id == id))
                return null;

            var now = DateTimeOffset.UtcNow;
            for (var index = 0; index < profiles.Count; index++)
                profiles[index] = profiles[index] with
                {
                    IsActive = profiles[index].Id == id,
                    UpdatedAt = now
                };

            await repository.ReplaceAllAsync(profiles, cancellationToken);
            return profiles.Single(profile => profile.Id == id);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Profile names cannot be empty.", nameof(name));

        var normalized = name.Trim();
        if (normalized.Length > 60)
            throw new ArgumentException("Profile names cannot contain more than 60 characters.", nameof(name));
        return normalized;
    }

    private static void EnsureUniqueName(IEnumerable<Profile> profiles, string name, Guid? exceptId)
    {
        if (profiles.Any(profile =>
                profile.Id != exceptId &&
                string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A profile with that name already exists.", nameof(name));
    }

    private static ProfileSettings ValidateSettings(ProfileSettings settings)
    {
        if (settings.CaptureMode is not ("screen" or "audio" or "custom"))
            throw new ArgumentException("Capture mode must be 'screen', 'audio', or 'custom'.", nameof(settings));
        if (settings.SoundMode is not ("off" or "average" or "spectrum"))
            throw new ArgumentException("Sound mode must be 'off' or 'average'.", nameof(settings));
        ValidateAudioColors(settings.AudioColors);
        CustomEffectSettings.Validate(settings.CustomEffect, settings.CustomColors, settings.CustomSpeed);
        LedDeviceSettingsValidator.Validate(settings.LedDevice);
        return settings with
        {
            SoundMode = settings.CaptureMode == "audio" ? "average" : "off"
        };
    }

    internal static void ValidateAudioColors(string[]? colors)
    {
        if (colors is null || colors.Length is < 2 or > 8 ||
            colors.Any(color => color is null || color.Length != 7 || color[0] != '#' ||
                !color.AsSpan(1).ToString().All(Uri.IsHexDigit)))
            throw new ArgumentException("Choose between 2 and 8 valid RGB colors.", nameof(colors));
    }
}
