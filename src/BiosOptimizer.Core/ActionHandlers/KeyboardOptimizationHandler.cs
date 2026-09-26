using Microsoft.Win32;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class KeyboardOptimizationHandler : IActionHandler
{
    public string ActionName => "SetKeyboardOptimization";

    private const string TargetKey = @"Control Panel\Keyboard";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var currentValue = GetCurrentValueDisplay(entry);
        if (currentValue == "Delay 0") // we set delay 0 and speed 31
        {
            return new OptimizationResult
            {
                Status = ResultStatus.AlreadyOptimized,
                ItemId = "KeyboardSpeed",
                DisplayName = "Keyboard Optimization already applied"
            };
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(TargetKey);
            if (key != null)
            {
                // Capture before state into Universal Backup Manager
                try
                {
                    var prevDelay = key.GetValue("KeyboardDelay")?.ToString() ?? "Unknown";
                    var prevSpeed = key.GetValue("KeyboardSpeed")?.ToString() ?? "Unknown";
                    BiosOptimizer.Core.Implementations.BackupManager.Instance.CaptureInputTweak(
                        "Manual",
                        "Keyboard Optimization",
                        "HKCU\\Control Panel\\Keyboard",
                        $"Delay={prevDelay},Speed={prevSpeed}",
                        "Delay=0,Speed=31",
                        "SAFE"
                    );
                }
                catch { }

                // Standard optimal Windows keyboard delay/speed
                key.SetValue("KeyboardDelay", "0", RegistryValueKind.String);
                key.SetValue("KeyboardSpeed", "31", RegistryValueKind.String);
                
                var newValue = GetCurrentValueDisplay(entry);
                if (newValue == "Delay 0")
                {
                    return new OptimizationResult
                    {
                        Status = ResultStatus.Success,
                        ItemId = "KeyboardSpeed",
                        DisplayName = "Optimized Keyboard Delay and Repeat Rate",
                        
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
            using var key = Registry.CurrentUser.OpenSubKey(TargetKey);
            if (key != null)
            {
                var val = key.GetValue("KeyboardDelay");
                if (val != null) return $"Delay {val}";
            }
        }
        catch { }
        return "Unknown";
    }
}
