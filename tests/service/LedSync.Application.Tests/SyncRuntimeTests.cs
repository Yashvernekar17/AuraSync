using LedSync.Application.Sync;
using LedSync.Application.Capture;
using LedSync.Domain.Models;
using LedSync.Infrastructure.Sync;

namespace LedSync.Application.Tests;

public sealed class SyncRuntimeTests
{
    [Fact]
    public void SetActiveProfile_PreservesApplicationFrameRate()
    {
        var runtime = new SerialSyncRuntime(new RecordingLedOutput());
        runtime.SetConfiguration(CreateConfiguration() with { FramesPerSecond = 75 });
        var profile = new Profile(
            Guid.NewGuid(),
            "Movie night",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            new ProfileSettings("profile-display", "screen", null, "off")
            {
                LedDevice = CreateConfiguration().LedDevice
            },
            true);

        runtime.SetActiveProfile(profile);

        Assert.Equal(75, runtime.GetStatus().FramesPerSecond);
        Assert.Equal("profile-display", runtime.GetStatus().SelectedDisplayId);
    }

    [Fact]
    public void AdalightEncoder_WritesMagicWordLengthChecksumAndRgb()
    {
        byte[] rgb = [12, 34, 56, 78, 90, 123];

        var packet = AdalightFrameEncoder.Encode(rgb, 2, "adalight");

        Assert.Equal(new byte[] { (byte)'A', (byte)'d', (byte)'a', 0, 1, 0x54, 12, 34, 56, 78, 90, 123 }, packet);
    }

    [Fact]
    public void AdalightEncoder_EncodesTheProvidedSketchLedCount()
    {
        byte[] rgb = new byte[108 * 3];

        var packet = AdalightFrameEncoder.Encode(rgb, 108, "adalight");

        Assert.Equal(6 + (108 * 3), packet.Length);
        Assert.Equal(new byte[] { (byte)'A', (byte)'d', (byte)'a', 0, 107, 0x3e }, packet[..6]);
    }

    [Fact]
    public void ArdulightEncoder_WritesSyncByteAndRgb()
    {
        byte[] rgb = [1, 2, 3];

        var packet = AdalightFrameEncoder.Encode(rgb, 1, "ardulight");

        Assert.Equal(new byte[] { 0xff, 1, 2, 3 }, packet);
    }

    [Fact]
    public void Encoder_WritesIntoReusableBufferWithoutChangingPacket()
    {
        byte[] rgb = [12, 34, 56, 78, 90, 123];
        var packet = new byte[12];

        var packetLength = AdalightFrameEncoder.EncodeInto(rgb, 2, "adalight", packet);

        Assert.Equal(12, packetLength);
        Assert.Equal(AdalightFrameEncoder.Encode(rgb, 2, "adalight"), packet);
    }

    [Fact]
    public async Task StartAndWriteFrame_OpensConfiguredPortAndForwardsRgb()
    {
        var output = new RecordingLedOutput();
        var runtime = new SerialSyncRuntime(output);
        var settings = CreateConfiguration();
        runtime.SetConfiguration(settings);

        await runtime.StartAsync(CancellationToken.None);
        byte[] rgb = [11, 22, 33, 44, 55, 66];
        await runtime.WriteFrameAsync(rgb, CancellationToken.None);

        Assert.True(runtime.GetStatus().IsRunning);
        Assert.Equal(settings.LedDevice, output.OpenedSettings);
        Assert.Equal(settings.LedDevice, runtime.GetStatus().LedDevice);
        Assert.Equal(rgb, Assert.Single(output.Frames));
    }

    [Fact]
    public async Task CustomLighting_StartsWithoutDisplayOrAudioCapture()
    {
        var output = new RecordingLedOutput();
        var runtime = new SerialSyncRuntime(output);
        runtime.SetConfiguration(CreateConfiguration() with
        {
            DisplayId = null,
            CaptureMode = "custom",
            CustomEffect = "rainbow",
            CustomColors = ["#FF0000", "#0000FF"],
            CustomSpeed = 75
        });

        await runtime.StartAsync(CancellationToken.None);

        Assert.True(runtime.GetStatus().IsRunning);
        Assert.Equal("custom", runtime.GetStatus().CaptureMode);
        Assert.Equal("rainbow", runtime.Configuration.CustomEffect);
        await runtime.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void CustomLighting_AcceptsFireEffect()
    {
        var runtime = new SerialSyncRuntime(new RecordingLedOutput());

        runtime.SetConfiguration(CreateConfiguration() with
        {
            CaptureMode = "custom",
            CustomEffect = "fire",
            CustomColors = ["#FF4500"]
        });

        Assert.Equal("fire", runtime.Configuration.CustomEffect);
    }

    [Fact]
    public async Task AudioCapture_MapsOutputLevelsThroughConfiguredPalette()
    {
        var output = new RecordingLedOutput();
        var audio = new RecordingAudioCapture();
        var runtime = new SerialSyncRuntime(output, audio);
        var configuration = CreateConfiguration() with
        {
            FramesPerSecond = 240,
            CaptureMode = "audio",
            AudioDeviceId = "output-1",
            SoundMode = "average",
            AudioColors = ["#FF0000", "#00FF00"]
        };
        runtime.SetConfiguration(configuration);

        await runtime.StartAsync(CancellationToken.None);
        audio.Publish(new AudioLevels(1, 0, 0, 0));
        await Task.Delay(30);
        await runtime.StopAsync(CancellationToken.None);

        Assert.True(audio.Started);
        Assert.Contains(output.Frames, frame => frame[0] == 0 && frame[1] > 0 && frame[2] == 0);
        Assert.True(audio.Stopped);
    }

    [Fact]
    public async Task StopDuringAudioFrameWrite_StillSendsFadeOutBeforeClosingPort()
    {
        var output = new RecordingLedOutput { BlockFrameWritesUntilCanceled = true };
        var audio = new RecordingAudioCapture();
        var runtime = new SerialSyncRuntime(output, audio);
        runtime.SetConfiguration(CreateConfiguration() with
        {
            FramesPerSecond = 240,
            CaptureMode = "audio",
            AudioDeviceId = "output-1",
            AudioColors = ["#FFFFFF", "#FFFFFF"]
        });
        await runtime.StartAsync(CancellationToken.None);
        await output.FrameWriteStarted.WaitAsync(TimeSpan.FromSeconds(2));

        await runtime.StopAsync(CancellationToken.None);

        Assert.False(runtime.GetStatus().IsRunning);
        Assert.True(output.Closed);
        Assert.Equal(4, output.Frames.Count);
        Assert.All(output.Frames, frame => Assert.Equal(new byte[6], frame));
        Assert.All(output.EnsureTransmitted, Assert.True);
    }

    [Fact]
    public async Task AudioCapture_NormalizesLegacySpectrumSettingToAverage()
    {
        var output = new RecordingLedOutput();
        var audio = new RecordingAudioCapture();
        var runtime = new SerialSyncRuntime(output, audio);
        var configuration = CreateConfiguration() with
        {
            FramesPerSecond = 240,
            CaptureMode = "audio",
            AudioDeviceId = "output-1",
            SoundMode = "spectrum",
            AudioColors = ["#FF0000", "#00FF00"]
        };
        runtime.SetConfiguration(configuration);

        await runtime.StartAsync(CancellationToken.None);
        audio.Publish(new AudioLevels(0.5f, 0.9f, 0, 0));
        await Task.Delay(30);
        await runtime.StopAsync(CancellationToken.None);

        Assert.Equal("average", runtime.Configuration.SoundMode);
        var frame = output.Frames.Last(item => item[0] > 0);
        Assert.Equal(frame[0], frame[3]);
        Assert.Equal(frame[1], frame[4]);
        Assert.Equal(frame[2], frame[5]);
    }

    [Fact]
    public async Task WriteFrame_RejectsFrameWithWrongLedCount()
    {
        var output = new RecordingLedOutput();
        var runtime = new SerialSyncRuntime(output);
        runtime.SetConfiguration(CreateConfiguration());
        await runtime.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            runtime.WriteFrameAsync(new byte[] { 1, 2, 3 }, CancellationToken.None));
        Assert.Empty(output.Frames);
    }

    [Fact]
    public async Task Stop_SendsBlackFrameAndClosesPort()
    {
        var output = new RecordingLedOutput();
        var runtime = new SerialSyncRuntime(output);
        runtime.SetConfiguration(CreateConfiguration());
        await runtime.StartAsync(CancellationToken.None);
        await runtime.WriteFrameAsync(new byte[] { 200, 100, 40, 200, 100, 40 }, CancellationToken.None);

        await runtime.StopAsync(CancellationToken.None);

        Assert.False(runtime.GetStatus().IsRunning);
        Assert.True(output.Closed);
        Assert.Equal(5, output.Frames.Count);
        Assert.Equal(new byte[] { 150, 75, 30, 150, 75, 30 }, output.Frames[^4]);
        Assert.Equal(new byte[] { 100, 50, 20, 100, 50, 20 }, output.Frames[^3]);
        Assert.Equal(new byte[] { 50, 25, 10, 50, 25, 10 }, output.Frames[^2]);
        Assert.Equal(new byte[6], output.Frames[^1]);
        Assert.All(output.EnsureTransmitted.Skip(1), Assert.True);
    }

    private static SyncConfiguration CreateConfiguration() => new(
        "display-1",
        30,
        "screen",
        null,
        "off")
    {
        LedDevice = new LedDeviceSettings("adalight", "COM3", 115200, 2, 2, 0, 0)
    };

    private sealed class RecordingLedOutput : ILedOutput
    {
        private readonly TaskCompletionSource _frameWriteStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LedDeviceSettings? OpenedSettings { get; private set; }
        public List<byte[]> Frames { get; } = [];
        public List<bool> EnsureTransmitted { get; } = [];
        public bool Closed { get; private set; }
        public bool BlockFrameWritesUntilCanceled { get; init; }
        public Task FrameWriteStarted => _frameWriteStarted.Task;

        public Task OpenAsync(LedDeviceSettings settings, CancellationToken cancellationToken)
        {
            OpenedSettings = settings;
            return Task.CompletedTask;
        }

        public async Task WriteFrameAsync(
            ReadOnlyMemory<byte> rgbFrame,
            CancellationToken cancellationToken,
            bool ensureTransmitted = false)
        {
            if (BlockFrameWritesUntilCanceled && !ensureTransmitted)
            {
                _frameWriteStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            Frames.Add(rgbFrame.ToArray());
            EnsureTransmitted.Add(ensureTransmitted);
        }

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            Closed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAudioCapture : IAudioCaptureProvider
    {
        private Action<AudioLevels>? _callback;
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }

        public Task<IReadOnlyList<AudioDevice>> GetAudioDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AudioDevice>>([]);

        public void Start(string deviceId, Action<AudioLevels> onLevels, Action<Exception> onError)
        {
            Assert.Equal("output-1", deviceId);
            _callback = onLevels;
            Started = true;
        }

        public void Publish(AudioLevels levels) => _callback?.Invoke(levels);
        public void Stop() => Stopped = true;
    }
}
