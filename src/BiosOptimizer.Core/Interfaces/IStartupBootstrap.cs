using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Interfaces
{
    public enum WindowsStartupRegistrationStatus
    {
        VerifiedEnabled,
        DisabledByUser,
        ExternalChangeDetectedDisabled,
        NotConfigured,
        CorruptedOrMissingExe,
        AccessDenied,
        BlockedByPolicy
    }

    public class WindowsStartupInfo
    {
        public WindowsStartupRegistrationStatus Status { get; set; } = WindowsStartupRegistrationStatus.NotConfigured;
        public bool IsEnabled { get; set; } = false;
        public string ExePath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string SourceKey { get; set; } = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
        public string EntryName { get; set; } = "Error Optimizer";
        public string DisplayStatus => Status switch
        {
            WindowsStartupRegistrationStatus.VerifiedEnabled => "VERIFIED (Enabled)",
            WindowsStartupRegistrationStatus.DisabledByUser => "DISABLED (By User)",
            WindowsStartupRegistrationStatus.ExternalChangeDetectedDisabled => "EXTERNAL CHANGE DETECTED (Disabled in Task Manager)",
            WindowsStartupRegistrationStatus.CorruptedOrMissingExe => "CORRUPTED / INVALID PATH",
            WindowsStartupRegistrationStatus.AccessDenied => "ACCESS DENIED",
            WindowsStartupRegistrationStatus.BlockedByPolicy => "WINDOWS STARTUP BLOCKED",
            _ => "NOT CONFIGURED"
        };
        public string DetailMessage { get; set; } = string.Empty;
    }

    public interface IStartupComponent
    {
        string Id { get; }
        string Name { get; }
        bool IsEnabled { get; set; }
        string Status { get; set; }
        string Message { get; set; }
        Task<bool> InitializeAsync(CancellationToken ct = default);
        Task<bool> VerifyAsync(CancellationToken ct = default);
        Task ShutdownAsync(CancellationToken ct = default);
    }

    public class StartupBootstrapReport
    {
        public DateTime StartupTime { get; set; } = DateTime.Now;
        public string OverallStatus { get; set; } = "INITIALIZING"; // READY, PARTIALLY_READY, FAILED
        public string WindowsStartupState { get; set; } = "UNKNOWN";
        public string CoreBackendState { get; set; } = "UNKNOWN";
        public string AiPowerPlanState { get; set; } = "NOT_CONFIGURED";
        public string AiWorkloadState { get; set; } = "NOT_CONFIGURED";
        public string AiRamState { get; set; } = "NOT_CONFIGURED";
        public string SmartAutoOptimizeState { get; set; } = "NOT_CONFIGURED";
        public string TelemetryState { get; set; } = "UNKNOWN";
        public string BackupEngineState { get; set; } = "UNKNOWN";
        public List<string> LogEntries { get; set; } = new();
        public Dictionary<string, string> ComponentStatuses { get; set; } = new();

        public string FormattedStartupTime => StartupTime.ToString("hh:mm:ss tt");
    }

    public interface IStartupBootstrapService
    {
        Task<StartupBootstrapReport> RunBootstrapAsync(bool isBackgroundLaunch = false, CancellationToken ct = default);
        StartupBootstrapReport CurrentReport { get; }
        WindowsStartupInfo CheckWindowsStartupState();
        bool SetWindowsStartup(bool enable, bool startMinimized = false);
        void RegisterComponent(IStartupComponent component);
        event Action<StartupBootstrapReport>? BootstrapCompleted;
    }
}
