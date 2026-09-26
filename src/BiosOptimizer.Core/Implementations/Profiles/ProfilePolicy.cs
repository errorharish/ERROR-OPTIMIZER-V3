#nullable enable
using System;
using System.Collections.Generic;

namespace BiosOptimizer.Core.Implementations.Profiles
{
    public enum ApplicabilityState
    {
        Supported,
        Applicable,
        AlreadyOptimal,
        NotApplicable,
        Unsupported,
        RequiresAdmin,
        Conflict,
        FailedToAnalyze
    }

    public enum LearningStatus
    {
        Unknown,
        Supported,
        Tried,
        Verified,
        ProvenEffective,
        Ineffective
    }

    public class ProfilePolicy
    {
        public string ProfileId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Aggressiveness { get; set; } = 1; // 1 to 10
        public HashSet<string> AllowedCategories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> MaxAllowedRisks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool AllowServiceChanges { get; set; }
        public bool AllowPowerPlanSwitching { get; set; }
        public bool AllowBackgroundProcessSuspension { get; set; }
        public bool AllowBiosPlatformTweaks { get; set; }
        public bool AllowDebloatAppRemoval { get; set; }
        public bool WorkloadAdaptationEnabled { get; set; }
        public bool MachineLearningEnabled { get; set; }

        public static readonly ProfilePolicy Normal = new()
        {
            ProfileId = "tier1_safe",
            DisplayName = "Normal",
            Description = "Safe, low-risk everyday optimization. Preserves all system services and defaults.",
            Aggressiveness = 1,
            AllowedCategories = new(StringComparer.OrdinalIgnoreCase) { "Visual", "Storage", "Input", "RAM" },
            MaxAllowedRisks = new(StringComparer.OrdinalIgnoreCase) { "Low", "Safe", "Core" },
            AllowServiceChanges = false,
            AllowPowerPlanSwitching = false,
            AllowBackgroundProcessSuspension = false,
            AllowBiosPlatformTweaks = false,
            AllowDebloatAppRemoval = false,
            WorkloadAdaptationEnabled = false,
            MachineLearningEnabled = false
        };

        public static readonly ProfilePolicy Pro = new()
        {
            ProfileId = "tier2_standard",
            DisplayName = "Pro",
            Description = "Moderate performance optimization with controlled network, GPU, and input tuning.",
            Aggressiveness = 5,
            AllowedCategories = new(StringComparer.OrdinalIgnoreCase) { "Visual", "Storage", "Input", "RAM", "Network", "GPU", "CPU", "Service" },
            MaxAllowedRisks = new(StringComparer.OrdinalIgnoreCase) { "Low", "Medium", "Safe", "Standard", "Core" },
            AllowServiceChanges = true,
            AllowPowerPlanSwitching = true,
            AllowBackgroundProcessSuspension = false,
            AllowBiosPlatformTweaks = false,
            AllowDebloatAppRemoval = false,
            WorkloadAdaptationEnabled = true,
            MachineLearningEnabled = true
        };

        public static readonly ProfilePolicy Ultimate = new()
        {
            ProfileId = "tier3_aggressive",
            DisplayName = "Ultimate",
            Description = "Deep supported performance optimization across all system subsystems with strict safety gates.",
            Aggressiveness = 8,
            AllowedCategories = new(StringComparer.OrdinalIgnoreCase) { "Visual", "Storage", "Input", "RAM", "Network", "GPU", "CPU", "Service", "Power", "Debloat" },
            MaxAllowedRisks = new(StringComparer.OrdinalIgnoreCase) { "Low", "Medium", "High", "Safe", "Standard", "Advanced", "Core" },
            AllowServiceChanges = true,
            AllowPowerPlanSwitching = true,
            AllowBackgroundProcessSuspension = true,
            AllowBiosPlatformTweaks = false,
            AllowDebloatAppRemoval = true,
            WorkloadAdaptationEnabled = true,
            MachineLearningEnabled = true
        };

        public static readonly ProfilePolicy Debloat = new()
        {
            ProfileId = "tier4_experimental",
            DisplayName = "Debloat",
            Description = "Focused Windows background process, telemetry, and non-essential app debloating.",
            Aggressiveness = 6,
            AllowedCategories = new(StringComparer.OrdinalIgnoreCase) { "Debloat", "Service", "Storage", "Visual" },
            MaxAllowedRisks = new(StringComparer.OrdinalIgnoreCase) { "Low", "Medium", "Safe", "Standard", "Core" },
            AllowServiceChanges = true,
            AllowPowerPlanSwitching = false,
            AllowBackgroundProcessSuspension = true,
            AllowBiosPlatformTweaks = false,
            AllowDebloatAppRemoval = true,
            WorkloadAdaptationEnabled = false,
            MachineLearningEnabled = true
        };

        public static readonly ProfilePolicy BiosSafe = new()
        {
            ProfileId = "tier5_extreme",
            DisplayName = "BIOS Safe",
            Description = "Hardware and platform configuration with strict safety verification.",
            Aggressiveness = 3,
            AllowedCategories = new(StringComparer.OrdinalIgnoreCase) { "BiosSafe", "CPU", "GPU", "Power" },
            MaxAllowedRisks = new(StringComparer.OrdinalIgnoreCase) { "Low", "Medium", "Safe", "Standard", "Core" },
            AllowServiceChanges = false,
            AllowPowerPlanSwitching = true,
            AllowBackgroundProcessSuspension = false,
            AllowBiosPlatformTweaks = true,
            AllowDebloatAppRemoval = false,
            WorkloadAdaptationEnabled = false,
            MachineLearningEnabled = true
        };

        public static readonly ProfilePolicy MaxPerformance = new()
        {
            ProfileId = "tier6_maximum",
            DisplayName = "Max Performance",
            Description = "Ultimate full-system performance combining hardware detection, workload adaptation, and machine learning.",
            Aggressiveness = 10,
            AllowedCategories = new(StringComparer.OrdinalIgnoreCase) { "Visual", "Storage", "Input", "RAM", "Network", "GPU", "CPU", "Service", "Power", "Debloat", "BiosSafe", "System" },
            MaxAllowedRisks = new(StringComparer.OrdinalIgnoreCase) { "Low", "Medium", "High", "Critical", "Safe", "Standard", "Advanced", "Core", "Experimental" },
            AllowServiceChanges = true,
            AllowPowerPlanSwitching = true,
            AllowBackgroundProcessSuspension = true,
            AllowBiosPlatformTweaks = true,
            AllowDebloatAppRemoval = true,
            WorkloadAdaptationEnabled = true,
            MachineLearningEnabled = true
        };

        public static ProfilePolicy GetPolicyById(string id)
        {
            return id.ToLowerInvariant() switch
            {
                "tier1_safe" or "normal" or "tier1" => Normal,
                "tier2_standard" or "pro" or "tier2" => Pro,
                "tier3_aggressive" or "ultimate" or "tier3" => Ultimate,
                "tier4_experimental" or "debloat" or "tier4" => Debloat,
                "tier5_extreme" or "biossafe" or "bios_safe" or "tier5" => BiosSafe,
                "tier6_maximum" or "maxperformance" or "max_performance" or "tier6" => MaxPerformance,
                _ => Pro
            };
        }
    }
}
