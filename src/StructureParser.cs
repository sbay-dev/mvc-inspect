using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Security.Cryptography;

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
        var staticFiles = new List<StaticFileEntry>();
        CollectFiles(rootPath, rootPath, files, razorFiles, staticFiles);

        var csprojFiles = _options.IncludeProjectFiles
            ? ProjectFileParser.ParseCsprojFiles(rootPath)
            : [];
        var slnFile = _options.IncludeProjectFiles
            ? ProjectFileParser.ParseSln(rootPath)
            : null;

        return new ProjectSnapshot(name, rootPath, files, razorFiles, staticFiles, csprojFiles, slnFile);
    }

    private void CollectFiles(string dir, string root,
        List<ParsedFile> result, List<ParsedRazorFile> razorResult,
        List<StaticFileEntry> staticResult)
    {
        string dirName = Path.GetFileName(dir);
        if (ExcludedDirs.Contains(dirName, StringComparer.OrdinalIgnoreCase)) return;
        if (!_options.IncludeMigrations && dirName.Equals("Migrations", StringComparison.OrdinalIgnoreCase)) return;

        bool isStaticDir = IsStaticAssetDir(dir, root);

        foreach (var file in Directory.GetFiles(dir))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();

            if (ext == ".cs" && !isStaticDir)
            {
                string relative = Path.GetRelativePath(root, file);
                var (namespaces, lambdas) = ParseCsFileWithLambdas(file);
                result.Add(new ParsedFile(relative, ext, namespaces, lambdas));
                continue;
            }

            if (ext == ".cshtml" && !_options.CsOnly && !isStaticDir)
            {
                string relative = Path.GetRelativePath(root, file);
                razorResult.Add(RazorParser.Parse(file, relative));
                continue;
            }

            if (isStaticDir && !_options.CsOnly)
            {
                string relative = Path.GetRelativePath(root, file);
                staticResult.Add(BuildStaticEntry(file, relative, ext));
            }
        }

        foreach (var sub in Directory.GetDirectories(dir))
            CollectFiles(sub, root, result, razorResult, staticResult);
    }

    /// <summary>True if <paramref name="dir"/> is under wwwroot/ or is wwwroot itself.</summary>
    private static bool IsStaticAssetDir(string dir, string root)
    {
        string rel = Path.GetRelativePath(root, dir).Replace('\\', '/').ToLowerInvariant();
        return rel == "wwwroot" || rel.StartsWith("wwwroot/");
    }

    private static StaticFileEntry BuildStaticEntry(string filePath, string relative, string ext)
    {
        var fi = new FileInfo(filePath);
        string hash;
        try
        {
            using var stream = File.OpenRead(filePath);
            hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch { hash = ""; }
        return new StaticFileEntry(relative, ext, fi.Length, hash);
    }

    // == Roslyn C# parsing ==================================================

    private static List<ParsedNamespace> ParseCsFile(string path)
    {
        var (namespaces, _) = ParseCsFileWithLambdas(path);
        return namespaces;
    }

    private static (List<ParsedNamespace> Namespaces, List<ParsedLambdaProperty> Lambdas) ParseCsFileWithLambdas(string path)
    {
        string code;
        try { code = File.ReadAllText(path); }
        catch { return ([], []); }

        var root = CSharpSyntaxTree.ParseText(code,
            new CSharpParseOptions(LanguageVersion.CSharp14)).GetRoot();
        var namespaces = new List<ParsedNamespace>();

        var nsList = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().ToList();
        if (!nsList.Any())
        {
            var types = ParseTypes(root);
            var delegates = ParseDelegates(root);
            if (types.Any() || delegates.Any())
                namespaces.Add(new ParsedNamespace("<global>", types, delegates));
        }
        else
        {
            foreach (var ns in nsList)
                namespaces.Add(new ParsedNamespace(ns.Name.ToString(), ParseTypes(ns), ParseDelegates(ns)));
        }

        var lambdas = ParseLambdas(root);
        return (namespaces, lambdas);
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

    private static List<ParsedDelegate> ParseDelegates(SyntaxNode parent) =>
        parent.ChildNodes().OfType<DelegateDeclarationSyntax>()
            .Select(d => new ParsedDelegate(
                Accessibility(d.Modifiers),
                ExtraModifiers(d.Modifiers),
                d.ReturnType.ToString(),
                d.Identifier.Text,
                d.TypeParameterList?.ToString() ?? "",
                FormatParams(d.ParameterList),
                Attrs(d.AttributeLists)))
            .ToList();

    private static List<ParsedLambdaProperty> ParseLambdas(SyntaxNode root)
    {
        var results = new List<ParsedLambdaProperty>();

        foreach (var lambda in root.DescendantNodes().OfType<LambdaExpressionSyntax>())
        {
            var containingType = lambda.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            string typeName = containingType?.Identifier.Text ?? "<top-level>";

            var (memberName, memberKind) = ResolveLambdaOwner(lambda);

            string lambdaKind = lambda switch
            {
                SimpleLambdaExpressionSyntax    => "SimpleLambda",
                ParenthesizedLambdaExpressionSyntax => "ParenthesizedLambda",
                _ => "Lambda"
            };

            string parameters = lambda switch
            {
                SimpleLambdaExpressionSyntax s => s.Parameter.ToString(),
                ParenthesizedLambdaExpressionSyntax p => p.ParameterList.ToString(),
                _ => ""
            };

            bool isAsync  = lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
            bool isStatic = lambda.Modifiers.Any(SyntaxKind.StaticKeyword);

            int bodyLines = lambda.Body switch
            {
                BlockSyntax block => block.Statements.Count,
                _ => 0
            };

            // Infer return type from lambda syntax (C# 14 explicit return types)
            string? inferredReturn = lambda switch
            {
                ParenthesizedLambdaExpressionSyntax { ReturnType: not null } p => p.ReturnType.ToString(),
                _ => null
            };

            var captured = ExtractCapturedIdentifiers(lambda);

            results.Add(new ParsedLambdaProperty(
                typeName, memberName, memberKind, lambdaKind,
                parameters, inferredReturn, isAsync, isStatic,
                bodyLines, captured));
        }

        // Also capture anonymous method expressions
        foreach (var anon in root.DescendantNodes().OfType<AnonymousMethodExpressionSyntax>())
        {
            var containingType = anon.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            string typeName = containingType?.Identifier.Text ?? "<top-level>";
            var (memberName, memberKind) = ResolveLambdaOwner(anon);

            bool isAsync = anon.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
            string parameters = anon.ParameterList?.ToString() ?? "";

            int bodyLines = anon.Block.Statements.Count;
            var captured = ExtractCapturedIdentifiers(anon);

            results.Add(new ParsedLambdaProperty(
                typeName, memberName, memberKind, "AnonymousMethod",
                parameters, null, isAsync, false, bodyLines, captured));
        }

        return results;
    }

    private static (string Name, string Kind) ResolveLambdaOwner(SyntaxNode lambda)
    {
        // Walk ancestors to find the containing member
        foreach (var ancestor in lambda.Ancestors())
        {
            switch (ancestor)
            {
                case PropertyDeclarationSyntax prop:
                    return (prop.Identifier.Text, "property");
                case FieldDeclarationSyntax field:
                    var varName = field.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "?";
                    return (varName, "field");
                case MethodDeclarationSyntax method:
                    return (method.Identifier.Text, "method");
                case ConstructorDeclarationSyntax ctor:
                    return (ctor.Identifier.Text, "constructor");
                case LocalDeclarationStatementSyntax local:
                    var localVar = local.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "?";
                    return (localVar, "local");
                case VariableDeclaratorSyntax varDecl:
                    return (varDecl.Identifier.Text, "variable");
                case ArgumentSyntax:
                    return ("<argument>", "argument");
                case LambdaExpressionSyntax:
                    // Nested lambda — skip to outer owner
                    continue;
            }
        }
        return ("<unknown>", "unknown");
    }

    private static List<string> ExtractCapturedIdentifiers(SyntaxNode lambda)
    {
        // Find identifiers referenced inside the lambda that are declared outside it
        var innerIdentifiers = lambda.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(id => id.Identifier.Text)
            .Distinct()
            .ToList();

        // Identifiers declared inside the lambda body
        var declaredInside = new HashSet<string>();
        foreach (var decl in lambda.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            declaredInside.Add(decl.Identifier.Text);
        foreach (var param in lambda.DescendantNodes().OfType<ParameterSyntax>())
            declaredInside.Add(param.Identifier.Text);

        // Lambda's own parameters
        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simple:
                declaredInside.Add(simple.Parameter.Identifier.Text);
                break;
            case ParenthesizedLambdaExpressionSyntax paren:
                foreach (var p in paren.ParameterList.Parameters)
                    declaredInside.Add(p.Identifier.Text);
                break;
        }

        // Exclude common keywords/types/well-known identifiers
        var excluded = new HashSet<string>(StringComparer.Ordinal)
        {
            "var", "string", "int", "bool", "double", "float", "long", "decimal",
            "object", "void", "null", "true", "false", "this", "base", "value",
            "nameof", "typeof", "default", "throw", "new", "async", "await",
            "Console", "Math", "String", "Task", "List", "Dictionary"
        };

        return innerIdentifiers
            .Where(id => !declaredInside.Contains(id) && !excluded.Contains(id))
            .OrderBy(id => id)
            .Take(20) // cap to avoid noise
            .ToList();
    }

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
