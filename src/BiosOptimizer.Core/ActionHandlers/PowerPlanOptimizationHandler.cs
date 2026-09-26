using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BiosOptimizer.Core.Implementations.Power;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
using BiosOptimizer.Core.Services;

namespace BiosOptimizer.Core.ActionHandlers;

public class PowerPlanOptimizationHandler : IActionHandler
{
    public string ActionName => "SetPowerPlan";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        if (entry == null) return TargetState.NotApplicable;

        // Safety gate for laptops on battery
        if (context != null && context.IsLaptop && context.HasBattery)
        {
            var (targetName, targetGuid) = ParseTarget(entry);
            if (targetGuid.Equals(PowerPlanEngine.UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase))
                return TargetState.NotApplicable;
        }

        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var result = new OptimizationResult
        {
            ItemId = entry.Id,
            ActionName = ActionName,
            Category = entry.Category,
            DisplayName = entry.DisplayName
        };

        var (targetName, targetGuid) = ParseTarget(entry);
        var (currentGuid, currentName) = PowerPlanEngine.Instance.GetActiveSchemeNative();
        string currentGuidStr = PowerPlanEngine.NormalizeGuid(currentGuid.ToString());

        // 1. Check if already active
        if (currentGuidStr.Equals(targetGuid, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(targetName) && currentName.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
        {
            result.Status = ResultStatus.AlreadyOptimized;
            result.Message = $"Power scheme is already set to {currentName} ({currentGuidStr}).";
            return result;
        }

        // 2. Resolve installed plan or create if needed
        var plans = PowerPlanEngine.Instance.DiscoverPowerPlansAsync().GetAwaiter().GetResult();
        var match = plans.FirstOrDefault(p => p.Guid.Equals(targetGuid, StringComparison.OrdinalIgnoreCase) && p.IsInstalled)
                 ?? plans.FirstOrDefault(p => !string.IsNullOrEmpty(targetName) && p.Name.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0 && p.IsInstalled);

        string guidToApply = match?.Guid ?? targetGuid;

        // If target is Ultimate Performance and not yet installed, enable it
        if ((match == null || !match.IsInstalled) && targetGuid.Equals(PowerPlanEngine.UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase))
        {
            var enableRes = PowerPlanEngine.Instance.EnableUltimatePerformanceAsync().GetAwaiter().GetResult();
            if (enableRes.Success && enableRes.Verified)
            {
                result.Status = ResultStatus.Success;
                result.Message = $"Activated {enableRes.ActiveSchemeName} ({enableRes.ActiveSchemeGuid}) - Verified.";
                return result;
            }
        }

        // 3. Capture before state into Universal Backup Manager
        try
        {
            BiosOptimizer.Core.Implementations.BackupManager.Instance.CapturePowerPlanTweak(
                "Manual",
                targetName,
                currentGuidStr,
                guidToApply,
                "SAFE"
            );
        }
        catch { }

        // Apply and Verify via canonical PowerPlanEngine
        var applyRes = PowerPlanEngine.Instance.ApplyPowerPlanAsync(guidToApply, PowerPlanChangeSource.USER_REQUEST).GetAwaiter().GetResult();
        if (applyRes.Success && applyRes.Verified)
        {
            result.Status = ResultStatus.Success;
            result.Message = $"Activated {applyRes.ActiveSchemeName} ({applyRes.ActiveSchemeGuid}) - Verified.";
            return result;
        }

        result.Status = ResultStatus.Failed;
        result.Message = applyRes.Message;
        return result;
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        var (guid, name) = PowerPlanEngine.Instance.GetActiveSchemeNative();
        string guidStr = PowerPlanEngine.NormalizeGuid(guid.ToString());
        return !string.IsNullOrEmpty(name) ? $"{name} ({guidStr})" : guidStr;
    }

    private static (string targetName, string targetGuid) ParseTarget(OptimizationEntry entry)
    {
        string valStr = entry.Value?.ToString()?.Trim() ?? "";
        string tgtStr = entry.Target?.Trim() ?? "";

        string guid = "";
        string name = "";

        if (Guid.TryParse(valStr, out _)) guid = PowerPlanEngine.NormalizeGuid(valStr);
        else if (Guid.TryParse(tgtStr, out _)) guid = PowerPlanEngine.NormalizeGuid(tgtStr);

        if (string.IsNullOrEmpty(guid))
        {
            if (tgtStr.IndexOf("Ultimate", StringComparison.OrdinalIgnoreCase) >= 0 || valStr.IndexOf("Ultimate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                guid = PowerPlanEngine.UltimatePerformanceGuid;
                name = "Ultimate Performance";
            }
            else if (tgtStr.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0 || valStr.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                guid = PowerPlanEngine.HighPerformanceGuid;
                name = "High Performance";
            }
            else if (tgtStr.IndexOf("Balanced", StringComparison.OrdinalIgnoreCase) >= 0 || valStr.IndexOf("Balanced", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                guid = PowerPlanEngine.BalancedGuid;
                name = "Balanced";
            }
            else
            {
                guid = PowerPlanEngine.HighPerformanceGuid;
                name = "High Performance";
            }
        }
        else
        {
            if (guid.Equals(PowerPlanEngine.UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase)) name = "Ultimate Performance";
            else if (guid.Equals(PowerPlanEngine.HighPerformanceGuid, StringComparison.OrdinalIgnoreCase) || guid.Equals(PowerPlanEngine.HighPerformanceAltGuid, StringComparison.OrdinalIgnoreCase)) name = "High Performance";
            else if (guid.Equals(PowerPlanEngine.BalancedGuid, StringComparison.OrdinalIgnoreCase)) name = "Balanced";
            else if (guid.Equals(PowerPlanEngine.PowerSaverGuid, StringComparison.OrdinalIgnoreCase)) name = "Power Saver";
            else name = !string.IsNullOrEmpty(tgtStr) && !Guid.TryParse(tgtStr, out _) ? tgtStr : "Custom Power Plan";
        }

        return (name, guid);
    }

    public class PowerSchemeInfo
    {
        public string Guid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public static List<PowerSchemeInfo> ListAllSchemes()
    {
        var plans = PowerPlanEngine.Instance.DiscoverPowerPlansAsync().GetAwaiter().GetResult();
        return plans.Select(p => new PowerSchemeInfo
        {
            Guid = p.Guid,
            Name = p.Name,
            IsActive = p.IsActive
        }).ToList();
    }
}


