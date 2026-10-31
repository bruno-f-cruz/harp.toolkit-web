using Harp.Toolkit.Web.Generation;

namespace Harp.Toolkit.Web.Tests;

[TestClass]
public sealed class DeviceCodeGeneratorTests
{
    static string ReadMetadata(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Metadata", name + ".yml"));

    static string[] FileNames(GenerationResult result, OutputLanguage language) =>
        result.Files.Where(file => file.Language == language).Select(file => file.Name).ToArray();

    [TestMethod]
    public void Generate_DeviceMetadata_EmitsAllFiles()
    {
        var result = DeviceCodeGenerator.Generate(ReadMetadata("hobgoblin"), null, new GenerationOptions());

        Assert.IsFalse(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        CollectionAssert.AreEqual(
            new[] { "Device.Generated.cs", "AsyncDevice.Generated.cs" },
            FileNames(result, OutputLanguage.CSharp));
        CollectionAssert.AreEqual(new[] { "device.py" }, FileNames(result, OutputLanguage.Python));
        CollectionAssert.AreEqual(
            new[] { "app.h", "app_funcs.h", "app_ios_and_regs.h", "app.c", "app_funcs.c", "app_ios_and_regs.c", "interrupts.c" },
            FileNames(result, OutputLanguage.Firmware));
        Assert.IsTrue(result.Files.All(file => file.Content.Length > 0));
    }

    [TestMethod]
    public void Generate_PortPinMetadata_IncludesPinsInFirmware()
    {
        var result = DeviceCodeGenerator.Generate(
            ReadMetadata("firmware-device"), ReadMetadata("firmware-ios"), new GenerationOptions());

        Assert.IsFalse(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var header = result.Files.Single(file => file.Name == "app_ios_and_regs.h").Content;
        StringAssert.Contains(header, "DO3");
    }

    [TestMethod]
    public void Generate_InvalidPortPinMetadata_ReportsIOErrorAndKeepsInterfaces()
    {
        const string ios = "DO0:\n  direction: output\n  port: PORTA\n  bogus: 1\n";
        var result = DeviceCodeGenerator.Generate(ReadMetadata("hobgoblin"), ios, new GenerationOptions());

        var diagnostic = result.Diagnostics.Single();
        Assert.AreEqual(MetadataSource.IO, diagnostic.Source);
        Assert.AreEqual(4L, diagnostic.Line);
        Assert.AreEqual("Unknown property 'bogus'.", diagnostic.Message);
        Assert.IsNotEmpty(FileNames(result, OutputLanguage.CSharp));
        Assert.IsEmpty(FileNames(result, OutputLanguage.Firmware));
    }

    [TestMethod]
    public void Generate_NoNamespace_UsesDeviceNamespace()
    {
        var result = DeviceCodeGenerator.Generate(ReadMetadata("hobgoblin"), null, new GenerationOptions());

        Assert.AreEqual("Harp.Hobgoblin", result.Namespace);
        StringAssert.Contains(result.Files[0].Content, "namespace Harp.Hobgoblin");
    }

    [TestMethod]
    public void Generate_CustomNamespace_UsesCustomNamespace()
    {
        var options = new GenerationOptions(Namespace: " My.Devices ");
        var result = DeviceCodeGenerator.Generate(ReadMetadata("hobgoblin"), null, options);

        Assert.AreEqual("My.Devices", result.Namespace);
        StringAssert.Contains(result.Files[0].Content, "namespace My.Devices");
    }

    [TestMethod]
    public void Generate_PythonPackage_EmitsPackageInitializer()
    {
        var options = new GenerationOptions(PythonPackage: true);
        var result = DeviceCodeGenerator.Generate(ReadMetadata("hobgoblin"), null, options);

        CollectionAssert.AreEqual(new[] { "__init__.py", "py.typed" }, FileNames(result, OutputLanguage.Python));
    }

    [TestMethod]
    public void Generate_PortPinWithoutDirection_ReportsMissingProperty()
    {
        const string ios = "DO0:\n  port: PORTA\n";
        var result = DeviceCodeGenerator.Generate(ReadMetadata("hobgoblin"), ios, new GenerationOptions());

        Assert.AreEqual("Missing required property 'direction'.", result.Diagnostics.Single().Message);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   \n")]
    public void Generate_EmptyText_ReturnsEmptyResult(string yaml)
    {
        var result = DeviceCodeGenerator.Generate(yaml, null, new GenerationOptions());

        Assert.AreSame(GenerationResult.Empty, result);
    }

    [TestMethod]
    public void Generate_InvalidYaml_ReportsErrorLocation()
    {
        const string yaml = "device: Test\nwhoAmI: 1\nregisters:\n  Foo:\n    address: [\n";
        var result = DeviceCodeGenerator.Generate(yaml, null, new GenerationOptions());

        Assert.IsTrue(result.HasErrors);
        Assert.IsEmpty(result.Files);
        Assert.IsNotNull(result.Diagnostics[0].Line);
        Assert.DoesNotStartWith("(Line:", result.Diagnostics[0].Message);
    }

    [TestMethod]
    public void Generate_UnknownProperty_ReportsErrorLocation()
    {
        const string yaml = "device: Test\nwhoAmI: 1\nbogus: 2\n";
        var result = DeviceCodeGenerator.Generate(yaml, null, new GenerationOptions());

        Assert.IsTrue(result.HasErrors);
        Assert.AreEqual(3L, result.Diagnostics[0].Line);
        Assert.AreEqual("Unknown property 'bogus'.", result.Diagnostics[0].Message);
    }

    [TestMethod]
    [DataRow("PulseDOPort0", 4L, 3L)]
    [DataRow("DigitalOutputs.DO4", 10L, 7L)]
    [DataRow("DigitalOutputs.Missing", null, null)]
    [DataRow("Missing", null, null)]
    public void FindDeclaration_DottedName_ReturnsNestedKeyPosition(string name, long? line, long? column)
    {
        const string yaml = """
            device: Test
            registers:
              DO4: &shadow
              PulseDOPort0:
                address: 46
            bitMasks:
              DigitalOutputs:
                bits:
                  DO3: 0x8
                  DO4: 0x16
            """;

        var position = DeviceCodeGenerator.FindDeclaration(yaml, name);

        Assert.AreEqual(line, position?.Line);
        Assert.AreEqual(column, position?.Column);
    }
}
