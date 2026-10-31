using System.Buffers.Binary;
using System.Globalization;
using Bonsai.Harp;

namespace Harp.Toolkit.Web.Devices;

/// <summary>
/// Specifies a field of a Harp message frame.
/// </summary>
public enum FrameField
{
    /// <summary>The message type, and whether the message reports an error.</summary>
    MessageType,
    /// <summary>The number of bytes that follow the length byte.</summary>
    Length,
    /// <summary>The register address.</summary>
    Address,
    /// <summary>The port, 255 for the device itself.</summary>
    Port,
    /// <summary>The payload type, and whether the message carries a timestamp.</summary>
    PayloadType,
    /// <summary>The device time at which the message was sent.</summary>
    Timestamp,
    /// <summary>The register value.</summary>
    Payload,
    /// <summary>The sum of every other byte, modulo 256.</summary>
    Checksum
}

/// <summary>
/// Represents one field of a Harp message frame: the bytes it occupies and what they mean.
/// </summary>
/// <param name="Field">The field.</param>
/// <param name="Label">The name of the field.</param>
/// <param name="Bytes">The bytes of the field.</param>
/// <param name="Value">What the bytes mean.</param>
/// <param name="IsProblem"><see langword="true"/> if the field reports an error or does not check out.</param>
public sealed record FrameSegment(FrameField Field, string Label, byte[] Bytes, string Value, bool IsProblem = false);

/// <summary>
/// Splits Harp messages into the fields of their binary frame.
/// </summary>
public static class HarpFrame
{
    const byte ErrorFlag = 0x08;
    const int HeaderSize = 5;
    const int TimestampSize = 6;
    const int DevicePort = 255;

    /// <summary>
    /// Returns the fields of a message frame, in order.
    /// </summary>
    /// <param name="message">The message to describe.</param>
    /// <param name="register">The schema of the register, used to decode the payload, if known.</param>
    /// <returns>The fields of the frame, from the first byte to the checksum.</returns>
    public static IReadOnlyList<FrameSegment> Describe(HarpMessage message, RegisterSchema? register = null)
    {
        var bytes = message.MessageBytes;
        var segments = new List<FrameSegment>();
        if (bytes.Length < HeaderSize + 1)
        {
            segments.Add(new(FrameField.Payload, "Bytes", bytes, "Too short to be a Harp message", true));
            return segments;
        }

        var isError = (bytes[0] & ErrorFlag) != 0;
        segments.Add(new(FrameField.MessageType, "Type", bytes[..1],
            message.MessageType + (isError ? " · error" : ""), isError));

        var declaredLength = bytes[1];
        segments.Add(new(FrameField.Length, "Length", bytes[1..2],
            $"{declaredLength} bytes follow", declaredLength != bytes.Length - 2));

        var address = bytes[2];
        var name = register?.Name;
        segments.Add(new(FrameField.Address, "Register", bytes[2..3],
            name is null ? $"Address {address}" : $"{address} · {name}"));

        var port = bytes[3];
        segments.Add(new(FrameField.Port, "Port", bytes[3..4],
            port == DevicePort ? "255 · this device" : $"Port {port}"));

        var payloadType = (PayloadType)bytes[4];
        var timestamped = (payloadType & PayloadType.Timestamp) != 0;
        var baseType = payloadType & ~PayloadType.Timestamp;
        segments.Add(new(FrameField.PayloadType, "Payload type", bytes[4..5],
            timestamped ? $"{baseType} · timestamped" : baseType.ToString()));

        var payloadStart = HeaderSize;
        if (timestamped && bytes.Length >= HeaderSize + TimestampSize + 1)
        {
            var timestampBytes = bytes[HeaderSize..(HeaderSize + TimestampSize)];
            var seconds = BinaryPrimitives.ReadUInt32LittleEndian(timestampBytes);
            var ticks = BinaryPrimitives.ReadUInt16LittleEndian(timestampBytes.AsSpan(4));
            // The second part counts 32-microsecond ticks.
            var time = seconds + ticks * 32e-6;
            segments.Add(new(FrameField.Timestamp, "Timestamp", timestampBytes,
                time.ToString("0.000000", CultureInfo.InvariantCulture) + " s"));
            payloadStart += TimestampSize;
        }

        var payloadBytes = bytes[payloadStart..^1];
        segments.Add(new(FrameField.Payload, "Payload", payloadBytes, DescribePayload(message, register, payloadBytes.Length)));

        var checksum = bytes[^1];
        byte sum = 0;
        for (int i = 0; i < bytes.Length - 1; i++)
            sum += bytes[i];
        segments.Add(new(FrameField.Checksum, "Checksum", bytes[^1..],
            sum == checksum ? "Valid" : $"Expected 0x{sum:X2}", sum != checksum));

        return segments;
    }

    static string DescribePayload(HarpMessage message, RegisterSchema? register, int length)
    {
        if (length == 0)
            return "Empty";
        try
        {
            return register is not null ? PayloadCodec.Summarize(register, message) : PayloadFormat.Format(message);
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
    }
}
