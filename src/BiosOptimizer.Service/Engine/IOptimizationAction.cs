using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.IPC.Contracts;

namespace BiosOptimizer.Service.Engine
{
    public interface IOptimizationAction
    {
        string Id { get; }
        string Name { get; }
        string Category { get; }
        string Description { get; }
        string RiskLevel { get; } // Safe, Medium, High
        bool RequiresRestart { get; }

        /// <summary>
        /// Hardware and software checks to ensure this is safe to run.
        /// </summary>
        Task<(bool IsApplicable, string Reason)> CheckApplicabilityAsync(MachineProfileDto machineProfile, WorkloadProfileDto workloadProfile, CancellationToken ct = default);

        /// <summary>
        /// Reads current value and determines if it is already optimized.
        /// </summary>
        Task<(bool AlreadyOptimized, string CurrentValue)> DetectAsync(CancellationToken ct = default);

        /// <summary>
        /// Backups existing state before modifying.
        /// </summary>
        Task<bool> BackupAsync(CancellationToken ct = default);

        /// <summary>
        /// Applies the optimization.
        /// </summary>
        Task<bool> ApplyAsync(CancellationToken ct = default);

        /// <summary>
        /// Reads back the value to verify it was set correctly.
        /// </summary>
        Task<bool> VerifyAsync(CancellationToken ct = default);

        /// <summary>
        /// Restores from the backup.
        /// </summary>
        Task<bool> RollbackAsync(CancellationToken ct = default);
    }
}
