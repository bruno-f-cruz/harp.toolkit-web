# Harp.Toolkit Web

[Harp.Toolkit](https://github.com/harp-tech/toolkit) in the browser: generate device interfaces, talk to a connected Harp device, and update its firmware, with nothing to install.

| Page | What it does | Browsers |
|---|---|---|
| **Generate** | Generates the C# and Python interfaces and the firmware of a device from its `device.yml` | All |
| **Device** | Connects to a device over USB serial, shows its identity and versions, switches it between Standby and Active, reads and writes any register, and logs every message it sends | Chrome, Edge |
| **Firmware** | Updates the firmware of an ATxmega device from a `.hex` release file, checking first that the file supports the device | Chrome, Edge |

The device pages use the [Web Serial API](https://developer.mozilla.org/docs/Web/API/Web_Serial_API), which only Chromium-based desktop browsers implement, and only on pages served over HTTPS or from `localhost`.

The Generate page started as a copy of [harp-tech/generators-web](https://github.com/harp-tech/generators-web).

## Run locally

This app depends on [Harp.WebSerial](../Harp.WebSerial), which is not on nuget.org yet. Build its package first. `nuget.config` restores it from the sibling folder:

```sh
dotnet pack ../Harp.WebSerial/src/Harp.WebSerial -c Release
dotnet run --project src/Harp.Toolkit.Web
dotnet test
```

## How it works

Everything runs as .NET in WebAssembly. The parts that cannot run in the browser are replaced:

- **Serial ports.** `System.IO.Ports` does not work in WebAssembly. Harp.WebSerial wraps Web Serial in a `Stream`.
- **Harp messages.** Bonsai.Harp reads its serial port synchronously on a dedicated thread, which the browser cannot do. [`HarpMessageReader`](src/Harp.Toolkit.Web/Devices/HarpMessageReader.cs) is an asynchronous port of its parser. [`HarpConnection`](src/Harp.Toolkit.Web/Devices/HarpConnection.cs) matches each command to its reply. Bonsai.Harp itself is still used for message types and core register definitions.
- **Firmware updates.** Harp.Toolkit.Core opens ports by name, so [`AtxmegaBootloader`](src/Harp.Toolkit.Web/Devices/AtxmegaBootloader.cs) follows its bootloader protocol step for step over an [`IDevicePort`](src/Harp.Toolkit.Web/Devices/IDevicePort.cs). Firmware files are still parsed by Harp.Toolkit.Core.
- **Register names.** These come from the device's `device.yml` in the [whoami registry](https://github.com/harp-tech/whoami), looked up by WhoAmI. Core registers come from the `core.yml` embedded in Harp.Generators.

The protocol code depends only on `Stream`, so the tests run it against an emulated device on desktop .NET, including a full firmware update.

### Upstream changes that would remove the copies

- **Bonsai.Harp:** accept a `Stream` (or a transport interface) next to a port name, with an asynchronous receive loop. `HarpMessageReader` and `HarpConnection` could then be dropped.
- **Harp.Toolkit.Core:** let the bootloaders take a port abstraction instead of a port name. `AtxmegaBootloader` could then be dropped.

## Not yet supported

- `verify`, which compiles generated code with Roslyn at run time.
- Updating the SoundCard PIC32 coprocessor, which is not in Harp.Toolkit.Core 0.4.0.
- Spreadsheet to `device.yml` conversion.
- Clock synchronization tests between two devices.

## Automation

The GitHub workflows come from generators-web:

- Every hour, the device list is rebuilt from the whoami registry and the site is redeployed if it changed.
- Every day, a pull request is opened if a new Harp.Generators release is on NuGet.

They need Harp.WebSerial to be restorable in CI, from nuget.org or a feed, before they can build.
