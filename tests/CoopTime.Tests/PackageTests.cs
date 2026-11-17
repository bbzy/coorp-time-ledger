using System.IO.Compression;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CoopTime.Tests;

public sealed class PackageTests
{
    [Fact]
    public void ReleaseContainsOnlyModAssembliesAndManifest()
    {
        using ZipArchive zip = ZipFile.OpenRead(ArchivePath());
        string[] files = zip.Entries.Where(e => e.Name.Length > 0).Select(e => e.FullName).Order().ToArray();
        Assert.Equal(new[]
        {
            "CoopTimeLedger/CoopTime.Core.dll",
            "CoopTimeLedger/CoopTime.dll",
            "CoopTimeLedger/manifest.json"
        }, files);

        using Stream manifestStream = zip.GetEntry("CoopTimeLedger/manifest.json")!.Open();
        using StreamReader reader = new(manifestStream);
        JObject manifest = JObject.Parse(reader.ReadToEnd());
        Assert.Equal("CoopTime.dll", manifest["EntryDll"]?.Value<string>());
        Assert.Equal("bbzy.CoopTimeLedger", manifest["UniqueID"]?.Value<string>());
        Assert.Equal("0.1.3", manifest["Version"]?.Value<string>());
    }

    [Fact]
    public void PluginIsALibrary()
    {
        using ZipArchive zip = ZipFile.OpenRead(ArchivePath());
        using MemoryStream library = new();
        zip.GetEntry("CoopTimeLedger/CoopTime.dll")!.Open().CopyTo(library);
        library.Position = 0;
        using ModuleDefinition module = ModuleDefinition.ReadModule(library);
        Assert.Null(module.EntryPoint);
        Assert.Contains(module.Types, t => t.BaseType?.FullName == "StardewModdingAPI.Mod");
    }

    private static string ArchivePath()
    {
        string? path = Environment.GetEnvironmentVariable("COOPTIME_ARTIFACT");
        Assert.False(string.IsNullOrEmpty(path), "Run tests/run.nu to build and verify the package without launching the game.");
        return path;
    }
}
