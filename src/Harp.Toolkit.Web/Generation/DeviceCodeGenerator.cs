using System.Reflection;
using System.Text.RegularExpressions;
using YamlDotNet.Core;

namespace Harp.Toolkit.Web.Generation;

/// <summary>
/// Generates the C# and Python device interfaces and the device firmware from the text of the
/// device metadata and IO pin metadata files.
/// </summary>
public static partial class DeviceCodeGenerator
{
    /// <summary>
    /// Gets the version of the Harp.Generators package used to generate code.
    /// </summary>
    public static string GeneratorsVersion { get; } =
        typeof(DeviceCodeGenerator).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "HarpGeneratorsVersion")?.Value ?? "unknown";

    /// <summary>
    /// Gets the namespace conventionally used for the C# interface of the specified device.
    /// </summary>
    public static string GetDefaultNamespace(DeviceMetadata metadata) =>
        metadata.IsApplicationDevice ? $"Harp.{metadata.Device}" : "Bonsai.Harp";

    /// <summary>
    /// Parses the device metadata and generates all interface and firmware files.
    /// </summary>
    /// <param name="yaml">The contents of the device metadata file.</param>
    /// <param name="iosYaml">
    /// The contents of the IO pin metadata file, or <see langword="null"/> or empty to generate
    /// firmware with no IO pins.
    /// </param>
    /// <param name="options">The code generation options.</param>
    public static GenerationResult Generate(string yaml, string? iosYaml, GenerationOptions options)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return GenerationResult.Empty;

        DeviceMetadata? metadata;
        try
        {
            metadata = DeviceMetadata.Parse(yaml);
        }
        catch (Exception ex)
        {
            var errors = ToDiagnostics(ex, yaml, MetadataSource.Device).OrderBy(diagnostic => diagnostic.Line ?? long.MaxValue);
            return new GenerationResult(null, null, [], errors.ToList());
        }

        if (metadata is null)
            return GenerationResult.Failed(new Diagnostic("The document does not contain any device metadata."));

        var files = new List<GeneratedFile>();
        var diagnostics = new List<Diagnostic>();
        var ns = string.IsNullOrWhiteSpace(options.Namespace)
            ? GetDefaultNamespace(metadata)
            : options.Namespace.Trim();

        try
        {
            var generator = new InterfaceGenerator(metadata, ns);
            var implementation = generator.GenerateImplementation();
            diagnostics.AddRange(ToDiagnostics(generator.Errors));
            files.AddRange(implementation.Select(file =>
                new GeneratedFile(file.Key, OutputLanguage.CSharp, file.Value)));
        }
        catch (Exception ex)
        {
            diagnostics.AddRange(ToDiagnostics(ex, yaml, MetadataSource.Device, "C#"));
        }

        try
        {
            var generator = new PythonGenerator(metadata, options.PythonPackage);
            var implementation = generator.GenerateImplementation();
            diagnostics.AddRange(ToDiagnostics(generator.Errors));
            files.AddRange(implementation.Select(file =>
                new GeneratedFile(file.Key, OutputLanguage.Python, file.Value)));
        }
        catch (Exception ex)
        {
            diagnostics.AddRange(ToDiagnostics(ex, yaml, MetadataSource.Device, "Python"));
        }

        if (TryParsePortPins(iosYaml, diagnostics) is { } portPins)
        {
            try
            {
                var generator = new FirmwareGenerator(metadata, portPins);
                var headers = generator.GenerateHeaders();
                var implementation = generator.GenerateImplementation();
                diagnostics.AddRange(ToDiagnostics(generator.Errors));
                files.AddRange(headers.Concat(implementation).Select(file =>
                    new GeneratedFile(file.Key, OutputLanguage.Firmware, file.Value)));
            }
            catch (Exception ex)
            {
                diagnostics.AddRange(ToDiagnostics(ex, yaml, MetadataSource.Device, "Firmware"));
            }
        }

        return new GenerationResult(metadata, ns, files, diagnostics);
    }

    static PortPinMetadata? TryParsePortPins(string? iosYaml, List<Diagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(iosYaml))
            return [];

        try
        {
            using var reader = new StringReader(iosYaml);
            return PortPinMetadata.Load(reader) ?? [];
        }
        catch (Exception ex)
        {
            diagnostics.AddRange(ToDiagnostics(ex, iosYaml, MetadataSource.IO));
            return null;
        }
    }

    // The analyzer flags CompilerError as unsupported on browser, but it is a plain data
    // class and works under WebAssembly, since the generators themselves rely on it.
#pragma warning disable CA1416
    static IEnumerable<Diagnostic> ToDiagnostics(System.CodeDom.Compiler.CompilerErrorCollection errors)
    {
        foreach (System.CodeDom.Compiler.CompilerError error in errors)
            yield return new Diagnostic(error.ErrorText, error.IsWarning);
    }
#pragma warning restore CA1416

    static IEnumerable<Diagnostic> ToDiagnostics(Exception ex, string yaml, MetadataSource source, string? stage = null)
    {
        // YamlDotNet wraps the meaningful error in one or more exceptions carrying the
        // document location, so report the innermost message at the innermost location.
        // Exceptions raised without a location carry an empty mark at the document start.
        Mark? location = null;
        var message = ex.Message;
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is YamlException yamlException && (yamlException.Start.Index > 0 || yamlException.End.Index > 0))
                location = yamlException.Start;
            message = current.Message;
        }

        message = MarkPrefix().Replace(message, string.Empty).Trim();
        message = MissingProperty().Replace(message, "Missing required property '$1'.");
        if (UnknownProperty().Match(message) is { Success: true } unknown)
        {
            // Some type converters report the enclosing mapping rather than the key itself.
            var name = unknown.Groups[1].Value;
            message = $"Unknown property '{name}'.";
            if (location is { } start && FindKeyFrom(yaml, name, (int)start.Line) is { } key)
                location = new Mark(start.Index, key.Line, key.Column);
        }
        var prefix = stage is null ? string.Empty : $"{stage} generation failed: ";

        // Validation reports every problem at once, one per line, each naming the register,
        // payload member, or mask bit it refers to. Split them up and point at that name.
        var lines = message.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (location is null && lines.Length > 1 && lines[0].EndsWith(':'))
        {
            foreach (var line in lines.Skip(1))
            {
                var match = NamedError().Match(line);
                if (!match.Success)
                {
                    yield return new Diagnostic(prefix + line, Source: source);
                    continue;
                }

                var name = match.Groups["name"].Value;
                var position = FindDeclaration(yaml, name);
                yield return new Diagnostic(
                    $"{prefix}{name}: {match.Groups["text"].Value}", IsWarning: false, position?.Line, position?.Column, source);
            }
            yield break;
        }

        yield return new Diagnostic(prefix + message, IsWarning: false, location?.Line, location?.Column, source);
    }

    /// <summary>
    /// Finds the line declaring a dotted name such as <c>DigitalOutputs.DO4</c>, by locating
    /// each segment as a mapping key nested under the previous one.
    /// </summary>
    public static (long Line, long Column)? FindDeclaration(string yaml, string name)
    {
        var lines = yaml.Split('\n');
        var start = 0;
        var parentIndent = -1;
        (long, long)? position = null;
        foreach (var segment in name.Split('.'))
        {
            var key = new Regex($@"^(?<indent>\s*)[""']?{Regex.Escape(segment)}[""']?\s*:");
            position = null;
            for (var i = start; i < lines.Length; i++)
            {
                var match = key.Match(lines[i]);
                if (match.Success && match.Groups["indent"].Length > parentIndent)
                {
                    parentIndent = match.Groups["indent"].Length;
                    position = (i + 1, parentIndent + 1);
                    start = i + 1;
                    break;
                }
            }

            if (position is null)
                return null;
        }

        return position;
    }

    static (long Line, long Column)? FindKeyFrom(string yaml, string key, int line)
    {
        var pattern = new Regex($@"^(?<indent>\s*)[""']?{Regex.Escape(key)}[""']?\s*:");
        var lines = yaml.Split('\n');
        for (var i = Math.Max(0, line - 1); i < lines.Length; i++)
        {
            if (pattern.Match(lines[i]) is { Success: true } match)
                return (i + 1, match.Groups["indent"].Length + 1);
        }
        return null;
    }

    [GeneratedRegex(@"^\(Line: \d+, Col: \d+, Idx: \d+\) - \(Line: \d+, Col: \d+, Idx: \d+\):\s*")]
    private static partial Regex MarkPrefix();

    [GeneratedRegex(@"^Property '(.+)' not found on type '.+'\.?$")]
    private static partial Regex UnknownProperty();

    [GeneratedRegex(@"^Required property '(.+)' not found when deserializing type '.+'\.?$")]
    private static partial Regex MissingProperty();

    [GeneratedRegex(@"^\((?<name>[^)]+)\)\s*(?<text>.*)$")]
    private static partial Regex NamedError();
}

/// <summary>
/// Specifies the language of a generated file.
/// </summary>
public enum OutputLanguage
{
    CSharp,
    Python,
    Firmware
}

/// <summary>
/// Specifies which metadata file a diagnostic refers to.
/// </summary>
public enum MetadataSource
{
    Device,
    IO
}

/// <summary>
/// Represents the user options for code generation.
/// </summary>
/// <param name="Namespace">The C# namespace, or <see langword="null"/> to use the device default.</param>
/// <param name="PythonPackage">Whether to generate the Python interface as a package initializer.</param>
public sealed record GenerationOptions(string? Namespace = null, bool PythonPackage = false);

/// <summary>
/// Represents a single generated source file.
/// </summary>
public sealed record GeneratedFile(string Name, OutputLanguage Language, string Content);

/// <summary>
/// Represents an error or warning emitted while parsing metadata or generating code.
/// </summary>
public sealed record Diagnostic(
    string Message,
    bool IsWarning = false,
    long? Line = null,
    long? Column = null,
    MetadataSource Source = MetadataSource.Device);

/// <summary>
/// Represents the outcome of generating code from a device metadata file.
/// </summary>
public sealed record GenerationResult(
    DeviceMetadata? Metadata,
    string? Namespace,
    IReadOnlyList<GeneratedFile> Files,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public static readonly GenerationResult Empty = new(null, null, [], []);

    public static GenerationResult Failed(Diagnostic diagnostic) => new(null, null, [], [diagnostic]);

    public bool HasErrors => Diagnostics.Any(diagnostic => !diagnostic.IsWarning);
}
