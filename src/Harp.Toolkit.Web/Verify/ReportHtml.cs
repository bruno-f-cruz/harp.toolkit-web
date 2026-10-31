using System.Globalization;
using System.Net;
using System.Text;

namespace Harp.Toolkit.Verify;

/// <summary>
/// Writes a verification report as a single self-contained HTML file, with the same content as
/// the Harp.Toolkit report.
/// </summary>
internal static class ReportHtml
{
    const string Style = """
        :root { --bg: #fff; --surface: #f6f8fa; --border: #d0d7de; --text: #1f2328; --muted: #59636e;
          --ok: #1a7f37; --ok-bg: #dafbe1; --error: #cf222e; --error-bg: #ffebe9; --warn: #9a6700; --warn-bg: #fff8c5; }
        @media (prefers-color-scheme: dark) { :root { --bg: #0d1117; --surface: #161b22; --border: #30363d; --text: #e6edf3;
          --muted: #9198a1; --ok: #3fb950; --ok-bg: #12261e; --error: #f85149; --error-bg: #25171c; --warn: #d29922; --warn-bg: #272115; } }
        * { box-sizing: border-box; }
        body { margin: 0; background: var(--bg); color: var(--text); font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
        main { max-width: 1000px; margin: 0 auto; padding: 32px 16px 64px; }
        h1 { margin: 0 0 4px; font-size: 28px; }
        h2 { margin: 32px 0 4px; font-size: 18px; }
        .muted { color: var(--muted); }
        dl { display: grid; grid-template-columns: max-content 1fr; gap: 4px 20px; margin: 20px 0; padding: 16px; border: 1px solid var(--border); border-radius: 8px; background: var(--surface); }
        dt { color: var(--muted); } dd { margin: 0; }
        .notice { padding: 10px 14px; border-radius: 6px; margin: 12px 0; }
        .notice.warn { background: var(--warn-bg); color: var(--warn); }
        .notice.error { background: var(--error-bg); color: var(--error); }
        .counts { display: flex; flex-wrap: wrap; gap: 8px; margin: 16px 0; }
        .pill { display: inline-block; padding: 2px 10px; border-radius: 999px; font-size: 13px; font-weight: 600; white-space: nowrap; }
        .passed { background: var(--ok-bg); color: var(--ok); }
        .failed, .error { background: var(--error-bg); color: var(--error); }
        .skipped { background: var(--surface); color: var(--muted); border: 1px solid var(--border); }
        .table { overflow-x: auto; }
        table { width: 100%; border-collapse: collapse; margin-top: 8px; font-size: 14px; }
        th, td { text-align: left; vertical-align: top; padding: 8px 10px; border-bottom: 1px solid var(--border); }
        th { color: var(--muted); font-weight: 500; }
        td.name { font-family: ui-monospace, Consolas, monospace; white-space: nowrap; }
        td small { display: block; color: var(--muted); font-family: system-ui, sans-serif; white-space: normal; }
        .stats { font-family: ui-monospace, Consolas, monospace; font-size: 12px; color: var(--muted); }
        a { color: inherit; }
        """;

    public static string Generate(Report report)
    {
        var results = report.Suites.SelectMany(suite => suite.Results).ToList();
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append($"<title>{E(report.DeviceName)} verification</title><style>{Style}</style></head><body><main>");

        html.Append($"<h1>{E(report.DeviceName)}</h1>")
            .Append($"<p class=\"muted\">Harp device verification, {E(report.RunDate.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))}</p>");

        html.Append("<dl>");
        Row(html, "WhoAmI", E(report.WhoAmI));
        Row(html, "Serial port", E(report.PortName));
        Row(html, "Hardware version", E(report.HardwareVersion));
        Row(html, "Firmware version", E(report.FirmwareVersion));
        Row(html, "Protocol version declared", E(report.DeclaredProtocolVersion));
        Row(html, "Checked against", E(report.CheckedProtocolVersion));
        Row(html, "Specification", $"<a href=\"{E(report.ProtocolCommitUrl)}\">harp-tech/protocol@{E(report.ProtocolCommit)}</a>");
        Row(html, "Register set", $"Harp.Generators {E(report.RegisterSetVersion)}");
        Row(html, "Toolkit version", E(report.ToolkitVersion));
        html.Append("</dl>");

        if (report.AbortReason.Length > 0)
            html.Append($"<p class=\"notice error\"><strong>Verification incomplete.</strong> {E(report.AbortReason)}</p>");
        if (report.ProtocolNotice.Length > 0)
            html.Append($"<p class=\"notice warn\">{E(report.ProtocolNotice)}</p>");

        html.Append("<div class=\"counts\">");
        foreach (var status in new[] { Status.Passed, Status.Failed, Status.Error, Status.Skipped })
        {
            var count = results.Count(result => result.Result.Status == status);
            html.Append($"<span class=\"pill {StatusClass(status)}\">{count} {status.ToString().ToLowerInvariant()}</span>");
        }
        html.Append("</div>");
        html.Append("<p class=\"muted\">A verification result records how a device behaved against a stated version of the specification, and it confers no compliance status.</p>");

        foreach (var suite in report.Suites)
        {
            html.Append($"<h2>{E(suite.Name)}</h2><p class=\"muted\">{E(suite.Description)}</p>")
                .Append("<div class=\"table\"><table><thead><tr><th>Check</th><th>Result</th><th>Details</th></tr></thead><tbody>");
            foreach (var test in suite.Results)
            {
                html.Append("<tr>")
                    .Append($"<td class=\"name\">{E(test.Name)}<small>{E(test.Description)}</small></td>")
                    .Append($"<td><span class=\"pill {StatusClass(test.Result.Status)}\">{test.Result.Status}</span></td>")
                    .Append($"<td>{E(Describe(test.Result))}");
                if (test.Result is NumericBenchmarkResult benchmark)
                {
                    var s = benchmark.Summary;
                    html.Append($"<div class=\"stats\">mean {s.Mean:F6} · median {s.Median:F6} · sd {s.StdDev:F6} · min {s.Min:F6} · max {s.Max:F6}</div>");
                }
                html.Append("</td></tr>");
            }
            html.Append("</tbody></table></div>");
        }

        html.Append("</main></body></html>");
        return html.ToString();
    }

    /// <summary>
    /// Returns the text that explains a result.
    /// </summary>
    public static string Describe(IResult result) => result switch
    {
        ErrorResult error => $"{error.Exception.GetType().Name}: {error.Message}",
        _ => result.Message ?? string.Empty
    };

    /// <summary>
    /// Returns the CSS class of a result status.
    /// </summary>
    public static string StatusClass(Status status) => status.ToString().ToLowerInvariant();

    static void Row(StringBuilder html, string name, string value) =>
        html.Append($"<dt>{E(name)}</dt><dd>{value}</dd>");

    static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
