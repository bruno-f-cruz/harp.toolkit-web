using System.Net.Http.Json;
using System.Runtime.Versioning;
using Bonsai.Harp;
using Harp.Generators;
using Harp.Toolkit.Web.Generation;
using Harp.WebSerial;

namespace Harp.Toolkit.Web.Devices;

/// <summary>
/// Specifies the state of the connection to the selected device.
/// </summary>
public enum SessionState
{
    /// <summary>No port is selected.</summary>
    NoPort,
    /// <summary>A port is selected but not open.</summary>
    Disconnected,
    /// <summary>The port is opening and the device identity is being read.</summary>
    Connecting,
    /// <summary>The device is connected and answering commands.</summary>
    Connected,
    /// <summary>An operation, such as a firmware update, has exclusive use of the port.</summary>
    Busy
}

/// <summary>
/// Holds the selected serial port and the connection to its device, shared by every page so the
/// device stays connected while moving between them.
/// </summary>
[SupportedOSPlatform("browser")]
public sealed class DeviceSession(HttpClient http)
{
    WebSerialDevicePort? port;
    IReadOnlyList<DeviceEntry>? catalog;

    /// <summary>
    /// Occurs when the state, identity or register map of the session changes.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Occurs when the connected device sends a message.
    /// </summary>
    public event Action<HarpMessage>? MessageReceived;

    /// <summary>
    /// Gets a value indicating whether the browser supports Web Serial.
    /// </summary>
    public bool IsSupported { get; private set; }

    /// <summary>
    /// Gets the state of the session.
    /// </summary>
    public SessionState State { get; private set; } = SessionState.NoPort;

    /// <summary>
    /// Gets the selected port, if any.
    /// </summary>
    public WebSerialPort? Port => port?.Port;

    /// <summary>
    /// Gets the connection to the device, while connected.
    /// </summary>
    public HarpConnection? Connection { get; private set; }

    /// <summary>
    /// Gets the identity of the device, read when it last connected.
    /// </summary>
    public DeviceIdentity? Identity { get; private set; }

    /// <summary>
    /// Gets the catalog entry of the device, if its WhoAmI is registered.
    /// </summary>
    public DeviceEntry? CatalogEntry { get; private set; }

    /// <summary>
    /// Gets the registers of the device, by address. Until the device metadata loads, only the core registers are known.
    /// </summary>
    public IReadOnlyDictionary<int, RegisterDescriptor> Registers { get; private set; } = RegisterDescriptor.Core;

    /// <summary>
    /// Gets the message of the last error, if any.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// Gets a short description of the selected port.
    /// </summary>
    public string PortLabel => Port is { } selected
        ? selected.UsbVendorId is { } vendor ? $"USB {vendor:x4}:{selected.UsbProductId:x4}" : "Serial port"
        : "No port";

    /// <summary>
    /// Asynchronously loads Web Serial and selects the first port the user granted earlier, if any.
    /// </summary>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        await WebSerial.WebSerial.InitializeAsync();
        IsSupported = WebSerial.WebSerial.IsSupported;
        if (!IsSupported)
            return;

        var granted = await WebSerial.WebSerial.GetPortsAsync();
        if (granted.Length > 0)
            SetPort(granted[0]);
    }

    /// <summary>
    /// Asynchronously asks the user to select a port, and connects to it.
    /// </summary>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public async Task SelectPortAsync()
    {
        var selected = await WebSerial.WebSerial.RequestPortAsync();
        if (selected is null)
            return;

        await DisconnectAsync();
        SetPort(selected);
        await ConnectAsync();
    }

    /// <summary>
    /// Asynchronously opens the selected port and reads the device identity.
    /// </summary>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public async Task ConnectAsync()
    {
        if (port is null || State is SessionState.Connecting or SessionState.Connected or SessionState.Busy)
            return;

        SetState(SessionState.Connecting, error: null);
        HarpConnection? connection = null;
        try
        {
            var stream = await port.OpenAsync(PortMode.Protocol);
            connection = new HarpConnection(stream);
            connection.MessageReceived += OnMessageReceived;
            connection.Closed += OnConnectionClosed;
            Identity = await connection.ReadIdentityAsync();
            Connection = connection;
            SetState(SessionState.Connected, error: null);
            await LoadRegistersAsync(Identity.WhoAmI);
        }
        catch (Exception ex)
        {
            if (connection is not null)
            {
                connection.Closed -= OnConnectionClosed;
                await connection.DisposeAsync();
            }
            await port.CloseAsync();
            Connection = null;
            SetState(SessionState.Disconnected, Describe(ex));
        }
    }

    /// <summary>
    /// Asynchronously closes the connection to the device.
    /// </summary>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public async Task DisconnectAsync()
    {
        var connection = Connection;
        Connection = null;
        if (connection is not null)
        {
            connection.Closed -= OnConnectionClosed;
            connection.MessageReceived -= OnMessageReceived;
            await connection.DisposeAsync();
        }

        if (port is not null)
        {
            await port.CloseAsync();
            SetState(SessionState.Disconnected, Error);
        }
    }

    /// <summary>
    /// Asynchronously runs an operation that needs exclusive use of the port, such as a firmware update,
    /// and reconnects to the device afterwards.
    /// </summary>
    /// <param name="operation">The operation, which receives the closed port.</param>
    /// <param name="reconnectDelay">The time to wait for the device to restart before reconnecting.</param>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public async Task RunExclusiveAsync(Func<IDevicePort, Task> operation, TimeSpan reconnectDelay)
    {
        var current = port ?? throw new InvalidOperationException("Select a port first.");
        await DisconnectAsync();
        SetState(SessionState.Busy, error: null);
        try
        {
            await operation(current);
        }
        finally
        {
            await current.CloseAsync();
            SetState(SessionState.Disconnected, Error);
        }

        await Task.Delay(reconnectDelay);
        await ConnectAsync();
    }

    /// <summary>
    /// Uses the register names and types in the specified metadata for the connected device.
    /// </summary>
    /// <param name="metadata">The device metadata, or <see langword="null"/> for the core registers alone.</param>
    public void UseMetadata(DeviceMetadata? metadata)
    {
        Registers = RegisterDescriptor.CreateMap(metadata);
        Changed?.Invoke();
    }

    async Task LoadRegistersAsync(int whoAmI)
    {
        CatalogEntry = null;
        Registers = RegisterDescriptor.Core;
        try
        {
            catalog ??= await http.GetFromJsonAsync<List<DeviceEntry>>("devices.json") ?? [];
            CatalogEntry = catalog.FirstOrDefault(entry => entry.WhoAmI == whoAmI);
            if (CatalogEntry is not null)
            {
                var yaml = await http.GetStringAsync(CatalogEntry.DeviceUrl);
                Registers = RegisterDescriptor.CreateMap(DeviceMetadata.Parse(yaml));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or YamlDotNet.Core.YamlException or NotSupportedException)
        {
            // The device metadata is optional: without it, registers are shown by address.
        }
        Changed?.Invoke();
    }

    void SetPort(WebSerialPort selected)
    {
        port = new WebSerialDevicePort(selected);
        Identity = null;
        CatalogEntry = null;
        Registers = RegisterDescriptor.Core;
        SetState(SessionState.Disconnected, error: null);
    }

    void SetState(SessionState state, string? error)
    {
        State = state;
        Error = error;
        Changed?.Invoke();
    }

    void OnMessageReceived(HarpMessage message) => MessageReceived?.Invoke(message);

    async void OnConnectionClosed(Exception? error)
    {
        // The read loop stopped on its own, for example because the device was unplugged.
        Connection = null;
        if (port is not null)
        {
            try { await port.CloseAsync(); } catch (IOException) { }
        }
        SetState(SessionState.Disconnected, error is null ? "The device closed the connection." : Describe(error));
    }

    static string Describe(Exception ex) => ex switch
    {
        TimeoutException => "The device did not answer. Check that it is powered and that the right port is selected.",
        _ => ex.Message
    };
}
