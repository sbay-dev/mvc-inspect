using MvcStructureInspector;
using System.Diagnostics;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0)
{
    PrintHelp();
    return 1;
}

// -- Parse shared options -------------------------------------------------------
var options = new InspectorOptions
{
    IncludeViews         = !args.Contains("--no-views"),
    CsOnly               = args.Contains("--cs-only"),
    IncludeMigrations    = !args.Contains("--no-migrations"),
    IncludeProjectFiles  = args.Contains("--with-proj"),
};

bool autoOpen = args.Contains("--open");

// --out is optional; if omitted the output is auto-saved inside the project folder
string? outFile = null;
int outIdx = Array.IndexOf(args, "--out");
if (outIdx >= 0 && outIdx + 1 < args.Length)
    outFile = args[outIdx + 1];

// -- Command: --compare <pathA> <pathB> -----------------------------------------
int cmpIdx = Array.IndexOf(args, "--compare");
if (cmpIdx >= 0)
{
    if (cmpIdx + 2 >= args.Length)
    {
        Error("Two paths required: mvc-inspect --compare <pathA> <pathB>");
        return 2;
    }

    string pathA = args[cmpIdx + 1];
    string pathB = args[cmpIdx + 2];

    if (!Directory.Exists(pathA)) { Error($"Path A not found: {pathA}"); return 2; }
    if (!Directory.Exists(pathB)) { Error($"Path B not found: {pathB}"); return 2; }

    // Auto output: <pathB>\mvc-gap-report_yyyyMMdd_HHmmss.txt  (timestamped to prevent overwrite)
    string resolvedOut = outFile ?? Path.Combine(Path.GetFullPath(pathB),
        $"mvc-gap-report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"  Analyzing [A]: {pathA}");
    Console.ResetColor();
    var parser = new StructureParser(options);
    var snapA  = parser.Parse(pathA);

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"  Analyzing [B]: {pathB}");
    Console.ResetColor();
    var snapB = parser.Parse(pathB);

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("  Comparing...");
    Console.ResetColor();

    var engine = new ComparisonEngine();
    var (diffs, razorDiffs, staticDiffs, projDiffs, slnDiff, summary) = engine.Compare(snapA, snapB);

    var formatter = new GapReportFormatter();
    string report = formatter.Format(snapA, snapB, diffs, razorDiffs, staticDiffs, projDiffs, slnDiff, summary);

    WriteOutput(report, resolvedOut, autoOpen);
    return 0;
}

// -- Command: --from-report <report> <pathB> ------------------------------------
int frIdx = Array.IndexOf(args, "--from-report");
if (frIdx >= 0)
{
    if (frIdx + 2 >= args.Length)
    {
        Error("Two arguments required: mvc-inspect --from-report <report.txt|.snapshot.json> <projectPath>");
        return 2;
    }

    string reportFile   = args[frIdx + 1];
    string liveProject  = args[frIdx + 2];

    if (!File.Exists(reportFile)) { Error($"Report file not found: {reportFile}"); return 2; }
    if (!Directory.Exists(liveProject)) { Error($"Project path not found: {liveProject}"); return 2; }

    string snapshotPath;
    try
    {
        snapshotPath = SnapshotSerializer.ResolveSnapshotPath(reportFile);
    }
    catch (FileNotFoundException ex)
    {
        Error(ex.Message);
        return 2;
    }

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"  Loading snapshot [A]: {snapshotPath}");
    Console.ResetColor();

    ProjectSnapshot snapA;
    try
    {
        snapA = SnapshotSerializer.Load(snapshotPath);
    }
    catch (Exception ex)
    {
        Error($"Failed to load snapshot: {ex.Message}");
        return 2;
    }

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"  Analyzing live project [B]: {liveProject}");
    Console.ResetColor();
    var parser = new StructureParser(options);
    var snapB  = parser.Parse(liveProject);

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("  Comparing snapshot [A] vs live project [B]...");
    Console.ResetColor();

    var engine = new ComparisonEngine();
    var (diffs, razorDiffs, staticDiffs, projDiffs, slnDiff, summary) = engine.Compare(snapA, snapB);

    var formatter = new GapReportFormatter();
    string report = formatter.Format(snapA, snapB, diffs, razorDiffs, staticDiffs, projDiffs, slnDiff, summary);

    string resolvedOut = outFile ?? Path.Combine(Path.GetFullPath(liveProject),
        $"mvc-gap-report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

    WriteOutput(report, resolvedOut, autoOpen);
    return 0;
}

// -- Command: gitignore <path> ---------------------------------------------------
int giIdx = Array.IndexOf(args, "gitignore");
if (giIdx >= 0 || Array.IndexOf(args, "--gitignore") >= 0)
{
    int idx = giIdx >= 0 ? giIdx : Array.IndexOf(args, "--gitignore");
    if (idx + 1 >= args.Length)
    {
        Error("Path required: mvc-inspect gitignore <projectPath>");
        return 2;
    }

    string giPath = Path.GetFullPath(args[idx + 1]);
    if (!Directory.Exists(giPath)) { Error($"Path not found: {giPath}"); return 2; }

    bool preview   = args.Contains("--preview");
    bool merge     = args.Contains("--merge");

    // Collect custom --add patterns
    var customPatterns = new List<string>();
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--add" && i + 1 < args.Length)
        {
            for (int j = i + 1; j < args.Length && !args[j].StartsWith("--"); j++)
                customPatterns.Add(args[j]);
        }
    }

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"  Scanning: {giPath}");
    Console.ResetColor();

    var generator = new GitignoreGenerator();
    var detected  = generator.DetectProjects(giPath);

    // Display detected ecosystems
    var ecosystems = detected
        .Where(d => d.Type is not (GitignoreGenerator.ProjectType.MacOS
            or GitignoreGenerator.ProjectType.Windows
            or GitignoreGenerator.ProjectType.Linux))
        .GroupBy(d => d.Type)
        .ToList();

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  Detected {ecosystems.Count} ecosystem(s):");
    foreach (var eco in ecosystems)
    {
        var locations = eco.DistinctBy(e => e.RelativePath).ToList();
        Console.WriteLine($"    • {eco.Key}: {locations.Count} location(s)");
        foreach (var loc in locations.Take(5))
            Console.WriteLine($"        └─ {loc.RelativePath} (via {loc.Indicator})");
        if (locations.Count > 5)
            Console.WriteLine($"        └─ ... and {locations.Count - 5} more");
    }
    Console.ResetColor();

    var nested = detected.Where(d => d.RelativePath != ".").ToList();
    if (nested.Count > 0)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"  ⚠ {nested.Count} nested/hybrid project(s) detected — patterns applied globally.");
        Console.ResetColor();
    }

    // Check for existing .gitignore
    string gitignorePath = Path.Combine(giPath, ".gitignore");
    List<string>? existingPatterns = null;
    bool gitignoreExists = File.Exists(gitignorePath);

    if (gitignoreExists)
    {
        existingPatterns = GitignoreGenerator.ParseExistingGitignore(
            File.ReadAllText(gitignorePath));
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  Existing .gitignore found ({existingPatterns.Count} patterns).");
        Console.ResetColor();
    }

    string content;
    if (merge && gitignoreExists)
    {
        content = generator.Generate(detected, customPatterns, existingPatterns);
        if (string.IsNullOrEmpty(content))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[OK] Existing .gitignore already covers all detected patterns. Nothing to add.");
            Console.ResetColor();
            return 0;
        }
    }
    else
    {
        content = generator.Generate(detected, customPatterns);
    }

    if (preview)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();
        Console.WriteLine(content);
        Console.ResetColor();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n  [Preview mode — no file written]");
        Console.ResetColor();
        return 0;
    }

    // Safety: NEVER overwrite existing .gitignore
    string outputPath;
    if (gitignoreExists && !merge)
    {
        outputPath = Path.Combine(giPath, $".gitignore.generated_{DateTime.Now:yyyyMMdd_HHmmss}");
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  Existing .gitignore preserved. Writing to new file.");
        Console.ResetColor();
    }
    else if (merge && gitignoreExists)
    {
        // Append unique patterns to existing file
        string existing = File.ReadAllText(gitignorePath);
        content = existing.TrimEnd() + "\n\n" + content;
        outputPath = gitignorePath;
    }
    else
    {
        outputPath = gitignorePath;
    }

    SecurityGuard.AssertSafeGitignoreWrite(outputPath, giPath);
    File.WriteAllText(outputPath, content);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[OK] .gitignore saved to:");
    Console.WriteLine($"     {outputPath}");
    Console.ResetColor();

    if (autoOpen) OpenFile(outputPath);
    return 0;
}

// -- Command: open <file> -------------------------------------------------------
int openIdx = Array.IndexOf(args, "open");
if (openIdx == 0)
{
    if (args.Length < 2)
    {
        Error("File path required: mvc-inspect open <report-file>");
        return 2;
    }
    string target = args[1];
    if (!File.Exists(target)) { Error($"File not found: {target}"); return 2; }
    OpenFile(target);
    return 0;
}

// -- Command: inspect single project (default) ----------------------------------
string projectPath = args[0];

if (projectPath.StartsWith("--"))
{
    PrintHelp();
    return 1;
}

if (!Directory.Exists(projectPath))
{
    Error($"Path not found: {projectPath}");
    return 2;
}

// Auto output: <projectPath>\mvc-structure_yyyyMMdd_HHmmss.txt  (timestamped to prevent overwrite)
string autoOut = outFile ?? Path.Combine(Path.GetFullPath(projectPath),
    $"mvc-structure_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

var structParser = new StructureParser(options);
var snapshot = structParser.Parse(projectPath);

var inspector = new ProjectInspector(options);
string result = inspector.Inspect(projectPath);
WriteOutput(result, autoOut, autoOpen);

// Save machine-readable snapshot alongside the text report for future --from-report usage
string snapshotOut = Path.ChangeExtension(autoOut, null) + ".snapshot.json";
SnapshotSerializer.Save(snapshot, snapshotOut);
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"     Snapshot: {snapshotOut}");
Console.ResetColor();

return 0;

// -- Helpers --------------------------------------------------------------------

static void WriteOutput(string text, string outFile, bool openAfter)
{
    SecurityGuard.AssertSafeOutputPath(outFile);   // refuse protected extensions
    File.WriteAllText(outFile, text, Encoding.UTF8);
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[OK] Report saved to:");
    Console.WriteLine($"     {outFile}");
    Console.ResetColor();

    if (openAfter)
        OpenFile(outFile);
}

static void OpenFile(string path)
{
    try
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[>>] Opening: {path}");
        Console.ResetColor();

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.WriteLine($"[WARN] Could not open file automatically: {ex.Message}");
        Console.ResetColor();
    }
}

static void Error(string msg)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"[ERROR] {msg}");
    Console.ResetColor();
}

static void PrintHelp()
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("MVC Structure Inspector - ASP.NET Core MVC project analysis tool");
    Console.WriteLine("==================================================================");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  mvc-inspect <path>                                  Inspect one project  -> saves .txt + .snapshot.json");
    Console.WriteLine("  mvc-inspect --compare <pathA> <pathB>               Gap analysis between two live projects");
    Console.WriteLine("  mvc-inspect --from-report <report> <projectPath>    Compare a saved report snapshot against a live project");
    Console.WriteLine("  mvc-inspect gitignore <path>                        Generate .gitignore with smart project detection");
    Console.WriteLine("  mvc-inspect open <report-file>                      Open an existing report with the default viewer");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --out <file>        Override the auto output path");
    Console.WriteLine("  --open              Open the report automatically after saving");
    Console.WriteLine("  --with-proj         Include .csproj and .sln comparison (compare only)");
    Console.WriteLine("  --no-views          Exclude .cshtml files");
    Console.WriteLine("  --cs-only           C# files only");
    Console.WriteLine("  --no-migrations     Exclude Migrations folder");
    Console.WriteLine();
    Console.WriteLine("Gitignore Options:");
    Console.WriteLine("  --preview           Preview generated .gitignore without writing to disk");
    Console.WriteLine("  --merge             Merge with existing .gitignore (append unique patterns only)");
    Console.WriteLine("  --add <patterns>    Add custom patterns (e.g., --add \"*.log\" \"tmp/\" \"secrets/\")");
    Console.WriteLine();
    Console.WriteLine("Notes:");
    Console.WriteLine("  When inspecting a project, a .snapshot.json file is saved alongside the .txt report.");
    Console.WriteLine("  This snapshot can later be used with --from-report to track structural drift over time.");
    Console.WriteLine("  The gitignore command detects all project ecosystems (including nested/hybrid projects)");
    Console.WriteLine("  and NEVER overwrites an existing .gitignore — it creates a .gitignore.generated_* file instead.");
    Console.WriteLine("  Use --merge to safely append missing patterns to an existing .gitignore.");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  mvc-inspect C:\\source\\MyApp");
    Console.WriteLine("  mvc-inspect C:\\source\\MyApp --open");
    Console.WriteLine("  mvc-inspect C:\\source\\MyApp --out C:\\reports\\structure.txt --open");
    Console.WriteLine("  mvc-inspect --compare C:\\source\\RefApp C:\\source\\DevApp --open");
    Console.WriteLine("  mvc-inspect --compare C:\\source\\RefApp C:\\source\\DevApp --with-proj --open");
    Console.WriteLine("  mvc-inspect --from-report C:\\reports\\baseline.txt C:\\source\\DevApp --open");
    Console.WriteLine("  mvc-inspect --from-report C:\\reports\\baseline.snapshot.json C:\\source\\DevApp");
    Console.WriteLine("  mvc-inspect gitignore C:\\source\\MyApp");
    Console.WriteLine("  mvc-inspect gitignore C:\\source\\MyApp --preview");
    Console.WriteLine("  mvc-inspect gitignore C:\\source\\MyApp --merge");
    Console.WriteLine("  mvc-inspect gitignore C:\\source\\MyApp --add \"*.log\" \"secrets/\" --merge");
    Console.WriteLine("  mvc-inspect open C:\\source\\MyApp\\mvc-structure_20260306_064429.txt");
}
