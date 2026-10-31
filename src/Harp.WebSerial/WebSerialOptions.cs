namespace Harp.WebSerial;

/// <summary>
/// Specifies the parity bit of a serial port.
/// </summary>
public enum WebSerialParity
{
    /// <summary>No parity bit is sent.</summary>
    None,
    /// <summary>The parity bit makes the number of set bits even.</summary>
    Even,
    /// <summary>The parity bit makes the number of set bits odd.</summary>
    Odd
}

/// <summary>
/// Specifies the flow control of a serial port.
/// </summary>
public enum WebSerialFlowControl
{
    /// <summary>No flow control is used.</summary>
    None,
    /// <summary>The RTS and CTS signals control the flow of data.</summary>
    Hardware
}

/// <summary>
/// Represents the settings used to open a serial port.
/// </summary>
public sealed record WebSerialOptions
{
    /// <summary>
    /// The baud rate, in bits per second, of the Harp protocol.
    /// </summary>
    public const int HarpBaudRate = 1000000;

    /// <summary>
    /// The size, in bytes, of the receive buffer that Bonsai.Harp uses for its serial ports.
    /// </summary>
    public const int HarpBufferSize = 1048576;

    /// <summary>
    /// Gets the settings that open a Harp device for protocol communication: 1 Mbaud, hardware
    /// flow control, and a receive buffer large enough for high event rates.
    /// </summary>
    public static WebSerialOptions Harp => new()
    {
        BaudRate = HarpBaudRate,
        FlowControl = WebSerialFlowControl.Hardware,
        BufferSize = HarpBufferSize
    };

    /// <summary>
    /// Gets or sets the baud rate, in bits per second.
    /// </summary>
    public int BaudRate { get; init; } = 9600;

    /// <summary>
    /// Gets or sets the number of data bits per frame, either 7 or 8.
    /// </summary>
    public int DataBits { get; init; } = 8;

    /// <summary>
    /// Gets or sets the number of stop bits at the end of each frame, either 1 or 2.
    /// </summary>
    public int StopBits { get; init; } = 1;

    /// <summary>
    /// Gets or sets the parity bit of each frame.
    /// </summary>
    public WebSerialParity Parity { get; init; } = WebSerialParity.None;

    /// <summary>
    /// Gets or sets the flow control of the port.
    /// </summary>
    public WebSerialFlowControl FlowControl { get; init; } = WebSerialFlowControl.None;

    /// <summary>
    /// Gets or sets the size, in bytes, of the browser buffers for received and sent data.
    /// </summary>
    /// <remarks>
    /// The browser default of 255 bytes drops data at high rates, so this property defaults to 64 KiB.
    /// The browser allows at most 16 MiB.
    /// </remarks>
    public int BufferSize { get; init; } = 65536;
}
