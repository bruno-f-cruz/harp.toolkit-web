using System.Numerics;
using Bonsai.Harp;

namespace Harp.Toolkit.Web.Devices;

/// <summary>
/// Specifies how a payload field is shown and edited.
/// </summary>
public enum FieldKind
{
    /// <summary>One or more numbers.</summary>
    Number,
    /// <summary>A flag stored in one or more bits.</summary>
    Bool,
    /// <summary>A set of independent bits from a bit mask.</summary>
    Flags,
    /// <summary>One value from a group mask.</summary>
    Choice,
    /// <summary>UTF-8 text.</summary>
    Text
}

/// <summary>
/// Represents a named value of a bit mask or group mask.
/// </summary>
public sealed record FieldOption(string Name, ulong Value, string Description);

/// <summary>
/// Describes one field of a register payload: where it is stored and how to show it.
/// </summary>
/// <param name="Name">The name of the field, or <c>Value</c> for a register with a single value.</param>
/// <param name="Description">The description of the field.</param>
/// <param name="Kind">How the field is shown and edited.</param>
/// <param name="ElementType">The type of each element the field reads, without the timestamp flag.</param>
/// <param name="ByteOffset">The offset, in bytes, of the first element of the field in the payload.</param>
/// <param name="Count">The number of elements the field spans.</param>
/// <param name="Mask">The bits of the element the field uses, or <see langword="null"/> for the whole element.</param>
/// <param name="Options">The named values of a <see cref="FieldKind.Flags"/> or <see cref="FieldKind.Choice"/> field.</param>
/// <param name="MinValue">The smallest valid value, if declared.</param>
/// <param name="MaxValue">The largest valid value, if declared.</param>
/// <param name="DefaultValue">The default value, if declared.</param>
/// <param name="IsRaw">
/// <see langword="true"/> if the metadata declares a custom conversion the app cannot apply, so the
/// field shows the raw payload elements.
/// </param>
public sealed record FieldSchema(
    string Name,
    string Description,
    FieldKind Kind,
    PayloadType ElementType,
    int ByteOffset,
    int Count,
    ulong? Mask,
    IReadOnlyList<FieldOption> Options,
    double? MinValue,
    double? MaxValue,
    double? DefaultValue,
    bool IsRaw)
{
    /// <summary>
    /// Gets the number of bits the masked value is shifted by within its element.
    /// </summary>
    public int Shift => Mask is { } mask ? BitOperations.TrailingZeroCount(mask) : 0;
}

/// <summary>
/// Describes a register and the fields of its payload.
/// </summary>
public sealed record RegisterSchema(
    int Address,
    string Name,
    string Description,
    PayloadType Type,
    int Length,
    RegisterAccess Access,
    bool IsCore,
    IReadOnlyList<FieldSchema> Fields)
{
    /// <summary>
    /// Gets the number of elements in the payload.
    /// </summary>
    public int ElementCount => Math.Max(1, Length);

    /// <summary>
    /// Gets a value indicating whether the register accepts writes.
    /// </summary>
    public bool IsWritable => Access.HasFlag(RegisterAccess.Write);

    /// <summary>
    /// Gets a value indicating whether the device sends events from the register.
    /// </summary>
    public bool HasEvents => Access.HasFlag(RegisterAccess.Event);
}

/// <summary>
/// Describes the registers of a device, built at run time from its metadata, the same way
/// Harp.Generators lays out the payload of each register in the code it generates.
/// </summary>
public sealed class DeviceSchema
{
    const string CoreResourceName = "Harp.Generators.core.yml";

    static readonly Lazy<DeviceMetadata> coreMetadata = new(ReadCoreMetadata);
    static readonly Lazy<DeviceSchema> core = new(() => new DeviceSchema(null, "Core registers"));

    readonly DeviceMetadata? device;

    DeviceSchema(DeviceMetadata? device, string source)
    {
        this.device = device;
        Source = source;
        var registers = new SortedDictionary<int, RegisterSchema>();
        foreach (var (name, info) in coreMetadata.Value.Registers)
            registers[info.Address] = CreateRegister(name, info, isCore: true);
        if (device is not null)
        {
            foreach (var (name, info) in device.Registers)
                registers[info.Address] = CreateRegister(name, info, isCore: false);
        }
        Registers = registers;
    }

    /// <summary>
    /// Gets the schema of the core registers that every Harp device implements.
    /// </summary>
    public static DeviceSchema Core => core.Value;

    /// <summary>
    /// Gets the name of the device, or <see langword="null"/> for the core registers alone.
    /// </summary>
    public string? DeviceName => device?.Device;

    /// <summary>
    /// Gets the WhoAmI the metadata declares, or <see langword="null"/> for the core registers alone.
    /// </summary>
    public int? WhoAmI => device?.WhoAmI;

    /// <summary>
    /// Gets where the metadata came from, for display.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the registers of the device, by address.
    /// </summary>
    public IReadOnlyDictionary<int, RegisterSchema> Registers { get; }

    /// <summary>
    /// Creates the schema of a device from the text of its device.yml.
    /// </summary>
    /// <param name="yaml">The contents of the device metadata file.</param>
    /// <param name="source">Where the metadata came from, for display.</param>
    /// <returns>The schema of the core registers and the device registers.</returns>
    /// <remarks>
    /// Unlike code generation, this does not validate the metadata. Harp.Generators 0.9.0 treats a
    /// missing default value as 0, so it rejects any register that declares a minimum above zero
    /// without a default, which several registers of the published Behavior device do.
    /// </remarks>
    /// <exception cref="YamlDotNet.Core.YamlException">The text is not a device metadata document.</exception>
    public static DeviceSchema Parse(string yaml, string source)
    {
        using var reader = new StringReader(yaml);
        var parser = new YamlDotNet.Core.MergingParser(new YamlDotNet.Core.Parser(reader));
        var metadata = MetadataDeserializer.Instance.Deserialize<DeviceMetadata?>(parser)
            ?? throw new YamlDotNet.Core.YamlException("The device metadata document is empty.");
        return new DeviceSchema(metadata, source);
    }

    RegisterSchema CreateRegister(string name, RegisterInfo info, bool isCore)
    {
        var type = info.Type & ~PayloadType.Timestamp;
        var elementSize = GetSize(type);
        var fields = new List<FieldSchema>();
        if (info.PayloadSpec is { Count: > 0 } spec)
        {
            foreach (var (memberName, member) in spec)
            {
                var offset = member.Offset.GetValueOrDefault() * elementSize;
                var isBool = member.InterfaceType == "bool" && !member.HasConverter;
                if (member.HasConverter)
                {
                    fields.Add(Raw(memberName, member.Description, type, offset, Math.Max(1, member.Length), member.Mask));
                }
                else if (member.Length > 0 && !isBool)
                {
                    fields.Add(CreateSpanField(memberName, member.Description, member, type, offset, member.Length));
                }
                else
                {
                    fields.Add(CreateValueField(memberName, member.Description, member, type, offset, member.Mask));
                }
            }
        }
        else if (info.HasConverter)
        {
            fields.Add(Raw("Value", "", type, 0, Math.Max(1, info.Length), null));
        }
        else if (info.Length > 0)
        {
            fields.Add(CreateSpanField("Value", "", info, type, 0, info.Length));
        }
        else
        {
            fields.Add(CreateValueField("Value", "", info, type, 0, null));
        }

        return new RegisterSchema(info.Address, name, info.Description, type, info.Length, info.Access, isCore, fields);
    }

    FieldSchema CreateSpanField(string name, string description, ValueInfo info, PayloadType type, int byteOffset, int length)
    {
        if (info.InterfaceType == "string")
            return new(name, description, FieldKind.Text, type, byteOffset, length, null, [], null, null, null, false);

        // A primitive interface type reinterprets the bytes of several elements, such as a float over four U8 elements.
        if (TryGetPrimitiveType(info.InterfaceType, out var primitiveType) && primitiveType != type)
            return Number(name, description, info, primitiveType, byteOffset, 1, null);

        return Number(name, description, info, type, byteOffset, length, null);
    }

    FieldSchema CreateValueField(string name, string description, ValueInfo info, PayloadType type, int byteOffset, ulong? mask)
    {
        if (info.InterfaceType == "bool")
            return new(name, description, FieldKind.Bool, type, byteOffset, 1, mask, [], null, null, info.DefaultValue, false);

        if (!string.IsNullOrEmpty(info.MaskType))
        {
            if (FindBitMask(info.MaskType) is { } bitMask)
                return new(name, description, FieldKind.Flags, type, byteOffset, 1, mask, ToOptions(bitMask.Bits, skipZero: true), null, null, info.DefaultValue, false);
            if (FindGroupMask(info.MaskType) is { } groupMask)
                return new(name, description, FieldKind.Choice, type, byteOffset, 1, mask, ToOptions(groupMask.Values, skipZero: false), null, null, info.DefaultValue, false);
        }

        return Number(name, description, info, type, byteOffset, 1, mask);
    }

    static FieldSchema Number(string name, string description, ValueInfo info, PayloadType type, int byteOffset, int count, ulong? mask) =>
        new(name, description, FieldKind.Number, type, byteOffset, count, mask, [], info.MinValue, info.MaxValue, info.DefaultValue, false);

    static FieldSchema Raw(string name, string description, PayloadType type, int byteOffset, int count, ulong? mask) =>
        new(name, description, FieldKind.Number, type, byteOffset, count, mask, [], null, null, null, true);

    static List<FieldOption> ToOptions(Dictionary<string, MaskValue> values, bool skipZero) =>
        values
            .Where(entry => !skipZero || entry.Value.Value != 0)
            .Select(entry => new FieldOption(entry.Key, entry.Value.Value, entry.Value.Description))
            .ToList();

    BitMaskInfo? FindBitMask(string name)
    {
        if (device is not null && device.BitMasks.TryGetValue(name, out var mask)) return mask;
        if (device is not null && device.GroupMasks.ContainsKey(name)) return null;
        return coreMetadata.Value.BitMasks.GetValueOrDefault(name);
    }

    GroupMaskInfo? FindGroupMask(string name)
    {
        if (device is not null && device.GroupMasks.TryGetValue(name, out var mask)) return mask;
        if (device is not null && device.BitMasks.ContainsKey(name)) return null;
        return coreMetadata.Value.GroupMasks.GetValueOrDefault(name);
    }

    internal static int GetSize(PayloadType type) => (int)type & 0xF;

    static bool TryGetPrimitiveType(string interfaceType, out PayloadType type)
    {
        type = interfaceType switch
        {
            "byte" => PayloadType.U8,
            "sbyte" => PayloadType.S8,
            "ushort" => PayloadType.U16,
            "short" => PayloadType.S16,
            "uint" => PayloadType.U32,
            "int" => PayloadType.S32,
            "ulong" => PayloadType.U64,
            "long" => PayloadType.S64,
            "float" => PayloadType.Float,
            _ => 0
        };
        return type != 0;
    }

    static DeviceMetadata ReadCoreMetadata()
    {
        using var stream = typeof(InterfaceGenerator).Assembly.GetManifestResourceStream(CoreResourceName)
            ?? throw new InvalidOperationException($"Harp.Generators does not embed '{CoreResourceName}'.");
        using var reader = new StreamReader(stream);
        return MetadataDeserializer.Instance.Deserialize<DeviceMetadata>(reader);
    }
}
