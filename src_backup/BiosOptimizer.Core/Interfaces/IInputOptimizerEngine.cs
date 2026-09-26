using System.Collections.Generic;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Interfaces
{
    public class InputOptimizationAction
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
        public bool RequiresReboot { get; set; } = false;
        
        // Advanced Execution States
        public string SupportState { get; set; } = "UNKNOWN"; // SUPPORTED, NOT AVAILABLE, UNSUPPORTED, FAILED
        public string ExecutionState { get; set; } = "IDLE";
        public string VerificationState { get; set; } = "UNVERIFIED";
        public string RollbackInformation { get; set; } = string.Empty;

        // General status string mapped by IPC for GUI backwards-compat
        public string Status { get; set; } = string.Empty;
    }

    public class InputOptimizationPlan
    {
        public List<InputOptimizationAction> Actions { get; set; } = new List<InputOptimizationAction>();
    }

    public interface IInputOptimizerEngine
    {
        Task<InputOptimizationPlan> PlanInputOptimizationAsync();
        Task<(bool Success, string Message)> ApplyInputOptimizationAsync(string riskLevel); 
        Task<(bool Success, string Message)> RestoreInputOptimizationAsync();
        void ProcessPendingReboots();
    }
}
