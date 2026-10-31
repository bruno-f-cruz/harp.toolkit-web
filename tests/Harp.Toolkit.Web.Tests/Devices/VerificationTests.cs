using Harp.Toolkit.Verify;
using Harp.Toolkit.Web.Devices;

namespace Harp.Toolkit.Web.Tests.Devices;

[TestClass]
public sealed class VerificationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task Run_EmulatedDevice_CompletesEveryCheck()
    {
        // The emulator implements only a few registers, so many checks fail: this tests that the
        // run itself completes, not the emulator's conformance.
        var device = new EmulatedDevice(name: "Behavior", whoAmI: 1216);
        var options = new VerificationOptions(IncludePrerelease: false, Clock: null, Schema: null);
        var started = 0;

        var report = await VerificationRun.RunAsync(
            device, "emulated", options,
            onProgress: _ => started++,
            cancellationToken: TestContext.CancellationToken);

        var results = report.Suites.SelectMany(suite => suite.Results).ToList();
        Assert.AreEqual("", report.AbortReason);
        Assert.AreEqual("Behavior", report.DeviceName);
        Assert.AreEqual("1216", report.WhoAmI);
        Assert.HasCount(started, results);
        Assert.IsTrue(results.Any(result => result.Result.Status == Status.Passed));
        Assert.IsTrue(report.Suites.Any(suite => suite.Name == "R_WHO_AM_I"));

        var html = ReportHtml.Generate(report);
        StringAssert.Contains(html, "<h1>Behavior</h1>");
    }

    [TestMethod]
    public void CountChecks_DeviceSchema_AddsOneCheckPerRegister()
    {
        var schema = DeviceSchema.Parse("""
            device: Test
            whoAmI: 1
            registers:
              A:
                address: 32
                type: U8
              B:
                address: 33
                type: U16
            """, "test");

        var without = VerificationRun.CountChecks(new VerificationOptions(false, null, null));
        var with = VerificationRun.CountChecks(new VerificationOptions(false, null, schema));

        Assert.AreEqual(schema.Registers.Count, with - without);
    }
}
