using System.Diagnostics;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class PowerPlanOptimizationHandler : IActionHandler
{
    public string ActionName => "SetPowerPlan";
    
    private const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        // Don't apply ultimate performance if running on battery
        if (context.HardwareProfile.IsLaptop && context.HardwareProfile.BatteryPresent)
        {
            // We could check if AC is connected, but as a safety measure for laptops,
            // we skip forcing Ultimate Performance globally without explicit battery state checks.
            // For now, if it's a laptop, we only allow High Performance or Balanced.
            if (entry.Target == UltimatePerformanceGuid) return TargetState.NotApplicable;
        }
        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var targetGuid = entry.Target ?? UltimatePerformanceGuid;
        var currentGuid = GetCurrentValueDisplay(entry);

        if (currentGuid.Equals(targetGuid, StringComparison.OrdinalIgnoreCase))
        {
            return new OptimizationResult
            {
                Status = ResultStatus.AlreadyOptimized,
                ItemId = targetGuid,
                DisplayName = $"Power Plan already set to {targetGuid}"
            };
        }
        
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"-setactive {targetGuid}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            p?.WaitForExit();
            
            if (p?.ExitCode == 0)
            {
                var newGuid = GetCurrentValueDisplay(entry);
                if (newGuid.Equals(targetGuid, StringComparison.OrdinalIgnoreCase))
                {
                    return new OptimizationResult
                    {
                        Status = ResultStatus.Success,
                        ItemId = targetGuid,
                        DisplayName = $"Set Power Plan to {targetGuid}",
                        
                    };
                }
            }
        }
        catch { }

        return new OptimizationResult
        {
            Status = ResultStatus.Failed,
            ItemId = targetGuid,
            Message = "Failed to apply power plan."
        };
    }


    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = "-getactivescheme",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            var output = p?.StandardOutput.ReadToEnd();
            p?.WaitForExit();

            if (!string.IsNullOrEmpty(output))
            {
                var match = System.Text.RegularExpressions.Regex.Match(output, @"GUID: ([0-9a-f\-]+)");
                if (match.Success) return match.Groups[1].Value;
            }
        }
        catch { }
        return "Unknown";
    }
}
