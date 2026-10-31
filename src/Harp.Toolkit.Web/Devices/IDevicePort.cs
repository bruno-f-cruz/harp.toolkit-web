namespace Harp.Toolkit.Web.Devices;

/// <summary>
/// Specifies the settings used to open the serial port of a Harp device.
/// </summary>
public enum PortMode
{
    /// <summary>
    /// The Harp protocol: 1 Mbaud with hardware flow control, and DTR asserted.
    /// </summary>
    Protocol,

    /// <summary>
    /// The ATxmega bootloader: 1 Mbaud with no flow control, and DTR and RTS cleared.
    /// </summary>
    Bootloader
}

/// <summary>
/// Represents a serial port that can be opened with the settings needed at each stage of
/// talking to a Harp device.
/// </summary>
public interface IDevicePort
{
    /// <summary>
    /// Asynchronously opens the port with the settings of the specified mode.
    /// </summary>
    /// <param name="mode">The settings with which to open the port.</param>
    /// <returns>
    /// The task object representing the asynchronous operation. The <see cref="Task{TResult}.Result"/>
    /// property contains the stream used to read and write data.
    /// </returns>
    Task<Stream> OpenAsync(PortMode mode);

    /// <summary>
    /// Asynchronously closes the port. Closing a port that is not open has no effect.
    /// </summary>
    /// <returns>The task object representing the asynchronous operation.</returns>
    Task CloseAsync();
}
