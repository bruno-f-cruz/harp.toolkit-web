#:package YamlDotNet@16.3.0
#:project ../src/Harp.Toolkit.Web/Harp.Toolkit.Web.csproj
// File-based apps default to native AOT, which disables the reflection YAML and JSON rely on.
#:property PublishAot=false

// Builds the device catalog shown in the app from the Harp whoami registry.
//
// Every device in whoami.yml whose repository is public and has a device.yml at its root is
// listed, along with the IO pin metadata file (ios.yml) when the repository has one.
//
// Usage: dotnet run scripts/UpdateDevices.cs -- <output.json>
//
// Set GITHUB_TOKEN to raise the GitHub API rate limit; it is needed to find ios.yml files.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Harp.Toolkit.Web.Generation;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

const string WhoAmIUrl = "https://raw.githubusercontent.com/harp-tech/whoami/main/whoami.yml";

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run scripts/UpdateDevices.cs -- <output.json>");
    return 1;
}

var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("harp-generators-web", "1.0"));

var registry = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build()
    .Deserialize<Registry>(new MergingParser(new Parser(new StringReader(await http.GetStringAsync(WhoAmIUrl)))));

var devices = new List<DeviceEntry>();
foreach (var (whoAmI, entry) in registry.Devices)
{
    if (entry is null || ParseGitHubRepository(entry.RepositoryUrl) is not var (owner, repo))
        continue;

    RepositoryFiles files;
    try
    {
        files = await FindFilesAsync(owner, repo);
    }
    catch (HttpRequestException ex) when (ex.StatusCode is not null)
    {
        Console.Error.WriteLine($"skip {entry.Name}: {owner}/{repo} returned {(int)ex.StatusCode}");
        continue;
    }

    if (!files.HasDevice)
    {
        Console.Error.WriteLine($"skip {entry.Name}: no public device.yml in {owner}/{repo}");
        continue;
    }

    var raw = $"https://raw.githubusercontent.com/{files.Name}/{files.Branch}";
    devices.Add(new DeviceEntry(
        entry.Name,
        whoAmI,
        $"https://github.com/{files.Name}",
        $"{raw}/device.yml",
        files.IosPath is null ? null : $"{raw}/{files.IosPath}"));
}

devices.Sort((a, b) => string.CompareOrdinal(a.Name.ToLowerInvariant(), b.Name.ToLowerInvariant()));
var json = JsonSerializer.Serialize(devices, new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true,
    NewLine = "\n",
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
});
await File.WriteAllTextAsync(args[0], json + "\n");
Console.WriteLine($"wrote {devices.Count} devices to {args[0]}");
return 0;

static (string Owner, string Repo)? ParseGitHubRepository(string? url)
{
    const string Prefix = "https://github.com/";
    if (url is null || !url.StartsWith(Prefix, StringComparison.Ordinal))
        return null;

    var parts = url[Prefix.Length..].Trim('/').Split('/');
    return parts.Length >= 2 ? (parts[0], parts[1].EndsWith(".git") ? parts[1][..^4] : parts[1]) : null;
}

async Task<RepositoryFiles> FindFilesAsync(string owner, string repo)
{
    if (string.IsNullOrEmpty(token))
    {
        // Without a token, probe the raw files on the default branch instead of using the API.
        using var response = await http.SendAsync(new HttpRequestMessage(
            HttpMethod.Head, $"https://raw.githubusercontent.com/{owner}/{repo}/HEAD/device.yml"));
        if (response.StatusCode != HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
        return new($"{owner}/{repo}", "HEAD", response.IsSuccessStatusCode, null);
    }

    // The API follows renamed repositories, but raw file links do not, so use the current name.
    using var info = await GetApiJsonAsync($"repos/{owner}/{repo}");
    var name = info.RootElement.GetProperty("full_name").GetString()!;
    var branch = info.RootElement.GetProperty("default_branch").GetString()!;
    if (info.RootElement.GetProperty("private").GetBoolean())
    {
        // Browsers cannot fetch files from private repositories.
        return new(name, branch, false, null);
    }

    using var tree = await GetApiJsonAsync($"repos/{name}/git/trees/{branch}?recursive=1");
    var paths = tree.RootElement.GetProperty("tree").EnumerateArray()
        .Where(item => item.GetProperty("type").GetString() == "blob")
        .Select(item => item.GetProperty("path").GetString()!)
        .ToList();
    var ios = paths
        .Where(path => path == "ios.yml" || path.EndsWith("/ios.yml", StringComparison.Ordinal))
        .OrderBy(path => path.Length)
        .FirstOrDefault();
    return new(name, branch, paths.Contains("device.yml"), ios);
}

async Task<JsonDocument> GetApiJsonAsync(string path)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/{path}");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var response = await http.SendAsync(request);
    response.EnsureSuccessStatusCode();
    return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
}

record RepositoryFiles(string Name, string Branch, bool HasDevice, string? IosPath);

class Registry
{
    public Dictionary<int, RegistryEntry?> Devices { get; set; } = [];
}

class RegistryEntry
{
    public string Name { get; set; } = "";

    public string? RepositoryUrl { get; set; }
}
