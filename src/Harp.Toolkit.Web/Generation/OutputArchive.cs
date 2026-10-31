using System.IO.Compression;
using System.Text;

namespace Harp.Toolkit.Web.Generation;

/// <summary>
/// Packages generated files into a zip archive with one folder per language.
/// </summary>
public static class OutputArchive
{
    public static byte[] Create(IEnumerable<GeneratedFile> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry($"{GetFolder(file.Language)}/{file.Name}", CompressionLevel.Fastest);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(file.Content);
            }
        }
        return stream.ToArray();
    }

    public static string GetFileName(DeviceMetadata? metadata) =>
        metadata is { IsApplicationDevice: true } ? $"{metadata.Device}.zip" : "Harp.zip";

    static string GetFolder(OutputLanguage language) => language switch
    {
        OutputLanguage.CSharp => "csharp",
        OutputLanguage.Python => "python",
        OutputLanguage.Firmware => "firmware",
        _ => throw new ArgumentOutOfRangeException(nameof(language))
    };
}
