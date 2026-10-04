using System.IO.Ports;
using System.Diagnostics;
using LedSync.Application.Sync;
using LedSync.Domain.Models;

namespace LedSync.Infrastructure.Sync;

/// <summary>Owns the Windows serial connection and reuses a packet buffer for frame writes.</summary>
public sealed class WindowsSerialLedOutput : ILedOutput, IDisposable
{
    private SerialPort? _serialPort;
    private string? _controllerType;
    private int _ledCount;
    private byte[]? _packetBuffer;

    public async Task OpenAsync(LedDeviceSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Arduino serial output is currently supported on Windows only.");
        if (string.IsNullOrWhiteSpace(settings.SerialPort))
            throw new InvalidOperationException("Select or enter the Arduino COM port in the active profile.");

        await CloseAsync(CancellationToken.None);

        var port = new SerialPort(settings.SerialPort, settings.BaudRate, Parity.None, 8, StopBits.One)
        {
            DtrEnable = false,
            Handshake = Handshake.None,
            WriteTimeout = 2_000
        };

        try
        {
            port.Open();
            cancellationToken.ThrowIfCancellationRequested();
            _serialPort = port;
            _controllerType = settings.ControllerType;
            _ledCount = settings.LedCount;
            _packetBuffer = new byte[(settings.ControllerType == "adalight" ? 6 : 1) + settings.LedCount * 3];
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }

    public async Task WriteFrameAsync(
        ReadOnlyMemory<byte> rgbFrame,
        CancellationToken cancellationToken,
        bool ensureTransmitted = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var port = _serialPort ?? throw new InvalidOperationException("The Arduino serial port is not open.");
        var packet = _packetBuffer ?? throw new InvalidOperationException("The Arduino serial packet buffer is not initialized.");
        var packetLength = AdalightFrameEncoder.EncodeInto(rgbFrame.Span, _ledCount, _controllerType!, packet);
        // Let the next timer tick produce a fresh frame rather than queueing output that is already stale.
        if (!ensureTransmitted && port.BytesToWrite > packetLength / 4)
            return;

        if (ensureTransmitted)
            await WaitForTransmissionAsync(port, cancellationToken);
        await port.BaseStream.WriteAsync(packet.AsMemory(0, packetLength), cancellationToken);
        await port.BaseStream.FlushAsync(cancellationToken);
        if (ensureTransmitted)
            await WaitForTransmissionAsync(port, cancellationToken);
    }

    private static async Task WaitForTransmissionAsync(
        SerialPort port,
        CancellationToken cancellationToken)
    {
        var timeout = Stopwatch.StartNew();
        while (port.BytesToWrite > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (timeout.Elapsed >= TimeSpan.FromSeconds(2))
                throw new TimeoutException("Timed out waiting for the LED controller to receive its final frame.");
            await Task.Delay(5, cancellationToken);
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        var port = _serialPort;
        _serialPort = null;
        _controllerType = null;
        _ledCount = 0;
        _packetBuffer = null;
        if (port is not null)
        {
            port.Close();
            port.Dispose();
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _serialPort?.Dispose();
        _serialPort = null;
    }
}
