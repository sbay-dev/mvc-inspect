using Xunit;

namespace MvcStructureInspector.Tests;

/// <summary>
/// Tests for SnapshotSerializer: roundtrip fidelity and ResolveSnapshotPath logic.
/// </summary>
public class SnapshotSerializerTests
{
    // ── Roundtrip serialization ──────────────────────────────────────────────

    [Fact]
    public void Roundtrip_preserves_cs_file_structure()
    {
        var dir  = TempDir();
        File.WriteAllText(Path.Combine(dir, "A.cs"),
            "namespace MyApp; public class Foo { public void Bar() {} }");

        var parser = new StructureParser(new InspectorOptions { IncludeViews = false });
        var original = parser.Parse(dir);

        string jsonPath = Path.Combine(dir, "test.snapshot.json");
        SnapshotSerializer.Save(original, jsonPath);
        var loaded = SnapshotSerializer.Load(jsonPath);

        Assert.Equal(original.ProjectName, loaded.ProjectName);
        Assert.Equal(original.Files.Count, loaded.Files.Count);
        Assert.Equal(original.Files[0].Namespaces[0].Name, loaded.Files[0].Namespaces[0].Name);
        Assert.Equal(original.Files[0].Namespaces[0].Types[0].Name,
                     loaded.Files[0].Namespaces[0].Types[0].Name);
    }

    [Fact]
    public void Roundtrip_preserves_razor_files()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "Views"));
        File.WriteAllText(Path.Combine(dir, "Views", "Index.cshtml"),
            "@model MyModel\n@inject IService Svc\n<h1>Hello</h1>");

        var parser = new StructureParser(new InspectorOptions { IncludeViews = true });
        var original = parser.Parse(dir);

        string jsonPath = Path.Combine(dir, "test.snapshot.json");
        SnapshotSerializer.Save(original, jsonPath);
        var loaded = SnapshotSerializer.Load(jsonPath);

        Assert.Equal(original.RazorFiles.Count, loaded.RazorFiles.Count);
        Assert.Equal(original.RazorFiles[0].ModelType, loaded.RazorFiles[0].ModelType);
        Assert.Equal(original.RazorFiles[0].Injects.Count, loaded.RazorFiles[0].Injects.Count);
    }

    [Fact]
    public void Roundtrip_preserves_static_files()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "wwwroot", "css"));
        File.WriteAllText(Path.Combine(dir, "wwwroot", "css", "site.css"), "body { color: red; }");

        var parser = new StructureParser(new InspectorOptions { IncludeViews = false });
        var original = parser.Parse(dir);

        string jsonPath = Path.Combine(dir, "test.snapshot.json");
        SnapshotSerializer.Save(original, jsonPath);
        var loaded = SnapshotSerializer.Load(jsonPath);

        Assert.Equal(original.StaticFiles.Count, loaded.StaticFiles.Count);
        Assert.Equal(original.StaticFiles[0].ContentHash, loaded.StaticFiles[0].ContentHash);
        Assert.Equal(original.StaticFiles[0].SizeBytes, loaded.StaticFiles[0].SizeBytes);
    }

    [Fact]
    public void Roundtrip_preserves_csproj_and_sln()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "A.cs"), "public class A {}");
        File.WriteAllText(Path.Combine(dir, "App.csproj"),
            @"<Project Sdk=""Microsoft.NET.Sdk"">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
              <ItemGroup><PackageReference Include=""Serilog"" Version=""3.1.1"" /></ItemGroup>
            </Project>");

        var parser = new StructureParser(new InspectorOptions
            { IncludeViews = false, IncludeProjectFiles = true });
        var original = parser.Parse(dir);

        string jsonPath = Path.Combine(dir, "test.snapshot.json");
        SnapshotSerializer.Save(original, jsonPath);
        var loaded = SnapshotSerializer.Load(jsonPath);

        Assert.Equal(original.CsprojFiles.Count, loaded.CsprojFiles.Count);
        Assert.Equal("Serilog", loaded.CsprojFiles[0].PackageReferences[0].Name);
    }

    // ── Comparison: saved snapshot vs live project ───────────────────────────

    [Fact]
    public void Snapshot_vs_live_detects_missing_method()
    {
        // Create original project with two methods
        var dirA = TempDir();
        File.WriteAllText(Path.Combine(dirA, "A.cs"),
            "public class A { public void Foo() {} public void Bar() {} }");

        var parser = new StructureParser(new InspectorOptions { IncludeViews = false });
        var snapA = parser.Parse(dirA);

        string jsonPath = Path.Combine(dirA, "baseline.snapshot.json");
        SnapshotSerializer.Save(snapA, jsonPath);

        // Create "live" project with only one method
        var dirB = TempDir();
        File.WriteAllText(Path.Combine(dirB, "A.cs"),
            "public class A { public void Foo() {} }");

        var loadedA = SnapshotSerializer.Load(jsonPath);
        var snapB   = parser.Parse(dirB);

        var (_, _, _, _, _, summary) = new ComparisonEngine().Compare(loadedA, snapB);

        Assert.True(summary.MembersOnlyInA >= 1,
            "Method 'Bar' in saved snapshot should be reported missing from live project.");
    }

    [Fact]
    public void Snapshot_vs_live_detects_static_file_change()
    {
        var dirA = TempDir();
        File.WriteAllText(Path.Combine(dirA, "A.cs"), "public class A {}");
        Directory.CreateDirectory(Path.Combine(dirA, "wwwroot"));
        File.WriteAllText(Path.Combine(dirA, "wwwroot", "app.js"), "// v1");

        var parser = new StructureParser(new InspectorOptions { IncludeViews = false });
        var snapA = parser.Parse(dirA);

        string jsonPath = Path.Combine(dirA, "baseline.snapshot.json");
        SnapshotSerializer.Save(snapA, jsonPath);

        // Modify the static file in "live" project
        var dirB = TempDir();
        File.WriteAllText(Path.Combine(dirB, "A.cs"), "public class A {}");
        Directory.CreateDirectory(Path.Combine(dirB, "wwwroot"));
        File.WriteAllText(Path.Combine(dirB, "wwwroot", "app.js"), "// v2 — changed!");

        var loadedA = SnapshotSerializer.Load(jsonPath);
        var snapB   = parser.Parse(dirB);

        var (_, _, staticDiffs, _, _, summary) = new ComparisonEngine().Compare(loadedA, snapB);

        Assert.True(summary.StaticModified >= 1);
        Assert.True(staticDiffs.Any(d => d.Status == DiffStatus.Modified));
    }

    // ── ResolveSnapshotPath ─────────────────────────────────────────────────

    [Fact]
    public void ResolveSnapshotPath_from_json_returns_self()
    {
        var dir = TempDir();
        string jsonPath = Path.Combine(dir, "report.snapshot.json");
        File.WriteAllText(jsonPath, "{}");

        string resolved = SnapshotSerializer.ResolveSnapshotPath(jsonPath);
        Assert.Equal(jsonPath, resolved);
    }

    [Fact]
    public void ResolveSnapshotPath_from_txt_finds_sibling_json()
    {
        var dir = TempDir();
        string txtPath  = Path.Combine(dir, "report.txt");
        string jsonPath = Path.Combine(dir, "report.snapshot.json");
        File.WriteAllText(txtPath, "");
        File.WriteAllText(jsonPath, "{}");

        string resolved = SnapshotSerializer.ResolveSnapshotPath(txtPath);
        Assert.Equal(jsonPath, resolved);
    }

    [Fact]
    public void ResolveSnapshotPath_throws_when_no_json_exists()
    {
        var dir = TempDir();
        string txtPath = Path.Combine(dir, "report.txt");
        File.WriteAllText(txtPath, "");

        Assert.Throws<FileNotFoundException>(() =>
            SnapshotSerializer.ResolveSnapshotPath(txtPath));
    }

    [Fact]
    public void Load_throws_on_missing_file()
    {
        Assert.Throws<FileNotFoundException>(() =>
            SnapshotSerializer.Load(@"C:\nonexistent\file.snapshot.json"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mvc-inspect-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
