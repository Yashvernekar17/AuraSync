using LedSync.Application.Sync;
using LedSync.Application.Capture;
using LedSync.Application.Profiles;
using LedSync.Domain.Models;
using System.Globalization;

namespace LedSync.Infrastructure.Sync;

/// <summary>Coordinates capture, frame validation, serial output, and synchronization lifecycle.</summary>
public sealed class SerialSyncRuntime(ILedOutput ledOutput, IAudioCaptureProvider? audioCapture = null) : ISyncRuntime
{
    private readonly SemaphoreSlim _outputGate = new(1, 1);
    private readonly object _configurationGate = new();
    private readonly object _audioLevelsGate = new();
    private string? _activeProfileId;
    private string? _message;
    private bool _isRunning;
    private bool _isStarting;
    private AudioLevels _audioLevels;
    private byte[]? _lastOutputFrame;
    private CancellationTokenSource? _audioLoopCancellation;
    private Task? _audioLoopTask;

    public SyncConfiguration Configuration { get; private set; } = SyncConfiguration.Default;

    public SyncStatus GetStatus() => new(
        _isRunning,
        _isRunning ? "running" : "stopped",
        _activeProfileId,
        Configuration.DisplayId,
        Configuration.FramesPerSecond,
        _message)
    {
        LedDevice = Configuration.LedDevice,
        CaptureMode = Configuration.CaptureMode
    };

    public void SetConfiguration(SyncConfiguration configuration)
    {
        if (configuration.FramesPerSecond is < 1 or > 240)
            throw new ArgumentOutOfRangeException(nameof(configuration), "Frames per second must be between 1 and 240.");
        if (configuration.CaptureMode is not ("screen" or "audio" or "custom"))
            throw new ArgumentException("Capture mode must be 'screen', 'audio', or 'custom'.", nameof(configuration));
        if (configuration.SoundMode is not ("off" or "average" or "spectrum"))
            throw new ArgumentException("Sound mode must be 'off' or 'average'.", nameof(configuration));
        ValidateAudioColors(configuration.AudioColors);
        CustomEffectSettings.Validate(
            configuration.CustomEffect,
            configuration.CustomColors,
            configuration.CustomSpeed);
        LedDeviceSettingsValidator.Validate(configuration.LedDevice);
        lock (_configurationGate)
        {
            if ((_isRunning || _isStarting) && configuration.LedDevice != Configuration.LedDevice)
                throw new InvalidOperationException("Stop synchronization before changing the LED controller, port, baud rate, or layout.");
            if ((_isRunning || _isStarting) &&
                (configuration.CaptureMode != Configuration.CaptureMode ||
                 configuration.AudioDeviceId != Configuration.AudioDeviceId))
                throw new InvalidOperationException("Stop synchronization before changing the active capture mode or audio output device.");

            Configuration = configuration with
            {
                SoundMode = configuration.CaptureMode == "audio" ? "average" : "off"
            };
        }
    }

    public void SetActiveProfile(Profile? profile)
    {
        _activeProfileId = profile?.Id.ToString();
        if (profile is not null)
            SetConfiguration(new SyncConfiguration(
                profile.Settings.DisplayId,
                Configuration.FramesPerSecond,
                profile.Settings.CaptureMode,
                profile.Settings.AudioDeviceId,
                profile.Settings.SoundMode)
            {
                LedDevice = profile.Settings.LedDevice,
                AudioColors = profile.Settings.AudioColors,
                CustomEffect = profile.Settings.CustomEffect,
                CustomColors = profile.Settings.CustomColors,
                CustomSpeed = profile.Settings.CustomSpeed
            });
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _outputGate.WaitAsync(cancellationToken);
        try
        {
            SyncConfiguration configuration;
            lock (_configurationGate)
            {
                if (_isRunning)
                    return;
                _isStarting = true;
                configuration = Configuration;
            }

            if (configuration.CaptureMode == "screen" && string.IsNullOrWhiteSpace(configuration.DisplayId))
                throw new InvalidOperationException("Select a display before starting LED synchronization.");
            if (configuration.CaptureMode == "audio" &&
                (audioCapture is null || string.IsNullOrWhiteSpace(configuration.AudioDeviceId)))
                throw new InvalidOperationException("Select an audio output device before starting sound synchronization.");

            await ledOutput.OpenAsync(configuration.LedDevice, cancellationToken);
            if (configuration.CaptureMode == "audio")
                audioCapture!.Start(configuration.AudioDeviceId!, levels =>
                {
                    lock (_audioLevelsGate) _audioLevels = levels;
                }, HandleAudioCaptureError);
            lock (_configurationGate)
            {
                _isRunning = true;
                _isStarting = false;
                _lastOutputFrame = null;
                _message = configuration.CaptureMode == "audio"
                    ? "Capturing audio from the selected output device."
                    : $"Connected to {configuration.LedDevice.SerialPort} at {configuration.LedDevice.BaudRate} baud.";
                if (configuration.CaptureMode == "audio")
                {
                    _audioLoopCancellation = new CancellationTokenSource();
                    var audioToken = _audioLoopCancellation.Token;
                    _audioLoopTask = Task.Run(
                        () => RunAudioLoopAsync(configuration, audioToken),
                        CancellationToken.None);
                }
            }
        }
        catch (Exception exception)
        {
            Exception? cleanupException = null;
            try { audioCapture?.Stop(); }
            catch (Exception cleanup) { cleanupException = cleanup; }
            try { await ledOutput.CloseAsync(CancellationToken.None); }
            catch (Exception cleanup) { cleanupException ??= cleanup; }
            lock (_configurationGate)
            {
                _isRunning = false;
                _isStarting = false;
                _message = exception.Message;
            }
            if (cleanupException is not null)
                throw new AggregateException("Audio synchronization failed and cleanup was incomplete.", exception, cleanupException);
            throw;
        }
        finally
        {
            _outputGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task? audioLoop;
        CancellationTokenSource? audioLoopCancellation;
        lock (_configurationGate)
        {
            audioLoopCancellation = _audioLoopCancellation;
            audioLoopCancellation?.Cancel();
            audioLoop = _audioLoopTask;
            _audioLoopTask = null;
            _audioLoopCancellation = null;
        }
        Exception? stopError = null;
        try { audioCapture?.Stop(); }
        catch (Exception exception) { stopError = exception; }
        if (audioLoop is not null)
        {
            try { await audioLoop; }
            catch (Exception exception) { stopError ??= exception; }
        }
        audioLoopCancellation?.Dispose();

        await _outputGate.WaitAsync(cancellationToken);
        try
        {
            var wasRunning = _isRunning;
            try
            {
                if (wasRunning)
                    await SendFadeOutAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                stopError = exception;
            }
            try
            {
                await ledOutput.CloseAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                stopError ??= exception;
            }

            _isRunning = false;
            _lastOutputFrame = null;
            _message = stopError is null ? null : $"LED output stopped with an error: {stopError.Message}";
            if (stopError is not null)
                throw new IOException(_message, stopError);
        }
        finally
        {
            _outputGate.Release();
        }
    }

    public async Task WriteFrameAsync(ReadOnlyMemory<byte> rgbFrame, CancellationToken cancellationToken)
    {
        await _outputGate.WaitAsync(cancellationToken);
        try
        {
            if (!_isRunning)
                throw new InvalidOperationException("LED synchronization is stopped.");
            if (rgbFrame.Length != Configuration.LedDevice.LedCount * 3)
                throw new ArgumentException(
                    $"A frame for {Configuration.LedDevice.LedCount} LEDs must contain exactly {Configuration.LedDevice.LedCount * 3} RGB bytes.",
                    nameof(rgbFrame));

            await ledOutput.WriteFrameAsync(rgbFrame, cancellationToken);
            var lastOutputFrame = _lastOutputFrame;
            if (lastOutputFrame is null || lastOutputFrame.Length != rgbFrame.Length)
            {
                lastOutputFrame = new byte[rgbFrame.Length];
                _lastOutputFrame = lastOutputFrame;
            }
            rgbFrame.CopyTo(lastOutputFrame);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _isRunning = false;
            _message = $"LED output failed: {exception.Message}";
            await ledOutput.CloseAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            _outputGate.Release();
        }
    }

    private async Task SendFadeOutAsync(CancellationToken cancellationToken)
    {
        var lastFrame = _lastOutputFrame;
        var frame = new byte[Configuration.LedDevice.LedCount * 3];
        const int fadeSteps = 4;
        // Ensure each final fade frame reaches the controller before closing its serial connection.
        for (var step = 1; step <= fadeSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lastFrame is not null && lastFrame.Length == frame.Length)
            {
                var brightness = (fadeSteps - step) / (double)fadeSteps;
                for (var index = 0; index < frame.Length; index++)
                    frame[index] = (byte)Math.Round(lastFrame[index] * brightness);
            }

            await ledOutput.WriteFrameAsync(frame, cancellationToken, ensureTransmitted: true);
        }
    }

    private async Task RunAudioLoopAsync(SyncConfiguration configuration, CancellationToken cancellationToken)
    {
        var packetBytes = 6 + (configuration.LedDevice.LedCount * 3);
        var frameTimeMs = (packetBytes * 10 * 1000d / configuration.LedDevice.BaudRate) +
            (configuration.LedDevice.LedCount * 0.03d);
        var serialCapacity = Math.Max(1, (int)Math.Round((1000d / frameTimeMs) * 0.95d));
        var frameRate = Math.Min(configuration.FramesPerSecond, serialCapacity);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000d / frameRate));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var currentConfiguration = Configuration;
                AudioLevels levels;
                lock (_audioLevelsGate) levels = _audioLevels;
                var frame = CreateAudioFrame(currentConfiguration, levels);
                await WriteFrameAsync(frame, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (_configurationGate)
            {
                _isRunning = false;
                _message = $"Audio synchronization failed: {exception.Message}";
            }
        }
    }

    private void HandleAudioCaptureError(Exception exception)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await StopAsync(CancellationToken.None);
            }
            catch (Exception stopException)
            {
                lock (_configurationGate)
                    _message = $"Audio capture failed: {exception.Message}; stopping output also failed: {stopException.Message}";
                return;
            }

            lock (_configurationGate)
                _message = $"Audio capture failed: {exception.Message}";
        });
    }

    private static byte[] CreateAudioFrame(SyncConfiguration configuration, AudioLevels levels)
    {
        var colors = configuration.AudioColors.Select(ParseColor).ToArray();
        var frame = new byte[configuration.LedDevice.LedCount * 3];
        for (var led = 0; led < configuration.LedDevice.LedCount; led++)
        {
            var palettePosition = levels.Overall * (colors.Length - 1);
            var colorIndex = Math.Min((int)palettePosition, colors.Length - 2);
            var color = Blend(colors[colorIndex], colors[colorIndex + 1], palettePosition - colorIndex);
            var energy = levels.Overall;
            var brightness = energy < 0.025f ? 0 : 0.12f + (energy * 0.88f);
            var offset = led * 3;
            frame[offset] = (byte)Math.Clamp((int)Math.Round(color.Red * brightness), 0, 255);
            frame[offset + 1] = (byte)Math.Clamp((int)Math.Round(color.Green * brightness), 0, 255);
            frame[offset + 2] = (byte)Math.Clamp((int)Math.Round(color.Blue * brightness), 0, 255);
        }
        return frame;
    }

    private static (byte Red, byte Green, byte Blue) ParseColor(string color) => (
        byte.Parse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static (byte Red, byte Green, byte Blue) Blend(
        (byte Red, byte Green, byte Blue) start,
        (byte Red, byte Green, byte Blue) end,
        float amount) => (
        (byte)(start.Red + ((end.Red - start.Red) * amount)),
        (byte)(start.Green + ((end.Green - start.Green) * amount)),
        (byte)(start.Blue + ((end.Blue - start.Blue) * amount)));

    private static void ValidateAudioColors(string[]? colors)
    {
        if (colors is null || colors.Length is < 2 or > 8 ||
            colors.Any(color => color is null || color.Length != 7 || color[0] != '#' ||
                !color.AsSpan(1).ToString().All(Uri.IsHexDigit)))
            throw new ArgumentException("Choose between 2 and 8 valid RGB colors.", nameof(colors));
    }
}
