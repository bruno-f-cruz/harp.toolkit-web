# Harp.WebSerial

Serial port access for Blazor WebAssembly apps through the browser [Web Serial API](https://developer.mozilla.org/docs/Web/API/Web_Serial_API), for communicating with [Harp](https://harp-tech.org) devices from the browser.

`System.IO.Ports` does not work in WebAssembly. This package gives you a `WebSerialPort` with a `Stream`, so code written against a serial port's `BaseStream` can run in the browser.

## Browser support

Web Serial is available only in Chromium-based desktop browsers (Chrome, Edge, Opera), and only on pages served over HTTPS or from `localhost`. Check `SerialPorts.IsSupported` before using it.

## Usage

Load the JavaScript module once at startup, in `Program.cs`:

```csharp
var host = builder.Build();
await SerialPorts.InitializeAsync();
await host.RunAsync();
```

The APIs are marked as browser-only, so declare the app browser-only too, for example in `AssemblyInfo.cs`:

```csharp
[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]
```

Ask the user for a port in response to a click, then open it with the Harp settings (1 Mbaud, hardware flow control, 1 MiB receive buffer):

```csharp
var port = await SerialPorts.RequestPortAsync(WebSerialPortFilter.Ftdi);
if (port is null) return; // the user dismissed the chooser

await port.OpenAsync(WebSerialOptions.Harp);
var stream = port.BaseStream;
await stream.WriteAsync(command);
var count = await stream.ReadAsync(buffer, cancellationToken);
```

Ports the user already granted are available without a prompt from `SerialPorts.GetPortsAsync()`.

To switch a device to another baud rate, for example a bootloader, close the port and open it again:

```csharp
await port.CloseAsync();
await port.OpenAsync(new WebSerialOptions { BaudRate = 1000000 });
await port.SetSignalsAsync(requestToSend: true);
```

## Behavior that differs from `System.IO.Ports`

The browser runs .NET on a single thread and cannot block, so:

- **Reads:** `ReadAsync` waits for data. Synchronous `Read` returns only data that has already arrived, and throws `NotSupportedException` when none has. Cancelling `ReadAsync` keeps any data that arrives for the next read.
- **Writes:** writes are queued in order and never block. `WriteAsync` and `FlushAsync` wait until the data is sent; a failed synchronous `Write` is reported by the next `FlushAsync`.
- **Closing:** use `CloseAsync` or `DisposeAsync`. Synchronous `Dispose` on the stream starts closing the port and returns before it is closed.
- **Port names:** there are none. A port is identified by the object the user granted, and its USB vendor and product IDs.
- **Errors:** I/O failures surface as `IOException` with the browser's message, for example when the device is unplugged.

## Sample

The [Device page](../Harp.Toolkit.Web/Pages/Device.razor) of Harp.Toolkit Web uses this package to connect to Harp devices.

## Building the package

```sh
dotnet pack src/Harp.WebSerial -c Release
```

The package is written to `src/Harp.WebSerial/bin/Release`.
