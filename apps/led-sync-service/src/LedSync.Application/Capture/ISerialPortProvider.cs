namespace LedSync.Application.Capture;

/// <summary>Port for listing serial ports available to the controller configuration UI.</summary>
public interface ISerialPortProvider
{
    Task<IReadOnlyList<string>> GetPortsAsync(CancellationToken cancellationToken);
}
