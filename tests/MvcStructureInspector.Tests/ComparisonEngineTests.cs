using Xunit;

namespace MvcStructureInspector.Tests;

/// <summary>
/// Verifies that ComparisonEngine produces correct results — including the
/// critical guarantee that comparing a project with itself yields zero gaps.
/// </summary>
public class ComparisonEngineTests
{
    private static readonly InspectorOptions NoViews = new()
        { IncludeViews = false, IncludeMigrations = false };

    // ── Self-comparison ───────────────────────────────────────────────────────

    [Fact]
    public void Self_comparison_yields_zero_cs_gaps()
    {
        var dir  = TempProject("Test.cs", "namespace T; public class A { public void M() {} }");
        var snap = new StructureParser(NoViews).Parse(dir);
        var (diffs, _, _, _, summary) = new ComparisonEngine().Compare(snap, snap);

        Assert.Equal(0, summary.FilesOnlyInA);
        Assert.Equal(0, summary.FilesOnlyInB);
        Assert.Equal(0, summary.TypesOnlyInA);
        Assert.Equal(0, summary.TypesOnlyInB);
        Assert.Empty(diffs);
    }

    [Fact]
    public void Self_comparison_yields_zero_csproj_gaps()
    {
        var dir = TempProject("Test.cs", "public class X {}");
        File.WriteAllText(Path.Combine(dir, "Test.csproj"),
            @"<Project Sdk=""Microsoft.NET.Sdk"">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>");

        var opts = new InspectorOptions { IncludeViews = false, IncludeProjectFiles = true };
        var snap = new StructureParser(opts).Parse(dir);
        var (_, _, projDiffs, _, summary) = new ComparisonEngine().Compare(snap, snap);

        Assert.Equal(0, summary.ProjOnlyInA);
        Assert.Equal(0, summary.ProjOnlyInB);
        Assert.Equal(0, summary.ProjDiffsCount);
        Assert.Empty(projDiffs.Where(p => p.Status != DiffStatus.Identical));
    }

    // ── Missing / extra file detection ───────────────────────────────────────

    [Fact]
    public void Missing_file_is_reported()
    {
        var dirA = TempProject("Only_In_A.cs", "public class OnlyA {}");
        var dirB = TempProject("Only_In_B.cs", "public class OnlyB {}");

        var parser = new StructureParser(NoViews);
        var (diffs, _, _, _, summary) = new ComparisonEngine()
            .Compare(parser.Parse(dirA), parser.Parse(dirB));

        Assert.True(summary.FilesOnlyInA >= 1, "File present in A must be reported as missing in B.");
        Assert.True(diffs.Any(d => d.Status == DiffStatus.Missing));
    }

    [Fact]
    public void Extra_file_is_reported()
    {
        var dirA = TempProject("A.cs", "public class A {}");
        var dirB = TempProject("A.cs", "public class A {}");
        // Add an extra file to B
        File.WriteAllText(Path.Combine(dirB, "Extra.cs"), "public class Extra {}");

        var parser = new StructureParser(NoViews);
        var (diffs, _, _, _, summary) = new ComparisonEngine()
            .Compare(parser.Parse(dirA), parser.Parse(dirB));

        Assert.True(summary.FilesOnlyInB >= 1);
        Assert.True(diffs.Any(d => d.Status == DiffStatus.Extra));
    }

    // ── Member-level detection ────────────────────────────────────────────────

    [Fact]
    public void Missing_method_is_reported()
    {
        var dirA = TempProject("A.cs",
            "public class A { public void Foo() {} public void Bar() {} }");
        var dirB = TempProject("A.cs",
            "public class A { public void Foo() {} }");   // Bar missing in B

        var parser = new StructureParser(NoViews);
        var (diffs, _, _, _, summary) = new ComparisonEngine()
            .Compare(parser.Parse(dirA), parser.Parse(dirB));

        Assert.True(summary.MembersOnlyInA >= 1,
            "Method 'Bar' exists in A but not in B — should be counted as missing.");
    }

    // ── .csproj package diff detection ───────────────────────────────────────

    [Fact]
    public void Package_version_change_is_detected()
    {
        var dirA = TempProject("A.cs", "public class A {}");
        var dirB = TempProject("A.cs", "public class A {}");

        File.WriteAllText(Path.Combine(dirA, "App.csproj"),
            @"<Project Sdk=""Microsoft.NET.Sdk"">
              <ItemGroup><PackageReference Include=""Serilog"" Version=""3.1.1"" /></ItemGroup>
            </Project>");
        File.WriteAllText(Path.Combine(dirB, "App.csproj"),
            @"<Project Sdk=""Microsoft.NET.Sdk"">
              <ItemGroup><PackageReference Include=""Serilog"" Version=""2.12.0"" /></ItemGroup>
            </Project>");

        var opts = new InspectorOptions { IncludeViews = false, IncludeProjectFiles = true };
        var parser = new StructureParser(opts);
        var (_, _, projDiffs, _, _) = new ComparisonEngine()
            .Compare(parser.Parse(dirA), parser.Parse(dirB));

        var modifiedProj = projDiffs.FirstOrDefault(p => p.Status == DiffStatus.Modified);
        Assert.NotNull(modifiedProj);

        var pkgDiff = modifiedProj.PropertyDiffs
            .FirstOrDefault(d => d.Category == "PackageRef" && d.Name == "Serilog");
        Assert.NotNull(pkgDiff);
        Assert.Equal(DiffStatus.Modified, pkgDiff.Status);
        Assert.Contains("3.1.1", pkgDiff.ValueA);
        Assert.Contains("2.12.0", pkgDiff.ValueB);
    }

    // ── .sln project list diff ────────────────────────────────────────────────

    [Fact]
    public void Sln_missing_project_is_detected()
    {
        var dirA = TempProject("A.cs", "public class A {}");
        var dirB = TempProject("A.cs", "public class A {}");

        File.WriteAllText(Path.Combine(dirA, "A.sln"),
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Core\", \"src/Core.csproj\", \"{11111111-0000-0000-0000-000000000001}\"");
        File.WriteAllText(Path.Combine(dirB, "A.sln"), "");  // empty sln — Core project missing

        var opts = new InspectorOptions { IncludeViews = false, IncludeProjectFiles = true };
        var parser = new StructureParser(opts);
        var (_, _, _, slnDiff, summary) = new ComparisonEngine()
            .Compare(parser.Parse(dirA), parser.Parse(dirB));

        Assert.NotNull(slnDiff);
        Assert.True(summary.SlnProjectsOnlyInA >= 1);
        Assert.True(slnDiff.ProjectDiffs.Any(p => p.Status == DiffStatus.Missing && p.Name == "Core"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string TempProject(string fileName, string content)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mvc-inspect-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
        return dir;
    }
}
