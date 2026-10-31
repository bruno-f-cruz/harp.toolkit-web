using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Harp.WebSerial;

/// <summary>
/// Specifies the input control signals of a serial port.
/// </summary>
[Flags]
public enum SerialInputSignals
{
    /// <summary>No input signal is asserted.</summary>
    None = 0,
    /// <summary>The Data Carrier Detect (DCD) signal.</summary>
    DataCarrierDetect = 1,
    /// <summary>The Clear To Send (CTS) signal.</summary>
    ClearToSend = 2,
    /// <summary>The Ring Indicator (RI) signal.</summary>
    RingIndicator = 4,
    /// <summary>The Data Set Ready (DSR) signal.</summary>
    DataSetReady = 8
}

/// <summary>
/// Represents a serial port the user has granted the page access to.
/// </summary>
/// <remarks>
/// A port can be closed and opened again with different settings, for example to switch
/// a device between its protocol and bootloader baud rates.
/// </remarks>
[SupportedOSPlatform("browser")]
public sealed class WebSerialPort : IAsyncDisposable
{
    readonly JSObject port;
    JSObject? handle;
    WebSerialStream? stream;

    internal WebSerialPort(JSObject port)
    {
        this.port = port;
        var vendorId = Interop.GetVendorId(port);
        var productId = Interop.GetProductId(port);
        UsbVendorId = vendorId >= 0 ? (ushort)vendorId : null;
        UsbProductId = productId >= 0 ? (ushort)productId : null;
    }

    /// <summary>
    /// Gets the USB vendor ID of the device, or <see langword="null"/> if the port is not a USB device.
    /// </summary>
    public ushort? UsbVendorId { get; }

    /// <summary>
    /// Gets the USB product ID of the device, or <see langword="null"/> if the port is not a USB device.
    /// </summary>
    public ushort? UsbProductId { get; }

    /// <summary>
    /// Gets a value indicating whether the port is open.
    /// </summary>
    public bool IsOpen => handle is not null;

    /// <summary>
    /// Gets the stream used to read and write data on the open port.
    /// </summary>
    /// <exception cref="InvalidOperationException">The port is not open.</exception>
    public Stream BaseStream => stream ?? throw new InvalidOperationException("The port is not open.");

    /// <summary>
    /// Asynchronously opens the port with the specified settings.
    /// </summary>
    /// <param name="options">The settings used to open the port.</param>
    /// <returns>The task object representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">The port is already open.</exception>
    /// <exception cref="IOException">The browser failed to open the port.</exception>
    public async Task OpenAsync(WebSerialOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (handle is not null)
        {
            throw new InvalidOperationException("The port is already open.");
        }

        try
        {
            handle = await Interop.OpenAsync(
                port,
                options.BaudRate,
                options.DataBits,
                options.StopBits,
                options.Parity.ToString().ToLowerInvariant(),
                options.FlowControl.ToString().ToLowerInvariant(),
                options.BufferSize);
        }
        catch (JSException ex)
        {
            throw new IOException($"The port could not be opened. {ex.Message}", ex);
        }
        stream = new WebSerialStream(this, handle);
    }

    /// <summary>
    /// Asynchronously sets the output control signals of the open port.
    /// </summary>
    /// <param name="dataTerminalReady">
    /// The state of the Data Terminal Ready (DTR) signal, or <see langword="null"/> to leave it unchanged.
    /// </param>
    /// <param name="requestToSend">
    /// The state of the Request To Send (RTS) signal, or <see langword="null"/> to leave it unchanged.
    /// </param>
    /// <param name="breakSignal">
    /// The state of the break signal, or <see langword="null"/> to leave it unchanged.
    /// </param>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public Task SetSignalsAsync(bool? dataTerminalReady = null, bool? requestToSend = null, bool? breakSignal = null)
    {
        return Interop.SetSignalsAsync(GetHandle(), ToInt(dataTerminalReady), ToInt(requestToSend), ToInt(breakSignal));

        static int ToInt(bool? value) => value switch { null => -1, true => 1, false => 0 };
    }

    /// <summary>
    /// Asynchronously reads the input control signals of the open port.
    /// </summary>
    /// <returns>
    /// The task object representing the asynchronous operation. The <see cref="Task{TResult}.Result"/>
    /// property contains the asserted input signals.
    /// </returns>
    public async Task<SerialInputSignals> GetSignalsAsync()
    {
        return (SerialInputSignals)await Interop.GetSignalsAsync(GetHandle());
    }

    /// <summary>
    /// Asynchronously closes the port. Closing a port that is not open has no effect.
    /// </summary>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public async Task CloseAsync()
    {
        var current = handle;
        if (current is null) return;

        handle = null;
        stream?.Detach();
        stream = null;
        try
        {
            await Interop.CloseAsync(current);
        }
        catch (JSException ex)
        {
            throw new IOException($"The port could not be closed. {ex.Message}", ex);
        }
        finally
        {
            current.Dispose();
        }
    }

    /// <summary>
    /// Asynchronously closes the port and revokes the access the user granted to it.
    /// </summary>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public async Task ForgetAsync()
    {
        await CloseAsync();
        await Interop.ForgetAsync(port);
    }

    /// <summary>
    /// Asynchronously closes the port and releases the browser object that represents it.
    /// </summary>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        port.Dispose();
    }

    internal void CloseFromStream(JSObject streamHandle)
    {
        // Dispose cannot block in the browser, so the stream starts closing the port and returns.
        if (ReferenceEquals(handle, streamHandle))
        {
            CloseAsync().ContinueWith(
                task => _ = task.Exception,
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    JSObject GetHandle() => handle ?? throw new InvalidOperationException("The port is not open.");
}
