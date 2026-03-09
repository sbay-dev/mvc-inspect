namespace MvcStructureInspector;

// ── Functional coverage records ──────────────────────────────────────────────

/// <summary>A logical endpoint extracted from either a PageModel handler or Controller action.</summary>
public record FunctionalEndpoint(
    string FunctionName,      // normalized: Register, Login, ConfirmEmail
    string HttpVerb,          // GET | POST | PUT | DELETE | * (unknown)
    string SourceClass,       // RegisterModel, AccountController
    string SourceMethod,      // OnPostAsync, Register
    string SourceNamespace,   // full namespace
    string SourceFile,        // relative file path
    string Pattern,           // PageModel | Controller
    string Signature);        // full method signature for display

/// <summary>Per-namespace functional coverage result.</summary>
public record NamespaceCoverage(
    string NamespaceA,
    string? MatchedNamespaceB,
    int TotalEndpoints,
    int Covered,
    int Uncovered,
    double CoveragePercent,
    List<EndpointMatch> Matches,
    List<FunctionalEndpoint> UncoveredEndpoints);

/// <summary>A matched pair of endpoints across projects.</summary>
public record EndpointMatch(
    FunctionalEndpoint EndpointA,
    FunctionalEndpoint EndpointB,
    string MatchKind);         // Exact | NameOnly | VerbOnly

/// <summary>Aggregate functional coverage summary.</summary>
public record FunctionalCoverageSummary(
    int TotalNamespaces,
    int FullyCovered,
    int PartiallyCovered,
    int NotCovered,
    int TotalEndpointsA,
    int TotalCovered,
    int TotalUncovered,
    double OverallCoveragePercent,
    List<NamespaceCoverage> NamespaceDetails);

// ── Engine ───────────────────────────────────────────────────────────────────

public class FunctionalCoverageEngine
{
    /// <summary>
    /// Analyzes functional endpoint coverage: how many logical operations
    /// from project A's namespaces are implemented in project B, regardless
    /// of whether A uses Razor Pages (PageModel) and B uses MVC Controllers.
    /// </summary>
    public FunctionalCoverageSummary Analyze(ProjectSnapshot snapA, ProjectSnapshot snapB)
    {
        var endpointsA = ExtractEndpoints(snapA);
        var endpointsB = ExtractEndpoints(snapB);

        // Group A endpoints by namespace
        var nsGroupsA = endpointsA
            .GroupBy(e => e.SourceNamespace)
            .OrderBy(g => g.Key)
            .ToList();

        var namespaceCoverages = new List<NamespaceCoverage>();

        foreach (var nsGroup in nsGroupsA)
        {
            var nsEndpoints = nsGroup.ToList();
            var matches = new List<EndpointMatch>();
            var uncovered = new List<FunctionalEndpoint>();
            string? matchedNsB = null;

            foreach (var epA in nsEndpoints)
            {
                var match = FindBestMatch(epA, endpointsB);
                if (match != null)
                {
                    matches.Add(match);
                    matchedNsB ??= match.EndpointB.SourceNamespace;
                }
                else
                {
                    uncovered.Add(epA);
                }
            }

            int total = nsEndpoints.Count;
            int covered = matches.Count;
            double pct = total > 0 ? Math.Round(covered * 100.0 / total, 1) : 0;

            namespaceCoverages.Add(new NamespaceCoverage(
                nsGroup.Key, matchedNsB,
                total, covered, uncovered.Count, pct,
                matches, uncovered));
        }

        int totalNs = namespaceCoverages.Count;
        int fullCov = namespaceCoverages.Count(n => n.CoveragePercent >= 100);
        int partCov = namespaceCoverages.Count(n => n.CoveragePercent > 0 && n.CoveragePercent < 100);
        int noCov   = namespaceCoverages.Count(n => n.CoveragePercent == 0);
        int totalEp = namespaceCoverages.Sum(n => n.TotalEndpoints);
        int totalCo = namespaceCoverages.Sum(n => n.Covered);
        int totalUn = namespaceCoverages.Sum(n => n.Uncovered);
        double overall = totalEp > 0 ? Math.Round(totalCo * 100.0 / totalEp, 1) : 0;

        return new FunctionalCoverageSummary(
            totalNs, fullCov, partCov, noCov,
            totalEp, totalCo, totalUn, overall,
            namespaceCoverages);
    }

    // ── Endpoint extraction ──────────────────────────────────────────────────

    /// <summary>Extracts logical functional endpoints from all C# files.</summary>
    public static List<FunctionalEndpoint> ExtractEndpoints(ProjectSnapshot snap)
    {
        var endpoints = new List<FunctionalEndpoint>();

        foreach (var file in snap.Files)
        {
            foreach (var ns in file.Namespaces)
            {
                foreach (var type in ns.Types)
                {
                    if (IsPageModel(type))
                        endpoints.AddRange(ExtractPageModelEndpoints(type, ns.Name, file.RelativePath));
                    else if (IsController(type))
                        endpoints.AddRange(ExtractControllerEndpoints(type, ns.Name, file.RelativePath));
                }
            }
        }

        return endpoints;
    }

    // ── PageModel detection & extraction ─────────────────────────────────────

    private static bool IsPageModel(ParsedType type) =>
        type.Kind == "class" &&
        (type.BaseList.Contains("PageModel") ||
         type.Name.EndsWith("Model") && HasPageHandlers(type));

    private static bool HasPageHandlers(ParsedType type) =>
        type.Methods.Any(m =>
            m.Name.StartsWith("OnGet", StringComparison.Ordinal) ||
            m.Name.StartsWith("OnPost", StringComparison.Ordinal) ||
            m.Name.StartsWith("OnPut", StringComparison.Ordinal) ||
            m.Name.StartsWith("OnDelete", StringComparison.Ordinal));

    private static List<FunctionalEndpoint> ExtractPageModelEndpoints(
        ParsedType type, string ns, string filePath)
    {
        var endpoints = new List<FunctionalEndpoint>();

        // The page name derives from the class name (strip "Model" suffix)
        string pageName = type.Name.EndsWith("Model")
            ? type.Name[..^5]
            : type.Name;

        foreach (var method in type.Methods)
        {
            if (!IsPageHandler(method.Name)) continue;

            string verb = ExtractVerbFromHandler(method.Name);
            string handlerName = ExtractHandlerName(method.Name);

            // Function = PageName + optional handler suffix
            string functionName = string.IsNullOrEmpty(handlerName)
                ? pageName
                : $"{pageName}.{handlerName}";

            endpoints.Add(new FunctionalEndpoint(
                NormalizeFunctionName(functionName), verb,
                type.Name, method.Name, ns, filePath,
                "PageModel", method.Signature));
        }

        // If the class has no explicit handlers but is a PageModel, register the page itself
        if (!endpoints.Any() && IsPageModel(type))
        {
            endpoints.Add(new FunctionalEndpoint(
                NormalizeFunctionName(pageName), "GET",
                type.Name, "(implicit)", ns, filePath,
                "PageModel", $"{type.Name} : PageModel"));
        }

        return endpoints;
    }

    private static bool IsPageHandler(string methodName) =>
        methodName.StartsWith("OnGet", StringComparison.Ordinal) ||
        methodName.StartsWith("OnPost", StringComparison.Ordinal) ||
        methodName.StartsWith("OnPut", StringComparison.Ordinal) ||
        methodName.StartsWith("OnDelete", StringComparison.Ordinal) ||
        methodName.StartsWith("OnPatch", StringComparison.Ordinal);

    private static string ExtractVerbFromHandler(string handlerName)
    {
        if (handlerName.StartsWith("OnGet", StringComparison.Ordinal)) return "GET";
        if (handlerName.StartsWith("OnPost", StringComparison.Ordinal)) return "POST";
        if (handlerName.StartsWith("OnPut", StringComparison.Ordinal)) return "PUT";
        if (handlerName.StartsWith("OnDelete", StringComparison.Ordinal)) return "DELETE";
        if (handlerName.StartsWith("OnPatch", StringComparison.Ordinal)) return "PATCH";
        return "*";
    }

    /// <summary>
    /// Extracts the named handler portion from a PageModel handler:
    /// OnPostConfirmEmailAsync → ConfirmEmail
    /// OnGetAsync → (empty — default handler)
    /// OnPost → (empty — default handler)
    /// </summary>
    private static string ExtractHandlerName(string methodName)
    {
        // Strip On{Verb} prefix
        string remainder = methodName;
        foreach (var prefix in new[] { "OnGet", "OnPost", "OnPut", "OnDelete", "OnPatch" })
        {
            if (remainder.StartsWith(prefix, StringComparison.Ordinal))
            {
                remainder = remainder[prefix.Length..];
                break;
            }
        }

        // Strip Async suffix
        if (remainder.EndsWith("Async", StringComparison.Ordinal))
            remainder = remainder[..^5];

        return remainder;
    }

    // ── Controller detection & extraction ────────────────────────────────────

    private static bool IsController(ParsedType type) =>
        type.Kind == "class" &&
        (type.BaseList.Contains("Controller") ||
         type.BaseList.Contains("ControllerBase") ||
         type.Name.EndsWith("Controller"));

    private static List<FunctionalEndpoint> ExtractControllerEndpoints(
        ParsedType type, string ns, string filePath)
    {
        var endpoints = new List<FunctionalEndpoint>();

        foreach (var method in type.Methods)
        {
            // Skip non-public and infrastructure methods
            if (method.Accessibility != "public") continue;
            if (IsInfrastructureMethod(method.Name)) continue;

            string verb = ExtractVerbFromAttributes(method.Attributes);
            string functionName = method.Name;

            endpoints.Add(new FunctionalEndpoint(
                NormalizeFunctionName(functionName), verb,
                type.Name, method.Name, ns, filePath,
                "Controller", method.Signature));
        }

        return endpoints;
    }

    private static string ExtractVerbFromAttributes(List<string> attributes)
    {
        foreach (var attr in attributes)
        {
            if (attr.StartsWith("HttpGet", StringComparison.Ordinal)) return "GET";
            if (attr.StartsWith("HttpPost", StringComparison.Ordinal)) return "POST";
            if (attr.StartsWith("HttpPut", StringComparison.Ordinal)) return "PUT";
            if (attr.StartsWith("HttpDelete", StringComparison.Ordinal)) return "DELETE";
            if (attr.StartsWith("HttpPatch", StringComparison.Ordinal)) return "PATCH";
        }
        return "GET"; // default MVC convention
    }

    private static readonly HashSet<string> InfraMethodNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dispose", "ToString", "GetHashCode", "Equals",
        "OnActionExecuting", "OnActionExecuted",
        "OnResultExecuting", "OnResultExecuted",
        "OnException"
    };

    private static bool IsInfrastructureMethod(string name) =>
        InfraMethodNames.Contains(name);

    // ── Matching ─────────────────────────────────────────────────────────────

    private static EndpointMatch? FindBestMatch(
        FunctionalEndpoint epA, List<FunctionalEndpoint> poolB)
    {
        string normA = NormalizeFunctionName(epA.FunctionName);

        // Priority 1: exact name + verb match
        var exact = poolB.FirstOrDefault(b =>
            NormalizeFunctionName(b.FunctionName) == normA &&
            VerbsMatch(epA.HttpVerb, b.HttpVerb));
        if (exact != null)
            return new EndpointMatch(epA, exact, "Exact");

        // Priority 2: name match (any verb)
        var nameMatch = poolB.FirstOrDefault(b =>
            NormalizeFunctionName(b.FunctionName) == normA);
        if (nameMatch != null)
            return new EndpointMatch(epA, nameMatch, "NameOnly");

        // Priority 3: fuzzy — function name contained in method name or vice versa
        var fuzzy = poolB.FirstOrDefault(b =>
            FuzzyNameMatch(normA, NormalizeFunctionName(b.FunctionName)));
        if (fuzzy != null)
            return new EndpointMatch(epA, fuzzy, "Fuzzy");

        return null;
    }

    private static bool VerbsMatch(string a, string b) =>
        a == b || a == "*" || b == "*";

    private static bool FuzzyNameMatch(string a, string b)
    {
        if (a.Length < 3 || b.Length < 3) return false;
        return a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
               b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }

    // ── Normalization ────────────────────────────────────────────────────────

    private static string NormalizeFunctionName(string name)
    {
        // Strip common suffixes
        if (name.EndsWith("Async", StringComparison.Ordinal))
            name = name[..^5];

        // Strip "Confirmation" → "Confirm" style normalization
        // Keep it simple — just lowercase for matching
        return name.ToLowerInvariant();
    }
}
