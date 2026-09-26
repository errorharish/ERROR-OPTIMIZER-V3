using System.Diagnostics;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class CpuAffinityHandler : IActionHandler
{
    public string ActionName => "SetCpuAffinity";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        // Block aggressive affinity if the machine has fewer than 4 physical cores
        if (context.PhysicalCoreCount > 0 && context.PhysicalCoreCount < 4)
        {
            return TargetState.NotApplicable;
        }
        
        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var processName = entry.Target;
        var processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
        {
            return new OptimizationResult { Status = ResultStatus.AlreadyOptimized, Message = "Process not running" };
        }

        if (entry.Value == null || !long.TryParse(entry.Value.ToString(), out var targetMask))
        {
            return new OptimizationResult { Status = ResultStatus.Failed, Message = "Invalid mask" };
        }

        var currentMaskStr = GetCurrentValueDisplay(entry);
        if (currentMaskStr == targetMask.ToString())
        {
            return new OptimizationResult { Status = ResultStatus.AlreadyOptimized, Message = "CPU Affinity already set" };
        }

        bool success = false;
        foreach (var p in processes)
        {
            try
            {
                p.ProcessorAffinity = (IntPtr)targetMask;
                success = true;
            }
            catch { }
        }

        if (success)
        {
            var newMaskStr = GetCurrentValueDisplay(entry);
            if (newMaskStr == targetMask.ToString())
            {
                return new OptimizationResult
                {
                    Status = ResultStatus.Success,
                    ItemId = processName,
                    DisplayName = $"Set {processName} CPU Affinity to {targetMask}"
                };
            }
        }

        return new OptimizationResult { Status = ResultStatus.Failed, ItemId = processName, Message = "Failed to set affinity" };
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        var processes = Process.GetProcessesByName(entry.Target);
        if (processes.Length > 0)
        {
            try { return ((long)processes[0].ProcessorAffinity).ToString(); } catch { }
        }
        return "Not Running";
    }
}
