using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Interfaces;

public enum WorkloadSessionState
{
    Detected,
    Prepared,
    Running,
    Ended,
    Restoring,
    Restored,
    Failed
}

public interface IWorkloadDetector
{
    string WorkloadType { get; }
    InstalledWorkload? DetectInstallation();
    bool IsRunning();
    string GetRunningProcessPath();
}

public interface IWorkloadSessionManager
{
    WorkloadSessionState CurrentState { get; }
    InstalledWorkload? ActiveWorkload { get; }

    bool StartSession(InstalledWorkload workload);
    bool StopSession();
    void MonitorWorkload();
}
