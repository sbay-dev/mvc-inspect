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

var inspector = new ProjectInspector(options);
string result = inspector.Inspect(projectPath);
WriteOutput(result, autoOut, autoOpen);
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
    Console.WriteLine("  mvc-inspect <path>                      Inspect one project  -> saves mvc-structure_<timestamp>.txt inside <path>");
    Console.WriteLine("  mvc-inspect --compare <pathA> <pathB>   Gap analysis         -> saves mvc-gap-report_<timestamp>.txt inside <pathB>");
    Console.WriteLine("  mvc-inspect open <report-file>          Open an existing report with the default viewer");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --out <file>        Override the auto output path");
    Console.WriteLine("  --open              Open the report automatically after saving");
    Console.WriteLine("  --with-proj         Include .csproj and .sln comparison (compare only)");
    Console.WriteLine("  --no-views          Exclude .cshtml files");
    Console.WriteLine("  --cs-only           C# files only");
    Console.WriteLine("  --no-migrations     Exclude Migrations folder");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  mvc-inspect C:\\source\\MyApp");
    Console.WriteLine("  mvc-inspect C:\\source\\MyApp --open");
    Console.WriteLine("  mvc-inspect C:\\source\\MyApp --out C:\\reports\\structure.txt --open");
    Console.WriteLine("  mvc-inspect --compare C:\\source\\RefApp C:\\source\\DevApp --open");
    Console.WriteLine("  mvc-inspect --compare C:\\source\\RefApp C:\\source\\DevApp --with-proj --open");
    Console.WriteLine("  mvc-inspect open C:\\source\\MyApp\\mvc-structure_20260306_064429.txt");
}
