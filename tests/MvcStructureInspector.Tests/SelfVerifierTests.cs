using Xunit;

namespace MvcStructureInspector.Tests;

public class SelfVerifierTests
{
    [Fact]
    public void GetVersionInfo_returns_non_empty_values()
    {
        var (ver, product, copyright, framework) = SelfVerifier.GetVersionInfo();
        Assert.False(string.IsNullOrEmpty(ver));
        Assert.False(string.IsNullOrEmpty(product));
        Assert.False(string.IsNullOrEmpty(framework));
    }

    [Fact]
    public void RunAll_returns_non_empty_results()
    {
        var results = SelfVerifier.RunAll();
        Assert.NotEmpty(results);
    }

    [Fact]
    public void RunAll_all_checks_pass()
    {
        var results = SelfVerifier.RunAll();
        var failed = results.Where(r => !r.Passed).ToList();

        if (failed.Count > 0)
        {
            var details = string.Join("\n", failed.Select(f => $"  [{f.Category}] {f.Check}: {f.Detail}"));
            Assert.Fail($"Self-verification failed:\n{details}");
        }
    }

    [Fact]
    public void FormatReport_contains_product_info()
    {
        var results = SelfVerifier.RunAll();
        var report = SelfVerifier.FormatReport(results);

        Assert.Contains("MVC Structure Inspector", report);
        Assert.Contains("Self-Verification Report", report);
        Assert.Contains("Summary:", report);
        Assert.Contains("nuget.org", report);
        Assert.Contains("sbay-dev", report);
    }

    [Fact]
    public void FormatReport_contains_all_categories()
    {
        var results = SelfVerifier.RunAll();
        var report = SelfVerifier.FormatReport(results);

        Assert.Contains("Assembly Integrity", report);
        Assert.Contains("Security Guard", report);
        Assert.Contains("Runtime Compatibility", report);
        Assert.Contains("Dependency Integrity", report);
        Assert.Contains("File System Safety", report);
    }

    [Fact]
    public void FormatReport_shows_pass_status_when_all_pass()
    {
        var results = SelfVerifier.RunAll();
        var allPass = results.All(r => r.Passed);
        var report = SelfVerifier.FormatReport(results);

        if (allPass)
            Assert.Contains("ALL CHECKS PASSED", report);
    }

    [Fact]
    public void VerifyResult_record_has_correct_fields()
    {
        var result = new SelfVerifier.VerifyResult("Cat", "Check", true, "Detail");
        Assert.Equal("Cat", result.Category);
        Assert.Equal("Check", result.Check);
        Assert.True(result.Passed);
        Assert.Equal("Detail", result.Detail);
    }

    [Fact]
    public void Security_guard_checks_cover_critical_extensions()
    {
        var results = SelfVerifier.RunAll();
        Assert.Contains(results, r =>
            r.Category == "Security Guard" && r.Check.Contains("critical extensions") && r.Passed);
    }

    [Fact]
    public void Runtime_checks_verify_dotnet_version()
    {
        var results = SelfVerifier.RunAll();
        Assert.Contains(results, r =>
            r.Category == "Runtime Compatibility" && r.Check.Contains(".NET runtime") && r.Passed);
    }

    [Fact]
    public void Runtime_checks_verify_sha256()
    {
        var results = SelfVerifier.RunAll();
        Assert.Contains(results, r =>
            r.Category == "Runtime Compatibility" && r.Check.Contains("SHA-256") && r.Passed);
    }
}
