namespace MvcStructureInspector;

public class InspectorOptions
{
    public bool IncludeViews { get; set; } = true;
    public bool CsOnly { get; set; } = false;
    public bool IncludeMigrations { get; set; } = true;
}
