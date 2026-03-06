using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MvcStructureInspector;

/// <summary>Parses .csproj (XML) and .sln (text) files into structured snapshots.</summary>
public static class ProjectFileParser
{
    // Properties already surfaced as dedicated fields — treated separately
    private static readonly HashSet<string> KnownProps = new(StringComparer.OrdinalIgnoreCase)
    {
        "TargetFramework", "TargetFrameworks",
        "OutputType", "Nullable", "ImplicitUsings", "LangVersion",
        "AssemblyName", "RootNamespace",
        "PackAsTool", "ToolCommandName", "Version",
        "Description", "Authors", "Company", "Copyright",
        "PackageTags", "PackageLicenseExpression", "PackageProjectUrl",
        "RepositoryUrl", "RepositoryType", "PackageReadmeFile", "Title",
        "PackageId"
    };

    private static readonly string[] ExcludedDirs = ["bin", "obj", ".git", ".vs", "node_modules"];

    // ── .csproj ──────────────────────────────────────────────────────────────

    public static List<ParsedCsprojFile> ParseCsprojFiles(string rootPath)
    {
        var result = new List<ParsedCsprojFile>();
        foreach (var file in Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(rootPath, file);
            // skip bin / obj folders
            if (rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   .Any(p => ExcludedDirs.Contains(p, StringComparer.OrdinalIgnoreCase)))
                continue;

            result.Add(ParseCsproj(file, rel));
        }
        return result;
    }

    private static ParsedCsprojFile ParseCsproj(string path, string rel)
    {
        try
        {
            var doc = XDocument.Load(path);
            string sdk = doc.Root?.Attribute("Sdk")?.Value ?? "";

            var props = doc.Descendants("PropertyGroup").SelectMany(pg => pg.Elements()).ToList();
            string Get(string n) =>
                props.FirstOrDefault(e => e.Name.LocalName.Equals(n, StringComparison.OrdinalIgnoreCase))
                     ?.Value.Trim() ?? "";
            string? Opt(string n) { var v = Get(n); return v == "" ? null : v; }

            // Target frameworks
            var frameworks = new List<string>();
            string tf  = Get("TargetFramework");
            string tfs = Get("TargetFrameworks");
            if (!string.IsNullOrWhiteSpace(tfs))
                frameworks.AddRange(tfs.Split(';').Select(s => s.Trim()).Where(s => s != ""));
            else if (!string.IsNullOrWhiteSpace(tf))
                frameworks.Add(tf);

            // PackageReferences (Include or Update attributes)
            var pkgRefs = doc.Descendants("PackageReference")
                .Select(e => new PackageRef(
                    e.Attribute("Include")?.Value ?? e.Attribute("Update")?.Value ?? "?",
                    e.Attribute("Version")?.Value ?? e.Element("Version")?.Value ?? ""))
                .OrderBy(r => r.Name)
                .ToList();

            // ProjectReferences
            var projRefs = doc.Descendants("ProjectReference")
                .Select(e => (e.Attribute("Include")?.Value ?? "?").Replace('\\', '/'))
                .OrderBy(p => p)
                .ToList();

            // Catch-all remaining PropertyGroup entries
            var other = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in props.Where(e => !KnownProps.Contains(e.Name.LocalName) && !string.IsNullOrWhiteSpace(e.Value)))
                other[el.Name.LocalName] = el.Value.Trim();

            return new ParsedCsprojFile(rel, sdk, frameworks,
                Opt("OutputType"), Opt("Nullable"), Opt("ImplicitUsings"), Opt("LangVersion"),
                pkgRefs, projRefs, other);
        }
        catch
        {
            return new ParsedCsprojFile(rel, "", [], null, null, null, null, [], [], []);
        }
    }

    // ── .sln ─────────────────────────────────────────────────────────────────

    private static readonly Regex SlnProjectRx = new(
        @"^Project\(""(?<type>\{[^}]+\})""\)\s*=\s*""(?<name>[^""]+)""\s*,\s*""(?<path>[^""]+)""",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static ParsedSlnFile? ParseSln(string rootPath)
    {
        var slnFiles = Directory.GetFiles(rootPath, "*.sln", SearchOption.TopDirectoryOnly);
        if (slnFiles.Length == 0) return null;

        string file = slnFiles[0];
        string rel  = Path.GetRelativePath(rootPath, file);
        string text;
        try { text = File.ReadAllText(file); } catch { return null; }

        var projects = SlnProjectRx.Matches(text)
            .Select(m => new SlnProject(
                m.Groups["type"].Value,
                m.Groups["name"].Value,
                m.Groups["path"].Value.Replace('\\', '/')))
            .ToList();

        return new ParsedSlnFile(rel, projects);
    }
}
