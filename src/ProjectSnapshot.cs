namespace MvcStructureInspector;

// ─── Parsed project snapshot ────────────────────────────────────────────────

public record ProjectSnapshot(
    string ProjectName,
    string RootPath,
    List<ParsedFile> Files,
    List<ParsedRazorFile> RazorFiles,
    List<StaticFileEntry> StaticFiles,
    List<ParsedCsprojFile> CsprojFiles,
    ParsedSlnFile? SlnFile);

/// <summary>A non-code file found under wwwroot or similar static asset directories.</summary>
public record StaticFileEntry(
    string RelativePath,      // e.g. wwwroot/css/site.css
    string Extension,
    long   SizeBytes,
    string ContentHash);      // SHA-256 for content comparison

// ─── C# file ────────────────────────────────────────────────────────────────

public record ParsedFile(
    string RelativePath,      // e.g. Controllers\HomeController.cs
    string Extension,
    List<ParsedNamespace> Namespaces,
    List<ParsedLambdaProperty> Lambdas);

public record ParsedNamespace(
    string Name,
    List<ParsedType> Types,
    List<ParsedDelegate> Delegates);

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

// ─── Delegate declarations ──────────────────────────────────────────────────

public record ParsedDelegate(
    string Accessibility,
    string Modifiers,
    string ReturnType,
    string Name,
    string TypeParams,
    string Parameters,
    List<string> Attributes)
{
    public string Signature => $"{Modifiers}{ReturnType} {Name}{TypeParams}({Parameters})".Trim();
    public string MatchKey  => $"delegate::{Name}{TypeParams}({Parameters})";
}

// ─── Lambda / anonymous function expressions ────────────────────────────────

public record ParsedLambdaProperty(
    string ContainingType,
    string MemberName,           // field, property, or variable name
    string MemberKind,           // field | property | local | parameter | argument
    string LambdaKind,           // SimpleLambda | ParenthesizedLambda | AnonymousMethod
    string Parameters,           // e.g. "(x, y)" or "x"
    string? InferredReturnType,  // when resolvable from syntax
    bool IsAsync,
    bool IsStatic,
    int  BodyLineCount,          // block body line count (0 = expression body)
    List<string> CapturedIdentifiers)  // identifiers referenced from enclosing scope
{
    public string Signature => $"{LambdaKind} {Parameters} => ...{(IsAsync ? " [async]" : "")}{(IsStatic ? " [static]" : "")}";
}

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

// ─── .csproj file ────────────────────────────────────────────────────────────

public record ParsedCsprojFile(
    string RelativePath,
    string Sdk,
    List<string> TargetFrameworks,
    string? OutputType,
    string? Nullable,
    string? ImplicitUsings,
    string? LangVersion,
    List<PackageRef> PackageReferences,
    List<string> ProjectReferences,
    Dictionary<string, string> OtherProperties)
{
    public string MatchKey => RelativePath.Replace('\\', '/').ToLowerInvariant();
}

public record PackageRef(string Name, string Version);

// ─── .sln file ───────────────────────────────────────────────────────────────

public record ParsedSlnFile(
    string RelativePath,
    List<SlnProject> Projects);

public record SlnProject(string TypeGuid, string Name, string Path)
{
    public string MatchKey => Name.ToLowerInvariant();
}

