using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using Bonsai.Harp;
using Harp.Toolkit.Web.Devices;

namespace Harp.Toolkit.Verify;

/// <summary>
/// A single Harp connection shared by every test in a verification run.
/// </summary>
/// <remarks>
/// This replaces the Harp.Toolkit class of the same name, which opens a serial port by name through
/// Bonsai.Harp and so cannot run in the browser. Its public members behave the same, so the test
/// suites run unchanged. Like the Bonsai.Harp device the original wraps, opening the connection puts
/// the device in Standby with its LEDs on and its heartbeat off, and closing it returns the device to Standby.
/// </remarks>
public sealed class VerifyConnection : IAsyncDisposable, IDisposable
{
    const int ReadTimeoutMilliseconds = 2000;
    const int ConnectDelayMilliseconds = 200;
    const int ReadyAttempts = 5;
    const int ReadyTimeoutMilliseconds = 1000;

    readonly IDevicePort port;
    readonly Stream stream;
    readonly HarpConnection connection;
    readonly Subject<HarpMessage> messages = new();
    bool disposed;

    VerifyConnection(IDevicePort port, Stream stream)
    {
        this.port = port;
        this.stream = stream;
        connection = new HarpConnection(stream);
        connection.MessageReceived += messages.OnNext;
    }

    /// <summary>
    /// Asynchronously opens the connection, reading the device identity first.
    /// </summary>
    /// <param name="port">The closed port of the device.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>
    /// The task object representing the asynchronous operation. The <see cref="Task{TResult}.Result"/>
    /// property contains the open connection.
    /// </returns>
    public static async Task<VerifyConnection> OpenAsync(IDevicePort port, CancellationToken cancellationToken = default)
    {
        var stream = await port.OpenAsync(PortMode.Protocol);
        var connection = new VerifyConnection(port, stream);
        try
        {
            await Task.Delay(ConnectDelayMilliseconds, cancellationToken);
            await connection.WaitUntilReadyAsync(cancellationToken);
            connection.WhoAmI = await connection.ReadWhoAmIAsync(cancellationToken);
            await connection.CommandAsync(CreateOperationControl(OperationMode.Standby), cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    static HarpMessage CreateOperationControl(OperationMode mode) =>
        OperationControl.FromPayload(MessageType.Write, new OperationControlPayload(
            mode,
            dumpRegisters: false,
            muteReplies: false,
            visualIndicators: LedState.On,
            operationLed: LedState.On,
            heartbeat: EnableFlag.Disabled));

    /// <summary>
    /// The device identifier validated when the connection was opened.
    /// </summary>
    public int WhoAmI { get; private set; }

    /// <summary>
    /// Every message received from the device, before any reply correlation.
    /// </summary>
    public IObservable<HarpMessage> Messages => messages;

    /// <summary>
    /// Sends a message without awaiting a reply.
    /// </summary>
    public void Write(HarpMessage message) => stream.Write(message.MessageBytes);

    /// <summary>
    /// Sends the specified request and awaits the matching reply, failing with a
    /// <see cref="TimeoutException"/> if the device does not answer in time.
    /// </summary>
    public async Task<HarpMessage> CommandAsync(HarpMessage command, CancellationToken cancellationToken = default)
    {
        using var replyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        replyTimeout.CancelAfter(ReadTimeoutMilliseconds);
        var reply = messages.FirstAsync(message =>
        {
            var match = message.IsMatch(command.Address, command.MessageType);
            if (match && message.Error)
            {
                throw new HarpException(message);
            }

            return match;
        }).RunAsync(replyTimeout.Token);

        Write(command);
        try
        {
            return await reply;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The device did not reply to a {command.MessageType} request at address " +
                $"{command.Address} within {ReadTimeoutMilliseconds} ms.");
        }
    }

    /// <summary>
    /// Sends the specified messages and collects everything received for the given duration.
    /// </summary>
    public async Task<IList<HarpMessage>> WriteAndCollectAsync(
        IEnumerable<HarpMessage> messagesToWrite,
        TimeSpan listenDuration,
        CancellationToken cancellationToken = default)
    {
        var collected = new List<HarpMessage>();
        using (messages.Subscribe(message =>
        {
            lock (collected)
            {
                collected.Add(message);
            }
        }))
        {
            foreach (var message in messagesToWrite)
            {
                Write(message);
            }

            await Task.Delay(listenDuration, cancellationToken);
        }

        lock (collected)
        {
            return collected.ToList();
        }
    }

    public async Task<byte> ReadByteAsync(int address, CancellationToken cancellationToken = default)
    {
        var reply = await CommandAsync(HarpCommand.ReadByte(address), cancellationToken);
        return reply.GetPayloadByte();
    }

    public async Task<byte[]> ReadByteArrayAsync(int address, CancellationToken cancellationToken = default)
    {
        var reply = await CommandAsync(HarpCommand.ReadByte(address), cancellationToken);
        return reply.GetPayloadArray<byte>();
    }

    public async Task<ushort> ReadUInt16Async(int address, CancellationToken cancellationToken = default)
    {
        var reply = await CommandAsync(HarpCommand.ReadUInt16(address), cancellationToken);
        return reply.GetPayloadUInt16();
    }

    public async Task<uint> ReadUInt32Async(int address, CancellationToken cancellationToken = default)
    {
        var reply = await CommandAsync(HarpCommand.ReadUInt32(address), cancellationToken);
        return reply.GetPayloadUInt32();
    }

    public async Task<int> ReadWhoAmIAsync(CancellationToken cancellationToken = default)
    {
        return await ReadUInt16Async(Bonsai.Harp.WhoAmI.Address, cancellationToken);
    }

    public async Task<string> ReadDeviceNameAsync(CancellationToken cancellationToken = default)
    {
        var payload = await ReadByteArrayAsync(DeviceName.Address, cancellationToken);
        var terminator = Array.IndexOf(payload, (byte)0);
        return Encoding.ASCII.GetString(payload, 0, terminator < 0 ? payload.Length : terminator);
    }

    public async Task<int> ReadAssemblyVersionAsync(CancellationToken cancellationToken = default)
    {
        return await ReadByteAsync(AssemblyVersion.Address, cancellationToken);
    }

    public async Task<int> ReadSerialNumberAsync(CancellationToken cancellationToken = default)
    {
        return await ReadUInt16Async(SerialNumber.Address, cancellationToken);
    }

    public async Task<HarpVersion> ReadHardwareVersionAsync(CancellationToken cancellationToken = default)
    {
        var major = await ReadByteAsync(HardwareVersionHigh.Address, cancellationToken);
        var minor = await ReadByteAsync(HardwareVersionLow.Address, cancellationToken);
        return new HarpVersion(major, minor);
    }

    public async Task<HarpVersion> ReadFirmwareVersionAsync(CancellationToken cancellationToken = default)
    {
        var major = await ReadByteAsync(FirmwareVersionHigh.Address, cancellationToken);
        var minor = await ReadByteAsync(FirmwareVersionLow.Address, cancellationToken);
        return new HarpVersion(major, minor);
    }

    public async Task<uint> ReadTimestampSecondsAsync(CancellationToken cancellationToken = default)
    {
        return await ReadUInt32Async(TimestampSeconds.Address, cancellationToken);
    }

    public async Task WriteTimestampSecondsAsync(uint seconds, CancellationToken cancellationToken = default)
    {
        await CommandAsync(HarpCommand.WriteUInt32(TimestampSeconds.Address, seconds), cancellationToken);
    }

    /// <summary>
    /// Reads the identity registers reported in the run header, leaving any register the device
    /// does not answer within the read timeout unreported rather than failing the run.
    /// </summary>
    internal async Task<DeviceIdentity> ReadDeviceIdentityAsync(CancellationToken cancellationToken = default)
    {
        var name = await TryReadAsync(ReadDeviceNameAsync, cancellationToken);
        var hardwareVersion = await TryReadAsync(ReadHardwareVersionAsync, cancellationToken);
        var firmwareVersion = await TryReadAsync(ReadFirmwareVersionAsync, cancellationToken);
        return new DeviceIdentity(WhoAmI, name, hardwareVersion, firmwareVersion);
    }

    /// <summary>
    /// Reads the protocol version the device declares, leaving it undeclared when the version
    /// register is unreadable, carries an unexpected length, or reports all zeros.
    /// </summary>
    internal async Task<Suites.SemanticVersion?> ReadProtocolVersionAsync(CancellationToken cancellationToken = default)
    {
        var reply = await TryReadAsync(
            token => CommandAsync(HarpCommand.ReadByte(Suites.Version.Address), token),
            cancellationToken);
        if (reply is null || reply.GetPayloadArray<byte>().Length != Suites.Version.RegisterLength)
            return null;

        var protocolVersion = Suites.Version.GetPayload(reply).ProtocolVersion;
        return protocolVersion.Major == 0 ? null : protocolVersion;
    }

    static async Task<T?> TryReadAsync<T>(
        Func<CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await read(cancellationToken);
        }
        catch (Exception ex) when (ex is not TimeoutException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReadyTimeoutMilliseconds);
            try
            {
                await ReadWhoAmIAsync(timeout.Token);
                return;
            }
            catch (Exception ex) when (
                ex is OperationCanceledException or TimeoutException &&
                attempt < ReadyAttempts &&
                !cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    /// <summary>
    /// Asynchronously returns the device to Standby and closes its port.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
            return;
        disposed = true;

        try
        {
            Write(CreateOperationControl(OperationMode.Standby));
            await stream.FlushAsync();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The device may already be gone; closing the port is all that is left to do.
        }

        connection.MessageReceived -= messages.OnNext;
        await connection.DisposeAsync();
        messages.OnCompleted();
        messages.Dispose();
        await port.CloseAsync();
    }

    /// <summary>
    /// Starts closing the connection. Prefer <see cref="DisposeAsync"/>, which waits for the port to close.
    /// </summary>
    public void Dispose() => _ = DisposeAsync().AsTask();
}
