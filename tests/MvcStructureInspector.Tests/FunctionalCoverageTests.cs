using Xunit;

namespace MvcStructureInspector.Tests;

public class FunctionalCoverageTests
{
    [Fact]
    public void PageModel_endpoints_are_extracted()
    {
        var snap = BuildSnap("Pages/Account/Register.cshtml.cs",
            "MyApp.Areas.Identity.Pages.Account",
            kind: "class", name: "RegisterModel", baseList: "PageModel",
            methods: new[]
            {
                Method("OnGetAsync", "Task<IActionResult>", isAsync: true),
                Method("OnPostAsync", "Task<IActionResult>", isAsync: true),
            });

        var endpoints = FunctionalCoverageEngine.ExtractEndpoints(snap);

        Assert.Equal(2, endpoints.Count);
        Assert.Contains(endpoints, e => e.FunctionName == "register" && e.HttpVerb == "GET");
        Assert.Contains(endpoints, e => e.FunctionName == "register" && e.HttpVerb == "POST");
        Assert.All(endpoints, e => Assert.Equal("PageModel", e.Pattern));
    }

    [Fact]
    public void Controller_endpoints_are_extracted()
    {
        var snap = BuildSnap("Controllers/AccountController.cs",
            "MyApp.Controllers",
            kind: "class", name: "AccountController", baseList: "Controller",
            methods: new[]
            {
                Method("Register", "IActionResult", attrs: new[] { "HttpPost" }),
                Method("Login", "IActionResult", attrs: new[] { "HttpGet" }),
                Method("Logout", "Task<IActionResult>", attrs: new[] { "HttpPost" }),
            });

        var endpoints = FunctionalCoverageEngine.ExtractEndpoints(snap);

        Assert.Equal(3, endpoints.Count);
        Assert.Contains(endpoints, e => e.FunctionName == "register" && e.HttpVerb == "POST");
        Assert.Contains(endpoints, e => e.FunctionName == "login" && e.HttpVerb == "GET");
        Assert.Contains(endpoints, e => e.FunctionName == "logout" && e.HttpVerb == "POST");
        Assert.All(endpoints, e => Assert.Equal("Controller", e.Pattern));
    }

    [Fact]
    public void PageModel_to_Controller_coverage_is_matched()
    {
        // Project A: Identity Razor Pages
        var snapA = BuildMultiSnap(
            ("Pages/Account/Register.cshtml.cs", "App.Identity.Pages",
                "RegisterModel", "PageModel",
                new[] { Method("OnGetAsync", "Task<IActionResult>"), Method("OnPostAsync", "Task<IActionResult>") }),
            ("Pages/Account/Login.cshtml.cs", "App.Identity.Pages",
                "LoginModel", "PageModel",
                new[] { Method("OnGetAsync", "Task<IActionResult>"), Method("OnPostAsync", "Task<IActionResult>") }),
            ("Pages/Account/Logout.cshtml.cs", "App.Identity.Pages",
                "LogoutModel", "PageModel",
                new[] { Method("OnPostAsync", "Task<IActionResult>") })
        );

        // Project B: MVC Controllers — only Register and Login implemented
        var snapB = BuildSnap("Controllers/AccountController.cs",
            "App.Controllers",
            kind: "class", name: "AccountController", baseList: "Controller",
            methods: new[]
            {
                Method("Register", "IActionResult", attrs: new[] { "HttpGet" }),
                Method("Register", "Task<IActionResult>", attrs: new[] { "HttpPost" }, parameters: "RegisterViewModel model"),
                Method("Login", "IActionResult", attrs: new[] { "HttpGet" }),
                Method("Login", "Task<IActionResult>", attrs: new[] { "HttpPost" }, parameters: "LoginViewModel model"),
            });

        var engine = new FunctionalCoverageEngine();
        var result = engine.Analyze(snapA, snapB);

        // 5 endpoints in A (Register GET/POST, Login GET/POST, Logout POST)
        Assert.Equal(5, result.TotalEndpointsA);
        // Register + Login matched (4 endpoints), Logout not matched (1)
        Assert.True(result.TotalCovered >= 4, $"Expected >= 4 covered, got {result.TotalCovered}");
        Assert.True(result.TotalUncovered >= 1, $"Expected >= 1 uncovered, got {result.TotalUncovered}");
        Assert.True(result.OverallCoveragePercent > 0);
        Assert.True(result.OverallCoveragePercent < 100);
    }

    [Fact]
    public void Full_coverage_yields_100_percent()
    {
        var snapA = BuildSnap("Controllers/HomeController.cs", "App.Controllers",
            kind: "class", name: "HomeController", baseList: "Controller",
            methods: new[] { Method("Index", "IActionResult"), Method("About", "IActionResult") });

        var snapB = BuildSnap("Controllers/HomeController.cs", "App.Controllers",
            kind: "class", name: "HomeController", baseList: "Controller",
            methods: new[] { Method("Index", "IActionResult"), Method("About", "IActionResult") });

        var result = new FunctionalCoverageEngine().Analyze(snapA, snapB);

        Assert.Equal(100.0, result.OverallCoveragePercent);
        Assert.Equal(1, result.FullyCovered);
        Assert.Equal(0, result.NotCovered);
    }

    [Fact]
    public void No_endpoints_yields_null_coverage()
    {
        // Project with no controllers or page models
        var snap = BuildSnap("Models/User.cs", "App.Models",
            kind: "class", name: "User", baseList: "",
            methods: Array.Empty<ParsedMethod>());

        var result = new FunctionalCoverageEngine().Analyze(snap, snap);

        Assert.Equal(0, result.TotalEndpointsA);
    }

    [Fact]
    public void Named_page_handler_extracted_correctly()
    {
        var snap = BuildSnap("Pages/Account/ExternalLogin.cshtml.cs",
            "App.Pages.Account",
            kind: "class", name: "ExternalLoginModel", baseList: "PageModel",
            methods: new[]
            {
                Method("OnGetCallbackAsync", "Task<IActionResult>"),
                Method("OnPostConfirmationAsync", "Task<IActionResult>"),
            });

        var endpoints = FunctionalCoverageEngine.ExtractEndpoints(snap);

        Assert.Equal(2, endpoints.Count);
        Assert.Contains(endpoints, e => e.FunctionName == "externallogin.callback" && e.HttpVerb == "GET");
        Assert.Contains(endpoints, e => e.FunctionName == "externallogin.confirmation" && e.HttpVerb == "POST");
    }

    [Fact]
    public void Infrastructure_methods_are_excluded()
    {
        var snap = BuildSnap("Controllers/BaseController.cs", "App.Controllers",
            kind: "class", name: "BaseController", baseList: "Controller",
            methods: new[]
            {
                Method("Index", "IActionResult"),
                Method("Dispose", "void"),
                Method("OnActionExecuting", "void", parameters: "ActionExecutingContext context"),
            });

        var endpoints = FunctionalCoverageEngine.ExtractEndpoints(snap);

        Assert.Single(endpoints);
        Assert.Equal("index", endpoints[0].FunctionName);
    }

    [Fact]
    public void Coverage_report_section_renders()
    {
        var snapA = BuildSnap("Controllers/AccountController.cs", "App.Controllers",
            kind: "class", name: "AccountController", baseList: "Controller",
            methods: new[]
            {
                Method("Register", "IActionResult", attrs: new[] { "HttpPost" }),
                Method("Login", "IActionResult", attrs: new[] { "HttpGet" }),
            });

        var snapB = BuildSnap("Controllers/AccountController.cs", "App.Controllers",
            kind: "class", name: "AccountController", baseList: "Controller",
            methods: new[] { Method("Register", "IActionResult", attrs: new[] { "HttpPost" }) });

        var (diffs, razorDiffs, staticDiffs, projDiffs, slnDiff, summary, funcCoverage) =
            new ComparisonEngine().Compare(snapA, snapB);

        var formatter = new GapReportFormatter();
        var report = formatter.Format(snapA, snapB, diffs, razorDiffs, staticDiffs,
            projDiffs, slnDiff, summary, funcCoverage);

        Assert.Contains("FUNCTIONAL COVERAGE", report);
        Assert.Contains("50", report); // 50% coverage (1 of 2)
    }

    [Fact]
    public void Non_public_controller_methods_are_excluded()
    {
        var snap = BuildSnap("Controllers/TestController.cs", "App.Controllers",
            kind: "class", name: "TestController", baseList: "Controller",
            methods: new[]
            {
                Method("Index", "IActionResult"),
                PrivateMethod("HelperMethod", "string"),
            });

        var endpoints = FunctionalCoverageEngine.ExtractEndpoints(snap);

        Assert.Single(endpoints);
        Assert.Equal("index", endpoints[0].FunctionName);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ParsedMethod Method(string name, string returnType,
        bool isAsync = false, string[]? attrs = null, string parameters = "")
    {
        return new ParsedMethod("method", "public",
            isAsync ? "async " : "",
            returnType, name, "", parameters,
            attrs?.ToList() ?? []);
    }

    private static ParsedMethod PrivateMethod(string name, string returnType)
    {
        return new ParsedMethod("method", "private", "", returnType, name, "", "", []);
    }

    private static ProjectSnapshot BuildSnap(string filePath, string ns,
        string kind, string name, string baseList, ParsedMethod[] methods)
    {
        var type = new ParsedType(kind, "public", "", name, "", baseList,
            [], [], [], methods.ToList(), [], []);
        var parsedNs = new ParsedNamespace(ns, [type], []);
        var file = new ParsedFile(filePath, ".cs", [parsedNs], []);
        return new ProjectSnapshot("TestProject", "/test", [file], [], [], [], null);
    }

    private static ProjectSnapshot BuildMultiSnap(
        params (string File, string Ns, string ClassName, string BaseList, ParsedMethod[] Methods)[] entries)
    {
        var files = new List<ParsedFile>();
        foreach (var (filePath, ns, className, baseList, methods) in entries)
        {
            var type = new ParsedType("class", "public", "", className, "", baseList,
                [], [], [], methods.ToList(), [], []);
            var parsedNs = new ParsedNamespace(ns, [type], []);
            files.Add(new ParsedFile(filePath, ".cs", [parsedNs], []));
        }
        return new ProjectSnapshot("TestProject", "/test", files, [], [], [], null);
    }
}
