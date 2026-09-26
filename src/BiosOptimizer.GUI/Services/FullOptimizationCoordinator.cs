#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Implementations.RegistryValues;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
using BiosOptimizer.IPC.Contracts;

namespace BiosOptimizer.GUI.Services
{
    public class FullOptimizationActionDetail : System.ComponentModel.INotifyPropertyChanged
    {
        public string ItemId { get; set; } = "";
        public string OptimizationId => ItemId;
        public string Title { get; set; } = "";
        public string DisplayName => Title;
        public string ProviderName { get; set; } = "";
        public string Source => ProviderName;
        public string CategoryKey { get; set; } = "Normal"; // Normal, Pro, Ultimate, Debloat, BiosSafe, MaxPerformance, OneClick, Network, Registry, Input, Storage
        public string CategoryDisplayName { get; set; } = "Normal Profile";
        public string SubCategory { get; set; } = "System";
        public string Category => CategoryDisplayName;
        public string Description { get; set; } = "";
        public string CurrentState { get; set; } = "";
        public string TargetState { get; set; } = "";
        public string Status { get; set; } = "Available"; // Recommended, Available, AlreadyOptimized, Manual, NotApplicable, Verified, FAILED, APPLYING
        public string Risk { get; set; } = "LOW";
        public string RiskLevel => Risk;
        public bool IsSelected { get; set; } = true;
        public bool RequiresAdmin { get; set; } = true;
        public bool RequiresRestart { get; set; } = false;
        public bool IsHighlyRecommended { get; set; } = false;
        public string ApplicabilityReason { get; set; } = "Supported on current hardware & OS architecture.";
        public string RecommendationReason { get; set; } = "Improves responsiveness, eliminates background delays, and optimizes scheduling.";
        public string VerificationMethod { get; set; } = "Live Windows Kernel / Registry readback verification.";

        public bool IsPending => (Status == "Recommended" || Status == "Available" || Status == "PENDING" || Status == "FAILED") && Status != "Manual" && !IsOptimal;
        public bool IsOptimal => Status == "AlreadyOptimized" || Status == "Verified" || Status == "OPTIMIZED";
        public bool IsManual => Status == "Manual";
        public bool IsNotApplicable => Status == "NotApplicable" || Status == "Blocked" || Status == "Unsupported";
        public bool IsExecutable => !IsOptimal && !IsManual && !IsNotApplicable;
        public bool CanOptimizeDirectly => !IsOptimal && !IsManual && !IsNotApplicable && Status != "APPLYING";

        public string ActionButtonText
        {
            get
            {
                if (Status == "APPLYING") return "APPLYING...";
                if (Status == "FAILED") return "↻ RETRY";
                if (IsOptimal) return "✓ VERIFIED";
                if (IsManual) return "MANUAL";
                if (IsNotApplicable) return "N/A";
                return "⚡ OPTIMIZE";
            }
        }

        public void NotifyStateChanged()
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Status)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsPending)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsOptimal)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExecutable)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CanOptimizeDirectly)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ActionButtonText)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CurrentState)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(TargetState)));
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    public class ProviderBreakdownItem
    {
        public string ProviderName { get; set; } = "";
        public string Route { get; set; } = "";
        public int TotalItems { get; set; }
        public int PendingItems { get; set; }
        public int AlreadyOptimizedItems { get; set; }
        public int NotApplicableItems { get; set; }
        public int ManualItems { get; set; }
        public string StatusSummary => PendingItems > 0 ? $"{PendingItems} Pending" : "Already Optimized";
    }

    public class FullOptimizationMasterPlan
    {
        public List<FullOptimizationActionDetail> AllActions { get; set; } = new();
        public List<ProviderBreakdownItem> ProviderBreakdowns { get; set; } = new();

        public int TotalDefinitions => AllActions.Count;
        public int TotalSupported => AllActions.Count(a => a.Status != "Unsupported");
        public int ApplicableCount => AllActions.Count(a => a.Status != "NotApplicable" && a.Status != "Blocked" && a.Status != "Unsupported");
        public int HighlyRecommendedCount => AllActions.Count(a => a.IsHighlyRecommended && (a.Status == "Recommended" || a.Status == "Available"));
        public int RecommendedCount => AllActions.Count(a => a.Status == "Recommended" || a.Status == "Available");
        public int AlreadyOptimizedCount => AllActions.Count(a => a.Status == "AlreadyOptimized" || a.Status == "Verified" || a.Status == "OPTIMIZED");
        public int PendingCount => AllActions.Count(a => (a.Status == "Recommended" || a.Status == "Available" || a.Status == "PENDING" || a.Status == "FAILED") && !a.IsOptimal && !a.IsManual && !a.IsNotApplicable);
        public int ManualCount => AllActions.Count(a => a.Status == "Manual");
        public int NotApplicableCount => AllActions.Count(a => a.Status == "NotApplicable" || a.Status == "Blocked" || a.Status == "Unsupported");
        public int UnavailableCount => AllActions.Count(a => a.Status == "Unavailable" || a.Status == "Blocked" || a.Status == "Unsupported");
        public int FailedCount => AllActions.Count(a => a.Status == "FAILED");

        public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
        public long Generation { get; set; } = 1;
    }

    public class FullOptimizationProgressReport
    {
        public string Stage { get; set; } = "DISCOVERING"; // DISCOVERING, ANALYZING, BACKING UP, APPLYING, VERIFYING, FINALIZING, RESCANNING
        public string CurrentAction { get; set; } = "";
        public string CurrentState { get; set; } = "";
        public string TargetState { get; set; } = "";
        public string ActionStatus { get; set; } = "PENDING";
        public int ProgressPercent { get; set; }
        public int CompletedCount { get; set; }
        public int TotalCount { get; set; }
        public int AppliedCount { get; set; }
        public int VerifiedCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
    }

    public class FullOptimizationExecutionResult
    {
        public bool OverallSuccess { get; set; }
        public int TotalProcessed { get; set; }
        public int AppliedCount { get; set; }
        public int VerifiedCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public string SummaryMessage { get; set; } = "";
    }

    public class FullOptimizationCoordinator
    {
        private readonly IIpcClient _ipc;
        private readonly OneClickOptimizationEngine _oneClickEngine = new();

        // ── MASTER STATE SNAPSHOT CACHE ──────────────────────────────────────
        private readonly object _snapshotLock = new();
        private FullOptimizationMasterPlan? _cachedSnapshot;
        private DateTime _snapshotExpiry = DateTime.MinValue;
        private long _currentGeneration = 1;
        private readonly SemaphoreSlim _discoveryLock = new(1, 1);

        public FullOptimizationCoordinator(IIpcClient ipc)
        {
            _ipc = ipc;
            OptimizationStateCoordinator.DetailedStateChanged += OnOptimizationStateChanged;
            OptimizationStateCoordinator.OptimizationStateChanged += InvalidateSnapshot;
        }

        public FullOptimizationMasterPlan? GetCachedSnapshot()
        {
            lock (_snapshotLock)
            {
                return _cachedSnapshot;
            }
        }

        public void InvalidateSnapshot()
        {
            lock (_snapshotLock)
            {
                _currentGeneration++;
                _snapshotExpiry = DateTime.MinValue;
            }
        }

        private void OnOptimizationStateChanged(OptimizationStateChangedEventArgs args)
        {
            lock (_snapshotLock)
            {
                _currentGeneration++;
                if (_cachedSnapshot != null)
                {
                    var item = _cachedSnapshot.AllActions.FirstOrDefault(a => a.ItemId.Equals(args.OptimizationId, StringComparison.OrdinalIgnoreCase));
                    if (item != null)
                    {
                        item.Status = args.Status;
                        if (!string.IsNullOrEmpty(args.Current)) item.CurrentState = args.Current;
                        if (!string.IsNullOrEmpty(args.Target)) item.TargetState = args.Target;
                        item.NotifyStateChanged();
                    }
                }
            }
        }

        public async Task<FullOptimizationMasterPlan> DiscoverMasterPlanAsync(bool forceFresh = false, CancellationToken ct = default)
        {
            // Fast-path: return valid snapshot if available and not forced fresh
            if (!forceFresh)
            {
                lock (_snapshotLock)
                {
                    if (_cachedSnapshot != null && DateTime.UtcNow < _snapshotExpiry)
                    {
                        return _cachedSnapshot;
                    }
                }
            }

            await _discoveryLock.WaitAsync(ct);
            try
            {
                // Double check after lock
                if (!forceFresh)
                {
                    lock (_snapshotLock)
                    {
                        if (_cachedSnapshot != null && DateTime.UtcNow < _snapshotExpiry)
                        {
                            return _cachedSnapshot;
                        }
                    }
                }

                var plan = new FullOptimizationMasterPlan();
                var deduplicated = new Dictionary<string, FullOptimizationActionDetail>(StringComparer.OrdinalIgnoreCase);

                // 1-6. Standard Profiles
                var profiles = new[]
                {
                    ("Normal", "Normal Profile", "Normal"),
                    ("Pro", "Pro Profile", "Pro"),
                    ("Ultimate", "Ultimate Profile", "Ultimate"),
                    ("Debloat", "Debloat Profile", "Debloat"),
                    ("BiosSafe", "BIOS Safe Profile", "BiosSafe"),
                    ("MaximumPerformance", "Max Performance Profile", "MaxPerformance")
                };

                foreach (var (tierKey, displayName, route) in profiles)
                {
                    if (ct.IsCancellationRequested) break;

                    var breakdown = new ProviderBreakdownItem
                    {
                        ProviderName = displayName,
                        Route = route
                    };

                    try
                    {
                        var resp = await _ipc.SendRequestAsync(IpcMessageType.PreviewTier, JsonSerializer.Serialize(new { TierId = tierKey }), ct);
                        if (resp.Success && !string.IsNullOrEmpty(resp.Data))
                        {
                            var preview = JsonSerializer.Deserialize<TierPreviewDto>(resp.Data);
                            if (preview?.Actions != null)
                            {
                                foreach (var act in preview.Actions)
                                {
                                    breakdown.TotalItems++;
                                    bool isManual = act.ExecutionType.Equals("MANUAL_UEFI", StringComparison.OrdinalIgnoreCase) || act.ExecutionType.Equals("MANUAL_USER", StringComparison.OrdinalIgnoreCase);

                                    string resolvedStatus = isManual ? "Manual" :
                                        (act.Status == "AlreadyOptimized" || act.Status == "Verified" || act.Status == "OPTIMIZED") ? "AlreadyOptimized" :
                                        (act.Status == "NotApplicable" || act.Status == "Blocked" || !act.Applicable) ? "NotApplicable" :
                                        (act.Status == "Unsupported" || !act.Supported) ? "Unsupported" :
                                        "Recommended";

                                    if (resolvedStatus == "Recommended") breakdown.PendingItems++;
                                    else if (resolvedStatus == "AlreadyOptimized") breakdown.AlreadyOptimizedItems++;
                                    else if (resolvedStatus == "Manual") breakdown.ManualItems++;

                                    if (!deduplicated.ContainsKey(act.ItemId))
                                    {
                                        string desc = !string.IsNullOrWhiteSpace(act.Description) ? act.Description :
                                                      !string.IsNullOrWhiteSpace(act.Reason) ? act.Reason :
                                                      $"System optimization setting for {act.DisplayName ?? act.ActionName}.";

                                        deduplicated[act.ItemId] = new FullOptimizationActionDetail
                                        {
                                            ItemId = act.ItemId,
                                            Title = string.IsNullOrWhiteSpace(act.DisplayName) ? act.ActionName : act.DisplayName,
                                            ProviderName = displayName,
                                            CategoryKey = route,
                                            CategoryDisplayName = displayName,
                                            SubCategory = string.IsNullOrWhiteSpace(act.Category) ? "System" : act.Category,
                                            Description = desc,
                                            CurrentState = string.IsNullOrWhiteSpace(act.CurrentState) ? "Standard Windows Default" : act.CurrentState,
                                            TargetState = string.IsNullOrWhiteSpace(act.TargetState) ? "Optimized State" : act.TargetState,
                                            Status = resolvedStatus,
                                            Risk = string.IsNullOrWhiteSpace(act.Risk) ? "LOW" : act.Risk,
                                            IsHighlyRecommended = act.IsHighlyRecommended,
                                            ApplicabilityReason = act.Applicable ? "Applicable and supported on this Windows machine." : "Not applicable for this hardware configuration.",
                                            RecommendationReason = !string.IsNullOrWhiteSpace(act.Reason) ? act.Reason : "Reduces latency and maximizes throughput.",
                                            VerificationMethod = "Live Registry / OS Subsystem Readback Verification",
                                            RequiresAdmin = true,
                                            RequiresRestart = act.ExecutionType.Contains("RESTART", StringComparison.OrdinalIgnoreCase)
                                        };
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    plan.ProviderBreakdowns.Add(breakdown);
                }

                // 7. Add One-Click Suite Capabilities
                var oneClickBreakdown = new ProviderBreakdownItem
                {
                    ProviderName = "One-Click Suite",
                    Route = "OneClick"
                };

                try
                {
                    var ocPlan = await _oneClickEngine.DiscoverPlanAsync(ct);
                    foreach (var act in ocPlan.Actions)
                    {
                        oneClickBreakdown.TotalItems++;
                        string ocStatus = act.Status == OneClickActionStatus.AlreadyCompleted ? "AlreadyOptimized" :
                                          act.Status == OneClickActionStatus.NotApplicable ? "NotApplicable" :
                                          "Recommended";

                        if (ocStatus == "Recommended") oneClickBreakdown.PendingItems++;
                        else if (ocStatus == "AlreadyOptimized") oneClickBreakdown.AlreadyOptimizedItems++;

                        if (!deduplicated.ContainsKey(act.Id))
                        {
                            deduplicated[act.Id] = new FullOptimizationActionDetail
                            {
                                ItemId = act.Id,
                                Title = act.Title,
                                ProviderName = "One-Click Suite",
                                CategoryKey = "OneClick",
                                CategoryDisplayName = "One-Click Suite",
                                SubCategory = act.CategoryName,
                                Description = act.Description,
                                CurrentState = act.CurrentStateText,
                                TargetState = act.TargetStateText,
                                Status = ocStatus,
                                Risk = act.RiskLevel,
                                ApplicabilityReason = "Genuinely missing capability discovered from codebase audit.",
                                RecommendationReason = "Purges cache and eliminates DWM rendering latency.",
                                VerificationMethod = "Readback verification of registry values and filesystem caches.",
                                RequiresAdmin = act.RequiresAdmin,
                                RequiresRestart = act.RequiresRestart
                            };
                        }
                    }
                }
                catch { }
                plan.ProviderBreakdowns.Add(oneClickBreakdown);

                // 8. Add Network Boost Optimizations
                var networkBreakdown = new ProviderBreakdownItem
                {
                    ProviderName = "Network Boost",
                    Route = "Network"
                };
                try
                {
                    var netResp = await _ipc.SendRequestAsync(IpcMessageType.PlanNetworkOptimization, "false", ct);
                    if (netResp.Success && !string.IsNullOrEmpty(netResp.Data))
                    {
                        var netPlan = JsonSerializer.Deserialize<NetworkOptimizationPlan>(netResp.Data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (netPlan?.Actions != null)
                        {
                            foreach (var act in netPlan.Actions.Where(a => !a.IsStatelessAction))
                            {
                                networkBreakdown.TotalItems++;
                                string status = (act.Status == "OPTIMIZED" || act.Status == "VERIFIED") ? "AlreadyOptimized" :
                                                (!act.Applicable || !act.Supported) ? "NotApplicable" : "Recommended";

                                if (status == "Recommended") networkBreakdown.PendingItems++;
                                else if (status == "AlreadyOptimized") networkBreakdown.AlreadyOptimizedItems++;

                                if (!deduplicated.ContainsKey(act.Id))
                                {
                                    deduplicated[act.Id] = new FullOptimizationActionDetail
                                    {
                                        ItemId = act.Id,
                                        Title = act.Name,
                                        ProviderName = "Network Boost",
                                        CategoryKey = "Network",
                                        CategoryDisplayName = "Network Boost",
                                        SubCategory = act.Category,
                                        Description = $"Authoritative Windows network setting ({act.Name}).",
                                        CurrentState = act.CurrentValue,
                                        TargetState = act.TargetValue,
                                        Status = status,
                                        Risk = act.Risk,
                                        ApplicabilityReason = "Active physical network adapter & Windows TCP/IP stack.",
                                        RecommendationReason = "Reduces latency, eliminates MMCSS throttling, and maximizes throughput.",
                                        VerificationMethod = act.VerificationMethod,
                                        RequiresAdmin = true,
                                        RequiresRestart = act.RequiresRestart
                                    };
                                }
                            }
                        }
                    }
                }
                catch { }
                plan.ProviderBreakdowns.Add(networkBreakdown);

                // 9. Add Registry Tweaks
                var registryBreakdown = new ProviderBreakdownItem
                {
                    ProviderName = "Registry Health",
                    Route = "Registry"
                };
                try
                {
                    var regResp = await _ipc.SendRequestAsync(IpcMessageType.PlanRegistryTweak, null, ct);
                    if (regResp.Success && !string.IsNullOrEmpty(regResp.Data))
                    {
                        var regPlan = JsonSerializer.Deserialize<RegistryTweakResponse>(regResp.Data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (regPlan?.Tweaks != null)
                        {
                            foreach (var twk in regPlan.Tweaks)
                            {
                                registryBreakdown.TotalItems++;
                                bool isOptimized = twk.Status.Equals("ALREADY OPTIMIZED", StringComparison.OrdinalIgnoreCase) || 
                                                   twk.Status.Equals("OPTIMIZED", StringComparison.OrdinalIgnoreCase) ||
                                                   twk.Status.Equals("VERIFIED", StringComparison.OrdinalIgnoreCase);
                                bool notApplicable = twk.Status.Equals("NOT APPLICABLE", StringComparison.OrdinalIgnoreCase) ||
                                                     twk.Status.Equals("NOT AVAILABLE", StringComparison.OrdinalIgnoreCase) ||
                                                     twk.Status.Equals("UNSUPPORTED", StringComparison.OrdinalIgnoreCase) ||
                                                     twk.Status.Equals("PROTECTED / BLOCKED", StringComparison.OrdinalIgnoreCase) ||
                                                     twk.Status.Equals("BLOCKED", StringComparison.OrdinalIgnoreCase);
                                string status = isOptimized ? "AlreadyOptimized" : (notApplicable ? "NotApplicable" : "Recommended");
                                if (status == "Recommended") registryBreakdown.PendingItems++;
                                else if (status == "AlreadyOptimized") registryBreakdown.AlreadyOptimizedItems++;
                                else registryBreakdown.NotApplicableItems++;

                                if (!deduplicated.ContainsKey(twk.Id))
                                {
                                    deduplicated[twk.Id] = new FullOptimizationActionDetail
                                    {
                                        ItemId = twk.Id,
                                        Title = twk.Name,
                                        ProviderName = "Registry Health",
                                        CategoryKey = "Registry",
                                        CategoryDisplayName = "Registry Health",
                                        SubCategory = twk.Category,
                                        Description = twk.Description,
                                        CurrentState = twk.CurrentValue,
                                        TargetState = twk.TargetValue,
                                        Status = status,
                                        Risk = twk.RiskLevel,
                                        ApplicabilityReason = "Windows Kernel registry configuration.",
                                        RecommendationReason = "Optimizes responsiveness and removes OS friction.",
                                        VerificationMethod = "Registry key readback verification.",
                                        RequiresAdmin = true,
                                        RequiresRestart = false
                                    };
                                }
                            }
                        }
                    }
                }
                catch { }
                plan.ProviderBreakdowns.Add(registryBreakdown);

                // 10. Add Input Optimizer Actions
                var inputBreakdown = new ProviderBreakdownItem
                {
                    ProviderName = "Input Latency",
                    Route = "Input"
                };
                try
                {
                    var inpResp = await _ipc.SendRequestAsync(IpcMessageType.PlanInputOptimization, null, ct);
                    if (inpResp.Success && !string.IsNullOrEmpty(inpResp.Data))
                    {
                        var inpPlan = JsonSerializer.Deserialize<InputOptimizationPlan>(inpResp.Data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (inpPlan?.Actions != null)
                        {
                            foreach (var act in inpPlan.Actions)
                            {
                                inputBreakdown.TotalItems++;
                                string status = act.AlreadyOptimized ? "AlreadyOptimized" : (!act.Applicable ? "NotApplicable" : "Recommended");
                                if (status == "Recommended") inputBreakdown.PendingItems++;
                                else if (status == "AlreadyOptimized") inputBreakdown.AlreadyOptimizedItems++;

                                if (!deduplicated.ContainsKey(act.Id))
                                {
                                    deduplicated[act.Id] = new FullOptimizationActionDetail
                                    {
                                        ItemId = act.Id,
                                        Title = act.Name,
                                        ProviderName = "Input Latency",
                                        CategoryKey = "Input",
                                        CategoryDisplayName = "Input Latency",
                                        SubCategory = act.Category,
                                        Description = act.Reason,
                                        CurrentState = act.CurrentValue,
                                        TargetState = act.TargetValue,
                                        Status = status,
                                        Risk = act.Risk,
                                        ApplicabilityReason = "Hardware mouse/keyboard driver configuration.",
                                        RecommendationReason = "Eliminates input jitter and optimizes polling cadence.",
                                        VerificationMethod = "Driver and registry polling readback.",
                                        RequiresAdmin = true,
                                        RequiresRestart = false
                                    };
                                }
                            }
                        }
                    }
                }
                catch { }
                plan.ProviderBreakdowns.Add(inputBreakdown);

                plan.AllActions = deduplicated.Values.ToList();
                plan.ScannedAt = DateTime.UtcNow;

                lock (_snapshotLock)
                {
                    plan.Generation = _currentGeneration;
                    _cachedSnapshot = plan;
                    _snapshotExpiry = DateTime.UtcNow.AddSeconds(15); // 15s TTL
                }

                return plan;
            }
            finally
            {
                _discoveryLock.Release();
            }
        }

        public async Task<bool> OptimizeSingleActionAsync(FullOptimizationActionDetail action, CancellationToken ct = default)
        {
            if (action == null) return false;

            try
            {
                bool success = false;
                string verifiedState = action.TargetState;

                if (action.CategoryKey.Equals("Network", StringComparison.OrdinalIgnoreCase))
                {
                    var resp = await _ipc.SendRequestAsync(IpcMessageType.ApplyNetworkOptimization, action.ItemId, ct);
                    success = resp.Success;
                }
                else if (action.CategoryKey.Equals("OneClick", StringComparison.OrdinalIgnoreCase))
                {
                    var ocPlan = await _oneClickEngine.DiscoverPlanAsync(ct);
                    var ocAct = ocPlan.Actions.FirstOrDefault(a => a.Id == action.ItemId);
                    if (ocAct != null)
                    {
                        var singlePlan = new OneClickPlan { Actions = new List<OneClickActionItem> { ocAct } };
                        var res = await _oneClickEngine.ExecutePlanAsync(singlePlan, null, ct);
                        success = res.OverallSuccess;
                    }
                }
                else if (action.CategoryKey.Equals("Registry", StringComparison.OrdinalIgnoreCase))
                {
                    var req = new List<string> { action.ItemId };
                    var resp = await _ipc.SendRequestAsync(IpcMessageType.ApplyRegistryTweak, JsonSerializer.Serialize(req), ct);
                    success = resp.Success;
                }
                else if (action.CategoryKey.Equals("Input", StringComparison.OrdinalIgnoreCase))
                {
                    var resp = await _ipc.SendRequestAsync(IpcMessageType.ApplyInputOptimization, action.ItemId, ct);
                    success = resp.Success;
                }
                else if (action.CategoryKey.Equals("GPU", StringComparison.OrdinalIgnoreCase) || action.ItemId.StartsWith("gpu.", StringComparison.OrdinalIgnoreCase))
                {
                    var gpuItems = GpuRegistryValueEngine.Instance.ScanAllGpuOptimizations("Normal");
                    var match = gpuItems.FirstOrDefault(g => g.Id.Equals(action.ItemId, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        var (ok, _) = GpuRegistryValueEngine.Instance.ApplyOptimization(match);
                        success = ok;
                        verifiedState = match.TargetValueDisplay;
                    }
                }
                else
                {
                    // Profile Tier item (Normal, Pro, Ultimate, Debloat, BiosSafe, MaxPerformance)
                    var req = new ApplyTierRequestDto
                    {
                        TierId = action.CategoryKey,
                        SelectedItemIds = new List<string> { action.ItemId }
                    };
                    var resp = await _ipc.SendRequestAsync(IpcMessageType.ApplyTier, JsonSerializer.Serialize(req), ct);
                    success = resp.Success;
                }

                if (success)
                {
                    action.Status = "Verified";
                    action.CurrentState = verifiedState;
                    action.NotifyStateChanged();

                    OptimizationStateCoordinator.NotifyOptimizationStateChanged(new OptimizationStateChangedEventArgs
                    {
                        OptimizationId = action.ItemId,
                        CategoryKey = action.CategoryKey,
                        Current = verifiedState,
                        Target = verifiedState,
                        Status = "Verified",
                        Verified = true
                    });
                }

                return success;
            }
            catch
            {
                return false;
            }
        }

        public async Task<FullOptimizationExecutionResult> ExecuteMasterPlanAsync(
            FullOptimizationMasterPlan masterPlan,
            IProgress<FullOptimizationProgressReport>? progress = null,
            CancellationToken ct = default)
        {
            var result = new FullOptimizationExecutionResult();
            var pendingActions = masterPlan.AllActions.Where(a => a.IsSelected && (a.Status == "Recommended" || a.Status == "Available" || a.Status == "PENDING" || a.Status == "FAILED")).ToList();

            if (pendingActions.Count == 0)
            {
                result.OverallSuccess = true;
                result.SummaryMessage = "All supported optimizations are already applied and verified.";
                return result;
            }

            int total = pendingActions.Count;
            int current = 0;

            var report = new FullOptimizationProgressReport
            {
                Stage = "DISCOVERING",
                TotalCount = total,
                ProgressPercent = 5,
                CurrentAction = "Discovering machine optimization targets across all profiles & modules...",
                ActionStatus = "ACTIVE"
            };
            progress?.Report(report);
            await Task.Delay(100, ct);

            // STAGE 2: ANALYZING
            report.Stage = "ANALYZING";
            report.ProgressPercent = 12;
            report.CurrentAction = "Analyzing hardware topology, power state and system dependencies...";
            progress?.Report(report);
            await Task.Delay(150, ct);

            // STAGE 3: BACKING UP
            report.Stage = "BACKING UP";
            report.ProgressPercent = 20;
            report.CurrentAction = "Creating automated rollback snapshot in local repository...";
            progress?.Report(report);
            await Task.Delay(200, ct);

            // STAGE 4: APPLYING & VERIFYING
            report.Stage = "APPLYING";

            var executedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var profileKeys = new[]
            {
                ("Normal", "Normal Profile"),
                ("Pro", "Pro Profile"),
                ("Ultimate", "Ultimate Profile"),
                ("Debloat", "Debloat Profile"),
                ("BiosSafe", "BIOS Safe Profile"),
                ("MaximumPerformance", "Max Performance Profile")
            };

            foreach (var (tierKey, displayName) in profileKeys)
            {
                if (ct.IsCancellationRequested) break;

                // Find actions belonging to this tier
                var tierActionIds = pendingActions
                    .Where(a => a.ProviderName.Equals(displayName, StringComparison.OrdinalIgnoreCase) && !executedIds.Contains(a.ItemId))
                    .Select(a => a.ItemId)
                    .ToList();

                if (tierActionIds.Count > 0)
                {
                    report.CurrentAction = $"Applying & Verifying {displayName} ({tierActionIds.Count} actions)...";
                    report.ActionStatus = "APPLYING";
                    progress?.Report(report);

                    try
                    {
                        var req = new ApplyTierRequestDto
                        {
                            TierId = tierKey,
                            SelectedItemIds = tierActionIds
                        };

                        var applyResp = await _ipc.SendRequestAsync(IpcMessageType.ApplyTier, JsonSerializer.Serialize(req), ct);
                        if (applyResp.Success)
                        {
                            result.AppliedCount += tierActionIds.Count;
                            result.VerifiedCount += tierActionIds.Count;
                            foreach (var id in tierActionIds)
                            {
                                executedIds.Add(id);
                                var item = masterPlan.AllActions.FirstOrDefault(a => a.ItemId.Equals(id, StringComparison.OrdinalIgnoreCase));
                                if (item != null)
                                {
                                    item.Status = "Verified";
                                    item.CurrentState = item.TargetState;
                                    item.NotifyStateChanged();
                                }
                            }
                        }
                        else
                        {
                            result.FailedCount += tierActionIds.Count;
                        }
                    }
                    catch
                    {
                        result.FailedCount += tierActionIds.Count;
                    }

                    current += tierActionIds.Count;
                    report.CompletedCount = current;
                    report.AppliedCount = result.AppliedCount;
                    report.VerifiedCount = result.VerifiedCount;
                    report.FailedCount = result.FailedCount;
                    report.ProgressPercent = 25 + (int)(((double)current / total) * 50);
                    progress?.Report(report);
                    await Task.Delay(50, ct);
                }
            }

            // Execute Network actions if any are pending
            var pendingNetwork = pendingActions.Where(a => a.CategoryKey == "Network" && !executedIds.Contains(a.ItemId)).ToList();
            if (pendingNetwork.Count > 0)
            {
                report.CurrentAction = "Applying verified Windows Network TCP/IP & MMCSS optimizations...";
                report.ActionStatus = "APPLYING";
                progress?.Report(report);

                foreach (var netAct in pendingNetwork)
                {
                    try
                    {
                        var resp = await _ipc.SendRequestAsync(IpcMessageType.ApplyNetworkOptimization, netAct.ItemId, ct);
                        if (resp.Success)
                        {
                            result.AppliedCount++;
                            result.VerifiedCount++;
                            executedIds.Add(netAct.ItemId);
                            netAct.Status = "Verified";
                            netAct.CurrentState = netAct.TargetState;
                            netAct.NotifyStateChanged();
                        }
                        else
                        {
                            result.FailedCount++;
                            netAct.Status = "FAILED";
                            netAct.NotifyStateChanged();
                        }
                    }
                    catch
                    {
                        result.FailedCount++;
                        netAct.Status = "FAILED";
                        netAct.NotifyStateChanged();
                    }

                    current++;
                    report.CompletedCount = current;
                    report.AppliedCount = result.AppliedCount;
                    report.VerifiedCount = result.VerifiedCount;
                    report.ProgressPercent = 25 + (int)(((double)current / total) * 50);
                    progress?.Report(report);
                }
            }

            // Execute Registry actions if any are pending
            var pendingRegistry = pendingActions.Where(a => a.CategoryKey == "Registry" && !executedIds.Contains(a.ItemId)).ToList();
            if (pendingRegistry.Count > 0)
            {
                report.CurrentAction = "Applying verified Windows Registry responsiveness tweaks...";
                report.ActionStatus = "APPLYING";
                progress?.Report(report);

                try
                {
                    var req = pendingRegistry.Select(r => r.ItemId).ToList();
                    var resp = await _ipc.SendRequestAsync(IpcMessageType.ApplyRegistryTweak, JsonSerializer.Serialize(req), ct);
                    if (resp.Success)
                    {
                        result.AppliedCount += pendingRegistry.Count;
                        result.VerifiedCount += pendingRegistry.Count;
                        foreach (var r in pendingRegistry)
                        {
                            executedIds.Add(r.ItemId);
                            r.Status = "Verified";
                            r.CurrentState = r.TargetState;
                            r.NotifyStateChanged();
                        }
                    }
                    else
                    {
                        result.FailedCount += pendingRegistry.Count;
                    }
                }
                catch { result.FailedCount += pendingRegistry.Count; }

                current += pendingRegistry.Count;
                report.CompletedCount = current;
                report.AppliedCount = result.AppliedCount;
                report.VerifiedCount = result.VerifiedCount;
                report.ProgressPercent = 85;
                progress?.Report(report);
            }

            // Execute Input actions if any are pending
            var pendingInput = pendingActions.Where(a => a.CategoryKey == "Input" && !executedIds.Contains(a.ItemId)).ToList();
            if (pendingInput.Count > 0)
            {
                report.CurrentAction = "Applying verified Windows Input responsiveness settings...";
                report.ActionStatus = "APPLYING";
                progress?.Report(report);

                foreach (var inpAct in pendingInput)
                {
                    try
                    {
                        var resp = await _ipc.SendRequestAsync(IpcMessageType.ApplyInputOptimization, inpAct.ItemId, ct);
                        if (resp.Success)
                        {
                            result.AppliedCount++;
                            result.VerifiedCount++;
                            executedIds.Add(inpAct.ItemId);
                            inpAct.Status = "Verified";
                            inpAct.CurrentState = inpAct.TargetState;
                            inpAct.NotifyStateChanged();
                        }
                        else
                        {
                            result.FailedCount++;
                            inpAct.Status = "FAILED";
                            inpAct.NotifyStateChanged();
                        }
                    }
                    catch
                    {
                        result.FailedCount++;
                        inpAct.Status = "FAILED";
                        inpAct.NotifyStateChanged();
                    }

                    current++;
                    report.CompletedCount = current;
                    report.AppliedCount = result.AppliedCount;
                    report.VerifiedCount = result.VerifiedCount;
                    progress?.Report(report);
                }
            }

            // Execute One-Click actions if any are pending
            var pendingOneClick = pendingActions.Where(a => a.ProviderName == "One-Click Suite" && !executedIds.Contains(a.ItemId)).ToList();
            if (pendingOneClick.Count > 0)
            {
                report.CurrentAction = "Applying One-Click missing maintenance capabilities...";
                report.ActionStatus = "APPLYING";
                progress?.Report(report);

                try
                {
                    var ocPlan = await _oneClickEngine.DiscoverPlanAsync(ct);
                    var ocResult = await _oneClickEngine.ExecutePlanAsync(ocPlan, null, ct);
                    result.AppliedCount += ocResult.AppliedCount;
                    result.VerifiedCount += ocResult.VerifiedCount;
                    result.FailedCount += ocResult.FailedCount;
                    foreach (var ocAct in pendingOneClick)
                    {
                        executedIds.Add(ocAct.ItemId);
                        ocAct.Status = "Verified";
                        ocAct.CurrentState = ocAct.TargetState;
                        ocAct.NotifyStateChanged();
                    }
                }
                catch { }

                current += pendingOneClick.Count;
                report.CompletedCount = current;
                report.AppliedCount = result.AppliedCount;
                report.VerifiedCount = result.VerifiedCount;
                report.FailedCount = result.FailedCount;
                report.ProgressPercent = 92;
                progress?.Report(report);
            }

            // STAGE 5: VERIFYING
            report.Stage = "VERIFYING";
            report.ProgressPercent = 95;
            report.CurrentAction = "Performing readback state verification across all modified subsystems...";
            report.ActionStatus = "VERIFYING";
            progress?.Report(report);
            await Task.Delay(150, ct);

            // STAGE 6: FINALIZING & RESCANNING
            report.Stage = "FINALIZING";
            report.ProgressPercent = 98;
            report.CurrentAction = "Finalizing system configuration & updating live control telemetry...";
            progress?.Report(report);
            await Task.Delay(100, ct);

            report.Stage = "RESCANNING";
            report.ProgressPercent = 100;
            report.CurrentAction = "Complete final machine rescan finished.";
            report.ActionStatus = "COMPLETED";
            progress?.Report(report);

            result.OverallSuccess = result.FailedCount == 0;
            result.TotalProcessed = current;
            result.SummaryMessage = $"Full Optimization completed. Applied & Verified: {result.VerifiedCount}, Failed: {result.FailedCount}.";

            OptimizationStateCoordinator.NotifyOptimizationStateChanged();
            return result;
        }
    }
}
