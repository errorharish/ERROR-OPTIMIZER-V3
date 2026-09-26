#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Implementations.Cleaners;
using BiosOptimizer.Core.Implementations.Memory;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.GUI.Services
{
    public enum AutoOptimizeScheduleOption
    {
        Off,
        ThirtyMinutes,
        OneHour,
        ThreeHours,
        SixHours,
        TwelveHours,
        OneDay
    }

    public class AutoOptimizeScheduleState
    {
        public string OptionKey { get; set; } = "OFF";
        public bool IsEnabled { get; set; } = false;
        public DateTime? LastRun { get; set; }
        public DateTime? NextRun { get; set; }
        public string LastResult { get; set; } = "No scheduled run executed yet";
        public string LastRunStatus { get; set; } = "IDLE"; // IDLE, SCHEDULED, RUNNING, VERIFYING, COMPLETED, SKIPPED, DEFERRED, FAILED, OFF
        public string LastDeferralReason { get; set; } = "";
        public string ExecutionTrigger { get; set; } = "SYSTEM";
        public long TotalBytesReclaimedHistorical { get; set; }
    }

    public class ScheduledMaintenanceResult
    {
        public bool Success { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public TimeSpan Duration { get; set; }
        public long TempBytesScanned { get; set; }
        public int TempFilesScanned { get; set; }
        public long TempBytesDeleted { get; set; }
        public int TempFilesDeleted { get; set; }
        public bool MemoryActionExecuted { get; set; }
        public string MemoryActionReason { get; set; } = "";
        public long StandbyBeforeBytes { get; set; }
        public long StandbyAfterBytes { get; set; }
        public long StandbyReclaimedBytes { get; set; }
        public long AvailableBeforeBytes { get; set; }
        public long AvailableAfterBytes { get; set; }
        public string SummaryMessage { get; set; } = "";
        public string VerificationStatus { get; set; } = "VERIFIED";
    }

    public class SmartAutoOptimizeScheduler : IDisposable
    {
        private static readonly Lazy<SmartAutoOptimizeScheduler> _instance = new(() => new SmartAutoOptimizeScheduler());
        public static SmartAutoOptimizeScheduler Instance => _instance.Value;

        private const string TASK_NAME = @"\ErrorOptimizer\SmartAutoOptimizeTask";
        private const string MUTEX_NAME = @"Global\ErrorOptimizer_SmartAutoOptimize_Mutex";

        private readonly string _configFilePath;
        private readonly string _logFilePath;
        private readonly StorageCleanerEngine _storageEngine = new();
        private readonly SemaphoreSlim _executionGate = new(1, 1);
        private readonly DispatcherTimer _inAppTimer;
        private AutoOptimizeScheduleState _state = new();
        private bool _isExecuting = false;

        public event Action<AutoOptimizeScheduleState>? StateChanged;
        public event Func<Task>? RequestTelemetryRefresh;

        public AutoOptimizeScheduleState State => _state;
        public bool IsExecuting => _isExecuting;

        public SmartAutoOptimizeScheduler()
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer");
            string logDir = Path.Combine(appData, "logs");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(logDir);

            _configFilePath = Path.Combine(appData, "auto_optimize_schedule.json");
            _logFilePath = Path.Combine(logDir, "auto_maintenance.log");

            LoadState();

            _inAppTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _inAppTimer.Tick += async (_, _) => await CheckAndTriggerScheduledRunAsync();
            _inAppTimer.Start();
        }

        public static string GetOptionDisplayName(AutoOptimizeScheduleOption option) => option switch
        {
            AutoOptimizeScheduleOption.ThirtyMinutes => "30 MINUTES",
            AutoOptimizeScheduleOption.OneHour => "1 HOUR",
            AutoOptimizeScheduleOption.ThreeHours => "3 HOURS",
            AutoOptimizeScheduleOption.SixHours => "6 HOURS",
            AutoOptimizeScheduleOption.TwelveHours => "12 HOURS",
            AutoOptimizeScheduleOption.OneDay => "1 DAY",
            _ => "OFF"
        };

        public static TimeSpan GetOptionInterval(AutoOptimizeScheduleOption option) => option switch
        {
            AutoOptimizeScheduleOption.ThirtyMinutes => TimeSpan.FromMinutes(30),
            AutoOptimizeScheduleOption.OneHour => TimeSpan.FromHours(1),
            AutoOptimizeScheduleOption.ThreeHours => TimeSpan.FromHours(3),
            AutoOptimizeScheduleOption.SixHours => TimeSpan.FromHours(6),
            AutoOptimizeScheduleOption.TwelveHours => TimeSpan.FromHours(12),
            AutoOptimizeScheduleOption.OneDay => TimeSpan.FromDays(1),
            _ => TimeSpan.Zero
        };

        public static AutoOptimizeScheduleOption ParseOption(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return AutoOptimizeScheduleOption.Off;
            string t = text.Trim().ToUpperInvariant();
            if (t.Contains("30") || t.Contains("HALF")) return AutoOptimizeScheduleOption.ThirtyMinutes;
            if (t == "1 HOUR" || t == "1H" || t == "HOURLY" || t.Contains("1 HR")) return AutoOptimizeScheduleOption.OneHour;
            if (t.Contains("3 HOUR") || t == "3H" || t.Contains("3 HR")) return AutoOptimizeScheduleOption.ThreeHours;
            if (t.Contains("6 HOUR") || t == "6H" || t.Contains("6 HR")) return AutoOptimizeScheduleOption.SixHours;
            if (t.Contains("12 HOUR") || t == "12H" || t.Contains("12 HR")) return AutoOptimizeScheduleOption.TwelveHours;
            if (t.Contains("1 DAY") || t == "1D" || t == "DAILY" || t.Contains("24") || t.Contains("24H")) return AutoOptimizeScheduleOption.OneDay;
            return AutoOptimizeScheduleOption.Off;
        }

        public async Task SetScheduleAsync(AutoOptimizeScheduleOption option)
        {
            var interval = GetOptionInterval(option);
            bool isEnabled = option != AutoOptimizeScheduleOption.Off;

            _state.OptionKey = GetOptionDisplayName(option);
            _state.IsEnabled = isEnabled;

            if (isEnabled)
            {
                _state.NextRun = DateTime.Now.Add(interval);
                _state.LastRunStatus = "SCHEDULED";
                _state.LastDeferralReason = "";
            }
            else
            {
                _state.NextRun = null;
                _state.LastRunStatus = "OFF";
                _state.LastDeferralReason = "";
            }

            SaveState();
            await SyncWindowsTaskSchedulerAsync(option);
            StateChanged?.Invoke(_state);
        }

        public async Task<ScheduledMaintenanceResult?> RunManualOrScheduledAsync(string trigger = "SCHEDULED", bool bypassWorkloadProtection = false, CancellationToken ct = default)
        {
            if (!await _executionGate.WaitAsync(100, ct))
            {
                Debug.WriteLine("[Scheduler] Execution gate locked. Skipping run.");
                return null;
            }

            bool mutexAcquired = false;
            Mutex? mutex = null;

            try
            {
                mutex = new Mutex(false, MUTEX_NAME);
                try
                {
                    mutexAcquired = mutex.WaitOne(100, false);
                }
                catch (AbandonedMutexException)
                {
                    mutexAcquired = true;
                }

                if (!mutexAcquired)
                {
                    Debug.WriteLine("[Scheduler] Mutex in use by another instance. Skipping.");
                    return null;
                }

                _isExecuting = true;
                _state.ExecutionTrigger = trigger;
                _state.LastRunStatus = "RUNNING";
                StateChanged?.Invoke(_state);

                var sw = Stopwatch.StartNew();
                var result = new ScheduledMaintenanceResult();

                // 1. Check Foreground Workload Protection (if not explicitly bypassed for test/manual trigger)
                if (!bypassWorkloadProtection)
                {
                    var workload = UserWorkloadDetector.Instance.ClassifyCurrentWorkload();
                    if (IsHighDemandWorkload(workload))
                    {
                        _state.LastRunStatus = "DEFERRED";
                        _state.LastDeferralReason = $"Active high-demand workload detected ({workload.PrimaryProcessName} - {workload.Category}).";
                        ScheduleNextRun();
                        SaveState();
                        StateChanged?.Invoke(_state);
                        LogMaintenance("[DEFERRED]", _state.LastDeferralReason);
                        return null;
                    }

                    // 2. Check Battery Protection
                    if (IsBatteryPowerConstrained())
                    {
                        _state.LastRunStatus = "DEFERRED";
                        _state.LastDeferralReason = "Running on battery power (< 50%). Maintenance deferred to preserve energy.";
                        ScheduleNextRun();
                        SaveState();
                        StateChanged?.Invoke(_state);
                        LogMaintenance("[DEFERRED]", _state.LastDeferralReason);
                        return null;
                    }
                }

                // 3. Real Temporary File Cleanup (Using Authoritative Storage Cleaner Engine)
                string sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var allCategories = StorageCleanerEngine.BuildCategories();
                var tempCats = allCategories.Where(c =>
                    c.CategoryType == "TEMP" ||
                    c.Id == "temp-files" ||
                    c.Id == "temp-appdata" ||
                    c.Id == "log-files" ||
                    c.Id == "crash-dumps").ToList();

                long tempBytesScanned = 0;
                int tempFilesScanned = 0;
                foreach (var cat in tempCats)
                {
                    var scan = _storageEngine.ScanCategory(cat);
                    tempBytesScanned += scan.DetectedBytes;
                    tempFilesScanned += scan.FileCount;
                }

                result.TempBytesScanned = tempBytesScanned;
                result.TempFilesScanned = tempFilesScanned;

                var cleanRes = await Task.Run(() => _storageEngine.CleanCategories(tempCats, sysDrive, null, ct), ct);
                result.TempBytesDeleted = cleanRes.BytesReclaimed;
                result.TempFilesDeleted = cleanRes.FilesRemoved;

                // 4. Condition-Based Real Memory / Standby Reclaim
                var memBefore = WindowsMemoryTelemetryProvider.Instance.SampleCurrentMemoryState();
                result.StandbyBeforeBytes = memBefore.StandbyBytes;
                result.AvailableBeforeBytes = memBefore.AvailableBytes;

                bool shouldReclaimMemory = ShouldExecuteStandbyCleanup(memBefore);
                if (shouldReclaimMemory)
                {
                    _state.LastRunStatus = "VERIFYING";
                    StateChanged?.Invoke(_state);

                    if (ExternalEmptyStandbyListProvider.Instance.IsAvailable)
                    {
                        var extRes = await ExternalEmptyStandbyListProvider.Instance.ExecuteAsync(false, ct);
                        result.StandbyReclaimedBytes = extRes.StandbyReclaimedBytes;
                    }
                    else
                    {
                        var purgeRes = await WindowsMemoryListProvider.Instance.ExecuteStandbyPurgeAsync(false, ct);
                        result.StandbyReclaimedBytes = purgeRes.StandbyReclaimedBytes;
                    }

                    // Trim working sets
                    await WindowsMemoryListProvider.Instance.ExecuteWorkingSetTrimAsync(ct);

                    // Re-sample post-cleanup memory
                    var memAfter = WindowsMemoryTelemetryProvider.Instance.SampleCurrentMemoryState();
                    result.StandbyAfterBytes = memAfter.StandbyBytes;
                    result.AvailableAfterBytes = memAfter.AvailableBytes;
                    result.MemoryActionExecuted = true;
                    result.MemoryActionReason = $"Memory pressure elevated ({memBefore.MemoryPressurePercent}%). Standby reclaimed: {FormatBytes(result.StandbyReclaimedBytes)}.";
                }
                else
                {
                    result.StandbyAfterBytes = memBefore.StandbyBytes;
                    result.AvailableAfterBytes = memBefore.AvailableBytes;
                    result.MemoryActionExecuted = false;
                    result.MemoryActionReason = "Standby cleanup skipped due to healthy memory pressure.";
                }

                // 5. Trigger Telemetry Refresh on Dashboard
                try
                {
                    if (RequestTelemetryRefresh != null)
                    {
                        await RequestTelemetryRefresh.Invoke();
                    }
                }
                catch { }

                sw.Stop();
                result.Duration = sw.Elapsed;
                result.Success = true;

                // 6. Format Result Summary
                string storageSummary = result.TempBytesDeleted > 0
                    ? $"{FormatBytes(result.TempBytesDeleted)} temp files cleaned ({result.TempFilesDeleted} files)"
                    : "Temp files already clean";

                string memorySummary = result.MemoryActionExecuted
                    ? $"standby reclaimed {FormatBytes(result.StandbyReclaimedBytes)} (Available: {FormatBytes(result.AvailableBeforeBytes)} → {FormatBytes(result.AvailableAfterBytes)})"
                    : result.MemoryActionReason;

                result.SummaryMessage = $"Completed — {storageSummary}; {memorySummary}.";

                // 7. Update Scheduler State
                _state.LastRun = DateTime.Now;
                _state.LastResult = result.SummaryMessage;
                _state.LastRunStatus = "COMPLETED";
                _state.LastDeferralReason = "";
                _state.TotalBytesReclaimedHistorical += result.TempBytesDeleted + result.StandbyReclaimedBytes;

                ScheduleNextRun();
                SaveState();

                // 8. Record Maintenance Transaction
                RecordMaintenanceTransaction(result, trigger);

                _state.LastRunStatus = _state.IsEnabled ? "SCHEDULED" : "OFF";
                StateChanged?.Invoke(_state);

                return result;
            }
            catch (Exception ex)
            {
                _state.LastRunStatus = "FAILED";
                _state.LastResult = $"Error: {ex.Message}";
                ScheduleNextRun();
                SaveState();
                StateChanged?.Invoke(_state);
                LogMaintenance("[ERROR]", ex.ToString());
                return null;
            }
            finally
            {
                _isExecuting = false;
                if (mutexAcquired && mutex != null)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                }
                mutex?.Dispose();
                _executionGate.Release();
            }
        }

        public async Task<ScheduledMaintenanceResult?> TriggerScheduledCleanupNowAsync(CancellationToken ct = default)
        {
            return await RunManualOrScheduledAsync("MANUAL_TEST_TRIGGER", bypassWorkloadProtection: true, ct: ct);
        }

        private async Task CheckAndTriggerScheduledRunAsync()
        {
            if (!_state.IsEnabled || !_state.NextRun.HasValue) return;

            if (DateTime.Now >= _state.NextRun.Value && !_isExecuting)
            {
                await RunManualOrScheduledAsync("SCHEDULED_TIMER");
            }
        }

        private void ScheduleNextRun()
        {
            if (!_state.IsEnabled)
            {
                _state.NextRun = null;
                return;
            }

            var opt = ParseOption(_state.OptionKey);
            var interval = GetOptionInterval(opt);
            if (interval > TimeSpan.Zero)
            {
                _state.NextRun = DateTime.Now.Add(interval);
            }
            else
            {
                _state.NextRun = null;
            }
        }

        private bool ShouldExecuteStandbyCleanup(MemoryTelemetryState memState)
        {
            // Policy: Only flush standby when memory pressure is elevated or standby list is meaningfully large
            if (!WindowsMemoryListProvider.Instance.IsSupported && !ExternalEmptyStandbyListProvider.Instance.IsAvailable)
            {
                return false;
            }

            // High pressure (> 65% memory used)
            if (memState.MemoryPressurePercent > 65) return true;

            // Meaningful standby cache (> 1.5 GB)
            if (memState.StandbyBytes > 1536L * 1024 * 1024) return true;

            // Low available memory (< 20% of total)
            if (memState.TotalPhysicalBytes > 0 && memState.AvailableBytes < (memState.TotalPhysicalBytes * 0.20)) return true;

            return false;
        }

        private bool IsHighDemandWorkload(WorkloadClassificationResult? workload)
        {
            if (workload == null) return false;
            return workload.Category switch
            {
                ExtendedWorkloadCategory.Gaming => true,
                ExtendedWorkloadCategory.CreativeRendering => true,
                ExtendedWorkloadCategory.CreativeEditing => true,
                ExtendedWorkloadCategory.Compilation => true,
                ExtendedWorkloadCategory.Virtualization => true,
                _ => false
            };
        }

        private bool IsBatteryPowerConstrained()
        {
            try
            {
                var hw = HardwareProfiler.GetQuickProfile();
                return hw.IsBatteryPowered && hw.BatteryPercent < 50;
            }
            catch
            {
                return false;
            }
        }

        private void RecordMaintenanceTransaction(ScheduledMaintenanceResult res, string trigger)
        {
            try
            {
                // Capture to Universal Backup Manager
                if (res.TempBytesDeleted > 0)
                {
                    BackupManager.Instance.CaptureStorageCleanup(
                        "Auto Maintenance Schedule",
                        "Scheduled Temporary File Clean",
                        res.TempBytesDeleted,
                        res.TempFilesDeleted,
                        new System.Collections.Generic.List<string> { "%TEMP%", "Windows\\Temp", "Stale Logs" },
                        false
                    );
                }

                if (res.MemoryActionExecuted && res.StandbyReclaimedBytes > 0)
                {
                    BackupManager.Instance.CaptureMemoryRuntime(
                        "Auto Maintenance Standby Flush",
                        res.StandbyReclaimedBytes / (1024 * 1024),
                        $"Standby: {FormatBytes(res.StandbyBeforeBytes)}",
                        $"Standby: {FormatBytes(res.StandbyAfterBytes)}",
                        $"Available: {FormatBytes(res.AvailableBeforeBytes)} → {FormatBytes(res.AvailableAfterBytes)}"
                    );
                }

                // Append to dedicated maintenance log
                string logLine = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [AUTO_MAINTENANCE] Trigger={trigger}, Interval={_state.OptionKey}, TempDeleted={FormatBytes(res.TempBytesDeleted)} ({res.TempFilesDeleted} files), MemoryAction={res.MemoryActionExecuted}, StandbyReclaimed={FormatBytes(res.StandbyReclaimedBytes)}, Duration={res.Duration.TotalMilliseconds:F0}ms, Summary='{res.SummaryMessage}'";
                LogMaintenance("[TRANSACTION]", logLine);
            }
            catch { }
        }

        private void LogMaintenance(string tag, string message)
        {
            try
            {
                string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {tag} {message}";
                File.AppendAllLines(_logFilePath, new[] { line });
                Debug.WriteLine(line);
            }
            catch { }
        }

        private async Task SyncWindowsTaskSchedulerAsync(AutoOptimizeScheduleOption option)
        {
            await Task.Run(() =>
            {
                try
                {
                    // Remove existing task first to ensure single authoritative registration
                    RunSchtasks($"/delete /tn \"{TASK_NAME}\" /f");

                    if (option == AutoOptimizeScheduleOption.Off) return;

                    string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    {
                        exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ErrorOptimizer.exe");
                    }

                    string scheduleParams = option switch
                    {
                        AutoOptimizeScheduleOption.ThirtyMinutes => "/sc MINUTE /mo 30",
                        AutoOptimizeScheduleOption.OneHour => "/sc HOURLY /mo 1",
                        AutoOptimizeScheduleOption.ThreeHours => "/sc HOURLY /mo 3",
                        AutoOptimizeScheduleOption.SixHours => "/sc HOURLY /mo 6",
                        AutoOptimizeScheduleOption.TwelveHours => "/sc HOURLY /mo 12",
                        AutoOptimizeScheduleOption.OneDay => "/sc DAILY /mo 1",
                        _ => ""
                    };

                    if (!string.IsNullOrEmpty(scheduleParams))
                    {
                        string actionCmd = $"\"{exePath}\" --auto-optimize-background";
                        RunSchtasks($"/create /tn \"{TASK_NAME}\" {scheduleParams} /tr \"{actionCmd}\" /rl HIGHEST /f");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Scheduler] Task Scheduler sync failed: {ex.Message}");
                }
            });
        }

        private static void RunSchtasks(string arguments)
        {
            try
            {
                using var p = new Process();
                p.StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                p.Start();
                p.WaitForExit(3000);
            }
            catch { }
        }

        private void LoadState()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var loaded = JsonSerializer.Deserialize<AutoOptimizeScheduleState>(json);
                    if (loaded != null)
                    {
                        _state = loaded;
                        // If it was enabled and next run is in the past, adjust next run forward
                        if (_state.IsEnabled && _state.NextRun.HasValue && _state.NextRun.Value < DateTime.Now)
                        {
                            var opt = ParseOption(_state.OptionKey);
                            var interval = GetOptionInterval(opt);
                            if (interval > TimeSpan.Zero)
                            {
                                _state.NextRun = DateTime.Now.Add(interval);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveState()
        {
            try
            {
                string json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }
            catch { }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public void Dispose()
        {
            _inAppTimer.Stop();
            _executionGate.Dispose();
        }
    }
}

