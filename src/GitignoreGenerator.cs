namespace MvcStructureInspector;

/// <summary>
/// Detects project types within a directory tree (including nested/hybrid projects)
/// and generates a comprehensive .gitignore file with merged patterns.
/// </summary>
public sealed class GitignoreGenerator
{
    // ── Project-type detection ──────────────────────────────────────────────

    /// <summary>Identifies a project ecosystem detected at a specific path.</summary>
    public record DetectedProject(ProjectType Type, string RelativePath, string Indicator);

    public enum ProjectType
    {
        DotNet,
        NodeJs,
        Python,
        Rust,
        Go,
        Java,
        Ruby,
        Php,
        Swift,
        DartFlutter,
        Unity,
        Terraform,
        Docker,
        VisualStudio,
        JetBrains,
        MacOS,
        Windows,
        Linux
    }

    /// <summary>Indicators used to detect each project type.</summary>
    private static readonly (ProjectType Type, string[] Indicators)[] DetectionRules =
    [
        (ProjectType.DotNet,       ["*.csproj", "*.sln", "*.fsproj", "*.vbproj", "global.json"]),
        (ProjectType.NodeJs,       ["package.json"]),
        (ProjectType.Python,       ["requirements.txt", "setup.py", "pyproject.toml", "Pipfile", "setup.cfg"]),
        (ProjectType.Rust,         ["Cargo.toml"]),
        (ProjectType.Go,           ["go.mod"]),
        (ProjectType.Java,         ["pom.xml", "build.gradle", "build.gradle.kts", "settings.gradle"]),
        (ProjectType.Ruby,         ["Gemfile"]),
        (ProjectType.Php,          ["composer.json"]),
        (ProjectType.Swift,        ["Package.swift", "*.xcodeproj", "*.xcworkspace"]),
        (ProjectType.DartFlutter,  ["pubspec.yaml"]),
        (ProjectType.Unity,        ["ProjectSettings/ProjectVersion.txt"]),
        (ProjectType.Terraform,    ["*.tf"]),
        (ProjectType.Docker,       ["Dockerfile", "docker-compose.yml", "docker-compose.yaml"]),
        (ProjectType.VisualStudio, [".vs/"]),
        (ProjectType.JetBrains,    [".idea/"]),
    ];

    /// <summary>
    /// Scans the given root directory (and subdirectories) for all project types.
    /// Returns a list of detected projects with their relative paths.
    /// </summary>
    public List<DetectedProject> DetectProjects(string rootPath)
    {
        var results = new List<DetectedProject>();
        var root = Path.GetFullPath(rootPath);

        foreach (var (type, indicators) in DetectionRules)
        {
            foreach (var indicator in indicators)
            {
                if (indicator.EndsWith('/'))
                {
                    // Directory indicator
                    var dirName = indicator.TrimEnd('/');
                    FindDirectories(root, dirName, root, type, indicator, results);
                }
                else if (indicator.Contains('*'))
                {
                    // Glob indicator
                    FindByGlob(root, indicator, root, type, results);
                }
                else
                {
                    // Exact file indicator
                    FindFiles(root, indicator, root, type, results);
                }
            }
        }

        // OS-level patterns (always add for root)
        results.Add(new DetectedProject(ProjectType.MacOS,   ".", ".DS_Store"));
        results.Add(new DetectedProject(ProjectType.Windows, ".", "Thumbs.db"));
        results.Add(new DetectedProject(ProjectType.Linux,   ".", "*~"));

        return results.DistinctBy(d => (d.Type, d.RelativePath)).ToList();
    }

    private static void FindFiles(string searchRoot, string fileName, string projectRoot,
        ProjectType type, List<DetectedProject> results)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(searchRoot, fileName, SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(file)!;
                var rel = Path.GetRelativePath(projectRoot, dir);
                if (IsInsideIgnoredDir(rel)) continue;
                results.Add(new DetectedProject(type, rel, fileName));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static void FindByGlob(string searchRoot, string pattern, string projectRoot,
        ProjectType type, List<DetectedProject> results)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(searchRoot, pattern, SearchOption.AllDirectories))
            {
                var dir = Path.GetDirectoryName(file)!;
                var rel = Path.GetRelativePath(projectRoot, dir);
                if (IsInsideIgnoredDir(rel)) continue;
                results.Add(new DetectedProject(type, rel, Path.GetFileName(file)));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static void FindDirectories(string searchRoot, string dirName, string projectRoot,
        ProjectType type, string indicator, List<DetectedProject> results)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(searchRoot, dirName, SearchOption.AllDirectories))
            {
                var parent = Path.GetDirectoryName(dir)!;
                var rel = Path.GetRelativePath(projectRoot, parent);
                if (IsInsideIgnoredDir(rel)) continue;
                results.Add(new DetectedProject(type, rel, indicator));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static bool IsInsideIgnoredDir(string relativePath)
    {
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p is "node_modules" or "bin" or "obj" or ".git"
                           or "vendor" or "__pycache__" or "target" or ".vs" or ".idea");
    }

    // ── Pattern generation ──────────────────────────────────────────────────

    /// <summary>
    /// Generates a complete .gitignore content string from detected projects,
    /// optionally merging with existing patterns and adding custom entries.
    /// </summary>
    public string Generate(List<DetectedProject> detectedProjects,
        IReadOnlyList<string>? customPatterns = null,
        IReadOnlyList<string>? existingPatterns = null)
    {
        var types = detectedProjects.Select(d => d.Type).Distinct().OrderBy(t => t).ToList();
        var nestedProjects = detectedProjects.Where(d => d.RelativePath != ".").ToList();
        var sections = new List<string>();

        // Header
        sections.Add(GenerateHeader(types, nestedProjects));

        // OS patterns (always included)
        sections.Add(GenerateSection("OS Generated Files", GetOsPatterns()));

        // Per-type patterns
        foreach (var type in types)
        {
            if (type is ProjectType.MacOS or ProjectType.Windows or ProjectType.Linux)
                continue; // already in OS section

            var patterns = GetPatternsForType(type);
            if (patterns.Length > 0)
                sections.Add(GenerateSection(GetSectionName(type), patterns));
        }

        // Nested project warnings
        if (nestedProjects.Count > 0)
        {
            sections.Add(GenerateNestedProjectSection(nestedProjects));
        }

        // Custom patterns
        if (customPatterns is { Count: > 0 })
        {
            sections.Add(GenerateSection("Custom Patterns", customPatterns.ToArray()));
        }

        // Merge with existing: keep existing entries, add only new ones
        var allGenerated = string.Join("\n\n", sections);
        if (existingPatterns is { Count: > 0 })
        {
            var existingSet = new HashSet<string>(existingPatterns, StringComparer.OrdinalIgnoreCase);
            var newPatterns = ExtractPatterns(allGenerated)
                .Where(p => !existingSet.Contains(p))
                .ToList();

            if (newPatterns.Count == 0)
                return string.Empty; // nothing new to add

            var mergeSection = GenerateSection(
                $"Generated by mvc-inspect ({DateTime.Now:yyyy-MM-dd HH:mm:ss})",
                newPatterns.ToArray());
            return mergeSection;
        }

        return allGenerated;
    }

    /// <summary>
    /// Parses an existing .gitignore file into individual non-empty, non-comment lines.
    /// </summary>
    public static List<string> ParseExistingGitignore(string content)
    {
        return content.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ExtractPatterns(string content)
    {
        return content.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ── Section builders ────────────────────────────────────────────────────

    private static string GenerateHeader(List<ProjectType> types, List<DetectedProject> nested)
    {
        var lines = new List<string>
        {
            "# ═══════════════════════════════════════════════════════════════════════",
            "# .gitignore — Auto-generated by MVC Structure Inspector",
            $"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"# Detected ecosystems: {string.Join(", ", types.Where(t => t is not (ProjectType.MacOS or ProjectType.Windows or ProjectType.Linux)).Select(GetDisplayName))}",
        };

        if (nested.Count > 0)
        {
            lines.Add($"# Nested projects detected: {nested.Count}");
            foreach (var np in nested.Take(10))
                lines.Add($"#   └─ {np.Type} @ {np.RelativePath} (via {np.Indicator})");
            if (nested.Count > 10)
                lines.Add($"#   └─ ... and {nested.Count - 10} more");
        }

        lines.Add("# ═══════════════════════════════════════════════════════════════════════");
        return string.Join("\n", lines);
    }

    private static string GenerateSection(string title, string[] patterns)
    {
        var lines = new List<string>
        {
            $"# ── {title} " + new string('─', Math.Max(0, 60 - title.Length)),
        };
        lines.AddRange(patterns);
        return string.Join("\n", lines);
    }

    private static string GenerateNestedProjectSection(List<DetectedProject> nested)
    {
        var lines = new List<string>
        {
            "# ── Nested / Hybrid Project Awareness " + new string('─', 22),
            "# The following paths contain nested project ecosystems.",
            "# Patterns above apply globally; add path-specific overrides below if needed.",
        };

        foreach (var group in nested.GroupBy(n => n.RelativePath))
        {
            var types = string.Join(" + ", group.Select(g => GetDisplayName(g.Type)).Distinct());
            lines.Add($"# {group.Key}/ → {types}");
        }

        return string.Join("\n", lines);
    }

    // ── Pattern dictionaries ────────────────────────────────────────────────

    private static string[] GetOsPatterns() =>
    [
        "# macOS",
        ".DS_Store",
        ".AppleDouble",
        ".LSOverride",
        "Icon?",
        "._*",
        "",
        "# Windows",
        "Thumbs.db",
        "Thumbs.db:encryptable",
        "ehthumbs.db",
        "ehthumbs_vista.db",
        "*.stackdump",
        "[Dd]esktop.ini",
        "",
        "# Linux",
        "*~",
        ".fuse_hidden*",
        ".Trash-*",
        ".nfs*",
    ];

    private static string[] GetPatternsForType(ProjectType type) => type switch
    {
        ProjectType.DotNet => [
            "[Bb]in/",
            "[Oo]bj/",
            "[Dd]ebug/",
            "[Rr]elease/",
            "x64/",
            "x86/",
            "bld/",
            "[Ll]og/",
            "[Ll]ogs/",
            "",
            "# Visual Studio / MSBuild",
            "*.suo",
            "*.user",
            "*.userosscache",
            "*.sln.docstates",
            ".vs/",
            "*.nupkg",
            "**/[Pp]ackages/*",
            "!**/[Pp]ackages/build/",
            "*.snupkg",
            "",
            "# NuGet v3 local cache",
            "project.lock.json",
            "project.fragment.lock.json",
            "artifacts/",
            "",
            "# Build results",
            "[Dd]ebugPublic/",
            "[Rr]eleases/",
            "*.appx",
            "*.appxbundle",
            "*.appxupload",
            "",
            "# User-specific",
            "*.rsuser",
            "*.DotSettings.user",
            "launchSettings.json",
            "",
            "# Publish output",
            "*.publish.xml",
            "PublishScripts/",
            "",
            "# Code coverage",
            "*.coverage",
            "*.coveragexml",
            "coverage/",
            "TestResults/",
        ],

        ProjectType.NodeJs => [
            "node_modules/",
            "npm-debug.log*",
            "yarn-debug.log*",
            "yarn-error.log*",
            ".pnpm-debug.log*",
            "",
            "# Runtime data",
            "pids",
            "*.pid",
            "*.seed",
            "*.pid.lock",
            "",
            "# Coverage",
            "coverage/",
            "*.lcov",
            ".nyc_output",
            "",
            "# Build output",
            "dist/",
            "build/",
            ".next/",
            ".nuxt/",
            "out/",
            "",
            "# Dependency directories",
            "bower_components/",
            "jspm_packages/",
            "",
            "# Environment",
            ".env",
            ".env.local",
            ".env.*.local",
            "",
            "# Cache",
            ".cache/",
            ".parcel-cache/",
            ".eslintcache",
            ".stylelintcache",
        ],

        ProjectType.Python => [
            "__pycache__/",
            "*.py[cod]",
            "*$py.class",
            "",
            "# Virtual environments",
            ".venv/",
            "venv/",
            "ENV/",
            "env/",
            ".env",
            "",
            "# Distribution",
            "dist/",
            "build/",
            "*.egg-info/",
            "*.egg",
            "sdist/",
            "wheels/",
            "",
            "# Testing / Coverage",
            ".pytest_cache/",
            ".coverage",
            "htmlcov/",
            ".tox/",
            ".nox/",
            "",
            "# Jupyter",
            ".ipynb_checkpoints",
            "",
            "# mypy / type checkers",
            ".mypy_cache/",
            ".pytype/",
        ],

        ProjectType.Rust => [
            "target/",
            "Cargo.lock",
            "**/*.rs.bk",
        ],

        ProjectType.Go => [
            "vendor/",
            "*.exe",
            "*.exe~",
            "*.test",
            "*.out",
        ],

        ProjectType.Java => [
            "target/",
            "*.class",
            "*.jar",
            "*.war",
            "*.ear",
            "*.nar",
            "",
            "# Gradle",
            ".gradle/",
            "build/",
            "!gradle-wrapper.jar",
            "",
            "# Maven",
            "pom.xml.tag",
            "pom.xml.releaseBackup",
            "pom.xml.versionsBackup",
            "pom.xml.next",
            "release.properties",
        ],

        ProjectType.Ruby => [
            "*.gem",
            "*.rbc",
            ".bundle/",
            "vendor/bundle",
            ".byebug_history",
            "coverage/",
            "tmp/",
            "log/",
        ],

        ProjectType.Php => [
            "vendor/",
            "composer.lock",
            ".phpunit.result.cache",
            ".php_cs.cache",
            ".php-cs-fixer.cache",
        ],

        ProjectType.Swift => [
            ".build/",
            "Packages/",
            "xcuserdata/",
            "*.xcscmblueprint",
            "*.xccheckout",
            "DerivedData/",
            "*.moved-aside",
            "*.pbxuser",
            "*.mode1v3",
            "*.mode2v3",
            "*.perspectivev3",
        ],

        ProjectType.DartFlutter => [
            ".dart_tool/",
            ".packages",
            "build/",
            ".flutter-plugins",
            ".flutter-plugins-dependencies",
            "*.iml",
        ],

        ProjectType.Unity => [
            "[Ll]ibrary/",
            "[Tt]emp/",
            "[Oo]bj/",
            "[Bb]uild/",
            "[Bb]uilds/",
            "[Ll]ogs/",
            "UserSettings/",
            "MemoryCaptures/",
            "Recordings/",
            "Asset Store*.unitypackage",
            "*.pidb.meta",
            "*.pdb.meta",
            "*.mdb.meta",
            "sysinfo.txt",
            "crashlytics-build.properties",
        ],

        ProjectType.Terraform => [
            ".terraform/",
            "*.tfstate",
            "*.tfstate.*",
            "*.tfvars",
            "!*.tfvars.example",
            "crash.log",
            "override.tf",
            "override.tf.json",
            "*_override.tf",
            "*_override.tf.json",
            ".terraformrc",
            "terraform.rc",
        ],

        ProjectType.Docker => [
            "# Docker (no default ignores — use .dockerignore for build context)",
        ],

        ProjectType.VisualStudio => [
            ".vs/",
            "*.suo",
            "*.user",
        ],

        ProjectType.JetBrains => [
            ".idea/",
            "*.iml",
            "*.iws",
            "*.ipr",
            "out/",
        ],

        _ => [],
    };

    private static string GetSectionName(ProjectType type) => type switch
    {
        ProjectType.DotNet       => ".NET / ASP.NET Core / MSBuild",
        ProjectType.NodeJs       => "Node.js / npm / yarn",
        ProjectType.Python       => "Python",
        ProjectType.Rust         => "Rust / Cargo",
        ProjectType.Go           => "Go",
        ProjectType.Java         => "Java / Maven / Gradle",
        ProjectType.Ruby         => "Ruby / Bundler",
        ProjectType.Php          => "PHP / Composer",
        ProjectType.Swift        => "Swift / Xcode",
        ProjectType.DartFlutter  => "Dart / Flutter",
        ProjectType.Unity        => "Unity",
        ProjectType.Terraform    => "Terraform",
        ProjectType.Docker       => "Docker",
        ProjectType.VisualStudio => "Visual Studio",
        ProjectType.JetBrains    => "JetBrains IDE",
        _                        => type.ToString(),
    };

    private static string GetDisplayName(ProjectType type) => type switch
    {
        ProjectType.DotNet       => ".NET",
        ProjectType.NodeJs       => "Node.js",
        ProjectType.DartFlutter  => "Dart/Flutter",
        ProjectType.VisualStudio => "Visual Studio",
        ProjectType.JetBrains    => "JetBrains",
        _                        => type.ToString(),
    };
}
