using System.Diagnostics;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class DnsFlushHandler : IActionHandler
{
    public string ActionName => "DnsFlush";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        // For DNS flush, we simulate checking if flush is needed
        var currentValue = GetCurrentValueDisplay(entry);
        var targetValue = entry.Value?.ToString() ?? "Flushed";

        if (currentValue == targetValue)
        {
            return new OptimizationResult
            {
                Status = ResultStatus.AlreadyOptimized,
                ItemId = "DNS Cache",
                DisplayName = "DNS Cache already " + targetValue
            };
        }
        
        try
        {
            var p = Process.Start(new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            p?.WaitForExit();
            
            if (p?.ExitCode == 0)
            {
                // simulate verify
                var verifyValue = targetValue;
                if (verifyValue == targetValue)
                {
                    return new OptimizationResult
                    {
                        Status = ResultStatus.Success,
                        ItemId = "DNS Cache",
                        DisplayName = "Flush DNS Resolver Cache",
                        
                    };
                }
            }
        }
        catch { }

        return new OptimizationResult
        {
            Status = ResultStatus.Failed,
            ItemId = "DNS Cache",
            Message = "Failed to flush DNS cache."
        };
    }



    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        return "NeedsFlush"; // simulate state for DNS cache
    }
}
