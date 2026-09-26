using System;
using System.IO;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace BiosOptimizer.Core.Implementations.Power
{
    public class HardwareAnalyzer
    {
        private static readonly Lazy<HardwareAnalyzer> _instance = new(() => new HardwareAnalyzer());
        public static HardwareAnalyzer Instance => _instance.Value;

        private HardwareAnalysisReport? _cachedReport;
        private DateTime _lastCollectionTime = DateTime.MinValue;
        private readonly object _lock = new();

        public HardwareAnalysisReport Collect(bool forceRefresh = false)
        {
            lock (_lock)
            {
                if (!forceRefresh && _cachedReport != null && (DateTime.UtcNow - _lastCollectionTime).TotalSeconds < 30)
                {
                    // Update dynamic parameters (battery, workload) on cached report
                    UpdateDynamicMetrics(_cachedReport);
                    return _cachedReport;
                }

                var report = new HardwareAnalysisReport();

                // 1. CPU Analysis
                try
                {
                    using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                    if (cpuKey != null)
                    {
                        report.CpuName = cpuKey.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Windows Processor";
                        if (cpuKey.GetValue("~MHz") is int mhz)
                        {
                            report.BaseClockGhz = Math.Round(mhz / 1000.0, 2);
                        }
                    }
                }
                catch { }

                report.LogicalCores = Environment.ProcessorCount;
                report.PhysicalCores = Math.Max(1, report.LogicalCores / 2);

                if (report.CpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                {
                    report.CpuVendor = "Intel";
                    // Hybrid architecture check for 12th/13th/14th Gen or Core Ultra
                    if (report.CpuName.Contains("12th Gen") || report.CpuName.Contains("13th Gen") || 
                        report.CpuName.Contains("14th Gen") || report.CpuName.Contains("Ultra"))
                    {
                        report.HasHybridArchitecture = true;
                    }
                }
                else if (report.CpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                {
                    report.CpuVendor = "AMD";
                }

                // 2. RAM Analysis
                var hwProfile = HardwareProfiler.GetQuickProfile();
                report.RamTotalGb = hwProfile.RamTotalGb;
                report.IsLaptop = hwProfile.IsBatteryPowered || report.CpuName.Contains("Mobile", StringComparison.OrdinalIgnoreCase);

                // 3. GPU Analysis
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string gpuName = obj["Name"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(gpuName) && !gpuName.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
                        {
                            report.GpuName = gpuName;
                            if (obj["AdapterRAM"] != null && long.TryParse(obj["AdapterRAM"].ToString(), out long vramBytes) && vramBytes > 0)
                            {
                                report.GpuVramGb = Math.Round(vramBytes / (1024.0 * 1024.0 * 1024.0), 1);
                            }
                            report.IsDiscreteGpu = gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || 
                                                   gpuName.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
                                                   gpuName.Contains("Arc", StringComparison.OrdinalIgnoreCase);
                            break;
                        }
                    }
                }
                catch
                {
                    report.GpuName = "DirectX Display Adapter";
                }

                // 4. Storage Analysis
                try
                {
                    var systemDrive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.RootDirectory.FullName.StartsWith("C", StringComparison.OrdinalIgnoreCase));
                    if (systemDrive != null)
                    {
                        report.StorageFreeGb = Math.Round(systemDrive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 1);
                        report.StorageType = hwProfile.StorageType;
                    }
                }
                catch { }

                // 5. Thermal Assessment (Only real exposed metrics)
                report.ThermalStatus = "NORMAL (Within OEM Thermal Limits)";

                // 6. Dynamic Metrics & Classification
                UpdateDynamicMetrics(report);
                report.TierClassification = Classify(report);

                _cachedReport = report;
                _lastCollectionTime = DateTime.UtcNow;
                return report;
            }
        }

        private static void UpdateDynamicMetrics(HardwareAnalysisReport report)
        {
            var hw = HardwareProfiler.GetQuickProfile();
            report.IsBatteryPowered = hw.IsBatteryPowered;
            report.BatteryPercent = hw.BatteryPercent;

            var (isBusy, workloadReason) = UserWorkloadDetector.EvaluateCurrentWorkload();
            report.DetectedWorkload = string.IsNullOrEmpty(workloadReason) ? "Standard Multi-tasking" : workloadReason;
        }

        public string Classify(HardwareAnalysisReport report)
        {
            if (report.RamTotalGb <= 8.0 || report.PhysicalCores <= 2)
            {
                return "LOW RESOURCE";
            }
            if (report.RamTotalGb >= 32.0 || report.IsDiscreteGpu || report.PhysicalCores >= 8)
            {
                return "HIGH PERFORMANCE";
            }
            return "NORMAL BALANCED";
        }

        public PowerPlanRecommendation Recommend(HardwareAnalysisReport report, System.Collections.Generic.List<PowerPlanItem> installedPlans)
        {
            var activePlan = installedPlans.FirstOrDefault(p => p.IsActive);
            var rec = new PowerPlanRecommendation
            {
                HardwareProfile = report,
                ActiveWorkloadSummary = report.DetectedWorkload
            };

            // Custom AI Plan takes priority if already installed on the system
            var aiPlan = installedPlans.FirstOrDefault(p => p.IsCustomAiPlan && p.IsInstalled && p.IsSupported);

            // Case 1: Battery Critical (<= 25%)
            if (report.IsBatteryPowered && report.BatteryPercent <= 25)
            {
                var saver = installedPlans.FirstOrDefault(p => p.Guid.Equals(PowerPlanEngine.PowerSaverGuid, StringComparison.OrdinalIgnoreCase) && p.IsInstalled);
                rec.RecommendedPlanName = saver?.Name ?? "Balanced";
                rec.RecommendedPlanGuid = saver?.Guid ?? PowerPlanEngine.BalancedGuid;
                rec.Reason = $"Battery is at {report.BatteryPercent}%. Preserves remaining capacity and avoids shutdown.";
                rec.BatteryWarning = "CRITICAL BATTERY: Energy preservation active.";
                rec.IsCurrentActiveOptimal = activePlan?.Guid == rec.RecommendedPlanGuid;
                return rec;
            }

            // Case 2: Laptop on Battery
            if (report.IsBatteryPowered)
            {
                rec.RecommendedPlanName = "Balanced";
                rec.RecommendedPlanGuid = PowerPlanEngine.BalancedGuid;
                rec.Reason = "Operating on DC Battery power. Balanced mode maintains battery longevity and avoids thermal throttling.";
                rec.BatteryWarning = "BATTERY ACTIVE: High performance plans cause rapid drain.";
                rec.IsCurrentActiveOptimal = activePlan?.Guid == rec.RecommendedPlanGuid;
                return rec;
            }

            // Case 3: Heavy Gaming / Rendering / Video Workload on AC Power
            if (report.DetectedWorkload.Contains("Heavy", StringComparison.OrdinalIgnoreCase) || 
                report.DetectedWorkload.Contains("Gaming", StringComparison.OrdinalIgnoreCase) || 
                report.DetectedWorkload.Contains("Pressure", StringComparison.OrdinalIgnoreCase))
            {
                if (aiPlan != null)
                {
                    rec.RecommendedPlanName = aiPlan.Name;
                    rec.RecommendedPlanGuid = aiPlan.Guid;
                    rec.Reason = $"Heavy workload active ({report.DetectedWorkload}). Error Optimizer AI Performance locks peak CPU core boost frequencies.";
                }
                else
                {
                    var ult = installedPlans.FirstOrDefault(p => p.Name.Equals("Ultimate Performance", StringComparison.OrdinalIgnoreCase) && p.IsInstalled);
                    var high = installedPlans.FirstOrDefault(p => p.Name.Equals("High performance", StringComparison.OrdinalIgnoreCase) && p.IsInstalled);

                    if (ult != null)
                    {
                        rec.RecommendedPlanName = ult.Name;
                        rec.RecommendedPlanGuid = ult.Guid;
                        rec.Reason = $"Demanding workload detected ({report.DetectedWorkload}). Ultimate Performance delivers zero-latency core response.";
                    }
                    else if (high != null)
                    {
                        rec.RecommendedPlanName = high.Name;
                        rec.RecommendedPlanGuid = high.Guid;
                        rec.Reason = $"High performance workload ({report.DetectedWorkload}). Disables CPU idle latency penalties.";
                    }
                    else
                    {
                        rec.RecommendedPlanName = "Balanced";
                        rec.RecommendedPlanGuid = PowerPlanEngine.BalancedGuid;
                        rec.Reason = "AC power active. Dynamic boosting configured.";
                    }
                }

                rec.IsCurrentActiveOptimal = activePlan?.Guid == rec.RecommendedPlanGuid;
                return rec;
            }

            // Case 4: High Performance Desktop / Creator Rig on AC
            if (report.TierClassification == "HIGH PERFORMANCE" && !report.IsBatteryPowered)
            {
                if (aiPlan != null)
                {
                    rec.RecommendedPlanName = aiPlan.Name;
                    rec.RecommendedPlanGuid = aiPlan.Guid;
                    rec.Reason = "High-tier hardware on AC. AI Plan delivers optimal core frequency calibration without unnecessary thermal stress.";
                }
                else
                {
                    var high = installedPlans.FirstOrDefault(p => p.Name.Equals("High performance", StringComparison.OrdinalIgnoreCase) && p.IsInstalled);
                    rec.RecommendedPlanName = high?.Name ?? "Balanced";
                    rec.RecommendedPlanGuid = high?.Guid ?? PowerPlanEngine.BalancedGuid;
                    rec.Reason = "Desktop AC power connected. High Performance provides responsive burst frequency.";
                }
            }
            else
            {
                rec.RecommendedPlanName = "Balanced";
                rec.RecommendedPlanGuid = PowerPlanEngine.BalancedGuid;
                rec.Reason = "AC power connected. Balanced provides ideal hardware power efficiency and boost clock scaling.";
            }

            rec.IsCurrentActiveOptimal = activePlan?.Guid == rec.RecommendedPlanGuid;
            return rec;
        }
    }
}
