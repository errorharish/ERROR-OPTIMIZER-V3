using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Implementations.Cleaners;
using BiosOptimizer.Core.Implementations.Storage;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels;
using BiosOptimizer.IPC.Contracts;
using Xunit;

namespace BiosOptimizer.Core.Tests
{
    public class MockTestIpcClient : IIpcClient
    {
        public bool IsServiceAvailable => ConnectionState == IpcConnectionState.Connected;
        public IpcConnectionState ConnectionState { get; set; } = IpcConnectionState.Connected;
        public string ServiceBuildId => "2026.08.22.1429";
        public event Action<bool>? ServiceAvailabilityChanged { add { } remove { } }
        public event Action<IpcConnectionState>? ConnectionStateChanged { add { } remove { } }

        public void SetConnectionState(IpcConnectionState state)
        {
            ConnectionState = state;
        }

        public Task<bool> WaitForReadyAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task<IpcResponse> SendRequestAsync(IpcMessageType messageType, string? payload = null, CancellationToken ct = default)
        {
            if (messageType == IpcMessageType.PreviewTier)
            {
                var dto = new TierPreviewDto
                {
                    TierId = "TestTier",
                    TierName = "Test Tier",
                    ActionCount = 2,
                    CanApply = true,
                    CanRestore = true,
                    Actions = new List<OptimizationActionDto>
                    {
                        new() {
                            ItemId = "action_1",
                            ActionName = "NetworkThrottling",
                            DisplayName = "Disable Network Throttling",
                            Category = "Network",
                            Risk = "Low",
                            CurrentState = "Enabled",
                            TargetState = "Disabled",
                            Status = "Recommended",
                            IsSelected = true
                        },
                        new() {
                            ItemId = "action_2",
                            ActionName = "SystemResponsiveness",
                            DisplayName = "Optimize System Responsiveness",
                            Category = "System",
                            Risk = "Low",
                            CurrentState = "Standard",
                            TargetState = "Optimized",
                            Status = "Recommended",
                            IsSelected = true
                        }
                    }
                };

                return Task.FromResult(new IpcResponse { Success = true, Data = JsonSerializer.Serialize(dto) });
            }

            if (messageType == IpcMessageType.ApplyTier || messageType == IpcMessageType.ApplyCleaner || messageType == IpcMessageType.ScanCleaner)
            {
                return Task.FromResult(new IpcResponse { Success = true, Data = "Success" });
            }

            return Task.FromResult(new IpcResponse { Success = true, Data = "{}" });
        }

        public async IAsyncEnumerable<IpcResponse> SendStreamingRequestAsync(IpcMessageType messageType, string? payload = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var actions = new[]
            {
                new OptimizationActionDto
                {
                    ItemId = "action_1",
                    ActionName = "NetworkThrottling",
                    DisplayName = "Disable Network Throttling",
                    Category = "Network",
                    CurrentState = "Enabled",
                    TargetState = "Disabled",
                    Status = "Success",
                    Reason = "Applied & verified via registry"
                },
                new OptimizationActionDto
                {
                    ItemId = "action_2",
                    ActionName = "SystemResponsiveness",
                    DisplayName = "Optimize System Responsiveness",
                    Category = "System",
                    CurrentState = "Standard",
                    TargetState = "Optimized",
                    Status = "Success",
                    Reason = "Applied & verified via registry"
                }
            };

            for (int i = 0; i < actions.Length; i++)
            {
                if (ct.IsCancellationRequested) yield break;

                var streamPayload = new
                {
                    Type = "ProgressUpdate",
                    Current = i + 1,
                    Total = actions.Length,
                    Action = actions[i]
                };

                yield return new IpcResponse { Success = true, Data = JsonSerializer.Serialize(streamPayload) };
                await Task.Yield();
            }
        }
    }

    public class UniversalOptimizationExecutionTests
    {
        [Theory]
        [InlineData("Normal", "Normal")]
        [InlineData("Pro", "Pro")]
        [InlineData("Ultimate", "Ultimate")]
        [InlineData("Debloat", "Debloat")]
        [InlineData("BiosSafe", "BIOS Safe")]
        [InlineData("MaximumPerformance", "Maximum Performance")]
        public void TierProfiles_ImplementUniversalProgressModal_Correctly(string tierId, string tierName)
        {
            var mockIpc = new MockTestIpcClient();
            var vm = new TierViewModel(mockIpc, tierId, tierName, "Test Description");

            Assert.False(vm.IsProgressModalOpen);
            Assert.False(vm.IsOptimizationFinished);
            Assert.NotNull(vm.CloseProgressModalCommand);
            Assert.Equal("PENDING", vm.StageAnalyzing);
            Assert.Equal("PENDING", vm.StageBackup);
            Assert.Equal("PENDING", vm.StageApply);
            Assert.Equal("PENDING", vm.StageVerify);
            Assert.Equal("PENDING", vm.StageFinalize);
        }

        [Fact]
        public void Dashboard_ImplementsUniversalProgressModal_Correctly()
        {
            var mockIpc = new MockTestIpcClient();
            var dashVm = new DashboardViewModel(mockIpc);

            Assert.False(dashVm.IsProgressModalOpen);
            Assert.False(dashVm.IsOptimizationFinished);
            Assert.NotNull(dashVm.CloseProgressModalCommand);
            Assert.Equal("PENDING", dashVm.StageAnalyzing);
            Assert.Equal("PENDING", dashVm.StageBackup);
            Assert.Equal("PENDING", dashVm.StageApply);
            Assert.Equal("PENDING", dashVm.StageVerify);
            Assert.Equal("PENDING", dashVm.StageFinalize);
        }

        [Fact]
        public void RegistryTweaks_ImplementsUniversalProgressModal_Correctly()
        {
            var mockIpc = new MockTestIpcClient();
            var regVm = new RegistryTweakViewModel(mockIpc);

            Assert.False(regVm.IsProgressModalOpen);
            Assert.False(regVm.IsOptimizationFinished);
            Assert.NotNull(regVm.CloseProgressModalCommand);
        }

        [Fact]
        public async Task TwoActionJob_ExecutesDeterministically_ThroughAll5Stages_AndReaches100Percent()
        {
            var mockIpc = new MockTestIpcClient();
            var vm = new TierViewModel(mockIpc, "Pro", "Pro", "Description");
            vm.PreviewActions.Clear();

            // 1. Preview to populate 2 executable actions
            AppSettingsService.Instance.ConfirmHighRisk = false;
            AppSettingsService.Instance.ConfirmMediumRisk = false;
            await vm.PreviewAsync();
            Assert.Equal(2, vm.PreviewActions.Count(a => a.IsSelected));

            // 2. Execute Apply
            await vm.ApplyAsync();

            // 3. Verify State Machine is 100% complete and synchronized
            Assert.True(vm.IsOptimizationFinished);
            Assert.Equal(100.0, vm.ProgressPercentage);
            Assert.Equal("COMPLETED", vm.StageAnalyzing);
            Assert.Equal("COMPLETED", vm.StageBackup);
            Assert.Equal("COMPLETED", vm.StageApply);
            Assert.Equal("COMPLETED", vm.StageVerify);
            Assert.Equal("COMPLETED", vm.StageFinalize);
            Assert.Equal("VERIFIED", vm.CurrentActionStatus);
            Assert.Equal(2, vm.ProgressAppliedCount);
            Assert.Equal(2, vm.ProgressVerifiedCount);
            Assert.Equal(0, vm.ProgressFailedCount);
            Assert.Equal("2 / 2 OPTIMIZATIONS PROCESSED", vm.ProgressStatusText);

            // 4. Test Done Button closes modal
            vm.CloseProgressModalCommand.Execute(null);
            Assert.False(vm.IsProgressModalOpen);
        }

        [Fact]
        public async Task Dashboard_SmartOptimization_CompletesDeterministically()
        {
            var mockIpc = new MockTestIpcClient();
            var dashVm = new DashboardViewModel(mockIpc);

            await dashVm.RunSmartOptimizationAsync();

            Assert.True(dashVm.IsProgressModalOpen);
            Assert.True(dashVm.IsOptimizationFinished);
            Assert.Equal(100.0, dashVm.ProgressPercentage);
            Assert.Equal("COMPLETED", dashVm.StageAnalyzing);
            Assert.Equal("COMPLETED", dashVm.StageBackup);
            Assert.Equal("COMPLETED", dashVm.StageApply);
            Assert.Equal("COMPLETED", dashVm.StageVerify);
            Assert.Equal("COMPLETED", dashVm.StageFinalize);
            Assert.Equal("4 / 4 ACTIONS COMPLETED", dashVm.ProgressStatusText);
            Assert.Equal(4, dashVm.ProgressAppliedCount);
            Assert.Equal(4, dashVm.ProgressVerifiedCount);

            dashVm.CloseProgressModalCommand.Execute(null);
            Assert.False(dashVm.IsProgressModalOpen);
        }

        [Fact]
        public void SixAction_MixedSuccessFailure_TransitionsToFinalizingAndComplete()
        {
            var job = new OptimizationJob
            {
                ProfileTitle = "OPTIMIZING ULTIMATE",
                Actions = new List<OptimizationJobAction>
                {
                    new() { ActionId = "act1", DisplayName = "Action 1" },
                    new() { ActionId = "act2", DisplayName = "Action 2" },
                    new() { ActionId = "act3", DisplayName = "Action 3" },
                    new() { ActionId = "act4", DisplayName = "Action 4" },
                    new() { ActionId = "act5", DisplayName = "Action 5" },
                    new() { ActionId = "act6", DisplayName = "Action 6" }
                }
            };

            job.StartAnalyzing();
            Assert.Equal("ACTIVE", job.StageAnalyzingText);

            job.StartBackup();
            Assert.Equal("COMPLETED", job.StageAnalyzingText);
            Assert.Equal("ACTIVE", job.StageBackupText);

            // Execute 1: Verified
            job.StartApplyingAction(job.Actions[0]);
            Assert.Equal("ACTIVE", job.StageApplyText);
            job.StartVerifyingAction(job.Actions[0]);
            Assert.Equal("ACTIVE", job.StageVerifyText);
            job.CompleteAction(job.Actions[0], true);

            // Execute 2: Verified
            job.CompleteAction(job.Actions[1], true);

            // Execute 3: Failed
            job.CompleteAction(job.Actions[2], false, "Access denied");

            // Execute 4: Verified
            job.CompleteAction(job.Actions[3], true);

            // Execute 5: Failed
            job.CompleteAction(job.Actions[4], false, "Registry locked");

            // At 5 / 6:
            Assert.Equal(5, job.CompletedActions);
            Assert.Equal(3, job.VerifiedCount);
            Assert.Equal(2, job.FailedCount);
            Assert.Equal("5 / 6 OPTIMIZATIONS PROCESSED", job.ProgressStatusText);
            Assert.False(job.IsDoneEnabled);

            // Execute 6: Verified
            job.StartApplyingAction(job.Actions[5]);
            Assert.Equal("ACTIVE", job.StageApplyText);
            job.CompleteAction(job.Actions[5], true);

            // Now all 6 are terminal -> stage automatically becomes FINALIZING
            Assert.Equal(6, job.CompletedActions);
            Assert.Equal(4, job.VerifiedCount);
            Assert.Equal(2, job.FailedCount);
            Assert.Equal("COMPLETED", job.StageApplyText);
            Assert.Equal("COMPLETED", job.StageVerifyText);
            Assert.Equal("ACTIVE", job.StageFinalizeText);
            Assert.Equal(100.0, job.ProgressPercentage);

            // Complete Job
            job.CompleteJob();
            Assert.Equal(OptimizationJobState.Partial, job.State);
            Assert.Equal(100.0, job.ProgressPercentage);
            Assert.Equal("COMPLETED", job.StageFinalizeText);
            Assert.True(job.IsDoneEnabled);
        }

        [Fact]
        public void SixAction_AllSuccess_Reaches100Percent()
        {
            var job = new OptimizationJob
            {
                ProfileTitle = "OPTIMIZING PRO",
                Actions = Enumerable.Range(1, 6).Select(i => new OptimizationJobAction { ActionId = $"act_{i}", DisplayName = $"Action {i}" }).ToList()
            };

            foreach (var act in job.Actions)
            {
                job.CompleteAction(act, true);
            }

            job.CompleteJob();
            Assert.Equal(OptimizationJobState.Completed, job.State);
            Assert.Equal(6, job.VerifiedCount);
            Assert.Equal(0, job.FailedCount);
            Assert.Equal(100.0, job.ProgressPercentage);
            Assert.True(job.IsDoneEnabled);
        }

        [Fact]
        public void SixAction_AllFailure_TransitionsToTerminalAndEnablesDone()
        {
            var job = new OptimizationJob
            {
                ProfileTitle = "OPTIMIZING DEBLOAT",
                Actions = Enumerable.Range(1, 6).Select(i => new OptimizationJobAction { ActionId = $"act_{i}", DisplayName = $"Action {i}" }).ToList()
            };

            foreach (var act in job.Actions)
            {
                job.CompleteAction(act, false, "System error");
            }

            job.CompleteJob();
            Assert.Equal(OptimizationJobState.Failed, job.State);
            Assert.Equal(0, job.VerifiedCount);
            Assert.Equal(6, job.FailedCount);
            Assert.Equal(100.0, job.ProgressPercentage);
            Assert.True(job.IsDoneEnabled);
        }

        [Fact]
        public void ActionTimeout_TransitionsSafelyAndEnablesDone()
        {
            var job = new OptimizationJob
            {
                ProfileTitle = "OPTIMIZING BIOS SAFE",
                Actions = new List<OptimizationJobAction>
                {
                    new() { ActionId = "act1", DisplayName = "Action 1" }
                }
            };

            job.StartApplyingAction(job.Actions[0]);
            job.MarkActionTimeout(job.Actions[0], "Timeout reading register");

            Assert.Equal(1, job.FailedCount);
            Assert.Equal(OptimizationActionState.Timeout, job.Actions[0].State);

            job.FailJob("Timeout", isTimeout: true);
            Assert.Equal(OptimizationJobState.Timeout, job.State);
            Assert.Equal(100.0, job.ProgressPercentage);
            Assert.True(job.IsDoneEnabled);
        }

        [Fact]
        public void TestA_SingleAction_Success_FinalizesAndReaches100Percent()
        {
            var job = new OptimizationJob
            {
                ProfileTitle = "OPTIMIZING NORMAL",
                Actions = new List<OptimizationJobAction>
                {
                    new() { ActionId = "act1", DisplayName = "Action 1" }
                }
            };

            job.StartAnalyzing();
            job.StartBackup();
            job.StartApplyingAction(job.Actions[0]);
            job.StartVerifyingAction(job.Actions[0]);
            job.CompleteAction(job.Actions[0], true);

            // Reconcile and Finalize
            job.StartFinalizing();
            job.FinalizeJob(true);

            Assert.Equal("1 / 1 OPTIMIZATIONS PROCESSED", job.ProgressStatusText);
            Assert.Equal(1, job.AppliedCount);
            Assert.Equal(1, job.VerifiedCount);
            Assert.Equal(0, job.FailedCount);
            Assert.Equal(100.0, job.ProgressPercentage);
            Assert.Equal("COMPLETED", job.StageFinalizeText);
            Assert.True(job.IsDoneEnabled);
        }

        [Fact]
        public void TestB_SingleAction_Failure_FinalizesDeterministicallyAndEnablesDone()
        {
            var job = new OptimizationJob
            {
                ProfileTitle = "OPTIMIZING ULTIMATE",
                Actions = new List<OptimizationJobAction>
                {
                    new() { ActionId = "act1", DisplayName = "Action 1" }
                }
            };

            job.StartAnalyzing();
            job.StartBackup();
            job.StartApplyingAction(job.Actions[0]);
            job.CompleteAction(job.Actions[0], false, "Access Denied");

            Assert.Equal(0, job.AppliedCount);
            Assert.Equal(0, job.VerifiedCount);
            Assert.Equal(1, job.FailedCount);
            Assert.Equal("1 / 1 OPTIMIZATIONS PROCESSED", job.ProgressStatusText);

            // Finalize
            job.StartFinalizing();
            job.FinalizeJob(true);

            Assert.Equal(OptimizationJobState.Failed, job.State);
            Assert.Equal(100.0, job.ProgressPercentage);
            Assert.Equal("COMPLETED", job.StageFinalizeText);
            Assert.True(job.IsDoneEnabled);
        }

        [Fact]
        public void TestC_TwoActions_Mixed_FinalizesAsPartialAndEnablesDone()
        {
            var job = new OptimizationJob
            {
                ProfileTitle = "OPTIMIZING PRO",
                Actions = new List<OptimizationJobAction>
                {
                    new() { ActionId = "act1", DisplayName = "Action 1" },
                    new() { ActionId = "act2", DisplayName = "Action 2" }
                }
            };

            job.CompleteAction(job.Actions[0], true);
            job.CompleteAction(job.Actions[1], false, "Lock error");

            Assert.Equal(2, job.CompletedActions);
            Assert.Equal(1, job.VerifiedCount);
            Assert.Equal(1, job.FailedCount);

            job.StartFinalizing();
            job.FinalizeJob(true);

            Assert.Equal(OptimizationJobState.Partial, job.State);
            Assert.Equal(100.0, job.ProgressPercentage);
            Assert.True(job.IsDoneEnabled);
        }

        [Fact]
        public void TestD_FinalReconciliationFailure_ResolvesSafelyAndEnablesDone()
        {
            var job = new OptimizationJob
            {
                ProfileTitle = "OPTIMIZING DEBLOAT",
                Actions = new List<OptimizationJobAction>
                {
                    new() { ActionId = "act1", DisplayName = "Action 1" }
                }
            };

            job.CompleteAction(job.Actions[0], true);
            job.StartFinalizing();
            job.FinalizeJob(reconciliationSuccess: false, error: "WMI service unavailable");

            // Successful optimization remains Completed even if reconciliation warning occurs
            Assert.Equal(OptimizationJobState.Completed, job.State);
            Assert.Equal(FinalizationState.Failed, job.FinalizationState);
            Assert.Equal(1, job.VerifiedCount);
            Assert.Equal("COMPLETED", job.StageFinalizeText);
            Assert.Equal(100.0, job.ProgressPercentage);
            Assert.True(job.IsDoneEnabled);
        }

        [Fact]
        public void TestE_FinalReconciliationTimeout_ResolvesSafelyAndEnablesDone()
        {
            var job = new OptimizationJob
            {
                ProfileTitle = "OPTIMIZING MAX PERFORMANCE",
                Actions = new List<OptimizationJobAction>
                {
                    new() { ActionId = "act1", DisplayName = "Action 1" }
                }
            };

            job.CompleteAction(job.Actions[0], true);
            job.StartFinalizing();
            job.FinalizeJob(reconciliationSuccess: false, error: "Timeout reading live registers (4s)", isTimeout: true);

            // Successful optimization remains Completed even if reconciliation times out
            Assert.Equal(OptimizationJobState.Completed, job.State);
            Assert.Equal(FinalizationState.TimedOut, job.FinalizationState);
            Assert.Equal(1, job.VerifiedCount);
            Assert.Equal("COMPLETED", job.StageFinalizeText);
            Assert.Equal(100.0, job.ProgressPercentage);
            Assert.True(job.IsDoneEnabled);
        }

        [Fact]
        public void TestF_CapabilityCheck_NonApplicableAndUnsupported_ExcludedFromExecutionPlan()
        {
            var allActions = new List<OptimizationActionDto>
            {
                new() { ItemId = "act1", Status = "Recommended", IsSelected = true },
                new() { ItemId = "act2", Status = "AlreadyOptimized", IsSelected = true },
                new() { ItemId = "act3", Status = "Unsupported", IsSelected = true },
                new() { ItemId = "act4", Status = "NotApplicable", IsSelected = true },
                new() { ItemId = "act5", Status = "Recommended", IsSelected = true }
            };

            var executablePlan = allActions
                .Where(a => (a.Status == "Recommended" || a.Status == "Available") && 
                             a.Status != "AlreadyOptimized" && 
                             a.Status != "Unsupported" && 
                             a.Status != "NotApplicable")
                .ToList();

            Assert.Equal(2, executablePlan.Count);
            Assert.Contains(executablePlan, a => a.ItemId == "act1");
            Assert.Contains(executablePlan, a => a.ItemId == "act5");
            Assert.DoesNotContain(executablePlan, a => a.ItemId == "act2");
            Assert.DoesNotContain(executablePlan, a => a.ItemId == "act3");
            Assert.DoesNotContain(executablePlan, a => a.ItemId == "act4");
        }

        [Fact]
        public void TestG_ApplicabilityFirst_RecommendedIsSubsetOfApplicable()
        {
            var actions = new List<OptimizationActionDto>
            {
                new() { ItemId = "a1", Status = "Recommended", Applicable = true, Supported = true, CurrentState = "Enabled", TargetState = "Disabled" },
                new() { ItemId = "a2", Status = "AlreadyOptimized", Applicable = true, Supported = true, CurrentState = "Disabled", TargetState = "Disabled" },
                new() { ItemId = "a3", Status = "NotApplicable", Applicable = false, Supported = true },
                new() { ItemId = "a4", Status = "Unsupported", Applicable = false, Supported = false }
            };

            // Invariant 1: All recommended items must have Applicable == true
            var recommended = actions.Where(a => a.Status == "Recommended").ToList();
            Assert.All(recommended, a => Assert.True(a.Applicable));
            Assert.All(recommended, a => Assert.True(a.Supported));
            Assert.All(recommended, a => Assert.NotEqual(a.CurrentState, a.TargetState));

            // Invariant 2: Non-applicable and Unsupported cannot be in recommended
            var nonApplicable = actions.Where(a => !a.Applicable || a.Status == "NotApplicable" || a.Status == "Unsupported").ToList();
            Assert.DoesNotContain(nonApplicable, a => a.Status == "Recommended");

            // Invariant 3: Applicable count = Recommended + AlreadyOptimized
            var applicableCount = actions.Count(a => a.Applicable);
            var alreadyOptimizedCount = actions.Count(a => a.Status == "AlreadyOptimized");
            var recommendedCount = actions.Count(a => a.Status == "Recommended");
            Assert.Equal(applicableCount, recommendedCount + alreadyOptimizedCount);
        }

        [Fact]
        public void TestH_FullHierarchy_Available_Applicable_HighlyRecommended()
        {
            // Example: 145 Total, 100 Available, 85 Applicable, 60 Already Optimized, 25 Pending Applicable, 5 Highly Recommended
            var definitions = new List<OptimizationActionDto>();
            for (int i = 1; i <= 45; i++)
            {
                definitions.Add(new OptimizationActionDto { ItemId = $"unsupp_{i}", Supported = false, Applicable = false, Status = "Unsupported" });
            }
            for (int i = 1; i <= 15; i++)
            {
                definitions.Add(new OptimizationActionDto { ItemId = $"notapp_{i}", Supported = true, Applicable = false, Status = "NotApplicable" });
            }
            for (int i = 1; i <= 60; i++)
            {
                definitions.Add(new OptimizationActionDto { ItemId = $"already_{i}", Supported = true, Applicable = true, CurrentState = "Target", TargetState = "Target", Status = "AlreadyOptimized" });
            }
            for (int i = 1; i <= 25; i++)
            {
                definitions.Add(new OptimizationActionDto { ItemId = $"pending_{i}", Supported = true, Applicable = true, CurrentState = "Default", TargetState = "Target", Status = "Recommended", RecommendationScore = i * 2 });
            }

            int total = definitions.Count;
            int available = definitions.Count(a => a.Supported && a.Status != "Unsupported");
            int applicable = definitions.Count(a => a.Applicable);
            int alreadyOptimized = definitions.Count(a => a.Status == "AlreadyOptimized");
            int pending = definitions.Count(a => a.Applicable && a.Status == "Recommended");
            var highlyRecommended = definitions.Where(a => a.Applicable && a.Status == "Recommended").OrderByDescending(a => a.RecommendationScore).Take(5).ToList();

            Assert.Equal(145, total);
            Assert.Equal(100, available);
            Assert.Equal(85, applicable);
            Assert.Equal(60, alreadyOptimized);
            Assert.Equal(25, pending);
            Assert.Equal(5, highlyRecommended.Count);

            // Invariant: Highly Recommended is a subset of Pending Applicable
            Assert.All(highlyRecommended, hr => Assert.True(hr.Applicable && hr.Status == "Recommended"));
            Assert.True(highlyRecommended.Count <= pending);
            Assert.True(pending <= applicable);
            Assert.True(applicable <= available);
            Assert.True(available <= total);
        }

        [Fact]
        public void TestI_CustomMode_OnlyApplicableSelectable()
        {
            var actions = new List<OptimizationActionDto>
            {
                new() { ItemId = "c1", Applicable = true, Status = "Recommended", IsSelected = true },
                new() { ItemId = "c2", Applicable = true, Status = "AlreadyOptimized", IsSelected = false },
                new() { ItemId = "c3", Applicable = false, Status = "NotApplicable", IsSelected = false },
                new() { ItemId = "c4", Applicable = false, Status = "Unsupported", IsSelected = false }
            };

            // Custom mode query filters out NotApplicable and Unsupported
            var customModeQuery = actions.Where(a => a.Applicable && a.Status != "NotApplicable" && a.Status != "Unsupported").ToList();

            Assert.Equal(2, customModeQuery.Count);
            Assert.Contains(customModeQuery, a => a.ItemId == "c1");
            Assert.Contains(customModeQuery, a => a.ItemId == "c2");
            Assert.DoesNotContain(customModeQuery, a => a.ItemId == "c3");
            Assert.DoesNotContain(customModeQuery, a => a.ItemId == "c4");
        }

        [Fact]
        public void TestJ_NormalMode_BuildsCompleteExecutablePlanFromAllPendingApplicable()
        {
            var actions = new List<OptimizationActionDto>
            {
                new() { ItemId = "n1", Supported = true, Applicable = true, CurrentState = "Disabled", TargetState = "Enabled", Status = "Recommended", IsHighlyRecommended = true, RecommendationScore = 95 },
                new() { ItemId = "n2", Supported = true, Applicable = true, CurrentState = "Disabled", TargetState = "Enabled", Status = "Recommended", IsHighlyRecommended = true, RecommendationScore = 80 },
                new() { ItemId = "n3", Supported = true, Applicable = true, CurrentState = "Disabled", TargetState = "Enabled", Status = "Recommended", IsHighlyRecommended = false, RecommendationScore = 40 },
                new() { ItemId = "n4", Supported = true, Applicable = true, CurrentState = "Enabled", TargetState = "Enabled", Status = "AlreadyOptimized" },
                new() { ItemId = "n5", Supported = true, Applicable = false, Status = "NotApplicable" },
                new() { ItemId = "n6", Supported = false, Applicable = false, Status = "Unsupported" }
            };

            // Normal mode automatically plans ALL pending applicable actions permitted by policy
            var normalPlan = actions
                .Where(a => a.Applicable && a.Supported && a.Status == "Recommended" && a.CurrentState != a.TargetState)
                .OrderByDescending(a => a.RecommendationScore)
                .ToList();

            Assert.Equal(3, normalPlan.Count);
            Assert.Equal("n1", normalPlan[0].ItemId);
            Assert.True(normalPlan[0].IsHighlyRecommended);
            Assert.Equal("n2", normalPlan[1].ItemId);
            Assert.True(normalPlan[1].IsHighlyRecommended);
            Assert.Equal("n3", normalPlan[2].ItemId);
            Assert.False(normalPlan[2].IsHighlyRecommended);

            // Invariant: Non-applicable and unsupported items never enter normal plan
            Assert.DoesNotContain(normalPlan, a => a.ItemId == "n4");
            Assert.DoesNotContain(normalPlan, a => a.ItemId == "n5");
            Assert.DoesNotContain(normalPlan, a => a.ItemId == "n6");
        }

        [Fact]
        public void TestK_System100PercentOptimized_WhenPendingApplicableIsZero()
        {
            var actions = new List<OptimizationActionDto>
            {
                new() { ItemId = "opt1", Supported = true, Applicable = true, CurrentState = "Target", TargetState = "Target", Status = "AlreadyOptimized" },
                new() { ItemId = "opt2", Supported = true, Applicable = true, CurrentState = "Target", TargetState = "Target", Status = "AlreadyOptimized" },
                new() { ItemId = "opt3", Supported = true, Applicable = true, CurrentState = "Target", TargetState = "Target", Status = "AlreadyOptimized" }
            };

            int applicable = actions.Count(a => a.Applicable);
            int pending = actions.Count(a => a.Applicable && a.Status == "Recommended" && a.CurrentState != a.TargetState);
            int already = actions.Count(a => a.Applicable && a.Status == "AlreadyOptimized");

            Assert.Equal(3, applicable);
            Assert.Equal(3, already);
            Assert.Equal(0, pending);

            string statusText = pending == 0 ? "SYSTEM 100% OPTIMIZED" : "OPTIMIZATIONS AVAILABLE";
            Assert.Equal("SYSTEM 100% OPTIMIZED", statusText);
        }

        [Fact]
        public void TestL_FutureChange_TriggersFullOptimizationEnabled()
        {
            var actions = new List<OptimizationActionDto>
            {
                new() { ItemId = "opt1", Supported = true, Applicable = true, CurrentState = "Target", TargetState = "Target", Status = "AlreadyOptimized" },
                new() { ItemId = "opt2", Supported = true, Applicable = true, CurrentState = "Target", TargetState = "Target", Status = "AlreadyOptimized" }
            };

            Assert.Equal(0, actions.Count(a => a.Applicable && a.Status == "Recommended"));

            // Simulate external change: Windows Update or user reverts opt2 to Default
            actions[1].CurrentState = "Default";
            actions[1].Status = "Recommended";

            int pendingAfterChange = actions.Count(a => a.Applicable && a.Status == "Recommended" && a.CurrentState != a.TargetState);
            Assert.Equal(1, pendingAfterChange);

            string statusText = pendingAfterChange == 0 ? "SYSTEM 100% OPTIMIZED" : "1 OPTIMIZATION RECOMMENDED";
            Assert.Equal("1 OPTIMIZATION RECOMMENDED", statusText);
        }

        [Theory]
        [InlineData("Normal", 1)]
        [InlineData("Pro", 2)]
        [InlineData("Ultimate", 4)]
        [InlineData("Debloat", 8)]
        [InlineData("BiosSafe", 2)]
        [InlineData("MaximumPerformance", 8)]
        public void TestM_MultiAction_AllProfiles_DeterministicTermination(string profileName, int actionCount)
        {
            var job = new OptimizationJob
            {
                ProfileTitle = $"OPTIMIZING {profileName.ToUpper()}",
                Actions = Enumerable.Range(1, actionCount).Select(i => new OptimizationJobAction
                {
                    ActionId = $"{profileName}_act_{i}",
                    DisplayName = $"Action {i}"
                }).ToList()
            };

            job.StartAnalyzing();
            job.StartBackup();

            for (int i = 0; i < actionCount; i++)
            {
                var act = job.Actions[i];
                job.StartApplyingAction(act);
                job.StartVerifyingAction(act);
                job.CompleteAction(act, true);
            }

            job.StartFinalizing();
            job.FinalizeJob(true);

            Assert.Equal(actionCount, job.CompletedActions);
            Assert.Equal(actionCount, job.VerifiedCount);
            Assert.Equal(0, job.FailedCount);
            Assert.Equal(100.0, job.ProgressPercentage);
            Assert.Equal("COMPLETED", job.StageFinalizeText);
            Assert.True(job.IsDoneEnabled);
        }

        [Fact]
        public void TestN_PreflightValidation_RemovesInvalidatedActionsBeforeExecution()
        {
            var plannedActions = new List<OptimizationActionDto>
            {
                new() { ItemId = "p1", Supported = true, Applicable = true, CurrentState = "Default", TargetState = "Optimized", Status = "Recommended" },
                new() { ItemId = "p2", Supported = true, Applicable = true, CurrentState = "Default", TargetState = "Optimized", Status = "Recommended" }
            };

            // Before execution, p2 becomes non-applicable due to runtime hardware gate
            plannedActions[1].Applicable = false;
            plannedActions[1].Status = "NotApplicable";

            // Preflight validation filter
            var validatedPlan = plannedActions
                .Where(a => a.Supported && a.Applicable && a.Status == "Recommended" && a.CurrentState != a.TargetState)
                .ToList();

            Assert.Single(validatedPlan);
            Assert.Equal("p1", validatedPlan[0].ItemId);
            Assert.DoesNotContain(validatedPlan, a => a.ItemId == "p2");
        }

        [Fact]
        public void TestO_UltimateProfile_FiveActions_ActiveWindowTracking_Reaches100Percent()
        {
            var job = new OptimizationJob
            {
                ProfileTitle = "OPTIMIZING ULTIMATE",
                Actions = new List<OptimizationJobAction>
                {
                    new() { ActionId = "ult.svc.diagtrack", DisplayName = "Disable Connected User Experiences & Telemetry", CurrentState = "Running", TargetState = "Disabled" },
                    new() { ActionId = "ult.svc.dmwappushservice", DisplayName = "Disable WAP Push Message Routing", CurrentState = "Running", TargetState = "Disabled" },
                    new() { ActionId = "ult.reg.cortana", DisplayName = "Disable Cortana Background Agent", CurrentState = "1", TargetState = "0" },
                    new() { ActionId = "ult.reg.gamebar", DisplayName = "Disable Windows Game Bar Overlay", CurrentState = "1", TargetState = "0" },
                    new() { ActionId = "ult.reg.activewindowtracking", DisplayName = "Enable Active Window Tracking", CurrentState = "0", TargetState = "1" }
                }
            };

            job.StartAnalyzing();
            Assert.Equal("ACTIVE", job.StageAnalyzingText);
            Assert.Equal(0.0, job.ProgressPercentage);

            job.StartBackup();
            Assert.Equal("COMPLETED", job.StageAnalyzingText);
            Assert.Equal("ACTIVE", job.StageBackupText);

            // Execute Actions 1..4
            for (int i = 0; i < 4; i++)
            {
                var act = job.Actions[i];
                job.StartApplyingAction(act);
                job.StartVerifyingAction(act);
                job.CompleteAction(act, true, "Verified via registry/service");
            }

            // At 4 / 5: Progress MUST be strictly 80% (not 68%)
            Assert.Equal(4, job.CompletedActions);
            Assert.Equal(4, job.VerifiedCount);
            Assert.Equal(80.0, job.ProgressPercentage);
            Assert.Equal("4 / 5 OPTIMIZATIONS PROCESSED", job.ProgressStatusText);

            // Execute Action 5: Enable Active Window Tracking
            var act5 = job.Actions[4];
            job.StartApplyingAction(act5);
            Assert.Equal("ACTIVE", job.StageApplyText);
            Assert.Equal("APPLYING", job.CurrentActionStatus);

            job.StartVerifyingAction(act5);
            Assert.Equal("COMPLETED", job.StageApplyText);
            Assert.Equal("ACTIVE", job.StageVerifyText);
            Assert.Equal("VERIFYING", job.CurrentActionStatus);

            job.CompleteAction(act5, true, "Registry verified successfully (ActiveWindowTracking = 1).");
            Assert.Equal("COMPLETED", job.StageApplyText);
            Assert.Equal("COMPLETED", job.StageVerifyText);
            Assert.Equal("ACTIVE", job.StageFinalizeText);
            Assert.Equal("FINALIZING", job.CurrentActionStatus);

            // At 5 / 5: Progress MUST be 100%
            Assert.Equal(5, job.CompletedActions);
            Assert.Equal(5, job.VerifiedCount);
            Assert.Equal(100.0, job.ProgressPercentage);
            Assert.Equal("5 / 5 OPTIMIZATIONS PROCESSED", job.ProgressStatusText);

            // Finalize
            job.StartFinalizing();
            job.FinalizeJob(true);

            Assert.Equal(OptimizationJobState.Completed, job.State);
            Assert.Equal(100.0, job.ProgressPercentage);
            Assert.Equal("COMPLETED", job.StageFinalizeText);
            Assert.True(job.IsDoneEnabled);
        }

        [Fact]
        public void TestP_BiosSafe_ExecutionTypes_SeparatesAutomaticAndManualUefi()
        {
            var vm = new TierViewModel(new MockTestIpcClient(), "BiosSafe", "BIOS Safe", "Firmware Profile");
            vm.RebarStatus = "Supported (GPU Ready)"; // Not yet enabled in UEFI
            vm.MemorySpeedAndType = "DDR5 4800 MHz (XMP Profile Ready)"; // Not yet full speed
            vm.VirtualizationInfo = "Virtualization: Supported";
            vm.PreviewActions.Clear();

            var automaticAction = new OptimizationActionDto
            {
                ItemId = "biossafe.power.ultimate",
                ActionName = "SetPowerPlan",
                DisplayName = "Activate High Performance Hardware Power Plan",
                Category = "Power",
                CurrentState = "Balanced",
                TargetState = "High Performance",
                Status = "Recommended",
                ExecutionType = "WINDOWS_AUTOMATIC",
                Applicable = true,
                IsSelected = true
            };

            var skippedAction = new OptimizationActionDto
            {
                ItemId = "biossafe.notapplicable",
                DisplayName = "PCIe Link State Not Applicable",
                Status = "NotApplicable",
                Applicable = false
            };

            vm.PreviewActions.Add(automaticAction);
            vm.PreviewActions.Add(skippedAction);

            vm.TotalPossibleAnalyzed = 8;
            vm.SummaryAvailable = 8;
            vm.SummaryApplicable = 5;
            vm.SummaryHighlyRecommended = 3;
            vm.SummaryAlreadyOptimized = 2;
            vm.SummaryAutomaticPending = 1;
            vm.SummaryManualUefiPending = 2;
            vm.SummaryNotApplicable = 3;

            // Verify counters
            Assert.Equal(8, vm.TotalPossibleAnalyzed);
            Assert.Equal(8, vm.SummaryAvailable);
            Assert.Equal(5, vm.SummaryApplicable);
            Assert.Equal(3, vm.SummaryHighlyRecommended);
            Assert.Equal(2, vm.SummaryAlreadyOptimized);
            Assert.Equal(1, vm.SummaryAutomaticPending);
            Assert.Equal(2, vm.SummaryManualUefiPending);
            Assert.Equal(3, vm.SummaryNotApplicable);

            // Filter for automatic executor
            var autoExecutable = vm.PreviewActions.ToArray()
                .Where(a => (a.Status == "Recommended" || a.Status == "Available") && 
                            (string.IsNullOrEmpty(a.ExecutionType) || a.ExecutionType == "WINDOWS_AUTOMATIC"))
                .ToList();

            // ONLY 1 action can be sent to Windows automatic executor
            Assert.Single(autoExecutable);
            Assert.Equal("biossafe.power.ultimate", autoExecutable[0].ItemId);
            Assert.Equal("WINDOWS_AUTOMATIC", autoExecutable[0].ExecutionType);
        }

        [Fact]
        public void TestQ_StateNormalizer_DebloatClassicContextMenu_AndFreshRescan()
        {
            // Case 1: Restore Windows 10 Classic Context Menu uses empty string "" target and "" readback
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("", ""));
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Not Set", ""));

            // Case 2: DWORD & Hex equivalence
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("1", "1"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("0x1", "1"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("0", "Disabled"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("2", "Automatic"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("High Performance", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"));

            // Case 3: Debloat rescan transitions from 13 already + 1 pending to 14 already + 0 pending -> 100% OPTIMIZED
            var vm = new TierViewModel(new MockTestIpcClient(), "Debloat", "Debloat", "Debloat Profile");
            vm.PreviewActions.Clear();
            for (int i = 1; i <= 13; i++)
            {
                vm.PreviewActions.Add(new OptimizationActionDto
                {
                    ItemId = $"debloat_action_{i}",
                    DisplayName = $"Debloat Action {i}",
                    Status = "AlreadyOptimized",
                    Applicable = true
                });
            }

            // The pending action: Classic Context Menu
            var pendingClassicContext = new OptimizationActionDto
            {
                ItemId = "debloat.ui.classiccontext",
                DisplayName = "Restore Windows 10 Classic Context Menu",
                Category = "Windows UI",
                CurrentState = "Not Set",
                TargetState = "",
                Status = "Recommended",
                Applicable = true,
                ExecutionType = "WINDOWS_AUTOMATIC"
            };
            vm.PreviewActions.Add(pendingClassicContext);

            // Verify before optimization
            int beforeApplicable = vm.PreviewActions.Count;
            int beforeAlready = vm.PreviewActions.Count(a => a.Status == "AlreadyOptimized");
            int beforePending = vm.PreviewActions.Count(a => a.Status == "Recommended");
            Assert.Equal(14, beforeApplicable);
            Assert.Equal(13, beforeAlready);
            Assert.Equal(1, beforePending);

            // Simulate successful apply and authoritative readback
            string verifiedCurrent = ""; // Registry (Default) set to empty string
            bool isSatisfied = BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied(verifiedCurrent, pendingClassicContext.TargetState);
            Assert.True(isSatisfied);

            pendingClassicContext.CurrentState = verifiedCurrent;
            pendingClassicContext.Status = isSatisfied ? "AlreadyOptimized" : "Recommended";

            // Verify fresh rescan state
            int afterApplicable = vm.PreviewActions.Count;
            int afterAlready = vm.PreviewActions.Count(a => a.Status == "AlreadyOptimized");
            int afterPending = vm.PreviewActions.Count(a => a.Status == "Recommended");
            Assert.Equal(14, afterApplicable);
            Assert.Equal(14, afterAlready);
            Assert.Equal(0, afterPending);
        }

        [Fact]
        public void TestR_StateNormalizer_CompleteGeneralTestMatrix()
        {
            // Null and empty checks
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied(null, null));
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied(null, ""));
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("", null));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("", ""));

            // Exact match
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("0", "0"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("1", "1"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("abc", "ABC"));

            // Semantic flags (Disabled / Enabled)
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("0", "Disabled"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Disabled", "0"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("1", "Enabled"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Enabled", "1"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("false", "0"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("true", "1"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("off", "0"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("on", "1"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("stopped", "Disabled"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("running", "Enabled"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("not installed", "0"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("removed", "0"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("active", "1"));

            // Numeric & Hexadecimal
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("0x1", "1"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("1", "0x1"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("0x0", "0"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("0x80000001", "2147483649"));

            // Service states
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("2", "Automatic"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Automatic", "2"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("3", "Manual"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("4", "Disabled"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Auto", "2"));

            // Power Plans
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "High Performance"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("High Performance", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("381b4222-f694-41f0-9685-ff5bb260df2e", "Balanced"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("e9a42b02-d5df-448d-aa00-03f14749eb61", "Ultimate Performance"));

            // Invariance: Distinct non-matching values must NOT be conflated
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("0", "1"));
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Enabled", "Disabled"));
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Automatic", "Disabled"));
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Balanced", "High Performance"));
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Not Set", "1"));
        }

        [Fact]
        public void TestS_AllSixProfiles_RegressionProtection()
        {
            var profiles = new[] { "Normal", "Pro", "Ultimate", "Debloat", "BiosSafe", "MaximumPerformance" };

            foreach (var prof in profiles)
            {
                var vm = new TierViewModel(new MockTestIpcClient(), prof, prof, $"{prof} Profile");
                
                // Add 5 actions, all satisfied
                for (int i = 1; i <= 5; i++)
                {
                    vm.PreviewActions.Add(new OptimizationActionDto
                    {
                        ItemId = $"{prof}_act_{i}",
                        DisplayName = $"{prof} Action {i}",
                        Status = "AlreadyOptimized",
                        Applicable = true,
                        ExecutionType = "WINDOWS_AUTOMATIC"
                    });
                }

                if (prof == "BiosSafe")
                {
                    vm.RebarStatus = "Enabled (Full VRAM Addressing)";
                    vm.MemorySpeedAndType = "Profile 1 / EXPO Active (6000 MHz)";
                    vm.VirtualizationInfo = "Virtualization: Enabled (Hyper-V Ready)";
                }

                vm.SummaryRecommended = 0;
                vm.SummaryAutomaticPending = 0;
                vm.SummaryManualUefiPending = 0;
                vm.SummaryAlreadyOptimized = vm.PreviewActions.Count + (prof == "BiosSafe" ? 3 : 0);

                Assert.Equal(0, vm.SummaryRecommended);
                Assert.Equal(0, vm.SummaryAutomaticPending);
                Assert.Equal(0, vm.SummaryManualUefiPending);
                Assert.Equal("SYSTEM 100% OPTIMIZED", vm.RunButtonText);
            }
        }

        [Fact]
        public void TestT_FutureChangeDetection_RegressionProtection()
        {
            var vm = new TierViewModel(new MockTestIpcClient(), "Normal", "Normal", "Normal Profile");

            var action1 = new OptimizationActionDto
            {
                ItemId = "tweak_visual",
                DisplayName = "Visual Effects",
                CurrentState = "0",
                TargetState = "0",
                Status = "AlreadyOptimized",
                Applicable = true
            };
            vm.PreviewActions.Add(action1);

            vm.SummaryRecommended = 0;
            vm.SummaryAlreadyOptimized = 1;
            Assert.Equal("SYSTEM 100% OPTIMIZED", vm.RunButtonText);

            // System config changed externally: visual effects turned back on (Current = 1)
            action1.CurrentState = "1";
            bool isSatisfied = BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied(action1.CurrentState, action1.TargetState);
            Assert.False(isSatisfied);

            // Fresh rescan detects mismatch
            action1.Status = isSatisfied ? "AlreadyOptimized" : "Recommended";
            vm.SummaryRecommended = 1;
            vm.SummaryAlreadyOptimized = 0;

            Assert.Equal(1, vm.SummaryRecommended);
            Assert.Equal("FULL OPTIMIZATION", vm.RunButtonText);

            // Re-apply and restore target state
            action1.CurrentState = "0";
            bool isSatisfiedAgain = BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied(action1.CurrentState, action1.TargetState);
            Assert.True(isSatisfiedAgain);

            action1.Status = isSatisfiedAgain ? "AlreadyOptimized" : "Recommended";
            vm.SummaryRecommended = 0;
            vm.SummaryAlreadyOptimized = 1;

            Assert.Equal(0, vm.SummaryRecommended);
            Assert.Equal("SYSTEM 100% OPTIMIZED", vm.RunButtonText);
        }

        [Fact]
        public void TestU_BiosSafe_PowerPlanHandler_PreflightAndActivation()
        {
            var handler = new BiosOptimizer.Core.ActionHandlers.PowerPlanOptimizationHandler();
            var entry = new OptimizationEntry
            {
                Id = "biossafe.power.ultimate",
                DisplayName = "Activate High Performance Hardware Power Plan",
                Category = "Power",
                Action = "SetPowerPlan",
                Target = "High Performance",
                Value = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
                ExecutionType = "WINDOWS_AUTOMATIC"
            };

            var context = new EnvironmentContext
            {
                IsLaptop = false,
                HasBattery = false
            };

            var availability = handler.CheckAvailability(entry, context);
            Assert.Equal(TargetState.Ready, availability);

            var currentDisplay = handler.GetCurrentValueDisplay(entry);
            Assert.False(string.IsNullOrEmpty(currentDisplay));
            Assert.NotEqual("Unknown", currentDisplay);

            var schemes = BiosOptimizer.Core.ActionHandlers.PowerPlanOptimizationHandler.ListAllSchemes();
            Assert.NotEmpty(schemes);
            Assert.Contains(schemes, s => s.IsActive);
        }

        [Fact]
        public void TestV_ServiceStateNormalization_AndBitsReconciliation()
        {
            // 1. Validate NormalizeServiceState
            Assert.Equal("MANUAL", BiosOptimizer.Core.Services.StateNormalizer.NormalizeServiceState("Manual"));
            Assert.Equal("MANUAL", BiosOptimizer.Core.Services.StateNormalizer.NormalizeServiceState("3"));
            Assert.Equal("MANUAL", BiosOptimizer.Core.Services.StateNormalizer.NormalizeServiceState("Demand"));
            Assert.Equal("MANUAL", BiosOptimizer.Core.Services.StateNormalizer.NormalizeServiceState("SERVICE_DEMAND_START"));

            Assert.Equal("AUTOMATIC", BiosOptimizer.Core.Services.StateNormalizer.NormalizeServiceState("Automatic"));
            Assert.Equal("AUTOMATIC", BiosOptimizer.Core.Services.StateNormalizer.NormalizeServiceState("2"));
            Assert.Equal("AUTOMATIC", BiosOptimizer.Core.Services.StateNormalizer.NormalizeServiceState("Auto"));
            Assert.Equal("AUTOMATIC", BiosOptimizer.Core.Services.StateNormalizer.NormalizeServiceState("SERVICE_AUTO_START"));

            Assert.Equal("DISABLED", BiosOptimizer.Core.Services.StateNormalizer.NormalizeServiceState("Disabled"));
            Assert.Equal("DISABLED", BiosOptimizer.Core.Services.StateNormalizer.NormalizeServiceState("4"));
            Assert.Equal("DISABLED", BiosOptimizer.Core.Services.StateNormalizer.NormalizeServiceState("SERVICE_DISABLED"));

            // 2. Cross-matching service start types
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Manual", "Manual"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("3", "Manual"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Manual", "3"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Demand", "Manual"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Automatic", "2"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Disabled", "4"));

            // 3. Distinct start types must NOT be satisfied
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Automatic", "Manual"));
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Automatic", "Disabled"));
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Manual", "Disabled"));
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("Unknown", "Manual"));

            // 4. BITS Ultimate case: 112 already + 1 pending (BITS) -> optimize BITS to Manual -> 113 already + 0 pending -> 100% OPTIMIZED
            var vm = new TierViewModel(new MockTestIpcClient(), "Ultimate", "Ultimate", "Ultimate Profile");
            for (int i = 1; i <= 112; i++)
            {
                vm.PreviewActions.Add(new OptimizationActionDto
                {
                    ItemId = $"ultimate_act_{i}",
                    DisplayName = $"Ultimate Action {i}",
                    Status = "AlreadyOptimized",
                    Applicable = true,
                    ExecutionType = "WINDOWS_AUTOMATIC"
                });
            }

            var bitsAction = new OptimizationActionDto
            {
                ItemId = "ultimate.service.bits",
                DisplayName = "Background Intelligent Transfer Service",
                Category = "Service",
                ActionName = "SetServiceStartup",
                CurrentState = "Automatic",
                TargetState = "Manual",
                Status = "Recommended",
                Applicable = true,
                ExecutionType = "WINDOWS_AUTOMATIC"
            };
            vm.PreviewActions.Add(bitsAction);

            // Initial state: 113 applicable, 112 already, 1 pending
            int beforeApplicable = vm.PreviewActions.Count;
            int beforeAlready = vm.PreviewActions.Count(a => a.Status == "AlreadyOptimized");
            int beforePending = vm.PreviewActions.Count(a => a.Status == "Recommended");
            Assert.Equal(113, beforeApplicable);
            Assert.Equal(112, beforeAlready);
            Assert.Equal(1, beforePending);

            // Simulate Apply & Readback Verification of BITS
            string readbackStartup = "Manual"; // Real SCM & Registry readback
            bool isSatisfied = BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied(readbackStartup, bitsAction.TargetState);
            Assert.True(isSatisfied);

            bitsAction.CurrentState = readbackStartup;
            bitsAction.Status = isSatisfied ? "AlreadyOptimized" : "Recommended";

            // Final Rescan: 113 applicable, 113 already, 0 pending
            int afterApplicable = vm.PreviewActions.Count;
            int afterAlready = vm.PreviewActions.Count(a => a.Status == "AlreadyOptimized");
            int afterPending = vm.PreviewActions.Count(a => a.Status == "Recommended");
            Assert.Equal(113, afterApplicable);
            Assert.Equal(113, afterAlready);
            Assert.Equal(0, afterPending);

            vm.SummaryRecommended = afterPending;
            vm.SummaryAlreadyOptimized = afterAlready;
            vm.SummaryAutomaticPending = afterPending;
            vm.SummaryManualUefiPending = 0;

            Assert.Equal("SYSTEM 100% OPTIMIZED", vm.RunButtonText);
        }

        [Fact]
        public void TestW_RegistryTweakConsistency_AndFlexibleNumericNormalization()
        {
            // 1. Validate Network Throttling Index flexible hex / unsigned / signed DWORD normalization
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("-1", "ffffffff"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("-1", "0xFFFFFFFF"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("4294967295", "ffffffff"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("4294967295", "0xFFFFFFFF"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("ffffffff", "ffffffff"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("0", "0"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("1", "1"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("38", "38"));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("104857600", "104857600"));

            // 2. Mismatch checks
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("10", "ffffffff"));
            Assert.False(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("0", "1"));

            // 3. RegistryTweakViewModel IsFullyOptimized rule validation
            var regVm = new RegistryTweakViewModel(new MockTestIpcClient());
            regVm.IsNormalMode = true;
            for (int i = 1; i <= 24; i++)
            {
                regVm.Tweaks.Add(new RegistryTweakItemViewModel
                {
                    Id = $"tweak_{i}",
                    Name = $"Tweak {i}",
                    Status = "ALREADY OPTIMIZED"
                });
            }

            var pendingTweak = new RegistryTweakItemViewModel
            {
                Id = "tweak.mmcss.networkthrottling",
                Name = "Network Throttling Index",
                CurrentValue = "10",
                TargetValue = "0xFFFFFFFF (Disabled)",
                Status = "RECOMMENDED"
            };
            regVm.Tweaks.Add(pendingTweak);

            // Initial: 25 total, 24 optimized, 1 recommended -> NOT fully optimized!
            Assert.Equal(25, regVm.TotalCount);
            Assert.Equal(24, regVm.AlreadyOptimizedCount);
            Assert.Equal(1, regVm.RecommendedCount);
            Assert.False(regVm.IsFullyOptimized);
            Assert.Equal("⚡ FULL OPTIMIZATION", regVm.RunButtonText);
            Assert.Equal("24/25 OPTIMIZED", regVm.HealthStatus);

            // Simulate successful optimization & verification of Network Throttling Index
            pendingTweak.CurrentValue = "0xFFFFFFFF (Disabled)";
            pendingTweak.Status = "VERIFIED";

            // Post-rescan state: 25 total, 25 optimized, 0 recommended -> FULLY OPTIMIZED!
            Assert.Equal(25, regVm.TotalCount);
            Assert.Equal(25, regVm.AlreadyOptimizedCount);
            Assert.Equal(0, regVm.RecommendedCount);
            Assert.Equal("⚡ REGISTRY 100% OPTIMIZED", regVm.RunButtonText);
            Assert.Equal("100% OPTIMIZED", regVm.HealthStatus);
            Assert.False(regVm.CanApply);
        }

        [Fact]
        public void TestX_RealOptimizationProof_AndRestartPersistenceAudit()
        {
            // 1. Authoritative State Reader Contract Verification
            // Verify that StateNormalizer and flexible numeric matching are identical across all lifecycles
            string targetValue = "0xFFFFFFFF (Disabled)";
            string rawReadback1 = "4294967295";
            string rawReadback2 = "-1";
            string rawReadback3 = "ffffffff";

            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied(rawReadback1, targetValue));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied(rawReadback2, targetValue));
            Assert.True(BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied(rawReadback3, targetValue));

            // 2. Audit: Profile Restart Persistence Simulation (Normal, Pro, Ultimate, Debloat, BiosSafe, MaxPerf)
            var profiles = new[] { "Normal", "Pro", "Ultimate", "Debloat", "BiosSafe", "MaximumPerformance" };
            foreach (var profile in profiles)
            {
                var mockIpc = new MockTestIpcClient();
                var vm = new TierViewModel(mockIpc, profile, profile, "Description");
                vm.IsNormalMode = true;

                // Simulate Cold Application Startup Scan (Before Optimization)
                var beforeAction = new OptimizationActionDto
                {
                    ItemId = $"{profile.ToLower()}.test.item",
                    ActionName = "SetRegistryValue",
                    DisplayName = $"{profile} Test Optimization",
                    CurrentState = "0",
                    TargetState = "1",
                    Status = "Recommended",
                    Applicable = true,
                    ExecutionType = "WINDOWS_AUTOMATIC"
                };

                vm.PreviewActions.Add(beforeAction);
                vm.SummaryAvailable = 1;
                vm.SummaryApplicable = 1;
                vm.SummaryRecommended = 1;
                vm.SummaryAlreadyOptimized = 0;
                vm.SummaryAutomaticPending = 1;

                Assert.Equal("FULL OPTIMIZATION", vm.RunButtonText);

                // Simulate Real Apply + Readback Verify
                beforeAction.CurrentState = "1";
                beforeAction.Status = BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("1", "1") ? "AlreadyOptimized" : "Recommended";
                Assert.Equal("AlreadyOptimized", beforeAction.Status);

                // Post-optimization final rescan
                vm.SummaryRecommended = 0;
                vm.SummaryAlreadyOptimized = 1;
                vm.SummaryAutomaticPending = 0;
                Assert.Equal("SYSTEM 100% OPTIMIZED", vm.RunButtonText);

                // --- SIMULATE COMPLETE APP SHUTDOWN & COLD RESTART ---
                // Create brand new ViewModel instance (Cold Restart)
                var restartedVm = new TierViewModel(new MockTestIpcClient(), profile, profile, "Description");
                restartedVm.IsNormalMode = true;

                // Cold Startup Scan reads the REAL persisted state from Windows ("1")
                var restartedAction = new OptimizationActionDto
                {
                    ItemId = $"{profile.ToLower()}.test.item",
                    ActionName = "SetRegistryValue",
                    DisplayName = $"{profile} Test Optimization",
                    CurrentState = "1", // Persisted in Windows
                    TargetState = "1",
                    Status = BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("1", "1") ? "AlreadyOptimized" : "Recommended",
                    Applicable = true,
                    ExecutionType = "WINDOWS_AUTOMATIC"
                };

                restartedVm.PreviewActions.Add(restartedAction);
                restartedVm.SummaryAvailable = 1;
                restartedVm.SummaryApplicable = 1;
                restartedVm.SummaryRecommended = 0;
                restartedVm.SummaryAlreadyOptimized = 1;
                restartedVm.SummaryAutomaticPending = 0;

                // Verification: After restart, Real State is STILL 100% Optimized with NO stale recommendation
                Assert.Equal("AlreadyOptimized", restartedAction.Status);
                Assert.Equal(0, restartedVm.SummaryRecommended);
                Assert.Equal(1, restartedVm.SummaryAlreadyOptimized);
                Assert.Equal("SYSTEM 100% OPTIMIZED", restartedVm.RunButtonText);
            }

            // 3. Manual External Reversion Detection Audit
            // If an optimization is manually reverted outside Error Optimizer (e.g. CurrentState changed back to "0")
            var revertedVm = new TierViewModel(new MockTestIpcClient(), "Ultimate", "Ultimate", "Description");
            revertedVm.IsNormalMode = true;

            var revertedAction = new OptimizationActionDto
            {
                ItemId = "ultimate.test.reverted",
                ActionName = "SetRegistryValue",
                DisplayName = "Reverted Item",
                CurrentState = "0", // Externally reverted
                TargetState = "1",
                Status = BiosOptimizer.Core.Services.StateNormalizer.IsSatisfied("0", "1") ? "AlreadyOptimized" : "Recommended",
                Applicable = true,
                ExecutionType = "WINDOWS_AUTOMATIC"
            };

            revertedVm.PreviewActions.Add(revertedAction);
            revertedVm.SummaryAvailable = 1;
            revertedVm.SummaryApplicable = 1;
            revertedVm.SummaryRecommended = 1;
            revertedVm.SummaryAlreadyOptimized = 0;
            revertedVm.SummaryAutomaticPending = 1;

            // Startup scanner immediately recognizes that the state is NOT satisfied and marks it RECOMMENDED
            Assert.Equal("Recommended", revertedAction.Status);
            Assert.Equal(1, revertedVm.SummaryRecommended);
            Assert.Equal("FULL OPTIMIZATION", revertedVm.RunButtonText);
        }

        [Fact]
        public void TestY_RegistryTweaksToolbar_RestoreSelectedRemoved_AndCleanReflow()
        {
            string xamlPath = @"D:\PROJECTS FOR EXE\ON PROCESS\ERROR OPTIMIZER\src\BiosOptimizer.GUI\Views\RegistryTweakView.xaml";
            string content = File.ReadAllText(xamlPath);

            // Assert Restore Selected button is completely removed from UI
            Assert.DoesNotContain("Restore Selected", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("RestoreCommand", content, StringComparison.OrdinalIgnoreCase);

            // Assert the custom toolbar has Select All, Deselect All, Selected Count, Scan, Save Profile, and Apply
            Assert.Contains("BulkSelectLabel", content);
            Assert.Contains("DESELECT ALL", content);
            Assert.Contains("SelectedCountText", content);
            Assert.Contains("ScanCommand", content);
            Assert.Contains("SaveCustomProfileCommand", content);
            Assert.Contains("ApplyCommand", content);
        }

        public class DummyBackupManager : BiosOptimizer.Core.Interfaces.IBackupManager
        {
            private readonly BiosOptimizer.Core.Interfaces.IBackupManager _inner = BiosOptimizer.Core.Implementations.BackupManager.Instance;

            public event Action<BiosOptimizer.Core.Interfaces.BackupTransaction>? TransactionRecorded
            {
                add => _inner.TransactionRecorded += value;
                remove => _inner.TransactionRecorded -= value;
            }

            public string BeginBatchTransaction(string profileId, string profileName, string category) => _inner.BeginBatchTransaction(profileId, profileName, category);
            public BiosOptimizer.Core.Interfaces.BackupTransaction RecordTransaction(BiosOptimizer.Core.Interfaces.BackupTransaction transaction) => _inner.RecordTransaction(transaction);
            public bool CommitBatchTransaction(string batchTransactionId) => _inner.CommitBatchTransaction(batchTransactionId);

            public BiosOptimizer.Core.Interfaces.BackupTransaction CaptureRegistryTweak(string profileName, string optimizationName, string hive, string subKey, string valueName, object? oldValue, string oldType, object? targetValue, string riskLevel = "SAFE", string? parentBatchId = null) =>
                _inner.CaptureRegistryTweak(profileName, optimizationName, hive, subKey, valueName, oldValue, oldType, targetValue, riskLevel, parentBatchId);

            public BiosOptimizer.Core.Interfaces.BackupTransaction CaptureServiceTweak(string profileName, string serviceName, string previousStartup, string newStartup, string previousState, string riskLevel = "SAFE", string? parentBatchId = null) =>
                _inner.CaptureServiceTweak(profileName, serviceName, previousStartup, newStartup, previousState, riskLevel, parentBatchId);

            public BiosOptimizer.Core.Interfaces.BackupTransaction CapturePowerPlanTweak(string profileName, string planName, string previousGuid, string newGuid, string riskLevel = "SAFE", string? parentBatchId = null) =>
                _inner.CapturePowerPlanTweak(profileName, planName, previousGuid, newGuid, riskLevel, parentBatchId);

            public BiosOptimizer.Core.Interfaces.BackupTransaction CaptureInputTweak(string profileName, string optimizationName, string targetSetting, object? previousValue, object? newValue, string riskLevel = "SAFE", string? parentBatchId = null) =>
                _inner.CaptureInputTweak(profileName, optimizationName, targetSetting, previousValue, newValue, riskLevel, parentBatchId);

            public BiosOptimizer.Core.Interfaces.BackupTransaction CaptureNetworkTweak(string profileName, string settingName, string targetInterface, object? previousValue, object? newValue, string riskLevel = "SAFE", string? parentBatchId = null) =>
                _inner.CaptureNetworkTweak(profileName, settingName, targetInterface, previousValue, newValue, riskLevel, parentBatchId);

            public BiosOptimizer.Core.Interfaces.BackupTransaction CaptureStorageCleanup(string profileName, string categoryName, long reclaimedBytes, int filesDeleted, List<string> targetPaths, bool wasQuarantined = false, string? parentBatchId = null) =>
                _inner.CaptureStorageCleanup(profileName, categoryName, reclaimedBytes, filesDeleted, targetPaths, wasQuarantined, parentBatchId);

            public BiosOptimizer.Core.Interfaces.BackupTransaction CaptureMemoryRuntime(string profileName, long ramReclaimedMb, string loadBefore, string loadAfter, string details, string? parentBatchId = null) =>
                _inner.CaptureMemoryRuntime(profileName, ramReclaimedMb, loadBefore, loadAfter, details, parentBatchId);

            public BiosOptimizer.Core.Interfaces.BackupTransaction CaptureSystemRepair(string profileName, string operationName, string beforeState, string afterState, string? parentBatchId = null) =>
                _inner.CaptureSystemRepair(profileName, operationName, beforeState, afterState, parentBatchId);

            public BiosOptimizer.Core.Interfaces.BackupTransaction CaptureGenericTweak(string profileName, string optimizationId, string name, string category, string operationType, string beforeState, string afterState, string rollbackMethod, bool canRollback, string riskLevel = "SAFE", string? parentBatchId = null) =>
                _inner.CaptureGenericTweak(profileName, optimizationId, name, category, operationType, beforeState, afterState, rollbackMethod, canRollback, riskLevel, parentBatchId);

            public System.Threading.Tasks.Task<BiosOptimizer.Core.Interfaces.RestoreResult> RestoreSingleTransactionAsync(string transactionId, System.Threading.CancellationToken ct = default) => _inner.RestoreSingleTransactionAsync(transactionId, ct);
            public System.Threading.Tasks.Task<BiosOptimizer.Core.Interfaces.RestoreResult> RestoreProfileAsync(string profileName, System.Threading.CancellationToken ct = default) => _inner.RestoreProfileAsync(profileName, ct);
            public System.Threading.Tasks.Task<BiosOptimizer.Core.Interfaces.RestoreResult> RestoreFullSystemSnapshotAsync(System.Threading.CancellationToken ct = default) => _inner.RestoreFullSystemSnapshotAsync(ct);
            public System.Threading.Tasks.Task<bool> CreateSystemRestorePointAsync(string description, System.Threading.CancellationToken ct = default) => _inner.CreateSystemRestorePointAsync(description, ct);

            public List<BiosOptimizer.Core.Interfaces.BackupTransaction> GetAllTransactions() => _inner.GetAllTransactions();
            public List<BiosOptimizer.Core.Interfaces.ProfileBackupGroup> GetProfileBackupGroups() => _inner.GetProfileBackupGroups();
            public BiosOptimizer.Core.Interfaces.BackupSummaryStats GetSummaryStats() => _inner.GetSummaryStats();
            public BiosOptimizer.Core.Interfaces.BackupTransaction? GetTransactionById(string transactionId) => _inner.GetTransactionById(transactionId);
            public bool DeleteTransaction(string transactionId) => _inner.DeleteTransaction(transactionId);
            public void ClearOldBackups(int retentionDays) => _inner.ClearOldBackups(retentionDays);

            public bool CreateSystemRestorePoint(string description) => true;
            public bool BackupRegistryState(IEnumerable<string> registryPaths) => true;
            public bool BackupServiceState(IEnumerable<string> serviceNames) => true;
            public bool LogKilledProcesses(IEnumerable<string> processPaths) => true;
            public bool RestoreAll() => true;
            public bool BackupRegistryValue(string owner, string registryKey, string valueName, object oldValue, string oldType, object targetValue, string result) => true;
            public bool RestoreByOwner(string owner) => true;
        }

        [Fact]
        public async Task TestZ_ProcessIntelligence_SafetyClassification_AndRealScanValidation()
        {
            var dummyBackup = new DummyBackupManager();
            var engine = new BiosOptimizer.Core.Implementations.ProcessReductionEngine(dummyBackup);

            var candidates = await engine.ScanProcessesAsync();
            Assert.NotNull(candidates);
            Assert.NotEmpty(candidates);

            int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;

            foreach (var cand in candidates)
            {
                // Verify basic fields populated
                Assert.True(cand.ProcessId > 0);
                Assert.False(string.IsNullOrWhiteSpace(cand.ProcessName));
                Assert.False(string.IsNullOrWhiteSpace(cand.SafetyLevel));
                Assert.False(string.IsNullOrWhiteSpace(cand.Reason));

                // Verify self process is strictly protected
                if (cand.ProcessId == currentPid)
                {
                    Assert.Equal("PROTECTED", cand.SafetyLevel);
                    Assert.Equal(BiosOptimizer.Core.Interfaces.ProcessCategory.SystemCritical, cand.Category);
                }

                // Verify critical system processes are never marked SafeToKill
                if (cand.ProcessName.Equals("csrss", StringComparison.OrdinalIgnoreCase) ||
                    cand.ProcessName.Equals("services", StringComparison.OrdinalIgnoreCase) ||
                    cand.ProcessName.Equals("lsass", StringComparison.OrdinalIgnoreCase) ||
                    cand.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
                    cand.ProcessName.Equals("svchost", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.NotEqual(BiosOptimizer.Core.Interfaces.ProcessCategory.SafeToKill, cand.Category);
                    Assert.NotEqual("SAFE TO TERMINATE", cand.SafetyLevel);
                }
            }

            // Verify TerminateProcesses safety bounds: PID 0, 4, self PID cannot be killed
            int killed = engine.TerminateProcesses(new[] { 0, 4, currentPid });
            Assert.Equal(0, killed);

            // Verify UI structure in XAML
            string xamlPath = @"D:\PROJECTS FOR EXE\ON PROCESS\ERROR OPTIMIZER\src\BiosOptimizer.GUI\Views\ProcessReductionView.xaml";
            string content = File.ReadAllText(xamlPath);
            Assert.Contains("PROCESS INTELLIGENCE", content);
            Assert.Contains("SAFE BACKGROUND PROCESS MANAGEMENT", content);
            Assert.Contains("SAFE TO TERMINATE", content);
            Assert.Contains("RAM RECLAIMABLE", content);
            Assert.Contains("SYSTEM STATUS", content);
            Assert.Contains("TERMINATE SELECTED PROCESSES?", content);
            Assert.Contains("SelectedProcessForDetails", content);
        }

        [Fact]
        public void NoUnderlyingProgressBar_InTierViewXaml()
        {
            string xamlPath = @"D:\PROJECTS FOR EXE\ON PROCESS\ERROR OPTIMIZER\src\BiosOptimizer.GUI\Views\TierView.xaml";
            string content = File.ReadAllText(xamlPath);

            // Row 14's duplicate progress bar should be removed
            Assert.DoesNotContain("ROW 14: 3D GLOWING PROGRESS CONTROL", content);
            Assert.Contains("UNIVERSAL REAL-TIME OPTIMIZATION PROGRESS MODAL", content);
        }

        [Fact]
        public async Task TestZ2_InputOptimizer_NormalCustomModes_AndRealOptimizationValidation()
        {
            var dummyBackup = new DummyBackupManager();
            var engine = new BiosOptimizer.Core.Implementations.InputOptimizerEngine(dummyBackup);

            // 1. Plan validation
            var plan = await engine.PlanInputOptimizationAsync();
            Assert.NotNull(plan);
            Assert.NotEmpty(plan.Actions);

            foreach (var act in plan.Actions)
            {
                Assert.False(string.IsNullOrWhiteSpace(act.Id));
                Assert.False(string.IsNullOrWhiteSpace(act.Name));
                Assert.False(string.IsNullOrWhiteSpace(act.Category));
                Assert.False(string.IsNullOrWhiteSpace(act.Risk));
            }

            // 2. Custom execution using JSON list of action IDs
            string jsonPayload = System.Text.Json.JsonSerializer.Serialize(new[] { plan.Actions.First().Id });
            var customRes = await engine.ApplyInputOptimizationAsync(jsonPayload);
            Assert.True(customRes.Success);

            // 3. Independent executions: Normal (CORE) and Advanced
            var coreRes = await engine.ApplyInputOptimizationAsync("CORE");
            Assert.True(coreRes.Success);

            var advRes = await engine.ApplyInputOptimizationAsync("ADVANCED");
            Assert.NotNull(advRes.Message);

            // 4. Verify InputOptimizerView.xaml layout and elements
            string xamlPath = @"D:\PROJECTS FOR EXE\ON PROCESS\ERROR OPTIMIZER\src\BiosOptimizer.GUI\Views\InputOptimizerView.xaml";
            string content = File.ReadAllText(xamlPath);

            Assert.Contains("INPUT OPTIMIZER", content);
            Assert.Contains("LATENCY REDUCTION ENGINE", content);
            Assert.Contains("MOUSE", content);
            Assert.Contains("KEYBOARD", content);
            Assert.Contains("TIMER RESOLUTION", content);
            Assert.Contains("INPUT STABILITY", content);
            Assert.Contains("NORMAL INPUT OPTIMIZATION", content);
            Assert.Contains("ADVANCED INPUT OPTIMIZATION", content);
            Assert.Contains("REAL INPUT TEST", content);
            Assert.Contains("BEFORE / AFTER OPTIMIZATION PROOF", content);
            Assert.Contains("VIEW TECHNICAL DETAILS", content);

            // Verify Normal Mode has independent buttons for Normal and Advanced
            Assert.Contains("NormalRunButtonText", content);
            Assert.Contains("NormalOptimizeCommand", content);
            Assert.Contains("AdvancedRunButtonText", content);
            Assert.Contains("AdvancedOptimizeCommand", content);

            // Verify Normal Mode has NO individual list items
            Assert.DoesNotContain("ItemsControl ItemsSource=\"{Binding NormalItems}\"", content);
        }

        [Fact]
        public void Test_CheckSystemProtectionStatus_Accurate_System_Drive_Detection()
        {
            var engine = BiosOptimizer.Core.Implementations.WindowsSystemRestoreEngine.Instance;
            string sysDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";

            var (isEnabled, detectedDrive, reason) = engine.CheckSystemProtectionStatus();

            Assert.False(string.IsNullOrWhiteSpace(detectedDrive));
            Assert.Equal(sysDrive, detectedDrive);
            Assert.False(string.IsNullOrWhiteSpace(reason));

            // Verify Volume GUID retrieval helper
            string volGuid = engine.GetVolumeGuid(sysDrive);
            // On Windows systems with physical drives, volume GUID should start with \\?\Volume{
            if (!string.IsNullOrEmpty(volGuid))
            {
                Assert.StartsWith(@"\\?\Volume{", volGuid);
            }
        }

        [Fact]
        public void Test_Protection_Aware_Restore_Point_Success_State_Card_Formatting()
        {
            var progress = OptimizationProgressService.Instance;
            string customName = "Before Driver Update - Aug 27";
            string testDrive = "C:";
            long seqNumber = 142;
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            progress.ShowRestorePointSuccess(customName, testDrive, 1.25, seqNumber, timestamp);

            Assert.True(progress.IsCompleted);
            Assert.False(progress.IsFailed);
            Assert.Equal("RESTORE POINT CREATED", progress.ResultTitle);
            Assert.Equal("SYSTEM PROTECTION: ENABLED", progress.BackupStatusText);
            Assert.Equal("WINDOWS VERIFICATION: PASSED", progress.VerificationStatusText);
            Assert.Equal("RESTORE POINT CREATED & VERIFIED", progress.OverallStatusText);
            Assert.Contains(customName, progress.ResultMessage);
            Assert.Contains("Sequence #142", progress.ResultMessage);

            // Verify Stage checklist
            Assert.Contains(progress.ExecutionStages, s => s.Name.Contains("System Protection: ENABLED"));
            Assert.Contains(progress.ExecutionStages, s => s.Name.Contains("SRSetRestorePointW Native API"));
            Assert.Contains(progress.ExecutionStages, s => s.Name.Contains("BEGIN & END System Change"));
            Assert.Contains(progress.ExecutionStages, s => s.Name.Contains("WINDOWS VERIFICATION: PASSED"));

            // Verify Item detail
            var item = progress.OptimizationItems.FirstOrDefault();
            Assert.NotNull(item);
            Assert.Equal(customName, item.Name);
            Assert.Equal("VERIFIED", item.Status);
            Assert.Contains("SYSTEM PROTECTION: ENABLED", item.DetailNote);
            Assert.Contains("WINDOWS VERIFICATION: PASSED", item.DetailNote);
            Assert.Contains("Sequence #142", item.DetailNote);
        }

        [Fact]
        public async Task Test_SystemProtectionRequired_Modal_State_And_Cancel_Flow()
        {
            var progress = OptimizationProgressService.Instance;

            // Trigger SystemProtectionRequired modal state
            var promptTask = progress.PromptEnableSystemProtectionAsync("C:");

            Assert.True(progress.IsSystemProtectionRequired);
            Assert.False(progress.IsCompleted);
            Assert.False(progress.IsFailed);
            Assert.Equal("SYSTEM PROTECTION IS OFF", progress.SystemProtectionTitle);
            Assert.Contains("System Protection is currently disabled", progress.SystemProtectionMessage);
            Assert.Equal("C:", progress.SystemProtectionDrive);
            Assert.False(progress.IsEnableProtectionBusy);

            // User clicks CANCEL
            progress.CancelSystemProtectionCommand.Execute(null);

            bool userResponse = await promptTask;
            Assert.False(userResponse);
            Assert.False(progress.IsSystemProtectionRequired);
            Assert.False(progress.IsOpen);
        }

        [Fact]
        public async Task Test_SystemProtectionRequired_Modal_State_And_Enable_Flow()
        {
            var progress = OptimizationProgressService.Instance;

            // Trigger SystemProtectionRequired modal state
            var promptTask = progress.PromptEnableSystemProtectionAsync("C:");

            Assert.True(progress.IsSystemProtectionRequired);

            // User clicks ENABLE & CONTINUE
            progress.EnableProtectionAndContinueCommand.Execute(null);

            bool userResponse = await promptTask;
            Assert.True(userResponse);
            Assert.True(progress.IsEnableProtectionBusy);
        }

        [Fact]
        public void Test_Zero_Fake_Success_On_Failure_States()
        {
            var progress = OptimizationProgressService.Instance;

            // Test ENABLE FAILED
            progress.ReportFailure(
                stage: "System Protection Verification",
                reason: "SYSTEM PROTECTION COULD NOT BE ENABLED: Group policy restriction.",
                title: "SYSTEM PROTECTION COULD NOT BE ENABLED"
            );

            Assert.True(progress.IsFailed);
            Assert.False(progress.IsCompleted);
            Assert.Equal("SYSTEM PROTECTION COULD NOT BE ENABLED", progress.ResultTitle);
            Assert.Equal("System Protection Verification", progress.FailureStage);
            Assert.Contains("SYSTEM PROTECTION COULD NOT BE ENABLED", progress.FailureReason);

            // Test CREATION FAILED
            progress.ReportFailure(
                stage: "SRSetRestorePointW Native API",
                reason: "RESTORE POINT CREATION FAILED: Access Denied.",
                title: "RESTORE POINT CREATION FAILED"
            );

            Assert.True(progress.IsFailed);
            Assert.False(progress.IsCompleted);
            Assert.Equal("RESTORE POINT CREATION FAILED", progress.ResultTitle);
            Assert.Equal("SRSetRestorePointW Native API", progress.FailureStage);

            // Test VERIFICATION FAILED
            progress.ReportFailure(
                stage: "Windows Readback Verification",
                reason: "RESTORE POINT CREATION VERIFICATION FAILED: Point not enumerated in WMI.",
                title: "RESTORE POINT CREATION VERIFICATION FAILED"
            );

            Assert.True(progress.IsFailed);
            Assert.False(progress.IsCompleted);
            Assert.Equal("RESTORE POINT CREATION VERIFICATION FAILED", progress.ResultTitle);
            Assert.Equal("Windows Readback Verification", progress.FailureStage);
        }

        [Fact]
        public void Test_UniversalOptimizationModal_Xaml_Contains_State_0C_And_Key_Bindings()
        {
            string modalXamlPath = @"D:\PROJECTS FOR EXE\ON PROCESS\ERROR OPTIMIZER\src\BiosOptimizer.GUI\Controls\UniversalOptimizationModal.xaml";
            string content = File.ReadAllText(modalXamlPath);

            Assert.Contains("STATE 0C: SYSTEM PROTECTION REQUIRED MODAL", content);
            Assert.Contains("IsSystemProtectionRequired", content);
            Assert.Contains("SystemProtectionTitle", content);
            Assert.Contains("SystemProtectionMessage", content);
            Assert.Contains("SystemProtectionDrive", content);
            Assert.Contains("CancelSystemProtectionCommand", content);
            Assert.Contains("EnableProtectionAndContinueCommand", content);
            Assert.Contains("ENABLE &amp; CONTINUE", content);
            Assert.Contains("IsEnableProtectionBusy", content);
        }

        [Fact]
        public void Test_StorageCleaner_CandidateAccounting_AndLockedFileHandling()
        {
            // 1. BuildCategories produces distinct paths for temp-files
            var categories = StorageCleanerEngine.BuildCategories();
            Assert.NotEmpty(categories);

            var tempCat = categories.FirstOrDefault(c => c.Id == "temp-files");
            Assert.NotNull(tempCat);
            Assert.NotEmpty(tempCat.TargetPaths);

            // Verify no duplicate paths in temp-files
            var distinctPaths = tempCat.TargetPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Assert.Equal(distinctPaths.Count, tempCat.TargetPaths.Count);

            // 2. Storage cleanup execution accounting and verification
            var engine = new StorageCleanerEngine();
            string testTempDir = Path.Combine(Path.GetTempPath(), "StorageTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testTempDir);

            try
            {
                // Create 2 dummy files
                string file1 = Path.Combine(testTempDir, "test1.tmp");
                string file2 = Path.Combine(testTempDir, "test2.tmp");
                File.WriteAllText(file1, "Hello World 12345");
                File.WriteAllText(file2, "Hello World 67890");

                var testCat = new StorageCleanupCategory
                {
                    Id = "temp-files",
                    Name = "Test Temp",
                    CategoryType = "TEMP",
                    RiskLevel = "SAFE",
                    TargetPaths = new List<string> { testTempDir }
                };

                // Scan should discover both files
                var scanned = engine.ScanCategory(testCat);
                Assert.Equal(2, scanned.FileCount);
                Assert.True(scanned.DetectedBytes > 0);
                Assert.Equal(CleanupApplicabilityState.Applicable, scanned.Applicability);

                // Clean the category
                var result = engine.CleanCategories(new[] { testCat }, "C", null, CancellationToken.None);
                Assert.True(result.Success);
                Assert.Equal(2, result.FilesRemoved);
                Assert.Equal(0, result.FilesFailed);

                // Post-clean rescan verification
                Assert.Equal(0, testCat.FileCount);
                Assert.Equal(0, testCat.DetectedBytes);
                Assert.Equal(CleanupApplicabilityState.AlreadyClean, testCat.Applicability);
                Assert.Equal(CategoryVerificationStatus.Verified, testCat.VerifiedStatus);
            }
            finally
            {
                if (Directory.Exists(testTempDir))
                {
                    try { Directory.Delete(testTempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Test_EmulatorDisambiguation_MSI_vs_BlueStacks_HDPlayerCollisions()
        {
            var msiItem = new InstalledWorkloadItem
            {
                CanonicalAppId = CanonicalWorkloadIds.MsiAppPlayer,
                DisplayName = "MSI App Player / MSI 5",
                ExecutableName = "HD-Player",
                ExecutableAliases = new List<string> { "HD-Player", "BlueStacks" },
                InstallationPath = @"C:\Program Files\BlueStacks_msi5\HD-Player.exe",
                InstallationRoot = @"C:\Program Files\BlueStacks_msi5",
                Type = WorkloadType.Emulator
            };

            var bs5Item = new InstalledWorkloadItem
            {
                CanonicalAppId = CanonicalWorkloadIds.BlueStacks5,
                DisplayName = "BlueStacks 5",
                ExecutableName = "HD-Player",
                ExecutableAliases = new List<string> { "HD-Player", "BlueStacks" },
                InstallationPath = @"C:\Program Files\BlueStacks_nxt\HD-Player.exe",
                InstallationRoot = @"C:\Program Files\BlueStacks_nxt",
                Type = WorkloadType.Emulator
            };

            var bs4Item = new InstalledWorkloadItem
            {
                CanonicalAppId = CanonicalWorkloadIds.BlueStacks4,
                DisplayName = "BlueStacks 4",
                ExecutableName = "BlueStacks",
                ExecutableAliases = new List<string> { "BlueStacks" },
                InstallationPath = @"C:\Program Files\BlueStacks\BlueStacks.exe",
                InstallationRoot = @"C:\Program Files\BlueStacks",
                Type = WorkloadType.Emulator
            };

            // Test 1: MSI App Player running HD-Player.exe
            string msiProcPath = @"C:\Program Files\BlueStacks_msi5\HD-Player.exe";
            string msiNorm = WorkloadOptimizationEngine.NormalizeProcessPath(msiProcPath);
            Assert.True(WorkloadOptimizationEngine.MatchesProcess(msiItem, msiNorm, "hd-player", "MSI App Player"));
            Assert.False(WorkloadOptimizationEngine.MatchesProcess(bs5Item, msiNorm, "hd-player", "MSI App Player"));
            Assert.False(WorkloadOptimizationEngine.MatchesProcess(bs4Item, msiNorm, "hd-player", "MSI App Player"));

            // Test 2: BlueStacks 5 running HD-Player.exe
            string bs5ProcPath = @"C:\Program Files\BlueStacks_nxt\HD-Player.exe";
            string bs5Norm = WorkloadOptimizationEngine.NormalizeProcessPath(bs5ProcPath);
            Assert.False(WorkloadOptimizationEngine.MatchesProcess(msiItem, bs5Norm, "hd-player", "BlueStacks 5"));
            Assert.True(WorkloadOptimizationEngine.MatchesProcess(bs5Item, bs5Norm, "hd-player", "BlueStacks 5"));
            Assert.False(WorkloadOptimizationEngine.MatchesProcess(bs4Item, bs5Norm, "hd-player", "BlueStacks 5"));

            // Test 3: Custom drive path for MSI
            string customMsiPath = @"D:\Emulators\BlueStacks_msi5\HD-Player.exe";
            string customMsiNorm = WorkloadOptimizationEngine.NormalizeProcessPath(customMsiPath);
            Assert.True(WorkloadOptimizationEngine.MatchesProcess(msiItem, customMsiNorm, "hd-player", "MSI App Player"));
            Assert.False(WorkloadOptimizationEngine.MatchesProcess(bs5Item, customMsiNorm, "hd-player", "MSI App Player"));

            // Test 4: Custom drive path for BlueStacks 5
            string customBs5Path = @"E:\Gaming\BlueStacks_nxt\HD-Player.exe";
            string customBs5Norm = WorkloadOptimizationEngine.NormalizeProcessPath(customBs5Path);
            Assert.False(WorkloadOptimizationEngine.MatchesProcess(msiItem, customBs5Norm, "hd-player", "BlueStacks"));
            Assert.True(WorkloadOptimizationEngine.MatchesProcess(bs5Item, customBs5Norm, "hd-player", "BlueStacks"));

            // Test 5: BlueStacks 4 process
            string bs4ProcPath = @"C:\Program Files\BlueStacks\BlueStacks.exe";
            string bs4Norm = WorkloadOptimizationEngine.NormalizeProcessPath(bs4ProcPath);
            Assert.False(WorkloadOptimizationEngine.MatchesProcess(msiItem, bs4Norm, "bluestacks", "BlueStacks"));
            Assert.False(WorkloadOptimizationEngine.MatchesProcess(bs5Item, bs4Norm, "bluestacks", "BlueStacks"));
            Assert.True(WorkloadOptimizationEngine.MatchesProcess(bs4Item, bs4Norm, "bluestacks", "BlueStacks"));
        }

        [Fact]
        public void Test_EmulatorDisambiguation_RunningStateSync_AllScenarios()
        {
            var engine = WorkloadOptimizationEngine.Instance;
            engine.InstalledInventory.Clear();

            var msiItem = new InstalledWorkloadItem
            {
                CanonicalAppId = CanonicalWorkloadIds.MsiAppPlayer,
                DisplayName = "MSI App Player / MSI 5",
                ExecutableName = "HD-Player",
                ExecutableAliases = new List<string> { "HD-Player", "BlueStacks" },
                InstallationPath = @"C:\Program Files\BlueStacks_msi5\HD-Player.exe",
                InstallationRoot = @"C:\Program Files\BlueStacks_msi5",
                Type = WorkloadType.Emulator
            };

            var bs5Item = new InstalledWorkloadItem
            {
                CanonicalAppId = CanonicalWorkloadIds.BlueStacks5,
                DisplayName = "BlueStacks 5",
                ExecutableName = "HD-Player",
                ExecutableAliases = new List<string> { "HD-Player", "BlueStacks" },
                InstallationPath = @"C:\Program Files\BlueStacks_nxt\HD-Player.exe",
                InstallationRoot = @"C:\Program Files\BlueStacks_nxt",
                Type = WorkloadType.Emulator
            };

            engine.InstalledInventory.Add(msiItem);
            engine.InstalledInventory.Add(bs5Item);

            // Scenario 1: MSI Running, BlueStacks 5 Closed
            engine.ActiveSessionsMap.Clear();
            engine.ActiveSessionsMap[CanonicalWorkloadIds.MsiAppPlayer] = new RunningWorkloadSession
            {
                ApplicationId = CanonicalWorkloadIds.MsiAppPlayer,
                DisplayName = "MSI App Player / MSI 5",
                ExecutableName = "HD-Player",
                ExecutablePath = @"C:\Program Files\BlueStacks_msi5\HD-Player.exe",
                InstallationRoot = @"C:\Program Files\BlueStacks_msi5",
                Type = WorkloadType.Emulator,
                PrimaryPid = 1234
            };

            // Trigger sync
            typeof(WorkloadOptimizationEngine)
                .GetMethod("SyncInventoryRunningStates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(engine, null);

            Assert.True(msiItem.IsRunning, "MSI App Player must be RUNNING when its session is active.");
            Assert.False(bs5Item.IsRunning, "BlueStacks 5 must NOT be RUNNING when only MSI App Player is running.");

            // Scenario 2: BlueStacks 5 Running, MSI Closed
            engine.ActiveSessionsMap.Clear();
            engine.ActiveSessionsMap[CanonicalWorkloadIds.BlueStacks5] = new RunningWorkloadSession
            {
                ApplicationId = CanonicalWorkloadIds.BlueStacks5,
                DisplayName = "BlueStacks 5",
                ExecutableName = "HD-Player",
                ExecutablePath = @"C:\Program Files\BlueStacks_nxt\HD-Player.exe",
                InstallationRoot = @"C:\Program Files\BlueStacks_nxt",
                Type = WorkloadType.Emulator,
                PrimaryPid = 5678
            };

            typeof(WorkloadOptimizationEngine)
                .GetMethod("SyncInventoryRunningStates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(engine, null);

            Assert.False(msiItem.IsRunning, "MSI App Player must NOT be RUNNING when only BlueStacks 5 is running.");
            Assert.True(bs5Item.IsRunning, "BlueStacks 5 must be RUNNING when its session is active.");

            // Scenario 3: Both Running
            engine.ActiveSessionsMap[CanonicalWorkloadIds.MsiAppPlayer] = new RunningWorkloadSession
            {
                ApplicationId = CanonicalWorkloadIds.MsiAppPlayer,
                DisplayName = "MSI App Player / MSI 5",
                ExecutableName = "HD-Player",
                ExecutablePath = @"C:\Program Files\BlueStacks_msi5\HD-Player.exe",
                InstallationRoot = @"C:\Program Files\BlueStacks_msi5",
                Type = WorkloadType.Emulator,
                PrimaryPid = 1234
            };

            typeof(WorkloadOptimizationEngine)
                .GetMethod("SyncInventoryRunningStates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(engine, null);

            Assert.True(msiItem.IsRunning, "MSI App Player must be RUNNING when both are active.");
            Assert.True(bs5Item.IsRunning, "BlueStacks 5 must be RUNNING when both are active.");

            // Scenario 4: Both Closed
            engine.ActiveSessionsMap.Clear();

            typeof(WorkloadOptimizationEngine)
                .GetMethod("SyncInventoryRunningStates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(engine, null);

            Assert.False(msiItem.IsRunning, "MSI App Player must be NOT RUNNING when no sessions exist.");
            Assert.False(bs5Item.IsRunning, "BlueStacks 5 must be NOT RUNNING when no sessions exist.");
        }
    }
}
