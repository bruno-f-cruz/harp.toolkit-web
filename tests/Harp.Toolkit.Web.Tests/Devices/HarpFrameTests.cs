using Bonsai.Harp;
using Harp.Toolkit.Web.Devices;

namespace Harp.Toolkit.Web.Tests.Devices;

[TestClass]
public sealed class HarpFrameTests
{
    static FrameSegment Field(IReadOnlyList<FrameSegment> segments, FrameField field) =>
        segments.Single(segment => segment.Field == field);

    [TestMethod]
    public void Describe_TimestampedEvent_SplitsEveryField()
    {
        var message = HarpMessage.FromPayload(OperationControl.Address, 12.5, MessageType.Event, PayloadType.U8, [0x81]);
        var register = DeviceSchema.Core.Registers[OperationControl.Address];

        var segments = HarpFrame.Describe(message, register);

        CollectionAssert.AreEqual(
            new[] { FrameField.MessageType, FrameField.Length, FrameField.Address, FrameField.Port, FrameField.PayloadType, FrameField.Timestamp, FrameField.Payload, FrameField.Checksum },
            segments.Select(segment => segment.Field).ToArray());
        CollectionAssert.AreEqual(message.MessageBytes, segments.SelectMany(segment => segment.Bytes).ToArray());
        Assert.AreEqual("Event", Field(segments, FrameField.MessageType).Value);
        Assert.AreEqual("10 · OperationControl", Field(segments, FrameField.Address).Value);
        Assert.AreEqual("U8 · timestamped", Field(segments, FrameField.PayloadType).Value);
        Assert.AreEqual("12.500000 s", Field(segments, FrameField.Timestamp).Value);
        StringAssert.StartsWith(Field(segments, FrameField.Payload).Value, "OperationMode=Active");
        Assert.AreEqual("Valid", Field(segments, FrameField.Checksum).Value);
        Assert.IsFalse(segments.Any(segment => segment.IsProblem));
    }

    [TestMethod]
    public void Describe_CommandWithoutTimestamp_HasNoTimestampField()
    {
        var segments = HarpFrame.Describe(HarpCommand.ReadUInt16(WhoAmI.Address));

        Assert.IsFalse(segments.Any(segment => segment.Field == FrameField.Timestamp));
        Assert.AreEqual("Empty", Field(segments, FrameField.Payload).Value);
        Assert.AreEqual("255 · this device", Field(segments, FrameField.Port).Value);
    }

    [TestMethod]
    public void Describe_CorruptChecksum_ReportsExpectedValue()
    {
        var bytes = HarpCommand.WriteByte(32, 7).MessageBytes.ToArray();
        var expected = bytes[^1];
        bytes[^1] ^= 0xFF;

        var checksum = Field(HarpFrame.Describe(new HarpMessage(bytes)), FrameField.Checksum);

        Assert.IsTrue(checksum.IsProblem);
        Assert.AreEqual($"Expected 0x{expected:X2}", checksum.Value);
    }

    [TestMethod]
    public void Describe_ErrorReply_FlagsTheMessageType()
    {
        var bytes = HarpCommand.WriteByte(32, 7).MessageBytes.ToArray();
        bytes[0] |= 0x08;

        var type = Field(HarpFrame.Describe(new HarpMessage(updateChecksum: true, bytes)), FrameField.MessageType);

        Assert.IsTrue(type.IsProblem);
        Assert.AreEqual("Write · error", type.Value);
    }
}
