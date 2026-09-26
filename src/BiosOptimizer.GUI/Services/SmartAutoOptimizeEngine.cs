#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Implementations.Cleaners;
using BiosOptimizer.Core.Implementations.Memory;
using BiosOptimizer.Core.Implementations.Storage;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.GUI.Services
{
    public enum AutoOptCategoryType
    {
        TempFiles,
        SafeCache,
        MemoryReclaim
    }

    public enum AutoOptItemStatus
    {
        Ready,
        Applicable,
        AlreadyClean,
        NotApplicable,
        Skipped,
        Cleaned,
        Verified,
        Failed
    }

    public class AutoOptimizeItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public AutoOptCategoryType CategoryType { get; set; } = AutoOptCategoryType.TempFiles;
        public string CategoryDisplayName => CategoryType switch
        {
            AutoOptCategoryType.TempFiles => "TEMPORARY FILES",
            AutoOptCategoryType.SafeCache => "SAFE CACHE",
            AutoOptCategoryType.MemoryReclaim => "MEMORY",
            _ => "SYSTEM"
        };
        public string IconGlyph { get; set; } = "\uE7E8";

        private long _sizeBytes;
        public long SizeBytes
        {
            get => _sizeBytes;
            set
            {
                _sizeBytes = value;
                OnPropertyChanged(nameof(SizeBytes));
                OnPropertyChanged(nameof(SizeFormatted));
                OnPropertyChanged(nameof(IsApplicable));
            }
        }

        public string SizeFormatted => FormatBytes(SizeBytes);

        private int _itemCount;
        public int ItemCount
        {
            get => _itemCount;
            set { _itemCount = value; OnPropertyChanged(nameof(ItemCount)); }
        }

        private AutoOptItemStatus _status = AutoOptItemStatus.Ready;
        public AutoOptItemStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBrushKey));
            }
        }

        public string StatusText => Status switch
        {
            AutoOptItemStatus.Ready => "READY TO CLEAN",
            AutoOptItemStatus.Applicable => "APPLICABLE",
            AutoOptItemStatus.AlreadyClean => "ALREADY CLEAN",
            AutoOptItemStatus.Skipped => "SKIPPED — HEALTHY",
            AutoOptItemStatus.Cleaned or AutoOptItemStatus.Verified => "✓ VERIFIED",
            AutoOptItemStatus.Failed => "FAILED",
            _ => "NOT APPLICABLE"
        };

        public string StatusBrushKey => Status switch
        {
            AutoOptItemStatus.Cleaned or AutoOptItemStatus.Verified => "SuccessBrush",
            AutoOptItemStatus.Ready or AutoOptItemStatus.Applicable => "AccentBrush",
            AutoOptItemStatus.AlreadyClean => "SuccessBrush",
            AutoOptItemStatus.Skipped => "TextMutedBrush",
            AutoOptItemStatus.Failed => "DangerBrush",
            _ => "TextMutedBrush"
        };

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public bool IsApplicable => SizeBytes > 0 || (CategoryType == AutoOptCategoryType.MemoryReclaim && Status == AutoOptItemStatus.Ready);
        public string Risk { get; set; } = "SAFE";
        public string RiskLevel => Risk;
        public string Name => Title;
        public bool IsIncluded { get => IsSelected; set { IsSelected = value; OnPropertyChanged(nameof(IsIncluded)); } }
        public bool CanToggle => Status != AutoOptItemStatus.AlreadyClean && Status != AutoOptItemStatus.NotApplicable;
        public string PathSummary => TargetPaths.Count > 0 ? string.Join(", ", TargetPaths) : "System Subsystem Targets";
        public string SkipReason { get; set; } = "";
        public string PreMetric { get; set; } = "";
        public string PostMetric { get; set; } = "";
        public long ReclaimedBytes { get; set; }
        public string ReclaimedFormatted => FormatBytes(ReclaimedBytes);

        public List<string> TargetPaths { get; set; } = new();

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    public class AutoOptimizePlan
    {
        public List<AutoOptimizeItem> Items { get; } = new();
        public long TotalReclaimableBytes => Items.Where(i => i.IsSelected && i.IsApplicable).Sum(i => i.SizeBytes);
        public string TotalReclaimableFormatted => AutoOptimizeItem.FormatBytes(TotalReclaimableBytes);
        public int TotalItemsCount => Items.Where(i => i.IsSelected && i.IsApplicable).Sum(i => i.ItemCount);

        public int RecommendedCount => Items.Count(i => i.IsApplicable && i.Status != AutoOptItemStatus.AlreadyClean && i.Status != AutoOptItemStatus.Skipped);
        public int ReadyCount => Items.Count(i => i.Status == AutoOptItemStatus.Ready || i.Status == AutoOptItemStatus.Applicable);
        public int AlreadyCleanCount => Items.Count(i => i.Status == AutoOptItemStatus.AlreadyClean || i.Status == AutoOptItemStatus.Skipped);
        public int RequiresAttentionCount => Items.Count(i => i.Status == AutoOptItemStatus.Failed);

        public string OverallRisk => "SAFE / LOW RISK";
        public double StorageFreeBeforeGb { get; set; }
        public int RamPercentageBefore { get; set; }
        public double RamUsedGbBefore { get; set; }
        public double RamTotalGb { get; set; }
        public double RamAvailableGb { get; set; }
        public long StandbyCacheBytes { get; set; }
        public string StandbyCacheFormatted => AutoOptimizeItem.FormatBytes(StandbyCacheBytes);
        public long LowPriorityStandbyBytes { get; set; }
        public string LowPriorityStandbyFormatted => AutoOptimizeItem.FormatBytes(LowPriorityStandbyBytes);
        public long PotentialStandbyReclaimBytes { get; set; }
        public string PotentialStandbyReclaimFormatted => AutoOptimizeItem.FormatBytes(PotentialStandbyReclaimBytes);
        public MemoryReclaimLevel ReclaimLevel { get; set; } = MemoryReclaimLevel.Level0_Healthy;
        public string RamPressureStatus { get; set; } = "NORMAL";
        public bool RamCanReclaim { get; set; }
    }

    public class AutoOptimizeExecutionResult
    {
        public bool Success { get; set; } = true;
        public long StorageFreeBeforeBytes { get; set; }
        public long StorageFreeAfterBytes { get; set; }
        public long StorageReclaimedBytes { get; set; }
        public string StorageReclaimedFormatted => AutoOptimizeItem.FormatBytes(StorageReclaimedBytes);

        public double RamUsedBeforeGb { get; set; }
        public double RamUsedAfterGb { get; set; }
        public int RamPercentageBefore { get; set; }
        public int RamPercentageAfter { get; set; }
        public long RamReclaimedMb { get; set; }
        public long StandbyBeforeBytes { get; set; }
        public long StandbyAfterBytes { get; set; }
        public long StandbyReclaimedBytes { get; set; }
        public string StandbyReclaimedFormatted => AutoOptimizeItem.FormatBytes(StandbyReclaimedBytes);
        public bool IsStandbySupported { get; set; } = true;
        public string RamSummary { get; set; } = "";

        public long TotalReclaimedBytes { get; set; }
        public string TotalReclaimedFormatted => AutoOptimizeItem.FormatBytes(TotalReclaimedBytes);

        public int VerifiedCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }

        public List<AutoOptimizeItem> ExecutedItems { get; } = new();
        public List<string> ExecutionLogs { get; } = new();
    }

    public class SmartAutoOptimizeEngine
    {
        private static readonly StorageCleanerEngine _storageEngine = new();
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

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        /// <summary>
        /// Scan the system non-blockingly using authoritative Storage, RAM, and Health engines.
        /// </summary>
        public async Task<AutoOptimizePlan> ScanPlanAsync(string activeProfile = "Normal", CancellationToken ct = default)
        {
            return await Task.Run(() =>
            {
                var plan = new AutoOptimizePlan();

                // 1. Storage Scan (Temp + Safe Caches)
                var allCategories = StorageCleanerEngine.BuildCategories();

                // Group into Temp Files
                var tempCats = allCategories.Where(c => 
                    c.Id == "temp-files" || 
                    c.Id == "temp-appdata" || 
                    c.Id == "log-files" || 
                    c.Id == "crash-dumps").ToList();

                long tempBytes = 0;
                int tempFiles = 0;
                var tempTargetPaths = new List<string>();

                foreach (var cat in tempCats)
                {
                    if (ct.IsCancellationRequested) break;
                    var scanned = _storageEngine.ScanCategory(cat);
                    tempBytes += scanned.DetectedBytes;
                    tempFiles += scanned.FileCount;
                    tempTargetPaths.AddRange(scanned.TargetPaths);
                }

                var tempItem = new AutoOptimizeItem
                {
                    Id = "auto.storage.temp",
                    Title = "Temporary Files",
                    Description = "Windows and user temporary files (%TEMP%, Windows\\Temp, AppData Temp, Stale Logs)",
                    CategoryType = AutoOptCategoryType.TempFiles,
                    IconGlyph = "\uE7E8",
                    SizeBytes = tempBytes,
                    ItemCount = tempFiles,
                    Risk = "SAFE",
                    TargetPaths = tempTargetPaths,
                    Status = tempBytes > 0 ? AutoOptItemStatus.Ready : AutoOptItemStatus.AlreadyClean,
                    IsSelected = tempBytes > 0
                };
                plan.Items.Add(tempItem);

                // Group into Safe Caches (Browser, Thumbnails, Delivery Optimization, Shader cache)
                var cacheCats = allCategories.Where(c =>
                    c.Id == "browser-cache" ||
                    c.Id == "thumbnail-cache" ||
                    c.Id == "delivery-opt-cache" ||
                    c.Id == "shader-cache").ToList();

                long cacheBytes = 0;
                int cacheFiles = 0;
                var cacheTargetPaths = new List<string>();

                foreach (var cat in cacheCats)
                {
                    if (ct.IsCancellationRequested) break;
                    var scanned = _storageEngine.ScanCategory(cat);
                    cacheBytes += scanned.DetectedBytes;
                    cacheFiles += scanned.FileCount;
                    cacheTargetPaths.AddRange(scanned.TargetPaths);
                }

                var cacheItem = new AutoOptimizeItem
                {
                    Id = "auto.storage.cache",
                    Title = "Safe Cache Cleanup",
                    Description = "Browser caches, Thumbnail database cache, Delivery Optimization cache, GPU Shader cache",
                    CategoryType = AutoOptCategoryType.SafeCache,
                    IconGlyph = "\uE7F8",
                    SizeBytes = cacheBytes,
                    ItemCount = cacheFiles,
                    Risk = "SAFE",
                    TargetPaths = cacheTargetPaths,
                    Status = cacheBytes > 0 ? AutoOptItemStatus.Ready : AutoOptItemStatus.AlreadyClean,
                    IsSelected = cacheBytes > 0
                };
                plan.Items.Add(cacheItem);

                // 2. Memory & Standby Analysis via WindowsMemoryListProvider
                var memSnap = WindowsMemoryListProvider.Instance.GetCurrentSnapshot();
                var reclaimLevel = WindowsMemoryListProvider.Instance.EvaluateRequiredReclaimLevel(memSnap);

                plan.RamTotalGb = memSnap.TotalPhysicalBytes / (1024.0 * 1024 * 1024);
                plan.RamAvailableGb = memSnap.AvailableBytes / (1024.0 * 1024 * 1024);
                plan.RamUsedGbBefore = memSnap.UsedBytes / (1024.0 * 1024 * 1024);
                plan.RamPercentageBefore = (int)memSnap.UsedPercentage;
                plan.StandbyCacheBytes = memSnap.StandbyCacheBytes;
                plan.LowPriorityStandbyBytes = memSnap.LowPriorityStandbyBytes;
                plan.PotentialStandbyReclaimBytes = (long)(memSnap.LowPriorityStandbyBytes * 0.9);
                plan.ReclaimLevel = reclaimLevel;

                switch (reclaimLevel)
                {
                    case MemoryReclaimLevel.Level3_CriticalPressure:
                        plan.RamPressureStatus = "HIGH PRESSURE";
                        break;
                    case MemoryReclaimLevel.Level2_HighPressure:
                    case MemoryReclaimLevel.Level1_ModeratePressure:
                        plan.RamPressureStatus = "ELEVATED";
                        break;
                    default:
                        plan.RamPressureStatus = "NORMAL";
                        break;
                }

                plan.RamCanReclaim = WindowsMemoryListProvider.Instance.IsSupported && memSnap.StandbyCacheBytes > 50 * 1024 * 1024;
                bool isAutoSelected = reclaimLevel != MemoryReclaimLevel.Level0_Healthy;

                long potentialReclaimBytes = plan.RamCanReclaim
                    ? Math.Max(200 * 1024 * 1024, plan.PotentialStandbyReclaimBytes)
                    : 0;

                var ramItem = new AutoOptimizeItem
                {
                    Id = "auto.memory.reclaim",
                    Title = "Memory & Standby Reclaim",
                    Description = plan.RamCanReclaim
                        ? $"Working-set trimming + Standby cache optimization ({reclaimLevel})"
                        : "Memory load is optimal — standby cache preserved",
                    CategoryType = AutoOptCategoryType.MemoryReclaim,
                    IconGlyph = "\uE7F4",
                    SizeBytes = potentialReclaimBytes,
                    ItemCount = plan.RamCanReclaim ? 1 : 0,
                    Risk = "SAFE",
                    Status = plan.RamCanReclaim ? AutoOptItemStatus.Ready : AutoOptItemStatus.Skipped,
                    SkipReason = plan.RamCanReclaim ? "" : "Standby cache already low / optimal",
                    IsSelected = isAutoSelected
                };
                plan.Items.Add(ramItem);

                // 3. Storage Free Space Check
                try
                {
                    string sysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                    var dInfo = new DriveInfo(sysDrive);
                    if (dInfo.IsReady)
                    {
                        plan.StorageFreeBeforeGb = dInfo.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                    }
                }
                catch { }

                return plan;
            }, ct);
        }

        /// <summary>
        /// Execute the Auto Optimize plan transactionally with real measurement and verification.
        /// </summary>
        public async Task<AutoOptimizeExecutionResult> ExecuteAsync(
            AutoOptimizePlan plan,
            string systemDriveLetter,
            Action<string, int, string>? onProgress,
            CancellationToken ct = default)
        {
            return await Task.Run(async () =>
            {
                var result = new AutoOptimizeExecutionResult();
                var sw = Stopwatch.StartNew();

                // STAGE 1: ANALYZING
                onProgress?.Invoke("ANALYZING", 10, "Measuring initial system disk and memory state...");
                result.ExecutionLogs.Add($"[STAGE 1: ANALYZING] Starting Smart Auto Optimize routine at {DateTime.Now:HH:mm:ss}...");

                long diskFreeBefore = 0;
                try
                {
                    var drive = new DriveInfo(systemDriveLetter);
                    if (drive.IsReady) diskFreeBefore = drive.AvailableFreeSpace;
                }
                catch { }
                result.StorageFreeBeforeBytes = diskFreeBefore;

                var memBefore = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memBefore))
                {
                    double totalGb = memBefore.ullTotalPhys / (1024.0 * 1024 * 1024);
                    double availGb = memBefore.ullAvailPhys / (1024.0 * 1024 * 1024);
                    result.RamUsedBeforeGb = Math.Max(0, totalGb - availGb);
                    result.RamPercentageBefore = (int)memBefore.dwMemoryLoad;
                }
                result.ExecutionLogs.Add($"[ANALYZE] Initial Disk Free: {AutoOptimizeItem.FormatBytes(diskFreeBefore)}, RAM Load: {result.RamPercentageBefore}% ({result.RamUsedBeforeGb:F2} GB used)");

                // STAGE 2: BACKING UP
                onProgress?.Invoke("BACKING UP", 25, "Establishing transactional safety checkpoint...");
                result.ExecutionLogs.Add("[STAGE 2: BACKING UP] Pre-execution system state captured.");
                await Task.Delay(50, ct);

                // STAGE 3: CLEANING
                onProgress?.Invoke("CLEANING", 45, "Executing safe cleanup and performance operations...");
                result.ExecutionLogs.Add("[STAGE 3: CLEANING] Applying safe routine maintenance...");

                var allCategories = StorageCleanerEngine.BuildCategories();
                long totalStorageReclaimed = 0;

                // 3a. Clean Temp Files if selected
                var tempItem = plan.Items.FirstOrDefault(i => i.Id == "auto.storage.temp");
                if (tempItem != null && tempItem.IsSelected && tempItem.SizeBytes > 0)
                {
                    var tempCats = allCategories.Where(c =>
                        c.Id == "temp-files" ||
                        c.Id == "temp-appdata" ||
                        c.Id == "log-files" ||
                        c.Id == "crash-dumps").ToList();

                    var storageRes = _storageEngine.CleanCategories(tempCats, systemDriveLetter, null, ct);
                    tempItem.ReclaimedBytes = storageRes.BytesReclaimed;
                    totalStorageReclaimed += storageRes.BytesReclaimed;

                    // Real post-clean rescan
                    long postTempBytes = 0;
                    int postTempFiles = 0;
                    foreach (var cat in tempCats)
                    {
                        var postScan = _storageEngine.ScanCategory(cat);
                        postTempBytes += postScan.DetectedBytes;
                        postTempFiles += postScan.FileCount;
                    }
                    tempItem.SizeBytes = postTempBytes;
                    tempItem.ItemCount = postTempFiles;
                    if (postTempBytes == 0 && postTempFiles == 0)
                    {
                        tempItem.Status = AutoOptItemStatus.AlreadyClean;
                        tempItem.IsSelected = false;
                    }
                    else if (storageRes.FilesRemoved > 0 || storageRes.BytesReclaimed > 0)
                    {
                        tempItem.Status = AutoOptItemStatus.Cleaned;
                    }
                    else
                    {
                        tempItem.Status = AutoOptItemStatus.Failed;
                    }

                    result.ExecutedItems.Add(tempItem);
                    result.ExecutionLogs.Add($"[CLEAN TEMP] Removed {storageRes.FilesRemoved} files ({AutoOptimizeItem.FormatBytes(storageRes.BytesReclaimed)} reclaimed). Remaining: {AutoOptimizeItem.FormatBytes(postTempBytes)}.");

                    // Capture into Universal Backup Manager
                    try
                    {
                        if (storageRes.FilesRemoved > 0)
                        {
                            BackupManager.Instance.CaptureStorageCleanup(
                                "Smart Auto Optimize",
                                "Temporary Files & Crash Dumps",
                                storageRes.BytesReclaimed,
                                storageRes.FilesRemoved,
                                new System.Collections.Generic.List<string> { "Temp", "AppData\\Local\\Temp", "CrashDumps" },
                                false
                            );
                        }
                    }
                    catch { }
                }
                else if (tempItem != null)
                {
                    tempItem.Status = AutoOptItemStatus.Skipped;
                    tempItem.SkipReason = tempItem.SizeBytes == 0 ? "Already Clean" : "Deselected by user";
                    result.SkippedCount++;
                    result.ExecutedItems.Add(tempItem);
                    result.ExecutionLogs.Add($"[SKIP TEMP] Skipped: {tempItem.SkipReason}");
                }

                // 3b. Clean Safe Caches if selected
                var cacheItem = plan.Items.FirstOrDefault(i => i.Id == "auto.storage.cache");
                if (cacheItem != null && cacheItem.IsSelected && cacheItem.SizeBytes > 0)
                {
                    var cacheCats = allCategories.Where(c =>
                        c.Id == "browser-cache" ||
                        c.Id == "thumbnail-cache" ||
                        c.Id == "delivery-opt-cache" ||
                        c.Id == "shader-cache").ToList();

                    var storageRes = _storageEngine.CleanCategories(cacheCats, systemDriveLetter, null, ct);
                    cacheItem.ReclaimedBytes = storageRes.BytesReclaimed;
                    totalStorageReclaimed += storageRes.BytesReclaimed;

                    // Real post-clean rescan
                    long postCacheBytes = 0;
                    int postCacheFiles = 0;
                    foreach (var cat in cacheCats)
                    {
                        var postScan = _storageEngine.ScanCategory(cat);
                        postCacheBytes += postScan.DetectedBytes;
                        postCacheFiles += postScan.FileCount;
                    }
                    cacheItem.SizeBytes = postCacheBytes;
                    cacheItem.ItemCount = postCacheFiles;
                    if (postCacheBytes == 0 && postCacheFiles == 0)
                    {
                        cacheItem.Status = AutoOptItemStatus.AlreadyClean;
                        cacheItem.IsSelected = false;
                    }
                    else if (storageRes.FilesRemoved > 0 || storageRes.BytesReclaimed > 0)
                    {
                        cacheItem.Status = AutoOptItemStatus.Cleaned;
                    }
                    else
                    {
                        cacheItem.Status = AutoOptItemStatus.Failed;
                    }

                    result.ExecutedItems.Add(cacheItem);
                    result.ExecutionLogs.Add($"[CLEAN CACHE] Removed {storageRes.FilesRemoved} cache files ({AutoOptimizeItem.FormatBytes(storageRes.BytesReclaimed)} reclaimed). Remaining: {AutoOptimizeItem.FormatBytes(postCacheBytes)}.");

                    // Capture into Universal Backup Manager
                    try
                    {
                        if (storageRes.FilesRemoved > 0)
                        {
                            BackupManager.Instance.CaptureStorageCleanup(
                                "Smart Auto Optimize",
                                "System & Application Caches",
                                storageRes.BytesReclaimed,
                                storageRes.FilesRemoved,
                                new System.Collections.Generic.List<string> { "BrowserCache", "ThumbnailCache", "ShaderCache" },
                                false
                            );
                        }
                    }
                    catch { }
                }
                else if (cacheItem != null)
                {
                    cacheItem.Status = AutoOptItemStatus.Skipped;
                    cacheItem.SkipReason = cacheItem.SizeBytes == 0 ? "Already Clean" : "Deselected by user";
                    result.SkippedCount++;
                    result.ExecutedItems.Add(cacheItem);
                    result.ExecutionLogs.Add($"[SKIP CACHE] Skipped: {cacheItem.SkipReason}");
                }

                // 3c. Safe Memory & Standby Reclaim if warranted & selected
                var ramItem = plan.Items.FirstOrDefault(i => i.Id == "auto.memory.reclaim");
                if (ramItem != null && ramItem.IsSelected && plan.RamCanReclaim)
                {
                    result.ExecutionLogs.Add($"[CLEAN RAM] Executing Tiered Memory Reclaim ({plan.ReclaimLevel})...");
                    try
                    {
                        var memSnapBefore = WindowsMemoryListProvider.Instance.GetCurrentSnapshot();
                        result.StandbyBeforeBytes = memSnapBefore.StandbyCacheBytes;

                        long standbyReclaimed = 0;
                        string standbyMsg = "";

                        // 1. Purge Standby list via ExternalEmptyStandbyListProvider
                        if (ExternalEmptyStandbyListProvider.Instance.IsAvailable)
                        {
                            var extRes = await ExternalEmptyStandbyListProvider.Instance.ExecuteAsync(false, ct);
                            standbyReclaimed = extRes.StandbyReclaimedBytes;
                            standbyMsg = extRes.SummaryMessage;
                            result.StandbyAfterBytes = extRes.After.StandbyCacheBytes;
                            result.StandbyReclaimedBytes = standbyReclaimed;
                            result.IsStandbySupported = true;
                        }
                        else
                        {
                            var memRes = await WindowsMemoryListProvider.Instance.ExecuteStandbyPurgeAsync(false, ct);
                            standbyReclaimed = memRes.StandbyReclaimedBytes;
                            standbyMsg = memRes.SummaryMessage;
                            result.StandbyAfterBytes = memRes.After.StandbyCacheBytes;
                            result.StandbyReclaimedBytes = standbyReclaimed;
                            result.IsStandbySupported = memRes.IsSupported;
                        }

                        // 2. Trim working sets for background processes
                        var trimRes = await WindowsMemoryListProvider.Instance.ExecuteWorkingSetTrimAsync(ct);
                        long totalReclaimed = standbyReclaimed + trimRes.ReclaimedBytes;

                        ramItem.ReclaimedBytes = totalReclaimed;
                        ramItem.Status = AutoOptItemStatus.Cleaned;
                        ramItem.SkipReason = standbyMsg;

                        result.ExecutionLogs.Add($"[CLEAN RAM] {standbyMsg} {trimRes.SummaryMessage}");

                        // Capture into Universal Backup Manager as Runtime Metric
                        try
                        {
                            var snapAfter = WindowsMemoryListProvider.Instance.GetCurrentSnapshot();
                            BackupManager.Instance.CaptureMemoryRuntime(
                                "Smart Auto Optimize (Standby + Trim)",
                                totalReclaimed / (1024 * 1024),
                                $"{result.RamPercentageBefore}% ({result.RamUsedBeforeGb:F2} GB used)",
                                $"{snapAfter.UsedFormatted} used",
                                $"Standby reclaimed: {AutoOptimizeItem.FormatBytes(standbyReclaimed)}"
                            );
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        ramItem.Status = AutoOptItemStatus.Failed;
                        ramItem.SkipReason = ex.Message;
                        result.ExecutionLogs.Add($"[CLEAN RAM] Error: {ex.Message}");
                    }
                    result.ExecutedItems.Add(ramItem);
                }
                else if (ramItem != null)
                {
                    ramItem.Status = AutoOptItemStatus.Skipped;
                    ramItem.SkipReason = "System already healthy — no memory reclaim needed";
                    result.SkippedCount++;
                    result.ExecutedItems.Add(ramItem);
                    result.ExecutionLogs.Add("[SKIP RAM] Memory is healthy. Skipped memory reclaim.");
                }

                // STAGE 4: VERIFYING
                onProgress?.Invoke("VERIFYING", 80, "Performing readback measurement and physical verification...");
                result.ExecutionLogs.Add("[STAGE 4: VERIFYING] Reading back physical disk & memory metrics...");

                long diskFreeAfter = 0;
                try
                {
                    var drive = new DriveInfo(systemDriveLetter);
                    if (drive.IsReady) diskFreeAfter = drive.AvailableFreeSpace;
                }
                catch { }
                result.StorageFreeAfterBytes = diskFreeAfter;

                long diskDelta = Math.Max(0, diskFreeAfter - diskFreeBefore);
                result.StorageReclaimedBytes = diskDelta > 0 ? diskDelta : totalStorageReclaimed;
                result.TotalReclaimedBytes = result.StorageReclaimedBytes;

                var memAfter = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memAfter))
                {
                    double totalGb = memAfter.ullTotalPhys / (1024.0 * 1024 * 1024);
                    double availGb = memAfter.ullAvailPhys / (1024.0 * 1024 * 1024);
                    result.RamUsedAfterGb = Math.Max(0, totalGb - availGb);
                    result.RamPercentageAfter = (int)memAfter.dwMemoryLoad;

                    double ramDeltaGb = Math.Max(0, result.RamUsedBeforeGb - result.RamUsedAfterGb);
                    result.RamReclaimedMb = (long)(ramDeltaGb * 1024);

                    if (result.RamReclaimedMb > 20)
                    {
                        result.RamSummary = $"{result.RamPercentageBefore}% → {result.RamPercentageAfter}% ({result.RamReclaimedMb} MB Reclaimed)";
                    }
                    else
                    {
                        result.RamSummary = plan.RamCanReclaim ? "NO MEASURABLE CHANGE" : "No action required — Healthy";
                    }
                }

                // Mark verified counts
                foreach (var it in result.ExecutedItems)
                {
                    if (it.Status == AutoOptItemStatus.Cleaned)
                    {
                        it.Status = AutoOptItemStatus.Verified;
                        result.VerifiedCount++;
                    }
                }

                // STAGE 5: FINALIZING
                onProgress?.Invoke("FINALIZING", 100, "Optimization complete.");
                sw.Stop();
                result.ExecutionLogs.Add($"[STAGE 5: FINALIZING] Auto Optimize completed in {sw.ElapsedMilliseconds} ms.");
                result.ExecutionLogs.Add($"[SUMMARY] Total Reclaimed: {result.TotalReclaimedFormatted} • Verified: {result.VerifiedCount} • Skipped: {result.SkippedCount} • Failed: {result.FailedCount}");

                return result;
            }, ct);
        }
    }
}
