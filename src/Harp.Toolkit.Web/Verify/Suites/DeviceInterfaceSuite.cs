using Bonsai.Harp;
using Harp.Toolkit.Web.Devices;

namespace Harp.Toolkit.Verify.Suites;

/// <summary>
/// Validates a live device against the registers its device.yml declares: for every register,
/// it reads the live reply and checks its payload type and length against the metadata, then
/// decodes every payload field the way the generated interface lays them out. It also
/// cross-checks the WhoAmI/firmware/hardware version reported by the device against device.yml.
/// </summary>
/// <remarks>
/// Harp.Toolkit compiles the generated C# interface and parses each reply with it, which also
/// tests the generated code. Compiling C# in the browser would need a very large download, so
/// this suite decodes replies with <see cref="PayloadCodec"/>, which follows the same layout
/// rules from the metadata, instead.
/// </remarks>
internal class DeviceInterfaceSuite : Suite
{
    private readonly DeviceSchema? schema;
    private readonly IReadOnlyList<DynamicTest> dynamicTests;

    public DeviceInterfaceSuite(DeviceSchema? schema)
    {
        this.schema = schema?.DeviceName is null ? null : schema;
        dynamicTests = this.schema is null
            ? new List<DynamicTest>()
            : BuildRegisterTests(this.schema);
    }

    protected override IReadOnlyList<DynamicTest> DynamicTests => dynamicTests;

    public override string Description =>
        "Validates live register replies against the payload layout declared in device.yml, and cross-checks WhoAmI/firmware/hardware versions.";

    [HarpTest(Description = "Builds the register layout from device.yml.")]
    public Task<IResult> LoadRegisterLayout(VerifyConnection device)
    {
        IResult result = schema is null
            ? new Result<bool>(false, Status.Skipped, "No device metadata loaded. Load a device.yml on the Device page.")
            : new AssertionResult(true, $"Built the layout of {schema.Registers.Count} registers from {schema.Source}.");
        return Task.FromResult(result);
    }

    [HarpTest(Description = "Compares the WhoAmI/firmware/hardware version reported by the device against device.yml.")]
    public async Task<IResult> DeviceIdentity(VerifyConnection device)
    {
        if (schema is null)
            return new Result<bool>(false, Status.Skipped, "No device metadata loaded. Load a device.yml on the Device page.");

        var mismatches = new List<string>();

        int whoAmI = await device.ReadWhoAmIAsync();
        if (whoAmI != schema.WhoAmI)
            mismatches.Add($"WhoAmI: device={whoAmI}, device.yml={schema.WhoAmI}");

        // HarpVersion.Satisfies treats a null Major/Minor on the argument as a wildcard,
        // so a device.yml that only pins a major version (minor left unset) is honored.
        var firmware = await device.ReadFirmwareVersionAsync();
        if (schema.FirmwareVersion is not null && !firmware.Satisfies(schema.FirmwareVersion))
            mismatches.Add($"Firmware version: device={firmware}, device.yml={schema.FirmwareVersion}");

        var hardware = await device.ReadHardwareVersionAsync();
        if (schema.HardwareTargets is not null && !hardware.Satisfies(schema.HardwareTargets))
            mismatches.Add($"Hardware version: device={hardware}, device.yml={schema.HardwareTargets}");

        return new AssertionResult(
            mismatches.Count == 0,
            _ => mismatches.Count == 0
                ? $"WhoAmI={whoAmI}, Firmware={firmware}, Hardware={hardware} match device.yml."
                : string.Join("; ", mismatches));
    }

    private static IReadOnlyList<DynamicTest> BuildRegisterTests(DeviceSchema schema)
    {
        return schema.Registers.Values
            .OrderBy(register => register.Address)
            .Select(register => new DynamicTest(
                register.Name,
                $"Reads register '{register.Name}' (address {register.Address}) and decodes the reply with the layout declared in device.yml.",
                (device, cancellationToken) => CheckRegisterAsync(register, device, cancellationToken)))
            .ToList();
    }

    private static async Task<IResult> CheckRegisterAsync(RegisterSchema register, VerifyConnection device, CancellationToken cancellationToken)
    {
        HarpMessage reply;
        try
        {
            reply = await device.CommandAsync(HarpCommand.Read(register.Address, register.Type), cancellationToken);
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            return new ErrorResult(ex);
        }

        var replyType = reply.PayloadType & ~PayloadType.Timestamp;
        if (replyType != register.Type)
            return new AssertionResult(false, $"Register '{register.Name}' replied with payload type {replyType}, but device.yml declares {register.Type}.");

        var expectedLength = register.ElementCount * ((int)register.Type & 0xF);
        var length = reply.GetPayload().Count;
        if (length != expectedLength)
            return new AssertionResult(false, $"Register '{register.Name}' replied with {length} payload bytes, but device.yml declares {expectedLength}.");

        try
        {
            var value = PayloadCodec.Summarize(register, reply);
            return new AssertionResult(true, $"Register '{register.Name}' decoded successfully: {value}.");
        }
        catch (FormatException ex)
        {
            return new ErrorResult(ex);
        }
    }
}
