using Microsoft.Win32;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class NetworkThrottlingHandler : IActionHandler
{
    public string ActionName => "SetNetworkThrottling";

    private const string TargetKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string ValueName = "NetworkThrottlingIndex";

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var targetValue = entry.Value?.ToString();
        // Standard "unthrottled" value for gaming/real-time is ffffffff (hex) / 4294967295 (dec), or -1 if unchecked cast.
        // Or 10 (dec) for default.
        uint val;
        
        if (targetValue != null && targetValue.Equals("ffffffff", StringComparison.OrdinalIgnoreCase))
        {
            val = 0xFFFFFFFF;
        }
        else if (!uint.TryParse(targetValue, out val))
        {
            return new OptimizationResult { Status = ResultStatus.Failed, Message = "Invalid value" };
        }

        var currentValueStr = GetCurrentValueDisplay(entry);
        var expectedStr = val == 0xFFFFFFFF ? "ffffffff" : val.ToString();
        if (currentValueStr.Equals(expectedStr, StringComparison.OrdinalIgnoreCase))
        {
            return new OptimizationResult
            {
                Status = ResultStatus.AlreadyOptimized,
                ItemId = ValueName,
                DisplayName = $"Network Throttling Index already {(val == 0xFFFFFFFF ? "Disabled (ffffffff)" : val.ToString())}"
            };
        }

        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(TargetKey);
            if (key != null)
            {
                key.SetValue(ValueName, unchecked((int)val), RegistryValueKind.DWord);
                
                var newValueStr = GetCurrentValueDisplay(entry);
                if (newValueStr.Equals(expectedStr, StringComparison.OrdinalIgnoreCase))
                {
                    return new OptimizationResult
                    {
                        Status = ResultStatus.Success,
                        ItemId = ValueName,
                        DisplayName = $"Set Network Throttling Index to {(val == 0xFFFFFFFF ? "Disabled (ffffffff)" : val.ToString())}",
                        
                    };
                }
            }
        }
        catch (Exception ex)
        {
            return new OptimizationResult { Status = ResultStatus.Failed, Message = ex.Message };
        }

        return new OptimizationResult { Status = ResultStatus.Failed, Message = "Could not open or verify key" };
    }

    public string GetCurrentValueDisplay(OptimizationEntry entry)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(TargetKey);
            if (key != null)
            {
                var val = key.GetValue(ValueName);
                if (val != null)
                {
                    if (val is int intVal && intVal == -1) return "ffffffff";
                    return val.ToString() ?? "10";
                }
            }
        }
        catch { }
        return "Unknown";
    }
}
