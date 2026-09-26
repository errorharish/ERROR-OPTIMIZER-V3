using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Interfaces;

public interface IProfileRepository
{
    ProfileDef? LoadProfile(string tierId);
    ProfileDef? LoadResolvedProfile(string tierId);
    bool ValidateProfile(ProfileDef profile);
}

public enum TargetState
{
    Ready,
    NotApplicable,
    NotAvailable,
    Protected
}

public interface IActionHandler
{
    string ActionName { get; }
    
    TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context);
    
    OptimizationResult Apply(OptimizationEntry entry);
    
    string GetCurrentValueDisplay(OptimizationEntry entry);
}

public interface IActionRegistry
{
    IActionHandler? GetHandler(string actionName);
    void RegisterHandler(IActionHandler handler);
}
