namespace BiosOptimizer.Core.Interfaces;

public enum TaskState { Enabled, Disabled, Unknown }

public class ScheduledTaskState
{
    public string TaskPath { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public TaskState State { get; set; }
}

public interface IScheduledTaskManager
{
    ScheduledTaskState? FindTask(string taskPath);
    TaskState GetState(string taskPath);
    bool Enable(string taskPath);
    bool Disable(string taskPath);
    bool Restore(string taskPath, TaskState originalState);
}

public enum StartupState { Enabled, Disabled, Unknown }

public class StartupEntryState
{
    public string Source { get; set; } = string.Empty;
    public string FriendlySource { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Publisher { get; set; } = "Unknown";
    public string ExePath { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public string Version { get; set; } = "N/A";
    public object? OriginalValue { get; set; }
    public StartupState State { get; set; }
    public string Category { get; set; } = "USER APPLICATION";
    public string Impact { get; set; } = "UNKNOWN";
    public string ImpactMethod { get; set; } = "NOT MEASURED";
    public bool IsSystemItem { get; set; }
    public bool RequiresAdmin { get; set; }
    public bool RequiresRestart { get; set; }
    public bool FileExists { get; set; }
}

public interface IStartupManager
{
    List<StartupEntryState> GetStartupEntries();
    bool DisableEntry(string source, string path, string valueName);
    bool EnableEntry(string source, string path, string valueName);
}
