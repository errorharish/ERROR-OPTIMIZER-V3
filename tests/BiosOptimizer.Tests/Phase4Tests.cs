using BiosOptimizer.Core.ActionHandlers;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;
using Moq;

namespace BiosOptimizer.Tests;

public class Phase4Tests
{
    [Fact]
    public void CpuAffinityHandler_LowCoreCount_IsBlocked()
    {
        var handler = new CpuAffinityHandler();
        var entry = new OptimizationEntry
        {
            Action = "SetCpuAffinity",
            Target = "TestProcess",
            Value = "1"
        };
        var context = new EnvironmentContext();
        context.PhysicalCoreCount = 2; // Low core count

        var canApply = handler.CanApply(entry, context);

        Assert.False(canApply, "CPU Affinity should be blocked on low-core machines.");
    }

    [Fact]
    public void ProcessPriorityHandler_Realtime_IsBlocked()
    {
        var handler = new ProcessPriorityHandler();
        var entry = new OptimizationEntry
        {
            Action = "SetProcessPriority",
            Target = "TestProcess",
            Value = "RealTime"
        };
        var context = new EnvironmentContext();

        var canApply = handler.CanApply(entry, context);

        Assert.False(canApply, "RealTime priority must always be blocked.");
    }

    [Fact]
    public void WorkloadSessionManager_StartSession_ChangesState()
    {
        var manager = new BiosOptimizer.Core.Implementations.WorkloadSessionManager(new List<IWorkloadDetector>(), null!);
        
        var workload = new InstalledWorkload { WorkloadType = "Test" };
        var success = manager.StartSession(workload);

        Assert.True(success);
        Assert.Equal(WorkloadSessionState.Running, manager.CurrentState);
        Assert.Equal(workload, manager.ActiveWorkload);
    }
}
