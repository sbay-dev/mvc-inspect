using System.Text.RegularExpressions;

namespace MvcStructureInspector;

/// <summary>
/// Parses a .cshtml Razor file and extracts its structural elements
/// using regex patterns (no full Razor compiler needed).
/// </summary>
public static class RazorParser
{
    // ── Compiled patterns ────────────────────────────────────────────────────

    private static readonly Regex RxPage      = new(@"@page\s*(""[^""]*"")?", RegexOptions.Multiline);
    private static readonly Regex RxModel     = new(@"@model\s+(\S+)", RegexOptions.Multiline);
    private static readonly Regex RxLayout1   = new(@"Layout\s*=\s*""([^""]+)""", RegexOptions.Multiline);
    private static readonly Regex RxLayout2   = new(@"Layout\s*=\s*null", RegexOptions.Multiline);
    private static readonly Regex RxUsing     = new(@"@using\s+(\S+)", RegexOptions.Multiline);
    private static readonly Regex RxInject    = new(@"@inject\s+(\S+)\s+(\S+)", RegexOptions.Multiline);
    private static readonly Regex RxSection   = new(@"@section\s+(\w+)\s*\{", RegexOptions.Multiline);
    private static readonly Regex RxRenderSec = new(@"@RenderSection\s*\(\s*""([^""]+)""\s*(?:,\s*(true|false))?\s*\)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex RxRenderBody= new(@"@RenderBody\s*\(\s*\)", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    // Partial references
    private static readonly Regex RxPartialTag   = new(@"<partial\s+name=""([^""]+)""", RegexOptions.IgnoreCase);
    private static readonly Regex RxPartialHtml  = new(@"@(?:await\s+)?Html\.(?:Partial|RenderPartial|PartialAsync|RenderPartialAsync)\s*\(\s*""([^""]+)""", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    // View components
    private static readonly Regex RxComponent    = new(@"Component\.InvokeAsync\s*\(\s*""([^""]+)""", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex RxVcTag        = new(@"<vc:(\S+?)\s", RegexOptions.IgnoreCase);

    // ViewBag / ViewData
    private static readonly Regex RxViewBag  = new(@"ViewBag\.([A-Za-z_]\w*)", RegexOptions.Multiline);
    private static readonly Regex RxViewData = new(@"ViewData\[""([^""]+)""\]", RegexOptions.Multiline);

    // Tag helpers on forms
    private static readonly Regex RxForm     = new(@"<form\b([^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex RxAspAttr  = new(@"asp-(action|controller|area|method|for)\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);

    // asp-for on any tag
    private static readonly Regex RxAspFor   = new(@"asp-for\s*=\s*""([^""]*)""", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    // tag-helper usages (input, select, textarea, anchor with asp-* attrs)
    private static readonly Regex RxTagHelper = new(@"<(input|select|textarea|a|label|button)\b[^>]*asp-\w+[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex RxThAttr    = new(@"asp-for\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
    private static readonly Regex RxThAction  = new(@"asp-action\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
    private static readonly Regex RxThController = new(@"asp-controller\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);

    // ── Public entry point ───────────────────────────────────────────────────

    public static ParsedRazorFile Parse(string filePath, string relativePath)
    {
        string raw;
        try { raw = File.ReadAllText(filePath); }
        catch { raw = ""; }

        string fileName = Path.GetFileName(filePath);

        // ── Directives ───────────────────────────────────────────────────────

        var pageMatch = RxPage.Match(raw);
        string? pageDirective = pageMatch.Success
            ? pageMatch.Groups[1].Value.Trim('"', ' ')
            : null;

        string? modelType = RxModel.Match(raw).Groups[1].Value.NullIfEmpty();

        string? layout = null;
        bool layoutNull = RxLayout2.IsMatch(raw);
        var layoutMatch = RxLayout1.Match(raw);
        if (layoutMatch.Success)        layout = layoutMatch.Groups[1].Value;
        else if (layoutNull)            layout = "(null)";

        var usings = RxUsing.Matches(raw)
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();

        var injects = RxInject.Matches(raw)
            .Select(m => new RazorInject(m.Groups[1].Value, m.Groups[2].Value))
            .DistinctBy(x => x.FieldName).ToList();

        // ── Sections ─────────────────────────────────────────────────────────

        var sectionsDefined = RxSection.Matches(raw)
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();

        var sectionsRendered = RxRenderSec.Matches(raw)
            .Select(m => new RazorSection(
                m.Groups[1].Value,
                !m.Groups[2].Success || m.Groups[2].Value.Equals("true", StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(s => s.Name).ToList();

        bool hasRenderBody = RxRenderBody.IsMatch(raw);

        // ── References ───────────────────────────────────────────────────────

        var partials = RxPartialTag.Matches(raw).Select(m => m.Groups[1].Value)
            .Concat(RxPartialHtml.Matches(raw).Select(m => m.Groups[1].Value))
            .Distinct().OrderBy(x => x).ToList();

        var components = RxComponent.Matches(raw).Select(m => m.Groups[1].Value)
            .Concat(RxVcTag.Matches(raw).Select(m => m.Groups[1].Value))
            .Distinct().OrderBy(x => x).ToList();

        // ── ViewBag / ViewData ────────────────────────────────────────────────

        var viewBagKeys = RxViewBag.Matches(raw)
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();

        var viewDataKeys = RxViewData.Matches(raw)
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();

        // ── Form actions ─────────────────────────────────────────────────────

        var formActions = new List<RazorFormAction>();
        foreach (Match fm in RxForm.Matches(raw))
        {
            string attrs = fm.Groups[1].Value;
            string? action     = RxAspAttr.Matches(attrs).FirstOrDefault(m => m.Groups[1].Value.Equals("action",     StringComparison.OrdinalIgnoreCase))?.Groups[2].Value;
            string? controller = RxAspAttr.Matches(attrs).FirstOrDefault(m => m.Groups[1].Value.Equals("controller", StringComparison.OrdinalIgnoreCase))?.Groups[2].Value;
            string? area       = RxAspAttr.Matches(attrs).FirstOrDefault(m => m.Groups[1].Value.Equals("area",       StringComparison.OrdinalIgnoreCase))?.Groups[2].Value;
            string? method     = RxAspAttr.Matches(attrs).FirstOrDefault(m => m.Groups[1].Value.Equals("method",     StringComparison.OrdinalIgnoreCase))?.Groups[2].Value;
            if (action != null || controller != null)
                formActions.Add(new RazorFormAction(controller, action, method, area));
        }

        // ── asp-for fields ───────────────────────────────────────────────────

        var aspForFields = RxAspFor.Matches(raw)
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToList();

        // ── Tag helpers ──────────────────────────────────────────────────────

        var tagHelpers = new List<RazorTagHelper>();
        foreach (Match th in RxTagHelper.Matches(raw))
        {
            string tagContent = th.Value;
            string tagName = th.Groups[1].Value.ToLowerInvariant();
            string? aspFor   = RxThAttr.Match(tagContent).Groups[1].Value.NullIfEmpty();
            string? aspAct   = RxThAction.Match(tagContent).Groups[1].Value.NullIfEmpty();
            string? aspCtrl  = RxThController.Match(tagContent).Groups[1].Value.NullIfEmpty();
            tagHelpers.Add(new RazorTagHelper(tagName, aspFor, aspAct, aspCtrl));
        }
        tagHelpers = tagHelpers.DistinctBy(t => $"{t.TagName}|{t.AspFor}|{t.AspAction}").ToList();

        // ── Determine file kind ──────────────────────────────────────────────

        var kind = DetermineKind(fileName, pageDirective, hasRenderBody, raw);

        return new ParsedRazorFile(
            RelativePath:      relativePath,
            Kind:              kind,
            PageDirective:     pageDirective,
            ModelType:         modelType,
            Layout:            layout,
            IsLayoutFile:      hasRenderBody,
            UsingDirectives:   usings,
            Injects:           injects,
            SectionsDefined:   sectionsDefined,
            SectionsRendered:  sectionsRendered,
            HasRenderBody:     hasRenderBody,
            PartialRefs:       partials,
            ComponentRefs:     components,
            ViewBagKeys:       viewBagKeys,
            ViewDataKeys:      viewDataKeys,
            FormActions:       formActions,
            AspForFields:      aspForFields,
            TagHelpers:        tagHelpers);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RazorFileKind DetermineKind(string fileName, string? pageDirective,
        bool hasRenderBody, string raw)
    {
        if (fileName.Equals("_ViewImports.cshtml", StringComparison.OrdinalIgnoreCase))
            return RazorFileKind.ViewImports;
        if (fileName.Equals("_ViewStart.cshtml", StringComparison.OrdinalIgnoreCase))
            return RazorFileKind.ViewStart;
        if (pageDirective != null)
            return RazorFileKind.RazorPage;
        if (hasRenderBody)
            return RazorFileKind.Layout;
        if (fileName.StartsWith('_'))
            return RazorFileKind.PartialView;
        return RazorFileKind.View;
    }
}

internal static class StringExt
{
    public static string? NullIfEmpty(this string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s;
}
