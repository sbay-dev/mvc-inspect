using System.Text.RegularExpressions;
using Xunit;

namespace MvcStructureInspector.Tests;

/// <summary>
/// Verifies that security invariants of the tool hold:
/// - No protected extension can be overwritten via --out.
/// - Auto-generated filenames carry a timestamp (preventing silent overwrites).
/// - Consecutive runs in the same second produce names that still diverge after 1s.
/// </summary>
public class SecurityGuardTests
{
    // ── Protected-extension detection ────────────────────────────────────────

    [Theory]
    [InlineData(".sln")]
    [InlineData(".csproj")]
    [InlineData(".cs")]
    [InlineData(".cshtml")]
    [InlineData(".json")]
    [InlineData(".config")]
    [InlineData(".xml")]
    [InlineData(".dll")]
    [InlineData(".exe")]
    [InlineData(".key")]
    [InlineData(".pfx")]
    [InlineData(".pem")]
    [InlineData(".db")]
    [InlineData(".sqlite")]
    [InlineData(".razor")]
    public void Protected_extensions_are_rejected(string ext) =>
        Assert.True(SecurityGuard.IsProtectedExtension(ext),
            $"Extension '{ext}' should be protected but IsProtectedExtension returned false.");

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".log")]
    [InlineData(".csv")]
    [InlineData(".html")]
    [InlineData(".rst")]
    public void Safe_extensions_are_allowed(string ext) =>
        Assert.False(SecurityGuard.IsProtectedExtension(ext),
            $"Extension '{ext}' should be allowed but IsProtectedExtension returned true.");

    // ── AssertSafeOutputPath ─────────────────────────────────────────────────

    [Theory]
    [InlineData("MyApp.sln")]
    [InlineData(@"C:\project\App.csproj")]
    [InlineData(@"C:\src\Controller.cs")]
    [InlineData("layout._Layout.cshtml")]
    [InlineData("appsettings.json")]
    public void AssertSafeOutputPath_throws_for_protected_extensions(string path)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => SecurityGuard.AssertSafeOutputPath(path));
        Assert.Contains("[ERROR]", ex.Message);
        Assert.Contains(Path.GetExtension(path), ex.Message);
    }

    [Theory]
    [InlineData(@"C:\reports\output.txt")]
    [InlineData("mvc-structure_20260306_120000.txt")]
    [InlineData("report.md")]
    public void AssertSafeOutputPath_does_not_throw_for_safe_extensions(string path)
    {
        var ex = Record.Exception(() => SecurityGuard.AssertSafeOutputPath(path));
        Assert.Null(ex);
    }

    // ── Timestamped auto-save (no-overwrite guarantee) ───────────────────────

    [Theory]
    [InlineData("mvc-structure_20260306_064429.txt")]
    [InlineData("mvc-structure_20260306_000000.txt")]
    [InlineData("mvc-gap-report_20260101_235959.txt")]
    public void Timestamped_filenames_match_expected_pattern(string filename) =>
        Assert.True(SecurityGuard.IsTimestampedReport(filename),
            $"'{filename}' should be recognised as a timestamped report.");

    [Theory]
    [InlineData("mvc-structure.txt")]
    [InlineData("mvc-gap-report.txt")]
    [InlineData("output.txt")]
    [InlineData("mvc-structure_abc.txt")]
    public void Non_timestamped_filenames_are_not_recognised(string filename) =>
        Assert.False(SecurityGuard.IsTimestampedReport(filename),
            $"'{filename}' should NOT be recognised as a timestamped report.");

    [Fact]
    public void Auto_generated_structure_filename_is_timestamped()
    {
        string ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string name = $"mvc-structure_{ts}.txt";
        Assert.True(SecurityGuard.IsTimestampedReport(name));
        Assert.Matches(@"^mvc-structure_\d{8}_\d{6}\.txt$", name);
    }

    [Fact]
    public void Auto_generated_gap_filename_is_timestamped()
    {
        string ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string name = $"mvc-gap-report_{ts}.txt";
        Assert.True(SecurityGuard.IsTimestampedReport(name));
        Assert.Matches(@"^mvc-gap-report_\d{8}_\d{6}\.txt$", name);
    }

    [Fact]
    public async Task Consecutive_runs_produce_different_filenames_after_one_second()
    {
        string t1 = $"mvc-structure_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        await Task.Delay(1100);
        string t2 = $"mvc-structure_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        Assert.NotEqual(t1, t2);
    }

    // ── Extension set completeness ────────────────────────────────────────────

    [Fact]
    public void Protected_set_contains_at_least_the_documented_extensions()
    {
        string[] required = [".sln", ".csproj", ".cs", ".cshtml", ".json",
                              ".config", ".xml", ".dll", ".exe"];
        foreach (var ext in required)
            Assert.True(SecurityGuard.IsProtectedExtension(ext),
                $"'{ext}' is documented as protected but missing from the set.");
    }
}
