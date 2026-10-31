using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Bonsai.Harp;

namespace Harp.Toolkit.Web.Devices;

/// <summary>
/// Holds the editable value of one payload field.
/// </summary>
public sealed class FieldValue
{
    /// <summary>
    /// Gets or sets the elements of a <see cref="FieldKind.Number"/> field, as text.
    /// </summary>
    public string[] Numbers { get; set; } = [];

    /// <summary>
    /// Gets or sets the value of a <see cref="FieldKind.Bool"/> field.
    /// </summary>
    public bool Bool { get; set; }

    /// <summary>
    /// Gets or sets the bits of a <see cref="FieldKind.Flags"/> or <see cref="FieldKind.Choice"/> field.
    /// </summary>
    public ulong Bits { get; set; }

    /// <summary>
    /// Gets or sets the value of a <see cref="FieldKind.Text"/> field.
    /// </summary>
    public string Text { get; set; } = "";
}

/// <summary>
/// Decodes and encodes register payloads using a <see cref="RegisterSchema"/>.
/// </summary>
public static class PayloadCodec
{
    /// <summary>
    /// Returns the value of each field of a register, read from a message.
    /// </summary>
    /// <param name="register">The schema of the register.</param>
    /// <param name="message">A message from the register.</param>
    /// <returns>The value of each field, by field name.</returns>
    /// <exception cref="FormatException">The payload is shorter than the register declares.</exception>
    public static Dictionary<string, FieldValue> Decode(RegisterSchema register, HarpMessage message)
    {
        var payload = message.GetPayload().AsSpan();
        var values = new Dictionary<string, FieldValue>();
        foreach (var field in register.Fields)
        {
            var size = DeviceSchema.GetSize(field.ElementType);
            if (field.ByteOffset + field.Count * size > payload.Length)
                throw new FormatException($"The payload of {register.Name} is {payload.Length} bytes, shorter than its metadata declares.");

            var value = new FieldValue();
            switch (field.Kind)
            {
                case FieldKind.Text:
                    var bytes = payload.Slice(field.ByteOffset, field.Count);
                    var end = bytes.IndexOf((byte)0);
                    value.Text = Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
                    break;
                case FieldKind.Number:
                    value.Numbers = new string[field.Count];
                    for (int i = 0; i < field.Count; i++)
                    {
                        var bits = ReadElement(payload, field.ElementType, field.ByteOffset + i * size);
                        value.Numbers[i] = field.Mask is { } mask
                            ? ((bits & mask) >> field.Shift).ToString(CultureInfo.InvariantCulture)
                            : FormatElement(field.ElementType, bits);
                    }
                    break;
                default:
                    var element = ReadElement(payload, field.ElementType, field.ByteOffset);
                    var masked = field.Mask is { } fieldMask ? (element & fieldMask) >> field.Shift : element;
                    value.Bits = masked;
                    value.Bool = masked != 0;
                    break;
            }
            values[field.Name] = value;
        }
        return values;
    }

    /// <summary>
    /// Returns the default value of each field of a register, from the defaults its metadata declares.
    /// </summary>
    /// <param name="register">The schema of the register.</param>
    /// <returns>The value of each field, by field name.</returns>
    public static Dictionary<string, FieldValue> CreateDefault(RegisterSchema register)
    {
        var values = new Dictionary<string, FieldValue>();
        foreach (var field in register.Fields)
        {
            var initial = field.DefaultValue ?? Math.Max(0, field.MinValue ?? 0);
            values[field.Name] = new FieldValue
            {
                Numbers = Enumerable.Repeat(initial.ToString(CultureInfo.InvariantCulture), field.Count).ToArray(),
                Bool = initial != 0,
                Bits = (ulong)Math.Max(0, initial)
            };
        }
        return values;
    }

    /// <summary>
    /// Creates a write command for a register from the value of each field.
    /// </summary>
    /// <param name="register">The schema of the register.</param>
    /// <param name="values">The value of each field, by field name.</param>
    /// <returns>The write command.</returns>
    /// <exception cref="FormatException">A value is not valid for its field.</exception>
    public static HarpMessage Encode(RegisterSchema register, IReadOnlyDictionary<string, FieldValue> values)
    {
        var size = DeviceSchema.GetSize(register.Type);
        var payload = new byte[register.ElementCount * size];
        foreach (var field in register.Fields)
        {
            if (!values.TryGetValue(field.Name, out var value))
                continue;

            var fieldSize = DeviceSchema.GetSize(field.ElementType);
            switch (field.Kind)
            {
                case FieldKind.Text:
                    var bytes = Encoding.UTF8.GetBytes(value.Text);
                    if (bytes.Length > field.Count)
                        throw new FormatException($"{field.Name} holds at most {field.Count} bytes of text.");
                    bytes.CopyTo(payload, field.ByteOffset);
                    break;
                case FieldKind.Number:
                    for (int i = 0; i < field.Count; i++)
                    {
                        var text = i < value.Numbers.Length ? value.Numbers[i] : "0";
                        var offset = field.ByteOffset + i * fieldSize;
                        if (field.Mask is { } mask)
                            Merge(payload, field.ElementType, offset, ParseUnsigned(field, text) << field.Shift, mask);
                        else
                            WriteElement(payload, field.ElementType, offset, ParseElement(field, text));
                    }
                    break;
                case FieldKind.Bool:
                    var on = value.Bool ? 1UL : 0UL;
                    // A bool on a multi-bit mask sets every bit of the mask, as the generated code does.
                    var boolBits = field.Mask is { } boolMask ? (value.Bool ? boolMask : 0) : on;
                    Merge(payload, field.ElementType, field.ByteOffset, boolBits, field.Mask ?? ElementMask(field.ElementType));
                    break;
                default:
                    Merge(payload, field.ElementType, field.ByteOffset, value.Bits << field.Shift, field.Mask ?? ElementMask(field.ElementType));
                    break;
            }
        }

        return HarpCommand.Write(register.Address, register.Type, payload);
    }

    /// <summary>
    /// Returns a one-line summary of the fields of a register message, such as
    /// <c>OperationMode=Active, Heartbeat=Enabled</c>.
    /// </summary>
    /// <param name="register">The schema of the register.</param>
    /// <param name="message">A message from the register.</param>
    /// <returns>The summary of the message payload.</returns>
    public static string Summarize(RegisterSchema register, HarpMessage message)
    {
        var values = Decode(register, message);
        var parts = register.Fields.Select(field => (field, text: FormatValue(field, values[field.Name])));
        if (register.Fields.Count == 1)
            return parts.First().text;
        return string.Join(", ", parts.Select(part => $"{part.field.Name}={part.text}"));
    }

    /// <summary>
    /// Returns the value of a field as text, naming mask values.
    /// </summary>
    /// <param name="field">The schema of the field.</param>
    /// <param name="value">The value of the field.</param>
    /// <returns>The value as text.</returns>
    public static string FormatValue(FieldSchema field, FieldValue value)
    {
        switch (field.Kind)
        {
            case FieldKind.Text:
                return $"\"{value.Text}\"";
            case FieldKind.Bool:
                return value.Bool ? "true" : "false";
            case FieldKind.Choice:
                return field.Options.FirstOrDefault(option => option.Value == value.Bits)?.Name ?? $"0x{value.Bits:X}";
            case FieldKind.Flags:
                if (value.Bits == 0)
                    return "None";
                var names = field.Options.Where(option => option.Value != 0 && (value.Bits & option.Value) == option.Value).Select(option => option.Name).ToList();
                var known = field.Options.Aggregate(0UL, (mask, option) => mask | option.Value);
                if ((value.Bits & ~known) != 0)
                    names.Add($"0x{value.Bits & ~known:X}");
                return string.Join(" | ", names);
            default:
                return value.Numbers.Length == 1 ? value.Numbers[0] : $"[{string.Join(", ", value.Numbers)}]";
        }
    }

    static ulong ReadElement(ReadOnlySpan<byte> payload, PayloadType type, int offset)
    {
        var bytes = payload.Slice(offset, DeviceSchema.GetSize(type));
        return bytes.Length switch
        {
            1 => bytes[0],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            _ => BinaryPrimitives.ReadUInt64LittleEndian(bytes)
        };
    }

    static void WriteElement(byte[] payload, PayloadType type, int offset, ulong bits)
    {
        var bytes = payload.AsSpan(offset, DeviceSchema.GetSize(type));
        switch (bytes.Length)
        {
            case 1: bytes[0] = (byte)bits; break;
            case 2: BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)bits); break;
            case 4: BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)bits); break;
            default: BinaryPrimitives.WriteUInt64LittleEndian(bytes, bits); break;
        }
    }

    static void Merge(byte[] payload, PayloadType type, int offset, ulong bits, ulong mask)
    {
        var current = ReadElement(payload, type, offset);
        WriteElement(payload, type, offset, (current & ~mask) | (bits & mask));
    }

    static ulong ElementMask(PayloadType type)
    {
        var size = DeviceSchema.GetSize(type);
        return size >= 8 ? ulong.MaxValue : (1UL << (size * 8)) - 1;
    }

    static string FormatElement(PayloadType type, ulong bits)
    {
        var invariant = CultureInfo.InvariantCulture;
        return type switch
        {
            PayloadType.S8 => ((sbyte)bits).ToString(invariant),
            PayloadType.S16 => ((short)bits).ToString(invariant),
            PayloadType.S32 => ((int)bits).ToString(invariant),
            PayloadType.S64 => ((long)bits).ToString(invariant),
            PayloadType.Float => BitConverter.UInt32BitsToSingle((uint)bits).ToString("R", invariant),
            _ => bits.ToString(invariant)
        };
    }

    static ulong ParseUnsigned(FieldSchema field, string text)
    {
        if (!TryParseInteger(text, out var value) || value < 0)
            throw new FormatException($"{field.Name} must be a whole number of zero or more.");
        CheckRange(field, (double)value);
        var limit = field.Mask is { } mask ? mask >> field.Shift : ElementMask(field.ElementType);
        if ((ulong)value > limit)
            throw new FormatException($"{field.Name} must be at most {limit}.");
        return (ulong)value;
    }

    static ulong ParseElement(FieldSchema field, string text)
    {
        if (field.ElementType == PayloadType.Float)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                throw new FormatException($"{field.Name} must be a number.");
            CheckRange(field, number);
            return BitConverter.SingleToUInt32Bits(number);
        }

        if (!TryParseInteger(text, out var value))
            throw new FormatException($"{field.Name} must be a whole number.");
        CheckRange(field, (double)value);

        var size = DeviceSchema.GetSize(field.ElementType);
        var signed = ((int)field.ElementType & 0x80) != 0;
        var bitCount = size * 8;
        var min = signed ? -(Int128.One << (bitCount - 1)) : Int128.Zero;
        var max = signed ? (Int128.One << (bitCount - 1)) - 1 : (Int128.One << bitCount) - 1;
        if (value < min || value > max)
            throw new FormatException($"{field.Name} must be between {min} and {max}.");
        return (ulong)(value & ulong.MaxValue);
    }

    static bool TryParseInteger(string text, out Int128 value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = ulong.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex);
            value = hex;
            return parsed;
        }
        return Int128.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    static void CheckRange(FieldSchema field, double value)
    {
        if (value < field.MinValue)
            throw new FormatException($"{field.Name} must be at least {field.MinValue}.");
        if (value > field.MaxValue)
            throw new FormatException($"{field.Name} must be at most {field.MaxValue}.");
    }
}
