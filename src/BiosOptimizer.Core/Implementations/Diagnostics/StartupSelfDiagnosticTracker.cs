#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using BiosOptimizer.Core.Implementations.Startup;

namespace BiosOptimizer.Core.Implementations.Diagnostics
{
    public class StartupSelfDiagnosticReport
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string UserName { get; set; } = Environment.UserName;
        public string InteractiveUserSid { get; set; } = "Unknown";
        public int ProcessId { get; set; } = Environment.ProcessId;
        public int SessionId { get; set; } = Process.GetCurrentProcess().SessionId;
        public bool StartupLaunchDetected { get; set; } = false;
        public string StartupSource { get; set; } = "HKCU\\Run";
        public string StartupArgument { get; set; } = "None";
        public string InstalledExePath { get; set; } = string.Empty;
        public bool StartupRegistrationValid { get; set; } = false;
        public string StartupRegistrationStatus { get; set; } = "NOT_CHECKED";
        public bool SingleInstancePrimary { get; set; } = false;
        public bool TrayInitialized { get; set; } = false;
        public bool BackendConnected { get; set; } = false;
        public bool AIWorkloadInitialized { get; set; } = false;
        public bool AIRamInitialized { get; set; } = false;
        public bool AIPowerPlanInitialized { get; set; } = false;
        public bool IsWindowsStartupBlocked { get; set; } = false;
        public string WindowsStartupBlockedReason { get; set; } = string.Empty;
        public string FailureStage { get; set; } = "None";
        public int ExitCode { get; set; } = 0;
        public string ExceptionDetails { get; set; } = "None";
        public string DiagnosticSummary { get; set; } = "INITIALIZING";
    }

    public sealed class StartupSelfDiagnosticTracker
    {
        private static readonly Lazy<StartupSelfDiagnosticTracker> _instance = new(() => new StartupSelfDiagnosticTracker());
        public static StartupSelfDiagnosticTracker Instance => _instance.Value;

        private readonly object _lock = new();
        private readonly string _logDir;
        private readonly string _diagnosticJsonPath;
        private readonly string _startupLogTextPath;
        private readonly StartupSelfDiagnosticReport _report = new();

        public StartupSelfDiagnosticReport CurrentReport => _report;

        private StartupSelfDiagnosticTracker()
        {
            _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer", "Logs");
            _diagnosticJsonPath = Path.Combine(_logDir, "startup_self_diagnostic.json");
            _startupLogTextPath = Path.Combine(_logDir, "Startup.log");

            try
            {
                if (!Directory.Exists(_logDir))
                {
                    Directory.CreateDirectory(_logDir);
                }

                if (File.Exists(_diagnosticJsonPath))
                {
                    try
                    {
                        string existingJson = File.ReadAllText(_diagnosticJsonPath);
                        var loaded = JsonSerializer.Deserialize<StartupSelfDiagnosticReport>(existingJson);
                        if (loaded != null)
                        {
                            _report = loaded;
                        }
                    }
                    catch { }
                }

                using var identity = WindowsIdentity.GetCurrent();
                _report.InteractiveUserSid = identity.User?.Value ?? "Unknown";
                _report.UserName = identity.Name ?? Environment.UserName;
                _report.ProcessId = Environment.ProcessId;
                try { _report.SessionId = Process.GetCurrentProcess().SessionId; } catch { }

                if (string.IsNullOrWhiteSpace(_report.InstalledExePath))
                {
                    _report.InstalledExePath = WindowsStartupRegistrar.ResolveInstalledOrLocalExePath();
                }

                LogEvent("BOOT", $"Process started. PID={_report.ProcessId}, Session={_report.SessionId}, User={_report.UserName} ({_report.InteractiveUserSid})");
            }
            catch { }
        }

        public void RecordStartupLaunch(string[] args, string exePath)
        {
            lock (_lock)
            {
                _report.Timestamp = DateTime.Now;
                _report.StartupLaunchDetected = true;
                _report.StartupArgument = args.Length > 0 ? string.Join(" ", args) : "None (Normal Launch)";
                _report.InstalledExePath = !string.IsNullOrWhiteSpace(exePath) ? exePath : WindowsStartupRegistrar.ResolveInstalledOrLocalExePath();
                LogEvent("STARTUP DETECTED", $"Process launched. PID={_report.ProcessId}, Session={_report.SessionId}, User={_report.UserName} ({_report.InteractiveUserSid})");
                LogEvent("STARTUP ARGUMENT VERIFIED", $"Arguments='{_report.StartupArgument}', InstalledPath='{_report.InstalledExePath}'");
                Persist();
            }
        }

        public void RecordUserSid(string sid)
        {
            lock (_lock)
            {
                _report.InteractiveUserSid = sid;
                Persist();
            }
        }

        public void RecordRegistrationState(bool isValid, string status, bool isBlocked = false, string blockedReason = "")
        {
            lock (_lock)
            {
                _report.StartupRegistrationValid = isValid;
                _report.StartupRegistrationStatus = status;
                _report.IsWindowsStartupBlocked = isBlocked;
                _report.WindowsStartupBlockedReason = isBlocked ? blockedReason : "";
                if (string.IsNullOrWhiteSpace(_report.InstalledExePath))
                {
                    _report.InstalledExePath = WindowsStartupRegistrar.ResolveInstalledOrLocalExePath();
                }
                LogEvent("REGISTRATION_STATE", $"Valid={isValid}, Status={status}, Blocked={isBlocked}, Reason={blockedReason}");
                Persist();
            }
        }

        public void RecordSingleInstance(bool isPrimary)
        {
            lock (_lock)
            {
                _report.SingleInstancePrimary = isPrimary;
                if (isPrimary)
                {
                    LogEvent("SINGLE INSTANCE PRIMARY", $"PID={_report.ProcessId} acquired primary instance mutex.");
                }
                else
                {
                    LogEvent("SINGLE INSTANCE SECONDARY", $"PID={_report.ProcessId} detected primary instance and will signal/exit.");
                }
                Persist();
            }
        }

        public void RecordMainWindowState(bool isHidden)
        {
            lock (_lock)
            {
                if (isHidden)
                {
                    LogEvent("MAIN WINDOW HIDDEN", "MainWindow hidden into System Tray (Silent Background Startup).");
                }
                else
                {
                    LogEvent("MAIN WINDOW SHOWN", "MainWindow activated and displayed in normal interactive session.");
                }
                Persist();
            }
        }

        public void RecordTray(bool initialized)
        {
            lock (_lock)
            {
                _report.TrayInitialized = initialized;
                if (initialized)
                {
                    LogEvent("TRAY INITIALIZED", "NotifyIcon initialized and visible in interactive taskbar.");
                }
                else
                {
                    LogEvent("TRAY_STATUS", "NotifyIcon initialization failed or not active.");
                }
                Persist();
            }
        }

        public void RecordBackend(bool connected)
        {
            lock (_lock)
            {
                _report.BackendConnected = connected;
                if (connected)
                {
                    LogEvent("BACKEND CONNECTED", "Elevated backend service connection established.");
                }
                else
                {
                    LogEvent("BACKEND_STATUS", "Backend service connection offline or pending.");
                }
                Persist();
            }
        }

        public void RecordAiEngines(bool workload, bool ram, bool powerPlan)
        {
            lock (_lock)
            {
                _report.AIWorkloadInitialized = workload;
                _report.AIRamInitialized = ram;
                _report.AIPowerPlanInitialized = powerPlan;
                _report.DiagnosticSummary = (workload && ram && powerPlan && _report.TrayInitialized) ? "READY_AND_HEALTHY" : "PARTIAL";
                if (workload) LogEvent("AI WORKLOAD READY", "Passive process workload monitoring engine active.");
                if (ram) LogEvent("AI RAM READY", "Background memory working-set optimizer engine active.");
                if (powerPlan) LogEvent("AI POWER PLAN READY", "Power scheme governor and policy enforcer active.");
                LogEvent("BACKGROUND START COMPLETE", $"All background subsystems initialized. Summary={_report.DiagnosticSummary}");
                Persist();
            }
        }

        public void RecordFailure(string stage, string reason, Exception? ex = null)
        {
            lock (_lock)
            {
                _report.FailureStage = stage;
                _report.DiagnosticSummary = $"FAILED at {stage}: {reason}";
                if (ex != null)
                {
                    _report.ExceptionDetails = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
                }
                LogEvent("FAILURE", $"Stage={stage}, Reason={reason}, Exception={_report.ExceptionDetails}");
                Persist();
            }
        }

        public void RecordExit(int exitCode)
        {
            lock (_lock)
            {
                _report.ExitCode = exitCode;
                LogEvent("PROCESS_EXIT", $"ExitCode={exitCode}");
                Persist();
            }
        }

        public void LogEvent(string stage, string message)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{stage}] {message}\r\n";
                File.AppendAllText(_startupLogTextPath, line);
            }
            catch { }
        }

        public void Persist()
        {
            try
            {
                string json = JsonSerializer.Serialize(_report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_diagnosticJsonPath, json);
            }
            catch { }
        }
    }
}
