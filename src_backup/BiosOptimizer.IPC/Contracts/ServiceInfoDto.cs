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
}
