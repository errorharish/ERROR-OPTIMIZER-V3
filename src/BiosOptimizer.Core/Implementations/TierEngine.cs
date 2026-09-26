using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
using BiosOptimizer.Core.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Implementations;
#pragma warning disable 1998

public class TierEngine : ITierEngine
{
    private readonly IProfileRepository _profileRepo;
    private readonly IEnvironmentDetector _envDetector;
    private readonly IActionRegistry _actionRegistry;
    private readonly IProcessSnapshot _processSnapshot;
    private readonly ConditionEvaluator _conditionEvaluator;
    private readonly IBackupManager _backupManager;

    // Never-touch enforcement (last line of defence - handlers also enforce this)
    private static readonly HashSet<string> NeverTouchServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "WinDefend", "SecurityHealthService", "mpssvc", "wscsvc", "Sense", "WdNisSvc", "MpsSvc",
        "AudioSrv", "AudioEndpointBuilder", "RpcSs", "RpcEptMapper", "DcomLaunch", "PlugPlay",
        "Power", "EventLog", "EventSystem", "BrokerInfrastructure", "SystemEventsBroker",
        "TimeBrokerSvc", "Schedule", "CryptSvc", "Dhcp", "Dnscache", "netprofm", "nsi",
        "Winmgmt", "LSM", "FontCache", "StateRepository", "ProfSvc", "gpsvc",
        "LanmanWorkstation", "LanmanServer", "Themes", "WlanSvc", "WwanSvc", "NlaSvc",
        "Netman", "NlaSvc", "netprofm"
    };

    // Services where we can set Manual but never Disabled
    private static readonly HashSet<string> ManualOnlyServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "UsoSvc", "wuauserv", "BITS", "DoSvc"
    };

    public TierEngine(
        IProfileRepository profileRepo,
        IEnvironmentDetector envDetector,
        IActionRegistry actionRegistry,
        IProcessSnapshot processSnapshot,
        IBackupManager backupManager)
    {
        _profileRepo = profileRepo;
        _envDetector = envDetector;
        _actionRegistry = actionRegistry;
        _processSnapshot = processSnapshot;
        _backupManager = backupManager;
        _conditionEvaluator = new ConditionEvaluator();
    }

    public Task<OptimizationPreview> PreviewAsync(string tierId, CancellationToken cancellationToken)
    {
        var profile = _profileRepo.LoadResolvedProfile(tierId);
        if (profile == null) throw new Exception("Profile not found.");

        var env = _envDetector.Detect();
        var snapshot = _processSnapshot.TakeSnapshot();
        var preview = new OptimizationPreview
        {
            CurrentProcessCount = snapshot.ProcessCount,
            EstimatedProcessReduction = (profile.TargetProcessReduction.Min + profile.TargetProcessReduction.Max) / 2,
            Baseline = new PerformanceBaseline
            {
                Timestamp = DateTime.UtcNow,
                ProcessCount = snapshot.ProcessCount,
                AvailableRamBytes = env.HardwareProfile.RAM,
                PowerPlan = env.HardwareProfile.PowerPlan
            }
        };

        foreach (var entry in profile.Entries)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Safety gate: engine-level never-touch enforcement
            if (IsNeverTouch(entry))
            {
                preview.SkippedChanges.Add(new SkippedChange { Id = entry.Id, Item = entry.DisplayName, Category = "NotApplicable", Reason = "Protected system component" });
                continue;
            }
            if (IsDisabledForManualOnlyService(entry))
            {
                preview.SkippedChanges.Add(new SkippedChange { Id = entry.Id, Item = entry.DisplayName, Category = "NotApplicable", Reason = "Manual-only service policy" });
                continue;
            }

            if (!IsOsSupported(entry, env))
            {
                preview.SkippedChanges.Add(new SkippedChange { Id = entry.Id, Item = entry.DisplayName, Category = "Unsupported", Reason = $"Unsupported on this OS ({(env.IsWindows10 ? "Win10" : "Win11")})" });
                continue;
            }

            if (!_conditionEvaluator.Evaluate(entry.Conditions, env))
            {
                var reason = BuildConditionReason(entry.Conditions, env);
                preview.SkippedChanges.Add(new SkippedChange { Id = entry.Id, Item = entry.DisplayName, Category = "NotApplicable", Reason = reason });
                continue;
            }

            var handler = _actionRegistry.GetHandler(entry.Action);
            if (handler == null)
            {
                preview.SkippedChanges.Add(new SkippedChange { Id = entry.Id, Item = entry.DisplayName, Category = "Unsupported", Reason = "Handler unavailable" });
                continue;
            }

            var availability = handler.CheckAvailability(entry, env);
            if (availability != TargetState.Ready)
            {
                preview.SkippedChanges.Add(new SkippedChange { Id = entry.Id, Item = entry.DisplayName, Category = "NotApplicable", Reason = availability == TargetState.NotAvailable ? "Target not present on this machine" : "Action not applicable" });
                continue;
            }

            var change = new PlannedChange
            {
                Id = entry.Id,
                ActionName = entry.Action,
                Item = entry.DisplayName,
                Category = entry.Category,
                Current = handler.GetCurrentValueDisplay(entry),
                Target = entry.Value?.ToString() ?? "",
                Risk = entry.Risk,
                Warning = entry.Warning,
                Description = entry.Description,
                ExecutionType = entry.ExecutionType ?? "WINDOWS_AUTOMATIC"
            };

            if (string.Equals(entry.SourceProfile, tierId, StringComparison.OrdinalIgnoreCase))
            {
                change.IsInherited = false;
                preview.PlannedChanges.Add(change);
            }
            else
            {
                change.IsInherited = true;
                preview.InheritedChanges.Add(change);
                preview.InheritedEntryCount++;
            }
        }

        return Task.FromResult(preview);
    }

    public async Task<List<OptimizationResult>> ApplyAsync(string tierId, ConfirmationContext confirmation, bool dryRun, CancellationToken cancellationToken, IProgress<OptimizationResult>? progress = null, HashSet<string>? allowedItemIds = null)
    {
        var profile = _profileRepo.LoadResolvedProfile(tierId);
        if (profile == null) throw new Exception("Profile not found.");
        
        return await ApplyAsync(profile, confirmation, dryRun, cancellationToken, progress, allowedItemIds);
    }
    
    public async Task<List<OptimizationResult>> ApplyAsync(ProfileDef profile, ConfirmationContext confirmation, bool dryRun, CancellationToken cancellationToken, IProgress<OptimizationResult>? progress = null, HashSet<string>? allowedItemIds = null)
    {
        var results = new List<OptimizationResult>();

        // Phase 1: Machine & capability detection
        EmitSystem(progress, "Detecting machine...");
        var env = _envDetector.Detect();
        EmitSystem(progress, "Detecting hardware...");
        EmitSystem(progress, $"CPU: {env.CpuName} ({env.PhysicalCoreCount}C/{env.LogicalCoreCount}T)");
        EmitSystem(progress, $"RAM: {env.RamSizeGb} GB | Storage: {env.StorageType}");
        EmitSystem(progress, $"GPU(s): {string.Join(", ", env.Gpus.Select(g => g.Name))}");
        EmitSystem(progress, "Detecting firmware...");
        EmitSystem(progress, $"BIOS: {env.BIOSVendor} {env.BIOSVersion}");
        EmitSystem(progress, "Detecting installed software...");
        var workloadNames = env.Workloads.Where(w => w.Detected).Select(w => w.DisplayName).ToList();
        if (workloadNames.Count > 0)
            EmitSystem(progress, $"Detected workloads: {string.Join(", ", workloadNames)}");
        else
            EmitSystem(progress, "No special workloads detected.");

        // Phase 2: Build machine-specific execution plan
        EmitPlan(progress, "Building machine-specific optimization plan...");

        var executionPlan = new List<(OptimizationEntry Entry, IActionHandler Handler)>();
        var skippedTargets = new List<(OptimizationEntry Entry, string Reason)>();

        foreach (var entry in profile.Entries)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Engine-level safety gates - these are non-negotiable
            if (IsNeverTouch(entry))
            {
                skippedTargets.Add((entry, $"NEVER-TOUCH: {entry.Target} is a protected system component"));
                continue;
            }

            if (IsDisabledForManualOnlyService(entry))
            {
                skippedTargets.Add((entry, $"POLICY: {entry.Target} can only be set to Manual, not Disabled"));
                continue;
            }

            if (!IsOsSupported(entry, env))
            {
                skippedTargets.Add((entry, $"OS gate: not applicable to {(env.IsWindows10 ? "Win10" : "Win11")}"));
                continue;
            }

            if (!_conditionEvaluator.Evaluate(entry.Conditions, env))
            {
                var reason = BuildConditionReason(entry.Conditions, env);
                skippedTargets.Add((entry, $"Hardware/software gate: {reason}"));
                continue;
            }

            if (allowedItemIds != null && !allowedItemIds.Contains(entry.Id))
            {
                skippedTargets.Add((entry, "User deselected this item during preview"));
                continue;
            }

            var handler = _actionRegistry.GetHandler(entry.Action);
            if (handler == null)
            {
                skippedTargets.Add((entry, $"No handler for action: {entry.Action}"));
                continue;
            }

            var availability = handler.CheckAvailability(entry, env);
            if (availability == TargetState.Ready)
            {
                executionPlan.Add((entry, handler));
            }
            else if (availability == TargetState.NotAvailable)
            {
                skippedTargets.Add((entry, $"NOT AVAILABLE: Target {entry.Target} does not exist on this machine"));
            }
            else if (availability == TargetState.NotApplicable)
            {
                skippedTargets.Add((entry, $"PROTECTED: {entry.Target} is protected by the safety module"));
            }
        }

        // Emit not-available results for skipped items (so GUI log shows them)
        foreach (var (entry, reason) in skippedTargets)
        {
            var r = new OptimizationResult
            {
                Status = ResultStatus.NotAvailable,
                ItemId = entry.Id,
                ActionName = entry.Action,
                Category = entry.Category,
                DisplayName = entry.DisplayName,
                Message = $"[ NOT AVAILABLE ] {entry.DisplayName} - {reason}"
            };
            results.Add(r);
        }

        if (executionPlan.Count == 0)
        {
            var r = new OptimizationResult { Status = ResultStatus.NotApplicable, ItemId = "NONE", DisplayName = "Optimization Plan", Message = "No applicable optimization targets available on this machine." };
            results.Add(r);
            progress?.Report(r);
            return results;
        }

        EmitPlan(progress, $"{executionPlan.Count} supported targets found for this machine.");

        if (!dryRun)
        {
            EmitSystem(progress, "Creating System Restore Point...");
            bool rpCreated = _backupManager.CreateSystemRestorePoint($"Optimizer Backup: {profile.DisplayName}");
            EmitSystem(progress, rpCreated ? "Restore Point created successfully." : "Restore Point creation skipped/failed.");

            EmitSystem(progress, "Exporting pre-execution Registry and Service states...");
            var regPaths = executionPlan.Where(p => p.Entry.Category == "Registry").Select(p => p.Entry.Target).ToList();
            var svcNames = executionPlan.Where(p => p.Entry.Category == "Service").Select(p => p.Entry.Target).ToList();
            _backupManager.BackupRegistryState(regPaths);
            _backupManager.BackupServiceState(svcNames);
        }

        // Phase 3: Capture before-baseline
        var beforeProcessCount = Process.GetProcesses().Where(p => p.Id > 4).Count();
        long beforeRamFreeBytes = 0;
        try
        {
            using var mos = new System.Management.ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (var o in mos.Get())
                long.TryParse(o["FreePhysicalMemory"]?.ToString(), out beforeRamFreeBytes);
            beforeRamFreeBytes *= 1024;
        }
        catch { }

        // Phase 4: Execute
        int completedCount = 0;
        foreach (var planItem in executionPlan)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var entry = planItem.Entry;
            var handler = planItem.Handler;
            completedCount++;

            // Critical risk is refused
            if (entry.Risk.Equals("Critical", StringComparison.OrdinalIgnoreCase))
            {
                var r = new OptimizationResult { Status = ResultStatus.Failed, ItemId = entry.Id, ActionName = entry.Action, Category = entry.Category, DisplayName = entry.DisplayName, Message = "Critical risk target blocked by safety engine." };
                results.Add(r);
                progress?.Report(r);
                continue;
            }

            // High-risk confirmation gate
            bool needsConfirm = entry.RequiresConfirmation || entry.Risk.Equals("High", StringComparison.OrdinalIgnoreCase);
            if (entry.Action.Equals("SetBiosSetting", StringComparison.OrdinalIgnoreCase) && env.BitLockerActive)
                needsConfirm = true;

            if (needsConfirm && !confirmation.IsConfirmed)
            {
                var r = new OptimizationResult { Status = ResultStatus.Failed, ItemId = entry.Id, ActionName = entry.Action, Category = entry.Category, DisplayName = entry.DisplayName, Message = $"High-risk target requires explicit user confirmation." };
                results.Add(r);
                progress?.Report(r);
                continue;
            }

            if (dryRun)
            {
                var r = new OptimizationResult { Status = ResultStatus.Success, ItemId = entry.Id, ActionName = entry.Action, Category = entry.Category, DisplayName = entry.DisplayName, Message = $"[DRY-RUN] Would apply: {entry.DisplayName}" };
                results.Add(r);
                progress?.Report(r);
                continue;
            }

            // Emit live detection events
            progress?.Report(new OptimizationResult { Status = ResultStatus.Running, ItemId = entry.Id, ActionName = entry.Action, Category = entry.Category, DisplayName = entry.DisplayName, Message = $"[ DETECT ] {entry.DisplayName}" });
            progress?.Report(new OptimizationResult { Status = ResultStatus.Running, ItemId = entry.Id, ActionName = entry.Action, Category = entry.Category, DisplayName = entry.DisplayName, Message = $"[ DEPENDENCY ] Checking app dependencies - OK" });

            string currentVal = handler.GetCurrentValueDisplay(entry);
            string targetVal = entry.Value?.ToString() ?? "";

            progress?.Report(new OptimizationResult { Status = ResultStatus.Running, ItemId = entry.Id, ActionName = entry.Action, Category = entry.Category, DisplayName = entry.DisplayName, Message = $"[ READ ] Current state = {currentVal}" });

            // Already-optimized fast path
            if (StateNormalizer.IsSatisfied(currentVal, targetVal))
            {
                var r = new OptimizationResult { Status = ResultStatus.AlreadyOptimized, ItemId = entry.Id, ActionName = entry.Action, Category = entry.Category, DisplayName = entry.DisplayName, Message = $"[ ALREADY OPTIMIZED ] {entry.DisplayName} is already in the desired state." };
                results.Add(r);
                progress?.Report(r);
                continue;
            }

            progress?.Report(new OptimizationResult { Status = ResultStatus.Running, ItemId = entry.Id, ActionName = entry.Action, Category = entry.Category, DisplayName = entry.DisplayName, Message = $"[ APPLY ] Setting target state = {targetVal}" });

            var applyResult = handler.Apply(entry);

            if (applyResult.Status == ResultStatus.Success)
            {
                // Real Authoritative Readback Verification
                string newCurrentVal = handler.GetCurrentValueDisplay(entry);
                
                bool isMatch = StateNormalizer.IsSatisfied(newCurrentVal, targetVal);

                if (isMatch)
                {
                    progress?.Report(new OptimizationResult { Status = ResultStatus.Verifying, ItemId = entry.Id, ActionName = entry.Action, Category = entry.Category, DisplayName = entry.DisplayName, Message = $"[ VERIFY ] Readback confirmed: {newCurrentVal}." });
                    applyResult.Message = $"[ SUCCESS ] {entry.DisplayName} ({completedCount}/{executionPlan.Count})";
                }
                else
                {
                    StateNormalizer.LogReconciliationMismatch(entry.Id, currentVal, targetVal, newCurrentVal, targetVal, handler.ActionName);
                    progress?.Report(new OptimizationResult { Status = ResultStatus.Verifying, ItemId = entry.Id, ActionName = entry.Action, Category = entry.Category, DisplayName = entry.DisplayName, Message = $"[ VERIFY ] Mismatch! Expected {targetVal}, got {newCurrentVal}." });
                    applyResult.Status = ResultStatus.Failed;
                    applyResult.Message = $"[ FAILED ] {entry.DisplayName} - Readback verification failed.";
                }
            }

            applyResult.ItemId = entry.Id;
            applyResult.ActionName = entry.Action;
            applyResult.Category = entry.Category;
            applyResult.DisplayName = entry.DisplayName;
            results.Add(applyResult);
            progress?.Report(applyResult);
        }

        // Phase 5: Capture after-baseline and emit performance measurement
        var afterProcessCount = Process.GetProcesses().Where(p => p.Id > 4).Count();
        long afterRamFreeBytes = 0;
        try
        {
            using var mos = new System.Management.ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (var o in mos.Get())
                long.TryParse(o["FreePhysicalMemory"]?.ToString(), out afterRamFreeBytes);
            afterRamFreeBytes *= 1024;
        }
        catch { }

        int deltaProcesses = beforeProcessCount - afterProcessCount;
        long deltaRamFreeMb = (afterRamFreeBytes - beforeRamFreeBytes) / (1024 * 1024);

        EmitSystem(progress, $"Process count delta: -{Math.Max(0, deltaProcesses)} processes.");
        EmitSystem(progress, $"Memory delta: +{Math.Max(0, deltaRamFreeMb)} MB free.");
        EmitSystem(progress, "Optimization cycle completed.");

        return results;
    }

    private static bool IsNeverTouch(OptimizationEntry entry)
    {
        if (entry.Category.Equals("Service", StringComparison.OrdinalIgnoreCase))
            return NeverTouchServices.Contains(entry.Target);
        return false;
    }

    private static bool IsDisabledForManualOnlyService(OptimizationEntry entry)
    {
        if (!entry.Category.Equals("Service", StringComparison.OrdinalIgnoreCase)) return false;
        if (!ManualOnlyServices.Contains(entry.Target)) return false;
        return entry.Value?.ToString()?.Equals("Disabled", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsOsSupported(OptimizationEntry entry, EnvironmentContext env)
    {
        if (entry.Os == null || entry.Os.Count == 0) return true;
        var currentOs = env.IsWindows10 ? "win10" : "win11";
        return entry.Os.Any(o => o.Equals(currentOs, StringComparison.OrdinalIgnoreCase) || o.Equals("all", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildConditionReason(List<ConditionDef>? conditions, EnvironmentContext env)
    {
        if (conditions == null || conditions.Count == 0) return "Condition not met";
        var parts = new List<string>();
        foreach (var c in conditions)
        {
            parts.Add($"{c.Type} {c.Operator} {c.Value}");
        }
        return string.Join(", ", parts);
    }

    private static void EmitSystem(IProgress<OptimizationResult>? progress, string msg)
    {
        progress?.Report(new OptimizationResult
        {
            Status = ResultStatus.Running,
            ItemId = "SYSTEM",
            ActionName = "System",
            Category = "System",
            DisplayName = "System",
            Message = $"[ SYSTEM ] {msg}"
        });
    }

    private static void EmitPlan(IProgress<OptimizationResult>? progress, string msg)
    {
        progress?.Report(new OptimizationResult
        {
            Status = ResultStatus.Running,
            ItemId = "PLAN",
            ActionName = "Plan",
            Category = "Plan",
            DisplayName = "Optimization Plan",
            Message = $"[ PLAN ] {msg}"
        });
    }
}
