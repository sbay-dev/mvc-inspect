using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

namespace MvcStructureInspector;

public class ProjectInspector
{
    private readonly InspectorOptions _options;
    private static readonly string[] ExcludedDirs = ["bin", "obj", ".git", ".vs", "node_modules"];

    public ProjectInspector(InspectorOptions options)
    {
        _options = options;
    }

    public string Inspect(string rootPath)
    {
        rootPath = Path.GetFullPath(rootPath);
        string projectName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar));
        var sb = new StringBuilder();

        var separator = new string('=', 80);
        sb.AppendLine(separator);
        sb.AppendLine($"  {projectName} — MVC Structure Checklist");
        sb.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Path: {rootPath}");
        sb.AppendLine(separator);
        sb.AppendLine();

        sb.AppendLine($"{projectName}/");
        AppendDirectoryChildren(sb, rootPath, "");

        sb.AppendLine();
        sb.AppendLine(separator);
        sb.AppendLine("  Legend:");
        sb.AppendLine("  +  public    -  private    #  protected    ~  internal");
        sb.AppendLine("  [Attr]  Attribute/decorator applied to member");
        sb.AppendLine(separator);

        return sb.ToString();
    }

    // continuation: prefix placed before each direct child's branch character
    private void AppendDirectoryChildren(StringBuilder sb, string dirPath, string continuation)
    {
        var entries = Directory.GetFileSystemEntries(dirPath)
            .OrderBy(e => File.Exists(e) ? 1 : 0)
            .ThenBy(e => Path.GetFileName(e))
            .ToList();

        for (int i = 0; i < entries.Count; i++)
        {
            string entry = entries[i];
            bool isLast = (i == entries.Count - 1);
            string branch = isLast ? "└── " : "├── ";
            string childContinuation = continuation + (isLast ? "    " : "│   ");

            if (Directory.Exists(entry))
            {
                string dirName = Path.GetFileName(entry);
                if (ExcludedDirs.Contains(dirName, StringComparer.OrdinalIgnoreCase)) continue;
                if (!_options.IncludeMigrations &&
                    dirName.Equals("Migrations", StringComparison.OrdinalIgnoreCase)) continue;

                sb.AppendLine($"{continuation}{branch}{dirName}/");
                AppendDirectoryChildren(sb, entry, childContinuation);
            }
            else
            {
                AppendFile(sb, entry, continuation, branch, childContinuation);
            }
        }
    }

    private void AppendFile(StringBuilder sb, string filePath, string continuation, string branch, string childContinuation)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string fileName = Path.GetFileName(filePath);

        if (_options.CsOnly && ext != ".cs")
            return;

        if (!_options.IncludeViews && ext == ".cshtml")
            return;

        if (ext == ".cs")
        {
            sb.AppendLine($"{continuation}{branch}{fileName}");
            AppendCsFileStructure(sb, filePath, childContinuation);
        }
        else if (ext == ".cshtml" && !_options.CsOnly)
        {
            sb.AppendLine($"{continuation}{branch}{fileName}");
            AppendRazorFileStructure(sb, filePath, childContinuation);
        }
        else if (!_options.CsOnly)
        {
            string annotation = GetFileAnnotation(fileName, ext);
            sb.AppendLine($"{continuation}{branch}{fileName}{annotation}");
        }
    }

    private static string GetFileAnnotation(string fileName, string ext) => ext switch
    {
        ".json"  => "  [JSON config]",
        ".csproj"=> "  [Project file]",
        ".txt"   => "  [Text]",
        _        => ""
    };

    private static void AppendRazorFileStructure(StringBuilder sb, string filePath, string indent)
    {
        var r = RazorParser.Parse(filePath, filePath);

        string kindBadge = r.Kind switch
        {
            RazorFileKind.Layout      => "[Layout]",
            RazorFileKind.PartialView => "[Partial]",
            RazorFileKind.RazorPage   => "[RazorPage]",
            RazorFileKind.ViewImports => "[ViewImports]",
            RazorFileKind.ViewStart   => "[ViewStart]",
            _                         => "[View]"
        };
        sb.AppendLine($"{indent}Type: {kindBadge}");

        if (r.PageDirective != null)
            sb.AppendLine($"{indent}@page \"{r.PageDirective}\"");
        if (r.ModelType != null)
            sb.AppendLine($"{indent}@model {r.ModelType}");
        if (r.Layout != null)
            sb.AppendLine($"{indent}Layout = \"{r.Layout}\"");

        foreach (var inj in r.Injects)
            sb.AppendLine($"{indent}@inject {inj.ServiceType} {inj.FieldName}");

        foreach (var u in r.UsingDirectives)
            sb.AppendLine($"{indent}@using {u}");

        foreach (var sec in r.SectionsDefined)
            sb.AppendLine($"{indent}@section {sec}");

        foreach (var rs in r.SectionsRendered)
            sb.AppendLine($"{indent}@RenderSection(\"{rs.Name}\", required:{rs.Required})");

        if (r.HasRenderBody)
            sb.AppendLine($"{indent}@RenderBody()");

        foreach (var p in r.PartialRefs)
            sb.AppendLine($"{indent}<partial name=\"{p}\">");

        foreach (var c in r.ComponentRefs)
            sb.AppendLine($"{indent}@Component.InvokeAsync(\"{c}\")");

        foreach (var fa in r.FormActions)
        {
            string form = $"<form asp-controller=\"{fa.Controller}\" asp-action=\"{fa.Action}\"";
            if (fa.Area   != null) form += $" asp-area=\"{fa.Area}\"";
            if (fa.Method != null) form += $" method=\"{fa.Method}\"";
            sb.AppendLine($"{indent}{form}>");
        }

        foreach (var f in r.AspForFields)
            sb.AppendLine($"{indent}asp-for=\"{f}\"");

        if (r.ViewBagKeys.Any())
            sb.AppendLine($"{indent}ViewBag: {string.Join(", ", r.ViewBagKeys)}");

        if (r.ViewDataKeys.Any())
            sb.AppendLine($"{indent}ViewData: {string.Join(", ", r.ViewDataKeys.Select(k => $"[\"{k}\"]"))}");
    }

    private void AppendCsFileStructure(StringBuilder sb, string filePath, string indent)
    {
        string code;
        try { code = File.ReadAllText(filePath); }
        catch { sb.AppendLine($"{indent}[!] Cannot read file"); return; }

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var namespaces = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().ToList();

        if (!namespaces.Any())
        {
            // Top-level statements or no namespace
            AppendTopLevelMembers(sb, root, indent);
            return;
        }

        foreach (var ns in namespaces)
        {
            sb.AppendLine($"{indent}namespace: {ns.Name}");
            AppendTypeMembers(sb, ns, indent + "    ");
        }
    }

    private void AppendTopLevelMembers(StringBuilder sb, SyntaxNode root, string indent)
    {
        var types = root.ChildNodes().OfType<TypeDeclarationSyntax>();
        foreach (var type in types)
            AppendTypeDeclaration(sb, type, indent);
    }

    private void AppendTypeMembers(StringBuilder sb, SyntaxNode parent, string indent)
    {
        var types = parent.ChildNodes().OfType<TypeDeclarationSyntax>();
        var enums = parent.ChildNodes().OfType<EnumDeclarationSyntax>();

        foreach (var type in types)
            AppendTypeDeclaration(sb, type, indent);

        foreach (var en in enums)
            AppendEnumDeclaration(sb, en, indent);
    }

    private void AppendTypeDeclaration(StringBuilder sb, TypeDeclarationSyntax type, string indent)
    {
        string keyword = type switch
        {
            ClassDeclarationSyntax => "class",
            InterfaceDeclarationSyntax => "interface",
            StructDeclarationSyntax => "struct",
            RecordDeclarationSyntax r => r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record",
            _ => "type"
        };

        string accessibility = GetAccessibility(type.Modifiers);
        string modifiers = GetExtraModifiers(type.Modifiers);
        string baseList = type.BaseList != null ? $" : {type.BaseList.Types}" : "";
        string typeParams = type.TypeParameterList?.ToString() ?? "";

        string sign = AccessibilityToSign(accessibility);
        sb.AppendLine($"{indent}{sign} {modifiers}{keyword} {type.Identifier}{typeParams}{baseList}");

        string memberIndent = indent + "    ";

        // Fields
        foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
            AppendField(sb, field, memberIndent);

        // Properties
        foreach (var prop in type.Members.OfType<PropertyDeclarationSyntax>())
            AppendProperty(sb, prop, memberIndent);

        // Constructors
        foreach (var ctor in type.Members.OfType<ConstructorDeclarationSyntax>())
            AppendConstructor(sb, ctor, memberIndent);

        // Methods
        foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
            AppendMethod(sb, method, memberIndent);

        // Nested types
        foreach (var nested in type.Members.OfType<TypeDeclarationSyntax>())
            AppendTypeDeclaration(sb, nested, memberIndent);

        // Nested enums
        foreach (var en in type.Members.OfType<EnumDeclarationSyntax>())
            AppendEnumDeclaration(sb, en, memberIndent);
    }

    private void AppendEnumDeclaration(StringBuilder sb, EnumDeclarationSyntax en, string indent)
    {
        string accessibility = GetAccessibility(en.Modifiers);
        string sign = AccessibilityToSign(accessibility);
        sb.AppendLine($"{indent}{sign} enum {en.Identifier}");
        foreach (var member in en.Members)
            sb.AppendLine($"{indent}    | {member.Identifier}{(member.EqualsValue != null ? $" = {member.EqualsValue.Value}" : "")}");
    }

    private void AppendField(StringBuilder sb, FieldDeclarationSyntax field, string indent)
    {
        string accessibility = GetAccessibility(field.Modifiers);
        string modifiers = GetExtraModifiers(field.Modifiers);
        string sign = AccessibilityToSign(accessibility);
        string attrs = GetAttributesSummary(field.AttributeLists);
        string typeName = field.Declaration.Type.ToString();
        foreach (var variable in field.Declaration.Variables)
            sb.AppendLine($"{indent}{attrs}{sign} {modifiers}{typeName} {variable.Identifier}");
    }

    private void AppendProperty(StringBuilder sb, PropertyDeclarationSyntax prop, string indent)
    {
        string accessibility = GetAccessibility(prop.Modifiers);
        string modifiers = GetExtraModifiers(prop.Modifiers);
        string sign = AccessibilityToSign(accessibility);
        string attrs = GetAttributesSummary(prop.AttributeLists);
        string typeName = prop.Type.ToString();

        string accessors = "";
        if (prop.AccessorList != null)
        {
            var parts = prop.AccessorList.Accessors
                .Select(a => a.Keyword.Text)
                .ToList();
            accessors = $" {{ {string.Join("; ", parts)}; }}";
        }
        else if (prop.ExpressionBody != null)
            accessors = " => ...";

        sb.AppendLine($"{indent}{attrs}{sign} {modifiers}{typeName} {prop.Identifier}{accessors}");
    }

    private void AppendConstructor(StringBuilder sb, ConstructorDeclarationSyntax ctor, string indent)
    {
        string accessibility = GetAccessibility(ctor.Modifiers);
        string sign = AccessibilityToSign(accessibility);
        string attrs = GetAttributesSummary(ctor.AttributeLists);
        string parameters = FormatParameters(ctor.ParameterList);
        sb.AppendLine($"{indent}{attrs}{sign} {ctor.Identifier}({parameters})  [constructor]");
    }

    private void AppendMethod(StringBuilder sb, MethodDeclarationSyntax method, string indent)
    {
        string accessibility = GetAccessibility(method.Modifiers);
        string modifiers = GetExtraModifiers(method.Modifiers);
        string sign = AccessibilityToSign(accessibility);
        string attrs = GetAttributesSummary(method.AttributeLists);
        string returnType = method.ReturnType.ToString();
        string typeParams = method.TypeParameterList?.ToString() ?? "";
        string parameters = FormatParameters(method.ParameterList);
        sb.AppendLine($"{indent}{attrs}{sign} {modifiers}{returnType} {method.Identifier}{typeParams}({parameters})");
    }

    private static string GetAccessibility(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(SyntaxKind.PublicKeyword)) return "public";
        if (modifiers.Any(SyntaxKind.PrivateKeyword)) return "private";
        if (modifiers.Any(SyntaxKind.ProtectedKeyword)) return "protected";
        if (modifiers.Any(SyntaxKind.InternalKeyword)) return "internal";
        return "private"; // default
    }

    private static string GetExtraModifiers(SyntaxTokenList modifiers)
    {
        var extras = new List<string>();
        if (modifiers.Any(SyntaxKind.StaticKeyword)) extras.Add("static");
        if (modifiers.Any(SyntaxKind.AbstractKeyword)) extras.Add("abstract");
        if (modifiers.Any(SyntaxKind.VirtualKeyword)) extras.Add("virtual");
        if (modifiers.Any(SyntaxKind.OverrideKeyword)) extras.Add("override");
        if (modifiers.Any(SyntaxKind.SealedKeyword)) extras.Add("sealed");
        if (modifiers.Any(SyntaxKind.AsyncKeyword)) extras.Add("async");
        if (modifiers.Any(SyntaxKind.ReadOnlyKeyword)) extras.Add("readonly");
        if (modifiers.Any(SyntaxKind.ConstKeyword)) extras.Add("const");
        return extras.Count > 0 ? string.Join(" ", extras) + " " : "";
    }

    private static string AccessibilityToSign(string accessibility) => accessibility switch
    {
        "public" => "+",
        "private" => "-",
        "protected" => "#",
        "internal" => "~",
        _ => "?"
    };

    private static string GetAttributesSummary(SyntaxList<AttributeListSyntax> attrLists)
    {
        if (!attrLists.Any()) return "";
        var attrs = attrLists
            .SelectMany(al => al.Attributes)
            .Select(a => a.Name.ToString())
            .ToList();
        return $"[{string.Join(", ", attrs)}] ";
    }

    private static string FormatParameters(ParameterListSyntax paramList)
    {
        var parts = paramList.Parameters.Select(p =>
        {
            string type = p.Type?.ToString() ?? "";
            string name = p.Identifier.Text;
            string def = p.Default != null ? $" = {p.Default.Value}" : "";
            return $"{type} {name}{def}".Trim();
        });
        return string.Join(", ", parts);
    }
}
