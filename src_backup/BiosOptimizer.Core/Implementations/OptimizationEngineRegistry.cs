using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations;

public class OptimizationEngineRegistry : IOptimizationEngineRegistry
{
    private readonly Dictionary<string, OptimizationCapability> _capabilities = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterCapability(OptimizationCapability capability)
    {
        _capabilities[capability.EngineId] = capability;
    }

    public List<OptimizationCapability> GetAllCapabilities()
    {
        return _capabilities.Values.ToList();
    }

    public List<OptimizationCapability> GetCapabilitiesByCategory(string category)
    {
        return _capabilities.Values.Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public OptimizationCapability? GetCapability(string engineId)
    {
        _capabilities.TryGetValue(engineId, out var capability);
        return capability;
    }
}
