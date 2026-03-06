namespace MvcStructureInspector;

public class InspectorOptions
{
    public bool IncludeViews { get; set; } = true;
    public bool CsOnly { get; set; } = false;
    public bool IncludeMigrations { get; set; } = true;
    /// <summary>When true, parses .csproj and .sln files for comparison (--with-proj).</summary>
    public bool IncludeProjectFiles { get; set; } = false;
}
