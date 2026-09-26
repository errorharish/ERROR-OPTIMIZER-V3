using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Safety.Mocks;

public class MockEnvironmentDetector : IEnvironmentDetector
{
    public EnvironmentContext Context { get; set; } = new EnvironmentContext
    {
        IsWindows10 = true,
        IsWindows11 = false,
        RamSizeGb = 16,
        PhysicalCoreCount = 8,
        StorageType = "SSD",
        IsDesktop = true,
        HasBattery = false,
        VirtualizationSupported = true
    };

    public EnvironmentContext Detect() => Context;
}

public class MockProcessSnapshot : IProcessSnapshot
{
    public int MockCount { get; set; } = 143;

    public ProcessSnapshotInfo TakeSnapshot()
    {
        return new ProcessSnapshotInfo
        {
            Timestamp = DateTime.UtcNow,
            ProcessCount = MockCount
        };
    }
}

public class MockScheduledTaskManager : IScheduledTaskManager
{
    private readonly Dictionary<string, ScheduledTaskState> _tasks = new(StringComparer.OrdinalIgnoreCase);

    public void AddTask(string path, TaskState state)
    {
        _tasks[path] = new ScheduledTaskState { TaskPath = path, State = state };
    }

    public ScheduledTaskState? FindTask(string taskPath)
    {
        _tasks.TryGetValue(taskPath, out var task);
        return task;
    }

    public TaskState GetState(string taskPath)
    {
        if (_tasks.TryGetValue(taskPath, out var task)) return task.State;
        return TaskState.Unknown;
    }

    public bool Enable(string taskPath)
    {
        if (_tasks.TryGetValue(taskPath, out var task)) { task.State = TaskState.Enabled; return true; }
        return false;
    }

    public bool Disable(string taskPath)
    {
        if (_tasks.TryGetValue(taskPath, out var task)) { task.State = TaskState.Disabled; return true; }
        return false;
    }

    public bool Restore(string taskPath, TaskState originalState)
    {
        if (_tasks.TryGetValue(taskPath, out var task)) { task.State = originalState; return true; }
        return false;
    }
}

public class MockStartupManager : IStartupManager
{
    private readonly List<StartupEntryState> _entries = new();

    public void AddEntry(string source, string path, string name, StartupState state)
    {
        _entries.Add(new StartupEntryState { Source = source, Path = path, ValueName = name, State = state });
    }

    public List<StartupEntryState> GetStartupEntries() => _entries;

    public bool DisableEntry(string source, string path, string valueName)
    {
        var entry = _entries.FirstOrDefault(e => e.Source == source && e.Path == path && e.ValueName == valueName);
        if (entry != null) { entry.State = StartupState.Disabled; return true; }
        return false;
    }

    public bool EnableEntry(string source, string path, string valueName)
    {
        var entry = _entries.FirstOrDefault(e => e.Source == source && e.Path == path && e.ValueName == valueName);
        if (entry != null) { entry.State = StartupState.Enabled; return true; }
        return false;
    }
}
