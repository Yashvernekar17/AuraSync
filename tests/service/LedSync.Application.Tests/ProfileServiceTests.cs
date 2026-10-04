using LedSync.Application.Profiles;
using LedSync.Domain.Models;
using LedSync.Infrastructure.Persistence;

namespace LedSync.Application.Tests;

public sealed class ProfileServiceTests
{
    [Fact]
    public async Task CreateAsync_TrimsNameAndActivatesFirstProfile()
    {
        var service = new ProfileService(new InMemoryProfileRepository());

        var profile = await service.CreateAsync("  Movie night  ", null, CancellationToken.None);

        Assert.Equal("Movie night", profile.Name);
        Assert.True(profile.IsActive);
        Assert.Equal("screen", profile.Settings.CaptureMode);
    }

    [Fact]
    public async Task CreateAsync_UsesAdalightCompatibleArduinoDefaults()
    {
        var service = new ProfileService(new InMemoryProfileRepository());

        var profile = await service.CreateAsync("Arduino", null, CancellationToken.None);

        Assert.Equal("adalight", profile.Settings.LedDevice.ControllerType);
        Assert.Equal(108, profile.Settings.LedDevice.LedCount);
        Assert.Equal(15, profile.Settings.LedDevice.CardSizePercent);
        Assert.Equal(108, profile.Settings.LedDevice.TopLeds +
            (2 * profile.Settings.LedDevice.SideLeds) +
            profile.Settings.LedDevice.BottomLeds);
    }

    [Fact]
    public async Task UpdateAsync_RejectsLedLayoutThatDoesNotMatchTotalCount()
    {
        var service = new ProfileService(new InMemoryProfileRepository());
        var profile = await service.CreateAsync("LED layout", null, CancellationToken.None);
        var invalidSettings = profile.Settings with
        {
            LedDevice = profile.Settings.LedDevice with { LedCount = 24 }
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateAsync(profile.Id, profile.Name, invalidSettings, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_RejectsLedCountAboveArdulightLimit()
    {
        var service = new ProfileService(new InMemoryProfileRepository());
        var profile = await service.CreateAsync("Ardulight", null, CancellationToken.None);
        var invalidSettings = profile.Settings with
        {
            LedDevice = new LedDeviceSettings("ardulight", "COM3", 115200, 256, 256, 0, 0)
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.UpdateAsync(profile.Id, profile.Name, invalidSettings, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_RejectsInvalidComPort()
    {
        var service = new ProfileService(new InMemoryProfileRepository());
        var profile = await service.CreateAsync("Bad port", null, CancellationToken.None);
        var invalidSettings = profile.Settings with
        {
            LedDevice = profile.Settings.LedDevice with { SerialPort = "COM0" }
        };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateAsync(profile.Id, profile.Name, invalidSettings, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_RejectsMarginsThatLeaveNoDisplayArea()
    {
        var service = new ProfileService(new InMemoryProfileRepository());
        var profile = await service.CreateAsync("Invalid margins", null, CancellationToken.None);
        var invalidSettings = profile.Settings with
        {
            LedDevice = profile.Settings.LedDevice with { TopMarginPercent = 50, BottomMarginPercent = 40 }
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.UpdateAsync(profile.Id, profile.Name, invalidSettings, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_RejectsInvalidAudioPalette()
    {
        var service = new ProfileService(new InMemoryProfileRepository());
        var profile = await service.CreateAsync("Invalid palette", null, CancellationToken.None);
        var invalidSettings = profile.Settings with { AudioColors = ["#FFF"] };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateAsync(profile.Id, profile.Name, invalidSettings, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_NormalizesLegacySpectrumToAverageLevel()
    {
        var service = new ProfileService(new InMemoryProfileRepository());
        var profile = await service.CreateAsync("Legacy sound mode", null, CancellationToken.None);
        var legacySettings = profile.Settings with
        {
            CaptureMode = "audio",
            SoundMode = "spectrum"
        };

        var updated = await service.UpdateAsync(
            profile.Id,
            profile.Name,
            legacySettings,
            CancellationToken.None);

        Assert.Equal("average", updated?.Settings.SoundMode);
    }

    [Fact]
    public async Task JsonRepository_PersistsLedLayoutSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ledsync-layout-test-{Guid.NewGuid():N}");
        var originalDirectory = Environment.GetEnvironmentVariable("LEDSYNC_DATA_DIRECTORY");
        try
        {
            Environment.SetEnvironmentVariable("LEDSYNC_DATA_DIRECTORY", directory);
            var ledSettings = LedDeviceSettings.Default with
            {
                TopMarginPercent = 3.5,
                SideMarginPercent = 4,
                BottomMarginPercent = 5.5,
                CardSizePercent = 20,
                BottomGapPercent = 28,
                NumberingOffset = -2,
                SkipCorners = true,
                InvertOrder = true
            };
            var profileSettings = new ProfileSettings(null, "screen", null, "off")
            {
                LedDevice = ledSettings
            };
            await new ProfileService(new JsonProfileRepository())
                .CreateAsync("LED layout", profileSettings, CancellationToken.None);

            var persisted = await new JsonProfileRepository().GetAllAsync(CancellationToken.None);

            Assert.Equal(ledSettings, Assert.Single(persisted).Settings.LedDevice);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LEDSYNC_DATA_DIRECTORY", originalDirectory);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ActivateAsync_LeavesExactlyOneActiveProfile()
    {
        var service = new ProfileService(new InMemoryProfileRepository());
        var first = await service.CreateAsync("First", null, CancellationToken.None);
        var second = await service.CreateAsync("Second", null, CancellationToken.None);

        var activated = await service.ActivateAsync(second.Id, CancellationToken.None);
        var profiles = await service.GetAllAsync(CancellationToken.None);

        Assert.Equal(second.Id, activated?.Id);
        Assert.False(profiles.Single(profile => profile.Id == first.Id).IsActive);
        Assert.True(profiles.Single(profile => profile.Id == second.Id).IsActive);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateNamesIgnoringCase()
    {
        var service = new ProfileService(new InMemoryProfileRepository());
        await service.CreateAsync("Gaming", null, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync("gaming", null, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_AcceptsProfileSettingsWithoutFrameRate()
    {
        var service = new ProfileService(new InMemoryProfileRepository());
        var profile = await service.CreateAsync("Work", null, CancellationToken.None);

        var updated = await service.UpdateAsync(
            profile.Id,
            profile.Name,
            profile.Settings with { CaptureMode = "audio" },
            CancellationToken.None);

        Assert.Equal("audio", updated?.Settings.CaptureMode);
    }

    [Fact]
    public async Task DeleteAsync_ActivatesNextProfileWhenActiveProfileIsDeleted()
    {
        var service = new ProfileService(new InMemoryProfileRepository());
        var first = await service.CreateAsync("First", null, CancellationToken.None);
        var second = await service.CreateAsync("Second", null, CancellationToken.None);

        Assert.True(await service.DeleteAsync(first.Id, CancellationToken.None));

        var remaining = Assert.Single(await service.GetAllAsync(CancellationToken.None));
        Assert.Equal(second.Id, remaining.Id);
        Assert.True(remaining.IsActive);
    }

    [Fact]
    public async Task JsonRepository_PersistsProfilesAcrossRepositoryInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ledsync-test-{Guid.NewGuid():N}");
        var originalDirectory = Environment.GetEnvironmentVariable("LEDSYNC_DATA_DIRECTORY");
        try
        {
            Environment.SetEnvironmentVariable("LEDSYNC_DATA_DIRECTORY", directory);
            var service = new ProfileService(new JsonProfileRepository());
            var created = await service.CreateAsync("Persistent profile", new ProfileSettings(
                null,
                "screen",
                null,
                "off")
            {
                AudioColors = ["#102030", "#405060"]
            }, CancellationToken.None);

            var persisted = await new JsonProfileRepository().GetAllAsync(CancellationToken.None);

            var reloaded = Assert.Single(persisted);
            Assert.Equal(created.Id, reloaded.Id);
            Assert.Equal(created.Name, reloaded.Name);
            Assert.Equal(created.Settings.AudioColors, reloaded.Settings.AudioColors);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LEDSYNC_DATA_DIRECTORY", originalDirectory);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task JsonRepository_PersistsArduinoControllerAndEdgeCounts()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ledsync-led-test-{Guid.NewGuid():N}");
        var originalDirectory = Environment.GetEnvironmentVariable("LEDSYNC_DATA_DIRECTORY");
        try
        {
            Environment.SetEnvironmentVariable("LEDSYNC_DATA_DIRECTORY", directory);
            var ledSettings = new LedDeviceSettings("ardulight", "COM7", 230400, 20, 8, 3, 6);
            var inputSettings = new ProfileSettings(null, "screen", null, "off")
            {
                LedDevice = ledSettings
            };
            await new ProfileService(new JsonProfileRepository())
                .CreateAsync("Arduino layout", inputSettings, CancellationToken.None);

            var persisted = await new JsonProfileRepository().GetAllAsync(CancellationToken.None);

            Assert.Equal(ledSettings, Assert.Single(persisted).Settings.LedDevice);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LEDSYNC_DATA_DIRECTORY", originalDirectory);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task JsonRepository_AddsArduinoDefaultsToExistingProfiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ledsync-legacy-test-{Guid.NewGuid():N}");
        var originalDirectory = Environment.GetEnvironmentVariable("LEDSYNC_DATA_DIRECTORY");
        try
        {
            Environment.SetEnvironmentVariable("LEDSYNC_DATA_DIRECTORY", directory);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "profiles.json"),
                """
                [{
                  "id": "e8ea2fe3-929e-4af1-8c1e-263d2e761b91",
                  "name": "Existing",
                  "createdAt": "2026-01-01T00:00:00Z",
                  "updatedAt": "2026-01-01T00:00:00Z",
                  "settings": {
                    "displayId": null,
                    "framesPerSecond": 30,
                    "captureMode": "screen",
                    "audioDeviceId": null,
                    "soundMode": "off"
                  },
                  "isActive": true
                }]
                """);

            var profile = Assert.Single(await new JsonProfileRepository().GetAllAsync(CancellationToken.None));

            Assert.Equal(LedDeviceSettings.Default, profile.Settings.LedDevice);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LEDSYNC_DATA_DIRECTORY", originalDirectory);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class InMemoryProfileRepository : IProfileRepository
    {
        private IReadOnlyList<Profile> _profiles = [];

        public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_profiles);

        public Task ReplaceAllAsync(IReadOnlyList<Profile> profiles, CancellationToken cancellationToken)
        {
            _profiles = profiles.ToArray();
            return Task.CompletedTask;
        }
    }
}
