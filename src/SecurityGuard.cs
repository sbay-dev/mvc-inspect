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
    /// Validates that a gitignore output path is safe to write.
    /// Only allows writing .gitignore or .gitignore.generated_* files
    /// within the target project directory.
    /// </summary>
    public static void AssertSafeGitignoreWrite(string outputPath, string projectRoot)
    {
        var fullOutput  = Path.GetFullPath(outputPath);
        var fullRoot    = Path.GetFullPath(projectRoot);
        var fileName    = Path.GetFileName(fullOutput);

        // Must be inside the project directory
        if (!fullOutput.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"[ERROR] Refusing to write outside project directory.\n" +
                $"        Output: {fullOutput}\n" +
                $"        Project: {fullRoot}");

        // Only .gitignore or .gitignore.generated_* filenames are allowed
        if (!IsGitignoreFile(fileName))
            throw new InvalidOperationException(
                $"[ERROR] Invalid gitignore output filename: {fileName}\n" +
                $"        Only .gitignore or .gitignore.generated_* are allowed.");
    }

    /// <summary>Returns <c>true</c> when the filename is a valid gitignore output name.</summary>
    public static bool IsGitignoreFile(string fileName) =>
        fileName.Equals(".gitignore", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith(".gitignore.generated_", StringComparison.OrdinalIgnoreCase);

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
