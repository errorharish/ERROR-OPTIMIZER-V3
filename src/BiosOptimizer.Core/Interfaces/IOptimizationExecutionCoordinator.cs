#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Interfaces
{
    public enum OptimizationCategory
    {
        Memory,
        Storage,
        Gpu,
        Power,
        Network,
        Registry,
        Input,
        Global
    }

    public enum OptimizationOperationState
    {
        Idle,
        Queued,
        Running,
        Verifying,
        Success,
        Failed,
        TimedOut,
        Cancelled
    }

    public class OptimizationExecutionContext
    {
        public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
        public string OperationName { get; set; } = "";
        public OptimizationCategory Category { get; set; } = OptimizationCategory.Global;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
        public OptimizationOperationState State { get; set; } = OptimizationOperationState.Idle;
        public DateTime StartTimeUtc { get; set; }
        public DateTime EndTimeUtc { get; set; }
        public TimeSpan Duration => EndTimeUtc > StartTimeUtc ? EndTimeUtc - StartTimeUtc : TimeSpan.Zero;
        public string StatusMessage { get; set; } = "";
        public string ErrorDetails { get; set; } = "";
        public bool Succeeded { get; set; }
        public int CallerThreadId { get; set; }
        public bool IsStaThread { get; set; }
    }

    public interface IOptimizationExecutionCoordinator
    {
        OptimizationOperationState GetCategoryState(OptimizationCategory category);
        bool IsCategoryBusy(OptimizationCategory category);

        event Action<OptimizationExecutionContext>? OperationStateChanged;

        Task<OptimizationExecutionContext> ExecuteAsync(
            string operationName,
            OptimizationCategory category,
            Func<CancellationToken, Task<string>> executeAction,
            TimeSpan? timeout = null,
            CancellationToken ct = default);
    }
}