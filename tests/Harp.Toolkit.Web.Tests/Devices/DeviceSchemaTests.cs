using Bonsai.Harp;
using Harp.Toolkit.Web.Devices;

namespace Harp.Toolkit.Web.Tests.Devices;

[TestClass]
public sealed class DeviceSchemaTests
{
    const string TestDevice = """
        %YAML 1.1
        ---
        device: TestDevice
        whoAmI: 9999
        firmwareVersion: "1.0"
        hardwareTargets: "1.0"
        registers:
          Levels:
            address: 32
            type: U16
            length: 3
            access: [Read, Write]
          Gain:
            address: 33
            type: S16
            access: Write
            minValue: -100
            maxValue: 100
          Outputs:
            address: 34
            type: U8
            access: [Write, Event]
            maskType: DigitalOutputs
          Label:
            address: 35
            type: U8
            length: 8
            access: Write
            interfaceType: string
          Config:
            address: 36
            type: U8
            length: 6
            access: Write
            payloadSpec:
              Mode:
                offset: 0
                mask: 0x3
                maskType: Mode
              Invert:
                offset: 0
                mask: 0x4
                interfaceType: bool
              Threshold:
                offset: 1
                length: 4
                interfaceType: float
              Count:
                offset: 5
        bitMasks:
          DigitalOutputs:
            bits:
              None:
                value: 0x0
              DO0:
                value: 0x1
              DO1:
                value: 0x2
        groupMasks:
          Mode:
            values:
              Off:
                value: 0x0
              Slow:
                value: 0x1
              Fast:
                value: 0x2
        """;

    static DeviceSchema Schema => DeviceSchema.Parse(TestDevice, "test");

    static HarpMessage Reply(RegisterSchema register, params byte[] payload) =>
        HarpMessage.FromPayload(register.Address, 1.0, MessageType.Read, register.Type, payload);

    [TestMethod]
    public void Core_OperationControl_HasTypedFields()
    {
        var register = DeviceSchema.Core.Registers[OperationControl.Address];

        var mode = register.Fields.Single(field => field.Name == "OperationMode");
        Assert.AreEqual(FieldKind.Choice, mode.Kind);
        Assert.AreEqual(0x3UL, mode.Mask);
        CollectionAssert.AreEqual(new[] { "Standby", "Active", "Speed" }, mode.Options.Select(option => option.Name).ToArray());
        Assert.AreEqual(FieldKind.Bool, register.Fields.Single(field => field.Name == "DumpRegisters").Kind);
        Assert.AreEqual(FieldKind.Flags, DeviceSchema.Core.Registers[ResetDevice.Address].Fields[0].Kind);
        Assert.AreEqual(FieldKind.Text, DeviceSchema.Core.Registers[DeviceName.Address].Fields[0].Kind);
    }

    [TestMethod]
    public void Summarize_OperationControl_NamesEveryField()
    {
        var register = DeviceSchema.Core.Registers[OperationControl.Address];

        var summary = PayloadCodec.Summarize(register, Reply(register, 0x81));

        Assert.AreEqual(
            "OperationMode=Active, DumpRegisters=false, MuteReplies=false, VisualIndicators=Disabled, OperationLed=Disabled, Heartbeat=Enabled",
            summary);
    }

    [TestMethod]
    public void Parse_DefaultOutsideRange_StillLoads()
    {
        // Published device files, such as Behavior's, declare defaults the generator rejects.
        var yaml = TestDevice.Replace("    minValue: -100", "    minValue: 1");

        var schema = DeviceSchema.Parse(yaml, "test");

        Assert.AreEqual(1.0, schema.Registers[33].Fields[0].MinValue);
    }

    [TestMethod]
    public void Parse_DeviceMetadata_AddsDeviceRegistersToCore()
    {
        var schema = Schema;

        Assert.AreEqual("TestDevice", schema.DeviceName);
        Assert.IsTrue(schema.Registers[WhoAmI.Address].IsCore);
        Assert.IsFalse(schema.Registers[32].IsCore);
        Assert.IsTrue(schema.Registers[34].HasEvents);
    }

    [TestMethod]
    public void Decode_PayloadSpec_ReadsMaskedBoolFloatAndOffsetMembers()
    {
        var register = Schema.Registers[36];
        var threshold = BitConverter.GetBytes(2.5f);
        byte[] payload = [0x06, threshold[0], threshold[1], threshold[2], threshold[3], 42];

        var values = PayloadCodec.Decode(register, Reply(register, payload));

        Assert.AreEqual(2UL, values["Mode"].Bits);
        Assert.IsTrue(values["Invert"].Bool);
        Assert.AreEqual("2.5", values["Threshold"].Numbers[0]);
        Assert.AreEqual("42", values["Count"].Numbers[0]);
        Assert.AreEqual("Mode=Fast, Invert=true, Threshold=2.5, Count=42", PayloadCodec.Summarize(register, Reply(register, payload)));
    }

    [TestMethod]
    [DataRow(36, new byte[] { 0x05, 0, 0, 0x20, 0x41, 7 })]
    [DataRow(32, new byte[] { 1, 0, 0xFF, 0xFF, 3, 1 })]
    [DataRow(33, new byte[] { 0x9C, 0xFF })]
    [DataRow(34, new byte[] { 0x03 })]
    [DataRow(35, new byte[] { (byte)'h', (byte)'a', (byte)'r', (byte)'p', 0, 0, 0, 0 })]
    public void Encode_DecodedValues_RoundTripsPayload(int address, byte[] payload)
    {
        var register = Schema.Registers[address];

        var command = PayloadCodec.Encode(register, PayloadCodec.Decode(register, Reply(register, payload)));

        Assert.AreEqual(MessageType.Write, command.MessageType);
        CollectionAssert.AreEqual(payload, command.GetPayload().ToArray());
    }

    [TestMethod]
    public void Encode_ValueOutsideDeclaredRange_Throws()
    {
        var register = Schema.Registers[33];
        var values = PayloadCodec.CreateDefault(register);
        values["Value"].Numbers = ["101"];

        Assert.ThrowsExactly<FormatException>(() => PayloadCodec.Encode(register, values));
    }

    [TestMethod]
    public void Encode_ValueOutsidePayloadType_Throws()
    {
        var register = Schema.Registers[32];
        var values = PayloadCodec.CreateDefault(register);
        values["Value"].Numbers = ["1", "70000", "3"];

        Assert.ThrowsExactly<FormatException>(() => PayloadCodec.Encode(register, values));
    }

    [TestMethod]
    public void Encode_TextLongerThanRegister_Throws()
    {
        var register = Schema.Registers[35];
        var values = PayloadCodec.CreateDefault(register);
        values["Value"].Text = "longer than eight";

        Assert.ThrowsExactly<FormatException>(() => PayloadCodec.Encode(register, values));
    }

    [TestMethod]
    public void FormatValue_FlagsAndUnknownChoice_AreNamed()
    {
        var outputs = Schema.Registers[34];
        var mode = Schema.Registers[36].Fields.Single(field => field.Name == "Mode");

        Assert.AreEqual("DO0 | DO1", PayloadCodec.Summarize(outputs, Reply(outputs, 0x03)));
        Assert.AreEqual("None", PayloadCodec.Summarize(outputs, Reply(outputs, 0x00)));
        Assert.AreEqual("0x3", PayloadCodec.FormatValue(mode, new FieldValue { Bits = 3 }));
    }
}
