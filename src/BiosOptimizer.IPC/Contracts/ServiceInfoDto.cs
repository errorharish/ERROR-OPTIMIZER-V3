namespace BiosOptimizer.IPC.Contracts;

public class ServiceInfoDto
{
    public string ServiceName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string StartupType { get; set; } = string.Empty;
    public string RunningState { get; set; } = string.Empty;
    public string Risk { get; set; } = string.Empty;
    public string ActionSafety { get; set; } = "UNKNOWN";
    public string ActionSafetyReason { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Category { get; set; } = "SYSTEM";
    public string ServiceType { get; set; } = "WIN32_OWN_PROCESS";
    public string Account { get; set; } = "LocalSystem";
    public bool IsDelayedStart { get; set; }
    public bool IsCritical { get; set; }
    public bool FileExists { get; set; }
    public bool IsMicrosoft { get; set; }
    public List<string> DependsOn { get; set; } = new();
    public List<string> DependentServices { get; set; } = new();
}
