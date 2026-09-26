using System;
using System.Collections.Generic;

namespace BiosOptimizer.Core.Implementations.Power
{
    public enum PowerPlanStatus
    {
        Active,
        Available,
        AvailableToEnable,
        Creatable,
        NotInstalled,
        Creating,
        Applying,
        Verifying,
        Verified,
        Failed,
        RollingBack,
        Restored,
        Unavailable,
        Unsupported
    }

    public enum PowerPlanChangeSource
    {
        USER_REQUEST,
        AI_POLICY,
        BATTERY_POLICY,
        WORKLOAD_POLICY,
        RESTORE,
        STARTUP,
        SYSTEM_RECONCILIATION,
        EXTERNAL_OR_WINDOWS,
        UNKNOWN
    }

    public class PowerPlanChangeLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string PreviousGuid { get; set; } = string.Empty;
        public string PreviousName { get; set; } = string.Empty;
        public string NewGuid { get; set; } = string.Empty;
        public string NewName { get; set; } = string.Empty;
        public PowerPlanChangeSource Source { get; set; } = PowerPlanChangeSource.UNKNOWN;
        public bool UserInitiated { get; set; }
        public string ThreadOrTask { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public override string ToString() => $"[{Timestamp:HH:mm:ss}] {PreviousName} -> {NewName} | Source: {Source} | Result: {Result}";
    }

    public class UserPowerPlanLock
    {
        public string UserSelectedPlanGuid { get; set; } = string.Empty;
        public string UserSelectedPlanName { get; set; } = string.Empty;
        public DateTime? UserSelectedTimestamp { get; set; }
        public PowerPlanChangeSource UserSelectionSource { get; set; } = PowerPlanChangeSource.USER_REQUEST;
        public bool IsLocked => !string.IsNullOrEmpty(UserSelectedPlanGuid);
    }

    public class StartupPowerPlanConfig
    {
        public string StartupPowerPlanGuid { get; set; } = string.Empty;
        public string StartupPowerPlanName { get; set; } = string.Empty;
        public bool AutoEnableOnStartup { get; set; }
        public bool ContinuousEnforcement { get; set; }
        public DateTime? LastStartupExecutionTime { get; set; }
        public string LastStartupExecutionResult { get; set; } = "READY";
        public string LastStartupExecutionMessage { get; set; } = string.Empty;
    }

    public class PowerPlanSettingItem
    {
        public Guid SettingGuid { get; set; }
        public Guid SubgroupGuid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public uint CurrentAC { get; set; }
        public uint TargetAC { get; set; }
        public uint CurrentDC { get; set; }
        public uint TargetDC { get; set; }
        public uint MinValue { get; set; } = 0;
        public uint MaxValue { get; set; } = 100;
        public string Unit { get; set; } = "%";
        public bool Supported { get; set; } = true;
        public bool Applied { get; set; }
        public bool RequiresReboot { get; set; }
        public bool Verified { get; set; }
    }

    public class PowerPlanItem
    {
        public string Guid { get; set; } = string.Empty;
        public Guid SchemeGuid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsInstalled { get; set; }
        public bool IsSupported { get; set; } = true;
        public bool IsCustomAiPlan { get; set; }
        public bool IsErrorOptimizerOwned { get; set; }
        public bool IsBuiltin { get; set; }
        public bool IsStartupPlan { get; set; }
        public string Owner { get; set; } = "Windows"; // "Windows", "ErrorOptimizer", "OEM"
        public string PlanType { get; set; } = "CUSTOM"; // "AI_BATTERY_EFFICIENCY", "AI_MAX_PERFORMANCE", "CUSTOM", "WINDOWS_BUILTIN", "OEM_VENDOR"
        public string BasePlanName { get; set; } = string.Empty;
        public PowerPlanStatus Status { get; set; } = PowerPlanStatus.Available;
        
        public List<PowerPlanSettingItem> Settings { get; set; } = new();

        public bool IsAiBatteryEfficiency => PlanType == "AI_BATTERY_EFFICIENCY" || Name.Contains("Battery Efficiency", StringComparison.OrdinalIgnoreCase);
        public bool IsAiMaxPerformance => PlanType == "AI_MAX_PERFORMANCE" || Name.Contains("Max Performance", StringComparison.OrdinalIgnoreCase);

        public string PlanTypeDisplay => PlanType switch
        {
            "AI_BATTERY_EFFICIENCY" => "ERROR OPTIMIZER AI",
            "AI_MAX_PERFORMANCE" => "ERROR OPTIMIZER AI",
            _ => IsErrorOptimizerOwned 
                ? "CUSTOM PLAN" 
                : (IsBuiltin ? "WINDOWS / OEM PLAN" : "OEM / VENDOR PLAN")
        };

        public string PerformanceProfileText { get; set; } = "HIGH PERFORMANCE PROFILE";
        public string AcPerformanceProfile { get; set; } = "MAX PERFORMANCE";
        public string DcPerformanceProfile { get; set; } = "PERFORMANCE / BATTERY LIMITED";

        public string StatusText => Status switch
        {
            PowerPlanStatus.Active => "ACTIVE",
            PowerPlanStatus.Available => "AVAILABLE",
            PowerPlanStatus.AvailableToEnable => "AVAILABLE TO ENABLE",
            PowerPlanStatus.Creatable => "CREATABLE",
            PowerPlanStatus.Creating => "CREATING...",
            PowerPlanStatus.Applying => "APPLYING...",
            PowerPlanStatus.Verifying => "VERIFYING...",
            PowerPlanStatus.Verified => "VERIFIED",
            PowerPlanStatus.Failed => "ACTIVATION FAILED",
            PowerPlanStatus.RollingBack => "ROLLING BACK...",
            PowerPlanStatus.Restored => "RESTORED",
            PowerPlanStatus.Unsupported => "NOT SUPPORTED ON THIS SYSTEM",
            _ => "NOT APPLICABLE"
        };

        public string StatusBrush => Status switch
        {
            PowerPlanStatus.Active => "#10B981",       // Vibrant Green
            PowerPlanStatus.Available => (IsAiMaxPerformance ? "#EC4899" : (IsAiBatteryEfficiency ? "#10B981" : (IsErrorOptimizerOwned ? "#8B5CF6" : "#3B82F6"))),
            PowerPlanStatus.AvailableToEnable => "#F59E0B", // Amber Warning
            PowerPlanStatus.Creatable => "#8B5CF6",    // Purple
            PowerPlanStatus.Failed => "#EF4444",       // Red
            _ => "#6B7280"                             // Muted Slate
        };

        public bool CanApply => IsInstalled && IsSupported && !IsActive;
        public bool CanEnable => !IsInstalled && IsSupported && Status == PowerPlanStatus.AvailableToEnable;
        public bool CanDelete => IsErrorOptimizerOwned && !IsActive && IsInstalled;
        public bool CanEdit => IsInstalled && IsSupported;
        
        public string BatteryImpact { get; set; } = "HIGH PERFORMANCE"; 
        public string RiskLevel { get; set; } = "SAFE";         // "SAFE", "LOW RISK", "CAUTION"
    }

    public class PowerPlanOwnershipRecord
    {
        public string Guid { get; set; } = string.Empty;
        public string BasePlanGuid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PlanType { get; set; } = "CUSTOM"; // "AI_BATTERY_EFFICIENCY", "AI_MAX_PERFORMANCE", "CUSTOM"
        public string Owner { get; set; } = "ErrorOptimizer";
        public DateTime CreationTime { get; set; } = DateTime.UtcNow;
        public string Version { get; set; } = "3.0";
    }

    public class HardwareAnalysisReport
    {
        public string CpuName { get; set; } = "Unknown CPU";
        public string CpuVendor { get; set; } = "Unknown";
        public int PhysicalCores { get; set; } = 4;
        public int LogicalCores { get; set; } = 8;
        public double BaseClockGhz { get; set; } = 2.5;
        public bool HasHybridArchitecture { get; set; }
        public bool IsLaptop { get; set; }
        public double RamTotalGb { get; set; } = 16.0;
        public string RamType { get; set; } = "DDR4";
        public int RamSpeedMhz { get; set; } = 3200;
        public string GpuName { get; set; } = "Unknown GPU";
        public double GpuVramGb { get; set; } = 0.0;
        public bool IsDiscreteGpu { get; set; }
        public string StorageType { get; set; } = "NVMe SSD";
        public double StorageFreeGb { get; set; } = 100.0;
        public bool IsBatteryPowered { get; set; }
        public int BatteryPercent { get; set; } = 100;
        public string ThermalStatus { get; set; } = "UNKNOWN (Not exposed by OEM ACPI)";
        public string TierClassification { get; set; } = "HIGH PERFORMANCE";
        public string DetectedWorkload { get; set; } = "Standard Workload";

        public string SummaryText => $"{CpuName} | {RamTotalGb:F0} GB {RamType} | {GpuName} | {(IsLaptop ? (IsBatteryPowered ? $"Battery ({BatteryPercent}%)" : "Laptop AC") : "Desktop AC")}";
    }

    public class PowerPlanRecommendation
    {
        public string RecommendedPlanName { get; set; } = "Balanced";
        public string RecommendedPlanGuid { get; set; } = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public string Reason { get; set; } = "Standard balanced energy efficiency.";
        public string ActiveWorkloadSummary { get; set; } = "Normal desktop workload";
        public string BatteryWarning { get; set; } = string.Empty;
        public bool HasBatteryWarning => !string.IsNullOrEmpty(BatteryWarning);
        public bool IsCurrentActiveOptimal { get; set; }
        public HardwareAnalysisReport? HardwareProfile { get; set; }
    }

    public class PowerPlanOperationResult
    {
        public bool Success { get; set; }
        public bool AlreadyActive { get; set; }
        public string Message { get; set; } = string.Empty;
        public string TargetSchemeGuid { get; set; } = string.Empty;
        public string ActiveSchemeGuid { get; set; } = string.Empty;
        public string ActiveSchemeName { get; set; } = string.Empty;
        public bool Verified { get; set; }
        public PowerPlanChangeSource Source { get; set; } = PowerPlanChangeSource.USER_REQUEST;
        public List<PowerPlanSettingItem> VerifiedSettings { get; set; } = new();
    }
}
