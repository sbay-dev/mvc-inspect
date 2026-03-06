namespace MvcStructureInspector;

/// <summary>
/// Centralises all output-path security checks for the CLI tool.
/// All members are public so they can be verified by the security test suite.
/// </summary>
public static class SecurityGuard
{
    /// <summary>File extensions that must never be overwritten by the tool's --out flag.</summary>
    public static readonly IReadOnlySet<string> ProtectedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".sln", ".csproj", ".cs", ".cshtml", ".json", ".config",
            ".xml", ".dll", ".exe", ".key", ".pfx", ".pem", ".p12",
            ".db", ".sqlite", ".sqlite3", ".resx", ".razor"
        };

    /// <summary>Returns <c>true</c> when the given extension is on the protected list.</summary>
    public static bool IsProtectedExtension(string ext) =>
        ProtectedExtensions.Contains(ext);

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if <paramref name="path"/> has a
    /// protected extension that the tool must not overwrite.
    /// Snapshot JSON files (*.snapshot.json) produced by this tool are explicitly allowed.
    /// </summary>
    public static void AssertSafeOutputPath(string path)
    {
        if (IsSnapshotFile(path))
            return;   // tool's own machine-readable format — always safe

        string ext = Path.GetExtension(path);
        if (IsProtectedExtension(ext))
            throw new InvalidOperationException(
                $"[ERROR] Refusing to overwrite a '{ext}' file: {path}\n" +
                $"        Use a .txt or .md output file instead.");
    }

    /// <summary>Returns <c>true</c> when the file is a tool-generated snapshot.</summary>
    public static bool IsSnapshotFile(string path) =>
        path.EndsWith(".snapshot.json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="filename"/> matches the timestamped
    /// auto-save pattern produced by the tool (prevents silent overwrites).
    /// </summary>
    public static bool IsTimestampedReport(string filename)
    {
        var name = Path.GetFileNameWithoutExtension(filename);
        return (name.StartsWith("mvc-structure_") || name.StartsWith("mvc-gap-report_"))
            && System.Text.RegularExpressions.Regex.IsMatch(
                name, @"_\d{8}_\d{6}$");
    }
}
