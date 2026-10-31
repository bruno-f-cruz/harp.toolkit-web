using Harp.Toolkit.Web.Devices;

namespace Harp.Toolkit.Verify;

/// <summary>
/// Options for clock alignment and PPS synchronization tests run against a reference clock device.
/// </summary>
/// <param name="ClockPort">
/// Port of the reference clock device (WhiteRabbit). Enabling this option runs the
/// simultaneous WhoAmI timestamp comparison test.
/// </param>
/// <param name="PpsEvent">
/// Address of the register on the tested device that reports the incoming PPS pulse from the
/// reference clock device. When provided, also runs the PPS alignment test.
/// </param>
/// <param name="ClockSamples">Number of PPS event pairs to collect for the PPS alignment test.</param>
internal record ClockTestOptions(
    IDevicePort ClockPort,
    int? PpsEvent = null,
    int ClockSamples = 5);
