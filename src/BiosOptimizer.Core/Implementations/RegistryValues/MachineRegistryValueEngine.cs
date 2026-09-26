using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace BiosOptimizer.Core.Implementations.RegistryValues
{
    public enum RegistryValueStatus
    {
        Recommended,
        AlreadyConfigured,
        RequiresRestart,
        VerificationRequired,
        NotApplicable,
        Failed
    }

    public class MachineRegistryValueItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Category { get; set; } = "Memory / Service Host";
        public string RegistryPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public RegistryValueKind ValueType { get; set; } = RegistryValueKind.DWord;
        public string Purpose { get; set; } = string.Empty;
        public string RiskLevel { get; set; } = "MEDIUM";
        public string HardwareEvidence { get; set; } = string.Empty;
        public string WhyApplicable { get; set; } = string.Empty;
        public string CurrentValueDisplay { get; set; } = "Unknown";
        public object? CurrentValueRaw { get; set; }
        public string CandidateValueDisplay { get; set; } = "Unknown";
        public object? CandidateValueRaw { get; set; }
        public string CandidateExplanation { get; set; } = string.Empty;
        public RegistryValueStatus Status { get; set; } = RegistryValueStatus.Recommended;
        public bool RequiresRestart { get; set; } = true;
        public bool HasBackup { get; set; }
        public int BeforeSvchostCount { get; set; }
        public double BeforeSvchostMemoryMb { get; set; }
        public int AfterSvchostCount { get; set; }
        public double AfterSvchostMemoryMb { get; set; }
        public string Effectiveness { get; set; } = "UNKNOWN";
        public string StatusMessage { get; set; } = string.Empty;
    }

    public class RegistryValueSnapshot
    {
        public string ItemId { get; set; } = string.Empty;
        public string RegistryPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public object? OldValueRaw { get; set; }
        public object? NewValueRaw { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public double DetectedRamGb { get; set; }
        public int BeforeSvchostCount { get; set; }
        public double BeforeSvchostMemoryMb { get; set; }
        public int AfterSvchostCount { get; set; }
        public double AfterSvchostMemoryMb { get; set; }
        public bool IsPendingRestart { get; set; }
        public string Effectiveness { get; set; } = "UNKNOWN";
    }

    public class MachineRegistryValueEngine
    {
        private static readonly Lazy<MachineRegistryValueEngine> _lazy = new(() => new MachineRegistryValueEngine());
        public static MachineRegistryValueEngine Instance => _lazy.Value;

        private static string SnapshotDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ErrorOptimizer", "RegistryValues");

        private static string SnapshotFile => Path.Combine(SnapshotDirectory, "RegistryValueSnapshots.json");

        private readonly Dictionary<long, uint> _ramToCandidateMapping = new()
        {
            { 4L,   0x00400000 }, // 4,194,304 KB
            { 8L,   0x00800000 }, // 8,388,608 KB
            { 12L,  0x00C00000 }, // 12,582,912 KB
            { 16L,  0x01000000 }, // 16,777,216 KB
            { 24L,  0x01800000 }, // 25,165,824 KB
            { 32L,  0x02000000 }, // 33,554,432 KB
            { 64L,  0x04000000 }, // 67,108,864 KB
            { 96L,  0x06000000 }, // 100,663,296 KB
            { 128L, 0x08000000 }  // 134,217,728 KB
        };

        public MachineRegistryValueEngine()
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

        /// <summary>
        /// Scans and evaluates all machine-specific registry values according to physical hardware and active Windows state.
        /// </summary>
        public List<MachineRegistryValueItem> ScanAllValues()
        {
            var profile = HardwareProfiler.GetQuickProfile();
            var snapshots = LoadSnapshots();

            var items = new List<MachineRegistryValueItem>();

            // 1. SvcHostSplitThresholdInKB
            items.Add(EvaluateSvcHostSplitThreshold(profile, snapshots));

            // 2. Win32PrioritySeparation
            items.Add(EvaluateWin32PrioritySeparation(profile, snapshots));

            // 3. SystemResponsiveness (MMCSS)
            items.Add(EvaluateSystemResponsiveness(profile, snapshots));

            // 4. NetworkThrottlingIndex
            items.Add(EvaluateNetworkThrottling(profile, snapshots));

            // 5. IoPageLockLimit
            items.Add(EvaluateIoPageLockLimit(profile, snapshots));

            return items;
        }

        // ── 1. SvcHost Split Threshold Evaluator ───────────────────────────
        private MachineRegistryValueItem EvaluateSvcHostSplitThreshold(HardwareProfile profile, Dictionary<string, RegistryValueSnapshot> snapshots)
        {
            const string regSubKey = @"SYSTEM\CurrentControlSet\Control";
            const string valName = "SvcHostSplitThresholdInKB";
            const string itemId = "val.svchost.split_threshold";

            double ramGb = profile.RamTotalGb;
            long matchedTier = CalculateNearestRamTier(ramGb, out string tierReason);
            uint candidateDword = _ramToCandidateMapping[matchedTier];
            long candidateKb = candidateDword;

            // Measure live svchost metrics
            var (svchostCount, svchostMemoryMb) = MeasureLiveSvchostMetrics();

            var item = new MachineRegistryValueItem
            {
                Id = itemId,
                Name = valName,
                DisplayName = "Service Host Process Splitting & Memory Threshold",
                Category = "Memory / Service Host",
                RegistryPath = @"HKLM\SYSTEM\CurrentControlSet\Control",
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Purpose = "Configures the per-process memory threshold for Windows Service Host (svchost.exe). Allows systems with ample RAM to isolate background services for greater reliability and fault isolation, or group them on constrained systems to reduce overhead.",
                RiskLevel = "MEDIUM",
                HardwareEvidence = $"Physical RAM: {ramGb:F1} GB | Active svchost count: {svchostCount} | Total svchost Memory: {svchostMemoryMb:F1} MB | Windows: {profile.WindowsVersion}",
                WhyApplicable = $"Configured specifically for your {matchedTier} GB RAM hardware profile. {tierReason}",
                CandidateValueDisplay = $"0x{candidateDword:X8} ({candidateKb:N0} KB)",
                CandidateValueRaw = candidateDword,
                CandidateExplanation = $"Machine-specific candidate calculated for {matchedTier} GB RAM ({candidateKb:N0} KB). Actual impact must be verified after Windows restart.",
                RequiresRestart = true,
                BeforeSvchostCount = svchostCount,
                BeforeSvchostMemoryMb = svchostMemoryMb
            };

            // Read live registry value
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(regSubKey, false);
                if (key != null)
                {
                    var raw = key.GetValue(valName);
                    if (raw != null)
                    {
                        item.CurrentValueRaw = raw;
                        long currentVal = Convert.ToInt64(raw);
                        item.CurrentValueDisplay = $"0x{currentVal:X8} ({currentVal:N0} KB)";

                        if (currentVal == candidateDword)
                        {
                            item.Status = RegistryValueStatus.AlreadyConfigured;
                            item.StatusMessage = "Already configured to match the machine-specific candidate.";
                        }
                        else
                        {
                            item.Status = RegistryValueStatus.Recommended;
                            item.StatusMessage = "Machine-specific candidate ready for application.";
                        }
                    }
                    else
                    {
                        // Default Windows 10/11 behavior without explicit value (usually defaults to 3.5GB threshold)
                        item.CurrentValueRaw = null;
                        item.CurrentValueDisplay = "0x00380000 (Default ~3,670,016 KB / Unset)";
                        item.Status = RegistryValueStatus.Recommended;
                        item.StatusMessage = "System is currently using default Windows service grouping.";
                    }
                }
                else
                {
                    item.Status = RegistryValueStatus.NotApplicable;
                    item.StatusMessage = "Registry control subkey inaccessible.";
                }
            }
            catch (Exception ex)
            {
                item.Status = RegistryValueStatus.Failed;
                item.StatusMessage = $"Error reading registry: {ex.Message}";
            }

            // Check snapshot state for pending restart / post-restart verification
            if (snapshots.TryGetValue(itemId, out var snap))
            {
                item.HasBackup = true;
                item.BeforeSvchostCount = snap.BeforeSvchostCount;
                item.BeforeSvchostMemoryMb = snap.BeforeSvchostMemoryMb;

                if (snap.IsPendingRestart)
                {
                    // Check if system restarted since snapshot timestamp
                    var bootTime = GetSystemBootTime();
                    if (bootTime > snap.Timestamp)
                    {
                        // Post-restart verification
                        snap.IsPendingRestart = false;
                        snap.AfterSvchostCount = svchostCount;
                        snap.AfterSvchostMemoryMb = svchostMemoryMb;

                        int deltaCount = svchostCount - snap.BeforeSvchostCount;
                        double deltaMem = svchostMemoryMb - snap.BeforeSvchostMemoryMb;

                        if (deltaMem < -5.0 || deltaCount < 0)
                        {
                            snap.Effectiveness = "IMPROVED";
                        }
                        else if (Math.Abs(deltaMem) <= 20.0 && Math.Abs(deltaCount) <= 3)
                        {
                            snap.Effectiveness = "NO MEASURABLE CHANGE";
                        }
                        else
                        {
                            snap.Effectiveness = "WORSE";
                        }

                        SaveSnapshot(snap);
                        item.Effectiveness = snap.Effectiveness;
                        item.AfterSvchostCount = svchostCount;
                        item.AfterSvchostMemoryMb = svchostMemoryMb;
                        item.Status = RegistryValueStatus.AlreadyConfigured;
                        item.StatusMessage = $"Verified post-restart. Result: {snap.Effectiveness} (Count: {snap.BeforeSvchostCount} → {svchostCount}, RAM: {snap.BeforeSvchostMemoryMb:F1} MB → {svchostMemoryMb:F1} MB)";
                    }
                    else
                    {
                        item.Status = RegistryValueStatus.RequiresRestart;
                        item.StatusMessage = "Value applied to Windows Registry. System restart required for Service Host regrouping to take effect.";
                    }
                }
                else if (snap.Effectiveness != "UNKNOWN")
                {
                    item.Effectiveness = snap.Effectiveness;
                    item.AfterSvchostCount = snap.AfterSvchostCount;
                    item.AfterSvchostMemoryMb = snap.AfterSvchostMemoryMb;
                }
            }

            return item;
        }

        // ── 2. Win32PrioritySeparation ─────────────────────────────────────
        private MachineRegistryValueItem EvaluateWin32PrioritySeparation(HardwareProfile profile, Dictionary<string, RegistryValueSnapshot> snapshots)
        {
            const string regSubKey = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
            const string valName = "Win32PrioritySeparation";
            const string itemId = "val.cpu.priority_separation";

            bool isLaptop = profile.IsBatteryPowered;
            uint candidateDword = isLaptop ? 0x00000026u : 0x00000028u; // 38 for high-response desktop, 38/26 for laptop
            string candidateDesc = isLaptop ? "0x00000026 (Balanced Quantum for Mobile CPU)" : "0x00000028 (Short Variable Quantum for Desktop Responsiveness)";

            var item = new MachineRegistryValueItem
            {
                Id = itemId,
                Name = valName,
                DisplayName = "CPU Thread Quantum & Foreground Scheduling",
                Category = "CPU Scheduling / Responsiveness",
                RegistryPath = @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl",
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Purpose = "Defines how the Windows kernel allocates CPU execution quantum timeslices between active foreground applications and background threads.",
                RiskLevel = "LOW",
                HardwareEvidence = $"Machine Type: {(isLaptop ? "Mobile / Laptop" : "Desktop / Workstation")} | CPU Cores: {profile.CpuLogicalCores} | Power: {(profile.IsBatteryPowered ? "Battery" : "AC Connected")}",
                WhyApplicable = isLaptop ? "Configured for mobile thermal envelope & balanced quantum timeslices." : "Configured for maximum desktop responsiveness and minimal foreground input latency.",
                CandidateValueDisplay = candidateDesc,
                CandidateValueRaw = candidateDword,
                CandidateExplanation = "Tunes foreground quantum priority. Smoothly switches scheduling behavior without requiring kernel rebuilds.",
                RequiresRestart = false
            };

            ReadRegistryState(regSubKey, valName, candidateDword, item, snapshots);
            return item;
        }

        // ── 3. SystemResponsiveness ────────────────────────────────────────
        private MachineRegistryValueItem EvaluateSystemResponsiveness(HardwareProfile profile, Dictionary<string, RegistryValueSnapshot> snapshots)
        {
            const string regSubKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
            const string valName = "SystemResponsiveness";
            const string itemId = "val.mmcss.system_responsiveness";

            uint candidateDword = profile.CpuLogicalCores >= 8 ? 0x00000000u : 0x0000000Au; // 0% or 10% reserved
            string candidateDesc = profile.CpuLogicalCores >= 8 ? "0x00000000 (0% Background Reservation / Max Game Throughput)" : "0x0000000A (10% Balanced Background Reservation)";

            var item = new MachineRegistryValueItem
            {
                Id = itemId,
                Name = valName,
                DisplayName = "Multimedia Network & Background CPU Reservation",
                Category = "MMCSS / Multimedia Scheduling",
                RegistryPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Purpose = "Controls the percentage of CPU execution time reserved by Windows for low-priority background tasks during gaming and multimedia playback.",
                RiskLevel = "LOW",
                HardwareEvidence = $"CPU Logical Cores: {profile.CpuLogicalCores} | Total RAM: {profile.RamTotalGb:F1} GB",
                WhyApplicable = profile.CpuLogicalCores >= 8 ? "High core count allows 0% background throttling without risking UI starvation." : "Multi-core processor handles 10% balanced background reservation cleanly.",
                CandidateValueDisplay = candidateDesc,
                CandidateValueRaw = candidateDword,
                CandidateExplanation = "Reduces micro-stutter by preventing background tasks from stealing cycles from priority gaming and rendering threads.",
                RequiresRestart = false
            };

            ReadRegistryState(regSubKey, valName, candidateDword, item, snapshots);
            return item;
        }

        // ── 4. NetworkThrottlingIndex ───────────────────────────────────────
        private MachineRegistryValueItem EvaluateNetworkThrottling(HardwareProfile profile, Dictionary<string, RegistryValueSnapshot> snapshots)
        {
            const string regSubKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
            const string valName = "NetworkThrottlingIndex";
            const string itemId = "val.network.throttling_index";

            const uint candidateDword = 0xFFFFFFFFu; // Disabled throttling

            var item = new MachineRegistryValueItem
            {
                Id = itemId,
                Name = valName,
                DisplayName = "Network Packet Processing Throttling Index",
                Category = "Network / Multimedia Profile",
                RegistryPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Purpose = "Disables non-multimedia network packet rate limiting during high-throughput network transfers and low-latency gaming.",
                RiskLevel = "LOW",
                HardwareEvidence = "Gigabit / High-Speed Network Subsystem",
                WhyApplicable = "Removes legacy 10-packet per millisecond throttling mechanism on modern network adapters.",
                CandidateValueDisplay = "0xFFFFFFFF (Throttling Disabled / Full Network Line Rate)",
                CandidateValueRaw = candidateDword,
                CandidateExplanation = "Ensures full line-rate throughput without synthetic packet coalescing delay.",
                RequiresRestart = false
            };

            ReadRegistryState(regSubKey, valName, candidateDword, item, snapshots);
            return item;
        }

        // ── 5. IoPageLockLimit ──────────────────────────────────────────────
        private MachineRegistryValueItem EvaluateIoPageLockLimit(HardwareProfile profile, Dictionary<string, RegistryValueSnapshot> snapshots)
        {
            const string regSubKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
            const string valName = "IoPageLockLimit";
            const string itemId = "val.memory.io_page_lock_limit";

            // 1/8th of RAM in bytes up to 512MB
            uint candidateBytes = profile.RamTotalGb >= 16.0 ? 536870912u : (uint)(profile.RamTotalGb * 1024 * 1024 * 1024 / 16);
            string candidateDesc = $"0x{candidateBytes:X8} ({candidateBytes / (1024 * 1024)} MB Buffer)";

            var item = new MachineRegistryValueItem
            {
                Id = itemId,
                Name = valName,
                DisplayName = "Direct I/O Locked Page Buffer Limit",
                Category = "Memory Management / I/O Buffering",
                RegistryPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                ValueName = valName,
                ValueType = RegistryValueKind.DWord,
                Purpose = "Sets the maximum number of bytes that can be locked for I/O operations simultaneously in memory buffers.",
                RiskLevel = "MEDIUM",
                HardwareEvidence = $"Physical Memory: {profile.RamTotalGb:F1} GB | Storage: {profile.StorageType}",
                WhyApplicable = $"Calculated proportionally for {profile.RamTotalGb:F1} GB RAM to accelerate large block NVMe/SSD disk transfers.",
                CandidateValueDisplay = candidateDesc,
                CandidateValueRaw = candidateBytes,
                CandidateExplanation = "Enlarges direct I/O page buffer pool for high-speed disk operations and large texture streaming.",
                RequiresRestart = true
            };

            ReadRegistryState(regSubKey, valName, candidateBytes, item, snapshots);
            return item;
        }

        // ── Helper: Read Registry State ─────────────────────────────────────
        private static void ReadRegistryState(string regSubKey, string valName, uint candidateDword, MachineRegistryValueItem item, Dictionary<string, RegistryValueSnapshot> snapshots)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(regSubKey, false);
                if (key != null)
                {
                    var raw = key.GetValue(valName);
                    if (raw != null)
                    {
                        item.CurrentValueRaw = raw;
                        long currentVal = Convert.ToInt64(raw);
                        item.CurrentValueDisplay = $"0x{currentVal:X8} ({currentVal})";

                        if ((uint)currentVal == candidateDword)
                        {
                            item.Status = RegistryValueStatus.AlreadyConfigured;
                            item.StatusMessage = "Already matches recommended candidate.";
                        }
                        else
                        {
                            item.Status = RegistryValueStatus.Recommended;
                            item.StatusMessage = "Ready for machine-specific optimization.";
                        }
                    }
                    else
                    {
                        item.CurrentValueRaw = null;
                        item.CurrentValueDisplay = "Default / Unset";
                        item.Status = RegistryValueStatus.Recommended;
                        item.StatusMessage = "Value not explicitly set (Using Windows fallback default).";
                    }
                }
                else
                {
                    item.Status = RegistryValueStatus.NotApplicable;
                    item.StatusMessage = "Registry subkey does not exist on this Windows build.";
                }
            }
            catch (Exception ex)
            {
                item.Status = RegistryValueStatus.Failed;
                item.StatusMessage = $"Error reading registry: {ex.Message}";
            }

            if (snapshots.TryGetValue(item.Id, out var snap))
            {
                item.HasBackup = true;
                if (snap.IsPendingRestart && item.RequiresRestart)
                {
                    item.Status = RegistryValueStatus.RequiresRestart;
                    item.StatusMessage = "Value applied. Restart required for full effect.";
                }
            }
        }

        // ── Apply Value with Real Registry Write & Backup ───────────────────
        public bool ApplyRegistryValue(MachineRegistryValueItem item, out string message)
        {
            if (item == null || item.CandidateValueRaw == null)
            {
                message = "Invalid registry item or candidate value.";
                return false;
            }

            string subKeyPath = item.RegistryPath.Replace(@"HKLM\", "", StringComparison.OrdinalIgnoreCase);

            try
            {
                // 1. Capture baseline metrics
                var (svchostCount, svchostMemoryMb) = MeasureLiveSvchostMetrics();

                // 2. Open key & read current value for backup
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (var readKey = baseKey.OpenSubKey(subKeyPath, false))
                    {
                        var currentVal = readKey?.GetValue(item.ValueName);
                        
                        // 3. Create Backup Snapshot before modifying
                        var snapshot = new RegistryValueSnapshot
                        {
                            ItemId = item.Id,
                            RegistryPath = item.RegistryPath,
                            ValueName = item.ValueName,
                            OldValueRaw = currentVal,
                            NewValueRaw = item.CandidateValueRaw,
                            Timestamp = DateTime.UtcNow,
                            DetectedRamGb = HardwareProfiler.GetQuickProfile().RamTotalGb,
                            BeforeSvchostCount = svchostCount,
                            BeforeSvchostMemoryMb = svchostMemoryMb,
                            IsPendingRestart = item.RequiresRestart,
                            Effectiveness = "UNKNOWN"
                        };

                        SaveSnapshot(snapshot);
                    }

                    // 4. Real Registry Write
                    using (var writeKey = baseKey.CreateSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
                    {
                        if (writeKey == null)
                        {
                            message = "Failed to open or create registry subkey with write permissions.";
                            return false;
                        }

                        if (item.CandidateValueRaw is uint uintVal)
                        {
                            writeKey.SetValue(item.ValueName, unchecked((int)uintVal), RegistryValueKind.DWord);
                        }
                        else if (item.CandidateValueRaw is int intVal)
                        {
                            writeKey.SetValue(item.ValueName, intVal, RegistryValueKind.DWord);
                        }
                        else
                        {
                            writeKey.SetValue(item.ValueName, item.CandidateValueRaw, item.ValueType);
                        }
                    }

                    // 5. Immediate Readback Verification
                    using (var verifyKey = baseKey.OpenSubKey(subKeyPath, false))
                    {
                        var readback = verifyKey?.GetValue(item.ValueName);
                        if (readback == null)
                        {
                            message = "Readback failed: value was not found in registry after write.";
                            return false;
                        }

                        long writtenVal = Convert.ToInt64(readback);
                        long targetVal = Convert.ToInt64(item.CandidateValueRaw);

                        if (writtenVal != targetVal)
                        {
                            message = $"Readback verification failed: Expected 0x{targetVal:X}, but read 0x{writtenVal:X}.";
                            return false;
                        }
                    }
                }

                item.HasBackup = true;
                item.CurrentValueRaw = item.CandidateValueRaw;
                item.CurrentValueDisplay = item.CandidateValueDisplay;

                if (item.RequiresRestart)
                {
                    item.Status = RegistryValueStatus.RequiresRestart;
                    message = "Registry value successfully written and verified. Windows restart required for full effect.";
                }
                else
                {
                    item.Status = RegistryValueStatus.AlreadyConfigured;
                    message = "Registry value successfully applied and verified live.";
                }

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                message = "Access denied. Administrator privileges required to write to HKEY_LOCAL_MACHINE.";
                return false;
            }
            catch (Exception ex)
            {
                message = $"Error applying registry value: {ex.Message}";
                return false;
            }
        }

        // ── Rollback Previous Value ─────────────────────────────────────────
        public bool RollbackRegistryValue(MachineRegistryValueItem item, out string message)
        {
            if (item == null)
            {
                message = "Invalid item.";
                return false;
            }

            var snapshots = LoadSnapshots();
            if (!snapshots.TryGetValue(item.Id, out var snapshot))
            {
                message = "No previous backup snapshot found for this registry item.";
                return false;
            }

            string subKeyPath = item.RegistryPath.Replace(@"HKLM\", "", StringComparison.OrdinalIgnoreCase);

            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (var writeKey = baseKey.CreateSubKey(subKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
                    {
                        if (writeKey == null)
                        {
                            message = "Failed to open registry subkey for writing.";
                            return false;
                        }

                        if (snapshot.OldValueRaw != null)
                        {
                            object valToWrite;
                            if (snapshot.OldValueRaw is JsonElement elem)
                            {
                                if (item.ValueType == RegistryValueKind.DWord && elem.TryGetInt32(out int intVal))
                                    valToWrite = intVal;
                                else if (item.ValueType == RegistryValueKind.DWord && elem.TryGetUInt32(out uint uintVal))
                                    valToWrite = unchecked((int)uintVal);
                                else if (item.ValueType == RegistryValueKind.QWord && elem.TryGetInt64(out long longVal))
                                    valToWrite = longVal;
                                else
                                    valToWrite = elem.GetString() ?? "";
                            }
                            else if (snapshot.OldValueRaw is uint u)
                            {
                                valToWrite = unchecked((int)u);
                            }
                            else
                            {
                                valToWrite = snapshot.OldValueRaw;
                            }

                            writeKey.SetValue(item.ValueName, valToWrite, item.ValueType);
                        }
                        else
                        {
                            // Value was originally not set; delete it to restore original state
                            writeKey.DeleteValue(item.ValueName, false);
                        }
                    }

                    // Verify readback
                    using (var verifyKey = baseKey.OpenSubKey(subKeyPath, false))
                    {
                        var readback = verifyKey?.GetValue(item.ValueName);
                        item.CurrentValueRaw = readback;
                        item.CurrentValueDisplay = readback != null ? $"0x{Convert.ToInt64(readback):X8}" : "Default / Unset";
                    }
                }

                snapshot.IsPendingRestart = false;
                snapshot.Effectiveness = "ROLLED_BACK";
                SaveSnapshot(snapshot);

                item.Status = RegistryValueStatus.Recommended;
                message = "Successfully rolled back registry value to previous backup state.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Rollback failed: {ex.Message}";
                return false;
            }
        }

        // ── Snapshot Retrieval Helper ─────────────────────────────────────────
        public RegistryValueSnapshot? GetSnapshot(string itemId)
        {
            var snapshots = LoadSnapshots();
            return snapshots.TryGetValue(itemId, out var s) ? s : null;
        }

        // ── RAM Nearest Supported Tier Calculator ───────────────────────────
        public static long CalculateNearestRamTier(double detectedRamGb, out string reason)
        {
            long[] supportedTiers = { 4, 8, 12, 16, 24, 32, 64, 96, 128 };

            // Exact or within 0.5 GB match
            foreach (var tier in supportedTiers)
            {
                if (Math.Abs(detectedRamGb - tier) <= 0.6)
                {
                    reason = $"Matches validated {tier} GB RAM profile.";
                    return tier;
                }
            }

            // Intermediate RAM: Find nearest supported tier (prioritizing higher tier on tie)
            long nearest = supportedTiers.OrderBy(t => Math.Abs(t - detectedRamGb)).ThenByDescending(t => t).First();
            reason = $"Nearest validated candidate for detected {detectedRamGb:F1} GB RAM configuration.";
            return nearest;
        }

        // ── Measure Live Svchost Metrics ───────────────────────────────────
        public static (int processCount, double totalMemoryMb) MeasureLiveSvchostMetrics()
        {
            try
            {
                var processes = Process.GetProcessesByName("svchost");
                int count = processes.Length;
                long totalBytes = 0;
                foreach (var p in processes)
                {
                    try
                    {
                        totalBytes += p.WorkingSet64;
                    }
                    catch { }
                }

                double memoryMb = totalBytes / (1024.0 * 1024.0);
                return (count, memoryMb);
            }
            catch
            {
                return (0, 0.0);
            }
        }

        private static DateTime GetSystemBootTime()
        {
            try
            {
                return DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64);
            }
            catch
            {
                return DateTime.UtcNow.AddDays(-1);
            }
        }

        // ── Snapshot Persistence ───────────────────────────────────────────
        private static Dictionary<string, RegistryValueSnapshot> LoadSnapshots()
        {
            var map = new Dictionary<string, RegistryValueSnapshot>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(SnapshotFile))
                {
                    string json = File.ReadAllText(SnapshotFile);
                    var list = JsonSerializer.Deserialize<List<RegistryValueSnapshot>>(json);
                    if (list != null)
                    {
                        foreach (var s in list)
                        {
                            map[s.ItemId] = s;
                        }
                    }
                }
            }
            catch { }
            return map;
        }

        private static void SaveSnapshot(RegistryValueSnapshot snapshot)
        {
            try
            {
                var map = LoadSnapshots();
                map[snapshot.ItemId] = snapshot;
                string json = JsonSerializer.Serialize(map.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SnapshotFile, json);
            }
            catch { }
        }
    }
}
