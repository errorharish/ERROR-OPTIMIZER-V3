using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations;

public class ConditionEvaluator
{
    public bool Evaluate(List<ConditionDef> conditions, EnvironmentContext context)
    {
        if (conditions == null || conditions.Count == 0) return true;

        foreach (var condition in conditions)
        {
            if (!EvaluateCondition(condition, context))
                return false;
        }

        return true;
    }

    private bool EvaluateCondition(ConditionDef condition, EnvironmentContext context)
    {
        var valueStr = condition.Value?.ToString() ?? "";
        
        return condition.Type switch
        {
            "Windows10" => context.IsWindows10 == bool.Parse(valueStr),
            "Windows11" => context.IsWindows11 == bool.Parse(valueStr),
            "HasBattery" => context.HasBattery == bool.Parse(valueStr),
            "IsLaptop" => context.IsLaptop == bool.Parse(valueStr),
            "IsDesktop" => context.IsDesktop == bool.Parse(valueStr),
            "IsDomainJoined" => context.IsDomainJoined == bool.Parse(valueStr),
            "HasPrinter" => EvaluateDeviceState(context.HasPrinter, valueStr),
            "HasBluetooth" => EvaluateDeviceState(context.HasBluetooth, valueStr),
            "HasTouchscreen" => EvaluateDeviceState(context.HasTouchscreen, valueStr),
            "HasCamera" => EvaluateDeviceState(context.HasCamera, valueStr),
            "BitLockerActive" => context.BitLockerActive == bool.Parse(valueStr),
            "MinimumRamGb" => context.RamSizeGb >= long.Parse(valueStr),
            "MaximumRamGb" => context.RamSizeGb <= long.Parse(valueStr),
            "MinimumPhysicalCores" => context.PhysicalCoreCount >= int.Parse(valueStr),
            "StorageType" => context.StorageType.Equals(valueStr, StringComparison.OrdinalIgnoreCase),
            "StorageIsSsd" => bool.Parse(valueStr) == (context.StorageType.Equals("SSD", StringComparison.OrdinalIgnoreCase) || context.StorageType.Equals("NVMe", StringComparison.OrdinalIgnoreCase)),
            "StorageIsNvme" => bool.Parse(valueStr) == context.StorageType.Equals("NVMe", StringComparison.OrdinalIgnoreCase),
            "StorageIsHdd" => bool.Parse(valueStr) == context.StorageType.Equals("HDD", StringComparison.OrdinalIgnoreCase),
            "VirtualizationSupported" => context.VirtualizationSupported == bool.Parse(valueStr),
            "VirtualizationEnabled" => context.VirtualizationEnabled == bool.Parse(valueStr),
            "HasNvidiaGpu" => bool.Parse(valueStr) == context.Gpus.Any(g =>
                g.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                g.Vendor.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)),
            "HasAmdGpu" => bool.Parse(valueStr) == context.Gpus.Any(g =>
                g.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                g.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                g.Vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase)),
            "HasIntelGpu" => bool.Parse(valueStr) == context.Gpus.Any(g =>
                g.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                g.Vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase)),
            "GpuVendorContains" => context.Gpus.Any(g =>
                g.Name.Contains(valueStr, StringComparison.OrdinalIgnoreCase) ||
                g.Vendor.Contains(valueStr, StringComparison.OrdinalIgnoreCase)),
            "CpuVendorContains" => context.CpuName.Contains(valueStr, StringComparison.OrdinalIgnoreCase),
            "WorkloadDetected" => context.Workloads.Any(w => w.Detected && w.WorkloadType.Equals(valueStr, StringComparison.OrdinalIgnoreCase)),
            "WorkloadInstalled" => context.Workloads.Any(w => w.Detected && w.WorkloadType.Equals(valueStr, StringComparison.OrdinalIgnoreCase)),
            "WorkloadNotInstalled" => !context.Workloads.Any(w => w.Detected && w.WorkloadType.Equals(valueStr, StringComparison.OrdinalIgnoreCase)),
            "IsOnAc" => bool.Parse(valueStr) != context.MachineProfile.IsOnBattery,
            "IsOnBattery" => bool.Parse(valueStr) == context.MachineProfile.IsOnBattery,
            "ManufacturerContains" => context.Manufacturer.Contains(valueStr, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private bool EvaluateDeviceState(DeviceState state, string expectedBoolStr)
    {
        if (!bool.TryParse(expectedBoolStr, out bool expected)) return false;
        
        if (expected) return state == DeviceState.Available;
        return state == DeviceState.Unavailable || state == DeviceState.Unknown;
    }
}
