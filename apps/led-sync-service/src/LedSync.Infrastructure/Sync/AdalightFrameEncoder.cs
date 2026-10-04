using LedSync.Domain.Models;

namespace LedSync.Infrastructure.Sync;

/// <summary>Encodes raw RGB LED bytes into Adalight or Ardulight serial packets.</summary>
public static class AdalightFrameEncoder
{
    public static byte[] Encode(ReadOnlySpan<byte> rgbFrame, int ledCount, string controllerType)
    {
        var headerLength = GetHeaderLength(rgbFrame, ledCount, controllerType);
        var packet = new byte[headerLength + rgbFrame.Length];
        EncodeInto(rgbFrame, ledCount, controllerType, packet);
        return packet;
    }

    public static int EncodeInto(
        ReadOnlySpan<byte> rgbFrame,
        int ledCount,
        string controllerType,
        Span<byte> packet)
    {
        if (ledCount is < 1 or > 511)
            throw new ArgumentOutOfRangeException(nameof(ledCount), "Adalight supports between 1 and 511 LEDs.");
        if (rgbFrame.Length != ledCount * 3)
            throw new ArgumentException("RGB frame length must equal three bytes per LED.", nameof(rgbFrame));

        var headerLength = controllerType switch
        {
            "adalight" => 6,
            "ardulight" => 1,
            _ => throw new ArgumentException("Controller type must be 'adalight' or 'ardulight'.", nameof(controllerType))
        };
        var packetLength = headerLength + rgbFrame.Length;
        if (packet.Length < packetLength)
            throw new ArgumentException($"The packet buffer must contain at least {packetLength} bytes.", nameof(packet));

        if (headerLength == 6)
        {
            // Adalight stores LED count minus one and uses an XOR checksum in its six-byte header.
            var count = ledCount - 1;
            var high = (byte)(count >> 8);
            var low = (byte)count;
            packet[0] = (byte)'A';
            packet[1] = (byte)'d';
            packet[2] = (byte)'a';
            packet[3] = high;
            packet[4] = low;
            packet[5] = (byte)(high ^ low ^ 0x55);
        }
        else
        {
            packet[0] = 0xff;
        }

        rgbFrame.CopyTo(packet[headerLength..]);
        return packetLength;
    }

    private static int GetHeaderLength(ReadOnlySpan<byte> rgbFrame, int ledCount, string controllerType)
    {
        if (ledCount is < 1 or > 511)
            throw new ArgumentOutOfRangeException(nameof(ledCount), "Adalight supports between 1 and 511 LEDs.");
        if (rgbFrame.Length != ledCount * 3)
            throw new ArgumentException("RGB frame length must equal three bytes per LED.", nameof(rgbFrame));

        return controllerType switch
        {
            "adalight" => 6,
            "ardulight" => 1,
            _ => throw new ArgumentException("Controller type must be 'adalight' or 'ardulight'.", nameof(controllerType))
        };
    }
}
