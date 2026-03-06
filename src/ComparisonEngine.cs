using System.Text;

namespace MvcStructureInspector;

// ── Diff result types ────────────────────────────────────────────────────────

public enum DiffStatus { Missing, Extra, Modified, Identical }

public record FileDiff(string RelativePath, DiffStatus Status, List<TypeDiff> TypeDiffs);

public record TypeDiff(
    string TypePath,          // e.g.  Controllers\HomeController.cs → HomeController
    DiffStatus Status,
    string? NoteA,            // signature in A (when modified)
    string? NoteB,            // signature in B (when modified)
    List<MemberDiff> MemberDiffs);

public record MemberDiff(
    string Kind,              // field | property | method | constructor | enum-value
    string Name,
    DiffStatus Status,
    string? SignatureA,
    string? SignatureB);

// ── Razor diff types ─────────────────────────────────────────────────────────

public record RazorFileDiff(
    string RelativePath,
    DiffStatus Status,
    List<RazorElementDiff> Elements);  // field-level differences inside the razor file

public record RazorElementDiff(
    string Category,    // Model | Layout | Section | Inject | PartialRef | FormAction | AspFor | Component | ViewBag | ViewData
    string Name,
    DiffStatus Status,
    string? ValueA,
    string? ValueB);

// ── Gap report summary counters ──────────────────────────────────────────────

public record GapSummary(
    int FilesOnlyInA, int FilesOnlyInB, int FilesMatched,
    int TypesOnlyInA, int TypesOnlyInB, int TypesModified,
    int MembersOnlyInA, int MembersOnlyInB, int MembersModified,
    int RazorOnlyInA, int RazorOnlyInB, int RazorModified,
    int RazorElementsOnlyInA, int RazorElementsOnlyInB, int RazorElementsModified,
    int ProjOnlyInA, int ProjOnlyInB, int ProjDiffsCount,
    int SlnProjectsOnlyInA, int SlnProjectsOnlyInB, int SlnProjectsModified);

// ── .csproj diff types ───────────────────────────────────────────────────────

public record CsprojDiff(
    string RelativePath,
    DiffStatus Status,
    List<ProjPropertyDiff> PropertyDiffs);

/// <summary>A single property/package/reference difference inside a .csproj.</summary>
public record ProjPropertyDiff(
    string Category,    // SDK | TargetFramework | OutputType | Nullable | ImplicitUsings | LangVersion | PackageRef | ProjectRef | Property
    string Name,        // property name or package name
    DiffStatus Status,
    string? ValueA,
    string? ValueB);

// ── .sln diff types ──────────────────────────────────────────────────────────

public record SlnDiff(
    string? RelPathA,
    string? RelPathB,
    List<SlnProjectDiff> ProjectDiffs);

public record SlnProjectDiff(
    string Name,
    DiffStatus Status,
    string? PathA,
    string? PathB,
    string? TypeA,
    string? TypeB);

// ── Comparison engine ────────────────────────────────────────────────────────

public class ComparisonEngine
{
    public (List<FileDiff> Diffs, List<RazorFileDiff> RazorDiffs,
            List<CsprojDiff> ProjDiffs, SlnDiff? SlnDiff,
            GapSummary Summary) Compare(
        ProjectSnapshot snapA, ProjectSnapshot snapB)
    {
        var diffs = new List<FileDiff>();

        // Normalise paths for matching (lower-case, forward slashes)
        var filesA = snapA.Files.ToDictionary(f => Norm(f.RelativePath), f => f);
        var filesB = snapB.Files.ToDictionary(f => Norm(f.RelativePath), f => f);

        var allKeys = filesA.Keys.Union(filesB.Keys).OrderBy(k => k).ToList();

        int onlyA = 0, onlyB = 0, matched = 0;
        int typesOnlyA = 0, typesOnlyB = 0, typesMod = 0;
        int memOnlyA = 0, memOnlyB = 0, memMod = 0;

        foreach (var key in allKeys)
        {
            bool inA = filesA.TryGetValue(key, out var fa);
            bool inB = filesB.TryGetValue(key, out var fb);

            if (inA && !inB)
            {
                onlyA++;
                CountAllAsStatus(fa!, DiffStatus.Missing, ref typesOnlyA, ref memOnlyA);
                diffs.Add(new FileDiff(fa!.RelativePath, DiffStatus.Missing, []));
                continue;
            }
            if (!inA && inB)
            {
                onlyB++;
                CountAllAsStatus(fb!, DiffStatus.Extra, ref typesOnlyB, ref memOnlyB);
                diffs.Add(new FileDiff(fb!.RelativePath, DiffStatus.Extra, []));
                continue;
            }

            matched++;
            var typeDiffs = CompareFiles(fa!, fb!,
                ref typesOnlyA, ref typesOnlyB, ref typesMod,
                ref memOnlyA, ref memOnlyB, ref memMod);

            bool anyDiff = typeDiffs.Any(d => d.Status != DiffStatus.Identical);
            diffs.Add(new FileDiff(fa!.RelativePath,
                anyDiff ? DiffStatus.Modified : DiffStatus.Identical,
                typeDiffs));
        }

        // ── Razor comparison ─────────────────────────────────────────────────
        var razorDiffs = new List<RazorFileDiff>();
        int rzOnlyA = 0, rzOnlyB = 0, rzMod = 0;
        int rzElOnlyA = 0, rzElOnlyB = 0, rzElMod = 0;
        CompareRazorFiles(snapA.RazorFiles, snapB.RazorFiles,
            razorDiffs,
            ref rzOnlyA, ref rzOnlyB, ref rzMod,
            ref rzElOnlyA, ref rzElOnlyB, ref rzElMod);

        // ── .csproj comparison ───────────────────────────────────────────────
        var projDiffs = new List<CsprojDiff>();
        int prOnlyA = 0, prOnlyB = 0, prMod = 0;
        CompareCsprojFiles(snapA.CsprojFiles, snapB.CsprojFiles,
            projDiffs, ref prOnlyA, ref prOnlyB, ref prMod);

        // ── .sln comparison ──────────────────────────────────────────────────
        int slnOnlyA = 0, slnOnlyB = 0, slnMod = 0;
        SlnDiff? slnDiff = CompareSlnFiles(snapA.SlnFile, snapB.SlnFile,
            ref slnOnlyA, ref slnOnlyB, ref slnMod);

        var summary = new GapSummary(onlyA, onlyB, matched,
            typesOnlyA, typesOnlyB, typesMod,
            memOnlyA, memOnlyB, memMod,
            rzOnlyA, rzOnlyB, rzMod,
            rzElOnlyA, rzElOnlyB, rzElMod,
            prOnlyA, prOnlyB, prMod,
            slnOnlyA, slnOnlyB, slnMod);

        return (diffs.Where(d => d.Status != DiffStatus.Identical).ToList(),
                razorDiffs.Where(d => d.Status != DiffStatus.Identical).ToList(),
                projDiffs,
                slnDiff,
                summary);
    }

    // ── File-level comparison ────────────────────────────────────────────────

    private static List<TypeDiff> CompareFiles(
        ParsedFile fa, ParsedFile fb,
        ref int typesOnlyA, ref int typesOnlyB, ref int typesMod,
        ref int memOnlyA, ref int memOnlyB, ref int memMod)
    {
        var result = new List<TypeDiff>();
        // Flatten all types across namespaces keyed by MatchKey
        var typesA = FlattenTypes(fa).ToDictionary(t => t.MatchKey);
        var typesB = FlattenTypes(fb).ToDictionary(t => t.MatchKey);

        foreach (var key in typesA.Keys.Union(typesB.Keys).OrderBy(k => k))
        {
            bool inA = typesA.TryGetValue(key, out var ta);
            bool inB = typesB.TryGetValue(key, out var tb);

            string typePath = $"{fa.RelativePath} → {(inA ? ta!.Name : tb!.Name)}";

            if (inA && !inB)
            {
                typesOnlyA++;
                memOnlyA += CountMembers(ta!);
                result.Add(new TypeDiff(typePath, DiffStatus.Missing, null, null, []));
                continue;
            }
            if (!inA && inB)
            {
                typesOnlyB++;
                memOnlyB += CountMembers(tb!);
                result.Add(new TypeDiff(typePath, DiffStatus.Extra, null, null, []));
                continue;
            }

            var memberDiffs = CompareTypes(ta!, tb!,
                ref memOnlyA, ref memOnlyB, ref memMod);

            bool notesDiffer = ta!.BaseList != tb!.BaseList || ta.Modifiers != tb.Modifiers;
            string? noteA = notesDiffer
                ? $"{ta.Accessibility} {ta.Modifiers}{ta.Kind} {ta.Name}{ta.TypeParams}{(ta.BaseList != "" ? " : " + ta.BaseList : "")}"
                : null;
            string? noteB = notesDiffer
                ? $"{tb!.Accessibility} {tb.Modifiers}{tb.Kind} {tb.Name}{tb.TypeParams}{(tb.BaseList != "" ? " : " + tb.BaseList : "")}"
                : null;

            bool anyDiff = memberDiffs.Any(d => d.Status != DiffStatus.Identical) || notesDiffer;
            if (anyDiff) typesMod++;

            result.Add(new TypeDiff(typePath,
                anyDiff ? DiffStatus.Modified : DiffStatus.Identical,
                noteA, noteB, memberDiffs));
        }

        return result;
    }

    // ── Member-level comparison ──────────────────────────────────────────────

    private static List<MemberDiff> CompareTypes(
        ParsedType ta, ParsedType tb,
        ref int memOnlyA, ref int memOnlyB, ref int memMod)
    {
        var result = new List<MemberDiff>();

        // Fields
        DiffMembers("field",
            ta.Fields.Select(f => (f.Name, $"{f.Modifiers}{f.TypeName} {f.Name}")),
            tb.Fields.Select(f => (f.Name, $"{f.Modifiers}{f.TypeName} {f.Name}")),
            result, ref memOnlyA, ref memOnlyB, ref memMod);

        // Properties
        DiffMembers("property",
            ta.Properties.Select(p => (p.Name, p.Signature)),
            tb.Properties.Select(p => (p.Name, p.Signature)),
            result, ref memOnlyA, ref memOnlyB, ref memMod);

        // Constructors
        DiffMembers("constructor",
            ta.Constructors.Select(c => (c.MatchKey, c.Signature)),
            tb.Constructors.Select(c => (c.MatchKey, c.Signature)),
            result, ref memOnlyA, ref memOnlyB, ref memMod);

        // Methods
        DiffMembers("method",
            ta.Methods.Select(m => (m.MatchKey, m.Signature)),
            tb.Methods.Select(m => (m.MatchKey, m.Signature)),
            result, ref memOnlyA, ref memOnlyB, ref memMod);

        // Enum values
        DiffMembers("enum-value",
            ta.EnumValues.Select(v => (v.Name, $"{v.Name}{(v.Value != null ? " = " + v.Value : "")}")),
            tb.EnumValues.Select(v => (v.Name, $"{v.Name}{(v.Value != null ? " = " + v.Value : "")}")),
            result, ref memOnlyA, ref memOnlyB, ref memMod);

        return result;
    }

    private static void DiffMembers(
        string kind,
        IEnumerable<(string Key, string Sig)> aItems,
        IEnumerable<(string Key, string Sig)> bItems,
        List<MemberDiff> result,
        ref int memOnlyA, ref int memOnlyB, ref int memMod)
    {
        var dictA = aItems.GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.First().Sig);
        var dictB = bItems.GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.First().Sig);

        foreach (var key in dictA.Keys.Union(dictB.Keys).OrderBy(k => k))
        {
            bool inA = dictA.TryGetValue(key, out var sigA);
            bool inB = dictB.TryGetValue(key, out var sigB);

            if (inA && !inB)
            {
                memOnlyA++;
                result.Add(new MemberDiff(kind, key, DiffStatus.Missing, sigA, null));
            }
            else if (!inA && inB)
            {
                memOnlyB++;
                result.Add(new MemberDiff(kind, key, DiffStatus.Extra, null, sigB));
            }
            else if (sigA != sigB)
            {
                memMod++;
                result.Add(new MemberDiff(kind, key, DiffStatus.Modified, sigA, sigB));
            }
            else
            {
                result.Add(new MemberDiff(kind, key, DiffStatus.Identical, null, null));
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IEnumerable<ParsedType> FlattenTypes(ParsedFile f)
    {
        foreach (var ns in f.Namespaces)
            foreach (var t in FlattenType(null, ns.Types))
                yield return t;
    }

    private static IEnumerable<ParsedType> FlattenType(ParsedType? _, IEnumerable<ParsedType> types)
    {
        foreach (var t in types)
        {
            yield return t;
            foreach (var n in FlattenType(t, t.NestedTypes))
                yield return n;
        }
    }

    private static int CountMembers(ParsedType t) =>
        t.Fields.Count + t.Properties.Count + t.Constructors.Count +
        t.Methods.Count + t.EnumValues.Count;

    private static void CountAllAsStatus(ParsedFile f, DiffStatus _,
        ref int types, ref int members)
    {
        foreach (var ns in f.Namespaces)
            foreach (var t in ns.Types)
            {
                types++;
                members += CountMembers(t);
            }
    }

    private static string Norm(string path) =>
        path.Replace('\\', '/').ToLowerInvariant();

    // ── .csproj comparison ────────────────────────────────────────────────────

    private static void CompareCsprojFiles(
        List<ParsedCsprojFile> listA, List<ParsedCsprojFile> listB,
        List<CsprojDiff> result,
        ref int onlyA, ref int onlyB, ref int modified)
    {
        var dictA = listA.ToDictionary(f => f.MatchKey);
        var dictB = listB.ToDictionary(f => f.MatchKey);

        foreach (var key in dictA.Keys.Union(dictB.Keys).OrderBy(k => k))
        {
            bool inA = dictA.TryGetValue(key, out var fa);
            bool inB = dictB.TryGetValue(key, out var fb);

            if (inA && !inB) { onlyA++; result.Add(new CsprojDiff(fa!.RelativePath, DiffStatus.Missing, [])); continue; }
            if (!inA && inB) { onlyB++; result.Add(new CsprojDiff(fb!.RelativePath, DiffStatus.Extra,   [])); continue; }

            var propDiffs = DiffCsproj(fa!, fb!);
            bool hasDiff  = propDiffs.Any(p => p.Status != DiffStatus.Identical);
            if (hasDiff) modified++;

            result.Add(new CsprojDiff(fa!.RelativePath,
                hasDiff ? DiffStatus.Modified : DiffStatus.Identical,
                propDiffs.Where(p => p.Status != DiffStatus.Identical).ToList()));
        }
    }

    private static List<ProjPropertyDiff> DiffCsproj(ParsedCsprojFile a, ParsedCsprojFile b)
    {
        var result = new List<ProjPropertyDiff>();
        int dummy = 0;

        // SDK
        ScalarProp("SDK", "Sdk", a.Sdk, b.Sdk, result, ref dummy);

        // Target frameworks (treat as set)
        ProjStringSet("TargetFramework", "TargetFramework", a.TargetFrameworks, b.TargetFrameworks, result, ref dummy, ref dummy);

        // Simple scalar properties
        ScalarProp("OutputType",     "OutputType",     a.OutputType,     b.OutputType,     result, ref dummy);
        ScalarProp("Nullable",       "Nullable",       a.Nullable,       b.Nullable,       result, ref dummy);
        ScalarProp("ImplicitUsings", "ImplicitUsings", a.ImplicitUsings, b.ImplicitUsings, result, ref dummy);
        ScalarProp("LangVersion",    "LangVersion",    a.LangVersion,    b.LangVersion,    result, ref dummy);

        // PackageReferences — keyed by package name, diff on version
        var pkgA = a.PackageReferences.ToDictionary(p => p.Name.ToLowerInvariant(), p => p.Version);
        var pkgB = b.PackageReferences.ToDictionary(p => p.Name.ToLowerInvariant(), p => p.Version);
        var origNameA = a.PackageReferences.ToDictionary(p => p.Name.ToLowerInvariant(), p => p.Name);
        var origNameB = b.PackageReferences.ToDictionary(p => p.Name.ToLowerInvariant(), p => p.Name);

        foreach (var k in pkgA.Keys.Union(pkgB.Keys).OrderBy(k => k))
        {
            bool ia = pkgA.TryGetValue(k, out var va);
            bool ib = pkgB.TryGetValue(k, out var vb);
            string displayName = ia ? origNameA[k] : origNameB[k];

            if (ia && !ib)
                result.Add(new ProjPropertyDiff("PackageRef", displayName, DiffStatus.Missing, $"{displayName} {va}", null));
            else if (!ia && ib)
                result.Add(new ProjPropertyDiff("PackageRef", displayName, DiffStatus.Extra, null, $"{displayName} {vb}"));
            else if (va != vb)
                result.Add(new ProjPropertyDiff("PackageRef", displayName, DiffStatus.Modified, $"{displayName} {va}", $"{displayName} {vb}"));
            else
                result.Add(new ProjPropertyDiff("PackageRef", displayName, DiffStatus.Identical, null, null));
        }

        // ProjectReferences (set)
        ProjStringSet("ProjectRef", "ProjectReference", a.ProjectReferences, b.ProjectReferences, result, ref dummy, ref dummy);

        // OtherProperties
        var otherKeys = a.OtherProperties.Keys
            .Union(b.OtherProperties.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k);
        foreach (var k in otherKeys)
        {
            a.OtherProperties.TryGetValue(k, out var ov);
            b.OtherProperties.TryGetValue(k, out var nv);
            ScalarProp("Property", k, ov, nv, result, ref dummy);
        }

        return result;
    }

    private static void ScalarProp(string category, string name,
        string? a, string? b, List<ProjPropertyDiff> result, ref int mod)
    {
        if (a == b || (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))) return;
        if (!string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
            result.Add(new ProjPropertyDiff(category, name, DiffStatus.Missing, a, null));
        else if (string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b))
            result.Add(new ProjPropertyDiff(category, name, DiffStatus.Extra, null, b));
        else
        { mod++; result.Add(new ProjPropertyDiff(category, name, DiffStatus.Modified, a, b)); }
    }

    private static void ProjStringSet(string category, string name,
        List<string> listA, List<string> listB,
        List<ProjPropertyDiff> result, ref int onlyA, ref int onlyB)
    {
        var setA = new HashSet<string>(listA, StringComparer.OrdinalIgnoreCase);
        var setB = new HashSet<string>(listB, StringComparer.OrdinalIgnoreCase);
        foreach (var v in setA.Except(setB, StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        { onlyA++; result.Add(new ProjPropertyDiff(category, name, DiffStatus.Missing, v, null)); }
        foreach (var v in setB.Except(setA, StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        { onlyB++; result.Add(new ProjPropertyDiff(category, name, DiffStatus.Extra, null, v)); }
    }

    // ── .sln comparison ───────────────────────────────────────────────────────

    private static SlnDiff? CompareSlnFiles(
        ParsedSlnFile? a, ParsedSlnFile? b,
        ref int onlyA, ref int onlyB, ref int modified)
    {
        if (a == null && b == null) return null;

        var projDiffs = new List<SlnProjectDiff>();
        var dictA = (a?.Projects ?? []).ToDictionary(p => p.MatchKey);
        var dictB = (b?.Projects ?? []).ToDictionary(p => p.MatchKey);

        foreach (var key in dictA.Keys.Union(dictB.Keys).OrderBy(k => k))
        {
            bool inA = dictA.TryGetValue(key, out var pa);
            bool inB = dictB.TryGetValue(key, out var pb);

            if (inA && !inB)
            {
                onlyA++;
                projDiffs.Add(new SlnProjectDiff(pa!.Name, DiffStatus.Missing, pa.Path, null, pa.TypeGuid, null));
            }
            else if (!inA && inB)
            {
                onlyB++;
                projDiffs.Add(new SlnProjectDiff(pb!.Name, DiffStatus.Extra, null, pb.Path, null, pb.TypeGuid));
            }
            else
            {
                bool pathDiff = !pa!.Path.Equals(pb!.Path, StringComparison.OrdinalIgnoreCase);
                bool typeDiff = !pa.TypeGuid.Equals(pb.TypeGuid, StringComparison.OrdinalIgnoreCase);
                if (pathDiff || typeDiff)
                {
                    modified++;
                    projDiffs.Add(new SlnProjectDiff(pa.Name, DiffStatus.Modified,
                        pathDiff ? pa.Path : null, pathDiff ? pb.Path : null,
                        typeDiff ? pa.TypeGuid : null, typeDiff ? pb.TypeGuid : null));
                }
            }
        }

        return new SlnDiff(a?.RelativePath, b?.RelativePath, projDiffs);
    }

    // ── Razor file comparison ─────────────────────────────────────────────────

    private static void CompareRazorFiles(
        List<ParsedRazorFile> listA, List<ParsedRazorFile> listB,
        List<RazorFileDiff> result,
        ref int onlyA, ref int onlyB, ref int modified,
        ref int elOnlyA, ref int elOnlyB, ref int elMod)
    {
        var dictA = listA.ToDictionary(f => f.MatchKey);
        var dictB = listB.ToDictionary(f => f.MatchKey);

        foreach (var key in dictA.Keys.Union(dictB.Keys).OrderBy(k => k))
        {
            bool inA = dictA.TryGetValue(key, out var fa);
            bool inB = dictB.TryGetValue(key, out var fb);

            if (inA && !inB)
            {
                onlyA++;
                result.Add(new RazorFileDiff(fa!.RelativePath, DiffStatus.Missing, []));
                continue;
            }
            if (!inA && inB)
            {
                onlyB++;
                result.Add(new RazorFileDiff(fb!.RelativePath, DiffStatus.Extra, []));
                continue;
            }

            var elements = DiffRazorElements(fa!, fb!,
                ref elOnlyA, ref elOnlyB, ref elMod);

            bool hasChanges = elements.Any(e => e.Status != DiffStatus.Identical);
            if (hasChanges) modified++;

            result.Add(new RazorFileDiff(fa!.RelativePath,
                hasChanges ? DiffStatus.Modified : DiffStatus.Identical,
                elements.Where(e => e.Status != DiffStatus.Identical).ToList()));
        }
    }

    private static List<RazorElementDiff> DiffRazorElements(
        ParsedRazorFile fa, ParsedRazorFile fb,
        ref int onlyA, ref int onlyB, ref int modified)
    {
        var result = new List<RazorElementDiff>();

        // Kind
        if (fa.Kind != fb.Kind)
        {
            modified++;
            result.Add(new RazorElementDiff("Kind", "FileKind", DiffStatus.Modified,
                fa.Kind.ToString(), fb.Kind.ToString()));
        }

        // @model
        DiffScalar("Model", "@model", fa.ModelType, fb.ModelType, result, ref modified);

        // @page
        DiffScalar("Page", "@page", fa.PageDirective, fb.PageDirective, result, ref modified);

        // Layout
        DiffScalar("Layout", "Layout", fa.Layout, fb.Layout, result, ref modified);

        // @using
        DiffStringSet("Using", "@using", fa.UsingDirectives, fb.UsingDirectives,
            result, ref onlyA, ref onlyB);

        // @inject
        var injectsA = fa.Injects.Select(i => (i.FieldName, $"{i.ServiceType} {i.FieldName}")).ToList();
        var injectsB = fb.Injects.Select(i => (i.FieldName, $"{i.ServiceType} {i.FieldName}")).ToList();
        DiffPairs("Inject", "@inject", injectsA, injectsB, result, ref onlyA, ref onlyB, ref modified);

        // @section defined
        DiffStringSet("Section", "@section", fa.SectionsDefined, fb.SectionsDefined,
            result, ref onlyA, ref onlyB);

        // @RenderSection
        var rsA = fa.SectionsRendered.Select(s => (s.Name, $"RenderSection(\"{s.Name}\", required:{s.Required})")).ToList();
        var rsB = fb.SectionsRendered.Select(s => (s.Name, $"RenderSection(\"{s.Name}\", required:{s.Required})")).ToList();
        DiffPairs("RenderSection", "@RenderSection", rsA, rsB, result, ref onlyA, ref onlyB, ref modified);

        // Partials
        DiffStringSet("PartialRef", "<partial>", fa.PartialRefs, fb.PartialRefs,
            result, ref onlyA, ref onlyB);

        // Components
        DiffStringSet("Component", "@Component", fa.ComponentRefs, fb.ComponentRefs,
            result, ref onlyA, ref onlyB);

        // ViewBag keys
        DiffStringSet("ViewBag", "ViewBag", fa.ViewBagKeys, fb.ViewBagKeys,
            result, ref onlyA, ref onlyB);

        // ViewData keys
        DiffStringSet("ViewData", "ViewData", fa.ViewDataKeys, fb.ViewDataKeys,
            result, ref onlyA, ref onlyB);

        // Form actions
        var faList = fa.FormActions.Select(f => ($"{f.Controller}/{f.Action}", FormatFormAction(f))).ToList();
        var fbList = fb.FormActions.Select(f => ($"{f.Controller}/{f.Action}", FormatFormAction(f))).ToList();
        DiffPairs("FormAction", "<form>", faList, fbList, result, ref onlyA, ref onlyB, ref modified);

        // asp-for fields
        DiffStringSet("AspFor", "asp-for", fa.AspForFields, fb.AspForFields,
            result, ref onlyA, ref onlyB);

        return result;
    }

    private static void DiffScalar(string category, string name,
        string? a, string? b, List<RazorElementDiff> result, ref int modified)
    {
        if (a == b) return;
        if (a != null && b == null)
            result.Add(new RazorElementDiff(category, name, DiffStatus.Missing, a, null));
        else if (a == null && b != null)
            result.Add(new RazorElementDiff(category, name, DiffStatus.Extra, null, b));
        else
        {
            modified++;
            result.Add(new RazorElementDiff(category, name, DiffStatus.Modified, a, b));
        }
    }

    private static void DiffStringSet(string category, string name,
        List<string> listA, List<string> listB,
        List<RazorElementDiff> result, ref int onlyA, ref int onlyB)
    {
        var setA = new HashSet<string>(listA, StringComparer.Ordinal);
        var setB = new HashSet<string>(listB, StringComparer.Ordinal);
        foreach (var v in setA.Except(setB).OrderBy(x => x))
        { onlyA++; result.Add(new RazorElementDiff(category, name, DiffStatus.Missing, v, null)); }
        foreach (var v in setB.Except(setA).OrderBy(x => x))
        { onlyB++; result.Add(new RazorElementDiff(category, name, DiffStatus.Extra, null, v)); }
    }

    private static void DiffPairs(string category, string name,
        List<(string Key, string Sig)> listA, List<(string Key, string Sig)> listB,
        List<RazorElementDiff> result, ref int onlyA, ref int onlyB, ref int modified)
    {
        var dictA = listA.ToDictionary(x => x.Key, x => x.Sig);
        var dictB = listB.ToDictionary(x => x.Key, x => x.Sig);
        foreach (var key in dictA.Keys.Union(dictB.Keys).OrderBy(k => k))
        {
            bool inA = dictA.TryGetValue(key, out var sA);
            bool inB = dictB.TryGetValue(key, out var sB);
            if (inA && !inB)      { onlyA++;    result.Add(new RazorElementDiff(category, name, DiffStatus.Missing,  sA,   null)); }
            else if (!inA && inB) { onlyB++;    result.Add(new RazorElementDiff(category, name, DiffStatus.Extra,    null, sB));   }
            else if (sA != sB)    { modified++; result.Add(new RazorElementDiff(category, name, DiffStatus.Modified, sA,   sB));   }
        }
    }

    private static string FormatFormAction(RazorFormAction f) =>
        $"controller={f.Controller ?? "*"} action={f.Action ?? "*"} method={f.Method ?? "POST"}{(f.Area != null ? " area=" + f.Area : "")}";
}
