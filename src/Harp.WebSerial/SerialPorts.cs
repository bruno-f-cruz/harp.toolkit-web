using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;

namespace Harp.WebSerial;

/// <summary>
/// Provides access to the serial ports of the browser through the Web Serial API.
/// </summary>
/// <remarks>
/// The Web Serial API is available only in Chromium-based desktop browsers, such as Chrome and Edge,
/// and only on pages served over HTTPS or from localhost. Call <see cref="InitializeAsync"/> once
/// before using any other member.
/// </remarks>
[SupportedOSPlatform("browser")]
public static class SerialPorts
{
    /// <summary>
    /// The default URL of the JavaScript module, relative to the <c>_framework</c> folder of the app.
    /// </summary>
    public const string DefaultModuleUrl = "../_content/Harp.WebSerial/harp-webserial.js";

    static Task? initialization;

    /// <summary>
    /// Asynchronously loads the JavaScript module used to access the Web Serial API.
    /// </summary>
    /// <param name="moduleUrl">
    /// The URL of the JavaScript module. The default URL suits apps that reference this package.
    /// </param>
    /// <returns>The task object representing the asynchronous operation.</returns>
    /// <remarks>Calling this method more than once loads the module only once.</remarks>
    public static Task InitializeAsync(string moduleUrl = DefaultModuleUrl)
    {
        return initialization ??= JSHost.ImportAsync(Interop.ModuleName, moduleUrl);
    }

    /// <summary>
    /// Gets a value indicating whether the browser supports the Web Serial API.
    /// </summary>
    public static bool IsSupported
    {
        get
        {
            EnsureInitialized();
            return Interop.IsSupported();
        }
    }

    /// <summary>
    /// Asynchronously asks the user to select a serial port, and grants the page access to it.
    /// </summary>
    /// <param name="filters">
    /// The optional filters that limit which ports the browser offers to the user.
    /// </param>
    /// <returns>
    /// The task object representing the asynchronous operation. The <see cref="Task{TResult}.Result"/>
    /// property contains the selected port, or <see langword="null"/> if the user selected no port.
    /// </returns>
    /// <remarks>
    /// Browsers allow this call only in response to a user action, such as a button click.
    /// </remarks>
    public static async Task<WebSerialPort?> RequestPortAsync(params WebSerialPortFilter[] filters)
    {
        EnsureInitialized();
        var port = await Interop.RequestPortAsync(filters.Length > 0 ? ToJson(filters) : null);
        return port is null ? null : new WebSerialPort(port);
    }

    /// <summary>
    /// Asynchronously returns the serial ports the user has already granted the page access to.
    /// </summary>
    /// <returns>
    /// The task object representing the asynchronous operation. The <see cref="Task{TResult}.Result"/>
    /// property contains the ports the page can open without asking the user.
    /// </returns>
    public static async Task<WebSerialPort[]> GetPortsAsync()
    {
        EnsureInitialized();
        using var ports = await Interop.GetPortsAsync();
        var result = new WebSerialPort[Interop.GetPortCount(ports)];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = new WebSerialPort(Interop.GetPortAt(ports, i));
        }
        return result;
    }

    internal static void EnsureInitialized()
    {
        if (initialization is not { IsCompletedSuccessfully: true })
        {
            throw new InvalidOperationException(
                $"The Web Serial module is not loaded. Await {nameof(SerialPorts)}.{nameof(InitializeAsync)} first.");
        }
    }

    static string ToJson(WebSerialPortFilter[] filters)
    {
        var json = new StringBuilder("[");
        for (int i = 0; i < filters.Length; i++)
        {
            if (i > 0) json.Append(',');
            json.Append("{\"usbVendorId\":").Append(filters[i].UsbVendorId);
            if (filters[i].UsbProductId is ushort productId)
            {
                json.Append(",\"usbProductId\":").Append(productId);
            }
            json.Append('}');
        }
        return json.Append(']').ToString();
    }
}
