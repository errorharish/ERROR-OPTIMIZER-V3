namespace BiosOptimizer.Core.Models;

public class OptimizationCapability
{
    public string EngineId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Supported { get; set; } = true;
    public string Status { get; set; } = "Unknown";
    public string RiskLevel { get; set; } = "Low";
    public bool RequiresAdmin { get; set; } = true;
    public bool RequiresConfirmation { get; set; } = false;
    public bool RequiresReboot { get; set; } = false;
    public bool Reversible { get; set; } = true;
}

public class OptimizationScoreDto
{
    public int OverallScore { get; set; }
    public Dictionary<string, int> CategoryScores { get; set; } = new();
    
    public int TotalAvailable { get; set; }
    public int Applied { get; set; }
    public int Remaining { get; set; }
    public int Skipped { get; set; }
    public int Unsupported { get; set; }
    public int RequiresConfirmation { get; set; }
    public int Failed { get; set; }
}

public class OptimizationSummaryDto
{
    public int TotalOptimizationsAvailable { get; set; }
    public int OptimizationsApplied { get; set; }
    public int RemainingOptimizations { get; set; }
    public int Skipped { get; set; }
    public int Unsupported { get; set; }
    public int RequiresConfirmation { get; set; }
    public int Failed { get; set; }

    public Dictionary<string, int> CategoryBreakdown { get; set; } = new();
}

public class CleanupScanDto
{
    public string Category { get; set; } = string.Empty;
    public long TotalSizeInBytes { get; set; }
    public int FileCount { get; set; }
    public List<string> ScannedPaths { get; set; } = new();
    public bool IsSafeToClean { get; set; }
}

public class NetworkStatusDto
{
    public string ActiveAdapter { get; set; } = string.Empty;
    public string TcpAutoTuningLevel { get; set; } = string.Empty;
    public string CongestionProvider { get; set; } = string.Empty;
    public string NetworkThrottlingIndex { get; set; } = string.Empty;
}
