using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MvcStructureInspector;

/// <summary>Parses an MVC project folder into a <see cref="ProjectSnapshot"/>.</summary>
public class StructureParser
{
    private static readonly string[] ExcludedDirs = ["bin", "obj", ".git", ".vs", "node_modules"];
    private readonly InspectorOptions _options;

    public StructureParser(InspectorOptions options) { _options = options; }

    public ProjectSnapshot Parse(string rootPath)
    {
        rootPath = Path.GetFullPath(rootPath);
        string name = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar));
        var files      = new List<ParsedFile>();
        var razorFiles = new List<ParsedRazorFile>();
        CollectFiles(rootPath, rootPath, files, razorFiles);
        return new ProjectSnapshot(name, rootPath, files, razorFiles);
    }

    private void CollectFiles(string dir, string root,
        List<ParsedFile> result, List<ParsedRazorFile> razorResult)
    {
        string dirName = Path.GetFileName(dir);
        if (ExcludedDirs.Contains(dirName, StringComparer.OrdinalIgnoreCase)) return;
        if (!_options.IncludeMigrations && dirName.Equals("Migrations", StringComparison.OrdinalIgnoreCase)) return;

        foreach (var file in Directory.GetFiles(dir))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();

            if (ext == ".cs")
            {
                string relative = Path.GetRelativePath(root, file);
                var namespaces = ParseCsFile(file);
                result.Add(new ParsedFile(relative, ext, namespaces));
                continue;
            }

            if (ext == ".cshtml" && !_options.CsOnly)
            {
                string relative = Path.GetRelativePath(root, file);
                razorResult.Add(RazorParser.Parse(file, relative));
            }
        }

        foreach (var sub in Directory.GetDirectories(dir))
            CollectFiles(sub, root, result, razorResult);
    }

    // == Roslyn C# parsing ==================================================

    private static List<ParsedNamespace> ParseCsFile(string path)
    {
        string code;
        try { code = File.ReadAllText(path); }
        catch { return []; }

        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        var result = new List<ParsedNamespace>();

        var nsList = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().ToList();
        if (!nsList.Any())
        {
            var types = ParseTypes(root);
            if (types.Any()) result.Add(new ParsedNamespace("<global>", types));
        }
        else
        {
            foreach (var ns in nsList)
                result.Add(new ParsedNamespace(ns.Name.ToString(), ParseTypes(ns)));
        }
        return result;
    }

    private static List<ParsedType> ParseTypes(SyntaxNode parent)
    {
        var result = new List<ParsedType>();
        foreach (var t in parent.ChildNodes().OfType<TypeDeclarationSyntax>())
            result.Add(BuildType(t));
        foreach (var e in parent.ChildNodes().OfType<EnumDeclarationSyntax>())
            result.Add(BuildEnum(e));
        return result;
    }

    private static ParsedType BuildType(TypeDeclarationSyntax t)
    {
        string kind = t switch
        {
            ClassDeclarationSyntax     => "class",
            InterfaceDeclarationSyntax => "interface",
            StructDeclarationSyntax    => "struct",
            RecordDeclarationSyntax r  => r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record",
            _                          => "type"
        };

        var fields      = t.Members.OfType<FieldDeclarationSyntax>().SelectMany(BuildFields).ToList();
        var properties  = t.Members.OfType<PropertyDeclarationSyntax>().Select(BuildProperty).ToList();
        var ctors       = t.Members.OfType<ConstructorDeclarationSyntax>().Select(BuildConstructor).ToList();
        var methods     = t.Members.OfType<MethodDeclarationSyntax>().Select(BuildMethod).ToList();
        var nested      = t.Members.OfType<TypeDeclarationSyntax>().Select(BuildType).ToList();
        var nestedEnums = t.Members.OfType<EnumDeclarationSyntax>().Select(BuildEnum).ToList();

        return new ParsedType(
            Kind:         kind,
            Accessibility:Accessibility(t.Modifiers),
            Modifiers:    ExtraModifiers(t.Modifiers),
            Name:         t.Identifier.Text,
            TypeParams:   t.TypeParameterList?.ToString() ?? "",
            BaseList:     t.BaseList?.Types.ToString() ?? "",
            Fields:       fields,
            Properties:   properties,
            Constructors: ctors,
            Methods:      methods,
            NestedTypes:  nested.Concat(nestedEnums).ToList(),
            EnumValues:   []);
    }

    private static ParsedType BuildEnum(EnumDeclarationSyntax e)
    {
        var values = e.Members
            .Select(m => new ParsedEnumValue(m.Identifier.Text, m.EqualsValue?.Value.ToString()))
            .ToList();
        return new ParsedType("enum", Accessibility(e.Modifiers), ExtraModifiers(e.Modifiers),
            e.Identifier.Text, "", "", [], [], [], [], [], values);
    }

    private static IEnumerable<ParsedField> BuildFields(FieldDeclarationSyntax f)
    {
        var attrs = Attrs(f.AttributeLists);
        string typeName = f.Declaration.Type.ToString();
        string acc  = Accessibility(f.Modifiers);
        string mods = ExtraModifiers(f.Modifiers);
        return f.Declaration.Variables.Select(v =>
            new ParsedField(acc, mods, typeName, v.Identifier.Text, attrs));
    }

    private static ParsedProperty BuildProperty(PropertyDeclarationSyntax p)
    {
        string acc = "";
        if (p.AccessorList != null)
            acc = "{ " + string.Join("; ", p.AccessorList.Accessors.Select(a => a.Keyword.Text)) + "; }";
        else if (p.ExpressionBody != null)
            acc = "=> ...";

        return new ParsedProperty(
            Accessibility(p.Modifiers), ExtraModifiers(p.Modifiers),
            p.Type.ToString(), p.Identifier.Text, acc, Attrs(p.AttributeLists));
    }

    private static ParsedMethod BuildConstructor(ConstructorDeclarationSyntax c) =>
        new("constructor", Accessibility(c.Modifiers), ExtraModifiers(c.Modifiers),
            "", c.Identifier.Text, "", FormatParams(c.ParameterList), Attrs(c.AttributeLists));

    private static ParsedMethod BuildMethod(MethodDeclarationSyntax m) =>
        new("method", Accessibility(m.Modifiers), ExtraModifiers(m.Modifiers),
            m.ReturnType.ToString(), m.Identifier.Text,
            m.TypeParameterList?.ToString() ?? "",
            FormatParams(m.ParameterList), Attrs(m.AttributeLists));

    // == Helpers =============================================================

    private static string Accessibility(SyntaxTokenList m)
    {
        if (m.Any(SyntaxKind.PublicKeyword))    return "public";
        if (m.Any(SyntaxKind.ProtectedKeyword)) return "protected";
        if (m.Any(SyntaxKind.InternalKeyword))  return "internal";
        return "private";
    }

    private static string ExtraModifiers(SyntaxTokenList m)
    {
        var parts = new List<string>();
        if (m.Any(SyntaxKind.StaticKeyword))   parts.Add("static");
        if (m.Any(SyntaxKind.AbstractKeyword)) parts.Add("abstract");
        if (m.Any(SyntaxKind.VirtualKeyword))  parts.Add("virtual");
        if (m.Any(SyntaxKind.OverrideKeyword)) parts.Add("override");
        if (m.Any(SyntaxKind.SealedKeyword))   parts.Add("sealed");
        if (m.Any(SyntaxKind.AsyncKeyword))    parts.Add("async");
        if (m.Any(SyntaxKind.ReadOnlyKeyword)) parts.Add("readonly");
        if (m.Any(SyntaxKind.ConstKeyword))    parts.Add("const");
        return parts.Count > 0 ? string.Join(" ", parts) + " " : "";
    }

    private static List<string> Attrs(SyntaxList<AttributeListSyntax> lists) =>
        lists.SelectMany(al => al.Attributes).Select(a => a.Name.ToString()).ToList();

    private static string FormatParams(ParameterListSyntax p) =>
        string.Join(", ", p.Parameters.Select(x =>
            $"{x.Type} {x.Identifier}{(x.Default != null ? $" = {x.Default.Value}" : "")}".Trim()));
}
