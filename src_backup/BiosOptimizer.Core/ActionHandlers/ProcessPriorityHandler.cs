using System.Diagnostics;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class ProcessPriorityHandler : IActionHandler
{
    public string ActionName => "SetProcessPriority";

    private readonly HashSet<string> _protectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "csrss", "smss", "wininit", "services", "lsass", "svchost", "dwm", "winlogon", "explorer"
    };


    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        var targetValue = entry.Value?.ToString();
        if (targetValue != null && targetValue.Equals("RealTime", StringComparison.OrdinalIgnoreCase))
        {
            return TargetState.NotApplicable; // Never allow Realtime
        }

        if (_protectedProcesses.Contains(entry.Target))
        {
            return TargetState.NotApplicable;
        }

        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var processName = entry.Target;
        var targetClassStr = entry.Value?.ToString() ?? "Normal";

        if (!Enum.TryParse<ProcessPriorityClass>(targetClassStr, true, out var targetClass))
        {
            return new OptimizationResult { Status = ResultStatus.Failed, Message = "Invalid priority class" };
        }

        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
            // Specifically handling active workload tracking (temporary state)
            return new OptimizationResult { Status = ResultStatus.AlreadyOptimized, Message = "Process not running" };
        }

        var originalPriority = processes[0].PriorityClass.ToString();
        if (originalPriority.Equals(targetClassStr, StringComparison.OrdinalIgnoreCase))
        {
            return new OptimizationResult { Status = ResultStatus.AlreadyOptimized, Message = "Process already at target priority" };
        }

        bool success = false;
        foreach (var p in processes)
        {
            try
            {
                p.PriorityClass = targetClass;
                success = true;
            }
            catch { }
        }

        if (success)
        {
            var verifyValue = GetCurrentValueDisplay(entry);
            if (verifyValue.Equals(targetClassStr, StringComparison.OrdinalIgnoreCase))
            {
                return new OptimizationResult
                {
                    Status = ResultStatus.Success,
                    ItemId = processName,
                    DisplayName = $"Set {processName} Priority to {targetClassStr}"
                };
            }
        }

        return new OptimizationResult { Status = ResultStatus.Failed, ItemId = processName, Message = "Failed to set priority" };
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        var processes = Process.GetProcessesByName(entry.Target);
        if (processes.Length > 0)
        {
            try { return processes[0].PriorityClass.ToString(); } catch { }
        }
        return "Not Running";
    }
}
