#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Core.Implementations.RegistryValues
{
    public enum GpuVendorScope
    {
        Universal,
        Nvidia,
        Amd,
        Diagnostic
    }

    public class GpuGroupSummary
    {
        public GpuVendorScope Scope { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string StatusBadge { get; set; } = string.Empty;
        public bool IsDetected { get; set; }
        public int TotalCandidates { get; set; }
        public int ApplicableCount { get; set; }
        public int OptimizedCount { get; set; }
        public int PendingCount { get; set; }
        public int NotApplicableCount { get; set; }

        public bool CanOptimize => IsDetected && ApplicableCount > 0 && PendingCount > 0;

        public string ButtonText => !IsDetected
            ? "NOT AVAILABLE"
            : (PendingCount == 0 && OptimizedCount > 0 ? "ALREADY OPTIMIZED" : $"OPTIMIZE {GroupName}");

        public string BadgeBrushKey => !IsDetected
            ? "TextMutedBrush"
            : (PendingCount == 0 && OptimizedCount > 0 ? "SuccessBrush" : "AccentBrush");

        public double CardOpacity => IsDetected ? 1.0 : 0.45;
    }

    public class GpuOptimizationPlan
    {
        public HardwareSnapshot Hardware { get; set; } = new();
        public GpuGroupSummary UniversalGroup { get; set; } = new();
        public GpuGroupSummary NvidiaGroup { get; set; } = new();
        public GpuGroupSummary AmdGroup { get; set; } = new();

        public List<GpuRegistryItem> AllCandidates { get; set; } = new();
        public List<GpuRegistryItem> UniversalCandidates { get; set; } = new();
        public List<GpuRegistryItem> NvidiaCandidates { get; set; } = new();
        public List<GpuRegistryItem> AmdCandidates { get; set; } = new();
    }

    public class GpuOptimizationApplicabilityEngine
    {
        private static readonly Lazy<GpuOptimizationApplicabilityEngine> _instance = new(() => new GpuOptimizationApplicabilityEngine());
        public static GpuOptimizationApplicabilityEngine Instance => _instance.Value;

        private readonly GpuRegistryValueEngine _registryEngine = GpuRegistryValueEngine.Instance;

        public GpuOptimizationPlan BuildPlan(HardwareSnapshot hw, string profileMode = "NORMAL", bool forceRescan = false)
        {
            var rawItems = _registryEngine.ScanAllGpuOptimizations(profileMode, forceRescan);
            var plan = new GpuOptimizationPlan
            {
                Hardware = hw
            };

            // Filter into scopes
            foreach (var item in rawItems)
            {
                plan.AllCandidates.Add(item);

                if (item.Category == GpuRegistryCategory.Universal)
                {
                    plan.UniversalCandidates.Add(item);
                }
                else if (item.Category == GpuRegistryCategory.NvidiaSpecific)
                {
                    // Override applicability based strictly on HardwareSnapshot
                    if (!hw.HasNvidia)
                    {
                        item.Status = RegistryValueStatus.NotApplicable;
                        item.WhyApplicable = "NVIDIA GPU not detected on this system.";
                    }
                    plan.NvidiaCandidates.Add(item);
                }
                else if (item.Category == GpuRegistryCategory.AmdSpecific)
                {
                    // Override applicability based strictly on HardwareSnapshot
                    if (!hw.HasAmd)
                    {
                        item.Status = RegistryValueStatus.NotApplicable;
                        item.WhyApplicable = "AMD Radeon GPU not detected on this system.";
                    }
                    plan.AmdCandidates.Add(item);
                }
            }

            // Universal Group Summary
            plan.UniversalGroup = CreateGroupSummary(
                GpuVendorScope.Universal,
                "UNIVERSAL GPU",
                "Universal Windows Graphics & Multimedia Scheduling Architecture",
                "Configures low-latency multimedia responsiveness, MMCSS game thread priority, and hardware flip model presentation.",
                isDetected: true,
                statusBadge: "UNIVERSAL ACTIVE",
                candidates: plan.UniversalCandidates
            );

            // NVIDIA Group Summary
            bool nvidiaDetected = hw.HasNvidia;
            plan.NvidiaGroup = CreateGroupSummary(
                GpuVendorScope.Nvidia,
                "NVIDIA GPU",
                "NVIDIA GeForce / RTX Driver & Power Architecture",
                nvidiaDetected 
                    ? $"Configures PowerMizer performance states, driver preemption, and compute memory topology for {hw.PrimaryGpu.Name}." 
                    : "NVIDIA GPU not detected on this system. NVIDIA-specific optimizations are unavailable.",
                isDetected: nvidiaDetected,
                statusBadge: nvidiaDetected ? "NVIDIA DETECTED" : "NOT DETECTED",
                candidates: plan.NvidiaCandidates
            );

            // AMD Group Summary
            bool amdDetected = hw.HasAmd;
            plan.AmdGroup = CreateGroupSummary(
                GpuVendorScope.Amd,
                "AMD GPU",
                "AMD Radeon Adrenalin Driver & Power Architecture",
                amdDetected 
                    ? "Configures Radeon driver background telemetry, ULPS deep sleep, and surface format optimizations." 
                    : "AMD GPU not detected on this system. AMD-specific optimizations are unavailable.",
                isDetected: amdDetected,
                statusBadge: amdDetected ? "AMD DETECTED" : "NOT DETECTED",
                candidates: plan.AmdCandidates
            );

            return plan;
        }

        private static GpuGroupSummary CreateGroupSummary(
            GpuVendorScope scope,
            string groupName,
            string subtitle,
            string description,
            bool isDetected,
            string statusBadge,
            List<GpuRegistryItem> candidates)
        {
            var summary = new GpuGroupSummary
            {
                Scope = scope,
                GroupName = groupName,
                Subtitle = subtitle,
                Description = description,
                IsDetected = isDetected,
                StatusBadge = statusBadge,
                TotalCandidates = candidates.Count
            };

            if (!isDetected)
            {
                summary.ApplicableCount = 0;
                summary.OptimizedCount = 0;
                summary.PendingCount = 0;
                summary.NotApplicableCount = candidates.Count;
                return summary;
            }

            int app = 0;
            int opt = 0;
            int pend = 0;
            int notApp = 0;

            foreach (var item in candidates)
            {
                if (item.Status == RegistryValueStatus.AlreadyConfigured)
                {
                    app++;
                    opt++;
                }
                else if (item.Status == RegistryValueStatus.Recommended || item.Status == RegistryValueStatus.RequiresRestart || item.Status == RegistryValueStatus.Failed)
                {
                    app++;
                    pend++;
                }
                else
                {
                    notApp++;
                }
            }

            summary.ApplicableCount = app;
            summary.OptimizedCount = opt;
            summary.PendingCount = pend;
            summary.NotApplicableCount = notApp;

            return summary;
        }

        public (int Succeeded, int Failed, List<string> Logs) ApplyGroup(GpuVendorScope scope, HardwareSnapshot hw)
        {
            var plan = BuildPlan(hw, "NORMAL", forceRescan: true);
            var targetList = scope switch
            {
                GpuVendorScope.Universal => plan.UniversalCandidates,
                GpuVendorScope.Nvidia => plan.NvidiaCandidates,
                GpuVendorScope.Amd => plan.AmdCandidates,
                _ => new List<GpuRegistryItem>()
            };

            int succ = 0;
            int fail = 0;
            var logs = new List<string>();

            foreach (var item in targetList)
            {
                if (item.CanApply && item.Status != RegistryValueStatus.AlreadyConfigured)
                {
                    var (ok, msg) = _registryEngine.ApplyOptimization(item);
                    if (ok) succ++; else fail++;
                    logs.Add($"[{item.DisplayName}]: {msg}");
                }
            }

            return (succ, fail, logs);
        }

        public (int Succeeded, int Failed, List<string> Logs) ApplySelected(IEnumerable<string> selectedIds, HardwareSnapshot hw)
        {
            var plan = BuildPlan(hw, "CUSTOM", forceRescan: true);
            var selectedSet = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);

            int succ = 0;
            int fail = 0;
            var logs = new List<string>();

            foreach (var item in plan.AllCandidates)
            {
                if (selectedSet.Contains(item.Id) && item.CanApply)
                {
                    var (ok, msg) = _registryEngine.ApplyOptimization(item);
                    if (ok) succ++; else fail++;
                    logs.Add($"[{item.DisplayName}]: {msg}");
                }
            }

            return (succ, fail, logs);
        }
    }
}
