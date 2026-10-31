using System.Globalization;
using System.Numerics;
using Bonsai.Harp;

namespace Harp.Toolkit.Web.Devices;

/// <summary>
/// Converts Harp message payloads to and from text.
/// </summary>
public static class PayloadFormat
{
    /// <summary>
    /// Gets the payload types a register can have, in display order.
    /// </summary>
    public static IReadOnlyList<PayloadType> RegisterTypes { get; } =
    [
        PayloadType.U8, PayloadType.S8,
        PayloadType.U16, PayloadType.S16,
        PayloadType.U32, PayloadType.S32,
        PayloadType.U64, PayloadType.S64,
        PayloadType.Float
    ];

    /// <summary>
    /// Returns the payload type of the message, without the timestamp flag.
    /// </summary>
    public static PayloadType GetBaseType(PayloadType type) => type & ~PayloadType.Timestamp;

    /// <summary>
    /// Returns the payload of the message as comma-separated values.
    /// </summary>
    /// <param name="message">The message whose payload to format.</param>
    /// <returns>The values in the payload, separated by commas.</returns>
    public static string Format(HarpMessage message)
    {
        return GetBaseType(message.PayloadType) switch
        {
            PayloadType.U8 => Join(message.GetPayloadArray<byte>()),
            PayloadType.S8 => Join(message.GetPayloadArray<sbyte>()),
            PayloadType.U16 => Join(message.GetPayloadArray<ushort>()),
            PayloadType.S16 => Join(message.GetPayloadArray<short>()),
            PayloadType.U32 => Join(message.GetPayloadArray<uint>()),
            PayloadType.S32 => Join(message.GetPayloadArray<int>()),
            PayloadType.U64 => Join(message.GetPayloadArray<ulong>()),
            PayloadType.S64 => Join(message.GetPayloadArray<long>()),
            PayloadType.Float => Join(message.GetPayloadArray<float>()),
            _ => Convert.ToHexString(message.GetPayload())
        };
    }

    /// <summary>
    /// Returns the payload of the message as text if every byte up to the first zero is printable.
    /// </summary>
    /// <param name="message">The message whose payload to read as text.</param>
    /// <returns>The text in the payload, or <see langword="null"/> if the payload is not text.</returns>
    public static string? FormatText(HarpMessage message)
    {
        if (GetBaseType(message.PayloadType) != PayloadType.U8)
            return null;

        var payload = message.GetPayload().AsSpan();
        var length = payload.IndexOf((byte)0);
        if (length < 0)
            length = payload.Length;
        if (length == 0)
            return null;

        foreach (var value in payload[..length])
        {
            if (value < 0x20 || value > 0x7E)
                return null;
        }
        return System.Text.Encoding.ASCII.GetString(payload[..length]);
    }

    /// <summary>
    /// Creates a write command from comma-separated values.
    /// </summary>
    /// <param name="address">The address of the register to write.</param>
    /// <param name="type">The payload type of the register.</param>
    /// <param name="text">The values to write, separated by commas or spaces.</param>
    /// <returns>The write command.</returns>
    /// <exception cref="FormatException">A value is not a number of the register type.</exception>
    public static HarpMessage CreateWrite(int address, PayloadType type, string text)
    {
        var values = text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0)
            throw new FormatException("Enter at least one value to write.");

        return GetBaseType(type) switch
        {
            PayloadType.U8 => HarpCommand.WriteByte(address, Parse<byte>(values)),
            PayloadType.S8 => HarpCommand.WriteSByte(address, Parse<sbyte>(values)),
            PayloadType.U16 => HarpCommand.WriteUInt16(address, Parse<ushort>(values)),
            PayloadType.S16 => HarpCommand.WriteInt16(address, Parse<short>(values)),
            PayloadType.U32 => HarpCommand.WriteUInt32(address, Parse<uint>(values)),
            PayloadType.S32 => HarpCommand.WriteInt32(address, Parse<int>(values)),
            PayloadType.U64 => HarpCommand.WriteUInt64(address, Parse<ulong>(values)),
            PayloadType.S64 => HarpCommand.WriteInt64(address, Parse<long>(values)),
            PayloadType.Float => HarpCommand.WriteSingle(address, Parse<float>(values)),
            _ => throw new FormatException($"The payload type {type} cannot be written.")
        };
    }

    static string Join<T>(T[] values) where T : IFormattable =>
        string.Join(", ", values.Select(value => value.ToString(null, CultureInfo.InvariantCulture)));

    static T[] Parse<T>(string[] values) where T : INumber<T>
    {
        var result = new T[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var isHex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            var parsed = isHex
                ? T.TryParse(value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var number)
                : T.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out number);
            if (!parsed)
                throw new FormatException($"'{value}' is not a valid {typeof(T).Name} value.");
            result[i] = number!;
        }
        return result;
    }
}
