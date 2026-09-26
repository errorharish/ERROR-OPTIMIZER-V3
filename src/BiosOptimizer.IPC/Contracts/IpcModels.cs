namespace BiosOptimizer.IPC.Contracts
{
    public enum IpcMessageType
    {
        GetVersion,
        GetDashboard,
        GetOptimizationScore,
        GetOptimizationSummary,
        GetCapabilities,
        GetSystemInfo,
        GetHardwareInfo,
        GetStartupItems,
        EnableStartupItem,
        DisableStartupItem,
        GetServices,
        SetServiceStartupType,
        ChangeServiceState,
        GetNetworkStatus,
        GetStorageStatus,
        PreviewTier,
        ApplyTier,
        RestoreTier,
        ScanCleaner,
        ApplyCleaner,
        GetDebloatItems,
        ApplyDebloatItems,
        GetBiosCapabilities,
        ApplyBiosSetting,
        GetBackups,
        RestoreBackup,
        GetMachineProfile,
        GetProcessCandidates,
        TerminateProcesses,
        RestoreAllSystem,
        PlanInputOptimization,
        ApplyInputOptimization,
        RestoreInputOptimization,
        PlanRegistryTweak,
        ApplyRegistryTweak,
        RestoreRegistryTweak,
        PlanNetworkOptimization,
        ApplyNetworkOptimization,
        RestoreNetworkOptimization,
        FlushDnsCache,
        RunNetworkDiagnostics,
        // Storage Optimizer (new authoritative storage engine)
        ScanStorageCategories,
        ApplyStorageCleanup,
        GetRecycleBinInfo,
        EmptyRecycleBin,
        ScanLargeFiles,
        ScanDuplicateFiles,
        DeleteStorageFile,
        // Power Plan Optimization
        GetPowerPlans,
        ApplyPowerPlan,
        RestorePowerPlan,
        EnableUltimatePerformance,
        CreateCustomAiPlan,
        DeleteCustomAiPlan,
        // System Restore Points & System Protection
        CreateRestorePoint,
        GetRestorePoints,
        CheckSystemRestoreStatus,
        EnableSystemProtection,
        GetSystemProtectionStatus,
        DisableSystemProtection,
        // Windows Servicing & Health Tools
        ExecuteServicingTool,
        GetServicingEnvironment,
        EnsureServicingPrerequisites
    }

    public class IpcMessage
    {
        public IpcMessageType Type { get; set; }
        public string? Payload { get; set; }
    }

    public class IpcResponse
    {
        public bool Success { get; set; }
        public string? Data { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class OptimizationActionDto
    {
        public string ItemId { get; set; } = string.Empty;
        public string ActionName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool IsInherited { get; set; }
        public string Risk { get; set; } = string.Empty;
        public string Warning { get; set; } = string.Empty;
        public string CurrentState { get; set; } = string.Empty;
        public string TargetState { get; set; } = string.Empty;
        public string Status { get; set; } = "Available"; // Available, Running, Verified, Skipped, Failed
        public string Reason { get; set; } = string.Empty;
        public bool Supported { get; set; } = true;
        public bool Applicable { get; set; } = true;
        public bool IsHighlyRecommended { get; set; } = false;
        public double RecommendationScore { get; set; } = 0.0;
        public string ExecutionType { get; set; } = "WINDOWS_AUTOMATIC";
        public bool IsSelected { get; set; } = true;
    }

    public class ApplyTierRequestDto
    {
        public string TierId { get; set; } = string.Empty;
        public System.Collections.Generic.List<string> SelectedItemIds { get; set; } = new();
    }

    public class TierPreviewDto
    {
        public string TierId { get; set; } = string.Empty;
        public string TierName { get; set; } = string.Empty;
        public int ActionCount { get; set; }
        public bool CanApply { get; set; }
        public bool CanRestore { get; set; }
        public System.Collections.Generic.List<OptimizationActionDto> Actions { get; set; } = new();
    }

    public class DebloatItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
        public string Status { get; set; } = "Detected";
    }

    public class DebloatRequestDto
    {
        public System.Collections.Generic.List<string> SelectedIds { get; set; } = new();
    }

    public class IpcHandshakeResponse
    {
        public string ServiceName { get; set; } = string.Empty;
        public string ServiceVersion { get; set; } = string.Empty;
        public string BuildId { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string ExecutablePath { get; set; } = string.Empty;
        public string ProtocolVersion { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public System.Collections.Generic.List<string> Capabilities { get; set; } = new();
    }

    public class InputOptimizationActionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Risk { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;
        public string TargetValue { get; set; } = string.Empty;
        public bool Applicable { get; set; }
        public bool AlreadyOptimized { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool RequiresReboot { get; set; }
        public string Status { get; set; } = string.Empty;
    }


    public class InputOptimizationPlanDto
    {
        public System.Collections.Generic.List<InputOptimizationActionDto> Actions { get; set; } = new();
    }

    public class ServicingToolRequestDto
    {
        public string ToolId { get; set; } = string.Empty;
        public string? Arguments { get; set; }
        public int TimeoutSeconds { get; set; } = 7200;
    }

    public class ServicingToolResponseDto
    {
        public string ToolId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public bool ProcessStarted { get; set; }
        public int ProcessId { get; set; }
        public int ExitCode { get; set; }
        public bool Success { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string DiagnosedRootCause { get; set; } = string.Empty;
        public string RecommendedRemediation { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public bool IsCancelled { get; set; }
        public bool IsTimedOut { get; set; }
        public bool IsNotApplicable { get; set; }
        public string StandardOutput { get; set; } = string.Empty;
        public string StandardError { get; set; } = string.Empty;
        public System.Collections.Generic.List<string> RelevantLogExcerpts { get; set; } = new();
    }
}
