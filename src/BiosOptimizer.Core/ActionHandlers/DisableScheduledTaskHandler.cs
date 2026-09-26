using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class DisableScheduledTaskHandler : IActionHandler
{
    private readonly IScheduledTaskManager _taskManager;

    public DisableScheduledTaskHandler(IScheduledTaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    public string ActionName => "DisableScheduledTask";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        var task = _taskManager.FindTask(entry.Target);
        return task != null ? TargetState.Ready : TargetState.NotAvailable;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var result = new OptimizationResult { ItemId = entry.Id, DisplayName = entry.DisplayName };
        var state = _taskManager.GetState(entry.Target);

        if (state == TaskState.Disabled)
        {
            result.Status = ResultStatus.AlreadyOptimized;
            result.Message = "Task is already disabled.";
            return result;
        }

        // Capture before state into Universal Backup Manager
        try
        {
            BiosOptimizer.Core.Implementations.BackupManager.Instance.CaptureGenericTweak(
                entry.SourceProfile is { Length: > 0 } sp ? sp : "Manual",
                $"task.{entry.Target}".ToLowerInvariant().Replace('\\', '.'),
                $"Scheduled Task: {entry.Target}",
                "Services",
                "ScheduledTask",
                state.ToString(),
                "Disabled",
                "TaskStateRestore",
                false, // Task re-enabling not automated via BackupManager
                "SAFE"
            );
        }
        catch { }

        bool success = _taskManager.Disable(entry.Target);
        if (!success)
        {
            result.Status = ResultStatus.Failed;
            result.Message = "Failed to disable task.";
            return result;
        }

        if (_taskManager.GetState(entry.Target) != TaskState.Disabled)
        {
            result.Status = ResultStatus.Failed;
            result.Message = "Verification failed. Task state is not disabled.";
            return result;
        }

        result.Status = ResultStatus.Success;
        result.Message = "Task disabled successfully.";
        return result;
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        return _taskManager.GetState(entry.Target).ToString();
    }
}
