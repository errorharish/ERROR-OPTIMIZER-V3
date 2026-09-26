using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class SetBiosSettingHandler : IActionHandler
{
    private readonly IProviderManager _providerManager;
    private readonly IVerificationEngine _verificationEngine;

    public string ActionName => "SetBiosSetting";

    public SetBiosSettingHandler(
        IProviderManager providerManager,
        IVerificationEngine verificationEngine)
    {
        _providerManager = providerManager;
        _verificationEngine = verificationEngine;
    }

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        var provider = _providerManager.GetActiveProvider();
        var cap = provider.GetCapability(entry.Target);
        if (cap == null || !cap.Supported) return TargetState.NotAvailable;
        if (!cap.Writable) return TargetState.NotAvailable;
        return TargetState.Ready;
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        var provider = _providerManager.GetActiveProvider();
        var cap = provider.GetCapability(entry.Target);
        return cap != null ? cap.CurrentValue : "Unknown/Unsupported";
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var result = new OptimizationResult { ItemId = entry.Id, DisplayName = entry.DisplayName, Status = ResultStatus.Failed };

        var provider = _providerManager.GetActiveProvider();
        var cap = provider.GetCapability(entry.Target);
        
        if (cap == null || !cap.Supported || !cap.Writable)
        {
            result.Status = ResultStatus.Skipped;
            result.Message = "BIOS capability not found or read-only.";
            return result;
        }

        var requestedValue = entry.Value?.ToString() ?? "";
        if (cap.CurrentValue.Equals(requestedValue, StringComparison.OrdinalIgnoreCase))
        {
            result.Status = ResultStatus.AlreadyOptimized;
            result.Message = "BIOS setting is already at requested value.";
            return result;
        }

        var opResult = provider.WriteSetting(cap.SettingId, requestedValue);

        if (opResult.Success)
        {
            if (opResult.RequiresReboot)
            {
                // We can't verify until reboot
                result.Status = ResultStatus.Success;
                result.Message = "BIOS setting applied. Requires reboot to verify.";
            }
            else
            {
                bool verified = provider.VerifySetting(cap.SettingId, requestedValue);
                if (verified)
                {
                    result.Status = ResultStatus.Success;
                    result.Message = "BIOS setting applied and verified.";
                }
                else
                {
                    // Auto-revert
                    provider.WriteSetting(cap.SettingId, cap.CurrentValue);
                    result.Status = ResultStatus.Reverted;
                    result.Message = "Verification failed after write. Automatically reverted to original value.";
                }
            }
        }
        else
        {
            result.Message = $"Failed to write setting: {opResult.Message}";
        }

        return result;
    }
}
