#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Interfaces
{
    public enum StandbyCleanupStatus
    {
        SuccessVerified,
        SuccessNoMeaningfulReclaim,
        NotFound,
        ElevationRequired,
        StartFailed,
        Timeout,
        Cancelled,
        NonzeroExit,
        VerificationFailed
    }

    public class StandbyCleanupExecutionResult
    {
        public bool Succeeded => Status == StandbyCleanupStatus.SuccessVerified || Status == StandbyCleanupStatus.SuccessNoMeaningfulReclaim;
        public StandbyCleanupStatus Status { get; set; } = StandbyCleanupStatus.SuccessVerified;
        public string ExecutablePath { get; set; } = string.Empty;
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; } = string.Empty;
        public string StandardError { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public MemorySnapshot Before { get; set; } = new();
        public MemorySnapshot After { get; set; } = new();
        public long StandbyReclaimedBytes => Math.Max(0, Before.StandbyCacheBytes - After.StandbyCacheBytes);
        public string StandbyReclaimedFormatted => MemorySnapshot.FormatBytes(StandbyReclaimedBytes);
        public long AvailableIncreaseBytes => Math.Max(0, After.AvailableBytes - Before.AvailableBytes);
        public string AvailableIncreaseFormatted => MemorySnapshot.FormatBytes(AvailableIncreaseBytes);
        public string SummaryMessage { get; set; } = string.Empty;
        public string ErrorDetails { get; set; } = string.Empty;
    }

    public interface IStandbyCleanupProvider
    {
        bool IsAvailable { get; }
        string ResolvedExecutablePath { get; }
        string FindExecutable();
        bool ValidateExecutable(out string reason);
        Task<StandbyCleanupExecutionResult> ExecuteAsync(bool lowPriorityOnly = false, CancellationToken cancellationToken = default);
    }
}
