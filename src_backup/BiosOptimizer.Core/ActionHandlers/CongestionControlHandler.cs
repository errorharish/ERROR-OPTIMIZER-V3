using System.Diagnostics;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class CongestionControlHandler : IActionHandler
{
    public string ActionName => "SetCongestionControl";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        // Require Windows 10/11
        return (context.IsWindows10 || context.IsWindows11) ? TargetState.Ready : TargetState.NotApplicable;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var targetValue = entry.Value?.ToString() ?? "ctcp"; // ctcp or cubic or default
        var currentValue = GetCurrentValueDisplay(entry);
        
        if (currentValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase))
        {
            return new OptimizationResult
            {
                Status = ResultStatus.AlreadyOptimized,
                ItemId = "CongestionControl",
                DisplayName = $"Congestion Provider already set to {targetValue}"
            };
        }

        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"int tcp set supplemental template=internet congestionprovider={targetValue}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            p?.WaitForExit();
            
            if (p?.ExitCode == 0)
            {
                var newValue = GetCurrentValueDisplay(entry);
                if (newValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase))
                {
                    return new OptimizationResult
                    {
                        Status = ResultStatus.Success,
                        ItemId = "CongestionControl",
                        DisplayName = $"Set Congestion Provider to {targetValue}",
                        
                    };
                }
            }
        }
        catch { }

        return new OptimizationResult
        {
            Status = ResultStatus.Failed,
            ItemId = "CongestionControl",
            Message = "Failed to set Congestion Provider."
        };
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "int tcp show supplemental",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            var output = p?.StandardOutput.ReadToEnd();
            p?.WaitForExit();

            if (!string.IsNullOrEmpty(output))
            {
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("Congestion Control Provider"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length > 1) return parts[1].Trim();
                    }
                }
            }
        }
        catch { }
        return "Unknown";
    }
}
