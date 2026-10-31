using System.Runtime.Versioning;
using Harp.WebSerial;

namespace Harp.Toolkit.Web.Devices;

/// <summary>
/// Opens a browser serial port with the settings Harp.Toolkit uses for each stage.
/// </summary>
/// <remarks>
/// The settings match the toolkit's <c>SerialPort</c> settings: the protocol port uses RTS/CTS flow
/// control with DTR asserted, and the bootloader port uses no flow control with DTR and RTS cleared.
/// </remarks>
[SupportedOSPlatform("browser")]
public sealed class WebSerialDevicePort(WebSerialPort port) : IDevicePort
{
    static readonly WebSerialOptions BootloaderOptions = new()
    {
        BaudRate = WebSerialOptions.HarpBaudRate,
        FlowControl = WebSerialFlowControl.None
    };

    /// <summary>
    /// Gets the browser serial port.
    /// </summary>
    public WebSerialPort Port { get; } = port;

    /// <inheritdoc/>
    public async Task<Stream> OpenAsync(PortMode mode)
    {
        await Port.CloseAsync();
        if (mode == PortMode.Protocol)
        {
            await Port.OpenAsync(WebSerialOptions.Harp);
            await Port.SetSignalsAsync(dataTerminalReady: true);
        }
        else
        {
            await Port.OpenAsync(BootloaderOptions);
            await Port.SetSignalsAsync(dataTerminalReady: false, requestToSend: false);
        }
        return Port.BaseStream;
    }

    /// <inheritdoc/>
    public Task CloseAsync() => Port.CloseAsync();
}
