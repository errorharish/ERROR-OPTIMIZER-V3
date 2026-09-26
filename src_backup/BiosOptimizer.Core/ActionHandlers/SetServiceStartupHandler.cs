using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

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
        if (_safetyPolicy.IsProtectedTarget(entry.Target)) return TargetState.NotApplicable;
        var service = _serviceManager.GetService(entry.Target);
        return service != null ? TargetState.Ready : TargetState.NotAvailable;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var result = new OptimizationResult { ItemId = entry.Id, DisplayName = entry.DisplayName };

        var service = _serviceManager.GetService(entry.Target);
        if (service == null)
        {
            result.Status = ResultStatus.Failed;
            result.Message = "Service not found.";
            return result;
        }

        if (!Enum.TryParse<ServiceStartupType>(entry.Value?.ToString(), true, out var targetStartup))
        {
            result.Status = ResultStatus.Failed;
            result.Message = $"Invalid target startup type: {entry.Value}";
            return result;
        }

        bool isCurrentlyDesired = service.StartupType == targetStartup;
        if (targetStartup == ServiceStartupType.Disabled && service.RunningState == ServiceRunningState.Running)
        {
            isCurrentlyDesired = false;
        }
        else if (targetStartup == ServiceStartupType.Automatic && service.RunningState == ServiceRunningState.Stopped)
        {
            isCurrentlyDesired = false;
        }

        if (isCurrentlyDesired)
        {
            result.Status = ResultStatus.AlreadyOptimized;
            result.Message = "Service is already in the desired state.";
            return result;
        }

        var originalStartupType = service.StartupType;

        // Apply
        try
        {
            bool success = _serviceManager.SetStartupType(entry.Target, targetStartup);
            if (!success)
            {
                result.Status = ResultStatus.Failed;
                result.Message = "Service manager returned false (could not set registry value).";
                return result;
            }
            
            // Also enforce running state
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

        // Verify
        var newType = _serviceManager.GetStartupType(entry.Target);
        if (newType != targetStartup)
        {
            // Auto rollback manually
            bool rbSuccess = _serviceManager.SetStartupType(entry.Target, originalStartupType);
            if (!rbSuccess)
            {
                result.Status = ResultStatus.Failed;
                result.Message = "Critical Restore Failure. Verification failed, and Rollback also failed!";
                return result;
            }
            
            var verifyRb = _serviceManager.GetStartupType(entry.Target);
            if (verifyRb != originalStartupType)
            {
                result.Status = ResultStatus.Failed;
                result.Message = "Critical Restore Failure. Verification failed, and Rollback failed to restore original state!";
                return result;
            }

            result.Status = ResultStatus.Reverted;
            result.Message = "Verification failed. Changes successfully reverted.";
            return result;
        }

        result.Status = ResultStatus.Success;
        result.Message = "Successfully updated service startup type and running state.";
        return result;
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        var service = _serviceManager.GetService(entry.Target);
        if (service == null) return "Unknown";
        
        if (service.StartupType == ServiceStartupType.Disabled) return "Disabled";
        return $"{service.StartupType} ({service.RunningState})";
    }
}
