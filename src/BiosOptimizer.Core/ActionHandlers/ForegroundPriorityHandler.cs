using Microsoft.Win32;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class ForegroundPriorityHandler : IActionHandler
{
    public string ActionName => "SetForegroundPriority";

    private const string TargetKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string ValueName = "Win32PrioritySeparation";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var targetValue = entry.Value?.ToString();
        if (!int.TryParse(targetValue, out var val))
        {
            return new OptimizationResult { Status = ResultStatus.Failed, Message = "Invalid value" };
        }

        var currentValueStr = GetCurrentValueDisplay(entry);
        if (int.TryParse(currentValueStr, out var currentVal) && currentVal == val)
        {
            return new OptimizationResult
            {
                Status = ResultStatus.AlreadyOptimized,
                ItemId = ValueName,
                DisplayName = $"Foreground Priority already {val}"
            };
        }

        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(TargetKey);
            if (key != null)
            {
                key.SetValue(ValueName, val, RegistryValueKind.DWord);
                
                var newValueStr = GetCurrentValueDisplay(entry);
                if (int.TryParse(newValueStr, out var newVal) && newVal == val)
                {
                    return new OptimizationResult
                    {
                        Status = ResultStatus.Success,
                        ItemId = ValueName,
                        DisplayName = $"Set Foreground Priority to {val}"
                    };
                }
            }
        }
        catch (Exception ex)
        {
            return new OptimizationResult { Status = ResultStatus.Failed, Message = ex.Message };
        }

        return new OptimizationResult { Status = ResultStatus.Failed, Message = "Could not open or verify key" };
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(TargetKey);
            if (key != null)
            {
                var val = key.GetValue(ValueName);
                if (val != null) return val.ToString() ?? "0";
            }
        }
        catch { }
        return "Unknown";
    }
}
