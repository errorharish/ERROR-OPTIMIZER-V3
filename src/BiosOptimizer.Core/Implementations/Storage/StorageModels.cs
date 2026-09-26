using System;
using System.Collections.Generic;

namespace BiosOptimizer.Core.Implementations.Storage
{
    public class StorageDriveInfo
    {
        public string Name { get; set; } = string.Empty; // e.g. "C:\"
        public string Letter => Name.TrimEnd('\\');
        public string VolumeLabel { get; set; } = string.Empty;
        public string DriveFormat { get; set; } = string.Empty; // NTFS, ReFS, FAT32
        public string DriveType { get; set; } = string.Empty; // Fixed, Removable, Network
        public string MediaType { get; set; } = "NVMe / SSD"; // SSD, HDD, NVMe, USB
        public string Model { get; set; } = "Solid State Drive";
        public string InterfaceType { get; set; } = "NVMe"; // NVMe, SATA, USB
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
        public long UsedBytes => TotalBytes > FreeBytes ? TotalBytes - FreeBytes : 0;
        public double TotalGb => Math.Round(TotalBytes / (1024.0 * 1024 * 1024), 1);
        public double UsedGb => Math.Round(UsedBytes / (1024.0 * 1024 * 1024), 1);
        public double FreeGb => Math.Round(FreeBytes / (1024.0 * 1024 * 1024), 1);
        public double UsagePercentage => TotalBytes > 0 ? Math.Round((double)UsedBytes / TotalBytes * 100.0, 1) : 0;
        
        public string HealthStatus { get; set; } = "EXCELLENT"; // EXCELLENT, GOOD, WARNING, CRITICAL, NOT AVAILABLE
        public string SmartStatus { get; set; } = "OK"; // OK, PREDICT_FAILURE, NOT AVAILABLE
        public string TemperatureText { get; set; } = "NOT AVAILABLE"; // e.g. "38°C" or "NOT AVAILABLE"
        public string StoragePressure { get; set; } = "LOW"; // LOW (<70%), MODERATE (70-85%), HIGH (85-95%), CRITICAL (>95%)
        public bool IsSystemDrive { get; set; }
        public bool IsReady { get; set; }
    }

    public enum CleanupApplicabilityState
    {
        Applicable,
        AlreadyClean,
        NotApplicable,
        Unsupported,
        RequiresElevation,
        FailedToScan
    }

    public enum CategoryVerificationStatus
    {
        NotCleaned,
        Verified,
        Partial,
        Failed
    }

    public class StorageCleanupCategory
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = ""; // MDL2 glyph
        public string CategoryType { get; set; } = "TEMP"; // TEMP, CACHE, UPDATE, LOGS, DUMPS, BROWSER, RECYCLEBIN, SHADER, OTHER
        public string RiskLevel { get; set; } = "SAFE"; // SAFE, LOW RISK, CAUTION, DO NOT TOUCH
        public long DetectedBytes { get; set; }
        public int FileCount { get; set; }
        public bool IsSelected { get; set; } = true;
        public bool IsScanned { get; set; }
        public List<string> TargetPaths { get; set; } = new();
        public string FormattedSize => FormatBytes(DetectedBytes);

        public CleanupApplicabilityState Applicability { get; set; } = CleanupApplicabilityState.Applicable;
        public string ApplicabilityDisplay => Applicability switch
        {
            CleanupApplicabilityState.Applicable => "APPLICABLE",
            CleanupApplicabilityState.AlreadyClean => "ALREADY CLEAN",
            CleanupApplicabilityState.NotApplicable => "NOT APPLICABLE",
            CleanupApplicabilityState.Unsupported => "UNSUPPORTED",
            CleanupApplicabilityState.RequiresElevation => "REQUIRES ELEVATION",
            CleanupApplicabilityState.FailedToScan => "FAILED TO SCAN",
            _ => "UNKNOWN"
        };

        public CategoryVerificationStatus VerifiedStatus { get; set; } = CategoryVerificationStatus.NotCleaned;
        public string VerifiedStatusDisplay => VerifiedStatus switch
        {
            CategoryVerificationStatus.Verified => "VERIFIED",
            CategoryVerificationStatus.Partial => "PARTIALLY CLEANED",
            CategoryVerificationStatus.Failed => "FAILED",
            _ => "NOT CLEANED"
        };

        public int DeletedCount { get; set; }
        public long DeletedBytes { get; set; }
        public string FormattedDeletedBytes => FormatBytes(DeletedBytes);
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public bool RequiresAdmin { get; set; }
        public string Scope { get; set; } = "User & System Paths";

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }

    public class StorageTransactionLog
    {
        public string TransactionId { get; set; } = Guid.NewGuid().ToString("N");
        public string Category { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long BeforeBytes { get; set; }
        public int BeforeFiles { get; set; }
        public int SelectedFiles { get; set; }
        public int DeletedFiles { get; set; }
        public int SkippedFiles { get; set; }
        public int FailedFiles { get; set; }
        public long AfterBytes { get; set; }
        public int AfterFiles { get; set; }
        public string Status { get; set; } = "COMPLETED"; // COMPLETED, PARTIALLY_COMPLETED, FAILED, CANCELLED, TIMED_OUT
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime EndTime { get; set; } = DateTime.Now;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class LargeFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public double SizeMb => Math.Round(SizeBytes / (1024.0 * 1024), 1);
        public string FormattedSize => StorageCleanupCategory.FormatBytes(SizeBytes);
        public DateTime LastModified { get; set; }
        public string DriveLetter { get; set; } = "C:";
    }

    public class DuplicateFileGroup
    {
        public string GroupId { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string FormattedFileSize => StorageCleanupCategory.FormatBytes(FileSizeBytes);
        public string Hash { get; set; } = string.Empty;
        public List<string> FilePaths { get; set; } = new();
        public long ReclaimableBytes => FilePaths.Count > 1 ? FileSizeBytes * (FilePaths.Count - 1) : 0;
        public string FormattedReclaimable => StorageCleanupCategory.FormatBytes(ReclaimableBytes);
    }

    public class StorageScanReport
    {
        public List<StorageDriveInfo> Drives { get; set; } = new();
        public List<StorageCleanupCategory> Categories { get; set; } = new();
        public long TotalReclaimableBytes { get; set; }
        public string FormattedTotalReclaimable => StorageCleanupCategory.FormatBytes(TotalReclaimableBytes);
        public int TotalFilesFound { get; set; }
        public string OverallStorageHealth { get; set; } = "EXCELLENT";
        public DateTime ScanTimestamp { get; set; } = DateTime.UtcNow;
    }

    public class StorageCleanupProgressReport
    {
        public string CurrentStage { get; set; } = "ANALYZING";
        public int ProgressPercent { get; set; }
        public string CurrentItemName { get; set; } = string.Empty;
        public int FilesProcessed { get; set; }
        public int FilesRemoved { get; set; }
        public int FilesSkipped { get; set; }
        public int FilesFailed { get; set; }
        public long BytesReclaimed { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }

    public class StorageCleanupResult
    {
        public bool Success { get; set; }
        public long BytesReclaimed { get; set; }
        public string FormattedReclaimed => StorageCleanupCategory.FormatBytes(BytesReclaimed);
        public int FilesRemoved { get; set; }
        public int FilesSkipped { get; set; }
        public int FilesFailed { get; set; }
        public long InitialFreeBytes { get; set; }
        public long FinalFreeBytes { get; set; }
        public string SummaryMessage { get; set; } = string.Empty;
        public List<string> DetailedLog { get; set; } = new();
    }

    public class StorageFileItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string FormattedSize => StorageCleanupCategory.FormatBytes(SizeBytes);
        public DateTime LastModified { get; set; }
        public string FormattedDate => LastModified.ToString("yyyy-MM-dd HH:mm");
        public string DriveLetter { get; set; } = "C:";
        public string FileTypeCategory { get; set; } = "OTHER"; // VIDEOS, IMAGES, AUDIO, DOCUMENTS, ARCHIVES, EXECUTABLES, ISO, PROJECTS, OTHER
        public string SafetyStatus { get; set; } = "REVIEW"; // SAFE TO REMOVE, REVIEW, SYSTEM/PROTECTED, UNKNOWN
        public bool IsSelected { get; set; }
    }

    public class StorageScanProgress
    {
        public long FilesScannedCount { get; set; }
        public long FoldersScannedCount { get; set; }
        public string CurrentScanningPath { get; set; } = string.Empty;
        public long TotalResultsCount { get; set; }
        public long TotalScannedBytes { get; set; }
        public double ElapsedSeconds { get; set; }
        public bool IsCompleted { get; set; }
    }

    public class StorageScanSummary
    {
        public long TotalFilesFound { get; set; }
        public long TotalScannedBytes { get; set; }
        public string FormattedTotalScannedBytes => StorageCleanupCategory.FormatBytes(TotalScannedBytes);

        public string LargestFileName { get; set; } = "None";
        public long LargestFileBytes { get; set; }
        public string FormattedLargestFileSize => StorageCleanupCategory.FormatBytes(LargestFileBytes);

        public long VideosBytes { get; set; }
        public long ImagesBytes { get; set; }
        public long AudioBytes { get; set; }
        public long DocumentsBytes { get; set; }
        public long ExecutablesBytes { get; set; }
        public long ArchivesBytes { get; set; }
        public long IsoBytes { get; set; }
        public long ProjectsBytes { get; set; }
        public long OtherBytes { get; set; }

        public string FormattedVideosBytes => StorageCleanupCategory.FormatBytes(VideosBytes);
        public string FormattedImagesBytes => StorageCleanupCategory.FormatBytes(ImagesBytes);
        public string FormattedAudioBytes => StorageCleanupCategory.FormatBytes(AudioBytes);
        public string FormattedDocumentsBytes => StorageCleanupCategory.FormatBytes(DocumentsBytes);
        public string FormattedExecutablesBytes => StorageCleanupCategory.FormatBytes(ExecutablesBytes);
        public string FormattedArchivesBytes => StorageCleanupCategory.FormatBytes(ArchivesBytes);
        public string FormattedIsoBytes => StorageCleanupCategory.FormatBytes(IsoBytes);
        public string FormattedProjectsBytes => StorageCleanupCategory.FormatBytes(ProjectsBytes);
        public string FormattedOtherBytes => StorageCleanupCategory.FormatBytes(OtherBytes);
    }

    public class StorageFileDeleteResult
    {
        public bool Success { get; set; }
        public int DeletedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public long ReclaimedBytes { get; set; }
        public string FormattedReclaimedBytes => StorageCleanupCategory.FormatBytes(ReclaimedBytes);
        public string SummaryMessage { get; set; } = string.Empty;
        public List<string> FailedFiles { get; set; } = new();
    }
}
