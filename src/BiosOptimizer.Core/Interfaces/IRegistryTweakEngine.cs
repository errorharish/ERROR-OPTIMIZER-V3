using System.Collections.Generic;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Interfaces
{
    public enum RegistryApplicabilityState
    {
        Applicable,
        AlreadyOptimal,
        NotApplicable,
        Unsupported,
        Unknown,
        RequiresAdmin,
        Conflict,
        FailedToAnalyze
    }

    public enum RegistryApplyState
    {
        None,
        Applied,
        AppliedPendingReboot,
        Failed,
        RolledBack
    }

    public enum RegistryVerificationState
    {
        Unverified,
        Verified,
        PendingReboot,
        VerificationFailed,
        ExternallyChanged
    }

    public enum MissingValueBehavior
    {
        SafeToCreate,
        NotApplicable,
        Unsupported
    }

    public class RegistryTweakDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;
        public string TargetValue { get; set; } = string.Empty;
        public string BeforeValue { get; set; } = string.Empty;
        public string DefaultValue { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "RECOMMENDED", "ALREADY OPTIMIZED", "VERIFIED", "RESTART REQUIRED", "NOT APPLICABLE", "UNSUPPORTED", "ATTENTION REQUIRED", "EXTERNALLY CHANGED", "FAILED"
        public string Applicability { get; set; } = "APPLICABLE";
        public string VerificationState { get; set; } = "UNVERIFIED";
        public string RiskLevel { get; set; } = "Low";
        public string RootKey { get; set; } = "HKLM";
        public string SubKey { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public string ValueType { get; set; } = "REG_DWORD";
        public string SupportedOS { get; set; } = "Windows 10 / 11 (x64 / ARM64)";
        public string HardwarePrerequisites { get; set; } = "None";
        public bool RequiresAdmin { get; set; } = true;
        public bool RequiresReboot { get; set; } = false;
        public bool RollbackSupported { get; set; } = true;
        public string MissingBehavior { get; set; } = "SafeToCreate";
        public string ApplicabilityReason { get; set; } = string.Empty;
        public string VerificationMethod { get; set; } = "Real-time registry key readback comparison against target";
        public string RegistryView { get; set; } = "Registry64";
        public string PermissionState { get; set; } = "Elevated";
        public string ErrorCode { get; set; } = "0x00000000";
        public string DiagnosticsDetail { get; set; } = string.Empty;
    }

    public class RegistryTweakResponse
    {
        public List<RegistryTweakDto> Tweaks { get; set; } = new List<RegistryTweakDto>();
        public int TotalCount { get; set; }
        public int ApplicableCount { get; set; }
        public int RecommendedCount { get; set; }
        public int AlreadyOptimizedCount { get; set; }
        public int RestartRequiredCount { get; set; }
        public int PendingCount { get; set; }
        public int NotApplicableCount { get; set; }
        public int UnsupportedCount { get; set; }
        public int BlockedCount { get; set; }
        public int AttentionCount { get; set; }
        public int FailedCount { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool Success { get; set; }
    }

    public interface IRegistryTweakEngine
    {
        Task<RegistryTweakResponse> PlanRegistryTweakAsync();
        Task<RegistryTweakResponse> ReconcileStartupAsync();
        Task<(bool Success, string Message, string Status, int AppliedCount, int VerifiedCount, int RestartRequiredCount, int FailedCount)> ApplyRegistryTweaksAsync(List<string> tweakIds);
        Task<(bool Success, string Message, string Status, int RestoredCount, int FailedCount)> RestoreRegistryTweaksAsync(List<string> tweakIds);
    }
}
