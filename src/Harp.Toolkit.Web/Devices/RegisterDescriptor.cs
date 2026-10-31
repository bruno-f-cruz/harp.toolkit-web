using Bonsai.Harp;
using Harp.Generators;

namespace Harp.Toolkit.Web.Devices;

/// <summary>
/// Represents a register shown by the app: its address, name and payload type.
/// </summary>
public sealed record RegisterDescriptor(int Address, string Name, PayloadType Type, int Length, string Description, bool Writable)
{
    const string CoreResourceName = "Harp.Generators.core.yml";

    static readonly Lazy<IReadOnlyDictionary<int, RegisterDescriptor>> core = new(() => FromMetadata(ReadCoreMetadata()));

    /// <summary>
    /// Gets the core registers that every Harp device implements, by address, as declared by Harp.Generators.
    /// </summary>
    public static IReadOnlyDictionary<int, RegisterDescriptor> Core => core.Value;

    /// <summary>
    /// Creates the register map of a device from its metadata, including the core registers.
    /// </summary>
    /// <param name="metadata">The device metadata, or <see langword="null"/> for the core registers alone.</param>
    /// <returns>The registers of the device, by address.</returns>
    public static IReadOnlyDictionary<int, RegisterDescriptor> CreateMap(DeviceMetadata? metadata)
    {
        if (metadata is null)
            return Core;

        var map = new Dictionary<int, RegisterDescriptor>(Core);
        foreach (var register in FromMetadata(metadata).Values)
        {
            map[register.Address] = register;
        }
        return map;
    }

    static Dictionary<int, RegisterDescriptor> FromMetadata(DeviceMetadata metadata)
    {
        var map = new Dictionary<int, RegisterDescriptor>();
        foreach (var (name, register) in metadata.Registers)
        {
            map[register.Address] = new(
                register.Address,
                name,
                register.Type,
                Math.Max(register.Length, 1),
                register.Description,
                register.Access.HasFlag(RegisterAccess.Write));
        }
        return map;
    }

    static DeviceMetadata ReadCoreMetadata()
    {
        using var stream = typeof(InterfaceGenerator).Assembly.GetManifestResourceStream(CoreResourceName)
            ?? throw new InvalidOperationException($"Harp.Generators does not embed '{CoreResourceName}'.");
        using var reader = new StreamReader(stream);
        return DeviceMetadata.Parse(reader.ReadToEnd());
    }
}
