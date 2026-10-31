using System.IO.Compression;
using Harp.Toolkit.Web.Generation;

namespace Harp.Toolkit.Web.Tests;

[TestClass]
public sealed class MetadataLinksTests
{
    [TestMethod]
    [DataRow(
        "https://github.com/harp-tech/device.behavior/blob/main/device.yml",
        "https://raw.githubusercontent.com/harp-tech/device.behavior/main/device.yml")]
    [DataRow(
        "https://github.com/org/repo/blob/feature/x/sub/dir/device.yml",
        "https://raw.githubusercontent.com/org/repo/feature/x/sub/dir/device.yml")]
    [DataRow(
        "https://raw.githubusercontent.com/harp-tech/device.behavior/main/device.yml",
        "https://raw.githubusercontent.com/harp-tech/device.behavior/main/device.yml")]
    public void ToRawUrl_GitHubLink_ReturnsRawLink(string url, string expected)
    {
        Assert.AreEqual(expected, MetadataLinks.ToRawUrl(url));
    }

    [TestMethod]
    [DataRow(
        "https://raw.githubusercontent.com/harp-tech/device.behavior/main/firmware/Behavior/ios.yml",
        "https://github.com/harp-tech/device.behavior/blob/main/firmware/Behavior/ios.yml")]
    [DataRow("https://example.org/device.yml", "https://example.org/device.yml")]
    public void ToPageUrl_RawLink_ReturnsGitHubPage(string url, string expected)
    {
        Assert.AreEqual(expected, MetadataLinks.ToPageUrl(url));
    }

    [TestMethod]
    public void GetDisplayName_RegisteredDevice_ReturnsDeviceName()
    {
        DeviceEntry[] devices = [new("Behavior", 1216, "https://github.com/harp-tech/device.behavior", "https://example.org/b/device.yml")];

        Assert.AreEqual("Behavior", MetadataLinks.GetDisplayName("https://example.org/b/device.yml", devices));
        Assert.AreEqual("device.yml", MetadataLinks.GetDisplayName("https://example.org/a/device.yml", devices));
    }

    [TestMethod]
    public void OutputArchive_Create_GroupsFilesByLanguage()
    {
        GeneratedFile[] files =
        [
            new("Device.Generated.cs", OutputLanguage.CSharp, "class A {}"),
            new("__init__.py", OutputLanguage.Python, "x = 1"),
            new("py.typed", OutputLanguage.Python, ""),
            new("app.h", OutputLanguage.Firmware, "#pragma once"),
        ];

        using var archive = new ZipArchive(new MemoryStream(OutputArchive.Create(files)));

        CollectionAssert.AreEqual(
            new[] { "csharp/Device.Generated.cs", "python/__init__.py", "python/py.typed", "firmware/app.h" },
            archive.Entries.Select(entry => entry.FullName).ToArray());
        using var reader = new StreamReader(archive.Entries[0].Open());
        Assert.AreEqual("class A {}", reader.ReadToEnd());
    }
}
