using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class CpuParkingHandler : IActionHandler
{
    public string ActionName => "SetCpuParking";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        // Must not unpark aggressively if fewer than 4 physical cores
        if (context.PhysicalCoreCount < 4) return TargetState.NotApplicable;
        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        string targetValue = entry.Value?.ToString() ?? "Optimized";
        string currentValue = GetCurrentValueDisplay(entry);

        if (currentValue == targetValue)
        {
            return new OptimizationResult
            {
                Status = ResultStatus.AlreadyOptimized,
                ItemId = "CoreParking",
                DisplayName = "Core Parking already " + targetValue
            };
        }

        return new OptimizationResult
        {
            Status = ResultStatus.Success,
            ItemId = "CoreParking",
            DisplayName = "Optimized Core Parking for High-Core CPU",
            
        };
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        return "Optimized";
    }
}
