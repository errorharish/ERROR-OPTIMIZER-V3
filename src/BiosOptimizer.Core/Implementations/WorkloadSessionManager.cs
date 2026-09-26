using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations;

public class WorkloadSessionManager : IWorkloadSessionManager
{
    private readonly IEnumerable<IWorkloadDetector> _detectors;
    private readonly ITierEngine _engine;
    
    public WorkloadSessionState CurrentState { get; private set; } = WorkloadSessionState.Ended;
    public InstalledWorkload? ActiveWorkload { get; private set; }

    public WorkloadSessionManager(IEnumerable<IWorkloadDetector> detectors, ITierEngine engine)
    {
        _detectors = detectors;
        _engine = engine;
    }

    public bool StartSession(InstalledWorkload workload)
    {
        if (CurrentState == WorkloadSessionState.Running)
        {
            return false;
        }

        ActiveWorkload = workload;
        CurrentState = WorkloadSessionState.Running;
        
        // The TierEngine handles applying the temporary optimizations via a Profile if passed,
        // so the session manager mainly tracks that we are IN a session, 
        // which the CLI can use to block other changes or to monitor exit.
        
        return true;
    }

    public bool StopSession()
    {
        if (CurrentState != WorkloadSessionState.Running)
        {
            return false;
        }

        CurrentState = WorkloadSessionState.Restoring;
        // In a real implementation, we would instruct TierEngine to Restore the "Session" transaction
        // _engine.RestoreAsync("WorkloadSession_...");

        ActiveWorkload = null;
        CurrentState = WorkloadSessionState.Restored;
        CurrentState = WorkloadSessionState.Ended;
        
        return true;
    }

    public void MonitorWorkload()
    {
        if (CurrentState != WorkloadSessionState.Running || ActiveWorkload == null)
            return;

        var detector = _detectors.FirstOrDefault(d => d.WorkloadType == ActiveWorkload.WorkloadType);
        if (detector != null && !detector.IsRunning())
        {
            StopSession();
        }
    }
}
