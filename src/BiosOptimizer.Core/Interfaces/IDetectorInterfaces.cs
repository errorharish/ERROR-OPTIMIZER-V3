using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Interfaces;

public interface IEnvironmentDetector
{
    EnvironmentContext Detect();
}

public interface IProcessSnapshot
{
    ProcessSnapshotInfo TakeSnapshot();
}

public class ProcessSnapshotInfo
{
    public DateTime Timestamp { get; set; }
    public int ProcessCount { get; set; }
    public List<string> ProcessNames { get; set; } = new();
}

public interface IMachineProfileDetector
{
    MachineProfile DetectMachine();
}

public interface IMachineCapabilityProvider
{
    bool IsSupported(OptimizationEntry entry, MachineProfile profile);
}
