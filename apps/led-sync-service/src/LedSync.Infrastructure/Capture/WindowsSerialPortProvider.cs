using LedSync.Application.Capture;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace LedSync.Infrastructure.Capture;

public sealed class WindowsSerialPortProvider : ISerialPortProvider
{
    private const string SerialPortMap = @"HARDWARE\DEVICEMAP\SERIALCOMM";

    public Task<IReadOnlyList<string>> GetPortsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Serial-port enumeration is currently implemented for Windows only.");

        return Task.FromResult<IReadOnlyList<string>>(EnumerateWindowsPorts());
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> EnumerateWindowsPorts()
    {
        using var key = Registry.LocalMachine.OpenSubKey(SerialPortMap);
        if (key is null)
            return [];

        var ports = key.GetValueNames()
            .Select(name => key.GetValue(name) as string)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, SerialPortComparer.Instance)
            .ToArray();
        return ports;
    }

    private sealed class SerialPortComparer : IComparer<string>
    {
        public static SerialPortComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            if (TryGetPortNumber(left, out var leftNumber) &&
                TryGetPortNumber(right, out var rightNumber))
                return leftNumber.CompareTo(rightNumber);
            return StringComparer.OrdinalIgnoreCase.Compare(left, right);
        }

        private static bool TryGetPortNumber(string value, out int portNumber)
        {
            portNumber = 0;
            return value.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
                   int.TryParse(value.AsSpan(3), out portNumber);
        }
    }
}
