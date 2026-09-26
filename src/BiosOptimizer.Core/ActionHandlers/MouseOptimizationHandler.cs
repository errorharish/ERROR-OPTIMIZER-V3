using Microsoft.Win32;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class MouseOptimizationHandler : IActionHandler
{
    public string ActionName => "SetMouseOptimization";

    private const string TargetKey = @"Control Panel\Mouse";
    private const string ValueName = "MouseSensitivity";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var targetValue = entry.Value?.ToString();
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(TargetKey);
            if (key != null)
            {
                // Capture before state into Universal Backup Manager
                try
                {
                    var prevSens = key.GetValue(ValueName)?.ToString() ?? "Unknown";
                    BiosOptimizer.Core.Implementations.BackupManager.Instance.CaptureInputTweak(
                        "Manual",
                        "Mouse Sensitivity",
                        "HKCU\\Control Panel\\Mouse",
                        $"{ValueName}:{prevSens}",
                        $"{ValueName}:{targetValue ?? "10"}",
                        "SAFE"
                    );
                }
                catch { }

                key.SetValue(ValueName, targetValue ?? "10", RegistryValueKind.String);
                return new OptimizationResult
                {
                    Status = ResultStatus.Success,
                    ItemId = ValueName,
                    DisplayName = "Optimized Mouse Sensitivity/Acceleration (Standard API)",
                    
                };
            }
        }
        catch (Exception ex)
        {
            return new OptimizationResult { Status = ResultStatus.Failed, Message = ex.Message };
        }

        return new OptimizationResult { Status = ResultStatus.Failed, Message = "Could not open key" };
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TargetKey);
            if (key != null)
            {
                var val = key.GetValue(ValueName);
                if (val != null) return val.ToString() ?? "Unknown";
            }
        }
        catch { }
        return "Unknown";
    }
}
