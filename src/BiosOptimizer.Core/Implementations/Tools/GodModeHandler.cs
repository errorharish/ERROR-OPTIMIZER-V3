using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations.Tools;

public class GodModeHandler : IActionHandler
{
    public string ActionName => "GodModeUtility";
    private readonly string _desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    private readonly string _godModeFolderName = "GodMode.{ED7BA470-8E54-465E-825C-99712043E01C}";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        return TargetState.Ready; // Simple shortcut creation
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        try
        {
            var fullPath = Path.Combine(_desktopPath, _godModeFolderName);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                return new OptimizationResult
                {
                    Status = ResultStatus.Success,
                    ItemId = "GodMode",
                    DisplayName = "Created God Mode Shortcut on Desktop"
                };
            }
            return new OptimizationResult
            {
                Status = ResultStatus.Success,
                ItemId = "GodMode",
                DisplayName = "God Mode already exists"
            };
        }
        catch (Exception ex)
        {
            return new OptimizationResult { Status = ResultStatus.Failed, Message = ex.Message };
        }
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        var fullPath = Path.Combine(_desktopPath, _godModeFolderName);
        return Directory.Exists(fullPath) ? "Exists" : "Not Present";
    }
}

