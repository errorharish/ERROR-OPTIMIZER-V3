using Microsoft.Win32;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class HagsConfigurationHandler : IActionHandler
{
    public string ActionName => "SetHags";

    private const string TargetKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string ValueName = "HwSchMode";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        return context.HardwareProfile.HagsSupported ? TargetState.Ready : TargetState.NotApplicable;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var targetValue = entry.Value?.ToString();
        if (!int.TryParse(targetValue, out var val))
        {
            return new OptimizationResult { Status = ResultStatus.Failed, Message = "Invalid value" };
        }

        var expectedStr = val == 2 ? "Enabled" : "Disabled";
        var currentValue = GetCurrentValueDisplay(entry);
        if (currentValue == expectedStr)
        {
            return new OptimizationResult
            {
                Status = ResultStatus.AlreadyOptimized,
                ItemId = ValueName,
                DisplayName = $"HAGS already {expectedStr}"
            };
        }

        // Capture before state into Universal Backup Manager
        try
        {
            using var readKey = Registry.LocalMachine.OpenSubKey(TargetKey, false);
            var oldRaw = readKey?.GetValue(ValueName);
            string oldState = oldRaw != null
                ? (oldRaw.ToString() == "2" ? "Enabled (2)" : $"Disabled ({oldRaw})")
                : "VALUE DID NOT EXIST";

            BiosOptimizer.Core.Implementations.BackupManager.Instance.CaptureRegistryTweak(
                entry.SourceProfile is { Length: > 0 } sp ? sp : "Manual",
                $"HAGS (Hardware Accelerated GPU Scheduling) \u2192 {expectedStr}",
                "HKLM",
                TargetKey,
                ValueName,
                oldState,
                "DWord",
                val,
                "SAFE"
            );
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(TargetKey, true);
            if (key != null)
            {
                key.SetValue(ValueName, val, RegistryValueKind.DWord);
                
                var newValue = GetCurrentValueDisplay(entry);
                if (newValue == expectedStr)
                {
                    return new OptimizationResult
                    {
                        Status = ResultStatus.RequiresReboot, // HAGS always requires reboot
                        ItemId = ValueName,
                        DisplayName = $"Set HAGS to {expectedStr}",
                        
                        RequiresReboot = true
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
                if (val != null) return val.ToString() == "2" ? "Enabled" : "Disabled";
            }
        }
        catch { }
        return "Unknown";
    }
}
