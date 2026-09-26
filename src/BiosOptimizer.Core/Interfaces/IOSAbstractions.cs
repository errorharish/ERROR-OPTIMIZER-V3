namespace BiosOptimizer.Core.Interfaces;

public enum ServiceStartupType { Disabled, Manual, Automatic, AutomaticDelayedStart, Unknown }
public enum ServiceRunningState { Running, Stopped, Paused, Unknown }

public class ServiceState
{
    public string ServiceName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ServiceStartupType StartupType { get; set; }
    public ServiceRunningState RunningState { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string Publisher { get; set; } = "Unknown";
    public string Version { get; set; } = "N/A";
    public string Category { get; set; } = "SYSTEM";
    public string ServiceType { get; set; } = "WIN32_OWN_PROCESS";
    public string Account { get; set; } = "LocalSystem";
    public string Risk { get; set; } = "LOW";
    public string ActionSafety { get; set; } = "UNKNOWN";
    public string ActionSafetyReason { get; set; } = string.Empty;
    public bool IsDelayedStart { get; set; }
    public bool IsCritical { get; set; }
    public bool FileExists { get; set; }
    public bool IsMicrosoft { get; set; }
    public List<string> DependsOn { get; set; } = new();
    public List<string> DependentServices { get; set; } = new();
}

public interface IServiceManager
{
    ServiceState? GetService(string serviceName);
    ServiceStartupType GetStartupType(string serviceName);
    ServiceRunningState GetRunningState(string serviceName);
    bool SetStartupType(string serviceName, ServiceStartupType startupType);
    bool Start(string serviceName);
    bool Stop(string serviceName);
    bool Restart(string serviceName);
    System.Collections.Generic.List<ServiceState> GetAllServices();
}

public enum RegistryValueType { String, DWord, QWord, MultiString, Binary, Unknown }

public class RegistryValueData
{
    public string Hive { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public RegistryValueType ValueType { get; set; }
    public object? Value { get; set; }
}

public interface IRegistryManager
{
    RegistryValueData? ReadValue(string hive, string path, string valueName);
    bool WriteValue(string hive, string path, string valueName, object value, RegistryValueType type);
    bool DeleteValue(string hive, string path, string valueName);
    bool BackupValue(string hive, string path, string valueName, string backupFilePath);
    bool RestoreValue(string backupFilePath);
}
