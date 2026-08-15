using System.Reflection;
using System.Text.Json;

namespace LabelZoom.Sdk.Tests.Conformance;

/// <summary>
/// Loads the shared, language-neutral fixtures in <c>conformance/</c>.
/// </summary>
/// <remarks>
/// These files are the contract. Six other language SDKs run the same cases, so a change here is
/// a change to every implementation — see <c>docs/CONFORMANCE.md</c>.
/// </remarks>
internal static class ConformanceFixtures
{
    private const string Language = "dotnet";

    private static readonly Lazy<string> RootDirectory = new(FindRoot);

    /// <summary>Every case id declared in <c>spec.json</c>, in order.</summary>
    public static IReadOnlyList<string> AllCaseIds { get; } = LoadSpecCaseIds();

    /// <summary>Case ids this language has explicitly declared it does not run.</summary>
    public static IReadOnlyDictionary<string, string> Skips { get; } = LoadSkips();

    /// <summary>The cases this language is expected to execute.</summary>
    public static IReadOnlyList<string> ExpectedCaseIds { get; } =
        AllCaseIds.Where(id => !Skips.ContainsKey(id)).ToList();

    /// <summary>xUnit theory data: one row per expected case.</summary>
    public static IEnumerable<object[]> TheoryData() => ExpectedCaseIds.Select(id => new object[] { id });

    public static JsonElement Load(string caseId)
    {
        var path = Path.Combine(RootDirectory.Value, "cases", caseId.Replace('/', Path.DirectorySeparatorChar) + ".json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Conformance case '{caseId}' has no fixture file. Expected it at {path}.", path);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    public static JsonElement Spec()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RootDirectory.Value, "spec.json")));
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<string> LoadSpecCaseIds() =>
        Spec().GetProperty("cases").EnumerateArray().Select(e => e.GetString()!).ToList();

    private static IReadOnlyDictionary<string, string> LoadSkips()
    {
        var path = Path.Combine(RootDirectory.Value, "skips", $"{Language}.json");
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("skips").EnumerateArray().ToDictionary(
            e => e.GetProperty("id").GetString()!,
            e => e.GetProperty("reason").GetString() ?? string.Empty);
    }

    /// <summary>
    /// Finds <c>conformance/</c>, preferring the copy the test project stages next to the binary
    /// and falling back to walking up to the repository root for local ad-hoc runs.
    /// </summary>
    private static string FindRoot()
    {
        var beside = Path.Combine(
            Path.GetDirectoryName(typeof(ConformanceFixtures).GetTypeInfo().Assembly.Location)!,
            "conformance");
        if (Directory.Exists(Path.Combine(beside, "cases")))
        {
            return beside;
        }

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "conformance");
            if (Directory.Exists(Path.Combine(candidate, "cases")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the conformance/ directory. It should be copied next to the test " +
            "assembly by the csproj, or reachable by walking up from the working directory.");
    }
}
