using System.Reflection;
using Harp.Toolkit.Verify.Suites;
using Harp.Toolkit.Web.Devices;

namespace Harp.Toolkit.Verify;

/// <summary>
/// Specifies the options of a verification run.
/// </summary>
/// <param name="IncludePrerelease">Also run the checks of the next, not yet ratified, protocol version.</param>
/// <param name="Clock">The reference clock device, to run the clock alignment checks.</param>
/// <param name="Schema">The device registers, to check the device against its device.yml.</param>
internal sealed record VerificationOptions(bool IncludePrerelease, ClockTestOptions? Clock, DeviceSchema? Schema);

/// <summary>
/// Reports the progress of a verification run.
/// </summary>
/// <param name="Completed">The number of checks completed.</param>
/// <param name="Total">The number of checks in the run.</param>
/// <param name="Suite">The suite of the check running now.</param>
/// <param name="Test">The name of the check running now.</param>
internal readonly record struct VerificationProgress(int Completed, int Total, string Suite, string Test);

/// <summary>
/// Runs every verification suite against a device and collects the results into a report, as
/// the Harp.Toolkit <c>verify</c> command does.
/// </summary>
internal static class VerificationRun
{
    /// <summary>
    /// Asynchronously verifies the device on the specified port.
    /// </summary>
    /// <param name="port">The closed port of the device.</param>
    /// <param name="portLabel">The description of the port shown in the report.</param>
    /// <param name="options">The options of the run.</param>
    /// <param name="onProgress">Called before each check runs.</param>
    /// <param name="onResult">Called with the result of each check.</param>
    /// <param name="cancellationToken">The token that stops the run between checks.</param>
    /// <returns>
    /// The task object representing the asynchronous operation. The <see cref="Task{TResult}.Result"/>
    /// property contains the report, whose <see cref="Report.AbortReason"/> says why the run stopped early, if it did.
    /// </returns>
    public static async Task<Report> RunAsync(
        IDevicePort port,
        string portLabel,
        VerificationOptions options,
        Action<VerificationProgress>? onProgress = null,
        Action<SuiteResult, MethodResult>? onResult = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await VerifyConnection.OpenAsync(port, cancellationToken);
        var identity = await connection.ReadDeviceIdentityAsync(cancellationToken);
        var declaredVersion = await connection.ReadProtocolVersionAsync(cancellationToken);
        var target = new ProtocolTarget(declaredVersion, options.IncludePrerelease);
        var runner = CreateRunner(target.IncludePrerelease, options);

        var report = new Report
        {
            DeviceName = identity.Name is { Length: > 0 } name ? name : "Harp Device",
            PortName = portLabel,
            WhoAmI = identity.WhoAmI.ToString(),
            HardwareVersion = identity.HardwareVersion?.ToString() ?? "not reported",
            FirmwareVersion = identity.FirmwareVersion?.ToString() ?? "not reported",
            RunDate = DateTime.Now,
            IncludePrerelease = target.IncludePrerelease,
            ProtocolNotice = GetProtocolNotice(target, runner.PrereleaseTestCount),
            DeclaredProtocolVersion = GetDeclaredVersion(target),
            CheckedProtocolVersion = GetCheckedVersion(target),
            ProtocolCommit = ProtocolReference.ShortCommit,
            ProtocolCommitUrl = ProtocolReference.TreeUrl,
            RegisterSetVersion = CoreSchema.Version,
            ToolkitVersion = GetToolkitVersion()
        };

        int completed = 0;
        try
        {
            await foreach (var (suite, result) in runner.RunAllAsync(connection, cancellationToken, (suite, testName, _) =>
                onProgress?.Invoke(new VerificationProgress(completed, runner.TestCount, suite.GetType().Name, testName))))
            {
                completed++;
                var suiteResult = report.Suites.FirstOrDefault(s => s.Name == suite.GetType().Name);
                if (suiteResult is null)
                {
                    suiteResult = new SuiteResult { Name = suite.GetType().Name, Description = suite.Description };
                    report.Suites.Add(suiteResult);
                }
                suiteResult.Results.Add(result);
                onResult?.Invoke(suiteResult, result);
            }
        }
        catch (TimeoutException ex)
        {
            report.AbortReason = $"{ex.Message} Verification stopped after {completed} of {runner.TestCount} checks. " +
                "Run the verification again once the device and the connection are stable.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            report.AbortReason = $"Verification was stopped after {completed} of {runner.TestCount} checks.";
        }

        return report;
    }

    /// <summary>
    /// Returns the number of checks a run with the specified options includes, before the device
    /// is known. Checks outside the device's protocol version may be left out once it is.
    /// </summary>
    public static int CountChecks(VerificationOptions options) =>
        CreateRunner(options.IncludePrerelease, options).TestCount;

    static Runner CreateRunner(bool includePrerelease, VerificationOptions options)
    {
        var runner = new Runner(includePrerelease);
        runner.AddSuite(new R_WHO_AM_I());
        runner.AddSuite(new R_HW_VERSION_H());
        runner.AddSuite(new R_HW_VERSION_L());
        runner.AddSuite(new R_ASSEMBLY_VERSION());
        runner.AddSuite(new R_CORE_VERSION_H());
        runner.AddSuite(new R_CORE_VERSION_L());
        runner.AddSuite(new R_FW_VERSION_H());
        runner.AddSuite(new R_FW_VERSION_L());
        runner.AddSuite(new R_TIMESTAMP_SECOND());
        runner.AddSuite(new R_TIMESTAMP_MICRO());
        runner.AddSuite(new R_OPERATION_CTRL());
        runner.AddSuite(new R_RESET_DEV());
        runner.AddSuite(new R_DEVICE_NAME());
        runner.AddSuite(new R_SERIAL_NUMBER());
        runner.AddSuite(new R_CLOCK_CONFIG());
        runner.AddSuite(new R_TIMESTAMP_OFFSET());
        runner.AddSuite(new R_UID());
        runner.AddSuite(new R_TAG());
        runner.AddSuite(new R_HEARTBEAT());
        runner.AddSuite(new R_VERSION());
        runner.AddSuite(new RoundTripTestSuite());
        runner.AddSuite(new ClockTestSuite(options.Clock));
        runner.AddSuite(new DeviceInterfaceSuite(options.Schema));
        return runner;
    }

    static string GetToolkitVersion()
    {
        var assembly = typeof(VerificationRun).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "not reported";
        return $"Harp.Toolkit Web {version}";
    }

    static string GetDeclaredVersion(ProtocolTarget target)
    {
        return target.DeclaredVersion.HasValue
            ? target.DeclaredVersion.GetValueOrDefault().ToString()
            : "not declared";
    }

    static string GetCheckedVersion(ProtocolTarget target)
    {
        if (target.IncludePrerelease)
            return $"v{ProtocolReference.PrereleaseMajorVersion}, which is not yet ratified";

        return target.Scope == ProtocolScope.V1
            ? "v1"
            : $"v1, since v{ProtocolReference.PrereleaseMajorVersion} is not yet ratified";
    }

    static string GetProtocolNotice(ProtocolTarget target, int count)
    {
        if (target.IncludePrerelease)
        {
            if (target.Scope == ProtocolScope.V2)
                return $"Including {count} prerelease checks, which this device declares support for.";

            return $"Including {count} prerelease checks against protocol " +
                $"v{ProtocolReference.PrereleaseMajorVersion}, which this device does not declare.";
        }

        if (target.Scope == ProtocolScope.Unsupported)
        {
            return $"This device declares protocol {GetDeclaredVersion(target)}, which this toolkit " +
                $"does not cover, so only the v1 baseline applies. {GetRerunHint(count)}".TrimEnd();
        }

        if (!target.DeclaredVersion.HasValue)
        {
            return "This device declares no protocol version, so only the v1 baseline applies. " +
                $"Implementing R_VERSION is the first step of a v{ProtocolReference.PrereleaseMajorVersion} " +
                $"migration. {GetRerunHint(count)}".TrimEnd();
        }

        return GetRerunHint(count);
    }

    static string GetRerunHint(int count)
    {
        return count > 0
            ? $"{count} prerelease checks were not run. Turn on prerelease checks to include them."
            : string.Empty;
    }
}
