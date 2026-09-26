using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Interfaces
{
    public class BackupTransaction
    {
        public string TransactionId { get; set; } = "";
        public string ProfileId { get; set; } = "Manual";
        public string ProfileName { get; set; } = "Manual Optimization";
        public string OptimizationId { get; set; } = "";
        public string OptimizationName { get; set; } = "";
        public string Category { get; set; } = "Registry";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string MachineId { get; set; } = Environment.MachineName;
        public string WindowsBuild { get; set; } = Environment.OSVersion.VersionString;
        public string User { get; set; } = Environment.UserName;
        public string RiskLevel { get; set; } = "SAFE";
        public string OperationType { get; set; } = "RegistryValue";
        
        public string BeforeState { get; set; } = "";
        public string AfterState { get; set; } = "";
        public string TargetKey { get; set; } = "";
        public string ValueName { get; set; } = "";
        public string ValueType { get; set; } = "";
        public bool PreviousExists { get; set; } = true;
        
        public string BackupLocation { get; set; } = "";
        public string RollbackMethod { get; set; } = "RegistryValueRestore";
        public bool RequiresRestart { get; set; } = false;
        public string Status { get; set; } = "VERIFIED";
        public string VerificationStatus { get; set; } = "VERIFIED";
        public bool CanRollback { get; set; } = true;
        public string RollbackStatus { get; set; } = "AVAILABLE";
        public string ParentTransactionId { get; set; } = "";
        public string Notes { get; set; } = "";
        public List<string> AffectedPaths { get; set; } = new();

        public string DisplayTimestamp => Timestamp.ToLocalTime().ToString("MMM dd, yyyy  hh:mm:ss tt");
        public string StatusBadgeBrushKey => Status switch
        {
            "VERIFIED" or "APPLIED" => "SuccessBrush",
            "ROLLED_BACK" => "AccentBrush",
            "FAILED" => "DangerBrush",
            _ => "TextMutedBrush"
        };

        public string RollbackBadgeBrushKey => RollbackStatus switch
        {
            "AVAILABLE" => "SuccessBrush",
            "RESTORED" => "AccentBrush",
            "NOT_SUPPORTED" => "TextMutedBrush",
            "FAILED" => "DangerBrush",
            _ => "TextMutedBrush"
        };
    }

    public class ProfileBackupGroup
    {
        public string ProfileName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public DateTime? LastBackupTime { get; set; }
        public int TotalTransactions { get; set; }
        public int RestorableTransactions { get; set; }
        public int FailedTransactions { get; set; }
        public List<BackupTransaction> Transactions { get; set; } = new();

        public string DisplayLastBackup => LastBackupTime.HasValue 
            ? LastBackupTime.Value.ToLocalTime().ToString("MMM dd, yyyy  hh:mm tt") 
            : "No backups recorded";
        public string StatusSummary => $"{TotalTransactions} transactions • {RestorableTransactions} restorable";
    }

    public class BackupSummaryStats
    {
        public int TotalBackups { get; set; }
        public int ProfileBackups { get; set; }
        public int OptimizationBackups { get; set; }
        public int RegistryBackups { get; set; }
        public int SystemSnapshots { get; set; }
        public int RestorableCount { get; set; }
        public int PartialCount { get; set; }
        public int NonReversibleCount { get; set; }
        public int FailedCount { get; set; }
    }

    public class RestoreResult
    {
        public bool Success { get; set; }
        public int RestoredCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public string Message { get; set; } = "";
        public string BackupName { get; set; } = "";
        public string Category { get; set; } = "";
        public string VerificationResult { get; set; } = "100% Verified";
        public double ElapsedSeconds { get; set; }
        public bool IsPartial => FailedCount > 0 && RestoredCount > 0;
        public List<string> RestoredItems { get; set; } = new();
        public List<string> ErrorLogs { get; set; } = new();
    }

    public interface IBackupManager
    {
        // Core Universal Transaction API
        string BeginBatchTransaction(string profileId, string profileName, string category);
        BackupTransaction RecordTransaction(BackupTransaction transaction);
        bool CommitBatchTransaction(string batchTransactionId);

        // Individual Transaction Capture Helpers
        BackupTransaction CaptureRegistryTweak(string profileName, string optimizationName, string hive, string subKey, string valueName, object? oldValue, string oldType, object? targetValue, string riskLevel = "SAFE", string? parentBatchId = null);
        BackupTransaction CaptureServiceTweak(string profileName, string serviceName, string previousStartup, string newStartup, string previousState, string riskLevel = "SAFE", string? parentBatchId = null);
        BackupTransaction CapturePowerPlanTweak(string profileName, string planName, string previousGuid, string newGuid, string riskLevel = "SAFE", string? parentBatchId = null);
        BackupTransaction CaptureInputTweak(string profileName, string optimizationName, string targetSetting, object? previousValue, object? newValue, string riskLevel = "SAFE", string? parentBatchId = null);
        BackupTransaction CaptureNetworkTweak(string profileName, string settingName, string targetInterface, object? previousValue, object? newValue, string riskLevel = "SAFE", string? parentBatchId = null);
        BackupTransaction CaptureStorageCleanup(string profileName, string categoryName, long reclaimedBytes, int filesDeleted, List<string> targetPaths, bool wasQuarantined = false, string? parentBatchId = null);
        BackupTransaction CaptureMemoryRuntime(string profileName, long ramReclaimedMb, string loadBefore, string loadAfter, string details, string? parentBatchId = null);
        BackupTransaction CaptureSystemRepair(string profileName, string operationName, string beforeState, string afterState, string? parentBatchId = null);
        BackupTransaction CaptureGenericTweak(string profileName, string optimizationId, string name, string category, string operationType, string beforeState, string afterState, string rollbackMethod, bool canRollback, string riskLevel = "SAFE", string? parentBatchId = null);

        // Rollback & Restore API
        Task<RestoreResult> RestoreSingleTransactionAsync(string transactionId, CancellationToken ct = default);
        Task<RestoreResult> RestoreProfileAsync(string profileName, CancellationToken ct = default);
        Task<RestoreResult> RestoreFullSystemSnapshotAsync(CancellationToken ct = default);
        Task<bool> CreateSystemRestorePointAsync(string description, CancellationToken ct = default);

        // Querying & Statistics API
        List<BackupTransaction> GetAllTransactions();
        List<ProfileBackupGroup> GetProfileBackupGroups();
        BackupSummaryStats GetSummaryStats();
        BackupTransaction? GetTransactionById(string transactionId);
        bool DeleteTransaction(string transactionId);
        void ClearOldBackups(int retentionDays);

        // Real-time notification event
        event Action<BackupTransaction>? TransactionRecorded;

        // Legacy compatibility
        bool CreateSystemRestorePoint(string description);
        bool BackupRegistryState(IEnumerable<string> registryPaths);
        bool BackupServiceState(IEnumerable<string> serviceNames);
        bool LogKilledProcesses(IEnumerable<string> processPaths);
        bool RestoreAll();
        bool BackupRegistryValue(string owner, string registryKey, string valueName, object oldValue, string oldType, object targetValue, string result);
        bool RestoreByOwner(string owner);
    }
}
