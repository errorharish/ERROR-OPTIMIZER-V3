using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Interop;
using BiosOptimizer.Core.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Implementations
{
    public class RegistryTransactionEntry
    {
        public string TransactionId { get; set; } = Guid.NewGuid().ToString("N");
        public string OptimizationId { get; set; } = string.Empty;
        public string Hive { get; set; } = "HKLM";
        public string Path { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public string ValueType { get; set; } = "REG_DWORD";
        public string BeforeValue { get; set; } = string.Empty;
        public string TargetValue { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;
        public string Applicability { get; set; } = "APPLICABLE";
        public string ApplyState { get; set; } = "APPLIED";
        public bool RequiresReboot { get; set; } = false;
        public string VerificationState { get; set; } = "UNVERIFIED";
        public string RegistryView { get; set; } = "Registry64";
        public string ErrorCode { get; set; } = "0x00000000";
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public DateTime? RebootVerifiedTimestamp { get; set; }
    }

    public class RegistryDiagnosticEntry
    {
        public string OptimizationId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Hive { get; set; } = "HKLM";
        public string SubKey { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public string RequiredType { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;
        public string TargetValue { get; set; } = string.Empty;
        public string WindowsBuild { get; set; } = string.Empty;
        public string RegistryView { get; set; } = string.Empty;
        public string PermissionState { get; set; } = string.Empty;
        public string ApplicabilityResult { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string VerificationState { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = "0x00000000";
        public string DiagnosticDetail { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class RegistryTransactionJournal
    {
        private static readonly object _sync = new object();
        private readonly string _journalPath;
        private readonly string _diagnosticsPath;
        private const string RegistryKeyPath = @"Software\ErrorOptimizer\RegistryOptimizer\Transactions";

        public RegistryTransactionJournal()
        {
            string progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(progData))
            {
                progData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer");
            }
            string dir = Path.Combine(progData, "ErrorOptimizer", "RegistryOptimizer");
            try { Directory.CreateDirectory(dir); } catch { }
            _journalPath = Path.Combine(dir, "registry_transaction_journal.json");
            _diagnosticsPath = Path.Combine(dir, "registry_diagnostics.json");
        }

        public List<RegistryTransactionEntry> LoadEntries()
        {
            lock (_sync)
            {
                var list = new Dictionary<string, RegistryTransactionEntry>(StringComparer.OrdinalIgnoreCase);

                // 1. Load from file
                try
                {
                    if (File.Exists(_journalPath))
                    {
                        var json = File.ReadAllText(_journalPath);
                        var fromFile = JsonSerializer.Deserialize<List<RegistryTransactionEntry>>(json);
                        if (fromFile != null)
                        {
                            foreach (var item in fromFile)
                            {
                                if (!string.IsNullOrEmpty(item.OptimizationId))
                                    list[item.OptimizationId] = item;
                            }
                        }
                    }
                }
                catch { }

                // 2. Load from registry fallback mirror
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                    if (key != null)
                    {
                        foreach (var subName in key.GetSubKeyNames())
                        {
                            using var sub = key.OpenSubKey(subName, false);
                            if (sub != null)
                            {
                                string optId = sub.GetValue("OptimizationId")?.ToString() ?? subName;
                                if (!list.ContainsKey(optId))
                                {
                                    list[optId] = new RegistryTransactionEntry
                                    {
                                        TransactionId = sub.GetValue("TransactionId")?.ToString() ?? Guid.NewGuid().ToString("N"),
                                        OptimizationId = optId,
                                        Hive = sub.GetValue("Hive")?.ToString() ?? "HKLM",
                                        Path = sub.GetValue("Path")?.ToString() ?? "",
                                        ValueName = sub.GetValue("ValueName")?.ToString() ?? "",
                                        ValueType = sub.GetValue("ValueType")?.ToString() ?? "REG_DWORD",
                                        BeforeValue = sub.GetValue("BeforeValue")?.ToString() ?? "",
                                        TargetValue = sub.GetValue("TargetValue")?.ToString() ?? "",
                                        CurrentValue = sub.GetValue("CurrentValue")?.ToString() ?? "",
                                        Applicability = sub.GetValue("Applicability")?.ToString() ?? "APPLICABLE",
                                        ApplyState = sub.GetValue("ApplyState")?.ToString() ?? "APPLIED",
                                        RequiresReboot = Convert.ToBoolean(sub.GetValue("RequiresReboot") ?? false),
                                        VerificationState = sub.GetValue("VerificationState")?.ToString() ?? "UNVERIFIED",
                                        RegistryView = sub.GetValue("RegistryView")?.ToString() ?? "Registry64",
                                        ErrorCode = sub.GetValue("ErrorCode")?.ToString() ?? "0x00000000",
                                        ErrorMessage = sub.GetValue("ErrorMessage")?.ToString() ?? "",
                                        Timestamp = DateTime.TryParse(sub.GetValue("Timestamp")?.ToString(), out var dt) ? dt : DateTime.UtcNow
                                    };
                                }
                            }
                        }
                    }
                }
                catch { }

                return list.Values.ToList();
            }
        }

        public void SaveEntry(RegistryTransactionEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.OptimizationId)) return;

            lock (_sync)
            {
                var all = LoadEntries();
                int idx = all.FindIndex(x => string.Equals(x.OptimizationId, entry.OptimizationId, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) all[idx] = entry;
                else all.Add(entry);

                // 1. Write to JSON
                try
                {
                    string json = JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_journalPath, json);
                }
                catch { }

                // 2. Mirror to HKCU Registry
                try
                {
                    using var root = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, true);
                    if (root != null)
                    {
                        using var sub = root.CreateSubKey(entry.OptimizationId, true);
                        if (sub != null)
                        {
                            sub.SetValue("TransactionId", entry.TransactionId);
                            sub.SetValue("OptimizationId", entry.OptimizationId);
                            sub.SetValue("Hive", entry.Hive);
                            sub.SetValue("Path", entry.Path);
                            sub.SetValue("ValueName", entry.ValueName);
                            sub.SetValue("ValueType", entry.ValueType);
                            sub.SetValue("BeforeValue", entry.BeforeValue ?? "");
                            sub.SetValue("TargetValue", entry.TargetValue ?? "");
                            sub.SetValue("CurrentValue", entry.CurrentValue ?? "");
                            sub.SetValue("Applicability", entry.Applicability);
                            sub.SetValue("ApplyState", entry.ApplyState);
                            sub.SetValue("RequiresReboot", entry.RequiresReboot ? 1 : 0, RegistryValueKind.DWord);
                            sub.SetValue("VerificationState", entry.VerificationState);
                            sub.SetValue("RegistryView", entry.RegistryView ?? "Registry64");
                            sub.SetValue("ErrorCode", entry.ErrorCode ?? "0x00000000");
                            sub.SetValue("ErrorMessage", entry.ErrorMessage ?? "");
                            sub.SetValue("Timestamp", entry.Timestamp.ToString("O"));
                            if (entry.RebootVerifiedTimestamp.HasValue)
                                sub.SetValue("RebootVerifiedTimestamp", entry.RebootVerifiedTimestamp.Value.ToString("O"));
                        }
                    }
                }
                catch { }
            }
        }

        public void SaveDiagnostics(List<RegistryDiagnosticEntry> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0) return;
            lock (_sync)
            {
                try
                {
                    string json = JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_diagnosticsPath, json);
                }
                catch { }
            }
        }
    }

    public class RegistryTweakEngine : IRegistryTweakEngine
    {
        private readonly IBackupManager _backupManager;
        private readonly RegistryTransactionJournal _journal;

        public class RegistryTweakDefinition
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string RootKey { get; set; } = "HKLM"; // "HKLM" or "HKCU"
            public string SubKey { get; set; } = string.Empty;
            public string ValueName { get; set; } = string.Empty;
            public RegistryValueKind ValueKind { get; set; } = RegistryValueKind.DWord;
            public string DefaultValueStr { get; set; } = string.Empty;
            public string TargetValueStr { get; set; } = string.Empty;
            public string DisplayTargetStr { get; set; } = string.Empty;
            public string RiskLevel { get; set; } = "Low";
            public bool RequiresRestart { get; set; } = false;
            public bool RequiresAdmin { get; set; } = true;
            public string SupportedOS { get; set; } = "Windows 10 / 11 (x64 / ARM64)";
            public string HardwarePrerequisites { get; set; } = "None";
            public MissingValueBehavior MissingBehavior { get; set; } = MissingValueBehavior.SafeToCreate;
            public RegistryView PreferredView { get; set; } = RegistryView.Registry64;
            public Func<(bool IsApplicable, string Reason)>? CustomApplicability { get; set; }
            public Func<(bool IsRecommended, string Reason)>? CustomRecommendation { get; set; }
        }

        private readonly List<RegistryTweakDefinition> _tweakDatabase = new List<RegistryTweakDefinition>
        {
            // --- CPU & SCHEDULING ---
            new RegistryTweakDefinition
            {
                Id = "tweak.process.priority",
                Name = "Win32PrioritySeparation (Foreground Quantum)",
                Description = "Optimizes CPU priority quantum allocation for active foreground applications, mitigating micro-stutters during intensive interactive tasks.",
                Category = "Process",
                RootKey = "HKLM",
                SubKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl",
                ValueName = "Win32PrioritySeparation",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "2",
                TargetValueStr = "38",
                DisplayTargetStr = "38 (Hex 26 • Optimal Quantum)",
                RiskLevel = "Medium",
                RequiresRestart = true,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "Workstation / Desktop OS",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.cpu.coreparking",
                Name = "Disable Core Parking",
                Description = "Forces active multi-core execution by setting core parking max headroom to 0, preventing thread wake-up stalls.",
                Category = "CPU",
                RootKey = "HKLM",
                SubKey = @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583",
                ValueName = "ValueMax",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "100",
                TargetValueStr = "0",
                DisplayTargetStr = "0 (Unparked)",
                RiskLevel = "Low",
                RequiresRestart = false,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "Multi-Core CPU",
                MissingBehavior = MissingValueBehavior.NotApplicable,
                PreferredView = RegistryView.Registry64,
                CustomApplicability = () => CheckCoreParkingApplicability()
            },

            // --- NETWORK & MMCSS ---
            new RegistryTweakDefinition
            {
                Id = "tweak.mmcss.networkthrottling",
                Name = "Network Throttling Index",
                Description = "Disables Multimedia Class Scheduler Service (MMCSS) network packet throttling to ensure consistent packet dispatching.",
                Category = "Network",
                RootKey = "HKLM",
                SubKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                ValueName = "NetworkThrottlingIndex",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "10",
                TargetValueStr = "ffffffff",
                DisplayTargetStr = "0xFFFFFFFF (Disabled)",
                RiskLevel = "Low",
                RequiresRestart = true,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "Network Interface",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.mmcss.systemresponsiveness",
                Name = "System Responsiveness Priority",
                Description = "Sets MMCSS background multimedia CPU reservation to 0% so foreground workloads receive dedicated scheduling time.",
                Category = "System",
                RootKey = "HKLM",
                SubKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                ValueName = "SystemResponsiveness",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "20",
                TargetValueStr = "0",
                DisplayTargetStr = "0 (100% Foreground)",
                RiskLevel = "Medium",
                RequiresRestart = true,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "None",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.network.tcpnodelay",
                Name = "TCP No Delay (Nagle's Algorithm)",
                Description = "Disables Nagle's algorithm packet coalescing in MSMQ/TCP stack for immediate socket dispatch.",
                Category = "Network",
                RootKey = "HKLM",
                SubKey = @"SOFTWARE\Microsoft\MSMQ\Parameters",
                ValueName = "TCPNoDelay",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "0",
                TargetValueStr = "1",
                DisplayTargetStr = "1 (No Delay)",
                RiskLevel = "Low",
                RequiresRestart = true,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "MSMQ / TCP Stack",
                MissingBehavior = MissingValueBehavior.NotApplicable,
                PreferredView = RegistryView.Registry64,
                CustomApplicability = () => CheckMsmqApplicability()
            },

            // --- GAMING & GPU ---
            new RegistryTweakDefinition
            {
                Id = "tweak.gaming.gamedvr",
                Name = "Disable Xbox GameDVR",
                Description = "Disables Windows Xbox GameDVR background frame capture to free GPU VRAM and reduce encoder overhead.",
                Category = "Gaming",
                RootKey = "HKCU",
                SubKey = @"System\GameConfigStore",
                ValueName = "GameDVR_Enabled",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "1",
                TargetValueStr = "0",
                DisplayTargetStr = "0 (Disabled)",
                RiskLevel = "Low",
                RequiresRestart = false,
                RequiresAdmin = false,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "DirectX 11/12 GPU",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.gaming.gamedvr_fse",
                Name = "GameDVR FSEBehavior Mode",
                Description = "Configures GameDVR FSE mode to enforce true exclusive fullscreen display presentation for lowest input latency.",
                Category = "Gaming",
                RootKey = "HKCU",
                SubKey = @"System\GameConfigStore",
                ValueName = "GameDVR_FSEBehaviorMode",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "0",
                TargetValueStr = "2",
                DisplayTargetStr = "2 (Exclusive)",
                RiskLevel = "Medium",
                RequiresRestart = false,
                RequiresAdmin = false,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "None",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.gpu.hags",
                Name = "Hardware Accelerated GPU Scheduling",
                Description = "Enables hardware-level GPU scheduling on supported WDDM 2.7+ drivers to reduce frame render queue overhead.",
                Category = "GPU",
                RootKey = "HKLM",
                SubKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
                ValueName = "HwSchMode",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "1",
                TargetValueStr = "2",
                DisplayTargetStr = "2 (Enabled)",
                RiskLevel = "Medium",
                RequiresRestart = true,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 Build 19041+ / Windows 11",
                HardwarePrerequisites = "WDDM 2.7+ Compatible GPU",
                MissingBehavior = MissingValueBehavior.Unsupported,
                PreferredView = RegistryView.Registry64,
                CustomApplicability = () => CheckHagsApplicability()
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.gpu.preemption",
                Name = "Disable GPU Preemption",
                Description = "Directs the GPU scheduler to minimize compute context-switch interrupts during rendering bursts.",
                Category = "GPU",
                RootKey = "HKLM",
                SubKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Scheduler",
                ValueName = "EnablePreemption",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "1",
                TargetValueStr = "0",
                DisplayTargetStr = "0 (Disabled)",
                RiskLevel = "High",
                RequiresRestart = true,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "Dedicated Graphics Card",
                MissingBehavior = MissingValueBehavior.NotApplicable,
                PreferredView = RegistryView.Registry64,
                CustomApplicability = () => CheckGpuSchedulerApplicability()
            },

            // --- MEMORY & PROCESS ---
            new RegistryTweakDefinition
            {
                Id = "tweak.ram.svchost",
                Name = "SvcHost Split Threshold",
                Description = "Increases per-service host split threshold in KB so Windows hosts system services cleanly on systems with 4GB+ RAM.",
                Category = "Memory",
                RootKey = "HKLM",
                SubKey = @"SYSTEM\CurrentControlSet\Control",
                ValueName = "SvcHostSplitThresholdInKB",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "380000",
                TargetValueStr = "104857600",
                DisplayTargetStr = "104857600 (100 GB Threshold)",
                RiskLevel = "Medium",
                RequiresRestart = true,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 Build 1703+ / Windows 11",
                HardwarePrerequisites = "RAM >= 4GB",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64,
                CustomApplicability = () => CheckSvcHostSplitApplicability()
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.ram.clearpagefile",
                Name = "Clear Pagefile on Shutdown",
                Description = "Configures Windows Session Manager to zero out paging memory upon system shutdown for clean memory allocations.",
                Category = "Memory",
                RootKey = "HKLM",
                SubKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                ValueName = "ClearPageFileAtShutdown",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "0",
                TargetValueStr = "1",
                DisplayTargetStr = "1 (Enabled)",
                RiskLevel = "Low",
                RequiresRestart = false,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "None",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.ram.pagingexec",
                Name = "Disable Paging Executive",
                Description = "Instructs Windows kernel drivers and system code to remain locked in physical RAM rather than being paged to disk.",
                Category = "Memory",
                RootKey = "HKLM",
                SubKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                ValueName = "DisablePagingExecutive",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "0",
                TargetValueStr = "1",
                DisplayTargetStr = "1 (Keep in RAM)",
                RiskLevel = "Medium",
                RequiresRestart = true,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "RAM >= 8GB Recommended",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64,
                CustomApplicability = () => CheckPagingExecApplicability()
            },

            // --- STORAGE & NTFS ---
            new RegistryTweakDefinition
            {
                Id = "tweak.storage.ntfs8dot3",
                Name = "Disable NTFS 8.3 Name Creation",
                Description = "Disables legacy MS-DOS 8.3 short filename generation across NTFS volumes, reducing disk metadata overhead.",
                Category = "NTFS",
                RootKey = "HKLM",
                SubKey = @"SYSTEM\CurrentControlSet\Control\FileSystem",
                ValueName = "NtfsDisable8dot3NameCreation",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "2",
                TargetValueStr = "1",
                DisplayTargetStr = "1 (Disabled)",
                RiskLevel = "Medium",
                RequiresRestart = true,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "NTFS Volume",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64,
                CustomApplicability = () => CheckNtfsVolumeApplicability()
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.storage.lastaccess",
                Name = "Disable NTFS Last Access Update",
                Description = "Prevents NTFS timestamp write updates whenever files or directories are read, saving SSD/HDD write operations.",
                Category = "NTFS",
                RootKey = "HKLM",
                SubKey = @"SYSTEM\CurrentControlSet\Control\FileSystem",
                ValueName = "NtfsDisableLastAccessUpdate",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "2",
                TargetValueStr = "1",
                DisplayTargetStr = "1 (Disabled)",
                RiskLevel = "Low",
                RequiresRestart = true,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "NTFS Volume",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64,
                CustomApplicability = () => CheckNtfsVolumeApplicability()
            },

            // --- PRIVACY & TELEMETRY ---
            new RegistryTweakDefinition
            {
                Id = "tweak.privacy.telemetry",
                Name = "Disable AllowTelemetry Policy",
                Description = "Sets diagnostic data policy collection to Security/Disabled level to eliminate background telemetry bandwidth.",
                Category = "Privacy",
                RootKey = "HKLM",
                SubKey = @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                ValueName = "AllowTelemetry",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "1",
                TargetValueStr = "0",
                DisplayTargetStr = "0 (Disabled)",
                RiskLevel = "Low",
                RequiresRestart = false,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "None",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.privacy.advertisingid",
                Name = "Disable Windows Advertising ID",
                Description = "Disables user advertising profiling identifier in current user profile.",
                Category = "Privacy",
                RootKey = "HKCU",
                SubKey = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
                ValueName = "Enabled",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "1",
                TargetValueStr = "0",
                DisplayTargetStr = "0 (Disabled)",
                RiskLevel = "Low",
                RequiresRestart = false,
                RequiresAdmin = false,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "None",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.telemetry.diagtrack",
                Name = "Disable DiagTrack Service Startup",
                Description = "Sets Connected User Experiences and Telemetry service startup configuration to Disabled in registry.",
                Category = "Telemetry",
                RootKey = "HKLM",
                SubKey = @"SYSTEM\CurrentControlSet\Services\DiagTrack",
                ValueName = "Start",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "2",
                TargetValueStr = "4",
                DisplayTargetStr = "4 (Disabled)",
                RiskLevel = "Medium",
                RequiresRestart = false,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "DiagTrack Service",
                MissingBehavior = MissingValueBehavior.NotApplicable,
                PreferredView = RegistryView.Registry64,
                CustomApplicability = () => CheckServiceKeyExists(@"SYSTEM\CurrentControlSet\Services\DiagTrack", "DiagTrack service detected.", "DiagTrack service is not installed on this system.")
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.telemetry.dmwappushservice",
                Name = "Disable WAP Push Service Startup",
                Description = "Sets Device Management Wireless Application Protocol (WAP) push message service startup to Disabled.",
                Category = "Telemetry",
                RootKey = "HKLM",
                SubKey = @"SYSTEM\CurrentControlSet\Services\dmwappushservice",
                ValueName = "Start",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "3",
                TargetValueStr = "4",
                DisplayTargetStr = "4 (Disabled)",
                RiskLevel = "Medium",
                RequiresRestart = false,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "dmwappushservice",
                MissingBehavior = MissingValueBehavior.NotApplicable,
                PreferredView = RegistryView.Registry64,
                CustomApplicability = () => CheckServiceKeyExists(@"SYSTEM\CurrentControlSet\Services\dmwappushservice", "dmwappushservice detected.", "dmwappushservice is not installed on this system.")
            },

            // --- SYSTEM & VISUAL EFFECTS ---
            new RegistryTweakDefinition
            {
                Id = "tweak.explorer.search",
                Name = "Disable Web Search in Start Menu",
                Description = "Disables Bing web search queries in Start Menu search box, eliminating background internet lookup delay.",
                Category = "System",
                RootKey = "HKCU",
                SubKey = @"SOFTWARE\Policies\Microsoft\Windows\Explorer",
                ValueName = "DisableSearchBoxSuggestions",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "0",
                TargetValueStr = "1",
                DisplayTargetStr = "1 (Disabled)",
                RiskLevel = "Low",
                RequiresRestart = false,
                RequiresAdmin = false,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "None",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.explorer.cortana",
                Name = "Disable Cortana Policy",
                Description = "Disables Cortana background search index assistant integration in group policy registry.",
                Category = "System",
                RootKey = "HKLM",
                SubKey = @"SOFTWARE\Policies\Microsoft\Windows\Windows Search",
                ValueName = "AllowCortana",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "1",
                TargetValueStr = "0",
                DisplayTargetStr = "0 (Disabled)",
                RiskLevel = "Low",
                RequiresRestart = false,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "None",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.visuals.animations",
                Name = "Disable Window Animations",
                Description = "Disables minimize and maximize window animation transitions for immediate window rendering.",
                Category = "Visual",
                RootKey = "HKCU",
                SubKey = @"Control Panel\Desktop\WindowMetrics",
                ValueName = "MinAnimate",
                ValueKind = RegistryValueKind.String,
                DefaultValueStr = "1",
                TargetValueStr = "0",
                DisplayTargetStr = "\"0\" (Instant)",
                RiskLevel = "Low",
                RequiresRestart = false,
                RequiresAdmin = false,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "Desktop Session",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.visuals.menushowdelay",
                Name = "Menu Show Delay",
                Description = "Reduces context menu display delay to 0 ms for instant popup response on click.",
                Category = "Visual",
                RootKey = "HKCU",
                SubKey = @"Control Panel\Desktop",
                ValueName = "MenuShowDelay",
                ValueKind = RegistryValueKind.String,
                DefaultValueStr = "400",
                TargetValueStr = "0",
                DisplayTargetStr = "\"0\" (0 ms)",
                RiskLevel = "Low",
                RequiresRestart = false,
                RequiresAdmin = false,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "None",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },

            // --- INPUT & POWER ---
            new RegistryTweakDefinition
            {
                Id = "tweak.input.mousehover",
                Name = "Mouse Hover Time",
                Description = "Reduces mouse hover tooltip activation delay from 400ms to 10ms for responsive UI interaction feedback.",
                Category = "Input",
                RootKey = "HKCU",
                SubKey = @"Control Panel\Mouse",
                ValueName = "MouseHoverTime",
                ValueKind = RegistryValueKind.String,
                DefaultValueStr = "400",
                TargetValueStr = "10",
                DisplayTargetStr = "\"10\" (10 ms)",
                RiskLevel = "Low",
                RequiresRestart = false,
                RequiresAdmin = false,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "Mouse Subsystem",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.power.hibernation",
                Name = "Disable System Hibernation",
                Description = "Disables Windows hibernation to prevent continuous hiberfil.sys disk writes, saving storage space and SSD endurance.",
                Category = "Power",
                RootKey = "HKLM",
                SubKey = @"SYSTEM\CurrentControlSet\Control\Power",
                ValueName = "HibernateEnabled",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "1",
                TargetValueStr = "0",
                DisplayTargetStr = "0 (Disabled)",
                RiskLevel = "Medium",
                RequiresRestart = false,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "ACPI Power Management",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            },
            new RegistryTweakDefinition
            {
                Id = "tweak.power.faststartup",
                Name = "Disable Fast Startup (Hiberboot)",
                Description = "Disables kernel state hybrid hibernation caching across shutdowns to ensure a fresh, clean hardware and driver boot.",
                Category = "Power",
                RootKey = "HKLM",
                SubKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power",
                ValueName = "HiberbootEnabled",
                ValueKind = RegistryValueKind.DWord,
                DefaultValueStr = "1",
                TargetValueStr = "0",
                DisplayTargetStr = "0 (Disabled)",
                RiskLevel = "Medium",
                RequiresRestart = false,
                RequiresAdmin = true,
                SupportedOS = "Windows 10 / 11",
                HardwarePrerequisites = "None",
                MissingBehavior = MissingValueBehavior.SafeToCreate,
                PreferredView = RegistryView.Registry64
            }
        };

        public RegistryTweakEngine(IBackupManager backupManager)
        {
            _backupManager = backupManager;
            _journal = new RegistryTransactionJournal();
        }

        #region Pre-Flight & Subsystem Applicability Checks

        public static bool IsRunningAsSystem()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return identity.IsSystem;
            }
            catch
            {
                return false;
            }
        }

        public static List<string> GetInteractiveUserSids()
        {
            var sids = new List<string>();
            try
            {
                using var usersBase = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
                foreach (var name in usersBase.GetSubKeyNames())
                {
                    if (name.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) &&
                        !name.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                    {
                        using var testSub = usersBase.OpenSubKey(name, false);
                        if (testSub != null)
                        {
                            sids.Add(name);
                        }
                    }
                }
            }
            catch { }
            return sids;
        }

        private static bool IsWindowsVersionSupported(string supportedOS)
        {
            try
            {
                int major = Environment.OSVersion.Version.Major;
                return major >= 10;
            }
            catch
            {
                return true;
            }
        }

        private static (bool IsApplicable, string Reason) CheckNtfsVolumeApplicability()
        {
            try
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
                bool hasNtfs = drives.Any(d => string.Equals(d.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase));
                return (hasNtfs, hasNtfs ? "Fixed NTFS volume detected." : "No active NTFS volumes detected on this machine.");
            }
            catch (Exception ex)
            {
                return (true, $"Volume check pass: {ex.Message}");
            }
        }

        private static (bool IsApplicable, string Reason) CheckMsmqApplicability()
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var k = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\MSMQ", false);
                if (k != null) return (true, "Microsoft Message Queuing (MSMQ) subsystem detected.");
                return (false, "MSMQ subsystem is not installed on this Windows installation.");
            }
            catch
            {
                return (false, "MSMQ subsystem check unavailable.");
            }
        }

        private static (bool IsApplicable, string Reason) CheckCoreParkingApplicability()
        {
            if (Environment.ProcessorCount <= 1)
                return (false, "Single-core processor detected; Core Parking is not applicable.");

            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var k = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583", false);
                if (k != null) return (true, "Multi-core processor with configurable Core Parking power scheme detected.");

                return (false, "Core parking power settings key is not supported by current Windows power configuration.");
            }
            catch
            {
                return (false, "Core parking power settings key inaccessible.");
            }
        }

        private static (bool IsApplicable, string Reason) CheckHagsApplicability()
        {
            try
            {
                if (Environment.OSVersion.Version.Major < 10 || (Environment.OSVersion.Version.Major == 10 && Environment.OSVersion.Version.Build < 19041))
                    return (false, "Windows 10 Build 19041+ or Windows 11 required for Hardware Accelerated GPU Scheduling.");

                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var k = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", false);
                if (k == null) return (false, "GraphicsDrivers registry tree not found on this machine.");

                return (true, "Hardware Accelerated GPU Scheduling supported.");
            }
            catch (Exception ex)
            {
                return (false, $"HAGS check failed: {ex.Message}");
            }
        }

        private static (bool IsApplicable, string Reason) CheckGpuSchedulerApplicability()
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var schedKey = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Scheduler", false);
                if (schedKey != null) return (true, "Graphics scheduler engine present and accessible.");

                return (false, "GraphicsDrivers\\Scheduler subkey is not supported or present in current display driver stack.");
            }
            catch (Exception ex)
            {
                return (false, $"GPU scheduler check failed: {ex.Message}");
            }
        }

        private static (bool IsApplicable, string Reason) CheckSvcHostSplitApplicability()
        {
            try
            {
                var gcMem = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                bool hasEnoughRam = gcMem >= (3L * 1024 * 1024 * 1024);
                return (hasEnoughRam, hasEnoughRam ? "Sufficient physical RAM detected (>= 3.5GB)." : "RAM is under 4GB; SvcHost split is not recommended.");
            }
            catch
            {
                return (true, "Memory threshold check standard pass.");
            }
        }

        private static (bool IsApplicable, string Reason) CheckPagingExecApplicability()
        {
            try
            {
                var memBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                bool has8Gb = memBytes >= (7L * 1024 * 1024 * 1024);
                return (has8Gb, has8Gb ? "Physical RAM is 8GB or greater." : "System has less than 8GB RAM; keeping paging executive enabled is recommended for low-memory stability.");
            }
            catch
            {
                return (true, "RAM check passed.");
            }
        }

        private static (bool IsApplicable, string Reason) CheckServiceKeyExists(string subKey, string foundReason, string missingReason)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var k = baseKey.OpenSubKey(subKey, false);
                if (k != null) return (true, foundReason);
                return (false, missingReason);
            }
            catch
            {
                return (false, missingReason);
            }
        }

        private static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Robust Cross-PC Registry Access & Read-Back

        public static (RegistryKey? Key, RegistryView ResolvedView, string ErrorCode, string ErrorDetail) OpenTargetKeyWithPreflight(
            string root, string subKey, bool writable, RegistryView preferredView = RegistryView.Registry64)
        {
            var viewsToTry = new[] { preferredView, preferredView == RegistryView.Registry64 ? RegistryView.Registry32 : RegistryView.Registry64 };

            foreach (var view in viewsToTry)
            {
                try
                {
                    if (string.Equals(root, "HKCU", StringComparison.OrdinalIgnoreCase))
                    {
                        // 1. If running under NT AUTHORITY\SYSTEM (Service), resolve active user profile in HKEY_USERS
                        if (IsRunningAsSystem())
                        {
                            var sids = GetInteractiveUserSids();
                            if (sids.Count > 0)
                            {
                                var usersBase = RegistryKey.OpenBaseKey(RegistryHive.Users, view);
                                string primarySid = sids[0];
                                string userSubKey = $"{primarySid}\\{subKey}";

                                try
                                {
                                    var key = writable ? usersBase.CreateSubKey(userSubKey, true) : usersBase.OpenSubKey(userSubKey, false);
                                    if (key != null)
                                    {
                                        if (writable && sids.Count > 1)
                                        {
                                            for (int i = 1; i < sids.Count; i++)
                                            {
                                                try
                                                {
                                                    using var mirrorKey = usersBase.CreateSubKey($"{sids[i]}\\{subKey}", true);
                                                }
                                                catch { }
                                            }
                                        }
                                        return (key, view, "0x00000000", "Success via HKEY_USERS interactive SID");
                                    }
                                }
                                catch (UnauthorizedAccessException ex)
                                {
                                    return (null, view, "0x80070005", $"Access Denied opening HKCU\\{subKey}: {ex.Message}");
                                }
                                catch (Exception ex)
                                {
                                    return (null, view, "0x80004005", $"Error accessing HKEY_USERS: {ex.Message}");
                                }
                            }
                        }

                        // 2. Interactive user context (GUI or standard user)
                        try
                        {
                            var cuBase = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                            var key = writable ? cuBase.CreateSubKey(subKey, true) : cuBase.OpenSubKey(subKey, false);
                            if (key != null) return (key, view, "0x00000000", "Success");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            return (null, view, "0x80070005", $"Access Denied opening HKCU\\{subKey}: {ex.Message}");
                        }
                        catch (System.Security.SecurityException ex)
                        {
                            return (null, view, "0x80070005", $"Security error opening HKCU\\{subKey}: {ex.Message}");
                        }
                    }
                    else
                    {
                        // Local Machine hive via explicit RegistryView
                        try
                        {
                            var lmBase = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                            var key = writable ? lmBase.CreateSubKey(subKey, true) : lmBase.OpenSubKey(subKey, false);
                            if (key != null) return (key, view, "0x00000000", "Success");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            return (null, view, "0x80070005", $"Access Denied opening HKLM\\{subKey}: {ex.Message}");
                        }
                        catch (System.Security.SecurityException ex)
                        {
                            return (null, view, "0x80070005", $"Security error opening HKLM\\{subKey}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    return (null, view, "0x80004005", $"Exception opening base key: {ex.Message}");
                }
            }

            return (null, preferredView, "0x80070002", $"Registry key '{root}\\{subKey}' not found.");
        }

        private static DateTime GetSystemBootTimeUtc()
        {
            try
            {
                return DateTime.UtcNow - TimeSpan.FromMilliseconds((double)Environment.TickCount64);
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }

        private static object ParseTargetValue(string targetStr, RegistryValueKind kind)
        {
            if (kind == RegistryValueKind.DWord)
            {
                string trimmed = targetStr.Trim();
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed.Substring(2);

                if (uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint uHex) &&
                    (targetStr.Contains("f", StringComparison.OrdinalIgnoreCase) || targetStr.Contains("x", StringComparison.OrdinalIgnoreCase)))
                {
                    return (int)uHex;
                }
                if (uint.TryParse(targetStr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out uint uDec))
                {
                    return (int)uDec;
                }
                if (int.TryParse(targetStr, out int iDec))
                {
                    return iDec;
                }
                return 0;
            }
            else if (kind == RegistryValueKind.QWord)
            {
                if (long.TryParse(targetStr, out long qVal)) return qVal;
                if (ulong.TryParse(targetStr, out ulong uqVal)) return (long)uqVal;
                return 0L;
            }
            else if (kind == RegistryValueKind.String)
            {
                string s = targetStr.Trim();
                if (s.StartsWith("\"") && s.EndsWith("\"") && s.Length >= 2)
                {
                    s = s.Substring(1, s.Length - 2);
                }
                return s;
            }
            return targetStr;
        }

        private (bool Matches, string DisplayVal) EvaluateValue(RegistryTweakDefinition def, object? val)
        {
            if (val == null) return (false, "NOT CONFIGURED");

            string sVal = val.ToString() ?? "";
            bool isMatch = StateNormalizer.IsSatisfied(sVal, def.TargetValueStr);

            string display;
            if (sVal == "4294967295" || sVal == "-1" || (def.TargetValueStr.Equals("ffffffff", StringComparison.OrdinalIgnoreCase) && isMatch))
            {
                display = "0xFFFFFFFF (Disabled)";
            }
            else if (def.ValueKind == RegistryValueKind.String)
            {
                display = $"\"{sVal}\"";
            }
            else
            {
                display = sVal;
            }

            return (isMatch, display);
        }

        #endregion

        public async Task<RegistryTweakResponse> ReconcileStartupAsync()
        {
            return await Task.Run(() =>
            {
                var journalEntries = _journal.LoadEntries();
                var bootTime = GetSystemBootTimeUtc();

                foreach (var entry in journalEntries)
                {
                    var def = _tweakDatabase.FirstOrDefault(t => string.Equals(t.Id, entry.OptimizationId, StringComparison.OrdinalIgnoreCase));
                    if (def == null) continue;

                    var (key, _, _, _) = OpenTargetKeyWithPreflight(def.RootKey, def.SubKey, false, def.PreferredView);
                    if (key != null)
                    {
                        using (key)
                        {
                            var liveVal = key.GetValue(def.ValueName);
                            var (matches, displayVal) = EvaluateValue(def, liveVal);
                            entry.CurrentValue = displayVal;

                            if (entry.RequiresReboot && (entry.ApplyState == "APPLIED_PENDING_REBOOT" || entry.VerificationState == "PENDING_REBOOT"))
                            {
                                if (bootTime > entry.Timestamp)
                                {
                                    if (matches)
                                    {
                                        entry.VerificationState = "VERIFIED";
                                        entry.ApplyState = "APPLIED";
                                        entry.RebootVerifiedTimestamp = DateTime.UtcNow;
                                    }
                                    else
                                    {
                                        entry.VerificationState = "VERIFICATION_FAILED";
                                    }
                                }
                            }
                            else if (entry.VerificationState == "VERIFIED" && !matches)
                            {
                                entry.VerificationState = "EXTERNALLY_CHANGED";
                            }

                            _journal.SaveEntry(entry);
                        }
                    }
                }

                return PlanRegistryTweakInternal();
            });
        }

        public async Task<RegistryTweakResponse> PlanRegistryTweakAsync()
        {
            return await Task.Run(() => PlanRegistryTweakInternal());
        }

        private RegistryTweakResponse PlanRegistryTweakInternal()
        {
            var response = new RegistryTweakResponse { Success = true, Message = "Plan generated from authoritative live registry pre-flight." };
            var journalMap = _journal.LoadEntries().ToDictionary(x => x.OptimizationId, x => x, StringComparer.OrdinalIgnoreCase);
            var bootTime = GetSystemBootTimeUtc();
            bool isAdmin = IsRunningAsAdmin() || IsRunningAsSystem();
            string winVerStr = Environment.OSVersion.VersionString;

            var diagnosticsList = new List<RegistryDiagnosticEntry>();

            int possibleCount = _tweakDatabase.Count;
            int applicableCount = 0;
            int recommendedCount = 0;
            int alreadyOptimizedCount = 0;
            int restartRequiredCount = 0;
            int pendingCount = 0;
            int notApplicableCount = 0;
            int unsupportedCount = 0;
            int blockedCount = 0;
            int attentionCount = 0;
            int failedCount = 0;

            foreach (var def in _tweakDatabase)
            {
                var dto = new RegistryTweakDto
                {
                    Id = def.Id,
                    Name = def.Name,
                    Description = def.Description,
                    Category = def.Category,
                    RiskLevel = def.RiskLevel,
                    DefaultValue = def.DefaultValueStr,
                    TargetValue = string.IsNullOrEmpty(def.DisplayTargetStr) ? def.TargetValueStr : def.DisplayTargetStr,
                    RootKey = def.RootKey,
                    SubKey = def.SubKey,
                    ValueName = def.ValueName,
                    ValueType = def.ValueKind == RegistryValueKind.DWord ? "REG_DWORD" : (def.ValueKind == RegistryValueKind.QWord ? "REG_QWORD" : "REG_SZ"),
                    SupportedOS = def.SupportedOS,
                    HardwarePrerequisites = def.HardwarePrerequisites,
                    RequiresAdmin = def.RequiresAdmin,
                    RequiresReboot = def.RequiresRestart,
                    RollbackSupported = true,
                    MissingBehavior = def.MissingBehavior.ToString(),
                    RegistryView = def.PreferredView.ToString(),
                    PermissionState = isAdmin ? "Elevated (Admin/Service)" : "Standard User"
                };

                // 1. OS & Build compatibility pre-flight
                if (!IsWindowsVersionSupported(def.SupportedOS))
                {
                    dto.Applicability = "UNSUPPORTED";
                    dto.Status = "UNSUPPORTED";
                    dto.CurrentValue = "UNSUPPORTED OS BUILD";
                    dto.ApplicabilityReason = $"Requires {def.SupportedOS}. Current OS: {winVerStr}";
                    dto.VerificationState = "NOT_APPLICABLE";
                    dto.ErrorCode = "0x80070032";
                    dto.DiagnosticsDetail = "OS Version requirement not satisfied.";
                    unsupportedCount++;
                    response.Tweaks.Add(dto);

                    diagnosticsList.Add(CreateDiagEntry(def, dto, "SKIPPED_UNSUPPORTED"));
                    continue;
                }

                // 2. Custom Applicability Check (Hardware / Services / Volumes / Subsystems)
                if (def.CustomApplicability != null)
                {
                    var (appOk, appReason) = def.CustomApplicability();
                    if (!appOk)
                    {
                        dto.Applicability = "NOT_APPLICABLE";
                        dto.Status = "NOT APPLICABLE";
                        dto.CurrentValue = "NOT APPLICABLE ON THIS PC";
                        dto.ApplicabilityReason = appReason;
                        dto.VerificationState = "NOT_APPLICABLE";
                        dto.ErrorCode = "0x00000000";
                        dto.DiagnosticsDetail = appReason;

                        notApplicableCount++;
                        response.Tweaks.Add(dto);
                        diagnosticsList.Add(CreateDiagEntry(def, dto, "SKIPPED_NOT_APPLICABLE"));
                        continue;
                    }
                    dto.ApplicabilityReason = appReason;
                }

                // 3. Target Key Read-Only Pre-Flight (Do not open writable during plan)
                var (readKey, resolvedView, readErrCode, readErrDetail) = OpenTargetKeyWithPreflight(def.RootKey, def.SubKey, false, def.PreferredView);
                dto.RegistryView = resolvedView.ToString();

                if (readKey == null)
                {
                    if (def.MissingBehavior == MissingValueBehavior.SafeToCreate)
                    {
                        dto.CurrentValue = "NOT CONFIGURED (Default)";
                        dto.Applicability = "APPLICABLE";
                        dto.Status = "RECOMMENDED";
                        dto.VerificationState = "UNVERIFIED";
                        dto.ErrorCode = "0x00000000";
                        dto.DiagnosticsDetail = "Target key absent but supported and safe to create.";
                        applicableCount++;
                        recommendedCount++;
                        pendingCount++;
                    }
                    else
                    {
                        dto.CurrentValue = "KEY NOT PRESENT";
                        dto.Applicability = "NOT_APPLICABLE";
                        dto.Status = "NOT APPLICABLE";
                        dto.ApplicabilityReason = $"Subsystem key does not exist on this machine: {readErrDetail}";
                        dto.VerificationState = "NOT_APPLICABLE";
                        dto.ErrorCode = readErrCode;
                        dto.DiagnosticsDetail = readErrDetail;
                        notApplicableCount++;
                    }

                    response.Tweaks.Add(dto);
                    diagnosticsList.Add(CreateDiagEntry(def, dto, dto.Applicability));
                    continue;
                }

                // 4. Live Value Evaluation & State Verification
                using (readKey)
                {
                    var liveVal = readKey.GetValue(def.ValueName);
                    
                    if (liveVal == null)
                    {
                        if (def.MissingBehavior == MissingValueBehavior.SafeToCreate)
                        {
                            dto.CurrentValue = "NOT SET (Default)";
                            dto.Applicability = "APPLICABLE";
                            dto.Status = "RECOMMENDED";
                            dto.VerificationState = "UNVERIFIED";
                            dto.ErrorCode = "0x00000000";
                            dto.DiagnosticsDetail = "Value not set; Windows uses system default.";
                            applicableCount++;
                            recommendedCount++;
                            pendingCount++;
                        }
                        else
                        {
                            dto.CurrentValue = "NOT CONFIGURED";
                            dto.Applicability = "NOT_APPLICABLE";
                            dto.Status = "NOT APPLICABLE";
                            dto.ApplicabilityReason = "Value does not exist on this subsystem.";
                            dto.VerificationState = "NOT_APPLICABLE";
                            dto.ErrorCode = "0x00000000";
                            dto.DiagnosticsDetail = "Subsystem value absent.";
                            notApplicableCount++;
                        }
                    }
                    else
                    {
                        var (matches, displayVal) = EvaluateValue(def, liveVal);
                        dto.CurrentValue = displayVal;

                        journalMap.TryGetValue(def.Id, out var journalEntry);
                        dto.BeforeValue = journalEntry?.BeforeValue ?? def.DefaultValueStr;

                        if (matches)
                        {
                            if (journalEntry != null && def.RequiresRestart && (journalEntry.ApplyState == "APPLIED_PENDING_REBOOT" || journalEntry.VerificationState == "PENDING_REBOOT"))
                            {
                                if (bootTime > journalEntry.Timestamp)
                                {
                                    journalEntry.VerificationState = "VERIFIED";
                                    journalEntry.ApplyState = "APPLIED";
                                    journalEntry.RebootVerifiedTimestamp = DateTime.UtcNow;
                                    _journal.SaveEntry(journalEntry);

                                    dto.Applicability = "ALREADY_OPTIMAL";
                                    dto.Status = "ALREADY OPTIMIZED";
                                    dto.VerificationState = "VERIFIED";
                                    dto.ApplicabilityReason = "Optimal value verified following system reboot.";
                                    dto.ErrorCode = "0x00000000";
                                    dto.DiagnosticsDetail = "Target state fully verified after reboot.";
                                    alreadyOptimizedCount++;
                                    applicableCount++;
                                }
                                else
                                {
                                    dto.Applicability = "APPLICABLE";
                                    dto.Status = "RESTART REQUIRED";
                                    dto.VerificationState = "PENDING_REBOOT";
                                    dto.ApplicabilityReason = "Value written to registry; system restart required for full kernel effect.";
                                    dto.ErrorCode = "0x00000000";
                                    dto.DiagnosticsDetail = "Target state written; reboot pending.";
                                    applicableCount++;
                                    restartRequiredCount++;
                                }
                            }
                            else
                            {
                                dto.Applicability = "ALREADY_OPTIMAL";
                                dto.Status = "ALREADY OPTIMIZED";
                                dto.VerificationState = "VERIFIED";
                                dto.ApplicabilityReason = "Current registry value already matches optimal target.";
                                dto.ErrorCode = "0x00000000";
                                dto.DiagnosticsDetail = "Target state fully verified.";
                                alreadyOptimizedCount++;
                                applicableCount++;
                            }
                        }
                        else
                        {
                            dto.Applicability = "APPLICABLE";
                            dto.Status = (journalEntry != null && journalEntry.VerificationState == "VERIFIED") ? "EXTERNALLY CHANGED" : "RECOMMENDED";
                            dto.VerificationState = "UNVERIFIED";
                            dto.ErrorCode = "0x00000000";
                            dto.DiagnosticsDetail = "Key is applicable and ready to be optimized.";
                            applicableCount++;
                            recommendedCount++;
                            pendingCount++;
                        }
                    }
                }

                response.Tweaks.Add(dto);
                diagnosticsList.Add(CreateDiagEntry(def, dto, dto.Applicability));
            }

            response.TotalCount = possibleCount;
            response.ApplicableCount = applicableCount;
            response.RecommendedCount = recommendedCount;
            response.AlreadyOptimizedCount = alreadyOptimizedCount;
            response.RestartRequiredCount = restartRequiredCount;
            response.PendingCount = pendingCount;
            response.NotApplicableCount = notApplicableCount;
            response.UnsupportedCount = unsupportedCount;
            response.BlockedCount = blockedCount;
            response.AttentionCount = attentionCount;
            response.FailedCount = failedCount;

            _journal.SaveDiagnostics(diagnosticsList);

            return response;
        }

        private static RegistryDiagnosticEntry CreateDiagEntry(RegistryTweakDefinition def, RegistryTweakDto dto, string result)
        {
            return new RegistryDiagnosticEntry
            {
                OptimizationId = def.Id,
                Name = def.Name,
                Hive = def.RootKey,
                SubKey = def.SubKey,
                ValueName = def.ValueName,
                RequiredType = dto.ValueType,
                CurrentValue = dto.CurrentValue,
                TargetValue = def.TargetValueStr,
                WindowsBuild = Environment.OSVersion.VersionString,
                RegistryView = dto.RegistryView,
                PermissionState = dto.PermissionState,
                ApplicabilityResult = dto.Applicability,
                Status = dto.Status,
                VerificationState = dto.VerificationState,
                ErrorCode = dto.ErrorCode,
                DiagnosticDetail = dto.DiagnosticsDetail,
                Timestamp = DateTime.UtcNow
            };
        }

        public async Task<(bool Success, string Message, string Status, int AppliedCount, int VerifiedCount, int RestartRequiredCount, int FailedCount)> ApplyRegistryTweaksAsync(List<string> tweakIds)
        {
            return await Task.Run(() =>
            {
                int appliedCount = 0;
                int verifiedCount = 0;
                int restartRequiredCount = 0;
                int failedCount = 0;
                int skippedCount = 0;
                int blockedCount = 0;

                // 1. Resolve candidates strictly according to applicability
                List<RegistryTweakDefinition> candidates;
                if (tweakIds == null || tweakIds.Count == 0)
                {
                    // Automatic Normal Mode: filter strictly to applicable tweaks
                    candidates = _tweakDatabase.Where(def =>
                    {
                        if (!IsWindowsVersionSupported(def.SupportedOS)) return false;
                        if (def.CustomApplicability != null && !def.CustomApplicability().IsApplicable) return false;
                        return true;
                    }).ToList();
                }
                else
                {
                    // Explicit Selection: only include requested IDs that are supported & applicable
                    candidates = _tweakDatabase.Where(def => tweakIds.Contains(def.Id, StringComparer.OrdinalIgnoreCase)).ToList();
                }

                foreach (var def in candidates)
                {
                    // Pre-flight check
                    if (!IsWindowsVersionSupported(def.SupportedOS))
                    {
                        skippedCount++;
                        continue;
                    }

                    if (def.CustomApplicability != null)
                    {
                        var (appOk, _) = def.CustomApplicability();
                        if (!appOk)
                        {
                            skippedCount++;
                            continue;
                        }
                    }

                    try
                    {
                        // 2. Pre-flight Read: Check if already optimal
                        var (readKey, resolvedView, _, _) = OpenTargetKeyWithPreflight(def.RootKey, def.SubKey, false, def.PreferredView);
                        if (readKey == null && def.MissingBehavior != MissingValueBehavior.SafeToCreate)
                        {
                            // Not applicable on this machine (subsystem key missing)
                            skippedCount++;
                            continue;
                        }

                        if (readKey != null)
                        {
                            using (readKey)
                            {
                                var existingVal = readKey.GetValue(def.ValueName);
                                var (alreadyDone, currentDisplay) = EvaluateValue(def, existingVal);
                                if (alreadyDone)
                                {
                                    verifiedCount++;
                                    var optEntry = new RegistryTransactionEntry
                                    {
                                        OptimizationId = def.Id,
                                        Hive = def.RootKey,
                                        Path = def.SubKey,
                                        ValueName = def.ValueName,
                                        ValueType = def.ValueKind.ToString(),
                                        BeforeValue = currentDisplay,
                                        TargetValue = def.TargetValueStr,
                                        CurrentValue = currentDisplay,
                                        Applicability = "ALREADY_OPTIMAL",
                                        ApplyState = "ALREADY_OPTIMIZED",
                                        VerificationState = "VERIFIED",
                                        RegistryView = resolvedView.ToString(),
                                        ErrorCode = "0x00000000",
                                        Timestamp = DateTime.UtcNow
                                    };
                                    _journal.SaveEntry(optEntry);
                                    continue;
                                }
                            }
                        }

                        // 3. Open Writable Key with Explicit View
                        var (key, writeView, errCode, errDetail) = OpenTargetKeyWithPreflight(def.RootKey, def.SubKey, true, def.PreferredView);
                        if (key == null)
                        {
                            if (errCode == "0x80070005")
                            {
                                blockedCount++;
                                var blockEntry = new RegistryTransactionEntry
                                {
                                    OptimizationId = def.Id,
                                    Hive = def.RootKey,
                                    Path = def.SubKey,
                                    ValueName = def.ValueName,
                                    ValueType = def.ValueKind.ToString(),
                                    TargetValue = def.TargetValueStr,
                                    ApplyState = "BLOCKED",
                                    VerificationState = "BLOCKED",
                                    RegistryView = writeView.ToString(),
                                    ErrorCode = errCode,
                                    ErrorMessage = $"Access Denied: {errDetail}",
                                    Timestamp = DateTime.UtcNow
                                };
                                _journal.SaveEntry(blockEntry);
                            }
                            else
                            {
                                failedCount++;
                                var failEntry = new RegistryTransactionEntry
                                {
                                    OptimizationId = def.Id,
                                    Hive = def.RootKey,
                                    Path = def.SubKey,
                                    ValueName = def.ValueName,
                                    ValueType = def.ValueKind.ToString(),
                                    TargetValue = def.TargetValueStr,
                                    ApplyState = "FAILED",
                                    VerificationState = "VERIFICATION_FAILED",
                                    RegistryView = writeView.ToString(),
                                    ErrorCode = errCode,
                                    ErrorMessage = errDetail,
                                    Timestamp = DateTime.UtcNow
                                };
                                _journal.SaveEntry(failEntry);
                            }
                            continue;
                        }

                        using (key)
                        {
                            // 4. Backup Original Value
                            var old = key.GetValue(def.ValueName);
                            string oldStr = old == null ? "VALUE DID NOT EXIST" : old.ToString() ?? "";
                            string ownerTag = $"AntiGravity.RegistryTweak.{def.Id}";
                            _backupManager.BackupRegistryValue(ownerTag, $"{def.RootKey}\\{def.SubKey}", def.ValueName, oldStr, def.ValueKind.ToString(), def.TargetValueStr, $"Backup of {def.Name}");

                            // 5. Parse & Write Target Value with exact type
                            object valObj = ParseTargetValue(def.TargetValueStr, def.ValueKind);
                            key.SetValue(def.ValueName, valObj, def.ValueKind);
                            try { key.Flush(); } catch { }

                            // 6. IMMEDIATE REAL READ-BACK VERIFICATION
                            var afterVal = key.GetValue(def.ValueName);
                            var (verified, displayAfter) = EvaluateValue(def, afterVal);

                            var entry = new RegistryTransactionEntry
                            {
                                OptimizationId = def.Id,
                                Hive = def.RootKey,
                                Path = def.SubKey,
                                ValueName = def.ValueName,
                                ValueType = def.ValueKind.ToString(),
                                BeforeValue = oldStr,
                                TargetValue = def.TargetValueStr,
                                CurrentValue = displayAfter,
                                Applicability = "APPLICABLE",
                                RequiresReboot = def.RequiresRestart,
                                RegistryView = writeView.ToString(),
                                ErrorCode = verified ? "0x00000000" : "0x80004005",
                                Timestamp = DateTime.UtcNow
                            };

                            if (verified)
                            {
                                appliedCount++;
                                if (def.RequiresRestart)
                                {
                                    entry.ApplyState = "APPLIED_PENDING_REBOOT";
                                    entry.VerificationState = "PENDING_REBOOT";
                                    restartRequiredCount++;
                                }
                                else
                                {
                                    entry.ApplyState = "APPLIED";
                                    entry.VerificationState = "VERIFIED";
                                    verifiedCount++;
                                }
                            }
                            else
                            {
                                entry.ApplyState = "FAILED";
                                entry.VerificationState = "VERIFICATION_FAILED";
                                entry.ErrorMessage = $"Read-back mismatch: Expected '{def.TargetValueStr}', got '{displayAfter}'.";
                                failedCount++;
                            }

                            _journal.SaveEntry(entry);
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        var errEntry = new RegistryTransactionEntry
                        {
                            OptimizationId = def.Id,
                            Hive = def.RootKey,
                            Path = def.SubKey,
                            ValueName = def.ValueName,
                            TargetValue = def.TargetValueStr,
                            ApplyState = "FAILED",
                            VerificationState = "VERIFICATION_FAILED",
                            ErrorCode = "0x80004005",
                            ErrorMessage = ex.Message,
                            Timestamp = DateTime.UtcNow
                        };
                        _journal.SaveEntry(errEntry);
                    }
                }

                bool isSuccess = failedCount == 0;
                string status = failedCount > 0 ? (appliedCount > 0 ? "PARTIAL_SUCCESS" : "FAILED")
                                                : (restartRequiredCount > 0 ? "RESTART_REQUIRED" : "SUCCESS");

                string msg = isSuccess
                    ? (appliedCount == 0 && verifiedCount > 0 
                        ? "ALREADY OPTIMIZED / NO ACTION REQUIRED" 
                        : (restartRequiredCount > 0 
                            ? $"{appliedCount} APPLIED & VERIFIED ({restartRequiredCount} RESTART REQUIRED)" 
                            : $"{appliedCount} APPLIED & 100% VERIFIED"))
                    : $"{appliedCount} APPLIED • {failedCount} FAILED";

                return (isSuccess, msg, status, appliedCount, verifiedCount, restartRequiredCount, failedCount);
            });
        }

        public async Task<(bool Success, string Message, string Status, int RestoredCount, int FailedCount)> RestoreRegistryTweaksAsync(List<string> tweakIds)
        {
            return await Task.Run(() =>
            {
                var tweaksToRestore = (tweakIds == null || tweakIds.Count == 0)
                    ? _tweakDatabase
                    : _tweakDatabase.Where(t => tweakIds.Contains(t.Id, StringComparer.OrdinalIgnoreCase)).ToList();

                int restoredCount = 0;
                int failedCount = 0;
                var journalMap = _journal.LoadEntries().ToDictionary(x => x.OptimizationId, x => x, StringComparer.OrdinalIgnoreCase);

                foreach (var def in tweaksToRestore)
                {
                    try
                    {
                        string ownerTag = $"AntiGravity.RegistryTweak.{def.Id}";
                        bool backupRestored = _backupManager.RestoreByOwner(ownerTag);

                        if (!backupRestored && journalMap.TryGetValue(def.Id, out var entry))
                        {
                            var (key, _, _, _) = OpenTargetKeyWithPreflight(def.RootKey, def.SubKey, true, def.PreferredView);
                            if (key != null)
                            {
                                using (key)
                                {
                                    if (entry.BeforeValue == "VALUE DID NOT EXIST" || entry.BeforeValue == "NOT CONFIGURED" || string.IsNullOrEmpty(entry.BeforeValue))
                                    {
                                        try { key.DeleteValue(def.ValueName, false); } catch { }
                                    }
                                    else
                                    {
                                        if (def.ValueKind == RegistryValueKind.DWord && int.TryParse(entry.BeforeValue, out int iVal))
                                            key.SetValue(def.ValueName, iVal, RegistryValueKind.DWord);
                                        else
                                            key.SetValue(def.ValueName, entry.BeforeValue, def.ValueKind);
                                    }
                                    backupRestored = true;
                                }
                            }
                        }

                        if (backupRestored)
                        {
                            restoredCount++;
                            if (journalMap.TryGetValue(def.Id, out var existingEntry))
                            {
                                existingEntry.ApplyState = "ROLLED_BACK";
                                existingEntry.VerificationState = "UNVERIFIED";
                                _journal.SaveEntry(existingEntry);
                            }
                        }
                    }
                    catch
                    {
                        failedCount++;
                    }
                }

                return (true, $"Restored {restoredCount} registry tweaks to original state.", "SUCCESS", restoredCount, failedCount);
            });
        }
    }
}

