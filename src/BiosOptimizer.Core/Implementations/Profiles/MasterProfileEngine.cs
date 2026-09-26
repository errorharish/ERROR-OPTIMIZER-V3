#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.Core.Implementations.Power;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations.Profiles
{
    public class ProfilePlanItem
    {
        public CanonicalOptimizationDefinition Definition { get; set; } = null!;
        public ApplicabilityState State { get; set; }
        public string ApplicabilityReason { get; set; } = string.Empty;
        public string CurrentState { get; set; } = string.Empty;
        public string TargetState { get; set; } = string.Empty;
        public OptimizationHistoryRecord LearningHistory { get; set; } = new();
        public bool IsSelected { get; set; }
        public bool IsApplied { get; set; }
        public bool IsVerified { get; set; }
        public bool IsFailed { get; set; }
        public string FailureReason { get; set; } = string.Empty;
        public object? CapturedBackupState { get; set; }
    }

    public class ProfilePlan
    {
        public ProfilePolicy Policy { get; set; } = null!;
        public HardwareProfile Hardware { get; set; } = null!;
        public WorkloadClassificationResult Workload { get; set; } = null!;
        public List<ProfilePlanItem> Items { get; set; } = new();

        public int TotalCandidates => Items.Count;
        public int ApplicableCount => Items.Count(i => i.State == ApplicabilityState.Applicable);
        public int AlreadyOptimalCount => Items.Count(i => i.State == ApplicabilityState.AlreadyOptimal);
        public int UnsupportedCount => Items.Count(i => i.State == ApplicabilityState.Unsupported || i.State == ApplicabilityState.NotApplicable);
        public int RequiresAdminCount => Items.Count(i => i.State == ApplicabilityState.RequiresAdmin);
        public int ConflictCount => Items.Count(i => i.State == ApplicabilityState.Conflict);
    }

    public class MasterProfileEngine
    {
        private static readonly Lazy<MasterProfileEngine> _instance = new(() => new MasterProfileEngine());
        public static MasterProfileEngine Instance => _instance.Value;

        public ProfilePlan GeneratePlan(string profileId)
        {
            var policy = ProfilePolicy.GetPolicyById(profileId);
            var hw = HardwareProfiler.GetQuickProfile(forceRefresh: false);
            var wl = UserWorkloadDetector.Instance.ClassifyCurrentWorkload();
            return GeneratePlan(policy, hw, wl);
        }

        public ProfilePlan GeneratePlan(ProfilePolicy policy, HardwareProfile hw, WorkloadClassificationResult wl)
        {
            var allDefs = MasterOptimizationRegistry.Instance.GetAllDefinitions();
            var plan = new ProfilePlan
            {
                Policy = policy,
                Hardware = hw,
                Workload = wl
            };

            foreach (var def in allDefs)
            {
                // 1. Policy Category Filter
                if (!policy.AllowedCategories.Contains(def.Category))
                    continue;

                // 2. Policy Risk Filter
                if (!policy.MaxAllowedRisks.Contains(def.RiskLevel))
                    continue;

                // 3. Minimum Aggressiveness Filter
                if (def.MinimumAggressiveness > policy.Aggressiveness)
                    continue;

                // 4. Policy Specific Feature Gates
                if (def.Category.Equals("Service", StringComparison.OrdinalIgnoreCase) && !policy.AllowServiceChanges)
                    continue;
                if (def.Category.Equals("Power", StringComparison.OrdinalIgnoreCase) && !policy.AllowPowerPlanSwitching)
                    continue;
                if (def.Category.Equals("BiosSafe", StringComparison.OrdinalIgnoreCase) && !policy.AllowBiosPlatformTweaks)
                    continue;

                // 5. Workload-Specific Adaptations
                if (policy.WorkloadAdaptationEnabled)
                {
                    if (wl.Category == ExtendedWorkloadCategory.Gaming)
                    {
                        // Boost priority of CPU/GPU/Input/Power during gaming
                        if (def.Category is "CPU" or "GPU" or "Input" or "Power")
                        {
                            def.Priority = Math.Min(100, def.Priority + 5);
                        }
                    }
                    else if (wl.Category == ExtendedWorkloadCategory.Compilation)
                    {
                        // Prioritize CPU and storage
                        if (def.Category is "CPU" or "Storage")
                        {
                            def.Priority = Math.Min(100, def.Priority + 5);
                        }
                    }
                }

                // 6. Applicability Evaluation
                var (appState, appReason) = def.ApplicabilityDetector(hw, wl);
                string currentState = def.CurrentStateReader();
                string targetState = def.TargetStateReader();
                var learningRecord = OptimizationLearningEngine.Instance.GetRecord(def.OptimizationId);

                var item = new ProfilePlanItem
                {
                    Definition = def,
                    State = appState,
                    ApplicabilityReason = appReason,
                    CurrentState = currentState,
                    TargetState = targetState,
                    LearningHistory = learningRecord,
                    IsSelected = appState == ApplicabilityState.Applicable
                };

                plan.Items.Add(item);
            }

            // 7. Sort by Priority Descending
            plan.Items = plan.Items.OrderByDescending(i => i.Definition.Priority).ToList();
            return plan;
        }

        public async Task<bool> ExecuteItemAsync(ProfilePlanItem item, string profileId)
        {
            await Task.Yield();
            try
            {
                string before = item.Definition.CurrentStateReader();
                item.CapturedBackupState = item.Definition.ApplyHandler();
                
                // Readback and Verify
                bool verified = item.Definition.VerificationHandler();
                string after = item.Definition.CurrentStateReader();

                item.IsApplied = true;
                item.IsVerified = verified;
                item.IsFailed = !verified;

                if (!verified)
                {
                    item.FailureReason = "Verification readback did not match expected target.";
                }

                // Record outcome in Machine History
                OptimizationLearningEngine.Instance.RecordOutcome(
                    item.Definition.OptimizationId,
                    profileId,
                    verified,
                    before,
                    after);

                return verified;
            }
            catch (Exception ex)
            {
                item.IsApplied = false;
                item.IsVerified = false;
                item.IsFailed = true;
                item.FailureReason = ex.Message;

                OptimizationLearningEngine.Instance.RecordOutcome(
                    item.Definition.OptimizationId,
                    profileId,
                    false,
                    item.CurrentState,
                    "EXCEPTION: " + ex.Message);

                return false;
            }
        }

        public async Task<int> ExecutePlanAsync(ProfilePlan plan, IProgress<(int current, int total, string name)>? progress = null, CancellationToken ct = default)
        {
            var selectedItems = plan.Items.Where(i => i.IsSelected && i.State == ApplicabilityState.Applicable).ToList();
            int total = selectedItems.Count;
            int successful = 0;

            for (int i = 0; i < total; i++)
            {
                if (ct.IsCancellationRequested) break;
                var item = selectedItems[i];
                progress?.Report((i + 1, total, item.Definition.Name));

                bool ok = await ExecuteItemAsync(item, plan.Policy.ProfileId);
                if (ok) successful++;
            }

            return successful;
        }

        public async Task RollbackItemAsync(ProfilePlanItem item)
        {
            await Task.Yield();
            if (item.CapturedBackupState != null && item.Definition.RollbackHandler != null)
            {
                try
                {
                    item.Definition.RollbackHandler(item.CapturedBackupState);
                    item.IsApplied = false;
                    item.IsVerified = false;
                }
                catch { }
            }
        }
    }
}
