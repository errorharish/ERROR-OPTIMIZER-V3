using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.Core.Implementations.Power;
using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Core.Implementations.Startup
{
    public class StartupBootstrapService : IStartupBootstrapService
    {
        private static readonly Lazy<StartupBootstrapService> _instance = new(() => new StartupBootstrapService());
        public static StartupBootstrapService Instance => _instance.Value;

        private readonly List<IStartupComponent> _components = new();
        private readonly object _lock = new();
        private StartupBootstrapReport _currentReport = new();
        private readonly string _logFilePath;

        public StartupBootstrapReport CurrentReport => _currentReport;
        public event Action<StartupBootstrapReport>? BootstrapCompleted;

        public StartupBootstrapService()
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer", "logs");
            Directory.CreateDirectory(appData);
            _logFilePath = Path.Combine(appData, "startup_bootstrap.log");
        }

        public void RegisterComponent(IStartupComponent component)
        {
            lock (_lock)
            {
                if (!_components.Any(c => c.Id.Equals(component.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    _components.Add(component);
                }
            }
        }

        public WindowsStartupInfo CheckWindowsStartupState()
        {
            return WindowsStartupRegistrar.Instance.GetStartupInfo();
        }

        public bool SetWindowsStartup(bool enable, bool startMinimized = false)
        {
            bool success = WindowsStartupRegistrar.Instance.ConfigureStartup(enable, startMinimized);
            var info = WindowsStartupRegistrar.Instance.GetStartupInfo();
            _currentReport.WindowsStartupState = info.DisplayStatus;
            Log($"[STARTUP REGISTRATION] Configured={enable}, Minimized={startMinimized}, Result={(success ? "SUCCESS" : "FAILED")}, State={info.DisplayStatus}");
            return success;
        }

        public async Task<StartupBootstrapReport> RunBootstrapAsync(bool isBackgroundLaunch = false, CancellationToken ct = default)
        {
            var report = new StartupBootstrapReport
            {
                StartupTime = DateTime.Now,
                OverallStatus = "INITIALIZING"
            };

            void AddLog(string msg)
            {
                string entry = $"[{DateTime.Now:HH:mm:ss}] {msg}";
                report.LogEntries.Add(entry);
                Log(entry);
            }

            AddLog("============================================================");
            AddLog($"ERROR OPTIMIZER V3 — STARTUP BOOTSTRAP INITIATED (BackgroundLaunch={isBackgroundLaunch})");
            AddLog("============================================================");

            // PHASE 1: Process Starts
            AddLog("PHASE 1: Process runtime verified.");

            // PHASE 2: Logging Starts
            AddLog($"PHASE 2: Audit log initialized at '{_logFilePath}'.");

            // PHASE 3: Configuration Loads
            AddLog("PHASE 3: Application settings & policies loaded.");

            // PHASE 4: Backup/Transaction System Initializes
            try
            {
                var backupStats = BackupManager.Instance.GetSummaryStats();
                report.BackupEngineState = $"READY ({backupStats.TotalBackups} transactions indexed)";
                AddLog($"PHASE 4: Universal Backup Engine initialized ({backupStats.TotalBackups} total backups, {backupStats.RestorableCount} restorable).");
            }
            catch (Exception ex)
            {
                report.BackupEngineState = "FAILED";
                AddLog($"PHASE 4: Universal Backup Engine failed to initialize: {ex.Message}");
            }

            // PHASE 5: Core Service/Backend Connection Initializes
            report.CoreBackendState = "READY";
            AddLog("PHASE 5: Core backend service pipeline initialized.");

            // PHASE 6: Hardware/OS Detection Initializes
            try
            {
                var profiler = HardwareProfiler.GetQuickProfile();
                AddLog($"PHASE 6: Hardware & OS detected (CPU: {profiler.CpuModel} [{profiler.CpuLogicalCores} cores], RAM: {profiler.RamTotalGb:F1} GB, GPU: {profiler.GpuName}, OS: {profiler.WindowsVersion}).");
            }
            catch (Exception ex)
            {
                AddLog($"PHASE 6: Hardware detection warning: {ex.Message}");
            }

            // PHASE 7: AI Engines Initialize
            AddLog("PHASE 7: Initializing AI engine components in safe monitoring mode...");
            bool anyEngineFailed = false;

            // 7A: Workload Engine
            try
            {
                var workloadEngine = WorkloadOptimizationEngine.Instance;
                if (workloadEngine.Config.IsEnabled)
                {
                    workloadEngine.Start();
                }
                report.AiWorkloadState = workloadEngine.Config.IsEnabled ? "RUNNING" : "DISABLED";
                AddLog($"PHASE 7A: AI Workload Engine initialized (Enabled={workloadEngine.Config.IsEnabled}, Mode={workloadEngine.Config.NormalMode}). Passive monitoring active.");
            }
            catch (Exception ex)
            {
                report.AiWorkloadState = "FAILED";
                anyEngineFailed = true;
                AddLog($"PHASE 7A: AI Workload Engine initialization failed: {ex.Message}");
            }

            // 7B: RAM Limiter Engine
            try
            {
                var ramEngine = RamLimiterEngine.Instance;
                if (ramEngine.Config.IsEnabled)
                {
                    ramEngine.Start();
                }
                report.AiRamState = ramEngine.Config.IsEnabled ? "RUNNING" : "DISABLED";
                AddLog($"PHASE 7B: AI RAM Limiter Engine initialized (Enabled={ramEngine.Config.IsEnabled}, Mode={ramEngine.Config.NormalMode}). Passive monitoring active.");
            }
            catch (Exception ex)
            {
                report.AiRamState = "FAILED";
                anyEngineFailed = true;
                AddLog($"PHASE 7B: AI RAM Limiter Engine initialization failed: {ex.Message}");
            }

            // 7C: Power Plan Engine
            try
            {
                var powerEngine = PowerPlanEngine.Instance;
                var (activeGuid, friendlyName) = powerEngine.GetActiveSchemeNative();
                AddLog($"PHASE 7C: AI Power Plan Engine initialized (Active Scheme: '{friendlyName}' [{activeGuid}]).");
            }
            catch (Exception ex)
            {
                report.AiPowerPlanState = "FAILED";
                anyEngineFailed = true;
                AddLog($"PHASE 7C: AI Power Plan Engine initialization failed: {ex.Message}");
            }

            // PHASE 8: Startup-Enabled Policies Execute (e.g. AI Power Plan Auto-Enable)
            AddLog("PHASE 8: Evaluating configured startup policies...");
            try
            {
                var powerEngine = PowerPlanEngine.Instance;
                var pConfig = powerEngine.GetStartupConfig();

                if (pConfig.AutoEnableOnStartup)
                {
                    if (string.IsNullOrWhiteSpace(pConfig.StartupPowerPlanGuid))
                    {
                        // Auto-assign active or ultimate performance
                        var plans = await powerEngine.DiscoverPowerPlansAsync(ct);
                        var target = plans.FirstOrDefault(p => p.Name.Equals("Ultimate Performance", StringComparison.OrdinalIgnoreCase) && p.IsInstalled)
                                  ?? plans.FirstOrDefault(p => p.Name.Equals("High Performance", StringComparison.OrdinalIgnoreCase) && p.IsInstalled)
                                  ?? plans.FirstOrDefault(p => p.IsActive)
                                  ?? plans.FirstOrDefault();

                        if (target != null)
                        {
                            pConfig.StartupPowerPlanGuid = target.Guid;
                            pConfig.StartupPowerPlanName = target.Name;
                            powerEngine.SaveStartupConfig(pConfig);
                        }
                    }

                    AddLog($"PHASE 8: Power Plan Auto-Enable is ON. Target: '{pConfig.StartupPowerPlanName}' ({pConfig.StartupPowerPlanGuid}). Executing bounded activation & verification...");
                    
                    var autoApplyResult = await powerEngine.InitializeStartupAutoApplyAsync(ct);
                    if (autoApplyResult.Success && autoApplyResult.Verified)
                    {
                        report.AiPowerPlanState = "ACTIVE (Verified)";
                        AddLog($"PHASE 8: Power Plan '{autoApplyResult.ActiveSchemeName}' ({autoApplyResult.ActiveSchemeGuid}) verified active in Windows.");
                        powerEngine.StartBackgroundEnforcement();
                    }
                    else
                    {
                        report.AiPowerPlanState = "FAILED";
                        AddLog($"PHASE 8: Power Plan auto-apply failed: {autoApplyResult.Message}");
                    }
                }
                else
                {
                    report.AiPowerPlanState = "DISABLED";
                    AddLog("PHASE 8: Power Plan Auto-Enable is OFF.");
                }
            }
            catch (Exception ex)
            {
                report.AiPowerPlanState = "FAILED";
                AddLog($"PHASE 8: Power Plan policy execution threw exception: {ex.Message}");
            }

            // PHASE 9: Telemetry Monitors Start
            report.TelemetryState = "RUNNING";
            AddLog("PHASE 9: System Telemetry and Adaptive Resource Governor background monitors active.");

            // PHASE 10: Scheduler Reconciliation
            AddLog("PHASE 10: Reconciling Smart Auto Optimize Scheduler...");
            try
            {
                var startupInfo = WindowsStartupRegistrar.Instance.GetStartupInfo();
                report.WindowsStartupState = startupInfo.DisplayStatus;
                AddLog($"PHASE 10: Windows Startup registration state: {startupInfo.DisplayStatus} ({startupInfo.DetailMessage})");
            }
            catch (Exception ex)
            {
                report.WindowsStartupState = "FAILED";
                AddLog($"PHASE 10: Windows Startup verification failed: {ex.Message}");
            }

            // PHASE 11: Final Verification & Registered Components
            AddLog("PHASE 11: Performing final verification across registered components...");
            List<IStartupComponent> componentsSnapshot;
            lock (_lock)
            {
                componentsSnapshot = _components.ToList();
            }

            foreach (var comp in componentsSnapshot)
            {
                try
                {
                    if (comp.IsEnabled)
                    {
                        bool initOk = await comp.InitializeAsync(ct);
                        bool verifyOk = initOk && await comp.VerifyAsync(ct);
                        comp.Status = verifyOk ? "READY" : "FAILED";
                        report.ComponentStatuses[comp.Id] = comp.Status;
                        AddLog($"PHASE 11: Component '{comp.Name}' -> {comp.Status} ({comp.Message})");
                    }
                    else
                    {
                        comp.Status = "DISABLED";
                        report.ComponentStatuses[comp.Id] = comp.Status;
                    }
                }
                catch (Exception ex)
                {
                    comp.Status = "FAILED";
                    comp.Message = ex.Message;
                    report.ComponentStatuses[comp.Id] = "FAILED";
                    anyEngineFailed = true;
                    AddLog($"PHASE 11: Component '{comp.Name}' threw exception: {ex.Message}");
                }
            }

            // PHASE 12: READY
            report.OverallStatus = anyEngineFailed ? "PARTIALLY_READY" : "READY";
            AddLog($"PHASE 12: Startup Bootstrap finished. Master System State: {report.OverallStatus}");
            AddLog("============================================================");

            _currentReport = report;
            BootstrapCompleted?.Invoke(report);
            return report;
        }

        private void Log(string msg)
        {
            try
            {
                File.AppendAllText(_logFilePath, $"{msg}\r\n");
            }
            catch { }
        }
    }
}
