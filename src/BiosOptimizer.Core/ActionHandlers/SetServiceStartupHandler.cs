using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
using BiosOptimizer.Core.Services;
using System;
using System.Threading;

namespace BiosOptimizer.Core.ActionHandlers;

public class SetServiceStartupHandler : IActionHandler
{
    private readonly IServiceManager _serviceManager;
    private readonly ISafetyPolicy _safetyPolicy;
    private readonly IVerificationEngine _verificationEngine;

    public SetServiceStartupHandler(
        IServiceManager serviceManager,
        ISafetyPolicy safetyPolicy,
        IVerificationEngine verificationEngine)
    {
        _serviceManager = serviceManager;
        _safetyPolicy = safetyPolicy;
        _verificationEngine = verificationEngine;
    }

    public string ActionName => "SetServiceStartup";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Target)) return TargetState.NotApplicable;
        if (_safetyPolicy.IsProtectedTarget(entry.Target)) return TargetState.NotApplicable;
        var service = _serviceManager.GetService(entry.Target);
        return service != null ? TargetState.Ready : TargetState.NotAvailable;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var result = new OptimizationResult 
        { 
            ItemId = entry.Id, 
            ActionName = ActionName,
            Category = entry.Category,
            DisplayName = entry.DisplayName 
        };

        var service = _serviceManager.GetService(entry.Target);
        if (service == null)
        {
            result.Status = ResultStatus.Failed;
            result.Message = $"Service '{entry.Target}' not found on system.";
            return result;
        }

        string targetVal = entry.Value?.ToString() ?? "Manual";
        var currentStartup = _serviceManager.GetStartupType(entry.Target);

        // Pre-apply: check if already optimized
        if (StateNormalizer.IsSatisfied(currentStartup.ToString(), targetVal))
        {
            result.Status = ResultStatus.AlreadyOptimized;
            result.Message = $"Service '{entry.Target}' is already in desired state ({currentStartup}).";
            return result;
        }

        if (!Enum.TryParse<ServiceStartupType>(targetVal, true, out var targetStartup))
        {
            // Map common aliases
            string norm = StateNormalizer.NormalizeServiceState(targetVal);
            targetStartup = norm switch
            {
                "AUTOMATIC" => ServiceStartupType.Automatic,
                "MANUAL"    => ServiceStartupType.Manual,
                "DISABLED"  => ServiceStartupType.Disabled,
                _           => ServiceStartupType.Manual
            };
        }

        var originalStartupType = currentStartup;
        var serviceRunningState = _serviceManager.GetRunningState(entry.Target);

        // Capture before state into Universal Backup Manager
        try
        {
            BiosOptimizer.Core.Implementations.BackupManager.Instance.CaptureServiceTweak(
                "Manual",
                entry.Target,
                currentStartup.ToString(),
                targetVal,
                serviceRunningState.ToString(),
                "SAFE"
            );
        }
        catch { }

        // Apply
        try
        {
            bool success = _serviceManager.SetStartupType(entry.Target, targetStartup);
            if (!success)
            {
                result.Status = ResultStatus.Failed;
                result.Message = $"Failed to set startup type for service '{entry.Target}'.";
                return result;
            }
            
            // Enforce running state
            if (targetStartup == ServiceStartupType.Disabled && service.RunningState == ServiceRunningState.Running)
            {
                _serviceManager.Stop(entry.Target);
            }
            else if (targetStartup == ServiceStartupType.Automatic && service.RunningState == ServiceRunningState.Stopped)
            {
                _serviceManager.Start(entry.Target);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            result.Status = ResultStatus.Blocked;
            result.Message = $"Service update blocked: Access denied ({ex.Message})";
            return result;
        }
        catch (System.Security.SecurityException ex)
        {
            result.Status = ResultStatus.Blocked;
            result.Message = $"Service update blocked: Security policy ({ex.Message})";
            return result;
        }
        catch (Exception ex)
        {
            result.Status = ResultStatus.Failed;
            result.Message = $"Service update failed: {ex.Message}";
            return result;
        }

        // Authoritative Verification with bounded retries
        ServiceStartupType newType = ServiceStartupType.Unknown;
        bool isVerified = false;

        for (int i = 0; i < 5; i++)
        {
            if (i > 0) Thread.Sleep(100);
            newType = _serviceManager.GetStartupType(entry.Target);
            if (StateNormalizer.IsSatisfied(newType.ToString(), targetVal))
            {
                isVerified = true;
                break;
            }
        }

        if (!isVerified)
        {
            StateNormalizer.LogReconciliationMismatch(entry.Id, currentStartup.ToString(), targetVal, newType.ToString(), targetVal, "WindowsServiceManager");
            
            // Rollback attempt
            _serviceManager.SetStartupType(entry.Target, originalStartupType);
            result.Status = ResultStatus.Failed;
            result.Message = $"Service verification failed: Expected '{targetVal}', readback was '{newType}'.";
            return result;
        }

        result.Status = ResultStatus.Success;
        result.Message = $"Service '{entry.Target}' startup type successfully set to {newType}.";
        return result;
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        var currentStartup = _serviceManager.GetStartupType(entry.Target);
        if (currentStartup != ServiceStartupType.Unknown)
            return currentStartup.ToString();

        var service = _serviceManager.GetService(entry.Target);
        return service?.StartupType.ToString() ?? "Unknown";
    }
}
