using System;
using System.Threading;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Interfaces
{
    public enum MemoryReclaimLevel
    {
        Level0_Healthy = 0,             // Memory healthy -> Do nothing
        Level1_ModeratePressure = 1,     // Moderate pressure (75-85%) -> Safe working-set trimming of eligible idle processes
        Level2_HighPressure = 2,         // High pressure (>85%) -> Working-set trim + Low-priority standby list purge
        Level3_CriticalPressure = 3      // Critical pressure (>90%) -> Full supported standby + working-set reclaim
    }

    public class MemorySnapshot
    {
        public long TotalPhysicalBytes { get; set; }
        public long AvailableBytes { get; set; }
        public long UsedBytes => Math.Max(0, TotalPhysicalBytes - AvailableBytes);
        public double UsedPercentage => TotalPhysicalBytes > 0 ? (double)UsedBytes / TotalPhysicalBytes * 100.0 : 0;
        public double MemoryPressure => UsedPercentage;
        public long StandbyCacheBytes { get; set; }
        public long LowPriorityStandbyBytes { get; set; }
        public long ModifiedBytes { get; set; }
        public long FreeBytes { get; set; }
        public long ZeroedBytes { get; set; }
        public long WorkingSetBytes { get; set; }
        public bool IsStandbyCleanupSupported { get; set; }
        public bool IsLowPriorityStandbySupported { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public string TotalFormatted => FormatBytes(TotalPhysicalBytes);
        public string AvailableFormatted => FormatBytes(AvailableBytes);
        public string UsedFormatted => FormatBytes(UsedBytes);
        public string StandbyFormatted => FormatBytes(StandbyCacheBytes);
        public string LowPriorityStandbyFormatted => FormatBytes(LowPriorityStandbyBytes);
        public string ModifiedFormatted => FormatBytes(ModifiedBytes);
        public string FreeFormatted => FormatBytes(FreeBytes);
        public string ZeroedFormatted => FormatBytes(ZeroedBytes);

        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    public class MemoryCleanupResult
    {
        public bool Succeeded { get; set; }
        public bool WasSkipped { get; set; }
        public bool IsSupported { get; set; } = true;
        public MemoryReclaimLevel LevelApplied { get; set; }
        public MemorySnapshot Before { get; set; } = new();
        public MemorySnapshot After { get; set; } = new();
        public long ReclaimedBytes => Math.Max(0, Before.UsedBytes - After.UsedBytes);
        public long StandbyReclaimedBytes => Math.Max(0, Before.StandbyCacheBytes - After.StandbyCacheBytes);
        public string ReclaimedFormatted => MemorySnapshot.FormatBytes(ReclaimedBytes);
        public string StandbyReclaimedFormatted => MemorySnapshot.FormatBytes(StandbyReclaimedBytes);
        public string SummaryMessage { get; set; } = "";
        public string ErrorDetails { get; set; } = "";
    }

    public interface IStandbyMemoryManager
    {
        bool IsSupported { get; }
        bool IsLowPriorityStandbySupported { get; }
        MemorySnapshot GetCurrentSnapshot();
        MemoryReclaimLevel EvaluateRequiredReclaimLevel(MemorySnapshot snapshot);
        Task<MemoryCleanupResult> ExecuteReclaimAsync(MemoryReclaimLevel level, CancellationToken cancellationToken = default);
        Task<MemoryCleanupResult> ExecuteStandbyPurgeAsync(bool lowPriorityOnly, CancellationToken cancellationToken = default);
        Task<MemoryCleanupResult> ExecuteWorkingSetTrimAsync(CancellationToken cancellationToken = default);
    }
}
