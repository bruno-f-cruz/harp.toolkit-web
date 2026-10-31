using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Harp.WebSerial;

[SupportedOSPlatform("browser")]
static partial class Interop
{
    internal const string ModuleName = "harp-webserial";

    [JSImport("isSupported", ModuleName)]
    internal static partial bool IsSupported();

    [JSImport("requestPort", ModuleName)]
    internal static partial Task<JSObject?> RequestPortAsync(string? filtersJson);

    [JSImport("getPorts", ModuleName)]
    internal static partial Task<JSObject> GetPortsAsync();

    [JSImport("getPortCount", ModuleName)]
    internal static partial int GetPortCount(JSObject ports);

    [JSImport("getPortAt", ModuleName)]
    internal static partial JSObject GetPortAt(JSObject ports, int index);

    [JSImport("getVendorId", ModuleName)]
    internal static partial int GetVendorId(JSObject port);

    [JSImport("getProductId", ModuleName)]
    internal static partial int GetProductId(JSObject port);

    [JSImport("isSamePort", ModuleName)]
    internal static partial bool IsSamePort(JSObject a, JSObject b);

    [JSImport("forget", ModuleName)]
    internal static partial Task ForgetAsync(JSObject port);

    [JSImport("open", ModuleName)]
    internal static partial Task<JSObject> OpenAsync(
        JSObject port,
        int baudRate,
        int dataBits,
        int stopBits,
        string parity,
        string flowControl,
        int bufferSize);

    [JSImport("waitForData", ModuleName)]
    internal static partial Task<int> WaitForDataAsync(JSObject handle);

    [JSImport("bytesAvailable", ModuleName)]
    internal static partial int BytesAvailable(JSObject handle);

    [JSImport("take", ModuleName)]
    internal static partial int Take(JSObject handle, [JSMarshalAs<JSType.MemoryView>] Span<byte> buffer);

    [JSImport("write", ModuleName)]
    internal static partial void Write(JSObject handle, [JSMarshalAs<JSType.MemoryView>] Span<byte> buffer);

    [JSImport("drain", ModuleName)]
    internal static partial Task DrainAsync(JSObject handle);

    [JSImport("setSignals", ModuleName)]
    internal static partial Task SetSignalsAsync(JSObject handle, int dataTerminalReady, int requestToSend, int breakSignal);

    [JSImport("getSignals", ModuleName)]
    internal static partial Task<int> GetSignalsAsync(JSObject handle);

    [JSImport("close", ModuleName)]
    internal static partial Task CloseAsync(JSObject handle);
}
