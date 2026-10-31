namespace Harp.WebSerial;

/// <summary>
/// Represents a USB filter that limits which ports the browser offers when requesting a port.
/// </summary>
/// <param name="UsbVendorId">The USB vendor ID of the device.</param>
/// <param name="UsbProductId">The optional USB product ID of the device.</param>
public readonly record struct WebSerialPortFilter(ushort UsbVendorId, ushort? UsbProductId = null)
{
    /// <summary>
    /// Gets a filter that matches FTDI USB serial converters, which most Harp devices use.
    /// </summary>
    public static WebSerialPortFilter Ftdi => new(0x0403);
}
