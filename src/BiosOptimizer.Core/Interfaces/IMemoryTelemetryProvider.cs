using System;

namespace BiosOptimizer.Core.Interfaces
{
    public class MemoryTelemetryState
    {
        public long TotalPhysicalBytes { get; set; }
        public long UsedBytes { get; set; }
        public long AvailableBytes { get; set; }
        public long FreeBytes { get; set; }
        public long CachedBytes { get; set; } // Task Manager-equivalent SystemCache
        public long StandbyBytes { get; set; } // Kernel Standby List (priorities 0-7)
        public long LowPriorityStandbyBytes { get; set; } // Standby priorities 0-2
        public long ModifiedBytes { get; set; }
        public long ZeroedBytes { get; set; }
        public long CommittedBytes { get; set; }
        public long CommitLimitBytes { get; set; }
        public long PagedPoolBytes { get; set; }
        public long NonPagedPoolBytes { get; set; }
        public double MemoryPressurePercent { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsNativeKernelQueried { get; set; }
        public string DiagnosticSummary { get; set; } = string.Empty;

        // Formatted Helpers
        public string TotalFormatted => FormatGb(TotalPhysicalBytes);
        public string UsedFormatted => FormatGb(UsedBytes);
        public string AvailableFormatted => FormatGb(AvailableBytes);
        public string FreeFormatted => FormatMbOrGb(FreeBytes);
        public string CachedFormatted => FormatGb(CachedBytes);
        public string StandbyFormatted => FormatGb(StandbyBytes);
        public string LowPriorityStandbyFormatted => FormatMbOrGb(LowPriorityStandbyBytes);
        public string ModifiedFormatted => FormatMb(ModifiedBytes);
        public string ReclaimableFormatted => FormatGb(StandbyBytes > 0 ? StandbyBytes : LowPriorityStandbyBytes);

        private static string FormatGb(long bytes) => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        private static string FormatMb(long bytes) => $"{bytes / (1024.0 * 1024.0):F1} MB";
        private static string FormatMbOrGb(long bytes)
        {
            double gb = bytes / (1024.0 * 1024.0 * 1024.0);
            if (gb >= 1.0) return $"{gb:F2} GB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }

    public interface IMemoryTelemetryProvider
    {
        MemoryTelemetryState SampleCurrentMemoryState();
        bool IsStandbyReclaimSupported { get; }
    }
}
