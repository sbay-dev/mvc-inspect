using System.Text;

namespace MvcStructureInspector;

/// <summary>Formats a gap-analysis report from comparison results.</summary>
public class GapReportFormatter
{
    private const string Line80     = "================================================================================";
    private const string Line80Thin = "--------------------------------------------------------------------------------";

    public string Format(
        ProjectSnapshot snapA, ProjectSnapshot snapB,
        List<FileDiff> diffs, List<RazorFileDiff> razorDiffs, GapSummary summary)
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
                      + summary.RazorElementsOnlyInA + summary.RazorElementsOnlyInB + summary.RazorElementsModified;

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

        // -- Section 4: Developer task checklist ------------------------------
        sb.AppendLine("[4] DEVELOPER TASK CHECKLIST");
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

        sb.AppendLine();
        sb.AppendLine(Line80);
        sb.AppendLine($"  Total: {taskNum - 1} task(s) required to align [B] with [A]");
        sb.AppendLine(Line80);

        return sb.ToString();
    }
}
