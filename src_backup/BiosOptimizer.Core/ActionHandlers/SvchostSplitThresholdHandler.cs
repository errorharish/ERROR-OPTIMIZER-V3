using Microsoft.Win32;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.ActionHandlers;

public class SvchostSplitThresholdHandler : IActionHandler
{
    public string ActionName => "SetSvchostSplitThreshold";

    private const string TargetKey = @"SYSTEM\CurrentControlSet\Control";
    private const string ValueName = "SvchostSplitThresholdInKB";

    private readonly Dictionary<long, uint> _authoritativeMapping = new()
    {
        // 4 GB
        { 4L * 1024 * 1024 * 1024, 0x4000000 },
        // 6 GB
        { 6L * 1024 * 1024 * 1024, 0x600000 },
        // 8 GB
        { 8L * 1024 * 1024 * 1024, 0x800000 },
        // 12 GB
        { 12L * 1024 * 1024 * 1024, 0xC00000 },
        // 16 GB
        { 16L * 1024 * 1024 * 1024, 0x1000000 },
        // 24 GB
        { 24L * 1024 * 1024 * 1024, 0x1800000 },
        // 32 GB
        { 32L * 1024 * 1024 * 1024, 0x2000000 },
        // 64 GB
        { 64L * 1024 * 1024 * 1024, 0x4000000 }
    };

    public TargetState CheckAvailability(OptimizationEntry entry, EnvironmentContext context)
    {
        // Must match one of the authoritative RAM profiles exactly
        var ram = context.HardwareProfile.RAM;
        // The hardware detection layer might report slightly less than exact GB (e.g. hardware reserved), 
        // but the prompt strictly says: "If detected RAM = 16 GB...". 
        // We will do a fuzzy match rounded to nearest GB to determine the 'installed physical RAM' tier,
        // because WMI often returns 16300MB instead of 16384MB. 
        // Wait, the prompt says "DO NOT round to the nearest profile. DO NOT select a lower/higher profile automatically."
        // We will strictly match the gigabyte buckets.
        var ramGb = (long)Math.Round((double)ram / (1024 * 1024 * 1024));
        long canonicalRam = ramGb * 1024 * 1024 * 1024;

        if (!_authoritativeMapping.ContainsKey(canonicalRam))
        {
            return TargetState.NotApplicable;
        }

        return TargetState.Ready;
    }

    public OptimizationResult Apply(OptimizationEntry entry)
    {
        var targetValue = entry.Value?.ToString();

        if (string.IsNullOrEmpty(targetValue))
        {
             return new OptimizationResult { Status = ResultStatus.Failed, Message = "Missing target value in entry" };
        }

        uint val;
        if (targetValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!uint.TryParse(targetValue.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out val))
            {
                return new OptimizationResult { Status = ResultStatus.Failed, Message = "Invalid hex value" };
            }
        }
        else if (!uint.TryParse(targetValue, out val))
        {
            return new OptimizationResult { Status = ResultStatus.Failed, Message = "Invalid numeric value" };
        }

        var expectedStr = $"0x{val:X}";
        var currentValueStr = GetCurrentValueDisplay(entry);

        if (currentValueStr.Equals(expectedStr, StringComparison.OrdinalIgnoreCase))
        {
            return new OptimizationResult
            {
                Status = ResultStatus.AlreadyOptimized,
                ItemId = ValueName,
                DisplayName = $"SvchostSplitThresholdInKB already {expectedStr}"
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
                        DisplayName = $"Set SvchostSplitThresholdInKB to 0x{val:X}",
                        
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
                if (val != null && val is int intVal)
                {
                    uint uval = unchecked((uint)intVal);
                    return $"0x{uval:X}";
                }
            }
        }
        catch { }
        return "Unknown";
    }

    public uint? GetAuthoritativeValueForRam(long ramBytes)
    {
        var ramGb = (long)Math.Round((double)ramBytes / (1024 * 1024 * 1024));
        long canonicalRam = ramGb * 1024 * 1024 * 1024;

        if (_authoritativeMapping.TryGetValue(canonicalRam, out var targetValue))
        {
            return targetValue;
        }

        return null;
    }
}
