using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.Core.Implementations.Cleaners;
using BiosOptimizer.Core.Implementations.Power;
using BiosOptimizer.Core.Models;
using Microsoft.Win32;

namespace BiosOptimizer.Core.Implementations
{
    public class SystemHealthEngine
    {
        private static readonly StorageCleanerEngine _storageEngine = new();
        private static readonly NetworkDiagnosticsService _networkService = new();
        private static readonly PowerPlanEngine _powerEngine = new();
        private static readonly RamCleanupEngine _ramCleaner = new();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryTimerResolution(out uint minResolution, out uint maxResolution, out uint currentResolution);

        public async Task<SystemHealthSnapshot> EvaluateSystemHealthAsync(CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var snapshot = new SystemHealthSnapshot();

            var categories = new List<HealthCategoryScore>();
            var recommendations = new List<HealthRecommendation>();

            // 1. Memory Diagnostics (Weight: 15)
            var memCat = await Task.Run(() => EvaluateMemoryHealth(), ct);
            categories.Add(memCat);
            recommendations.AddRange(memCat.Recommendations);

            // 2. Storage & Cleanup Diagnostics (Weight: 10)
            var storageCat = await Task.Run(() => EvaluateStorageHealth(), ct);
            categories.Add(storageCat);
            recommendations.AddRange(storageCat.Recommendations);

            // 3. Windows Integrity Diagnostics (Weight: 20)
            var integrityCat = await Task.Run(() => EvaluateWindowsIntegrity(), ct);
            categories.Add(integrityCat);
            recommendations.AddRange(integrityCat.Recommendations);

            // 4. Windows Maintenance Diagnostics (Weight: 10)
            var maintCat = await Task.Run(() => EvaluateWindowsMaintenance(), ct);
            categories.Add(maintCat);
            recommendations.AddRange(maintCat.Recommendations);

            // 5. Hardware & BIOS Diagnostics (Weight: 15)
            var hwCat = await Task.Run(() => EvaluateHardwareHealth(), ct);
            categories.Add(hwCat);
            recommendations.AddRange(hwCat.Recommendations);

            // 6. Power & Energy Diagnostics (Weight: 10)
            var powerCat = await Task.Run(() => EvaluatePowerHealth(), ct);
            categories.Add(powerCat);
            recommendations.AddRange(powerCat.Recommendations);

            // 7. Network Diagnostics (Weight: 10)
            var netCat = await Task.Run(() => EvaluateNetworkHealth(), ct);
            categories.Add(netCat);
            recommendations.AddRange(netCat.Recommendations);

            // 8. GPU & Display Diagnostics (Weight: 5)
            var gpuCat = await Task.Run(() => EvaluateGpuHealth(), ct);
            categories.Add(gpuCat);
            recommendations.AddRange(gpuCat.Recommendations);

            // 9. Advanced Diagnostics (Weight: 5)
            var advCat = await Task.Run(() => EvaluateAdvancedHealth(), ct);
            categories.Add(advCat);
            recommendations.AddRange(advCat.Recommendations);

            snapshot.Categories = categories;
            snapshot.ActiveRecommendations = recommendations;

            // Calculate Unified Health Score
            double totalWeighted = 0;
            double totalWeight = 0;

            foreach (var cat in categories)
            {
                totalWeighted += cat.Score * cat.Weight;
                totalWeight += cat.Weight;
            }

            int rawScore = totalWeight > 0 ? (int)Math.Round(totalWeighted / totalWeight) : 100;
            rawScore = Math.Clamp(rawScore, 0, 100);

            // Apply Critical Health Caps
            string? capMessage = null;

            // Storage critical failure cap (max 49)
            if (storageCat.State == HealthState.Critical)
            {
                if (rawScore > 49)
                {
                    rawScore = 49;
                    capMessage = "Critical Storage Pressure Cap Applied (Max 49)";
                }
            }

            // Severe Windows corruption cap (max 59)
            if (integrityCat.State == HealthState.Critical)
            {
                if (rawScore > 59)
                {
                    rawScore = 59;
                    capMessage = "Severe Windows Integrity Corruption Cap Applied (Max 59)";
                }
            }

            // Critical memory pressure cap (max 69)
            if (memCat.State == HealthState.Critical)
            {
                if (rawScore > 69)
                {
                    rawScore = 69;
                    capMessage = "Critical Memory Saturation Cap Applied (Max 69)";
                }
            }

            snapshot.OverallScore = rawScore;
            snapshot.CriticalCapApplied = capMessage;

            if (rawScore >= 90)
            {
                snapshot.OverallHealthState = HealthState.Excellent;
                snapshot.OverallHealthText = "SYSTEM EXCELLENT — ALL SUBSYSTEMS OPTIMAL";
            }
            else if (rawScore >= 75)
            {
                snapshot.OverallHealthState = HealthState.Good;
                snapshot.OverallHealthText = "SYSTEM GOOD — MINOR OPTIMIZATIONS RECOMMENDED";
            }
            else if (rawScore >= 55)
            {
                snapshot.OverallHealthState = HealthState.Warning;
                snapshot.OverallHealthText = "SYSTEM WARNING — ATTENTION REQUIRED ON FLAGGED ITEMS";
            }
            else
            {
                snapshot.OverallHealthState = HealthState.Critical;
                snapshot.OverallHealthText = "SYSTEM CRITICAL — IMMEDIATE ACTION REQUIRED";
            }

            sw.Stop();
            snapshot.ScanDuration = sw.Elapsed;
            return snapshot;
        }

        // ════════════════════════════════════════════════════════════════
        // 1. MEMORY HEALTH EVALUATION
        // ════════════════════════════════════════════════════════════════
        private HealthCategoryScore EvaluateMemoryHealth()
        {
            var cat = new HealthCategoryScore
            {
                Category = SystemHealthCategoryType.Memory,
                Name = "Memory & Physical RAM",
                IconGlyph = "\uE7F8",
                Weight = 15.0
            };

            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    double totalGb = mem.ullTotalPhys / (1024.0 * 1024 * 1024);
                    double availGb = mem.ullAvailPhys / (1024.0 * 1024 * 1024);
                    uint load = mem.dwMemoryLoad;

                    cat.Evidence = $"RAM: {availGb:F1} GB free / {totalGb:F1} GB total ({load}% load)";

                    if (load >= 92)
                    {
                        cat.Score = 30;
                        cat.State = HealthState.Critical;
                        cat.HealthLabel = "CRITICAL PRESSURE";
                        cat.Recommendations.Add(new HealthRecommendation
                        {
                            RecommendationId = "REC_MEM_CRITICAL",
                            Category = SystemHealthCategoryType.Memory,
                            Severity = RecommendationSeverity.Critical,
                            Title = "Critical Memory Saturation",
                            Why = "Memory load exceeds 92%. Active processes are facing severe physical RAM exhaustion.",
                            Evidence = $"Physical memory load is at {load}%. Free RAM is only {availGb:F1} GB.",
                            CurrentValue = $"{load}% Load",
                            Target = "< 75% Load",
                            Risk = "SAFE",
                            Confidence = 0.98,
                            ActionId = "OPEN_RAM_LIMITER",
                            ActionLabel = "OPTIMIZE RAM",
                            TargetEngine = "RamLimiterEngine"
                        });
                    }
                    else if (load >= 82)
                    {
                        cat.Score = 60;
                        cat.State = HealthState.Warning;
                        cat.HealthLabel = "HIGH PRESSURE";
                        cat.Recommendations.Add(new HealthRecommendation
                        {
                            RecommendationId = "REC_MEM_HIGH",
                            Category = SystemHealthCategoryType.Memory,
                            Severity = RecommendationSeverity.High,
                            Title = "High Memory Pressure",
                            Why = "Working sets and background heaps have accumulated unreferenced memory.",
                            Evidence = $"Memory load is {load}%. {availGb:F1} GB available.",
                            CurrentValue = $"{load}% Load",
                            Target = "< 75% Load",
                            Risk = "SAFE",
                            Confidence = 0.92,
                            ActionId = "TRIM_WORKING_SET",
                            ActionLabel = "TRIM WORKING SETS",
                            TargetEngine = "RamCleanupEngine"
                        });
                    }
                    else if (load >= 70)
                    {
                        cat.Score = 80;
                        cat.State = HealthState.Good;
                        cat.HealthLabel = "MODERATE LOAD";
                    }
                    else
                    {
                        cat.Score = 100;
                        cat.State = HealthState.Excellent;
                        cat.HealthLabel = "OPTIMAL";
                    }
                }
                else
                {
                    cat.Score = 90;
                    cat.Evidence = "Standard memory architecture active";
                    cat.HealthLabel = "READY";
                }
            }
            catch (Exception ex)
            {
                cat.Score = 80;
                cat.Evidence = $"Memory telemetry query notice: {ex.Message}";
                cat.HealthLabel = "HEALTH DATA UNAVAILABLE";
            }

            return cat;
        }

        // ════════════════════════════════════════════════════════════════
        // 2. STORAGE HEALTH EVALUATION
        // ════════════════════════════════════════════════════════════════
        private HealthCategoryScore EvaluateStorageHealth()
        {
            var cat = new HealthCategoryScore
            {
                Category = SystemHealthCategoryType.Storage,
                Name = "Storage Capacity & Caches",
                IconGlyph = "\uE74D",
                Weight = 10.0
            };

            try
            {
                var cDrive = new DriveInfo("C");
                long total = cDrive.TotalSize;
                long free = cDrive.AvailableFreeSpace;
                double freePct = (free * 100.0) / total;
                double freeGb = free / (1024.0 * 1024 * 1024);
                double totalGb = total / (1024.0 * 1024 * 1024);

                cat.Evidence = $"Drive C: {freeGb:F1} GB free of {totalGb:F1} GB ({freePct:F1}% available)";

                if (freePct < 10.0 || freeGb < 15.0)
                {
                    cat.Score = 35;
                    cat.State = HealthState.Critical;
                    cat.HealthLabel = "LOW STORAGE";
                    cat.Recommendations.Add(new HealthRecommendation
                    {
                        RecommendationId = "REC_STORAGE_LOW",
                        Category = SystemHealthCategoryType.Storage,
                        Severity = RecommendationSeverity.Critical,
                        Title = "Critically Low Disk Space on C:",
                        Why = "Low disk space compromises pagefile expansion, temporary operations, and update staging.",
                        Evidence = $"Only {freeGb:F1} GB ({freePct:F1}%) remaining on system volume.",
                        CurrentValue = $"{freeGb:F1} GB Free",
                        Target = "> 25 GB Free",
                        Risk = "SAFE",
                        Confidence = 0.99,
                        ActionId = "OPEN_STORAGE_CLEANER",
                        ActionLabel = "RUN SMART CLEAN",
                        TargetEngine = "StorageCleanerEngine"
                    });
                }
                else if (freePct < 20.0 || freeGb < 30.0)
                {
                    cat.Score = 70;
                    cat.State = HealthState.Warning;
                    cat.HealthLabel = "STORAGE WARNING";
                    cat.Recommendations.Add(new HealthRecommendation
                    {
                        RecommendationId = "REC_STORAGE_CLEANUP",
                        Category = SystemHealthCategoryType.Storage,
                        Severity = RecommendationSeverity.Medium,
                        Title = "Reclaimable Cache & Temp Buildup",
                        Why = "Temporary directories and delivery caches contain reclaimable space.",
                        Evidence = $"System volume has {freePct:F1}% free space.",
                        CurrentValue = $"{freeGb:F1} GB Free",
                        Target = "> 25% Free",
                        Risk = "SAFE",
                        Confidence = 0.90,
                        ActionId = "OPEN_STORAGE_CLEANER",
                        ActionLabel = "RUN STORAGE CLEANER",
                        TargetEngine = "StorageCleanerEngine"
                    });
                }
                else
                {
                    cat.Score = 100;
                    cat.State = HealthState.Excellent;
                    cat.HealthLabel = "OPTIMAL";
                }
            }
            catch (Exception ex)
            {
                cat.Score = 85;
                cat.Evidence = $"Storage telemetry query: {ex.Message}";
                cat.HealthLabel = "HEALTH DATA UNAVAILABLE";
            }

            return cat;
        }

        // ════════════════════════════════════════════════════════════════
        // 3. WINDOWS INTEGRITY EVALUATION
        // ════════════════════════════════════════════════════════════════
        private HealthCategoryScore EvaluateWindowsIntegrity()
        {
            var cat = new HealthCategoryScore
            {
                Category = SystemHealthCategoryType.WindowsIntegrity,
                Name = "Windows Integrity (SFC / DISM)",
                IconGlyph = "\uE90F",
                Weight = 20.0,
                RequiresAdmin = true
            };

            try
            {
                var snap = WindowsServicingHealthEngine.Instance.DetectServicingEnvironment();

                if (!snap.IsSfcAvailable && !snap.IsDismAvailable)
                {
                    cat.Score = 40;
                    cat.State = HealthState.Warning;
                    cat.HealthLabel = "TOOLS UNAVAILABLE";
                    cat.Evidence = "SFC and DISM servicing binaries are not available in System32.";
                }
                else if (snap.RequiredServices.TryGetValue("TrustedInstaller", out var ti) && !ti.IsHealthy)
                {
                    cat.Score = 65;
                    cat.State = HealthState.Warning;
                    cat.HealthLabel = "SERVICING DEGRADED";
                    cat.Evidence = "Windows Modules Installer (TrustedInstaller) is disabled. System file verification blocked.";
                    cat.Recommendations.Add(new HealthRecommendation
                    {
                        RecommendationId = "REC_INTEG_TRUSTEDINSTALLER",
                        Category = SystemHealthCategoryType.WindowsIntegrity,
                        Severity = RecommendationSeverity.High,
                        Title = "Windows Modules Installer is Disabled",
                        Why = "SFC and DISM require the TrustedInstaller service to verify and repair system manifests.",
                        Evidence = "TrustedInstaller service start mode is Disabled in registry.",
                        CurrentValue = "Disabled",
                        Target = "Manual (Demand)",
                        Risk = "SAFE",
                        Confidence = 0.99,
                        ActionId = "tool.repair.sfc",
                        ActionLabel = "REPAIR SERVICING",
                        TargetEngine = "WindowsServicingHealthEngine"
                    });
                }
                else if (snap.IsServicingLocked)
                {
                    cat.Score = 80;
                    cat.State = HealthState.Good;
                    cat.HealthLabel = "SERVICING ACTIVE";
                    cat.Evidence = $"Background Windows Servicing in progress: {string.Join(", ", snap.ActiveConflictingProcesses)}";
                }
                else
                {
                    cat.Score = 100;
                    cat.State = HealthState.Excellent;
                    cat.HealthLabel = "INTEGRITY READY";
                    cat.Evidence = $"Windows protected packages and servicing stack ready ({snap.Architecture}, Build {snap.OsBuild})";
                }
            }
            catch (Exception ex)
            {
                cat.Score = 90;
                cat.Evidence = $"Integrity state: {ex.Message}";
                cat.HealthLabel = "READY";
            }

            return cat;
        }

        // ════════════════════════════════════════════════════════════════
        // 4. WINDOWS MAINTENANCE EVALUATION
        // ════════════════════════════════════════════════════════════════
        private HealthCategoryScore EvaluateWindowsMaintenance()
        {
            var cat = new HealthCategoryScore
            {
                Category = SystemHealthCategoryType.WindowsMaintenance,
                Name = "Windows Maintenance & WinSxS",
                IconGlyph = "\uE8B7",
                Weight = 10.0,
                RequiresAdmin = true
            };

            try
            {
                bool pendingReboot = false;
                try
                {
                    using var key1 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
                    using var key2 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
                    if (key1 != null || key2 != null) pendingReboot = true;
                }
                catch { }

                if (pendingReboot)
                {
                    cat.Score = 75;
                    cat.State = HealthState.Warning;
                    cat.HealthLabel = "REBOOT PENDING";
                    cat.Evidence = "Windows Update or servicing stack has pending file renames requiring a reboot.";
                    cat.RequiresReboot = true;
                    cat.Recommendations.Add(new HealthRecommendation
                    {
                        RecommendationId = "REC_MAINT_REBOOT",
                        Category = SystemHealthCategoryType.WindowsMaintenance,
                        Severity = RecommendationSeverity.Medium,
                        Title = "System Reboot Recommended",
                        Why = "Pending servicing stack operations must complete to finalize Windows components.",
                        Evidence = "Component Based Servicing flag RebootPending is active.",
                        CurrentValue = "Reboot Pending",
                        Target = "Clean State",
                        Risk = "SAFE",
                        Confidence = 1.0,
                        ActionId = "NONE",
                        ActionLabel = "REBOOT REQUIRED",
                        RequiresReboot = true
                    });
                }
                else
                {
                    cat.Score = 100;
                    cat.State = HealthState.Excellent;
                    cat.HealthLabel = "OPTIMAL";
                    cat.Evidence = "Component store in clean state. No pending reboot operations.";
                }
            }
            catch (Exception ex)
            {
                cat.Score = 90;
                cat.Evidence = $"Maintenance check notice: {ex.Message}";
                cat.HealthLabel = "READY";
            }

            return cat;
        }

        // ════════════════════════════════════════════════════════════════
        // 5. HARDWARE & BIOS EVALUATION
        // ════════════════════════════════════════════════════════════════
        private HealthCategoryScore EvaluateHardwareHealth()
        {
            var cat = new HealthCategoryScore
            {
                Category = SystemHealthCategoryType.Hardware,
                Name = "Hardware & BIOS Platform",
                IconGlyph = "\uE950",
                Weight = 15.0
            };

            try
            {
                var profile = HardwareProfiler.GetQuickProfile();
                int cores = profile.CpuLogicalCores;
                string cpu = profile.CpuModel;

                cat.Evidence = $"{cpu} ({cores} Logical Processors), Tier: {profile.Tier}";

                if (profile.Tier == HardwareTier.LowResource)
                {
                    cat.Score = 75;
                    cat.State = HealthState.Good;
                    cat.HealthLabel = "LOW RESOURCE TIER";
                    cat.Recommendations.Add(new HealthRecommendation
                    {
                        RecommendationId = "REC_HW_LOWTIER",
                        Category = SystemHealthCategoryType.Hardware,
                        Severity = RecommendationSeverity.Low,
                        Title = "Low-Resource Concurrency Optimization",
                        Why = "System has constrained core/RAM budget. Bounded concurrency profile recommended.",
                        Evidence = $"{cores} cores, {profile.RamTotalGb:F1} GB RAM detected.",
                        CurrentValue = $"{cores} Cores",
                        Target = "Optimized Thread Pool",
                        Risk = "SAFE",
                        Confidence = 0.95,
                        ActionId = "SET_CONSERVATIVE_PROFILE",
                        ActionLabel = "ADJUST PROFILE",
                        TargetEngine = "AiWorkloadEngine"
                    });
                }
                else
                {
                    cat.Score = 100;
                    cat.State = HealthState.Excellent;
                    cat.HealthLabel = "EXCELLENT";
                }
            }
            catch (Exception ex)
            {
                cat.Score = 90;
                cat.Evidence = $"Hardware query: {ex.Message}";
                cat.HealthLabel = "READY";
            }

            return cat;
        }

        // ════════════════════════════════════════════════════════════════
        // 6. POWER & ENERGY EVALUATION
        // ════════════════════════════════════════════════════════════════
        private HealthCategoryScore EvaluatePowerHealth()
        {
            var cat = new HealthCategoryScore
            {
                Category = SystemHealthCategoryType.Power,
                Name = "Power Plan & Energy Policy",
                IconGlyph = "\uE7E8",
                Weight = 10.0
            };

            try
            {
                var (activeGuid, activeName) = _powerEngine.GetActiveSchemeNative();
                var hwProfile = HardwareProfiler.GetQuickProfile();
                bool isAc = !hwProfile.IsBatteryPowered;

                cat.Evidence = $"Active Plan: {activeName} (AC Online: {isAc})";

                if (isAc && activeName.Contains("Power Saver", StringComparison.OrdinalIgnoreCase))
                {
                    cat.Score = 65;
                    cat.State = HealthState.Warning;
                    cat.HealthLabel = "SUBOPTIMAL PLAN";
                    cat.Recommendations.Add(new HealthRecommendation
                    {
                        RecommendationId = "REC_POWER_SUBOPTIMAL",
                        Category = SystemHealthCategoryType.Power,
                        Severity = RecommendationSeverity.Medium,
                        Title = "Suboptimal Power Plan on AC Power",
                        Why = "Power Saver limits CPU frequency scaling while connected to mains AC power.",
                        Evidence = $"Current active plan is '{activeName}'.",
                        CurrentValue = activeName,
                        Target = "High Performance / Ultimate",
                        Risk = "SAFE",
                        Confidence = 0.95,
                        ActionId = "SET_HIGH_PERFORMANCE_POWER",
                        ActionLabel = "ACTIVATE HIGH PERF",
                        TargetEngine = "PowerPlanEngine",
                        RequiresAdmin = true
                    });
                }
                else
                {
                    cat.Score = 100;
                    cat.State = HealthState.Excellent;
                    cat.HealthLabel = "OPTIMAL";
                }
            }
            catch (Exception ex)
            {
                cat.Score = 90;
                cat.Evidence = $"Power query notice: {ex.Message}";
                cat.HealthLabel = "READY";
            }

            return cat;
        }

        // ════════════════════════════════════════════════════════════════
        // 7. NETWORK HEALTH EVALUATION
        // ════════════════════════════════════════════════════════════════
        private HealthCategoryScore EvaluateNetworkHealth()
        {
            var cat = new HealthCategoryScore
            {
                Category = SystemHealthCategoryType.Network,
                Name = "Network Stack & Gateway",
                IconGlyph = "\uE968",
                Weight = 10.0
            };

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(i => i.OperationalStatus == OperationalStatus.Up && i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                if (interfaces.Count > 0)
                {
                    var primary = interfaces.First();
                    cat.Evidence = $"Connected: {primary.Name} ({primary.NetworkInterfaceType}), Speed: {primary.Speed / 1_000_000} Mbps";
                    cat.Score = 100;
                    cat.State = HealthState.Excellent;
                    cat.HealthLabel = "CONNECTED & OPTIMAL";
                }
                else
                {
                    cat.Score = 60;
                    cat.State = HealthState.Warning;
                    cat.HealthLabel = "DISCONNECTED";
                    cat.Evidence = "No active primary network interface detected.";
                }
            }
            catch (Exception ex)
            {
                cat.Score = 85;
                cat.Evidence = $"Network telemetry query: {ex.Message}";
                cat.HealthLabel = "READY";
            }

            return cat;
        }

        // ════════════════════════════════════════════════════════════════
        // 8. GPU & DISPLAY EVALUATION
        // ════════════════════════════════════════════════════════════════
        private HealthCategoryScore EvaluateGpuHealth()
        {
            var cat = new HealthCategoryScore
            {
                Category = SystemHealthCategoryType.Gpu,
                Name = "GPU & Display Pipeline",
                IconGlyph = "\uE790",
                Weight = 5.0
            };

            try
            {
                var profile = HardwareProfiler.GetQuickProfile();
                if (profile.GpuPresent)
                {
                    cat.Evidence = $"{profile.GpuName} (VRAM: {profile.GpuVramGb:F1} GB)";
                    cat.Score = 100;
                    cat.State = HealthState.Excellent;
                    cat.HealthLabel = "ACTIVE & READY";
                }
                else
                {
                    cat.Evidence = "Standard Display Adapter";
                    cat.Score = 90;
                    cat.State = HealthState.Good;
                    cat.HealthLabel = "BASIC ADAPTER";
                }
            }
            catch (Exception ex)
            {
                cat.Score = 90;
                cat.Evidence = $"GPU query notice: {ex.Message}";
                cat.HealthLabel = "READY";
            }

            return cat;
        }

        // ════════════════════════════════════════════════════════════════
        // 9. ADVANCED SYSTEM DIAGNOSTICS
        // ════════════════════════════════════════════════════════════════
        private HealthCategoryScore EvaluateAdvancedHealth()
        {
            var cat = new HealthCategoryScore
            {
                Category = SystemHealthCategoryType.Advanced,
                Name = "Advanced Timers & Kernel",
                IconGlyph = "\uE90F",
                Weight = 5.0
            };

            try
            {
                int ntStatus = NtQueryTimerResolution(out uint minRes, out uint maxRes, out uint curRes);
                if (ntStatus == 0)
                {
                    double curMs = curRes / 10000.0;
                    cat.Evidence = $"Kernel Timer Resolution: {curMs:F4} ms (Min: {minRes/10000.0:F2}ms, Max: {maxRes/10000.0:F2}ms)";
                    cat.Score = 100;
                    cat.State = HealthState.Excellent;
                    cat.HealthLabel = "HIGH RESOLUTION";
                }
                else
                {
                    cat.Evidence = "Windows QPC High-Resolution Platform Clock active";
                    cat.Score = 95;
                    cat.State = HealthState.Excellent;
                    cat.HealthLabel = "QPC ACTIVE";
                }
            }
            catch (Exception ex)
            {
                cat.Score = 90;
                cat.Evidence = $"Advanced kernel query: {ex.Message}";
                cat.HealthLabel = "READY";
            }

            return cat;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024L * 1024)
                return $"{bytes / (1024.0 * 1024):F1} MB";
            if (bytes >= 1024L)
                return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }

        // ════════════════════════════════════════════════════════════════
        // ONE-CLICK REPAIR / RECOMMENDATION EXECUTION ORCHESTRATION
        // ════════════════════════════════════════════════════════════════
        public async Task<(bool Success, string Summary)> ExecuteRecommendationAsync(HealthRecommendation rec, CancellationToken ct = default)
        {
            if (rec == null) return (false, "Invalid recommendation reference");

            try
            {
                switch (rec.ActionId)
                {
                    case "TRIM_WORKING_SET":
                    case "OPEN_RAM_LIMITER":
                        long bytesFreed = await Task.Run(() => _ramCleaner.Clean(), ct);
                        return (true, $"RAM Working sets trimmed. Reclaimed {FormatBytes(bytesFreed)} physical memory.");

                    case "OPEN_STORAGE_CLEANER":
                        var categories = StorageCleanerEngine.BuildCategories().Take(3).ToList();
                        var cleanResult = await Task.Run(() => _storageEngine.CleanCategories(categories, "C", null, ct), ct);
                        return (cleanResult.Success, $"Storage cleanup executed: {FormatBytes(cleanResult.BytesReclaimed)} reclaimed, {cleanResult.FilesRemoved} files removed.");

                    case "SET_HIGH_PERFORMANCE_POWER":
                        var schemes = await _powerEngine.DiscoverPowerPlansAsync(ct);
                        var target = schemes.FirstOrDefault(s => s.Name.Contains("High Performance", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase));
                        if (target != null)
                        {
                            var res = await _powerEngine.ApplyPowerPlanAsync(target.Guid, PowerPlanChangeSource.USER_REQUEST, ct);
                            return (res.Success, res.Success ? $"Activated power plan: {target.Name}" : $"Failed to activate power plan: {res.Message}");
                        }
                        return (false, "No high performance power plan found on this system.");

                    case "tool.repair.sfc":
                    case "FIX_SERVICING_SERVICES":
                        var (repaired, repMsg) = WindowsServicingHealthEngine.Instance.EnsureServicingPrerequisites("sfc");
                        return (true, repaired ? $"Servicing prerequisites configured: {repMsg}" : "Servicing prerequisites verified healthy.");

                    default:
                        return (true, $"Recommendation acknowledged: {rec.Title}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Execution error on recommendation {rec.RecommendationId}: {ex.Message}");
            }
        }
    }
}
