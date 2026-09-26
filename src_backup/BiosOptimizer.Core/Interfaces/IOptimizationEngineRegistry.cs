using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Interfaces;

public interface IOptimizationEngineRegistry
{
    void RegisterCapability(OptimizationCapability capability);
    List<OptimizationCapability> GetAllCapabilities();
    List<OptimizationCapability> GetCapabilitiesByCategory(string category);
    OptimizationCapability? GetCapability(string engineId);
}

public interface IOptimizationScoreEngine
{
    OptimizationScoreDto CalculateScore(EnvironmentContext environmentContext, IProfileRepository profileRepository, IActionRegistry actionRegistry);
    OptimizationSummaryDto GenerateSummary(EnvironmentContext environmentContext, IProfileRepository profileRepository, IActionRegistry actionRegistry);
}
