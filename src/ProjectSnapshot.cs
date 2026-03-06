namespace MvcStructureInspector;

// ─── Parsed project snapshot ────────────────────────────────────────────────

public record ProjectSnapshot(
    string ProjectName,
    string RootPath,
    List<ParsedFile> Files,
    List<ParsedRazorFile> RazorFiles);

// ─── C# file ────────────────────────────────────────────────────────────────

public record ParsedFile(
    string RelativePath,      // e.g. Controllers\HomeController.cs
    string Extension,
    List<ParsedNamespace> Namespaces);

public record ParsedNamespace(
    string Name,
    List<ParsedType> Types);

public record ParsedType(
    string Kind,              // class | interface | struct | record | enum
    string Accessibility,
    string Modifiers,         // static abstract virtual override sealed async readonly
    string Name,
    string TypeParams,
    string BaseList,
    List<ParsedField> Fields,
    List<ParsedProperty> Properties,
    List<ParsedMethod> Constructors,
    List<ParsedMethod> Methods,
    List<ParsedType> NestedTypes,
    List<ParsedEnumValue> EnumValues)
{
    public string MatchKey => $"{Kind}::{Name}{TypeParams}";
}

public record ParsedField(
    string Accessibility,
    string Modifiers,
    string TypeName,
    string Name,
    List<string> Attributes);

public record ParsedProperty(
    string Accessibility,
    string Modifiers,
    string TypeName,
    string Name,
    string Accessors,
    List<string> Attributes)
{
    public string Signature => $"{Modifiers}{TypeName} {Name}{Accessors}".Trim();
}

public record ParsedMethod(
    string Kind,             // method | constructor
    string Accessibility,
    string Modifiers,
    string ReturnType,
    string Name,
    string TypeParams,
    string Parameters,
    List<string> Attributes)
{
    public string Signature => $"{Modifiers}{ReturnType} {Name}{TypeParams}({Parameters})".Trim();
    public string MatchKey  => $"{Name}{TypeParams}({Parameters})";
}

public record ParsedEnumValue(string Name, string? Value);

// ─── Razor (.cshtml) file ────────────────────────────────────────────────────

public record ParsedRazorFile(
    string RelativePath,            // e.g. Views\Home\Index.cshtml
    RazorFileKind Kind,             // View | Layout | PartialView | RazorPage | ViewImports | ViewStart
    string? PageDirective,          // @page "route"  (Razor Pages only)
    string? ModelType,              // @model TypeName
    string? Layout,                 // @{ Layout = "..."; }
    bool    IsLayoutFile,           // contains @RenderBody()
    List<string> UsingDirectives,   // @using Namespace
    List<RazorInject> Injects,      // @inject ServiceType Name
    List<string> SectionsDefined,   // @section Name { ... }
    List<RazorSection> SectionsRendered, // @RenderSection(...)
    bool    HasRenderBody,          // @RenderBody()
    List<string> PartialRefs,       // <partial name> / Html.Partial / PartialAsync
    List<string> ComponentRefs,     // Component.InvokeAsync(...)
    List<string> ViewBagKeys,       // ViewBag.XXX
    List<string> ViewDataKeys,      // ViewData["XXX"]
    List<RazorFormAction> FormActions,  // <form asp-action asp-controller>
    List<string> AspForFields,      // asp-for="..."
    List<RazorTagHelper> TagHelpers // distinct tag-helper usages
)
{
    /// <summary>Stable key for cross-project matching.</summary>
    public string MatchKey => RelativePath.Replace('\\', '/').ToLowerInvariant();
}

public enum RazorFileKind
{
    View,         // Views/**/*.cshtml
    Layout,       // Views/Shared/_Layout*.cshtml or contains @RenderBody
    PartialView,  // name starts with '_' (but not layout/imports/start)
    RazorPage,    // contains @page directive
    ViewImports,  // _ViewImports.cshtml
    ViewStart,    // _ViewStart.cshtml
    Unknown
}

public record RazorInject(string ServiceType, string FieldName);

public record RazorSection(string Name, bool Required);

public record RazorFormAction(
    string? Controller,
    string? Action,
    string? Method,
    string? Area);

public record RazorTagHelper(string TagName, string? AspFor, string? AspAction, string? AspController);

