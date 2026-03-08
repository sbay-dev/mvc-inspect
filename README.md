# MVC Structure Inspector

> **A Roslyn-powered .NET global CLI tool for static structural analysis of ASP.NET Core MVC projects.**

[![NuGet](https://img.shields.io/nuget/v/MvcStructureInspector?logo=nuget&label=NuGet)](https://www.nuget.org/packages/MvcStructureInspector)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple?logo=dotnet)](https://dotnet.microsoft.com)
[![Tests](https://img.shields.io/badge/Tests-93%20passed-brightgreen)](https://github.com/sbay-dev/mvc-inspect/actions)
[![Product Page](https://img.shields.io/badge/Product%20Page-sbay--dev.github.io-blue)](https://sbay-dev.github.io/mvc-inspect/)

---

## Overview

**MVC Structure Inspector** performs deep Abstract Syntax Tree (AST) traversal of ASP.NET Core MVC projects using the .NET Compiler Platform (Roslyn). It produces comprehensive structural reports and gap analysis across:

- **C# source files** — namespaces, classes, interfaces, structs, records, enums, delegates, fields, properties, constructors, methods, nested types, and attribute metadata
- **Razor views** — `@model`, `@inject`, `@section`, `@RenderBody`, `<partial>`, `asp-for`, `ViewBag`/`ViewData`, form actions, and tag helpers
- **Static assets** — all files under `wwwroot/` compared via SHA-256 content hashing
- **Project files** — `.csproj` properties, package references, project references, and `.sln` project registry
- **Snapshot-based drift detection** — serialize a project baseline, then compare against a live project at any future point
- **Intelligent `.gitignore` generation** — multi-ecosystem detection (15+ project types), nested/hybrid project awareness, safe merge, custom patterns

---

## Installation

```bash
dotnet tool install --global MvcStructureInspector
```

> **Requirements:** .NET 8.0 SDK or later · Windows / Linux / macOS

---

## Commands

### Inspect a Project

```bash
mvc-inspect <path>
```

Generates a full structural tree report with timestamped filename to prevent overwriting:

```
[OK] Report saved to:
     C:\source\MyApp\mvc-structure_20260306_064429.txt
     Snapshot: C:\source\MyApp\mvc-structure_20260306_064429.snapshot.json
```

A `.snapshot.json` file is automatically saved alongside the text report for future drift detection.

### Gap Analysis Between Two Projects

```bash
mvc-inspect --compare <referenceProject> <targetProject>
```

Compares project **[A]** (reference) against project **[B]** (target) and produces a detailed gap report with:
- Executive summary with quantified gap counts
- Per-file, per-type, and per-member differential analysis
- Razor view element-level comparison
- Static asset (wwwroot) content comparison via SHA-256
- Developer task checklist for alignment

### Drift Detection from Saved Report

```bash
mvc-inspect --from-report <report.txt|.snapshot.json> <projectPath>
```

Loads a previously saved project snapshot as the baseline **[A]** and compares it against a live project **[B]**. This enables tracking structural drift over time without access to the original source.

### Open an Existing Report

```bash
mvc-inspect open <report-file>
```

Opens any report file with the system default viewer.

### Generate `.gitignore`

```bash
mvc-inspect gitignore <path>
```

Scans the project directory for all ecosystems and generates a comprehensive `.gitignore`:

- **15+ project types**: .NET, Node.js, Python, Rust, Go, Java, Ruby, PHP, Swift, Dart/Flutter, Unity, Terraform, Docker, Visual Studio, JetBrains
- **Nested/hybrid detection**: automatically detects projects within projects (e.g., Node.js `ClientApp/` inside a .NET solution)
- **Safety guarantee**: never overwrites an existing `.gitignore` — creates `.gitignore.generated_*` instead
- **Merge mode**: `--merge` appends only missing patterns to an existing `.gitignore`
- **Custom patterns**: `--add` lets you specify additional patterns
- **Preview mode**: `--preview` shows the output without writing any file

```
  Scanning: C:\source\MyApp
  Detected 3 ecosystem(s):
    • DotNet: 2 location(s)
        └─ . (via App.sln)
        └─ src (via Api.csproj)
    • NodeJs: 1 location(s)
        └─ ClientApp (via package.json)
    • Python: 1 location(s)
        └─ scripts (via requirements.txt)
  ⚠ 3 nested/hybrid project(s) detected — patterns applied globally.
[OK] .gitignore saved to:
     C:\source\MyApp\.gitignore
```

---

## Options

| Option | Description |
|--------|-------------|
| `--out <file>` | Override the auto-generated output path |
| `--open` | Open the report automatically after generation |
| `--with-proj` | Include `.csproj` and `.sln` comparison (compare mode only) |
| `--no-views` | Exclude `.cshtml` Razor view files |
| `--cs-only` | Analyze C# source files only |
| `--no-migrations` | Exclude the `Migrations` directory |

### Gitignore Options

| Option | Description |
|--------|-------------|
| `--preview` | Preview the generated `.gitignore` without writing to disk |
| `--merge` | Merge with existing `.gitignore` (append only unique patterns) |
| `--add <patterns>` | Add custom ignore patterns (e.g., `--add "*.log" "tmp/" "secrets/"`) |

---

## Report Structure

### Single Project Report (`mvc-structure_*.txt`)

```
MyMvcApp/
├── Controllers/
│   └── HomeController.cs
│       namespace: MyMvcApp.Controllers
│           + class HomeController : Controller
│               - ApplicationDbContext _context
│               + HomeController(ApplicationDbContext context)  [constructor]
│               + IActionResult Index()
│               + IActionResult About()
├── Models/
│   └── User.cs
│       namespace: MyMvcApp.Models
│           + class User
│               + int Id { get; set; }
│               + string Name { get; set; }
└── Views/
    └── Home/
        └── Index.cshtml
            Type: [View]
            @model MyMvcApp.Models.HomeViewModel
            Layout = "_Layout"
            asp-for="Name"
```

### Gap Analysis Report (`mvc-gap-report_*.txt`)

The report contains six sections:

1. **Executive Summary** — quantified gap counts across all categories
2. **C# File Gaps** — missing, extra, or structurally modified source files
3. **Type & Member Differentials** — class, interface, method, and property-level changes
4. **Static File Gaps** — wwwroot content comparison with file sizes and SHA-256 hashes
5. **Project File Gaps** — `.csproj` property and package reference differentials, `.sln` project registry comparison
6. **Developer Task Checklist** — actionable items to align project [B] with reference [A]

---

## Security

- **Protected Extensions** — the tool refuses to overwrite sensitive file types (`.cs`, `.csproj`, `.sln`, `.json`, `.dll`, `.exe`, `.key`, `.pfx`, etc.)
- **Timestamped Reports** — automatic filenames with `yyyyMMdd_HHmmss` pattern prevent accidental overwrites
- **Gitignore Safety** — `.gitignore` generation never overwrites existing files; validates output paths stay within the project directory
- **CodeQL Scanning** — every release undergoes automated static security analysis
- **SBOM Generation** — CycloneDX Software Bill of Materials included with each release

---

## Build from Source

```bash
git clone https://github.com/sbay-dev/mvc-inspect
cd mvc-inspect
dotnet build src/MvcStructureInspector.csproj -c Release
dotnet test tests/MvcStructureInspector.Tests/ -c Release
dotnet pack src/MvcStructureInspector.csproj -c Release -o nupkg/
```

---

## Changelog

### v2.7.0
- Intelligent `.gitignore` generator with multi-ecosystem detection
- 15+ project types: .NET, Node.js, Python, Rust, Go, Java, Ruby, PHP, Swift, Dart/Flutter, Unity, Terraform, Docker, Visual Studio, JetBrains
- Nested/hybrid project awareness (e.g., Node.js inside .NET, Python scripts in a monorepo)
- Safe merge mode with existing `.gitignore` (append unique patterns only)
- Custom pattern support via `--add` flag
- Preview mode via `--preview` flag
- SecurityGuard: path-based gitignore write validation
- 93 unit tests (22 new for gitignore generator)

### v2.6.1
- Professional NuGet metadata and product page integration
- Comprehensive English documentation

### v2.6.0
- Snapshot-based drift detection via `--from-report` command
- Automatic `.snapshot.json` serialization alongside text reports
- SecurityGuard allowlist for tool-generated snapshot files

### v2.5.0
- wwwroot static file comparison with SHA-256 content hashing
- Static file gap reporting (Section [4]) with file sizes and hash differentials
- CI/CD pipeline: CodeQL, SBOM generation, automated NuGet publishing
- Professional bilingual product page at sbay-dev.github.io/mvc-inspect

### v2.4.0
- `.csproj` and `.sln` comparison via `--with-proj` flag
- Package reference, project reference, and SDK property differentials

### v2.3.0
- `--open` flag for automatic report viewing
- `open <file>` subcommand for existing reports

### v2.2.1
- Timestamped auto-save filenames to prevent report overwriting

### v2.0.0
- Razor view structural analysis (`.cshtml`)
- `@model`, `@inject`, `@section`, `asp-for`, `<partial>`, ViewBag/ViewData extraction

### v1.0.0
- Initial release — Roslyn-based C# structural inspection and gap analysis

---

## License

MIT © 2025-2026 [SBAY-SDK](https://github.com/sbay-dev)
