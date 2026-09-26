using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BiosOptimizer.Core.ActionHandlers;

public class RemoveAppxPackageHandler : IActionHandler
{
    private readonly IPackageManager _packageManager;
    private readonly ISafetyPolicy _safetyPolicy;
    private readonly IVerificationEngine _verificationEngine;

    public string ActionName => "RemoveAppxPackage";

    public RemoveAppxPackageHandler(
        IPackageManager packageManager,
        ISafetyPolicy safetyPolicy,
        IVerificationEngine verificationEngine)
    {
        _packageManager = packageManager;
        _safetyPolicy = safetyPolicy;
        _verificationEngine = verificationEngine;
    }

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        var package = _packageManager.GetPackage(entry.Target);
        if (package == null) return TargetState.NotAvailable;
        
        return (!package.IsFramework && !package.IsSystemComponent) ? TargetState.Ready : TargetState.Protected;
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        var package = _packageManager.GetPackage(entry.Target);
        return package != null ? $"Installed ({package.Version})" : "Not Installed";
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var result = new OptimizationResult { ItemId = entry.Id, DisplayName = entry.DisplayName, Status = ResultStatus.Failed };

        if (_safetyPolicy.IsProtectedTarget(entry.Target))
        {
            result.Status = ResultStatus.Blocked;
            result.Message = "Target is protected by Debloat safety policy.";
            return result;
        }

        var package = _packageManager.GetPackage(entry.Target);
        if (package == null)
        {
            result.Status = ResultStatus.AlreadyOptimized;
            result.Message = "Package already removed.";
            return result;
        }

        // Capture before state into Universal Backup Manager
        try
        {
            BiosOptimizer.Core.Implementations.BackupManager.Instance.CaptureGenericTweak(
                entry.SourceProfile is { Length: > 0 } sp ? sp : "Manual",
                $"appx.{entry.Target}".ToLowerInvariant(),
                $"AppX Package Removal: {package.Name}",
                "Storage",
                "AppxPackage",
                $"Installed (v{package.Version})",
                "Removed",
                "NonReversible",
                false,
                "SAFE"
            );
        }
        catch { }

        bool removed = _packageManager.RemovePackage(package.FullName, false);
        
        if (removed)
        {
            var verifyPackage = _packageManager.GetPackage(entry.Target);
            if (verifyPackage == null)
            {
                result.Status = ResultStatus.Success;
                result.Message = "Package removed successfully.";
            }
            else
            {
                result.Status = ResultStatus.Failed;
                result.Message = "Verification failed: Package still present after removal.";
            }
        }
        else
        {
            result.Message = "Failed to remove package.";
        }

        return result;
    }}
