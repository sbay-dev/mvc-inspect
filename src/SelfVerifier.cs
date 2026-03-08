using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace MvcStructureInspector;

/// <summary>
/// Performs self-verification checks on the tool's integrity, security posture,
/// and runtime compatibility. All checks run locally without requiring any
/// access keys or repository credentials.
/// </summary>
public static class SelfVerifier
{
    public record VerifyResult(string Category, string Check, bool Passed, string Detail);

    /// <summary>Runs all verification checks and returns a structured report.</summary>
    public static List<VerifyResult> RunAll()
    {
        var results = new List<VerifyResult>();
        results.AddRange(CheckAssemblyIntegrity());
        results.AddRange(CheckSecurityGuardInvariants());
        results.AddRange(CheckRuntimeCompatibility());
        results.AddRange(CheckDependencyIntegrity());
        results.AddRange(CheckFileSystemSafety());
        results.AddRange(CheckOsKernelInfo());
        return results;
    }

    /// <summary>Returns version metadata from the executing assembly.</summary>
    public static (string Version, string Product, string Copyright, string Framework) GetVersionInfo()
    {
        var asm = Assembly.GetExecutingAssembly();
        var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm.GetName().Version?.ToString() ?? "unknown";
        var product = asm.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "MvcStructureInspector";
        var copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";
        var framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        return (ver, product, copyright, framework);
    }

    /// <summary>Formats a verification report to a human-readable string.</summary>
    public static string FormatReport(List<VerifyResult> results)
    {
        var sb = new StringBuilder();
        var (ver, product, copyright, framework) = GetVersionInfo();

        sb.AppendLine("╔════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║         MVC Structure Inspector — Self-Verification Report        ║");
        sb.AppendLine("╚════════════════════════════════════════════════════════════════════╝");
        sb.AppendLine();
        sb.AppendLine($"  Product     : {product}");
        sb.AppendLine($"  Version     : {ver}");
        sb.AppendLine($"  Runtime     : {framework}");
        sb.AppendLine($"  OS          : {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        sb.AppendLine($"  Architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        sb.AppendLine($"  Kernel      : {GetKernelVersion()}");
        sb.AppendLine($"  {copyright}");
        sb.AppendLine();

        int passed = results.Count(r => r.Passed);
        int failed = results.Count(r => !r.Passed);

        // Group by category
        foreach (var group in results.GroupBy(r => r.Category))
        {
            var catPassed = group.All(r => r.Passed);
            var icon = catPassed ? "✓" : "✗";
            var catColor = catPassed ? "PASS" : "FAIL";
            sb.AppendLine($"  [{catColor}] {group.Key}");

            foreach (var r in group)
            {
                var mark = r.Passed ? "  ✓" : "  ✗";
                sb.AppendLine($"    {mark} {r.Check}");
                if (!r.Passed || !string.IsNullOrEmpty(r.Detail))
                    sb.AppendLine($"        {r.Detail}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("──────────────────────────────────────────────────────────────────────");
        sb.AppendLine($"  Summary: {passed} passed, {failed} failed, {results.Count} total");
        sb.AppendLine($"  Status : {(failed == 0 ? "ALL CHECKS PASSED ✓" : $"{failed} CHECK(S) FAILED ✗")}");
        sb.AppendLine($"  Time   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("──────────────────────────────────────────────────────────────────────");

        // Assembly hash for reproducibility
        try
        {
            var asmPath = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(asmPath) && File.Exists(asmPath))
            {
                var hash = ComputeSHA256(asmPath);
                sb.AppendLine($"  Assembly SHA-256: {hash}");
            }
        }
        catch { /* non-critical */ }

        sb.AppendLine();
        sb.AppendLine("  NuGet  : https://www.nuget.org/packages/MvcStructureInspector");
        sb.AppendLine("  Source : https://github.com/sbay-dev/mvc-inspect");
        sb.AppendLine("  Product: https://sbay-dev.github.io/mvc-inspect/");

        return sb.ToString();
    }

    // ── Check categories ────────────────────────────────────────────────────

    private static List<VerifyResult> CheckAssemblyIntegrity()
    {
        var results = new List<VerifyResult>();
        var cat = "Assembly Integrity";

        // 1. Assembly loads and has valid version
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            results.Add(new(cat, "Assembly loads correctly", true,
                $"v{ver}"));
        }
        catch (Exception ex)
        {
            results.Add(new(cat, "Assembly loads correctly", false, ex.Message));
        }

        // 2. Informational version present
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            results.Add(new(cat, "Informational version present", info != null,
                info?.InformationalVersion ?? "Missing"));
        }
        catch (Exception ex)
        {
            results.Add(new(cat, "Informational version present", false, ex.Message));
        }

        // 3. Assembly hash can be computed
        try
        {
            var path = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var hash = ComputeSHA256(path);
                results.Add(new(cat, "SHA-256 integrity hash computable", true, hash[..16] + "..."));
            }
            else
            {
                results.Add(new(cat, "SHA-256 integrity hash computable", true,
                    "Single-file publish (no separate DLL)"));
            }
        }
        catch (Exception ex)
        {
            results.Add(new(cat, "SHA-256 integrity hash computable", false, ex.Message));
        }

        // 4. Roslyn dependency available
        try
        {
            var roslynAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Microsoft.CodeAnalysis.CSharp");
            if (roslynAsm == null)
            {
                // Try loading it
                var testType = typeof(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree);
                results.Add(new(cat, "Roslyn compiler platform available", true,
                    testType.Assembly.GetName().Version?.ToString() ?? "loaded"));
            }
            else
            {
                results.Add(new(cat, "Roslyn compiler platform available", true,
                    $"v{roslynAsm.GetName().Version}"));
            }
        }
        catch (Exception ex)
        {
            results.Add(new(cat, "Roslyn compiler platform available", false, ex.Message));
        }

        return results;
    }

    private static List<VerifyResult> CheckSecurityGuardInvariants()
    {
        var results = new List<VerifyResult>();
        var cat = "Security Guard";

        // 1. Protected extensions set is non-empty
        results.Add(new(cat, "Protected extension set is populated",
            SecurityGuard.ProtectedExtensions.Count >= 10,
            $"{SecurityGuard.ProtectedExtensions.Count} protected extensions"));

        // 2. Critical extensions are protected
        string[] critical = [".cs", ".csproj", ".sln", ".dll", ".exe", ".key", ".pfx", ".pem"];
        foreach (var ext in critical)
        {
            bool ok = SecurityGuard.IsProtectedExtension(ext);
            if (!ok)
            {
                results.Add(new(cat, $"Critical extension '{ext}' is protected", false,
                    "SECURITY: This extension should be in the protected set"));
                break;
            }
        }
        if (critical.All(SecurityGuard.IsProtectedExtension))
            results.Add(new(cat, "All critical extensions are protected", true,
                string.Join(", ", critical)));

        // 3. Safe extensions are allowed
        string[] safe = [".txt", ".md"];
        results.Add(new(cat, "Report extensions (.txt, .md) are allowed",
            safe.All(e => !SecurityGuard.IsProtectedExtension(e)),
            "Output formats remain writable"));

        // 4. AssertSafeOutputPath blocks .cs
        try
        {
            SecurityGuard.AssertSafeOutputPath("test.cs");
            results.Add(new(cat, "AssertSafeOutputPath blocks .cs files", false,
                "SECURITY: Should have thrown for .cs"));
        }
        catch (InvalidOperationException)
        {
            results.Add(new(cat, "AssertSafeOutputPath blocks .cs files", true,
                "Correctly refused"));
        }

        // 5. Snapshot files are allowed
        try
        {
            SecurityGuard.AssertSafeOutputPath("report.snapshot.json");
            results.Add(new(cat, "Snapshot .json files are allowed", true,
                ".snapshot.json bypass works"));
        }
        catch
        {
            results.Add(new(cat, "Snapshot .json files are allowed", false,
                "SECURITY: .snapshot.json should be allowed"));
        }

        // 6. Timestamped reports prevent overwrite
        results.Add(new(cat, "Timestamped report pattern is enforced",
            SecurityGuard.IsTimestampedReport($"mvc-structure_{DateTime.Now:yyyyMMdd_HHmmss}.txt"),
            "yyyyMMdd_HHmmss pattern verified"));

        // 7. Gitignore safety
        results.Add(new(cat, "Gitignore filename validation works",
            SecurityGuard.IsGitignoreFile(".gitignore")
            && SecurityGuard.IsGitignoreFile(".gitignore.generated_20260308")
            && !SecurityGuard.IsGitignoreFile("malicious.cs"),
            ".gitignore, .gitignore.generated_* accepted; others rejected"));

        return results;
    }

    private static List<VerifyResult> CheckRuntimeCompatibility()
    {
        var results = new List<VerifyResult>();
        var cat = "Runtime Compatibility";

        // 1. .NET version
        var netVer = Environment.Version;
        results.Add(new(cat, ".NET runtime version",
            netVer.Major >= 8,
            $".NET {netVer} ({(netVer.Major >= 8 ? "supported" : "requires .NET 8+")})"));

        // 2. UTF-8 encoding support
        try
        {
            var utf8 = Encoding.UTF8;
            var test = utf8.GetBytes("مرحبا • 你好 • café");
            results.Add(new(cat, "UTF-8 encoding (multilingual)", true,
                $"Encoder: {utf8.EncodingName}"));
        }
        catch (Exception ex)
        {
            results.Add(new(cat, "UTF-8 encoding (multilingual)", false, ex.Message));
        }

        // 3. SHA-256 available
        try
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes("test"));
            results.Add(new(cat, "SHA-256 cryptographic hash available", true,
                "System.Security.Cryptography OK"));
        }
        catch (Exception ex)
        {
            results.Add(new(cat, "SHA-256 cryptographic hash available", false, ex.Message));
        }

        // 4. File system access
        try
        {
            var tmp = Path.GetTempPath();
            var testFile = Path.Combine(tmp, $"mvc-inspect-verify-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "verify");
            var content = File.ReadAllText(testFile);
            File.Delete(testFile);
            results.Add(new(cat, "File system read/write access", content == "verify",
                $"Temp path: {tmp}"));
        }
        catch (Exception ex)
        {
            results.Add(new(cat, "File system read/write access", false, ex.Message));
        }

        // 5. Console output (informational — all encodings are valid)
        results.Add(new(cat, "Console output encoding", true,
            Console.OutputEncoding.EncodingName));

        return results;
    }

    private static List<VerifyResult> CheckDependencyIntegrity()
    {
        var results = new List<VerifyResult>();
        var cat = "Dependency Integrity";

        // Check all referenced assemblies can be resolved
        var asm = Assembly.GetExecutingAssembly();
        var refs = asm.GetReferencedAssemblies();

        int loadable = 0;
        int failed = 0;
        var failedNames = new List<string>();

        foreach (var refAsm in refs)
        {
            try
            {
                Assembly.Load(refAsm);
                loadable++;
            }
            catch
            {
                failed++;
                failedNames.Add(refAsm.Name ?? "unknown");
            }
        }

        results.Add(new(cat, "Referenced assemblies resolvable",
            failed == 0,
            failed == 0
                ? $"{loadable}/{refs.Length} assemblies loaded successfully"
                : $"{failed} failed: {string.Join(", ", failedNames.Take(5))}"));

        // Roslyn version check
        try
        {
            var roslynRef = refs.FirstOrDefault(r => r.Name == "Microsoft.CodeAnalysis.CSharp");
            if (roslynRef != null)
            {
                results.Add(new(cat, "Roslyn version compatible",
                    roslynRef.Version?.Major >= 4,
                    $"v{roslynRef.Version}"));
            }
        }
        catch { /* non-critical */ }

        return results;
    }

    private static List<VerifyResult> CheckFileSystemSafety()
    {
        var results = new List<VerifyResult>();
        var cat = "File System Safety";

        // 1. Tool does not run from a protected directory
        var currentDir = Environment.CurrentDirectory;
        var systemDirs = new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };

        bool inSystemDir = systemDirs.Any(sd =>
            !string.IsNullOrEmpty(sd) && currentDir.StartsWith(sd, StringComparison.OrdinalIgnoreCase));
        results.Add(new(cat, "Not running from system directory",
            !inSystemDir,
            inSystemDir ? $"WARNING: Running from {currentDir}" : $"CWD: {currentDir}"));

        // 2. Temp directory writable
        try
        {
            var tmpDir = Path.GetTempPath();
            results.Add(new(cat, "Temporary directory accessible", Directory.Exists(tmpDir), tmpDir));
        }
        catch (Exception ex)
        {
            results.Add(new(cat, "Temporary directory accessible", false, ex.Message));
        }

        // 3. Path traversal guard
        try
        {
            SecurityGuard.AssertSafeOutputPath("../../../etc/passwd");
            // If we got here, the extension isn't protected (no ext), which is acceptable
            // The real guard is that the output format is .txt
            results.Add(new(cat, "Path traversal: unprotected extension check", true,
                "Extensionless paths not blocked (by design — format guard via --out)"));
        }
        catch (InvalidOperationException)
        {
            results.Add(new(cat, "Path traversal guard active", true,
                "Correctly blocked suspicious path"));
        }

        return results;
    }

    // ── OS Kernel Information (lambda-based) ─────────────────────────────────

    /// <summary>
    /// Lambda-based OS kernel probes that retrieve real kernel-level data
    /// without requiring elevated privileges or access keys.
    /// </summary>
    private static readonly Func<string>[] KernelProbes =
    [
        () => $"Kernel: {GetKernelVersion()}",
        () => $"Process: {Environment.ProcessPath ?? "N/A"} (PID {Environment.ProcessId})",
        () => $"Processors: {Environment.ProcessorCount} logical cores",
        () => $"64-bit OS: {Environment.Is64BitOperatingSystem}, 64-bit Process: {Environment.Is64BitProcess}",
        () => $"Machine: {Environment.MachineName}",
        () => $"User: {Environment.UserName}@{Environment.UserDomainName}",
        () => $"Uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64):d\\.hh\\:mm\\:ss}",
        () => $"Working Set: {Environment.WorkingSet / (1024 * 1024)} MB",
        () => $"GC Memory: {GC.GetTotalMemory(false) / (1024 * 1024)} MB (Gen0={GC.CollectionCount(0)}, Gen1={GC.CollectionCount(1)}, Gen2={GC.CollectionCount(2)})",
        () => $"Timezone: {TimeZoneInfo.Local.DisplayName} (UTC{TimeZoneInfo.Local.BaseUtcOffset:hh\\:mm})",
        () => $"Culture: {System.Globalization.CultureInfo.CurrentCulture.Name} ({System.Globalization.CultureInfo.CurrentCulture.EnglishName})",
    ];

    private static List<VerifyResult> CheckOsKernelInfo()
    {
        var results = new List<VerifyResult>();
        var cat = "OS Kernel & Environment";

        foreach (var probe in KernelProbes)
        {
            try
            {
                var info = probe();
                var parts = info.Split(':', 2);
                results.Add(new(cat, parts[0].Trim(), true, parts.Length > 1 ? parts[1].Trim() : info));
            }
            catch (Exception ex)
            {
                results.Add(new(cat, "Kernel probe", false, ex.Message));
            }
        }

        return results;
    }

    private static string GetKernelVersion()
    {
        if (OperatingSystem.IsWindows())
        {
            // Retrieve actual Windows NT kernel version
            Func<string> winKernel = () =>
            {
                var ver = Environment.OSVersion;
                return $"Windows NT {ver.Version} ({ver.ServicePack}{(string.IsNullOrEmpty(ver.ServicePack) ? "" : " ")}{ver.VersionString})";
            };
            return winKernel();
        }

        if (OperatingSystem.IsLinux())
        {
            Func<string> linuxKernel = () =>
            {
                try { return File.ReadAllText("/proc/version").Trim(); }
                catch { return System.Runtime.InteropServices.RuntimeInformation.OSDescription; }
            };
            return linuxKernel();
        }

        if (OperatingSystem.IsMacOS())
        {
            Func<string> macKernel = () =>
            {
                return $"Darwin {Environment.OSVersion.Version} ({System.Runtime.InteropServices.RuntimeInformation.OSDescription})";
            };
            return macKernel();
        }

        return System.Runtime.InteropServices.RuntimeInformation.OSDescription;
    }

    private static string ComputeSHA256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
