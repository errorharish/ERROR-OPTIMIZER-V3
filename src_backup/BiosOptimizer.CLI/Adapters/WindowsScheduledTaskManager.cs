using System.Runtime.Versioning;
using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.CLI.Adapters;

[SupportedOSPlatform("windows")]
public class WindowsScheduledTaskManager : IScheduledTaskManager
{
    private dynamic? GetTask(string taskPath)
    {
        try
        {
            Type? type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null) return null;

            dynamic scheduler = Activator.CreateInstance(type)!;
            scheduler.Connect();

            // Split into folder and name. Task path typically starts with "\"
            int lastSlash = taskPath.LastIndexOf('\\');
            if (lastSlash == -1) return null;

            string folderPath = taskPath.Substring(0, lastSlash);
            if (string.IsNullOrEmpty(folderPath)) folderPath = "\\";
            
            string taskName = taskPath.Substring(lastSlash + 1);

            dynamic folder = scheduler.GetFolder(folderPath);
            return folder.GetTask(taskName);
        }
        catch
        {
            return null;
        }
    }

    public ScheduledTaskState? FindTask(string taskPath)
    {
        var task = GetTask(taskPath);
        if (task == null) return null;

        return new ScheduledTaskState
        {
            TaskPath = taskPath,
            TaskName = task.Name,
            State = GetStateFromTask(task)
        };
    }

    public TaskState GetState(string taskPath)
    {
        var task = GetTask(taskPath);
        if (task == null) return TaskState.Unknown;
        return GetStateFromTask(task);
    }

    private TaskState GetStateFromTask(dynamic task)
    {
        // 1 = Disabled, 2 = Queued, 3 = Ready, 4 = Running
        if (task.State == 1) return TaskState.Disabled;
        if (task.State == 2 || task.State == 3 || task.State == 4) return TaskState.Enabled;
        return TaskState.Unknown;
    }

    public bool Enable(string taskPath)
    {
        try
        {
            var task = GetTask(taskPath);
            if (task == null) return false;
            task.Enabled = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Disable(string taskPath)
    {
        try
        {
            var task = GetTask(taskPath);
            if (task == null) return false;
            task.Enabled = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Restore(string taskPath, TaskState originalState)
    {
        if (originalState == TaskState.Enabled) return Enable(taskPath);
        if (originalState == TaskState.Disabled) return Disable(taskPath);
        return false;
    }
}
