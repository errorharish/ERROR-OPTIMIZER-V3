using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using BiosOptimizer.Core.Interfaces;
using Microsoft.Win32;

namespace BiosOptimizer.Core.Implementations.RegistryValues
{
    public enum GpuVendorType
    {
        Nvidia,
        Amd,
        Intel,
        Unknown
    }

    public enum GpuRegistryCategory
    {
        Universal,
        NvidiaSpecific,
        AmdSpecific,
        Diagnostic
    }

    public enum GpuOptimizationRisk
    {
        Safe,
        LowRisk,
        MediumRisk,
        HighRisk,
        DiagnosticOnly,
        Experimental,
        Unsupported
    }

    public class GpuHardwareInfo
    {
        public string Name { get; set; } = "Generic Graphics Adapter";
        public GpuVendorType VendorType { get; set; } = GpuVendorType.Unknown;
        public string VendorName { get; set; } = "Unknown Vendor";
        public string DriverVersion { get; set; } = "Unknown";
        public string DriverDate { get; set; } = "Unknown";
        public string WddmVersion { get; set; } = "WDDM 2.7+";
        public double WddmNumeric { get; set; } = 2.7;
        public string GpuType { get; set; } = "Discrete"; // Discrete, Integrated, Hybrid
        public bool IsPrimary { get; set; } = true;
        public int GpuCount { get; set; } = 1;
        public bool IsLaptop { get; set; } = false;
        public bool IsHybridGraphics { get; set; } = false;
        public string WindowsVersion { get; set; } = "Windows 11 / 10";
        public string WindowsBuild { get; set; } = "Unknown";
        public string AdapterRegistryKeyPath { get; set; } = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";
        public bool SupportsHags { get; set; } = false;
    }

    public class GpuRegistryItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public GpuRegistryCategory Category { get; set; } = GpuRegistryCategory.Universal;
        public string CategoryDisplayName => Category switch
        {
            GpuRegistryCategory.Universal => "UNIVERSAL GPU",
            GpuRegistryCategory.NvidiaSpecific => "NVIDIA-SPECIFIC GPU",
            GpuRegistryCategory.AmdSpecific => "AMD-SPECIFIC GPU",
            GpuRegistryCategory.Diagnostic => "DIAGNOSTIC CONFIGURATION",
            _ => "GPU REGISTRY"
        };
        public string SubCategory { get; set; } = "Scheduling";
        public string RegistryPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public RegistryValueKind ValueType { get; set; } = RegistryValueKind.DWord;
        public string Description { get; set; } = string.Empty;
        public GpuOptimizationRisk Risk { get; set; } = GpuOptimizationRisk.LowRisk;
        public string RiskLevel => Risk switch
        {
            GpuOptimizationRisk.Safe => "SAFE",
            GpuOptimizationRisk.LowRisk => "LOW RISK",
            GpuOptimizationRisk.MediumRisk => "MEDIUM RISK",
            GpuOptimizationRisk.HighRisk => "HIGH RISK",
            GpuOptimizationRisk.DiagnosticOnly => "DIAGNOSTIC ONLY",
            GpuOptimizationRisk.Experimental => "EXPERIMENTAL",
            _ => "UNSUPPORTED"
        };
        public bool RequiresRestart { get; set; } = true;
        public string HardwareRequirement { get; set; } = "Any Supported GPU";
        public string DriverRequirement { get; set; } = "WDDM 2.0+ Compatible Driver";
        public string WhyApplicable { get; set; } = string.Empty;
        public string WhyRecommended { get; set; } = string.Empty;

        public string CurrentValueDisplay { get; set; } = "Unknown";
        public object? CurrentValueRaw { get; set; }
        public string TargetValueDisplay { get; set; } = "Unknown";
        public object? TargetValueRaw { get; set; }

        public RegistryValueStatus Status { get; set; } = RegistryValueStatus.Recommended;
        public string StatusText => Status switch
        {
            RegistryValueStatus.AlreadyConfigured => "ALREADY CONFIGURED",
            RegistryValueStatus.Recommended => "APPLICABLE",
            RegistryValueStatus.RequiresRestart => "RESTART REQUIRED",
            RegistryValueStatus.NotApplicable => "NOT APPLICABLE",
            RegistryValueStatus.Failed => "FAILED",
            _ => "VERIFICATION REQUIRED"
        };

        public bool IsApplicable => Status != RegistryValueStatus.NotApplicable && Risk != GpuOptimizationRisk.Unsupported;
        public bool CanApply => IsApplicable && 
                                (Status == RegistryValueStatus.Recommended || Status == RegistryValueStatus.Failed) && 
                                Risk != GpuOptimizationRisk.DiagnosticOnly && 
                                Risk != GpuOptimizationRisk.Unsupported &&
                                Category != GpuRegistryCategory.Diagnostic;
        public bool HasBackup { get; set; }
        public string VerificationMethod { get; set; } = "Live Windows Registry readback verification";
        public string VerificationStatus { get; set; } = "UNVERIFIED";
        public string RollbackStatus { get; set; } = "AVAILABLE";
    }

    public class GpuRegistrySnapshot
    {
        public string ItemId { get; set; } = string.Empty;
        public string RegistryPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public object? PreviousValueRaw { get; set; }
        public string ValueTypeString { get; set; } = "DWord";
        public bool PreviousExisted { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class GpuRegistryValueEngine
    {
        private static readonly Lazy<GpuRegistryValueEngine> _lazy = new(() => new GpuRegistryValueEngine());
        public static GpuRegistryValueEngine Instance => _lazy.Value;

        private static string SnapshotDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ErrorOptimizer", "GpuRegistry");

        private static string SnapshotFile => Path.Combine(SnapshotDirectory, "GpuRegistrySnapshots.json");

        private GpuHardwareInfo? _cachedHardware;
        private readonly object _lock = new();

        public GpuRegistryValueEngine()
        {
            try
            {
                if (!Directory.Exists(SnapshotDirectory))
                {
                    Directory.CreateDirectory(SnapshotDirectory);
                }
            }
            catch { }
        }

        // ── 1. Real Hardware Detection ──────────────────────────────────────
        public GpuHardwareInfo DetectGpuHardware(bool forceRescan = false)
        {
            lock (_lock)
            {
                if (!forceRescan && _cachedHardware != null)
                {
                    return _cachedHardware;
                }

                var info = new GpuHardwareInfo();

                // Read OS details from Registry
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                    if (key != null)
                    {
                        string prod = key.GetValue("ProductName")?.ToString() ?? "Windows";
                        string build = key.GetValue("CurrentBuild")?.ToString() ?? Environment.OSVersion.Version.Build.ToString();
                        string displayVer = key.GetValue("DisplayVersion")?.ToString() ?? "";
                        info.WindowsVersion = $"{prod} {displayVer}".Trim();
                        info.WindowsBuild = build;
                    }
                }
                catch
                {
                    info.WindowsVersion = "Windows";
                    info.WindowsBuild = Environment.OSVersion.Version.Build.ToString();
                }

                // Detect Laptop vs Desktop
                try
                {
                    var p = HardwareProfiler.GetQuickProfile(forceRefresh: forceRescan);
                    info.IsLaptop = p.IsBatteryPowered || File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "battery.sys"));
                }
                catch { }

                // Query Video Controller and Class Registry Key
                int detectedCount = 0;
                bool hasNvidia = false;
                bool hasAmd = false;
                bool hasIntel = false;
                string primaryName = "";
                string primaryDriver = "";
                string primaryDriverDate = "";
                string adapterKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";

                try
                {
                    using var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                    if (classKey != null)
                    {
                        var subKeys = classKey.GetSubKeyNames().Where(s => Regex.IsMatch(s, @"^\d{4}$")).OrderBy(s => s);
                        foreach (var sub in subKeys)
                        {
                            using var devKey = classKey.OpenSubKey(sub);
                            if (devKey == null) continue;

                            string desc = devKey.GetValue("DriverDesc")?.ToString() ?? devKey.GetValue("Device Description")?.ToString() ?? "";
                            string provider = devKey.GetValue("ProviderName")?.ToString() ?? "";
                            string drvVer = devKey.GetValue("DriverVersion")?.ToString() ?? "";
                            string drvDate = devKey.GetValue("DriverDate")?.ToString() ?? "";

                            if (string.IsNullOrWhiteSpace(desc) || desc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || desc.Contains("Remote", StringComparison.OrdinalIgnoreCase))
                                continue;

                            detectedCount++;

                            if (desc.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                            {
                                hasNvidia = true;
                                if (string.IsNullOrEmpty(primaryName) || !primaryName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                                {
                                    primaryName = desc;
                                    primaryDriver = drvVer;
                                    primaryDriverDate = drvDate;
                                    adapterKey = $@"SYSTEM\CurrentControlSet\Control\Class\{{4d36e968-e325-11ce-bfc1-08002be10318}}\{sub}";
                                }
                            }
                            else if (desc.Contains("AMD", StringComparison.OrdinalIgnoreCase) || desc.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || provider.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase))
                            {
                                hasAmd = true;
                                if (string.IsNullOrEmpty(primaryName) || (!primaryName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) && !primaryName.Contains("AMD", StringComparison.OrdinalIgnoreCase)))
                                {
                                    primaryName = desc;
                                    primaryDriver = drvVer;
                                    primaryDriverDate = drvDate;
                                    adapterKey = $@"SYSTEM\CurrentControlSet\Control\Class\{{4d36e968-e325-11ce-bfc1-08002be10318}}\{sub}";
                                }
                            }
                            else if (desc.Contains("Intel", StringComparison.OrdinalIgnoreCase) || desc.Contains("UHD", StringComparison.OrdinalIgnoreCase) || desc.Contains("Iris", StringComparison.OrdinalIgnoreCase) || desc.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                            {
                                hasIntel = true;
                                if (string.IsNullOrEmpty(primaryName))
                                {
                                    primaryName = desc;
                                    primaryDriver = drvVer;
                                    primaryDriverDate = drvDate;
                                    adapterKey = $@"SYSTEM\CurrentControlSet\Control\Class\{{4d36e968-e325-11ce-bfc1-08002be10318}}\{sub}";
                                }
                            }
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(primaryName))
                {
                    primaryName = "DirectX Display Adapter";
                    primaryDriver = "Standard Windows Display Driver";
                    primaryDriverDate = DateTime.Now.ToString("yyyy-MM-dd");
                }

                info.Name = primaryName;
                info.DriverVersion = !string.IsNullOrEmpty(primaryDriver) ? primaryDriver : "Standard Driver";
                info.DriverDate = !string.IsNullOrEmpty(primaryDriverDate) ? primaryDriverDate : "Current";
                info.GpuCount = Math.Max(1, detectedCount);
                info.AdapterRegistryKeyPath = adapterKey;

                // Vendor classification
                if (hasNvidia)
                {
                    info.VendorType = GpuVendorType.Nvidia;
                    info.VendorName = "NVIDIA Corporation";
                }
                else if (hasAmd)
                {
                    info.VendorType = GpuVendorType.Amd;
                    info.VendorName = "Advanced Micro Devices (AMD)";
                }
                else if (hasIntel)
                {
                    info.VendorType = GpuVendorType.Intel;
                    info.VendorName = "Intel Corporation";
                }
                else
                {
                    info.VendorType = GpuVendorType.Unknown;
                    info.VendorName = "Standard VGA / Generic Display";
                }

                // Hybrid / Discrete detection
                if (detectedCount > 1 || (hasIntel && (hasNvidia || hasAmd)))
                {
                    info.IsHybridGraphics = true;
                    info.GpuType = "Hybrid (iGPU + dGPU)";
                }
                else if (hasIntel && !hasNvidia && !hasAmd)
                {
                    info.GpuType = "Integrated (iGPU)";
                }
                else
                {
                    info.GpuType = "Discrete (dGPU)";
                }

                // WDDM Version detection from GraphicsDrivers
                double wddm = 2.7;
                string wddmStr = "WDDM 2.7";
                try
                {
                    if (int.TryParse(info.WindowsBuild, out int bNum))
                    {
                        if (bNum >= 22621) { wddm = 3.1; wddmStr = "WDDM 3.1"; }
                        else if (bNum >= 22000) { wddm = 3.0; wddmStr = "WDDM 3.0"; }
                        else if (bNum >= 19041) { wddm = 2.7; wddmStr = "WDDM 2.7"; }
                    }
                }
                catch { }
                info.WddmNumeric = wddm;
                info.WddmVersion = wddmStr;

                // HAGS support evaluation: Windows 10 2004+ (Build 19041+) + WDDM 2.7+ + Modern GPU architecture
                info.SupportsHags = wddm >= 2.7 && (hasNvidia || hasAmd || (hasIntel && primaryName.Contains("Arc", StringComparison.OrdinalIgnoreCase)));

                _cachedHardware = info;
                return info;
            }
        }

        // ── 2. Scan and Build All GPU Registry Candidates ───────────────────
        public List<GpuRegistryItem> ScanAllGpuOptimizations(string profileName = "Normal", bool forceRescan = false)
        {
            var hw = DetectGpuHardware(forceRescan);
            var snapshots = LoadSnapshots();
            var items = new List<GpuRegistryItem>();

            // ── GROUP 1: UNIVERSAL GPU REGISTRY OPTIMIZATIONS ───────────────
            items.Add(EvaluateSystemResponsiveness(hw, snapshots, profileName));
            items.AddRange(EvaluateMmcssGamesTasks(hw, snapshots, profileName));
            items.Add(EvaluateHags(hw, snapshots, profileName));
            items.Add(EvaluateOverlayTestMode(hw, snapshots, profileName));
            items.Add(EvaluateHardwareFullscreen(hw, snapshots, profileName));
            items.Add(EvaluateGpuPowerPlanAttribute(hw, snapshots, profileName));

            // ── GROUP 2: NVIDIA-SPECIFIC GPU REGISTRY OPTIMIZATIONS ─────────
            items.Add(EvaluateNvidiaPowerMizerEnable(hw, snapshots, profileName));
            items.Add(EvaluateNvidiaPowerMizerLevel(hw, snapshots, profileName));
            items.Add(EvaluateNvidiaPerfLevelSrc(hw, snapshots, profileName));
            items.Add(EvaluateNvidiaCudaP2P(hw, snapshots, profileName));

            // ── GROUP 3: NVIDIA / GPU DIAGNOSTIC CONFIGURATION (DIAGNOSTIC ONLY) ──
            items.Add(EvaluateTdrDelay(hw, snapshots, profileName));
            items.Add(EvaluateTdrDdiDelay(hw, snapshots, profileName));
            items.Add(EvaluateTdrLimitCount(hw, snapshots, profileName));
            items.Add(EvaluateTdrLimitTime(hw, snapshots, profileName));

            // ── GROUP 4: AMD-SPECIFIC GPU REGISTRY OPTIMIZATIONS ────────────
            items.Add(EvaluateAmdEnableTelemetry(hw, snapshots, profileName));
            items.Add(EvaluateAmdEnableCrashReporting(hw, snapshots, profileName));
            items.Add(EvaluateAmdEnableCustomerFeedback(hw, snapshots, profileName));
            items.Add(EvaluateAmdEnableUlps(hw, snapshots, profileName));
            items.Add(EvaluateAmdDeepSleepDisable(hw, snapshots, profileName));
            items.Add(EvaluateAmdDisablePowerGating(hw, snapshots, profileName));
            items.Add(EvaluateAmdSurfaceFormatOptimization(hw, snapshots, profileName));
            items.Add(EvaluateAmdStartMinimized(hw, snapshots, profileName));

            return items;
        }

        // ──────────────────────────────────────────────────────────────────
        // INDIVIDUAL CANDIDATE EVALUATORS
        // ──────────────────────────────────────────────────────────────────

        // 1. SystemResponsiveness (Multimedia Profile)
        private GpuRegistryItem EvaluateSystemResponsiveness(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string regPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
            const string valName = "SystemResponsiveness";
            const string id = "gpu.universal.system_responsiveness";

            object? raw = ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", valName);
            uint curVal = raw is int i ? (uint)i : raw is uint u ? u : 0x14; // Default is 20% (0x14)
            uint targetVal = (profile.Equals("Debloat", StringComparison.OrdinalIgnoreCase) || profile.Equals("BiosSafe", StringComparison.OrdinalIgnoreCase)) ? 0x0Au : 0x00u; // 0% reserved for background, giving 100% to foreground GPU/Multimedia

            bool isOptimal = raw != null && curVal == targetVal;
            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "SYSTEM RESPONSIVENESS",
                Category = GpuRegistryCategory.Universal,
                SubCategory = "Multimedia Scheduling",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Configures the percentage of CPU/GPU scheduling time Windows reserves for background tasks vs foreground gaming/multimedia.",
                Risk = GpuOptimizationRisk.Safe,
                RequiresRestart = true,
                HardwareRequirement = "All Windows Systems",
                DriverRequirement = "Any Graphics Driver",
                WhyApplicable = "Windows by default reserves 20% execution cycles for background tasks. Setting to 0% reserves 100% for active applications.",
                WhyRecommended = "Eliminates micro-stutters during heavy gaming, 3D rendering, and video processing.",
                CurrentValueDisplay = raw != null ? $"{curVal}% (0x{curVal:X2})" : "20% (Windows Default)",
                CurrentValueRaw = raw,
                TargetValueDisplay = $"{targetVal}% (0x{targetVal:X2})",
                TargetValueRaw = (int)targetVal,
                Status = isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended,
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 2. MMCSS Games Tasks (GPU Priority, Priority, Scheduling Category, SFIO Priority, Clock Rate, Affinity)
        private List<GpuRegistryItem> EvaluateMmcssGamesTasks(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            var list = new List<GpuRegistryItem>();
            const string regPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
            const string subKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";

            // 2a. GPU Priority (DWORD: Default 8)
            {
                const string id = "gpu.mmcss.gpu_priority";
                object? raw = ReadRegistryValue(Registry.LocalMachine, subKey, "GPU Priority");
                int cur = raw is int i ? i : (raw is uint u ? (int)u : 8);
                int target = 8;
                bool isOptimal = raw != null && cur == target;

                list.Add(new GpuRegistryItem
                {
                    Id = id,
                    Name = "GPU Priority",
                    DisplayName = "MMCSS GAMES GPU PRIORITY",
                    Category = GpuRegistryCategory.Universal,
                    SubCategory = "MMCSS Task Priority",
                    RegistryPath = regPath,
                    ValueName = "GPU Priority",
                    ValueType = RegistryValueKind.DWord,
                    Description = "Sets the GPU task scheduling priority for applications recognized as games or high-throughput DirectX renderers.",
                    Risk = GpuOptimizationRisk.Safe,
                    RequiresRestart = true,
                    HardwareRequirement = "DirectX 11 / 12 GPU",
                    DriverRequirement = "WDDM 2.0+",
                    WhyApplicable = "Directs the Windows Multimedia Class Scheduler Service to grant maximum GPU preemption to foreground games.",
                    WhyRecommended = "Prevents background GPU workloads (DWM compositing, hardware web renderers) from interrupting frame pacing.",
                    CurrentValueDisplay = raw != null ? $"{cur} (0x{cur:X2})" : "8 (Default)",
                    CurrentValueRaw = raw,
                    TargetValueDisplay = $"{target} (0x{target:X2})",
                    TargetValueRaw = target,
                    Status = isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended,
                    HasBackup = snapshots.ContainsKey(id)
                });
            }

            // 2b. Priority (DWORD: Default 2 -> Target 6)
            {
                const string id = "gpu.mmcss.priority";
                object? raw = ReadRegistryValue(Registry.LocalMachine, subKey, "Priority");
                int cur = raw is int i ? i : (raw is uint u ? (int)u : 2);
                int target = 6;
                bool isOptimal = raw != null && cur == target;

                list.Add(new GpuRegistryItem
                {
                    Id = id,
                    Name = "Priority",
                    DisplayName = "MMCSS GAMES CPU TASK PRIORITY",
                    Category = GpuRegistryCategory.Universal,
                    SubCategory = "MMCSS Task Priority",
                    RegistryPath = regPath,
                    ValueName = "Priority",
                    ValueType = RegistryValueKind.DWord,
                    Description = "Sets the task execution priority for game worker threads within the Windows scheduler.",
                    Risk = GpuOptimizationRisk.Safe,
                    RequiresRestart = true,
                    HardwareRequirement = "Multi-Core CPU & GPU",
                    DriverRequirement = "Standard Windows Scheduler",
                    WhyApplicable = "Increases thread scheduling priority from normal (2) to high (6) when a game is running.",
                    WhyRecommended = "Reduces frame latency spikes caused by background worker thread contention.",
                    CurrentValueDisplay = raw != null ? $"{cur} (0x{cur:X2})" : "2 (Default)",
                    CurrentValueRaw = raw,
                    TargetValueDisplay = $"{target} (0x{target:X2})",
                    TargetValueRaw = target,
                    Status = isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended,
                    HasBackup = snapshots.ContainsKey(id)
                });
            }

            // 2c. Scheduling Category (REG_SZ: Target "High")
            {
                const string id = "gpu.mmcss.scheduling_category";
                object? raw = ReadRegistryValue(Registry.LocalMachine, subKey, "Scheduling Category");
                string cur = raw?.ToString() ?? "Medium";
                string target = "High";
                bool isOptimal = string.Equals(cur, target, StringComparison.OrdinalIgnoreCase);

                list.Add(new GpuRegistryItem
                {
                    Id = id,
                    Name = "Scheduling Category",
                    DisplayName = "SCHEDULING CATEGORY",
                    Category = GpuRegistryCategory.Universal,
                    SubCategory = "MMCSS Task Priority",
                    RegistryPath = regPath,
                    ValueName = "Scheduling Category",
                    ValueType = RegistryValueKind.String,
                    Description = "Configures the scheduling category bucket for gaming and graphics streaming tasks.",
                    Risk = GpuOptimizationRisk.Safe,
                    RequiresRestart = true,
                    HardwareRequirement = "All Windows Systems",
                    DriverRequirement = "Standard Windows Scheduler",
                    WhyApplicable = "Ensures MMCSS groups game processes into the High priority execution queue.",
                    WhyRecommended = "Guarantees predictable scheduler time slices for physics, render, and input threads.",
                    CurrentValueDisplay = raw != null ? cur : "Medium (Default)",
                    CurrentValueRaw = raw,
                    TargetValueDisplay = target,
                    TargetValueRaw = target,
                    Status = isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended,
                    HasBackup = snapshots.ContainsKey(id)
                });
            }

            // 2d. SFIO Priority (REG_SZ: Target "High")
            {
                const string id = "gpu.mmcss.sfio_priority";
                object? raw = ReadRegistryValue(Registry.LocalMachine, subKey, "SFIO Priority");
                string cur = raw?.ToString() ?? "Normal";
                string target = "High";
                bool isOptimal = string.Equals(cur, target, StringComparison.OrdinalIgnoreCase);

                list.Add(new GpuRegistryItem
                {
                    Id = id,
                    Name = "SFIO Priority",
                    DisplayName = "SFIO DISK / VRAM I/O PRIORITY",
                    Category = GpuRegistryCategory.Universal,
                    SubCategory = "MMCSS Task Priority",
                    RegistryPath = regPath,
                    ValueName = "SFIO Priority",
                    ValueType = RegistryValueKind.String,
                    Description = "Sets the Sequential File I/O priority for game texture streaming and shader cache load requests.",
                    Risk = GpuOptimizationRisk.Safe,
                    RequiresRestart = true,
                    HardwareRequirement = "NVMe / SSD / High-Speed Storage",
                    DriverRequirement = "Standard Storage Subsystem",
                    WhyApplicable = "Prioritizes direct asset decompression and texture streaming from disk to GPU memory.",
                    WhyRecommended = "Reduces hitching when loading new world cells or high-resolution textures during gameplay.",
                    CurrentValueDisplay = raw != null ? cur : "Normal (Default)",
                    CurrentValueRaw = raw,
                    TargetValueDisplay = target,
                    TargetValueRaw = target,
                    Status = isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended,
                    HasBackup = snapshots.ContainsKey(id)
                });
            }

            return list;
        }

        // 3. Hardware-Accelerated GPU Scheduling (HAGS)
        private GpuRegistryItem EvaluateHags(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string regPath = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
            const string valName = "HwSchMode";
            const string id = "gpu.universal.hags";

            object? raw = ReadRegistryValue(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", valName);
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 1);
            int target = hw.SupportsHags ? 2 : 1; // 2 = Enabled, 1 = Disabled

            bool isSupported = hw.SupportsHags;
            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "HARDWARE-ACCELERATED GPU SCHEDULING",
                Category = GpuRegistryCategory.Universal,
                SubCategory = "GPU Scheduling Architecture",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Offloads frame scheduling tasks from the CPU directly onto a dedicated GPU scheduling processor.",
                Risk = isSupported ? GpuOptimizationRisk.Safe : GpuOptimizationRisk.Unsupported,
                RequiresRestart = true,
                HardwareRequirement = "NVIDIA GTX 10xx+ / RTX 20/30/40xx+, AMD RX 5000/6000/7000+, Intel Arc",
                DriverRequirement = $"{hw.WddmVersion} (WDDM 2.7+ Required)",
                WhyApplicable = isSupported 
                    ? $"Supported on {hw.Name} ({hw.WddmVersion}, Windows Build {hw.WindowsBuild})."
                    : $"Unsupported: Requires WDDM 2.7+ driver and compatible GPU architecture (detected: {hw.WddmVersion}).",
                WhyRecommended = "Reduces CPU driver overhead and input latency by allowing the GPU to manage its own video memory schedule.",
                CurrentValueDisplay = raw != null ? (cur == 2 ? "Enabled (0x02)" : "Disabled (0x01)") : "Disabled (Windows Default)",
                CurrentValueRaw = raw,
                TargetValueDisplay = isSupported ? "Enabled (0x02)" : "Not Supported on current GPU",
                TargetValueRaw = target,
                Status = !isSupported ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 4. DWM OverlayTestMode (MPO Disabling)
        private GpuRegistryItem EvaluateOverlayTestMode(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string regPath = @"HKLM\SOFTWARE\Microsoft\Windows\Dwm";
            const string valName = "OverlayTestMode";
            const string id = "gpu.universal.overlay_test_mode";

            object? raw = ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", valName);
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 0);
            int target = 5; // 5 = Disables Multi-Plane Overlay (MPO)

            bool isOptimal = raw != null && cur == target;
            bool isAllowable = profile.Equals("Ultimate", StringComparison.OrdinalIgnoreCase) || profile.Equals("MaxPerformance", StringComparison.OrdinalIgnoreCase) || profile.Equals("Pro", StringComparison.OrdinalIgnoreCase);

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "MPO (MULTI-PLANE OVERLAY) DISPLAY FIX",
                Category = GpuRegistryCategory.Universal,
                SubCategory = "Desktop Window Manager",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Disables Multi-Plane Overlays (MPO) in DWM to resolve stuttering, flickering, and black screens on multi-monitor setups.",
                Risk = GpuOptimizationRisk.Experimental,
                RequiresRestart = true,
                HardwareRequirement = "Discrete GPU (Multi-Monitor / High Refresh)",
                DriverRequirement = "WDDM 2.0+",
                WhyApplicable = "MPO is a Windows display hardware feature that can cause micro-stuttering with Chromium and Discord hardware acceleration.",
                WhyRecommended = "Resolves driver-level DWM stutter and video playback frame drops on modern GPU drivers.",
                CurrentValueDisplay = raw != null ? $"0x{cur:X2} (MPO {(cur == 5 ? "Disabled" : "Active")})" : "0x00 (MPO Active - Windows Default)",
                CurrentValueRaw = raw,
                TargetValueDisplay = "0x05 (Disable MPO)",
                TargetValueRaw = target,
                Status = !isAllowable ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 5. DWM EnableHardwareFullscreen
        private GpuRegistryItem EvaluateHardwareFullscreen(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string regPath = @"HKLM\SOFTWARE\Microsoft\Windows\Dwm";
            const string valName = "EnableHardwareFullscreen";
            const string id = "gpu.universal.hardware_fullscreen";

            object? raw = ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", valName);
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 0);
            int target = 1;

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "HARDWARE FULLSCREEN EXCLUSIVE PROMOTION",
                Category = GpuRegistryCategory.Universal,
                SubCategory = "Desktop Window Manager",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Enables hardware-level fullscreen promotion for borderless and fullscreen DirectX swapchains.",
                Risk = GpuOptimizationRisk.LowRisk,
                RequiresRestart = true,
                HardwareRequirement = "DirectX 11 / 12 GPU",
                DriverRequirement = "Standard Display Driver",
                WhyApplicable = "Allows DirectX flip model applications to bypass DWM composition overhead.",
                WhyRecommended = "Delivers true exclusive fullscreen latency while maintaining fast Alt-Tab switching.",
                CurrentValueDisplay = raw != null ? $"{cur} (0x{cur:X2})" : "0 (Standard)",
                CurrentValueRaw = raw,
                TargetValueDisplay = $"{target} (0x{target:X2})",
                TargetValueRaw = target,
                Status = isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended,
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 6. GPU Power Setting Attribute Visibility
        private GpuRegistryItem EvaluateGpuPowerPlanAttribute(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string regPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\36333b52-d141-42ff-b50f-5405c32b0d36";
            const string valName = "Attributes";
            const string id = "gpu.universal.power_attribute";

            object? raw = ReadRegistryValue(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\36333b52-d141-42ff-b50f-5405c32b0d36", valName);
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 1);
            int target = 2; // 2 = Visible in Windows Power Options GUI

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "GPU POWER MANAGEMENT VISIBILITY IN POWER OPTIONS",
                Category = GpuRegistryCategory.Universal,
                SubCategory = "Power Configuration",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Unhides the Graphics Power Management sub-menu inside the active Windows Power Plan control panel.",
                Risk = GpuOptimizationRisk.Safe,
                RequiresRestart = false,
                HardwareRequirement = "All Windows Systems",
                DriverRequirement = "Standard Power Subsystem",
                WhyApplicable = "Windows conceals advanced GPU graphics power policy attributes by default (Attributes = 1).",
                WhyRecommended = "Exposes explicit GPU clock/power management controls inside Windows Power Options.",
                CurrentValueDisplay = raw != null ? (cur == 2 ? "Visible (0x02)" : "Hidden (0x01)") : "Hidden (Default)",
                CurrentValueRaw = raw,
                TargetValueDisplay = "Visible (0x02)",
                TargetValueRaw = target,
                Status = isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended,
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // ── NVIDIA SPECIFIC EVALUATORS ─────────────────────────────────────

        // 7. NVIDIA PowerMizerEnable
        private GpuRegistryItem EvaluateNvidiaPowerMizerEnable(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string valName = "PowerMizerEnable";
            const string id = "gpu.nvidia.powermizer_enable";
            bool isNvidia = hw.VendorType == GpuVendorType.Nvidia;

            string regPath = isNvidia ? hw.AdapterRegistryKeyPath : @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";
            string subKey = regPath.Replace(@"HKLM\", "");

            object? raw = isNvidia ? ReadRegistryValue(Registry.LocalMachine, subKey, valName) : null;
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 1);
            int target = 0; // 0 = Disable aggressive PowerMizer throttling

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "NVIDIA POWERMIZER LOW-LATENCY CLOCKS",
                Category = GpuRegistryCategory.NvidiaSpecific,
                SubCategory = "Power & Clock Management",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Disables aggressive dynamic PowerMizer clock step-down throttling when operating under active graphics load.",
                Risk = hw.IsLaptop ? GpuOptimizationRisk.MediumRisk : GpuOptimizationRisk.LowRisk,
                RequiresRestart = true,
                HardwareRequirement = "NVIDIA GeForce / RTX / GTX / Quadro",
                DriverRequirement = "Official NVIDIA Game Ready / Studio Driver",
                WhyApplicable = isNvidia ? $"NVIDIA GPU detected: {hw.Name} ({hw.DriverVersion})." : "Not Applicable: NVIDIA GPU not detected on this system.",
                WhyRecommended = "Eliminates clock fluctuation latency and stabilizes minimum 1% low frame rates in gaming and 3D rendering.",
                CurrentValueDisplay = isNvidia ? (raw != null ? $"{cur} (0x{cur:X2})" : "1 (Standard Dynamic Clocks)") : "NOT APPLICABLE",
                CurrentValueRaw = raw,
                TargetValueDisplay = isNvidia ? "0 (Low-Latency Static Clocks)" : "NOT APPLICABLE",
                TargetValueRaw = target,
                Status = !isNvidia ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 8. NVIDIA PowerMizerLevel
        private GpuRegistryItem EvaluateNvidiaPowerMizerLevel(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string valName = "PowerMizerLevel";
            const string id = "gpu.nvidia.powermizer_level";
            bool isNvidia = hw.VendorType == GpuVendorType.Nvidia;

            string regPath = isNvidia ? hw.AdapterRegistryKeyPath : @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";
            string subKey = regPath.Replace(@"HKLM\", "");

            object? raw = isNvidia ? ReadRegistryValue(Registry.LocalMachine, subKey, valName) : null;
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 0);
            int target = 1; // 1 = Maximum Performance Level

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "NVIDIA POWERMIZER LEVEL (MAX PERFORMANCE)",
                Category = GpuRegistryCategory.NvidiaSpecific,
                SubCategory = "Power & Clock Management",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Forces the NVIDIA hardware driver to initialize the GPU core and memory at maximum performance P-States.",
                Risk = GpuOptimizationRisk.LowRisk,
                RequiresRestart = true,
                HardwareRequirement = "NVIDIA GeForce / RTX / GTX",
                DriverRequirement = "Official NVIDIA Display Driver",
                WhyApplicable = isNvidia ? $"NVIDIA GPU detected: {hw.Name}." : "Not Applicable: No NVIDIA GPU detected.",
                WhyRecommended = "Locks core clock frequency to high-performance operational envelopes.",
                CurrentValueDisplay = isNvidia ? (raw != null ? $"{cur} (0x{cur:X2})" : "0 (Dynamic)") : "NOT APPLICABLE",
                CurrentValueRaw = raw,
                TargetValueDisplay = isNvidia ? "1 (Maximum Performance)" : "NOT APPLICABLE",
                TargetValueRaw = target,
                Status = !isNvidia ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 9. NVIDIA PerfLevelSrc
        private GpuRegistryItem EvaluateNvidiaPerfLevelSrc(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string valName = "PerfLevelSrc";
            const string id = "gpu.nvidia.perf_level_src";
            bool isNvidia = hw.VendorType == GpuVendorType.Nvidia;

            string regPath = isNvidia ? hw.AdapterRegistryKeyPath : @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";
            string subKey = regPath.Replace(@"HKLM\", "");

            object? raw = isNvidia ? ReadRegistryValue(Registry.LocalMachine, subKey, valName) : null;
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 0);
            int target = 0x2222; // 0x2222 = Static High Performance Source

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "NVIDIA PERFORMANCE LEVEL SOURCE",
                Category = GpuRegistryCategory.NvidiaSpecific,
                SubCategory = "Power & Clock Management",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Instructs the NVIDIA graphics driver to source performance levels from the maximum performance profile.",
                Risk = GpuOptimizationRisk.LowRisk,
                RequiresRestart = true,
                HardwareRequirement = "NVIDIA Desktop / High-TDP Discrete GPU",
                DriverRequirement = "Official NVIDIA Display Driver",
                WhyApplicable = isNvidia ? $"NVIDIA GPU detected: {hw.Name}." : "Not Applicable: No NVIDIA GPU detected.",
                WhyRecommended = "Eliminates P-State switching micro-delays between 2D desktop and 3D rendering modes.",
                CurrentValueDisplay = isNvidia ? (raw != null ? $"0x{cur:X4}" : "0x3333 (Dynamic Source)") : "NOT APPLICABLE",
                CurrentValueRaw = raw,
                TargetValueDisplay = isNvidia ? "0x2222 (Static Performance)" : "NOT APPLICABLE",
                TargetValueRaw = target,
                Status = !isNvidia ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 10. NVIDIA CUDA ForceP2P (Multi-GPU Peer-to-Peer)
        private GpuRegistryItem EvaluateNvidiaCudaP2P(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string valName = "ForceP2P";
            const string id = "gpu.nvidia.force_p2p";
            bool isNvidiaMultiGpu = hw.VendorType == GpuVendorType.Nvidia && hw.GpuCount > 1;

            string regPath = hw.AdapterRegistryKeyPath;
            string subKey = regPath.Replace(@"HKLM\", "");

            object? raw = isNvidiaMultiGpu ? ReadRegistryValue(Registry.LocalMachine, subKey, valName) : null;
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 0);
            int target = 1;

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "NVIDIA CUDA DIRECT PEER-TO-PEER (P2P)",
                Category = GpuRegistryCategory.NvidiaSpecific,
                SubCategory = "CUDA & Compute Acceleration",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Forces high-speed direct peer-to-peer PCIe memory transfers between multiple NVIDIA GPUs without CPU staging.",
                Risk = GpuOptimizationRisk.LowRisk,
                RequiresRestart = true,
                HardwareRequirement = "Multi-GPU NVIDIA Configuration (SLI / NVLink / Dual RTX)",
                DriverRequirement = "NVIDIA CUDA Driver",
                WhyApplicable = isNvidiaMultiGpu ? $"Multi-GPU NVIDIA configuration detected ({hw.GpuCount} GPUs)." : "Not Applicable: Single-GPU or non-NVIDIA system detected.",
                WhyRecommended = "Dramatically accelerates CUDA inter-GPU communication bandwidth and reduces latency in rendering and AI workloads.",
                CurrentValueDisplay = isNvidiaMultiGpu ? (raw != null ? $"{cur} (0x{cur:X2})" : "0 (Disabled)") : "NOT APPLICABLE (Single GPU)",
                CurrentValueRaw = raw,
                TargetValueDisplay = isNvidiaMultiGpu ? "1 (Enabled)" : "NOT APPLICABLE",
                TargetValueRaw = target,
                Status = !isNvidiaMultiGpu ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // ── NVIDIA / GPU DIAGNOSTIC CONFIGURATION (TDR ITEMS) ──────────────

        // 11. TdrDelay (DIAGNOSTIC ONLY)
        private GpuRegistryItem EvaluateTdrDelay(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string regPath = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
            const string valName = "TdrDelay";
            const string id = "gpu.diagnostic.tdr_delay";

            object? raw = ReadRegistryValue(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", valName);
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 2); // Default is 2 seconds
            int target = 8; // 8 seconds

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "GPU TDR TIMEOUT DELAY (DIAGNOSTIC)",
                Category = GpuRegistryCategory.Diagnostic,
                SubCategory = "Diagnostic Recovery",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Extends the Timeout Detection and Recovery (TDR) delay threshold from 2 seconds to 8 seconds for intensive compute tasks.",
                Risk = GpuOptimizationRisk.DiagnosticOnly,
                RequiresRestart = true,
                HardwareRequirement = "All Windows GPUs (Diagnostic Workloads)",
                DriverRequirement = "Standard Display Subsystem",
                WhyApplicable = "Diagnostic configuration for 3D modeling, Blender rendering, and machine learning models where long shader execution is normal.",
                WhyRecommended = "Prevents Windows from falsely resetting the GPU driver when processing extremely complex geometry or deep neural networks.",
                CurrentValueDisplay = raw != null ? $"{cur} seconds (0x{cur:X2})" : "2 seconds (Windows Default)",
                CurrentValueRaw = raw,
                TargetValueDisplay = $"{target} seconds (0x{target:X2})",
                TargetValueRaw = target,
                Status = isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended,
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 12. TdrDdiDelay (DIAGNOSTIC ONLY)
        private GpuRegistryItem EvaluateTdrDdiDelay(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string regPath = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
            const string valName = "TdrDdiDelay";
            const string id = "gpu.diagnostic.tdr_ddi_delay";

            object? raw = ReadRegistryValue(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", valName);
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 5);
            int target = 8;

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "GPU DDI DRIVER RECOVERY TIMEOUT (DIAGNOSTIC)",
                Category = GpuRegistryCategory.Diagnostic,
                SubCategory = "Diagnostic Recovery",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Sets the maximum time Windows waits for a device driver interface (DDI) kernel call before initiating recovery.",
                Risk = GpuOptimizationRisk.DiagnosticOnly,
                RequiresRestart = true,
                HardwareRequirement = "All Windows GPUs",
                DriverRequirement = "Standard Display Subsystem",
                WhyApplicable = "Diagnostic setting to prevent kernel timeout crashes during heavy raytracing initialization.",
                WhyRecommended = "Stabilizes application launch and shader pre-compilation on complex DirectX 12 titles.",
                CurrentValueDisplay = raw != null ? $"{cur} seconds (0x{cur:X2})" : "5 seconds (Default)",
                CurrentValueRaw = raw,
                TargetValueDisplay = $"{target} seconds (0x{target:X2})",
                TargetValueRaw = target,
                Status = isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended,
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 13. TdrLimitCount (DIAGNOSTIC ONLY)
        private GpuRegistryItem EvaluateTdrLimitCount(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string regPath = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
            const string valName = "TdrLimitCount";
            const string id = "gpu.diagnostic.tdr_limit_count";

            object? raw = ReadRegistryValue(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", valName);
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 5);
            int target = 10;

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "TDR RESET LIMIT COUNT (DIAGNOSTIC)",
                Category = GpuRegistryCategory.Diagnostic,
                SubCategory = "Diagnostic Recovery",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Defines the number of TDR recovery attempts Windows allows within the recovery time window before generating a bugcheck BSOD.",
                Risk = GpuOptimizationRisk.DiagnosticOnly,
                RequiresRestart = true,
                HardwareRequirement = "All Windows GPUs",
                DriverRequirement = "Standard Display Subsystem",
                WhyApplicable = "Diagnostic configuration for overclocking stability and development testing.",
                WhyRecommended = "Allows driver recovery without forcing an immediate blue-screen restart during intermittent GPU resets.",
                CurrentValueDisplay = raw != null ? $"{cur} attempts" : "5 attempts (Default)",
                CurrentValueRaw = raw,
                TargetValueDisplay = $"{target} attempts",
                TargetValueRaw = target,
                Status = isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended,
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 14. TdrLimitTime (DIAGNOSTIC ONLY)
        private GpuRegistryItem EvaluateTdrLimitTime(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string regPath = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
            const string valName = "TdrLimitTime";
            const string id = "gpu.diagnostic.tdr_limit_time";

            object? raw = ReadRegistryValue(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", valName);
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 60);
            int target = 60;

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "TDR LIMIT TIME WINDOW (DIAGNOSTIC)",
                Category = GpuRegistryCategory.Diagnostic,
                SubCategory = "Diagnostic Recovery",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Specifies the time window in seconds during which TdrLimitCount reset attempts are tracked.",
                Risk = GpuOptimizationRisk.DiagnosticOnly,
                RequiresRestart = true,
                HardwareRequirement = "All Windows GPUs",
                DriverRequirement = "Standard Display Subsystem",
                WhyApplicable = "Standard diagnostic baseline configuration.",
                WhyRecommended = "Maintains 60s tracking window for GPU hardware recovery.",
                CurrentValueDisplay = raw != null ? $"{cur} seconds" : "60 seconds (Default)",
                CurrentValueRaw = raw,
                TargetValueDisplay = $"{target} seconds",
                TargetValueRaw = target,
                Status = isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended,
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // ── AMD SPECIFIC EVALUATORS ────────────────────────────────────────

        // 15. AMD EnableTelemetry
        private GpuRegistryItem EvaluateAmdEnableTelemetry(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string valName = "EnableTelemetry";
            const string id = "gpu.amd.enable_telemetry";
            bool isAmd = hw.VendorType == GpuVendorType.Amd;

            const string regPath = @"HKLM\SOFTWARE\AMD\CN";
            object? raw = isAmd ? ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\AMD\CN", valName) : null;
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 1);
            int target = 0;

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "AMD SOFTWARE BACKGROUND TELEMETRY",
                Category = GpuRegistryCategory.AmdSpecific,
                SubCategory = "Privacy & Background Services",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Disables background diagnostic data collection and tracking threads in AMD Radeon Software.",
                Risk = GpuOptimizationRisk.Safe,
                RequiresRestart = false,
                HardwareRequirement = "AMD Radeon GPU & Software",
                DriverRequirement = "AMD Adrenalin Driver",
                WhyApplicable = isAmd ? $"AMD GPU detected: {hw.Name}." : "Not Applicable: AMD GPU not detected on this system.",
                WhyRecommended = "Eliminates background telemetry network polling and reduces CPU scheduler overhead.",
                CurrentValueDisplay = isAmd ? (raw != null ? (cur == 0 ? "Disabled (0x00)" : "Enabled (0x01)") : "Enabled (Default)") : "NOT APPLICABLE",
                CurrentValueRaw = raw,
                TargetValueDisplay = isAmd ? "Disabled (0x00)" : "NOT APPLICABLE",
                TargetValueRaw = target,
                Status = !isAmd ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 16. AMD EnableCrashReporting
        private GpuRegistryItem EvaluateAmdEnableCrashReporting(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string valName = "EnableCrashReporting";
            const string id = "gpu.amd.enable_crash_reporting";
            bool isAmd = hw.VendorType == GpuVendorType.Amd;

            const string regPath = @"HKLM\SOFTWARE\AMD\CN";
            object? raw = isAmd ? ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\AMD\CN", valName) : null;
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 1);
            int target = 0;

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "AMD CRASH REPORTING SERVICE",
                Category = GpuRegistryCategory.AmdSpecific,
                SubCategory = "Privacy & Background Services",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Disables automatic memory dump transmission to AMD servers upon application termination.",
                Risk = GpuOptimizationRisk.Safe,
                RequiresRestart = false,
                HardwareRequirement = "AMD Radeon GPU",
                DriverRequirement = "AMD Adrenalin Driver",
                WhyApplicable = isAmd ? $"AMD GPU detected: {hw.Name}." : "Not Applicable: AMD GPU not detected.",
                WhyRecommended = "Prevents disk I/O pauses caused by background crash dump serialization.",
                CurrentValueDisplay = isAmd ? (raw != null ? (cur == 0 ? "Disabled (0x00)" : "Enabled (0x01)") : "Enabled (Default)") : "NOT APPLICABLE",
                CurrentValueRaw = raw,
                TargetValueDisplay = isAmd ? "Disabled (0x00)" : "NOT APPLICABLE",
                TargetValueRaw = target,
                Status = !isAmd ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 17. AMD EnableCustomerFeedback
        private GpuRegistryItem EvaluateAmdEnableCustomerFeedback(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string valName = "EnableCustomerFeedback";
            const string id = "gpu.amd.enable_customer_feedback";
            bool isAmd = hw.VendorType == GpuVendorType.Amd;

            const string regPath = @"HKLM\SOFTWARE\AMD\CN";
            object? raw = isAmd ? ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\AMD\CN", valName) : null;
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 1);
            int target = 0;

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "AMD USER EXPERIENCE FEEDBACK",
                Category = GpuRegistryCategory.AmdSpecific,
                SubCategory = "Privacy & Background Services",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Disables AMD User Experience Program prompt popups and telemetry polling.",
                Risk = GpuOptimizationRisk.Safe,
                RequiresRestart = false,
                HardwareRequirement = "AMD Radeon GPU",
                DriverRequirement = "AMD Adrenalin Driver",
                WhyApplicable = isAmd ? $"AMD GPU detected: {hw.Name}." : "Not Applicable: AMD GPU not detected.",
                WhyRecommended = "Ensures clean background execution with zero telemetry interruptions.",
                CurrentValueDisplay = isAmd ? (raw != null ? (cur == 0 ? "Disabled (0x00)" : "Enabled (0x01)") : "Enabled (Default)") : "NOT APPLICABLE",
                CurrentValueRaw = raw,
                TargetValueDisplay = isAmd ? "Disabled (0x00)" : "NOT APPLICABLE",
                TargetValueRaw = target,
                Status = !isAmd ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 18. AMD EnableULPS (Ultra Low Power State)
        private GpuRegistryItem EvaluateAmdEnableUlps(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string valName = "EnableUlps";
            const string id = "gpu.amd.enable_ulps";
            bool isAmd = hw.VendorType == GpuVendorType.Amd;

            string regPath = isAmd ? hw.AdapterRegistryKeyPath : @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";
            string subKey = regPath.Replace(@"HKLM\", "");

            object? raw = isAmd ? ReadRegistryValue(Registry.LocalMachine, subKey, valName) : null;
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 1);
            int target = 0; // 0 = Disable ULPS

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "AMD ULPS (ULTRA LOW POWER STATE) DISABLE",
                Category = GpuRegistryCategory.AmdSpecific,
                SubCategory = "Power & Clock Management",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Disables Ultra Low Power State (ULPS) to eliminate wake-up micro-stutters and black-screen wake freezes.",
                Risk = hw.IsLaptop ? GpuOptimizationRisk.MediumRisk : GpuOptimizationRisk.LowRisk,
                RequiresRestart = true,
                HardwareRequirement = "AMD Radeon Desktop / High-Performance GPU",
                DriverRequirement = "AMD Display Driver",
                WhyApplicable = isAmd ? $"AMD GPU detected: {hw.Name}." : "Not Applicable: AMD GPU not detected.",
                WhyRecommended = "Fixes stuttering on multi-monitor setups and eliminates PCIe bus wake-from-sleep latency.",
                CurrentValueDisplay = isAmd ? (raw != null ? (cur == 0 ? "Disabled (0x00)" : "Enabled (0x01)") : "Enabled (Default)") : "NOT APPLICABLE",
                CurrentValueRaw = raw,
                TargetValueDisplay = isAmd ? "Disabled (0x00)" : "NOT APPLICABLE",
                TargetValueRaw = target,
                Status = !isAmd ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 19. AMD PP_Sclk_DeepSleep_Disable
        private GpuRegistryItem EvaluateAmdDeepSleepDisable(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string valName = "PP_Sclk_DeepSleep_Disable";
            const string id = "gpu.amd.deepsleep_disable";
            bool isAmd = hw.VendorType == GpuVendorType.Amd;

            string regPath = isAmd ? hw.AdapterRegistryKeyPath : @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";
            string subKey = regPath.Replace(@"HKLM\", "");

            object? raw = isAmd ? ReadRegistryValue(Registry.LocalMachine, subKey, valName) : null;
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 0);
            int target = 1; // 1 = Disable Deep Sleep on Core Clocks

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "AMD CORE CLOCK DEEP SLEEP DISABLE",
                Category = GpuRegistryCategory.AmdSpecific,
                SubCategory = "Power & Clock Management",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Prevents the AMD PowerPlay engine from placing GPU engine core clocks into deep sleep latency states.",
                Risk = GpuOptimizationRisk.MediumRisk,
                RequiresRestart = true,
                HardwareRequirement = "AMD Radeon GPU (Desktop Performance)",
                DriverRequirement = "AMD Display Driver",
                WhyApplicable = isAmd ? $"AMD GPU detected: {hw.Name}." : "Not Applicable: AMD GPU not detected.",
                WhyRecommended = "Eliminates low-framerate stutter caused by aggressive clock ramp-down between frames.",
                CurrentValueDisplay = isAmd ? (raw != null ? $"{cur} (0x{cur:X2})" : "0 (Deep Sleep Active)") : "NOT APPLICABLE",
                CurrentValueRaw = raw,
                TargetValueDisplay = isAmd ? "1 (Deep Sleep Disabled)" : "NOT APPLICABLE",
                TargetValueRaw = target,
                Status = !isAmd ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 20. AMD DisablePowerGating
        private GpuRegistryItem EvaluateAmdDisablePowerGating(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string valName = "DisablePowerGating";
            const string id = "gpu.amd.disable_power_gating";
            bool isAmd = hw.VendorType == GpuVendorType.Amd;

            string regPath = isAmd ? hw.AdapterRegistryKeyPath : @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";
            string subKey = regPath.Replace(@"HKLM\", "");

            object? raw = isAmd ? ReadRegistryValue(Registry.LocalMachine, subKey, valName) : null;
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 0);
            int target = 1;

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "AMD POWER GATING LATENCY REDUCTION",
                Category = GpuRegistryCategory.AmdSpecific,
                SubCategory = "Power & Clock Management",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Reduces internal power gating transition delays across compute units on supported Radeon architectures.",
                Risk = GpuOptimizationRisk.MediumRisk,
                RequiresRestart = true,
                HardwareRequirement = "AMD Radeon RX Architecture",
                DriverRequirement = "AMD Display Driver",
                WhyApplicable = isAmd ? $"AMD GPU detected: {hw.Name}." : "Not Applicable: AMD GPU not detected.",
                WhyRecommended = "Decreases shader dispatch latency when transitioning from light to heavy compute workloads.",
                CurrentValueDisplay = isAmd ? (raw != null ? $"{cur} (0x{cur:X2})" : "0 (Power Gating Enabled)") : "NOT APPLICABLE",
                CurrentValueRaw = raw,
                TargetValueDisplay = isAmd ? "1 (Power Gating Disabled)" : "NOT APPLICABLE",
                TargetValueRaw = target,
                Status = !isAmd ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 21. AMD SurfaceFormatOptimization
        private GpuRegistryItem EvaluateAmdSurfaceFormatOptimization(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string valName = "SurfaceFormatOptimization";
            const string id = "gpu.amd.surface_format_opt";
            bool isAmd = hw.VendorType == GpuVendorType.Amd;

            string regPath = isAmd ? hw.AdapterRegistryKeyPath : @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";
            string subKey = regPath.Replace(@"HKLM\", "");

            object? raw = isAmd ? ReadRegistryValue(Registry.LocalMachine, subKey, valName) : null;
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 0);
            int target = 1;

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "AMD SURFACE FORMAT OPTIMIZATION",
                Category = GpuRegistryCategory.AmdSpecific,
                SubCategory = "Shader & Texture Pipeline",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Enables driver-level surface format substitution to boost texture throughput in modern 3D titles.",
                Risk = GpuOptimizationRisk.LowRisk,
                RequiresRestart = true,
                HardwareRequirement = "AMD Radeon GPU",
                DriverRequirement = "AMD Display Driver",
                WhyApplicable = isAmd ? $"AMD GPU detected: {hw.Name}." : "Not Applicable: AMD GPU not detected.",
                WhyRecommended = "Increases texture sampling throughput with no perceptible loss in image fidelity.",
                CurrentValueDisplay = isAmd ? (raw != null ? $"{cur} (0x{cur:X2})" : "0 (Disabled)") : "NOT APPLICABLE",
                CurrentValueRaw = raw,
                TargetValueDisplay = isAmd ? "1 (Enabled)" : "NOT APPLICABLE",
                TargetValueRaw = target,
                Status = !isAmd ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // 22. AMD StartMinimized
        private GpuRegistryItem EvaluateAmdStartMinimized(GpuHardwareInfo hw, Dictionary<string, GpuRegistrySnapshot> snapshots, string profile)
        {
            const string valName = "StartMinimized";
            const string id = "gpu.amd.start_minimized";
            bool isAmd = hw.VendorType == GpuVendorType.Amd;

            const string regPath = @"HKLM\SOFTWARE\AMD\CN";
            object? raw = isAmd ? ReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\AMD\CN", valName) : null;
            int cur = raw is int i ? i : (raw is uint u ? (int)u : 0);
            int target = 1;

            bool isOptimal = raw != null && cur == target;

            var item = new GpuRegistryItem
            {
                Id = id,
                Name = valName,
                DisplayName = "AMD SOFTWARE START MINIMIZED",
                Category = GpuRegistryCategory.AmdSpecific,
                SubCategory = "Privacy & Background Services",
                RegistryPath = regPath,
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Description = "Configures AMD Software to start minimized to the notification area without opening a splash screen window at logon.",
                Risk = GpuOptimizationRisk.Safe,
                RequiresRestart = false,
                HardwareRequirement = "AMD Radeon Software",
                DriverRequirement = "AMD Adrenalin Driver",
                WhyApplicable = isAmd ? "Applicable for AMD Radeon Software installations." : "Not Applicable: AMD Software not installed.",
                WhyRecommended = "Reduces desktop logon delay and eliminates startup window flicker.",
                CurrentValueDisplay = isAmd ? (raw != null ? $"{cur} (0x{cur:X2})" : "0 (Normal Window)") : "NOT APPLICABLE",
                CurrentValueRaw = raw,
                TargetValueDisplay = isAmd ? "1 (Start Minimized)" : "NOT APPLICABLE",
                TargetValueRaw = target,
                Status = !isAmd ? RegistryValueStatus.NotApplicable : (isOptimal ? RegistryValueStatus.AlreadyConfigured : RegistryValueStatus.Recommended),
                HasBackup = snapshots.ContainsKey(id)
            };
            return item;
        }

        // ── 3. Transactional Apply & Rollback Pipeline ──────────────────────

        public (bool Success, string Message) ApplyOptimization(GpuRegistryItem item)
        {
            if (item == null) return (false, "Item is null.");
            if (!item.IsApplicable) return (false, "Item is marked Not Applicable for current hardware.");

            try
            {
                string subKey = item.RegistryPath.Replace(@"HKLM\", "");

                // 1. CAPTURE & BACKUP CURRENT STATE
                object? previousVal = ReadRegistryValue(Registry.LocalMachine, subKey, item.ValueName);
                bool existed = previousVal != null;

                // Record in Universal BackupManager
                try
                {
                    BackupManager.Instance.CaptureRegistryTweak(
                        "GPU Registry",
                        item.DisplayName,
                        "HKLM",
                        subKey,
                        item.ValueName,
                        previousVal,
                        item.ValueType.ToString(),
                        item.TargetValueRaw,
                        item.RiskLevel
                    );
                }
                catch { }

                // Save snapshot for local rollback
                var snapshots = LoadSnapshots();
                snapshots[item.Id] = new GpuRegistrySnapshot
                {
                    ItemId = item.Id,
                    RegistryPath = item.RegistryPath,
                    ValueName = item.ValueName,
                    PreviousValueRaw = previousVal,
                    ValueTypeString = item.ValueType.ToString(),
                    PreviousExisted = existed,
                    Timestamp = DateTime.UtcNow
                };
                SaveSnapshots(snapshots);

                // 2. APPLY TARGET VALUE
                using (var key = Registry.LocalMachine.CreateSubKey(subKey, true))
                {
                    if (key == null) return (false, $"Access Denied: Unable to open {subKey} for writing.");
                    
                    if (item.ValueType == RegistryValueKind.DWord)
                    {
                        int dwordVal = item.TargetValueRaw is int i ? i : Convert.ToInt32(item.TargetValueRaw);
                        key.SetValue(item.ValueName, dwordVal, RegistryValueKind.DWord);
                    }
                    else if (item.ValueType == RegistryValueKind.String)
                    {
                        key.SetValue(item.ValueName, item.TargetValueRaw?.ToString() ?? "", RegistryValueKind.String);
                    }
                    else
                    {
                        key.SetValue(item.ValueName, item.TargetValueRaw ?? 0);
                    }
                    key.Flush();
                }

                // 3. READBACK & VERIFICATION
                object? verifiedVal = ReadRegistryValue(Registry.LocalMachine, subKey, item.ValueName);
                bool matches = false;
                if (item.ValueType == RegistryValueKind.DWord)
                {
                    int vInt = verifiedVal is int vi ? vi : (verifiedVal is uint vu ? (int)vu : -1);
                    int tInt = item.TargetValueRaw is int ti ? ti : Convert.ToInt32(item.TargetValueRaw);
                    matches = vInt == tInt;
                }
                else
                {
                    matches = string.Equals(verifiedVal?.ToString(), item.TargetValueRaw?.ToString(), StringComparison.OrdinalIgnoreCase);
                }

                if (!matches)
                {
                    item.Status = RegistryValueStatus.Failed;
                    item.VerificationStatus = "FAILED";
                    return (false, "Readback Verification Failed: Registry value did not match target.");
                }

                item.CurrentValueRaw = verifiedVal;
                item.CurrentValueDisplay = item.TargetValueDisplay;
                item.Status = item.RequiresRestart ? RegistryValueStatus.RequiresRestart : RegistryValueStatus.AlreadyConfigured;
                item.VerificationStatus = "VERIFIED";
                item.HasBackup = true;

                return (true, $"Successfully applied and verified {item.DisplayName}. {(item.RequiresRestart ? "Restart required to take effect." : "")}");
            }
            catch (Exception ex)
            {
                item.Status = RegistryValueStatus.Failed;
                item.VerificationStatus = "FAILED";
                return (false, $"Exception: {ex.Message}");
            }
        }

        public (bool Success, string Message) RestoreOptimization(GpuRegistryItem item)
        {
            if (item == null) return (false, "Item is null.");

            try
            {
                var snapshots = LoadSnapshots();
                if (!snapshots.TryGetValue(item.Id, out var snap))
                {
                    return (false, "No previous backup state found for this item.");
                }

                string subKey = item.RegistryPath.Replace(@"HKLM\", "");

                using (var key = Registry.LocalMachine.OpenSubKey(subKey, true))
                {
                    if (key == null) return (false, $"Unable to open registry subkey: {subKey}");

                    if (snap.PreviousExisted && snap.PreviousValueRaw != null)
                    {
                        if (item.ValueType == RegistryValueKind.DWord)
                        {
                            int val = 0;
                            if (snap.PreviousValueRaw is JsonElement je && (je.ValueKind == JsonValueKind.Number))
                            {
                                val = je.GetInt32();
                            }
                            else if (snap.PreviousValueRaw is int i)
                            {
                                val = i;
                            }
                            else if (snap.PreviousValueRaw is uint u)
                            {
                                val = (int)u;
                            }
                            else
                            {
                                val = Convert.ToInt32(snap.PreviousValueRaw.ToString());
                            }
                            key.SetValue(item.ValueName, val, RegistryValueKind.DWord);
                        }
                        else
                        {
                            string strVal = snap.PreviousValueRaw is JsonElement je ? (je.GetString() ?? je.ToString()) : snap.PreviousValueRaw.ToString() ?? "";
                            key.SetValue(item.ValueName, strVal, RegistryValueKind.String);
                        }
                    }
                    else
                    {
                        try { key.DeleteValue(item.ValueName, false); } catch { }
                    }
                    key.Flush();
                }

                snapshots.Remove(item.Id);
                SaveSnapshots(snapshots);

                // Readback restored
                object? cur = ReadRegistryValue(Registry.LocalMachine, subKey, item.ValueName);
                item.CurrentValueRaw = cur;
                item.CurrentValueDisplay = cur != null ? cur.ToString()! : "Not Set";
                item.Status = RegistryValueStatus.Recommended;
                item.VerificationStatus = "RESTORED";
                item.HasBackup = false;

                return (true, $"Successfully restored previous state for {item.DisplayName}.");
            }
            catch (Exception ex)
            {
                return (false, $"Restore failed: {ex.Message}");
            }
        }

        public (bool Success, string Message) ResetToDefault(GpuRegistryItem item)
        {
            if (item == null) return (false, "Item is null.");

            try
            {
                string subKey = item.RegistryPath.Replace(@"HKLM\", "");
                using (var key = Registry.LocalMachine.OpenSubKey(subKey, true))
                {
                    if (key != null)
                    {
                        try { key.DeleteValue(item.ValueName, false); } catch { }
                        key.Flush();
                    }
                }

                object? cur = ReadRegistryValue(Registry.LocalMachine, subKey, item.ValueName);
                item.CurrentValueRaw = cur;
                item.CurrentValueDisplay = "Windows Default";
                item.Status = RegistryValueStatus.Recommended;
                item.VerificationStatus = "RESET";

                return (true, $"Successfully reset {item.DisplayName} to Windows Default.");
            }
            catch (Exception ex)
            {
                return (false, $"Reset failed: {ex.Message}");
            }
        }

        // ── Helper Methods ──────────────────────────────────────────────────
        private object? ReadRegistryValue(RegistryKey root, string subKey, string valName)
        {
            try
            {
                using var key = root.OpenSubKey(subKey);
                return key?.GetValue(valName);
            }
            catch { return null; }
        }

        private Dictionary<string, GpuRegistrySnapshot> LoadSnapshots()
        {
            try
            {
                if (File.Exists(SnapshotFile))
                {
                    string json = File.ReadAllText(SnapshotFile);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, GpuRegistrySnapshot>>(json);
                    if (dict != null) return dict;
                }
            }
            catch { }
            return new Dictionary<string, GpuRegistrySnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        private void SaveSnapshots(Dictionary<string, GpuRegistrySnapshot> snapshots)
        {
            try
            {
                string json = JsonSerializer.Serialize(snapshots, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SnapshotFile, json);
            }
            catch { }
        }
    }
}
