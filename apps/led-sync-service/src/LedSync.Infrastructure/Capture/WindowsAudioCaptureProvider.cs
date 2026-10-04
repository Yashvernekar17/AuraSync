using LedSync.Application.Capture;
using LedSync.Domain.Models;
using NAudio.CoreAudioApi;
using NAudio.Dmo;
using NAudio.Dsp;
using NAudio.Wave;
using System.Runtime.InteropServices;

namespace LedSync.Infrastructure.Capture;

/// <summary>Captures Windows render-endpoint loopback audio and reports normalized band levels.</summary>
public sealed class WindowsAudioCaptureProvider : IAudioCaptureProvider, IDisposable
{
    private const int FftLength = 2048;
    private readonly object _gate = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private WasapiLoopbackCapture? _capture;
    private Action<AudioLevels>? _onLevels;
    private Action<Exception>? _onError;
    private readonly float[] _sampleWindow = new float[FftLength];
    private int _sampleWriteIndex;
    private int _samplesBuffered;
    private long _lastAnalysisAt;

    public Task<IReadOnlyList<AudioDevice>> GetAudioDevicesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWindows();
        using var enumerator = new MMDeviceEnumerator();
        var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var devices = new List<AudioDevice>(endpoints.Count);
        foreach (var endpoint in endpoints)
        {
            try
            {
                devices.Add(new AudioDevice(endpoint.ID, endpoint.FriendlyName, "output"));
            }
            finally
            {
                endpoint.Dispose();
            }
        }
        return Task.FromResult<IReadOnlyList<AudioDevice>>(devices);
    }

    public void Start(string deviceId, Action<AudioLevels> onLevels, Action<Exception> onError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentNullException.ThrowIfNull(onLevels);
        ArgumentNullException.ThrowIfNull(onError);
        EnsureWindows();
        Stop();

        var enumerator = new MMDeviceEnumerator();
        MMDevice device;
        try
        {
            device = enumerator.GetDevice(deviceId);
        }
        catch (COMException exception)
        {
            enumerator.Dispose();
            throw new InvalidOperationException("The selected audio output is unavailable. Refresh the device list and choose an active output.", exception);
        }

        lock (_gate)
        {
            _enumerator = enumerator;
            _device = device;
            _onLevels = onLevels;
            _onError = onError;
            _sampleWriteIndex = 0;
            _samplesBuffered = 0;
            _lastAnalysisAt = 0;
            try
            {
                _capture = new WasapiLoopbackCapture(_device);
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
            }
            catch (Exception startException)
            {
                try
                {
                    Stop();
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException("Audio capture failed and cleanup was incomplete.", startException, cleanupException);
                }
                throw;
            }
        }
    }

    public void Stop()
    {
        WasapiLoopbackCapture? capture;
        MMDevice? device;
        MMDeviceEnumerator? enumerator;
        lock (_gate)
        {
            capture = _capture;
            _capture = null;
            device = _device;
            _device = null;
            enumerator = _enumerator;
            _enumerator = null;
            _onLevels = null;
            _onError = null;
            _samplesBuffered = 0;
            if (capture is not null)
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
            }
        }

        Exception? cleanupError = null;
        if (capture is not null)
        {
            try
            {
                if (capture.CaptureState != CaptureState.Stopped)
                    capture.StopRecording();
            }
            catch (Exception exception)
            {
                cleanupError = exception;
            }
            try { capture.Dispose(); }
            catch (Exception exception) { cleanupError ??= exception; }
        }

        try { device?.Dispose(); }
        catch (Exception exception) { cleanupError ??= exception; }
        try { enumerator?.Dispose(); }
        catch (Exception exception) { cleanupError ??= exception; }
        if (cleanupError is not null)
            throw new IOException("Unable to stop audio output capture cleanly.", cleanupError);
    }

    public void Dispose() => Stop();

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        Action<AudioLevels>? callback;
        Action<Exception>? errorCallback;
        lock (_gate) callback = _onLevels;
        lock (_gate) errorCallback = _onError;
        if (callback is null || args.BytesRecorded == 0) return;

        var capture = sender as WasapiLoopbackCapture;
        if (capture is null) return;
        try
        {
            var samples = ReadMonoSamples(args.Buffer, args.BytesRecorded, capture.WaveFormat);
            if (samples.Length == 0) return;

            float[]? window = null;
            lock (_gate)
            {
                foreach (var sample in samples)
                {
                    _sampleWindow[_sampleWriteIndex] = sample;
                    _sampleWriteIndex = (_sampleWriteIndex + 1) % FftLength;
                    _samplesBuffered = Math.Min(_samplesBuffered + 1, FftLength);
                }

                var now = Environment.TickCount64;
                // Analyze a complete rolling window at a bounded cadence instead of allocating per callback.
                if (_samplesBuffered == FftLength && now - _lastAnalysisAt >= 30)
                {
                    window = new float[FftLength];
                    for (var index = 0; index < FftLength; index++)
                        window[index] = _sampleWindow[(_sampleWriteIndex + index) % FftLength];
                    _lastAnalysisAt = now;
                }
            }
            if (window is not null)
                callback(Analyze(window, capture.WaveFormat.SampleRate));
        }
        catch (Exception exception)
        {
            errorCallback?.Invoke(exception);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        Action<Exception>? callback;
        lock (_gate) callback = _onError;
        if (args.Exception is not null) callback?.Invoke(args.Exception);
    }

    private static float[] ReadMonoSamples(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        var bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample is not (2 or 3 or 4))
            throw new InvalidOperationException($"Unsupported audio sample size: {format.BitsPerSample} bits.");

        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
            format is WaveFormatExtensible extensible &&
            extensible.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT;
        var sampleCount = bytesRecorded / (bytesPerSample * format.Channels);
        var samples = new float[sampleCount];
        for (var sample = 0; sample < sampleCount; sample++)
        {
            var sum = 0f;
            for (var channel = 0; channel < format.Channels; channel++)
            {
                var offset = (sample * format.Channels + channel) * bytesPerSample;
                sum += isFloat
                    ? BitConverter.ToSingle(buffer, offset)
                    : ReadPcmSample(buffer, offset, bytesPerSample);
            }
            samples[sample] = sum / format.Channels;
        }
        return samples;
    }

    private static float ReadPcmSample(byte[] buffer, int offset, int bytesPerSample)
    {
        return bytesPerSample switch
        {
            2 => BitConverter.ToInt16(buffer, offset) / 32768f,
            3 => Read24BitSample(buffer, offset),
            4 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => 0,
        };
    }

    private static float Read24BitSample(byte[] buffer, int offset)
    {
        var value = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        if ((value & 0x800000) != 0) value |= unchecked((int)0xff000000);
        return value / 8388608f;
    }

    private static AudioLevels Analyze(float[] samples, int sampleRate)
    {
        if (samples.Length != FftLength)
            throw new ArgumentException($"Audio analysis requires exactly {FftLength} samples.", nameof(samples));
        var fft = new Complex[FftLength];
        var overall = 0d;
        for (var index = 0; index < FftLength; index++)
            overall += samples[index] * samples[index];
        overall = Math.Sqrt(overall / FftLength);

        for (var index = 0; index < FftLength; index++)
        {
            var window = 0.5 - 0.5 * Math.Cos(2 * Math.PI * index / (FftLength - 1));
            fft[index].X = (float)(samples[index] * window);
        }
        FastFourierTransform.FFT(true, 11, fft);

        var lowPower = 0d;
        var midPower = 0d;
        var highPower = 0d;
        for (var bin = 1; bin < FftLength / 2; bin++)
        {
            var frequency = (double)bin * sampleRate / FftLength;
            var magnitude = Math.Sqrt(fft[bin].X * fft[bin].X + fft[bin].Y * fft[bin].Y);
            var power = magnitude * magnitude;
            if (frequency < 250) lowPower += power;
            else if (frequency < 2000) midPower += power;
            else highPower += power;
        }

        return new AudioLevels(
            Normalize((float)overall * 3),
            Normalize((float)(Math.Sqrt(lowPower) * Math.Sqrt(8) / FftLength * 4)),
            Normalize((float)(Math.Sqrt(midPower) * Math.Sqrt(8) / FftLength * 4)),
            Normalize((float)(Math.Sqrt(highPower) * Math.Sqrt(8) / FftLength * 4)));
    }

    private static float Normalize(float value) =>
        Math.Clamp((float)Math.Sqrt(Math.Clamp(value, 0, 1)), 0, 1);

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Audio output capture is currently available on Windows only.");
    }
}
