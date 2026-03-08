using Xunit;

namespace MvcStructureInspector.Tests;

public class GitignoreGeneratorTests
{
    private readonly GitignoreGenerator _generator = new();

    // ── Project detection ───────────────────────────────────────────────────

    [Fact]
    public void Detects_dotnet_project_from_csproj()
    {
        var tmp = CreateTempProject("dotnet");
        try
        {
            File.WriteAllText(Path.Combine(tmp, "App.csproj"), "<Project />");
            var detected = _generator.DetectProjects(tmp);
            Assert.Contains(detected, d => d.Type == GitignoreGenerator.ProjectType.DotNet);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Detects_nodejs_project_from_package_json()
    {
        var tmp = CreateTempProject("node");
        try
        {
            File.WriteAllText(Path.Combine(tmp, "package.json"), "{}");
            var detected = _generator.DetectProjects(tmp);
            Assert.Contains(detected, d => d.Type == GitignoreGenerator.ProjectType.NodeJs);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Detects_python_project_from_requirements()
    {
        var tmp = CreateTempProject("python");
        try
        {
            File.WriteAllText(Path.Combine(tmp, "requirements.txt"), "flask");
            var detected = _generator.DetectProjects(tmp);
            Assert.Contains(detected, d => d.Type == GitignoreGenerator.ProjectType.Python);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Detects_rust_project_from_cargo()
    {
        var tmp = CreateTempProject("rust");
        try
        {
            File.WriteAllText(Path.Combine(tmp, "Cargo.toml"), "[package]");
            var detected = _generator.DetectProjects(tmp);
            Assert.Contains(detected, d => d.Type == GitignoreGenerator.ProjectType.Rust);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Detects_go_project_from_go_mod()
    {
        var tmp = CreateTempProject("go");
        try
        {
            File.WriteAllText(Path.Combine(tmp, "go.mod"), "module example.com/app");
            var detected = _generator.DetectProjects(tmp);
            Assert.Contains(detected, d => d.Type == GitignoreGenerator.ProjectType.Go);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Detects_java_project_from_pom_xml()
    {
        var tmp = CreateTempProject("java");
        try
        {
            File.WriteAllText(Path.Combine(tmp, "pom.xml"), "<project/>");
            var detected = _generator.DetectProjects(tmp);
            Assert.Contains(detected, d => d.Type == GitignoreGenerator.ProjectType.Java);
        }
        finally { Directory.Delete(tmp, true); }
    }

    // ── Nested / Hybrid project detection ────────────────────────────────────

    [Fact]
    public void Detects_nested_nodejs_inside_dotnet()
    {
        var tmp = CreateTempProject("hybrid");
        try
        {
            File.WriteAllText(Path.Combine(tmp, "App.csproj"), "<Project />");
            var clientDir = Path.Combine(tmp, "ClientApp");
            Directory.CreateDirectory(clientDir);
            File.WriteAllText(Path.Combine(clientDir, "package.json"), "{}");

            var detected = _generator.DetectProjects(tmp);
            Assert.Contains(detected, d => d.Type == GitignoreGenerator.ProjectType.DotNet);
            Assert.Contains(detected, d => d.Type == GitignoreGenerator.ProjectType.NodeJs);

            var nodeProject = detected.First(d => d.Type == GitignoreGenerator.ProjectType.NodeJs);
            Assert.NotEqual(".", nodeProject.RelativePath);
            Assert.Contains("ClientApp", nodeProject.RelativePath);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void Detects_multiple_ecosystems_in_monorepo()
    {
        var tmp = CreateTempProject("monorepo");
        try
        {
            File.WriteAllText(Path.Combine(tmp, "App.sln"), "");
            var apiDir = Path.Combine(tmp, "api");
            Directory.CreateDirectory(apiDir);
            File.WriteAllText(Path.Combine(apiDir, "Api.csproj"), "<Project />");

            var frontDir = Path.Combine(tmp, "frontend");
            Directory.CreateDirectory(frontDir);
            File.WriteAllText(Path.Combine(frontDir, "package.json"), "{}");

            var scriptsDir = Path.Combine(tmp, "scripts");
            Directory.CreateDirectory(scriptsDir);
            File.WriteAllText(Path.Combine(scriptsDir, "requirements.txt"), "boto3");

            var detected = _generator.DetectProjects(tmp);
            Assert.Contains(detected, d => d.Type == GitignoreGenerator.ProjectType.DotNet);
            Assert.Contains(detected, d => d.Type == GitignoreGenerator.ProjectType.NodeJs);
            Assert.Contains(detected, d => d.Type == GitignoreGenerator.ProjectType.Python);
        }
        finally { Directory.Delete(tmp, true); }
    }

    // ── Content generation ──────────────────────────────────────────────────

    [Fact]
    public void Generated_content_includes_header()
    {
        var detected = new List<GitignoreGenerator.DetectedProject>
        {
            new(GitignoreGenerator.ProjectType.DotNet, ".", "App.csproj"),
            new(GitignoreGenerator.ProjectType.MacOS,  ".", ".DS_Store"),
            new(GitignoreGenerator.ProjectType.Windows,".", "Thumbs.db"),
            new(GitignoreGenerator.ProjectType.Linux,  ".", "*~"),
        };

        var content = _generator.Generate(detected);
        Assert.Contains("Auto-generated by MVC Structure Inspector", content);
        Assert.Contains(".NET", content);
    }

    [Fact]
    public void Generated_content_includes_dotnet_patterns()
    {
        var detected = new List<GitignoreGenerator.DetectedProject>
        {
            new(GitignoreGenerator.ProjectType.DotNet, ".", "App.csproj"),
            new(GitignoreGenerator.ProjectType.MacOS,  ".", ".DS_Store"),
            new(GitignoreGenerator.ProjectType.Windows,".", "Thumbs.db"),
            new(GitignoreGenerator.ProjectType.Linux,  ".", "*~"),
        };

        var content = _generator.Generate(detected);
        Assert.Contains("[Bb]in/", content);
        Assert.Contains("[Oo]bj/", content);
        Assert.Contains(".vs/", content);
        Assert.Contains("*.nupkg", content);
    }

    [Fact]
    public void Generated_content_includes_nodejs_patterns()
    {
        var detected = new List<GitignoreGenerator.DetectedProject>
        {
            new(GitignoreGenerator.ProjectType.NodeJs, ".", "package.json"),
            new(GitignoreGenerator.ProjectType.MacOS,  ".", ".DS_Store"),
            new(GitignoreGenerator.ProjectType.Windows,".", "Thumbs.db"),
            new(GitignoreGenerator.ProjectType.Linux,  ".", "*~"),
        };

        var content = _generator.Generate(detected);
        Assert.Contains("node_modules/", content);
        Assert.Contains(".env", content);
    }

    [Fact]
    public void Generated_content_includes_os_patterns()
    {
        var detected = new List<GitignoreGenerator.DetectedProject>
        {
            new(GitignoreGenerator.ProjectType.MacOS,  ".", ".DS_Store"),
            new(GitignoreGenerator.ProjectType.Windows,".", "Thumbs.db"),
            new(GitignoreGenerator.ProjectType.Linux,  ".", "*~"),
        };

        var content = _generator.Generate(detected);
        Assert.Contains(".DS_Store", content);
        Assert.Contains("Thumbs.db", content);
    }

    [Fact]
    public void Custom_patterns_are_included()
    {
        var detected = new List<GitignoreGenerator.DetectedProject>
        {
            new(GitignoreGenerator.ProjectType.MacOS,  ".", ".DS_Store"),
            new(GitignoreGenerator.ProjectType.Windows,".", "Thumbs.db"),
            new(GitignoreGenerator.ProjectType.Linux,  ".", "*~"),
        };

        var custom = new[] { "*.log", "tmp/", "secrets/" };
        var content = _generator.Generate(detected, custom);
        Assert.Contains("*.log", content);
        Assert.Contains("tmp/", content);
        Assert.Contains("secrets/", content);
        Assert.Contains("Custom Patterns", content);
    }

    // ── Merge logic ─────────────────────────────────────────────────────────

    [Fact]
    public void Merge_excludes_existing_patterns()
    {
        var detected = new List<GitignoreGenerator.DetectedProject>
        {
            new(GitignoreGenerator.ProjectType.DotNet, ".", "App.csproj"),
            new(GitignoreGenerator.ProjectType.MacOS,  ".", ".DS_Store"),
            new(GitignoreGenerator.ProjectType.Windows,".", "Thumbs.db"),
            new(GitignoreGenerator.ProjectType.Linux,  ".", "*~"),
        };

        var existing = new List<string> { "[Bb]in/", "[Oo]bj/", ".vs/", ".DS_Store", "Thumbs.db" };
        var content = _generator.Generate(detected, null, existing);

        // Content should not be empty (there are more .NET patterns than what's in existing)
        Assert.NotEmpty(content);
        // Existing patterns should not appear as new
        Assert.DoesNotContain("\n[Bb]in/\n", "\n" + content + "\n");
    }

    [Fact]
    public void Merge_returns_empty_when_fully_covered()
    {
        var detected = new List<GitignoreGenerator.DetectedProject>
        {
            new(GitignoreGenerator.ProjectType.MacOS,  ".", ".DS_Store"),
            new(GitignoreGenerator.ProjectType.Windows,".", "Thumbs.db"),
            new(GitignoreGenerator.ProjectType.Linux,  ".", "*~"),
        };

        // Provide all OS patterns as existing
        var existing = GitignoreGenerator.ParseExistingGitignore(
            ".DS_Store\n.AppleDouble\n.LSOverride\nIcon?\n._*\n" +
            "Thumbs.db\nThumbs.db:encryptable\nehthumbs.db\nehthumbs_vista.db\n*.stackdump\n[Dd]esktop.ini\n" +
            "*~\n.fuse_hidden*\n.Trash-*\n.nfs*");

        var content = _generator.Generate(detected, null, existing);
        Assert.Empty(content);
    }

    [Fact]
    public void ParseExistingGitignore_skips_comments_and_blanks()
    {
        var input = "# comment\n\nbin/\nobj/\n\n# another comment\n*.log\n";
        var patterns = GitignoreGenerator.ParseExistingGitignore(input);
        Assert.Equal(3, patterns.Count);
        Assert.Contains("bin/", patterns);
        Assert.Contains("obj/", patterns);
        Assert.Contains("*.log", patterns);
    }

    // ── Nested project section ──────────────────────────────────────────────

    [Fact]
    public void Nested_projects_generate_awareness_section()
    {
        var detected = new List<GitignoreGenerator.DetectedProject>
        {
            new(GitignoreGenerator.ProjectType.DotNet, ".", "App.csproj"),
            new(GitignoreGenerator.ProjectType.NodeJs, "ClientApp", "package.json"),
            new(GitignoreGenerator.ProjectType.MacOS,  ".", ".DS_Store"),
            new(GitignoreGenerator.ProjectType.Windows,".", "Thumbs.db"),
            new(GitignoreGenerator.ProjectType.Linux,  ".", "*~"),
        };

        var content = _generator.Generate(detected);
        Assert.Contains("Nested / Hybrid Project Awareness", content);
        Assert.Contains("ClientApp", content);
    }

    // ── SecurityGuard gitignore checks ──────────────────────────────────────

    [Fact]
    public void AssertSafeGitignoreWrite_allows_gitignore_in_project()
    {
        var tmp = CreateTempProject("sg-gitignore");
        try
        {
            var path = Path.Combine(tmp, ".gitignore");
            var ex = Record.Exception(() => SecurityGuard.AssertSafeGitignoreWrite(path, tmp));
            Assert.Null(ex);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void AssertSafeGitignoreWrite_allows_generated_file()
    {
        var tmp = CreateTempProject("sg-generated");
        try
        {
            var path = Path.Combine(tmp, ".gitignore.generated_20260308_120000");
            var ex = Record.Exception(() => SecurityGuard.AssertSafeGitignoreWrite(path, tmp));
            Assert.Null(ex);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void AssertSafeGitignoreWrite_rejects_outside_project()
    {
        var tmp = CreateTempProject("sg-outside");
        try
        {
            var outsidePath = Path.Combine(Path.GetTempPath(), ".gitignore");
            Assert.Throws<InvalidOperationException>(
                () => SecurityGuard.AssertSafeGitignoreWrite(outsidePath, tmp));
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void AssertSafeGitignoreWrite_rejects_invalid_filename()
    {
        var tmp = CreateTempProject("sg-invalid");
        try
        {
            var path = Path.Combine(tmp, "important.cs");
            Assert.Throws<InvalidOperationException>(
                () => SecurityGuard.AssertSafeGitignoreWrite(path, tmp));
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void IsGitignoreFile_validates_filenames()
    {
        Assert.True(SecurityGuard.IsGitignoreFile(".gitignore"));
        Assert.True(SecurityGuard.IsGitignoreFile(".gitignore.generated_20260308_120000"));
        Assert.False(SecurityGuard.IsGitignoreFile("Program.cs"));
        Assert.False(SecurityGuard.IsGitignoreFile(".gitignore.bak"));
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    private static string CreateTempProject(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mvc-inspect-test-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
