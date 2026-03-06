using Xunit;

namespace MvcStructureInspector.Tests;

/// <summary>
/// Verifies that ProjectFileParser correctly parses .csproj and .sln files
/// and handles edge cases (missing files, malformed XML) gracefully.
/// </summary>
public class ProjectFileParserTests
{
    // ── .csproj — empty directory ─────────────────────────────────────────────

    [Fact]
    public void ParseCsprojFiles_returns_empty_for_dir_with_no_csproj()
    {
        var dir = TempDir();
        var result = ProjectFileParser.ParseCsprojFiles(dir);
        Assert.Empty(result);
    }

    // ── .csproj — SDK and framework ──────────────────────────────────────────

    [Fact]
    public void ParseCsprojFiles_extracts_sdk_and_framework()
    {
        var dir = TempDir();
        WriteCsproj(dir, "App.csproj", @"
<Project Sdk=""Microsoft.NET.Sdk.Web"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
</Project>");

        var results = ProjectFileParser.ParseCsprojFiles(dir);
        Assert.Single(results);
        var p = results[0];
        Assert.Equal("Microsoft.NET.Sdk.Web", p.Sdk);
        Assert.Contains("net8.0", p.TargetFrameworks);
        Assert.Equal("enable", p.Nullable);
        Assert.Equal("enable", p.ImplicitUsings);
        Assert.Equal("Exe", p.OutputType);
    }

    // ── .csproj — multi-target ────────────────────────────────────────────────

    [Fact]
    public void ParseCsprojFiles_handles_multiple_target_frameworks()
    {
        var dir = TempDir();
        WriteCsproj(dir, "Lib.csproj", @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net6.0;netstandard2.1</TargetFrameworks>
  </PropertyGroup>
</Project>");

        var p = ProjectFileParser.ParseCsprojFiles(dir)[0];
        Assert.Equal(3, p.TargetFrameworks.Count);
        Assert.Contains("net8.0", p.TargetFrameworks);
        Assert.Contains("net6.0", p.TargetFrameworks);
        Assert.Contains("netstandard2.1", p.TargetFrameworks);
    }

    // ── .csproj — PackageReferences ──────────────────────────────────────────

    [Fact]
    public void ParseCsprojFiles_extracts_package_references()
    {
        var dir = TempDir();
        WriteCsproj(dir, "App.csproj", @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <PackageReference Include=""Serilog""              Version=""3.1.1"" />
    <PackageReference Include=""Microsoft.EntityFrameworkCore"" Version=""8.0.0"" />
    <PackageReference Include=""Dapper""               Version=""2.1.35"" />
  </ItemGroup>
</Project>");

        var pkgs = ProjectFileParser.ParseCsprojFiles(dir)[0].PackageReferences;
        Assert.Equal(3, pkgs.Count);
        Assert.Contains(pkgs, p => p.Name == "Serilog" && p.Version == "3.1.1");
        Assert.Contains(pkgs, p => p.Name == "Microsoft.EntityFrameworkCore" && p.Version == "8.0.0");
    }

    // ── .csproj — ProjectReferences ──────────────────────────────────────────

    [Fact]
    public void ParseCsprojFiles_extracts_project_references()
    {
        var dir = TempDir();
        WriteCsproj(dir, "App.csproj", @"
<Project Sdk=""Microsoft.NET.Sdk"">
  <ItemGroup>
    <ProjectReference Include=""..\SharedLib\SharedLib.csproj"" />
    <ProjectReference Include=""..\Common\Common.csproj"" />
  </ItemGroup>
</Project>");

        var refs = ProjectFileParser.ParseCsprojFiles(dir)[0].ProjectReferences;
        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r => r.Contains("SharedLib"));
    }

    // ── .csproj — malformed XML ───────────────────────────────────────────────

    [Fact]
    public void ParseCsprojFiles_handles_malformed_xml_gracefully()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "Bad.csproj"), "<<< THIS IS NOT VALID XML >>>");

        var results = ProjectFileParser.ParseCsprojFiles(dir);
        Assert.Single(results);
        // Should return a default (empty) record — no exception thrown
        Assert.Equal("", results[0].Sdk);
        Assert.Empty(results[0].TargetFrameworks);
    }

    // ── .csproj — bin/obj exclusion ──────────────────────────────────────────

    [Fact]
    public void ParseCsprojFiles_ignores_csproj_inside_bin_and_obj()
    {
        var dir = TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "bin"));
        Directory.CreateDirectory(Path.Combine(dir, "obj"));

        File.WriteAllText(Path.Combine(dir, "bin", "Published.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        File.WriteAllText(Path.Combine(dir, "obj", "Temp.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        Assert.Empty(ProjectFileParser.ParseCsprojFiles(dir));
    }

    // ── .sln — no file ───────────────────────────────────────────────────────

    [Fact]
    public void ParseSln_returns_null_when_no_sln_file_exists()
    {
        var dir = TempDir();
        Assert.Null(ProjectFileParser.ParseSln(dir));
    }

    // ── .sln — project extraction ────────────────────────────────────────────

    [Fact]
    public void ParseSln_extracts_all_registered_projects()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "Solution.sln"), """

            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MyApp", "src\MyApp.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "SharedLib", "lib\SharedLib.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Solution Items", "Solution Items", "{33333333-3333-3333-3333-333333333333}"
            EndProject
            """);

        var sln = ProjectFileParser.ParseSln(dir);
        Assert.NotNull(sln);
        Assert.Equal(3, sln.Projects.Count);
        Assert.Contains(sln.Projects, p => p.Name == "MyApp");
        Assert.Contains(sln.Projects, p => p.Name == "SharedLib");
        Assert.Contains(sln.Projects, p => p.Name == "Solution Items");
    }

    [Fact]
    public void ParseSln_normalises_backslashes_to_forward_in_paths()
    {
        var dir = TempDir();
        File.WriteAllText(Path.Combine(dir, "App.sln"),
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Core\", \"src\\Core\\Core.csproj\", \"{11111111-0000-0000-0000-000000000001}\"");

        var proj = ProjectFileParser.ParseSln(dir)!.Projects[0];
        Assert.DoesNotContain("\\", proj.Path);
        Assert.Contains("/", proj.Path);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mvc-inspect-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteCsproj(string dir, string name, string xml) =>
        File.WriteAllText(Path.Combine(dir, name), xml.TrimStart());
}
