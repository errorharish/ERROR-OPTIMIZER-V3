using System.Diagnostics;
using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Detection;

public class ProcessSnapshot : IProcessSnapshot
{
    public ProcessSnapshotInfo TakeSnapshot()
    {
        var processes = Process.GetProcesses();
        return new ProcessSnapshotInfo
        {
            Timestamp = DateTime.UtcNow,
            ProcessCount = processes.Length,
            ProcessNames = processes.Select(p => p.ProcessName).ToList()
        };
    }
}
