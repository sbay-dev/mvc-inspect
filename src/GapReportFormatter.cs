using System.Text;

namespace MvcStructureInspector;

/// <summary>Formats a gap-analysis report from comparison results.</summary>
public class GapReportFormatter
{
    private const string Line80     = "================================================================================";
    private const string Line80Thin = "--------------------------------------------------------------------------------";

    public string Format(
        ProjectSnapshot snapA, ProjectSnapshot snapB,
        List<FileDiff> diffs, List<RazorFileDiff> razorDiffs,
        List<StaticFileDiff> staticDiffs,
        List<CsprojDiff> projDiffs, SlnDiff? slnDiff,
        GapSummary summary,
        FunctionalCoverageSummary? functionalCoverage = null)
    {
        var sb = new StringBuilder();

        // -- Header -----------------------------------------------------------
        sb.AppendLine(Line80);
        sb.AppendLine("  GAP ANALYSIS REPORT");
        sb.AppendLine(Line80);
        sb.AppendLine($"  [A] Reference : {snapA.ProjectName}");
        sb.AppendLine($"      Path      : {snapA.RootPath}");
        sb.AppendLine($"  [B] Project   : {snapB.ProjectName}");
        sb.AppendLine($"      Path      : {snapB.RootPath}");
        sb.AppendLine($"  Generated     : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(Line80);
        sb.AppendLine();

        // -- Executive summary ------------------------------------------------
        int totalGaps = summary.FilesOnlyInA + summary.FilesOnlyInB
                      + summary.TypesOnlyInA + summary.TypesOnlyInB + summary.TypesModified
                      + summary.MembersOnlyInA + summary.MembersOnlyInB + summary.MembersModified
                      + summary.RazorOnlyInA + summary.RazorOnlyInB + summary.RazorModified
                      + summary.RazorElementsOnlyInA + summary.RazorElementsOnlyInB + summary.RazorElementsModified
                      + summary.StaticOnlyInA + summary.StaticOnlyInB + summary.StaticModified
                      + summary.ProjOnlyInA + summary.ProjOnlyInB + summary.ProjDiffsCount
                      + summary.SlnProjectsOnlyInA + summary.SlnProjectsOnlyInB + summary.SlnProjectsModified;

        sb.AppendLine($"  Total Gaps          : {totalGaps}");
        sb.AppendLine(Line80Thin);
        sb.AppendLine($"  C# files matched    : {summary.FilesMatched}");
        sb.AppendLine($"  C# files missing in [B]  : {summary.FilesOnlyInA}   (exist in A only)");
        sb.AppendLine($"  C# files extra in [B]    : {summary.FilesOnlyInB}   (not in A)");
        sb.AppendLine(Line80Thin);
        sb.AppendLine($"  Types missing in [B]     : {summary.TypesOnlyInA}");
        sb.AppendLine($"  Types extra in [B]        : {summary.TypesOnlyInB}");
        sb.AppendLine($"  Types modified            : {summary.TypesModified}");
        sb.AppendLine(Line80Thin);
        sb.AppendLine($"  Members missing in [B]   : {summary.MembersOnlyInA}   (methods/properties/fields)");
        sb.AppendLine($"  Members extra in [B]      : {summary.MembersOnlyInB}");
        sb.AppendLine($"  Members modified          : {summary.MembersModified}   (signature changed)");
        sb.AppendLine(Line80Thin);
        sb.AppendLine($"  Razor views missing in [B]: {summary.RazorOnlyInA}   (.cshtml)");
        sb.AppendLine($"  Razor views extra in [B]  : {summary.RazorOnlyInB}");
        sb.AppendLine($"  Razor views modified      : {summary.RazorModified}");
        sb.AppendLine($"  Razor elements missing    : {summary.RazorElementsOnlyInA}   (@model/@section/asp-for...)");
        sb.AppendLine($"  Razor elements extra      : {summary.RazorElementsOnlyInB}");
        sb.AppendLine($"  Razor elements modified   : {summary.RazorElementsModified}");
        if (summary.StaticOnlyInA + summary.StaticOnlyInB + summary.StaticModified > 0)
        {
            sb.AppendLine(Line80Thin);
            sb.AppendLine($"  Static files missing in [B]   : {summary.StaticOnlyInA}   (wwwroot/)");
            sb.AppendLine($"  Static files extra in [B]     : {summary.StaticOnlyInB}");
            sb.AppendLine($"  Static files modified         : {summary.StaticModified}   (content changed)");
        }
        if (summary.ProjOnlyInA + summary.ProjOnlyInB + summary.ProjDiffsCount
          + summary.SlnProjectsOnlyInA + summary.SlnProjectsOnlyInB + summary.SlnProjectsModified > 0)
        {
            sb.AppendLine(Line80Thin);
            sb.AppendLine($"  .csproj files missing in [B]  : {summary.ProjOnlyInA}");
            sb.AppendLine($"  .csproj files extra in [B]    : {summary.ProjOnlyInB}");
            sb.AppendLine($"  .csproj files with diffs      : {summary.ProjDiffsCount}");
            sb.AppendLine($"  .sln projects missing in [B]  : {summary.SlnProjectsOnlyInA}");
            sb.AppendLine($"  .sln projects extra in [B]    : {summary.SlnProjectsOnlyInB}");
            sb.AppendLine($"  .sln projects modified        : {summary.SlnProjectsModified}");
        }
        if (functionalCoverage != null)
        {
            sb.AppendLine(Line80Thin);
            sb.AppendLine($"  Functional Coverage           : {functionalCoverage.OverallCoveragePercent}%  ({functionalCoverage.TotalCovered}/{functionalCoverage.TotalEndpointsA} endpoints)");
            sb.AppendLine($"  Namespaces fully covered      : {functionalCoverage.FullyCovered}");
            sb.AppendLine($"  Namespaces partially covered  : {functionalCoverage.PartiallyCovered}");
            sb.AppendLine($"  Namespaces not covered        : {functionalCoverage.NotCovered}");
            sb.AppendLine($"  Endpoints uncovered in [B]    : {functionalCoverage.TotalUncovered}");
        }
        sb.AppendLine(Line80);
        sb.AppendLine();

        if (totalGaps == 0)
        {
            sb.AppendLine("  [OK] Projects are identical - no gaps found.");
            sb.AppendLine();
            sb.AppendLine(Line80);
            return sb.ToString();
        }

        // -- Section 1: C# file gaps ------------------------------------------
        var missingFiles = diffs.Where(d => d.Status == DiffStatus.Missing).ToList();
        var extraFiles   = diffs.Where(d => d.Status == DiffStatus.Extra).ToList();

        if (missingFiles.Any() || extraFiles.Any())
        {
            sb.AppendLine("[1] C# FILE GAPS");
            sb.AppendLine(Line80Thin);
            sb.AppendLine();

            if (missingFiles.Any())
            {
                sb.AppendLine("  [MISSING] Files in [A] not found in [B] - must be created:");
                foreach (var f in missingFiles)
                    sb.AppendLine($"       - {f.RelativePath}");
                sb.AppendLine();
            }

            if (extraFiles.Any())
            {
                sb.AppendLine("  [EXTRA] Files in [B] not in [A] - additional files:");
                foreach (var f in extraFiles)
                    sb.AppendLine($"       + {f.RelativePath}");
                sb.AppendLine();
            }
        }

        // -- Section 2: Class / member diffs ----------------------------------
        var modifiedFiles = diffs.Where(d => d.Status == DiffStatus.Modified).ToList();

        if (modifiedFiles.Any())
        {
            sb.AppendLine("[2] CLASS / MEMBER GAPS");
            sb.AppendLine(Line80Thin);

            foreach (var fileDiff in modifiedFiles)
            {
                var relevantTypes = fileDiff.TypeDiffs
                    .Where(t => t.Status != DiffStatus.Identical)
                    .ToList();

                if (!relevantTypes.Any()) continue;

                sb.AppendLine();
                sb.AppendLine($"  File: {fileDiff.RelativePath}");
                sb.AppendLine($"  {new string('-', Math.Min(fileDiff.RelativePath.Length + 6, 76))}");

                foreach (var typeDiff in relevantTypes)
                {
                    string typeName = typeDiff.TypePath.Contains('>')
                        ? typeDiff.TypePath[(typeDiff.TypePath.LastIndexOf('>') + 2)..].Trim()
                        : typeDiff.TypePath;

                    switch (typeDiff.Status)
                    {
                        case DiffStatus.Missing:
                            sb.AppendLine($"    [MISSING TYPE] {typeName}  (in A, not in B)");
                            break;

                        case DiffStatus.Extra:
                            sb.AppendLine($"    [EXTRA TYPE]   {typeName}  (in B, not in A)");
                            break;

                        case DiffStatus.Modified:
                            sb.AppendLine($"    [MODIFIED]     {typeName}");

                            if (typeDiff.NoteA != null)
                            {
                                sb.AppendLine($"       Definition A: {typeDiff.NoteA}");
                                sb.AppendLine($"       Definition B: {typeDiff.NoteB}");
                            }

                            var relevantMembers = typeDiff.MemberDiffs
                                .Where(m => m.Status != DiffStatus.Identical)
                                .ToList();

                            if (relevantMembers.Any())
                            {
                                foreach (var grp in relevantMembers.GroupBy(m => m.Kind).OrderBy(g => g.Key))
                                {
                                    string kindLabel = grp.Key switch
                                    {
                                        "field"       => "Fields",
                                        "property"    => "Properties",
                                        "constructor" => "Constructors",
                                        "method"      => "Methods",
                                        "enum-value"  => "Enum Values",
                                        _             => grp.Key
                                    };
                                    sb.AppendLine($"       -- {kindLabel}:");

                                    foreach (var m in grp)
                                    {
                                        switch (m.Status)
                                        {
                                            case DiffStatus.Missing:
                                                sb.AppendLine($"          [MISSING] {m.SignatureA}");
                                                break;
                                            case DiffStatus.Extra:
                                                sb.AppendLine($"          [EXTRA]   {m.SignatureB}");
                                                break;
                                            case DiffStatus.Modified:
                                                sb.AppendLine($"          [CHANGED]");
                                                sb.AppendLine($"             A: {m.SignatureA}");
                                                sb.AppendLine($"             B: {m.SignatureB}");
                                                break;
                                        }
                                    }
                                }
                            }
                            break;
                    }
                }
            }
            sb.AppendLine();
        }

        // -- Section 3: Razor gaps --------------------------------------------
        var razorMissing  = razorDiffs.Where(d => d.Status == DiffStatus.Missing).ToList();
        var razorExtra    = razorDiffs.Where(d => d.Status == DiffStatus.Extra).ToList();
        var razorModified = razorDiffs.Where(d => d.Status == DiffStatus.Modified).ToList();

        if (razorMissing.Any() || razorExtra.Any() || razorModified.Any())
        {
            sb.AppendLine("[3] RAZOR VIEW GAPS (.cshtml)");
            sb.AppendLine(Line80Thin);
            sb.AppendLine();

            if (razorMissing.Any())
            {
                sb.AppendLine("  [MISSING] Views not present in [B]:");
                foreach (var r in razorMissing)
                    sb.AppendLine($"       - {r.RelativePath}");
                sb.AppendLine();
            }

            if (razorExtra.Any())
            {
                sb.AppendLine("  [EXTRA] Views in [B] not in [A]:");
                foreach (var r in razorExtra)
                    sb.AppendLine($"       + {r.RelativePath}");
                sb.AppendLine();
            }

            if (razorModified.Any())
            {
                sb.AppendLine("  [MODIFIED] Views with element differences:");
                sb.AppendLine();

                foreach (var r in razorModified)
                {
                    sb.AppendLine($"  File: {r.RelativePath}");
                    sb.AppendLine($"  {new string('-', Math.Min(r.RelativePath.Length + 6, 76))}");

                    foreach (var grp in r.Elements.GroupBy(e => e.Category).OrderBy(g => g.Key))
                    {
                        string catLabel = grp.Key switch
                        {
                            "Model"         => "@model",
                            "Page"          => "@page",
                            "Layout"        => "Layout",
                            "Kind"          => "File Kind",
                            "Using"         => "@using",
                            "Inject"        => "@inject",
                            "Section"       => "@section",
                            "RenderSection" => "@RenderSection",
                            "PartialRef"    => "<partial>",
                            "Component"     => "ViewComponent",
                            "ViewBag"       => "ViewBag",
                            "ViewData"      => "ViewData",
                            "FormAction"    => "<form asp-action>",
                            "AspFor"        => "asp-for",
                            _               => grp.Key
                        };

                        sb.AppendLine($"     -- {catLabel}:");
                        foreach (var el in grp)
                        {
                            switch (el.Status)
                            {
                                case DiffStatus.Missing:
                                    sb.AppendLine($"        [MISSING] {el.ValueA}");
                                    break;
                                case DiffStatus.Extra:
                                    sb.AppendLine($"        [EXTRA]   {el.ValueB}");
                                    break;
                                case DiffStatus.Modified:
                                    sb.AppendLine($"        [CHANGED]");
                                    sb.AppendLine($"           A: {el.ValueA}");
                                    sb.AppendLine($"           B: {el.ValueB}");
                                    break;
                            }
                        }
                    }
                    sb.AppendLine();
                }
            }
        }

        // -- Section 4: Static file gaps (wwwroot) ----------------------------
        if (staticDiffs.Any())
        {
            sb.AppendLine("[4] STATIC FILE GAPS (wwwroot/)");
            sb.AppendLine(Line80Thin);
            sb.AppendLine();

            var stMissing  = staticDiffs.Where(d => d.Status == DiffStatus.Missing).ToList();
            var stExtra    = staticDiffs.Where(d => d.Status == DiffStatus.Extra).ToList();
            var stModified = staticDiffs.Where(d => d.Status == DiffStatus.Modified).ToList();

            if (stMissing.Any())
            {
                sb.AppendLine("  [MISSING] Static files in [A] not found in [B]:");
                foreach (var f in stMissing)
                    sb.AppendLine($"    - {f.RelativePath}  ({FormatSize(f.SizeA ?? 0)})");
                sb.AppendLine();
            }
            if (stExtra.Any())
            {
                sb.AppendLine("  [EXTRA] Static files in [B] not in [A]:");
                foreach (var f in stExtra)
                    sb.AppendLine($"    + {f.RelativePath}  ({FormatSize(f.SizeB ?? 0)})");
                sb.AppendLine();
            }
            if (stModified.Any())
            {
                sb.AppendLine("  [MODIFIED] Static files with different content:");
                foreach (var f in stModified)
                {
                    sb.AppendLine($"    ~ {f.RelativePath}");
                    sb.AppendLine($"        Size A: {FormatSize(f.SizeA ?? 0)}   B: {FormatSize(f.SizeB ?? 0)}");
                    sb.AppendLine($"        Hash A: {f.HashA?[..12]}...   B: {f.HashB?[..12]}...");
                }
                sb.AppendLine();
            }
        }

        // -- Section 5: .csproj / .sln gaps ----------------------------------
        var projMissing  = projDiffs.Where(d => d.Status == DiffStatus.Missing).ToList();
        var projExtra    = projDiffs.Where(d => d.Status == DiffStatus.Extra).ToList();
        var projModified = projDiffs.Where(d => d.Status == DiffStatus.Modified).ToList();
        bool hasSlnDiffs = slnDiff?.ProjectDiffs.Any() == true;

        if (projMissing.Any() || projExtra.Any() || projModified.Any() || hasSlnDiffs)
        {
            sb.AppendLine("[5] PROJECT FILE GAPS (.csproj / .sln)");
            sb.AppendLine(Line80Thin);
            sb.AppendLine();

            // ── .csproj file presence ──────────────────────────────────────
            if (projMissing.Any())
            {
                sb.AppendLine("  [MISSING] .csproj files in [A] not found in [B]:");
                foreach (var p in projMissing)
                    sb.AppendLine($"       - {p.RelativePath}");
                sb.AppendLine();
            }

            if (projExtra.Any())
            {
                sb.AppendLine("  [EXTRA] .csproj files in [B] not in [A]:");
                foreach (var p in projExtra)
                    sb.AppendLine($"       + {p.RelativePath}");
                sb.AppendLine();
            }

            // ── .csproj property diffs ──────────────────────────────────────
            if (projModified.Any())
            {
                sb.AppendLine("  [MODIFIED] .csproj files with differences:");
                sb.AppendLine();

                foreach (var pd in projModified)
                {
                    sb.AppendLine($"  File: {pd.RelativePath}");
                    sb.AppendLine($"  {new string('-', Math.Min(pd.RelativePath.Length + 6, 76))}");

                    foreach (var grp in pd.PropertyDiffs.GroupBy(d => d.Category).OrderBy(g => g.Key))
                    {
                        string catLabel = grp.Key switch
                        {
                            "SDK"             => "SDK",
                            "TargetFramework" => "Target Framework(s)",
                            "OutputType"      => "Output Type",
                            "Nullable"        => "Nullable",
                            "ImplicitUsings"  => "Implicit Usings",
                            "LangVersion"     => "Language Version",
                            "PackageRef"      => "Package References",
                            "ProjectRef"      => "Project References",
                            "Property"        => "Other Properties",
                            _                 => grp.Key
                        };

                        sb.AppendLine($"     -- {catLabel}:");
                        foreach (var d in grp)
                        {
                            switch (d.Status)
                            {
                                case DiffStatus.Missing:
                                    sb.AppendLine($"        [MISSING] {d.ValueA}");
                                    break;
                                case DiffStatus.Extra:
                                    sb.AppendLine($"        [EXTRA]   {d.ValueB}");
                                    break;
                                case DiffStatus.Modified:
                                    sb.AppendLine($"        [CHANGED] {d.Name}");
                                    sb.AppendLine($"           A: {d.ValueA}");
                                    sb.AppendLine($"           B: {d.ValueB}");
                                    break;
                            }
                        }
                    }
                    sb.AppendLine();
                }
            }

            // ── .sln project list diffs ────────────────────────────────────
            if (hasSlnDiffs)
            {
                sb.AppendLine("  .sln file:");
                if (slnDiff!.RelPathA != null) sb.AppendLine($"     [A] {slnDiff.RelPathA}");
                if (slnDiff!.RelPathB != null) sb.AppendLine($"     [B] {slnDiff.RelPathB}");
                sb.AppendLine();

                var slnMissing  = slnDiff.ProjectDiffs.Where(p => p.Status == DiffStatus.Missing).ToList();
                var slnExtra    = slnDiff.ProjectDiffs.Where(p => p.Status == DiffStatus.Extra).ToList();
                var slnModified = slnDiff.ProjectDiffs.Where(p => p.Status == DiffStatus.Modified).ToList();

                if (slnMissing.Any())
                {
                    sb.AppendLine("     [MISSING] Projects registered in [A].sln but absent in [B].sln:");
                    foreach (var p in slnMissing)
                        sb.AppendLine($"          - {p.Name}  ({p.PathA})");
                    sb.AppendLine();
                }

                if (slnExtra.Any())
                {
                    sb.AppendLine("     [EXTRA] Projects in [B].sln not registered in [A].sln:");
                    foreach (var p in slnExtra)
                        sb.AppendLine($"          + {p.Name}  ({p.PathB})");
                    sb.AppendLine();
                }

                if (slnModified.Any())
                {
                    sb.AppendLine("     [CHANGED] Projects with differing path or type GUID:");
                    foreach (var p in slnModified)
                    {
                        sb.AppendLine($"          ~ {p.Name}");
                        if (p.PathA != null)
                        {
                            sb.AppendLine($"              Path  A: {p.PathA}");
                            sb.AppendLine($"              Path  B: {p.PathB}");
                        }
                        if (p.TypeA != null)
                        {
                            sb.AppendLine($"              Type  A: {p.TypeA}");
                            sb.AppendLine($"              Type  B: {p.TypeB}");
                        }
                    }
                    sb.AppendLine();
                }
            }
        }

        // -- Section 6: Functional coverage ─────────────────────────────────
        if (functionalCoverage != null)
        {
            sb.AppendLine("[6] FUNCTIONAL COVERAGE (Namespace-Level Endpoint Analysis)");
            sb.AppendLine(Line80Thin);
            sb.AppendLine();
            sb.AppendLine($"  Overall Coverage : {functionalCoverage.OverallCoveragePercent}%  ({functionalCoverage.TotalCovered}/{functionalCoverage.TotalEndpointsA} endpoints)");
            sb.AppendLine($"  Pattern Mapping  : PageModel handlers ↔ Controller actions");
            sb.AppendLine();

            foreach (var ns in functionalCoverage.NamespaceDetails)
            {
                string bar = BuildProgressBar(ns.CoveragePercent, 30);
                sb.AppendLine($"  ┌─ {ns.NamespaceA}");
                sb.AppendLine($"  │  Coverage: {bar} {ns.CoveragePercent,5:F1}%  ({ns.Covered}/{ns.TotalEndpoints})");
                if (ns.MatchedNamespaceB != null)
                    sb.AppendLine($"  │  Mapped → {ns.MatchedNamespaceB}");

                if (ns.Matches.Any())
                {
                    sb.AppendLine($"  │  ── Covered endpoints:");
                    foreach (var m in ns.Matches)
                    {
                        string verb = m.EndpointA.HttpVerb.PadRight(6);
                        sb.AppendLine($"  │     ✓ [{verb}] {m.EndpointA.FunctionName,-30} ({m.EndpointA.Pattern}: {m.EndpointA.SourceMethod})");
                        sb.AppendLine($"  │       → {m.EndpointB.SourceClass}.{m.EndpointB.SourceMethod}  [{m.MatchKind}]");
                    }
                }

                if (ns.UncoveredEndpoints.Any())
                {
                    sb.AppendLine($"  │  ── Uncovered endpoints:");
                    foreach (var ep in ns.UncoveredEndpoints)
                    {
                        string verb = ep.HttpVerb.PadRight(6);
                        sb.AppendLine($"  │     ✗ [{verb}] {ep.FunctionName,-30} ({ep.Pattern}: {ep.SourceClass}.{ep.SourceMethod})");
                        sb.AppendLine($"  │       Signature: {ep.Signature}");
                    }
                }
                sb.AppendLine($"  └─");
                sb.AppendLine();
            }
        }

        // -- Section 7: Developer task checklist ------------------------------
        sb.AppendLine("[7] DEVELOPER TASK CHECKLIST");
        sb.AppendLine(Line80Thin);
        sb.AppendLine();

        int taskNum = 1;

        foreach (var f in missingFiles)
            sb.AppendLine($"  [ ] {taskNum++:D2}. Create C# file      : {f.RelativePath}");

        foreach (var fileDiff in modifiedFiles)
        {
            foreach (var td in fileDiff.TypeDiffs.Where(t => t.Status == DiffStatus.Missing))
            {
                string t = td.TypePath.Contains('>')
                    ? td.TypePath[(td.TypePath.LastIndexOf('>') + 2)..].Trim()
                    : td.TypePath;
                sb.AppendLine($"  [ ] {taskNum++:D2}. Create type         : {t}  in  {fileDiff.RelativePath}");
            }

            foreach (var td in fileDiff.TypeDiffs.Where(t => t.Status == DiffStatus.Modified))
            {
                string tName = td.TypePath.Contains('>')
                    ? td.TypePath[(td.TypePath.LastIndexOf('>') + 2)..].Trim()
                    : td.TypePath;

                foreach (var m in td.MemberDiffs.Where(m => m.Status == DiffStatus.Missing))
                    sb.AppendLine($"  [ ] {taskNum++:D2}. Add {m.Kind,-12}   : {m.SignatureA}  ->  {tName}");

                foreach (var m in td.MemberDiffs.Where(m => m.Status == DiffStatus.Modified))
                    sb.AppendLine($"  [ ] {taskNum++:D2}. Fix {m.Kind,-12}   : {m.Name}  ->  {tName}");
            }
        }

        foreach (var r in razorMissing)
            sb.AppendLine($"  [ ] {taskNum++:D2}. Create Razor view   : {r.RelativePath}");

        foreach (var r in razorModified)
        {
            foreach (var el in r.Elements.Where(e => e.Status == DiffStatus.Missing))
                sb.AppendLine($"  [ ] {taskNum++:D2}. Add {el.Category,-13}  : {el.ValueA}  ->  {r.RelativePath}");
            foreach (var el in r.Elements.Where(e => e.Status == DiffStatus.Modified))
                sb.AppendLine($"  [ ] {taskNum++:D2}. Fix {el.Category,-13}  : {r.RelativePath}  [A: {el.ValueA}  ->  B: {el.ValueB}]");
        }

        // Static file tasks
        foreach (var f in staticDiffs.Where(d => d.Status == DiffStatus.Missing))
            sb.AppendLine($"  [ ] {taskNum++:D2}. Copy static file    : {f.RelativePath}");
        foreach (var f in staticDiffs.Where(d => d.Status == DiffStatus.Modified))
            sb.AppendLine($"  [ ] {taskNum++:D2}. Update static file  : {f.RelativePath}");

        foreach (var p in projDiffs.Where(d => d.Status == DiffStatus.Missing))
            sb.AppendLine($"  [ ] {taskNum++:D2}. Create .csproj      : {p.RelativePath}");

        foreach (var p in projModified)
        {
            foreach (var d in p.PropertyDiffs.Where(d => d.Status == DiffStatus.Missing))
                sb.AppendLine($"  [ ] {taskNum++:D2}. Add {d.Category,-13}  : {d.ValueA}  ->  {p.RelativePath}");
            foreach (var d in p.PropertyDiffs.Where(d => d.Status == DiffStatus.Modified))
                sb.AppendLine($"  [ ] {taskNum++:D2}. Fix {d.Category,-13}  : {d.Name}  in  {p.RelativePath}");
        }

        if (slnDiff != null)
        {
            foreach (var p in slnDiff.ProjectDiffs.Where(d => d.Status == DiffStatus.Missing))
                sb.AppendLine($"  [ ] {taskNum++:D2}. Register in .sln    : {p.Name}  ({p.PathA})");
            foreach (var p in slnDiff.ProjectDiffs.Where(d => d.Status == DiffStatus.Modified))
                sb.AppendLine($"  [ ] {taskNum++:D2}. Fix .sln entry      : {p.Name}");
        }

        // Functional coverage tasks
        if (functionalCoverage != null)
        {
            foreach (var ns in functionalCoverage.NamespaceDetails)
            {
                foreach (var ep in ns.UncoveredEndpoints)
                    sb.AppendLine($"  [ ] {taskNum++:D2}. Implement endpoint  : [{ep.HttpVerb}] {ep.FunctionName}  (from {ep.SourceClass}.{ep.SourceMethod})");
            }
        }

        sb.AppendLine();
        sb.AppendLine(Line80);
        sb.AppendLine($"  Total: {taskNum - 1} task(s) required to align [B] with [A]");
        sb.AppendLine(Line80);

        return sb.ToString();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024         => $"{bytes} B",
        < 1024 * 1024  => $"{bytes / 1024.0:F1} KB",
        _              => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };

    private static string BuildProgressBar(double percent, int width)
    {
        int filled = (int)Math.Round(percent * width / 100);
        filled = Math.Clamp(filled, 0, width);
        return $"[{new string('█', filled)}{new string('░', width - filled)}]";
    }
}
