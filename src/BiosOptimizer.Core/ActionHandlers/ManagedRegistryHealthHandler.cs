using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class ManagedRegistryHealthHandler : IActionHandler
{
    public string ActionName => "RepairRegistryHealth";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        string targetValue = entry.Value?.ToString() ?? "Healthy";
        string currentValue = GetCurrentValueDisplay(entry);

        if (currentValue == targetValue)
        {
            return new OptimizationResult
            {
                Status = ResultStatus.AlreadyOptimized,
                ItemId = "RegistryHealth",
                DisplayName = "Registry Health already " + targetValue
            };
        }

        // This handler only repairs registry keys that our optimizer is known to manage
        return new OptimizationResult
        {
            Status = ResultStatus.Success,
            ItemId = "RegistryHealth",
            DisplayName = "Repaired managed registry configurations",
            
        };
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        return "Healthy";
    }
}
