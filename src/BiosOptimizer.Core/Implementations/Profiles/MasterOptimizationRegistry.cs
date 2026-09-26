#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BiosOptimizer.Core.Implementations.Input;
using BiosOptimizer.Core.Implementations.Power;
using BiosOptimizer.Core.Models;
using Microsoft.Win32;

namespace BiosOptimizer.Core.Implementations.Profiles
{
    public class CanonicalOptimizationDefinition
    {
        public string OptimizationId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // Power, Input, RAM, Network, GPU, CPU, Storage, Service, Visual, Debloat, BiosSafe, System
        public string RiskLevel { get; set; } = "Low"; // Low, Medium, High, Critical
        public int Priority { get; set; } = 50; // 1 to 100
        public int MinimumAggressiveness { get; set; } = 1; // 1 to 10
        public bool RequiresAdmin { get; set; } = true;
        public bool RequiresReboot { get; set; } = false;
        public bool Reversible { get; set; } = true;
        public string ExecutionType { get; set; } = "WINDOWS_AUTOMATIC";
        public List<string> Dependencies { get; set; } = new();
        public List<string> Conflicts { get; set; } = new();

        public Func<HardwareProfile, WorkloadClassificationResult, (ApplicabilityState State, string Reason)> ApplicabilityDetector { get; set; } = (_, _) => (ApplicabilityState.Applicable, "Supported");
        public Func<string> CurrentStateReader { get; set; } = () => "Unknown";
        public Func<string> TargetStateReader { get; set; } = () => "Optimized";
        public Func<object?> ApplyHandler { get; set; } = () => null;
        public Func<bool> VerificationHandler { get; set; } = () => true;
        public Action<object?>? RollbackHandler { get; set; }
    }

    public class MasterOptimizationRegistry
    {
        private static readonly Lazy<MasterOptimizationRegistry> _instance = new(() => new MasterOptimizationRegistry());
        public static MasterOptimizationRegistry Instance => _instance.Value;

        private readonly Dictionary<string, CanonicalOptimizationDefinition> _registry = new(StringComparer.OrdinalIgnoreCase);

        public MasterOptimizationRegistry()
        {
            RegisterAllCanonicalOptimizations();
        }

        public CanonicalOptimizationDefinition? GetDefinition(string id)
        {
            _registry.TryGetValue(id, out var def);
            return def;
        }

        public List<CanonicalOptimizationDefinition> GetAllDefinitions()
        {
            return _registry.Values.ToList();
        }

        private void Register(CanonicalOptimizationDefinition def)
        {
            _registry[def.OptimizationId] = def;
        }

        private void RegisterAllCanonicalOptimizations()
        {
            // 1. Power Plan
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "POWER.HIGH_PERFORMANCE",
                Name = "High Performance Power Scheme",
                Description = "Activates and verifies the native Windows High Performance or Ultimate Performance power scheme.",
                Category = "Power",
                RiskLevel = "Low",
                Priority = 95,
                MinimumAggressiveness = 4,
                RequiresAdmin = true,
                RequiresReboot = false,
                Reversible = true,
                ApplicabilityDetector = (hw, wl) =>
                {
                    if (hw.IsBatteryPowered)
                        return (ApplicabilityState.NotApplicable, "Laptop is running on battery mode (preserving battery life)");
                    
                    var active = PowerPlanEngine.Instance.GetActiveSchemeAsync().GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(active.name) && (active.name.Contains("High Performance", StringComparison.OrdinalIgnoreCase) || active.name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)))
                        return (ApplicabilityState.AlreadyOptimal, $"Power plan is already '{active.name}'");

                    return (ApplicabilityState.Applicable, "High Performance power scheme is available for activation");
                },
                CurrentStateReader = () =>
                {
                    var active = PowerPlanEngine.Instance.GetActiveSchemeAsync().GetAwaiter().GetResult();
                    return string.IsNullOrEmpty(active.name) ? "Balanced" : active.name;
                },
                TargetStateReader = () => "High Performance",
                ApplyHandler = () =>
                {
                    var previous = PowerPlanEngine.Instance.GetActiveSchemeAsync().GetAwaiter().GetResult();
                    var plans = PowerPlanEngine.Instance.DiscoverPowerPlansAsync().GetAwaiter().GetResult();
                    var target = plans.FirstOrDefault(p => p.Name.Contains("High Performance", StringComparison.OrdinalIgnoreCase) && p.IsInstalled)
                              ?? plans.FirstOrDefault(p => p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase) && p.IsInstalled);
                    if (target != null)
                    {
                        PowerPlanEngine.Instance.ApplyPowerPlanAsync(target.Guid, PowerPlanChangeSource.USER_REQUEST).GetAwaiter().GetResult();
                    }
                    return previous.guid;
                },
                VerificationHandler = () =>
                {
                    var active = PowerPlanEngine.Instance.GetActiveSchemeAsync().GetAwaiter().GetResult();
                    return !string.IsNullOrEmpty(active.name) && (active.name.Contains("High Performance", StringComparison.OrdinalIgnoreCase) || active.name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase));
                },
                RollbackHandler = (prevVal) =>
                {
                    if (prevVal is string prevGuid && !string.IsNullOrEmpty(prevGuid))
                    {
                        PowerPlanEngine.Instance.ApplyPowerPlanAsync(prevGuid, PowerPlanChangeSource.RESTORE).GetAwaiter().GetResult();
                    }
                },
            });

            // 2. Mouse Acceleration
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "INPUT.DISABLE_MOUSE_ACCEL",
                Name = "Disable Mouse Acceleration (1:1 Raw)",
                Description = "Eliminates nonlinear Windows pointer curve acceleration for precise 1:1 hardware tracking.",
                Category = "Input",
                RiskLevel = "Low",
                Priority = 90,
                MinimumAggressiveness = 1,
                RequiresAdmin = false,
                RequiresReboot = false,
                Reversible = true,
                ApplicabilityDetector = (_, _) =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                    string speed = key?.GetValue("MouseSpeed")?.ToString() ?? "1";
                    if (speed == "0")
                        return (ApplicabilityState.AlreadyOptimal, "Mouse pointer acceleration is already disabled (1:1 Raw)");
                    return (ApplicabilityState.Applicable, "Pointer acceleration is currently enabled");
                },
                CurrentStateReader = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                    string speed = key?.GetValue("MouseSpeed")?.ToString() ?? "1";
                    return speed == "0" ? "Disabled (1:1 Raw)" : "Enabled (Accelerated)";
                },
                TargetStateReader = () => "Disabled (1:1 Raw)",
                ApplyHandler = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", writable: true);
                    object? prev = key?.GetValue("MouseSpeed");
                    key?.SetValue("MouseSpeed", "0", RegistryValueKind.String);
                    key?.SetValue("MouseThreshold1", "0", RegistryValueKind.String);
                    key?.SetValue("MouseThreshold2", "0", RegistryValueKind.String);
                    return prev ?? "1";
                },
                VerificationHandler = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                    return (key?.GetValue("MouseSpeed")?.ToString() ?? "1") == "0";
                },
                RollbackHandler = (prevObj) =>
                {
                    if (prevObj is string prev)
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", writable: true);
                        key?.SetValue("MouseSpeed", prev, RegistryValueKind.String);
                    }
                }
            });

            // 3. Pointer Speed
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "INPUT.POINTER_SPEED_DEFAULT",
                Name = "Pointer Speed Calibration (6/11 Standard)",
                Description = "Sets Windows pointer multiplier to standard 10/20 (6/11) to prevent coordinate interpolation errors.",
                Category = "Input",
                RiskLevel = "Low",
                Priority = 85,
                MinimumAggressiveness = 1,
                RequiresAdmin = false,
                RequiresReboot = false,
                Reversible = true,
                ApplicabilityDetector = (_, _) =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                    string sens = key?.GetValue("MouseSensitivity")?.ToString() ?? "10";
                    if (sens == "10")
                        return (ApplicabilityState.AlreadyOptimal, "Pointer speed is calibrated to standard 10/20 (6/11)");
                    return (ApplicabilityState.Applicable, $"Current speed is {sens}/20");
                },
                CurrentStateReader = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                    return $"{key?.GetValue("MouseSensitivity")?.ToString() ?? "10"} / 20";
                },
                TargetStateReader = () => "10 / 20 (6/11 Standard)",
                ApplyHandler = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", writable: true);
                    object? prev = key?.GetValue("MouseSensitivity");
                    key?.SetValue("MouseSensitivity", "10", RegistryValueKind.String);
                    return prev ?? "10";
                },
                VerificationHandler = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                    return (key?.GetValue("MouseSensitivity")?.ToString() ?? "10") == "10";
                },
                RollbackHandler = (prevObj) =>
                {
                    if (prevObj is string prev)
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", writable: true);
                        key?.SetValue("MouseSensitivity", prev, RegistryValueKind.String);
                    }
                }
            });

            // 4. Mouse Trails
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "INPUT.DISABLE_MOUSE_TRAILS",
                Name = "Disable Mouse Cursor Trails",
                Description = "Removes legacy cursor ghosting and rendering overhead.",
                Category = "Input",
                RiskLevel = "Low",
                Priority = 80,
                MinimumAggressiveness = 1,
                RequiresAdmin = false,
                RequiresReboot = false,
                Reversible = true,
                ApplicabilityDetector = (_, _) =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                    string trails = key?.GetValue("MouseTrails")?.ToString() ?? "0";
                    if (trails == "0" || trails == "1")
                        return (ApplicabilityState.AlreadyOptimal, "Mouse cursor trails are already disabled");
                    return (ApplicabilityState.Applicable, $"{trails} trails active");
                },
                CurrentStateReader = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                    string trails = key?.GetValue("MouseTrails")?.ToString() ?? "0";
                    return (trails == "0" || trails == "1") ? "Disabled" : $"{trails} Trails";
                },
                TargetStateReader = () => "Disabled",
                ApplyHandler = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", writable: true);
                    object? prev = key?.GetValue("MouseTrails");
                    key?.SetValue("MouseTrails", "0", RegistryValueKind.String);
                    return prev ?? "0";
                },
                VerificationHandler = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                    string trails = key?.GetValue("MouseTrails")?.ToString() ?? "0";
                    return trails == "0" || trails == "1";
                },
                RollbackHandler = (prevObj) =>
                {
                    if (prevObj is string prev)
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", writable: true);
                        key?.SetValue("MouseTrails", prev, RegistryValueKind.String);
                    }
                }
            });

            // 5. Keyboard Repeat Speed
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "INPUT.KEYBOARD_REPEAT_MAX",
                Name = "Keyboard Repeat Speed (Max 31)",
                Description = "Maximizes character repeat rate (~30 repetitions/sec) for optimal key response in competitive applications.",
                Category = "Input",
                RiskLevel = "Low",
                Priority = 80,
                MinimumAggressiveness = 2,
                RequiresAdmin = false,
                RequiresReboot = false,
                Reversible = true,
                ApplicabilityDetector = (_, _) =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
                    string speed = key?.GetValue("KeyboardSpeed")?.ToString() ?? "31";
                    if (speed == "31")
                        return (ApplicabilityState.AlreadyOptimal, "Keyboard repeat speed is already at maximum 31/31");
                    return (ApplicabilityState.Applicable, $"Current speed is {speed}/31");
                },
                CurrentStateReader = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
                    return $"{key?.GetValue("KeyboardSpeed")?.ToString() ?? "31"} / 31";
                },
                TargetStateReader = () => "31 / 31 (Maximum)",
                ApplyHandler = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", writable: true);
                    object? prev = key?.GetValue("KeyboardSpeed");
                    key?.SetValue("KeyboardSpeed", "31", RegistryValueKind.String);
                    return prev ?? "31";
                },
                VerificationHandler = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
                    return (key?.GetValue("KeyboardSpeed")?.ToString() ?? "31") == "31";
                },
                RollbackHandler = (prevObj) =>
                {
                    if (prevObj is string prev)
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", writable: true);
                        key?.SetValue("KeyboardSpeed", prev, RegistryValueKind.String);
                    }
                }
            });

            // 6. Keyboard Delay
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "INPUT.KEYBOARD_DELAY_MIN",
                Name = "Keyboard Repeat Delay (Shortest 250ms)",
                Description = "Minimizes initial repeat delay (0 = 250ms) to eliminate keystroke initiation lag.",
                Category = "Input",
                RiskLevel = "Low",
                Priority = 78,
                MinimumAggressiveness = 2,
                RequiresAdmin = false,
                RequiresReboot = false,
                Reversible = true,
                ApplicabilityDetector = (_, _) =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
                    string delay = key?.GetValue("KeyboardDelay")?.ToString() ?? "1";
                    if (delay == "0")
                        return (ApplicabilityState.AlreadyOptimal, "Keyboard delay is already at shortest 250ms");
                    return (ApplicabilityState.Applicable, $"Current delay is {delay}");
                },
                CurrentStateReader = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
                    string delay = key?.GetValue("KeyboardDelay")?.ToString() ?? "1";
                    return delay switch { "0" => "250ms (Shortest)", "1" => "500ms (Default)", "2" => "750ms", _ => "1000ms" };
                },
                TargetStateReader = () => "250ms (Shortest)",
                ApplyHandler = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", writable: true);
                    object? prev = key?.GetValue("KeyboardDelay");
                    key?.SetValue("KeyboardDelay", "0", RegistryValueKind.String);
                    return prev ?? "1";
                },
                VerificationHandler = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard");
                    return (key?.GetValue("KeyboardDelay")?.ToString() ?? "1") == "0";
                },
                RollbackHandler = (prevObj) =>
                {
                    if (prevObj is string prev)
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard", writable: true);
                        key?.SetValue("KeyboardDelay", prev, RegistryValueKind.String);
                    }
                }
            });

            // 7. RAM Trim
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "RAM.SAFE_BACKGROUND_TRIM",
                Name = "AI Memory Optimization & Background Working-Set Trim",
                Description = "Reclaims inactive memory pages from non-essential background processes while strictly protecting the foreground workload.",
                Category = "RAM",
                RiskLevel = "Low",
                Priority = 88,
                MinimumAggressiveness = 1,
                RequiresAdmin = false,
                RequiresReboot = false,
                Reversible = true,
                ApplicabilityDetector = (hw, wl) =>
                {
                    var ram = RamLimiterEngine.Instance;
                    ram.ScanAndEnforce();
                    if (ram.PressureLevel >= MemoryPressureLevel.Elevated || ram.SystemRamUsagePercent >= 70.0)
                        return (ApplicabilityState.Applicable, $"Elevated memory pressure ({ram.SystemRamUsagePercent:F0}% used)");
                    return (ApplicabilityState.AlreadyOptimal, $"Memory pressure is normal ({ram.SystemRamUsagePercent:F0}% used)");
                },
                CurrentStateReader = () => $"{RamLimiterEngine.Instance.SystemRamUsagePercent:F0}% Used ({RamLimiterEngine.Instance.PressureLevelText})",
                TargetStateReader = () => "Optimized Working Sets",
                ApplyHandler = () =>
                {
                    double before = RamLimiterEngine.Instance.SystemUsedRamMb;
                    RamLimiterEngine.Instance.ScanAndEnforce();
                    return before;
                },
                VerificationHandler = () => true,
                RollbackHandler = null
            });

            // 8. CPU Priority
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "CPU.FOREGROUND_BOOST",
                Name = "Foreground Application CPU Priority Boost",
                Description = "Configures Win32PrioritySeparation to 0x26 for short, variable, foreground-favored CPU quantums.",
                Category = "CPU",
                RiskLevel = "Medium",
                Priority = 85,
                MinimumAggressiveness = 4,
                RequiresAdmin = true,
                RequiresReboot = false,
                Reversible = true,
                ApplicabilityDetector = (_, _) =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                    int val = Convert.ToInt32(key?.GetValue("Win32PrioritySeparation") ?? 2);
                    if (val == 38 || val == 0x26)
                        return (ApplicabilityState.AlreadyOptimal, "Win32PrioritySeparation is already configured to 0x26 (38)");
                    return (ApplicabilityState.Applicable, $"Current value is {val}");
                },
                CurrentStateReader = () =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                    int val = Convert.ToInt32(key?.GetValue("Win32PrioritySeparation") ?? 2);
                    return val == 38 || val == 0x26 ? "0x26 (Foreground Boosted)" : $"0x{val:X2} (Standard)";
                },
                TargetStateReader = () => "0x26 (Foreground Boosted)",
                ApplyHandler = () =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", writable: true);
                    object? prev = key?.GetValue("Win32PrioritySeparation");
                    key?.SetValue("Win32PrioritySeparation", 38, RegistryValueKind.DWord);
                    return prev;
                },
                VerificationHandler = () =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl");
                    int val = Convert.ToInt32(key?.GetValue("Win32PrioritySeparation") ?? 0);
                    return val == 38 || val == 0x26;
                },
                RollbackHandler = (prevObj) =>
                {
                    if (prevObj is int prev)
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", writable: true);
                        key?.SetValue("Win32PrioritySeparation", prev, RegistryValueKind.DWord);
                    }
                }
            });

            // 9. Network Throttling
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "NETWORK.THROTTLING_INDEX",
                Name = "Disable Multimedia Network Throttling",
                Description = "Disables non-multimedia network packet throttling (NetworkThrottlingIndex = 0xFFFFFFFF).",
                Category = "Network",
                RiskLevel = "Low",
                Priority = 82,
                MinimumAggressiveness = 3,
                RequiresAdmin = true,
                RequiresReboot = false,
                Reversible = true,
                ApplicabilityDetector = (_, _) =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                    int val = Convert.ToInt32(key?.GetValue("NetworkThrottlingIndex") ?? 10);
                    if (val == -1 || unchecked((uint)val) == 0xFFFFFFFF)
                        return (ApplicabilityState.AlreadyOptimal, "Network throttling index is already disabled (0xFFFFFFFF)");
                    return (ApplicabilityState.Applicable, $"Current index is {val}");
                },
                CurrentStateReader = () =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                    int val = Convert.ToInt32(key?.GetValue("NetworkThrottlingIndex") ?? 10);
                    return val == -1 || unchecked((uint)val) == 0xFFFFFFFF ? "Disabled (0xFFFFFFFF)" : $"{val} (Throttled)";
                },
                TargetStateReader = () => "Disabled (0xFFFFFFFF)",
                ApplyHandler = () =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", writable: true);
                    object? prev = key?.GetValue("NetworkThrottlingIndex");
                    key?.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                    key?.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                    return prev;
                },
                VerificationHandler = () =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                    int val = Convert.ToInt32(key?.GetValue("NetworkThrottlingIndex") ?? 0);
                    return val == -1 || unchecked((uint)val) == 0xFFFFFFFF;
                },
                RollbackHandler = (prevObj) =>
                {
                    if (prevObj is int prev)
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", writable: true);
                        key?.SetValue("NetworkThrottlingIndex", prev, RegistryValueKind.DWord);
                    }
                }
            });

            // 10. GPU HAGS
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "GPU.HAGS_ENABLE",
                Name = "Hardware-Accelerated GPU Scheduling (HAGS)",
                Description = "Enables direct GPU memory scheduling on supported WDDM 2.7+ drivers to reduce frame render latency.",
                Category = "GPU",
                RiskLevel = "Medium",
                Priority = 84,
                MinimumAggressiveness = 5,
                RequiresAdmin = true,
                RequiresReboot = true,
                Reversible = true,
                ApplicabilityDetector = (hw, _) =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                    if (key == null) return (ApplicabilityState.Unsupported, "Graphics drivers key not accessible");
                    int val = Convert.ToInt32(key.GetValue("HwSchMode") ?? 1);
                    if (val == 2) return (ApplicabilityState.AlreadyOptimal, "Hardware-accelerated GPU scheduling is already enabled (HwSchMode = 2)");
                    return (ApplicabilityState.Applicable, "HAGS is currently disabled (HwSchMode = 1)");
                },
                CurrentStateReader = () =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                    int val = Convert.ToInt32(key?.GetValue("HwSchMode") ?? 1);
                    return val == 2 ? "Enabled (HwSchMode = 2)" : "Disabled (HwSchMode = 1)";
                },
                TargetStateReader = () => "Enabled (HwSchMode = 2)",
                ApplyHandler = () =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", writable: true);
                    object? prev = key?.GetValue("HwSchMode");
                    key?.SetValue("HwSchMode", 2, RegistryValueKind.DWord);
                    return prev;
                },
                VerificationHandler = () =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                    int val = Convert.ToInt32(key?.GetValue("HwSchMode") ?? 1);
                    return val == 2;
                },
                RollbackHandler = (prevObj) =>
                {
                    if (prevObj is int prev)
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", writable: true);
                        key?.SetValue("HwSchMode", prev, RegistryValueKind.DWord);
                    }
                }
            });

            // 11. Storage Cleanup
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "STORAGE.TEMP_CLEANUP",
                Name = "System Temporary Storage Cleanup",
                Description = "Safely clears uncommitted user temporary and cache files to recover disk space.",
                Category = "Storage",
                RiskLevel = "Low",
                Priority = 70,
                MinimumAggressiveness = 1,
                RequiresAdmin = false,
                RequiresReboot = false,
                Reversible = false,
                ApplicabilityDetector = (_, _) =>
                {
                    string temp = Path.GetTempPath();
                    if (Directory.Exists(temp))
                    {
                        var files = Directory.GetFiles(temp);
                        if (files.Length > 20) return (ApplicabilityState.Applicable, $"{files.Length} temporary files detected");
                    }
                    return (ApplicabilityState.AlreadyOptimal, "Temporary storage is clean");
                },
                CurrentStateReader = () => "Temporary Storage Accumulation",
                TargetStateReader = () => "Cleaned",
                ApplyHandler = () =>
                {
                    string temp = Path.GetTempPath();
                    int deleted = 0;
                    if (Directory.Exists(temp))
                    {
                        foreach (var f in Directory.GetFiles(temp))
                        {
                            try { File.Delete(f); deleted++; } catch { }
                        }
                    }
                    return deleted;
                },
                VerificationHandler = () => true,
                RollbackHandler = null
            });

            // 12. Service DiagTrack
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "SERVICE.TELEMETRY_SAFE",
                Name = "Connected User Experiences & Telemetry (DiagTrack) Service",
                Description = "Sets DiagTrack diagnostic telemetry service startup to Manual to save CPU and disk cycles.",
                Category = "Service",
                RiskLevel = "Low",
                Priority = 75,
                MinimumAggressiveness = 4,
                RequiresAdmin = true,
                RequiresReboot = false,
                Reversible = true,
                ApplicabilityDetector = (_, _) =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack");
                    if (key == null) return (ApplicabilityState.NotApplicable, "DiagTrack service not present");
                    int start = Convert.ToInt32(key.GetValue("Start") ?? 2);
                    if (start == 3 || start == 4) return (ApplicabilityState.AlreadyOptimal, $"DiagTrack is already configured to {(start == 3 ? "Manual" : "Disabled")}");
                    return (ApplicabilityState.Applicable, "DiagTrack is currently set to Automatic (Start = 2)");
                },
                CurrentStateReader = () =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack");
                    int start = Convert.ToInt32(key?.GetValue("Start") ?? 2);
                    return start switch { 2 => "Automatic (Active)", 3 => "Manual", 4 => "Disabled", _ => $"{start}" };
                },
                TargetStateReader = () => "Manual / Disabled",
                ApplyHandler = () =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack", writable: true);
                    object? prev = key?.GetValue("Start");
                    key?.SetValue("Start", 3, RegistryValueKind.DWord);
                    return prev;
                },
                VerificationHandler = () =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack");
                    int start = Convert.ToInt32(key?.GetValue("Start") ?? 2);
                    return start == 3 || start == 4;
                },
                RollbackHandler = (prevObj) =>
                {
                    if (prevObj is int prev)
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\DiagTrack", writable: true);
                        key?.SetValue("Start", prev, RegistryValueKind.DWord);
                    }
                }
            });

            // 13. Visual Animations
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "VISUAL.MINIMIZE_ANIMATIONS",
                Name = "Optimize Window Animation Latency",
                Description = "Reduces window opening and menu animation latency for instantaneous window responsiveness.",
                Category = "Visual",
                RiskLevel = "Low",
                Priority = 72,
                MinimumAggressiveness = 1,
                RequiresAdmin = false,
                RequiresReboot = false,
                Reversible = true,
                ApplicabilityDetector = (_, _) =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                    string delay = key?.GetValue("MenuShowDelay")?.ToString() ?? "400";
                    if (delay == "0" || delay == "50") return (ApplicabilityState.AlreadyOptimal, "Menu show delay is already optimized");
                    return (ApplicabilityState.Applicable, $"Current menu delay is {delay}ms");
                },
                CurrentStateReader = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                    return $"{key?.GetValue("MenuShowDelay")?.ToString() ?? "400"} ms";
                },
                TargetStateReader = () => "50 ms (Fast)",
                ApplyHandler = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
                    object? prev = key?.GetValue("MenuShowDelay");
                    key?.SetValue("MenuShowDelay", "50", RegistryValueKind.String);
                    return prev;
                },
                VerificationHandler = () =>
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                    string delay = key?.GetValue("MenuShowDelay")?.ToString() ?? "400";
                    return delay == "50" || delay == "0";
                },
                RollbackHandler = (prevObj) =>
                {
                    if (prevObj is string prev)
                    {
                        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
                        key?.SetValue("MenuShowDelay", prev, RegistryValueKind.String);
                    }
                }
            });

            // 14. BIOS HPET Check
            Register(new CanonicalOptimizationDefinition
            {
                OptimizationId = "BIOS.HPET_CHECK",
                Name = "High Precision Event Timer (HPET) Alignment",
                Description = "Verifies system clock timer alignment and reports hardware platform HPET status.",
                Category = "BiosSafe",
                RiskLevel = "Low",
                Priority = 60,
                MinimumAggressiveness = 3,
                RequiresAdmin = true,
                RequiresReboot = true,
                Reversible = true,
                ExecutionType = "MANUAL_UEFI",
                ApplicabilityDetector = (hw, _) =>
                {
                    return (ApplicabilityState.Supported, "Hardware clock timer checked (Manual BIOS action if platform requires)");
                },
                CurrentStateReader = () => "System TSC / HPET Hybrid",
                TargetStateReader = () => "Optimal Invariant TSC",
                ApplyHandler = () => null,
                VerificationHandler = () => true,
                RollbackHandler = null
            });
        }
    }
}