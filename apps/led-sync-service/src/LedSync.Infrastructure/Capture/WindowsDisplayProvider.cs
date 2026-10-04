using System.ComponentModel;
using System.Runtime.InteropServices;
using LedSync.Application.Capture;
using LedSync.Domain.Models;

namespace LedSync.Infrastructure.Capture;

/// <summary>Enumerates Windows monitors and translates native bounds into service display records.</summary>
public sealed class WindowsDisplayProvider : IDisplayProvider
{
    private const int MonitorInfoDeviceNameLength = 32;
    private readonly record struct MonitorRectangle(int Left, int Top, int Right, int Bottom);

    // Field order and fixed-size strings must match the Win32 MONITORINFOEXW layout.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public MonitorRectangle Monitor;
        public MonitorRectangle Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MonitorInfoDeviceNameLength)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, IntPtr rectangle, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRectangle,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string? device,
        uint deviceNumber,
        ref DisplayDevice displayDevice,
        uint flags);

    public Task<IReadOnlyList<DisplaySource>> GetDisplaysAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Display enumeration is currently implemented for Windows only.");

        var displays = new List<DisplaySource>();
        var primaryAssigned = false;
        Win32Exception? monitorInfoError = null;
        MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>(), DeviceName = string.Empty };
            if (!GetMonitorInfo(monitor, ref info))
            {
                monitorInfoError = new Win32Exception(Marshal.GetLastWin32Error());
                return false;
            }

            var device = new DisplayDevice
            {
                Size = Marshal.SizeOf<DisplayDevice>(),
                DeviceName = string.Empty,
                DeviceString = string.Empty,
                DeviceId = string.Empty,
                DeviceKey = string.Empty
            };
            var friendlyName = info.DeviceName;
            if (EnumDisplayDevices(info.DeviceName, 0, ref device, 0) &&
                !string.IsNullOrWhiteSpace(device.DeviceString))
                friendlyName = device.DeviceString;

            var isPrimary = !primaryAssigned && (info.Flags & 1) != 0;
            primaryAssigned |= isPrimary;
            displays.Add(new DisplaySource(
                info.DeviceName,
                friendlyName,
                info.Monitor.Left,
                info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left,
                info.Monitor.Bottom - info.Monitor.Top,
                isPrimary));
            return true;
        };

        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            if (monitorInfoError is not null)
                throw new Win32Exception(monitorInfoError.NativeErrorCode, "Unable to read monitor information.");
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate connected displays.");
        }

        return Task.FromResult<IReadOnlyList<DisplaySource>>(displays);
    }
}
