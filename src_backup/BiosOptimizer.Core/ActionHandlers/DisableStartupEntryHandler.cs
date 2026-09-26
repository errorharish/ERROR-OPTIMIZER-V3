using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class DisableStartupEntryHandler : IActionHandler
{
    private readonly IStartupManager _startupManager;
    public DisableStartupEntryHandler(IStartupManager startupManager)
    {
        _startupManager = startupManager;
    }

    public string ActionName => "DisableStartupEntry";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        return TargetState.Ready; 
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var result = new OptimizationResult { ItemId = entry.Id, DisplayName = entry.DisplayName };
        
        var currentValue = GetCurrentValueDisplay(entry);
        var targetValue = entry.Value?.ToString() ?? "Disabled";

        if (currentValue == targetValue)
        {
            result.Status = ResultStatus.AlreadyOptimized;
            result.Message = "Startup Entry already " + targetValue;
            return result;
        }

        // Basic stub for Phase 1. 
        result.Status = ResultStatus.NotApplicable;
        result.Message = "Not fully implemented in Phase 1 stub.";
        return result;
    }


    public string GetCurrentValueDisplay(OptimizationEntry entry) => "Unknown";
}
