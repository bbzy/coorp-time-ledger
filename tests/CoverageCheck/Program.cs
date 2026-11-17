using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

string? path = Environment.GetEnvironmentVariable("COOPTIME_COVERAGE_PATH");
string? output = Environment.GetEnvironmentVariable("COOPTIME_REPORT_DIR");
if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(output))
{
    Console.Error.WriteLine("CoverageCheck requires COOPTIME_COVERAGE_PATH and COOPTIME_REPORT_DIR from tests/run.nu.");
    return 1;
}

try
{
    XDocument document = XDocument.Load(path);
    ModuleCoverage[] modules = document.Descendants("package").Select(package => new ModuleCoverage(
        Required(package, "name"), Rate(package, "line-rate"), Rate(package, "branch-rate"),
        package.Descendants("class").Select(type => new TypeCoverage(Required(type, "name"),
            Required(type, "filename"), Rate(type, "line-rate"), Rate(type, "branch-rate"),
            type.Element("lines")!.Elements("line").Where(line => Required(line, "hits") == "0")
                .Select(line => int.Parse(Required(line, "number"), CultureInfo.InvariantCulture)).ToArray())).ToArray())).ToArray();
    File.WriteAllText(Path.Combine(output, "coverage-summary.json"),
        JsonSerializer.Serialize(modules, new JsonSerializerOptions { WriteIndented = true }));
    XDocument results = XDocument.Load(Path.Combine(output, "tests.trx"));
    XElement counters = results.Descendants().Single(element => element.Name.LocalName == "Counters");
    string[] currentNames = results.Descendants().Where(element => element.Name.LocalName == "UnitTestResult"
        && Required(element, "outcome") == "Passed").Select(element => Required(element, "testName")).ToArray();
    string? baselinePath = Environment.GetEnvironmentVariable("COOPTIME_BASELINE_TRX");
    string[] baselineNames = Array.Empty<string>();
    if (!string.IsNullOrEmpty(baselinePath))
    {
        baselineNames = XDocument.Load(baselinePath).Descendants()
            .Where(element => element.Name.LocalName == "UnitTestResult")
            .Select(element => Required(element, "testName")).ToArray();
    }

    Dictionary<string, string> renamedCases = new()
    {
        ["CoopTime.Tests.LedgerTests.DayResetClearsAllBalances"] = "CoopTime.Tests.LedgerTests.ExplicitResetClearsAllBalances",
        ["CoopTime.Tests.PackageTests.ReleaseContainsOnlyModAssembliesManifestAndTranslations"] =
            "CoopTime.Tests.PackageTests.ReleaseContainsOnlyModAssembliesAndManifest",
        ["CoopTime.Tests.PackageTests.PluginIsALibraryAndTranslationsHaveIdenticalKeys"] =
            "CoopTime.Tests.PackageTests.PluginIsALibrary",
        ["CoopTime.Tests.AdapterContractTests.EveryDynamicStatusHasBothTranslationsAndPackageIsPortableManagedIL"] =
            "CoopTime.Tests.AdapterContractTests.PackageIsPortableManagedIL"
    };
    const string displayCase = "CoopTime.Tests.TimingTests.DisplayDoesNotChangeBalance";
    foreach ((string exact, string previous, string current) in new[]
    {
        ("0", "0", "00:00"), ("0.000001", "<10", "00:00"), ("9.999999", "<10", "00:10"),
        ("10", "10", "00:10"), ("19.999999", "10", "00:20"), ("20", "20", "00:20"), ("27.5", "20", "00:30")
    })
    {
        renamedCases[$"""{displayCase}(exact: "{exact}", expected: "{previous}")"""] =
            $"""{displayCase}(exact: "{exact}", expected: "{current}")""";
    }

    string[] retiredBaseline = baselineNames.Where(name => name.StartsWith("CoopTime.Tests.PanelTests.", StringComparison.Ordinal)).ToArray();
    string[] mappedBaseline = baselineNames.Except(retiredBaseline)
        .Select(name => renamedCases.GetValueOrDefault(name, name)).ToArray();
    string[] missing = mappedBaseline.Except(currentNames).ToArray();
    File.WriteAllText(Path.Combine(output, "regression-summary.json"), JsonSerializer.Serialize(new
    {
        Total = Required(counters, "total"), Passed = Required(counters, "passed"),
        BaselineCompared = !string.IsNullOrEmpty(baselinePath), BaselineCount = baselineNames.Length,
        RetiredBaselineCases = retiredBaseline,
        RenamedBaselineCases = renamedCases.Where(pair => baselineNames.Contains(pair.Key)).ToDictionary(),
        MissingBaselineCases = missing
    }, new JsonSerializerOptions { WriteIndented = true }));
    if (Required(counters, "total") != Required(counters, "passed") || missing.Length > 0)
    {
        Console.Error.WriteLine($"Regression gate failed: tests failed/skipped or {missing.Length} original cases did not pass.");
        return 1;
    }

    Console.WriteLine($"{currentNames.Length} tests passed; {mappedBaseline.Length} baseline cases passed, {retiredBaseline.Length} retired with the ledger panel.");
    ModuleCoverage? core = modules.SingleOrDefault(module => module.Name == "CoopTime.Core");
    ModuleCoverage? mod = modules.SingleOrDefault(module => module.Name == "CoopTime");
    TypeCoverage? rewriter = mod?.Types.SingleOrDefault(type => type.Name == "CoopTime.Mod.GameClockRewriter");
    foreach (ModuleCoverage module in modules)
    {
        Console.WriteLine($"{module.Name}: lines {module.LineRate:P2}, branches {module.BranchRate:P2}");
    }

    if (core is not { LineRate: 1, BranchRate: 1 } || rewriter is not { LineRate: 1, BranchRate: 1 }
        || mod == null || !new[] { "GameClockPatch", "ModEntry", "TimeController" }
            .All(name => mod.Types.Any(type => type.Name == $"CoopTime.Mod.{name}")))
    {
        Console.Error.WriteLine("Coverage gate failed: require all core and IL-rewriter branches, and report the game-dependent adapters without excluding them.");
        return 1;
    }

    Console.WriteLine("Coverage gate passed. Game event wiring, native hook execution and rendering remain unexecuted; see the per-type summary.");
    return 0;
}
catch (Exception error) when (error is IOException or System.Xml.XmlException or InvalidOperationException or FormatException)
{
    Console.Error.WriteLine($"CoverageCheck failed for {path}: {error.Message}");
    return 1;
}

static string Required(XElement element, string attribute) => element.Attribute(attribute)?.Value
    ?? throw new InvalidOperationException($"Missing coverage attribute {attribute}.");
static decimal Rate(XElement element, string attribute) => decimal.Parse(Required(element, attribute), CultureInfo.InvariantCulture);

internal sealed record ModuleCoverage(string Name, decimal LineRate, decimal BranchRate, TypeCoverage[] Types);
internal sealed record TypeCoverage(string Name, string File, decimal LineRate, decimal BranchRate, int[] UncoveredLines);
