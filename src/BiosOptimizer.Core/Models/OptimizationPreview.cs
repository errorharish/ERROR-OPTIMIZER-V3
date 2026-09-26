namespace BiosOptimizer.Core.Models;

public class OptimizationPreview
{
    public int CurrentProcessCount { get; set; }
    public int EstimatedProcessReduction { get; set; }
    public int InheritedEntryCount { get; set; }
    public List<PlannedChange> InheritedChanges { get; set; } = new();
    public List<PlannedChange> PlannedChanges { get; set; } = new();
    public List<SkippedChange> SkippedChanges { get; set; } = new();
    public PerformanceBaseline Baseline { get; set; } = new();
}

public class PlannedChange
{
    public string Id { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsInherited { get; set; }
    public string Current { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string Warning { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExecutionType { get; set; } = "WINDOWS_AUTOMATIC";
}

public class SkippedChange
{
    public string Id { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string ExecutionType { get; set; } = "WINDOWS_AUTOMATIC";
}

