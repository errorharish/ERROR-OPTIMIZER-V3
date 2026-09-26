using System;
using System.Collections.Generic;
using System.Linq;

namespace BiosOptimizer.Core.Models
{
    public enum OptimizationJobState
    {
        Idle,
        Analyzing,
        BackingUp,
        Applying,
        Verifying,
        Finalizing,
        Completed,
        Partial,
        Failed,
        Timeout,
        Cancelled
    }

    public enum FinalizationState
    {
        NotStarted,
        Running,
        Succeeded,
        Failed,
        TimedOut
    }

    public enum OptimizationActionState
    {
        Waiting,
        Applying,
        Verifying,
        Verified,
        Failed,
        Timeout,
        Skipped,
        AlreadyOptimized
    }

    public enum OptimizationStageStatus
    {
        Pending,
        Active,
        Completed,
        Failed
    }

    public class OptimizationJobAction
    {
        public string ActionId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Risk { get; set; } = "Low";
        public string CurrentState { get; set; } = string.Empty;
        public string TargetState { get; set; } = string.Empty;
        public OptimizationActionState State { get; set; } = OptimizationActionState.Waiting;
        public string ErrorMessage { get; set; } = string.Empty;

        public bool IsTerminal => State == OptimizationActionState.Verified ||
                                  State == OptimizationActionState.Failed ||
                                  State == OptimizationActionState.Timeout ||
                                  State == OptimizationActionState.Skipped ||
                                  State == OptimizationActionState.AlreadyOptimized;
    }

    public class OptimizationJob
    {
        public string JobId { get; set; } = Guid.NewGuid().ToString("N");
        public string ProfileTitle { get; set; } = string.Empty;
        public List<OptimizationJobAction> Actions { get; set; } = new();

        public int TotalActions => Actions.Count;
        public int CompletedActions => Actions.Count(a => a.IsTerminal);
        public int AppliedCount => Actions.Count(a => a.State == OptimizationActionState.Verified || a.State == OptimizationActionState.Verifying);
        public int VerifiedCount => Actions.Count(a => a.State == OptimizationActionState.Verified);
        public int FailedCount => Actions.Count(a => a.State == OptimizationActionState.Failed || a.State == OptimizationActionState.Timeout);
        public int SkippedCount => Actions.Count(a => a.State == OptimizationActionState.Skipped || a.State == OptimizationActionState.AlreadyOptimized);

        public OptimizationJobState State { get; private set; } = OptimizationJobState.Idle;
        public FinalizationState FinalizationState { get; private set; } = FinalizationState.NotStarted;

        public OptimizationStageStatus StageAnalyzingStatus { get; private set; } = OptimizationStageStatus.Pending;
        public OptimizationStageStatus StageBackupStatus { get; private set; } = OptimizationStageStatus.Pending;
        public OptimizationStageStatus StageApplyStatus { get; private set; } = OptimizationStageStatus.Pending;
        public OptimizationStageStatus StageVerifyStatus { get; private set; } = OptimizationStageStatus.Pending;
        public OptimizationStageStatus StageFinalizeStatus { get; private set; } = OptimizationStageStatus.Pending;

        public string CurrentActionName { get; private set; } = string.Empty;
        public string CurrentActionCurrentState { get; private set; } = string.Empty;
        public string CurrentActionTarget { get; private set; } = string.Empty;
        public string CurrentActionStatus { get; private set; } = "PENDING";

        public double ProgressPercentage { get; private set; } = 0;
        public bool IsDoneEnabled => State == OptimizationJobState.Completed || 
                                     State == OptimizationJobState.Partial || 
                                     State == OptimizationJobState.Failed || 
                                     State == OptimizationJobState.Timeout ||
                                     State == OptimizationJobState.Cancelled;

        public string ProgressStatusText => $"{CompletedActions} / {TotalActions} OPTIMIZATIONS PROCESSED";

        public string StageAnalyzingText => StageAnalyzingStatus.ToString().ToUpper();
        public string StageBackupText => StageBackupStatus.ToString().ToUpper();
        public string StageApplyText => StageApplyStatus.ToString().ToUpper();
        public string StageVerifyText => StageVerifyStatus.ToString().ToUpper();
        public string StageFinalizeText => StageFinalizeStatus.ToString().ToUpper();

        public event Action? StateChanged;

        private void Notify()
        {
            StateChanged?.Invoke();
        }

        private void RecalculateDynamicState()
        {
            if (State == OptimizationJobState.Idle) return;

            if (State == OptimizationJobState.Completed || 
                State == OptimizationJobState.Partial || 
                State == OptimizationJobState.Failed || 
                State == OptimizationJobState.Timeout ||
                State == OptimizationJobState.Cancelled)
            {
                StageAnalyzingStatus = OptimizationStageStatus.Completed;
                StageBackupStatus = OptimizationStageStatus.Completed;
                StageApplyStatus = OptimizationStageStatus.Completed;
                StageVerifyStatus = OptimizationStageStatus.Completed;
                if ((FinalizationState == FinalizationState.Failed || FinalizationState == FinalizationState.TimedOut) && VerifiedCount == 0)
                {
                    StageFinalizeStatus = OptimizationStageStatus.Failed;
                }
                else
                {
                    StageFinalizeStatus = OptimizationStageStatus.Completed;
                }
                ProgressPercentage = 100;
                return;
            }

            bool anyApplying = Actions.Any(a => a.State == OptimizationActionState.Applying);
            bool anyVerifying = Actions.Any(a => a.State == OptimizationActionState.Verifying);
            bool allTerminal = Actions.Count > 0 && Actions.All(a => a.IsTerminal);

            if (anyApplying || anyVerifying || allTerminal || Actions.Any(a => a.IsTerminal))
            {
                StageAnalyzingStatus = OptimizationStageStatus.Completed;
                StageBackupStatus = OptimizationStageStatus.Completed;

                if (allTerminal)
                {
                    State = OptimizationJobState.Finalizing;
                    if (FinalizationState == FinalizationState.NotStarted)
                    {
                        FinalizationState = FinalizationState.Running;
                    }
                    StageApplyStatus = OptimizationStageStatus.Completed;
                    StageVerifyStatus = OptimizationStageStatus.Completed;
                    StageFinalizeStatus = OptimizationStageStatus.Active;
                    ProgressPercentage = 100;
                    CurrentActionName = "Final System Reconciliation & Register Readback";
                    CurrentActionStatus = "FINALIZING";
                    return;
                }
                else if (anyVerifying)
                {
                    State = OptimizationJobState.Verifying;
                    StageApplyStatus = OptimizationStageStatus.Completed;
                    StageVerifyStatus = OptimizationStageStatus.Active;
                    StageFinalizeStatus = OptimizationStageStatus.Pending;
                }
                else if (anyApplying)
                {
                    State = OptimizationJobState.Applying;
                    StageApplyStatus = OptimizationStageStatus.Active;
                    StageVerifyStatus = OptimizationStageStatus.Pending;
                    StageFinalizeStatus = OptimizationStageStatus.Pending;
                }
                else
                {
                    State = OptimizationJobState.Applying;
                    StageApplyStatus = OptimizationStageStatus.Completed;
                    StageVerifyStatus = OptimizationStageStatus.Completed;
                    StageFinalizeStatus = OptimizationStageStatus.Pending;
                }

                double actionFraction = TotalActions > 0 ? (double)CompletedActions / TotalActions : 0;
                ProgressPercentage = Math.Round(actionFraction * 100.0, 1);
                return;
            }

            if (State == OptimizationJobState.Analyzing)
            {
                StageAnalyzingStatus = OptimizationStageStatus.Active;
                StageBackupStatus = OptimizationStageStatus.Pending;
                StageApplyStatus = OptimizationStageStatus.Pending;
                StageVerifyStatus = OptimizationStageStatus.Pending;
                StageFinalizeStatus = OptimizationStageStatus.Pending;
                ProgressPercentage = 0;
                return;
            }

            if (State == OptimizationJobState.BackingUp)
            {
                StageAnalyzingStatus = OptimizationStageStatus.Completed;
                StageBackupStatus = OptimizationStageStatus.Active;
                StageApplyStatus = OptimizationStageStatus.Pending;
                StageVerifyStatus = OptimizationStageStatus.Pending;
                StageFinalizeStatus = OptimizationStageStatus.Pending;
                ProgressPercentage = 0;
                return;
            }
        }

        public void StartAnalyzing(string details = "Analyzing System Hardware & Target Profile Parameters...")
        {
            State = OptimizationJobState.Analyzing;
            CurrentActionName = details;
            CurrentActionCurrentState = "Current Configuration";
            CurrentActionTarget = "Target Profile Parameters";
            CurrentActionStatus = "ANALYZING";
            ProgressPercentage = 5;
            RecalculateDynamicState();
            Notify();
        }

        public void StartBackup(string details = "Creating Automatic System & Registry Rollback Snapshot...")
        {
            State = OptimizationJobState.BackingUp;
            CurrentActionName = details;
            CurrentActionCurrentState = "Live Snapshot";
            CurrentActionTarget = "Local Rollback Repository";
            CurrentActionStatus = "BACKING UP";
            ProgressPercentage = 15;
            RecalculateDynamicState();
            Notify();
        }

        public void StartApplyingAction(OptimizationJobAction action)
        {
            action.State = OptimizationActionState.Applying;
            CurrentActionName = string.IsNullOrEmpty(action.DisplayName) ? action.ActionId : action.DisplayName;
            CurrentActionCurrentState = string.IsNullOrEmpty(action.CurrentState) ? "Current" : action.CurrentState;
            CurrentActionTarget = string.IsNullOrEmpty(action.TargetState) ? "Optimized" : action.TargetState;
            CurrentActionStatus = "APPLYING";

            RecalculateDynamicState();
            Notify();
        }

        public void StartVerifyingAction(OptimizationJobAction action)
        {
            action.State = OptimizationActionState.Verifying;
            CurrentActionName = string.IsNullOrEmpty(action.DisplayName) ? action.ActionId : action.DisplayName;
            CurrentActionCurrentState = string.IsNullOrEmpty(action.CurrentState) ? "Current" : action.CurrentState;
            CurrentActionTarget = string.IsNullOrEmpty(action.TargetState) ? "Optimized" : action.TargetState;
            CurrentActionStatus = "VERIFYING";

            RecalculateDynamicState();
            Notify();
        }

        public void CompleteAction(OptimizationJobAction action, bool success, string reason = "")
        {
            if (success)
            {
                action.State = OptimizationActionState.Verified;
                CurrentActionStatus = "VERIFIED";
            }
            else
            {
                action.State = OptimizationActionState.Failed;
                action.ErrorMessage = reason;
                CurrentActionStatus = "FAILED";
            }

            RecalculateDynamicState();
            Notify();
        }

        public void MarkActionTimeout(OptimizationJobAction action, string error = "Operation exceeded timeout limit.")
        {
            action.State = OptimizationActionState.Timeout;
            action.ErrorMessage = error;
            CurrentActionStatus = "TIMEOUT";

            RecalculateDynamicState();
            Notify();
        }

        public void StartFinalizing(string details = "Reconciling live system state & hardware registers...")
        {
            State = OptimizationJobState.Finalizing;
            FinalizationState = FinalizationState.Running;
            CurrentActionName = details;
            CurrentActionStatus = "FINALIZING";
            RecalculateDynamicState();
            Notify();
        }

        public void FinalizeJob(bool reconciliationSuccess = true, string? error = null, bool isTimeout = false)
        {
            StageFinalizeStatus = OptimizationStageStatus.Completed;

            if (VerifiedCount > 0 && FailedCount == 0)
            {
                FinalizationState = isTimeout ? FinalizationState.TimedOut : (reconciliationSuccess ? FinalizationState.Succeeded : FinalizationState.Failed);
                State = OptimizationJobState.Completed;
                CurrentActionStatus = "VERIFIED";
                CurrentActionName = isTimeout
                    ? (string.IsNullOrEmpty(error) ? "Optimization verified. Background rescan in progress..." : error)
                    : (string.IsNullOrEmpty(error) ? "All selected optimizations verified against live system configuration." : error);
            }
            else if (VerifiedCount > 0 && FailedCount > 0)
            {
                FinalizationState = isTimeout ? FinalizationState.TimedOut : (reconciliationSuccess ? FinalizationState.Succeeded : FinalizationState.Failed);
                State = OptimizationJobState.Partial;
                CurrentActionStatus = "PARTIAL";
                CurrentActionName = string.IsNullOrEmpty(error) ? "Optimization finished with some warnings or failed actions." : error;
            }
            else if (FailedCount > 0 && VerifiedCount == 0)
            {
                FinalizationState = isTimeout ? FinalizationState.TimedOut : (reconciliationSuccess ? FinalizationState.Succeeded : FinalizationState.Failed);
                State = isTimeout ? OptimizationJobState.Timeout : OptimizationJobState.Failed;
                CurrentActionStatus = isTimeout ? "TIMEOUT" : "FAILED";
                CurrentActionName = string.IsNullOrEmpty(error) ? "Optimization could not complete successfully." : error;
            }
            else
            {
                FinalizationState = isTimeout ? FinalizationState.TimedOut : (reconciliationSuccess ? FinalizationState.Succeeded : FinalizationState.Failed);
                State = OptimizationJobState.Completed;
                CurrentActionStatus = "VERIFIED";
                CurrentActionName = string.IsNullOrEmpty(error) ? "System configuration is up to date." : error;
            }

            RecalculateDynamicState();
            Notify();
        }

        public void CompleteJob()
        {
            FinalizeJob(true);
        }

        public void FailJob(string error, bool isTimeout = false)
        {
            FinalizeJob(false, error, isTimeout);
        }
    }
}
