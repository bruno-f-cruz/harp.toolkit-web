using Bonsai.Harp;

namespace Harp.Toolkit.Web.Devices;

/// <summary>
/// Represents the identity and versions a Harp device reports in its core registers.
/// </summary>
public sealed record DeviceIdentity(
    int WhoAmI,
    string? DeviceName,
    ushort? SerialNumber,
    HarpVersion HardwareVersion,
    HarpVersion FirmwareVersion,
    HarpVersion CoreVersion,
    int AssemblyVersion,
    uint TimestampSeconds)
{
    /// <summary>
    /// Gets the name to show for the device: its reported name, or its WhoAmI when it reports none.
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(DeviceName) ? $"Device {WhoAmI}" : DeviceName;
}

/// <summary>
/// Provides methods to read and write the core registers that every Harp device implements.
/// </summary>
public static class CoreRegisters
{
    /// <summary>
    /// Asynchronously reads the identity and versions of the device.
    /// </summary>
    /// <param name="connection">The connection to the device.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>
    /// The task object representing the asynchronous operation. The <see cref="Task{TResult}.Result"/>
    /// property contains the device identity.
    /// </returns>
    public static async Task<DeviceIdentity> ReadIdentityAsync(this HarpConnection connection, CancellationToken cancellationToken = default)
    {
        var whoAmI = WhoAmI.GetPayload(await connection.CommandAsync(HarpCommand.ReadUInt16(WhoAmI.Address), cancellationToken));
        var hardwareVersion = await ReadVersionAsync(connection, HardwareVersionHigh.Address, HardwareVersionLow.Address, cancellationToken);
        var firmwareVersion = await ReadVersionAsync(connection, FirmwareVersionHigh.Address, FirmwareVersionLow.Address, cancellationToken);
        var coreVersion = await ReadVersionAsync(connection, CoreVersionHigh.Address, CoreVersionLow.Address, cancellationToken);
        var assemblyVersion = await ReadByteAsync(connection, AssemblyVersion.Address, cancellationToken);
        var timestamp = TimestampSeconds.GetPayload(await connection.CommandAsync(HarpCommand.ReadUInt32(TimestampSeconds.Address), cancellationToken));

        // Older firmware does not implement the serial number or the device name, and replies with an error.
        var serialNumberReply = await connection.SendAsync(HarpCommand.ReadUInt16(SerialNumber.Address), cancellationToken);
        var serialNumber = serialNumberReply.Error ? default(ushort?) : SerialNumber.GetPayload(serialNumberReply);
        var deviceNameReply = await connection.SendAsync(HarpCommand.ReadByte(DeviceName.Address), cancellationToken);
        var deviceName = deviceNameReply.Error ? null : DeviceName.GetPayload(deviceNameReply);

        return new DeviceIdentity(
            whoAmI,
            deviceName,
            serialNumber,
            hardwareVersion,
            firmwareVersion,
            coreVersion,
            assemblyVersion,
            timestamp);
    }

    /// <summary>
    /// Asynchronously reads the reset register of the device.
    /// </summary>
    /// <param name="connection">The connection to the device.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>
    /// The task object representing the asynchronous operation. The <see cref="Task{TResult}.Result"/>
    /// property contains the reset flags, which report how the device booted.
    /// </returns>
    public static async Task<ResetFlags> ReadResetDeviceAsync(this HarpConnection connection, CancellationToken cancellationToken = default)
    {
        return ResetDevice.GetPayload(await connection.CommandAsync(HarpCommand.ReadByte(ResetDevice.Address), cancellationToken));
    }

    /// <summary>
    /// Asynchronously writes the reset register of the device.
    /// </summary>
    /// <param name="connection">The connection to the device.</param>
    /// <param name="flags">The reset operation to perform.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The task object representing the asynchronous operation.</returns>
    public static async Task WriteResetDeviceAsync(this HarpConnection connection, ResetFlags flags, CancellationToken cancellationToken = default)
    {
        await connection.CommandAsync(ResetDevice.FromPayload(MessageType.Write, flags), cancellationToken);
    }

    static async Task<byte> ReadByteAsync(HarpConnection connection, int address, CancellationToken cancellationToken)
    {
        return (await connection.CommandAsync(HarpCommand.ReadByte(address), cancellationToken)).GetPayloadByte();
    }

    static async Task<HarpVersion> ReadVersionAsync(HarpConnection connection, int highAddress, int lowAddress, CancellationToken cancellationToken)
    {
        var major = await ReadByteAsync(connection, highAddress, cancellationToken);
        var minor = await ReadByteAsync(connection, lowAddress, cancellationToken);
        return new HarpVersion(major, minor);
    }
}
