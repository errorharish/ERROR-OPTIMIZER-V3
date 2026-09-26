using System;
using System.Management;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class PagefileOptimizationHandler : IActionHandler
{
    public string ActionName => "SetPagefile";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        // Require sufficient RAM or an SSD to apply aggressive pagefile optimization
        if (!context.HardwareProfile.IsSSD && context.HardwareProfile.RAM < (16L * 1024 * 1024 * 1024))
        {
            return TargetState.NotApplicable;
        }
        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        string targetValue = entry.Target?.ToString() ?? "Auto";
        string currentValue = GetCurrentValueDisplay(entry);

        if (currentValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase))
        {
            return new OptimizationResult
            {
                Status = ResultStatus.AlreadyOptimized,
                ItemId = "Pagefile",
                DisplayName = "Pagefile already " + targetValue
            };
        }

        // Capture before state into Universal Backup Manager
        try
        {
            BiosOptimizer.Core.Implementations.BackupManager.Instance.CaptureGenericTweak(
                entry.SourceProfile is { Length: > 0 } sp ? sp : "Manual",
                "pagefile.automatic_managed",
                $"Pagefile Management \u2192 {targetValue}",
                "Memory",
                "WmiSetting",
                $"AutomaticManagedPagefile: {currentValue}",
                $"AutomaticManagedPagefile: {targetValue}",
                "WmiSettingRestore",
                true,
                "SAFE"
            );
        }
        catch { }

        try
        {
            bool setAuto = targetValue.Equals("Auto", StringComparison.OrdinalIgnoreCase);

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                obj["AutomaticManagedPagefile"] = setAuto;
                obj.Put();
            }

            return new OptimizationResult
            {
                Status = ResultStatus.RequiresReboot,
                ItemId = "Pagefile",
                DisplayName = $"Set Pagefile optimization to {targetValue}",
                
                RequiresReboot = true
            };
        }
        catch (Exception ex)
        {
            return new OptimizationResult
            {
                Status = ResultStatus.Failed,
                ItemId = "Pagefile",
                Message = $"WMI Error: {ex.Message}"
            };
        }
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                var isAuto = (bool)obj["AutomaticManagedPagefile"];
                return isAuto ? "Auto" : "Manual";
            }
        }
        catch { }
        return "Unknown";
    }
}
