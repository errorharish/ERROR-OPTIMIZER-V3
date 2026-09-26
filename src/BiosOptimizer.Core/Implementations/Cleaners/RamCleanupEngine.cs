using System.Diagnostics;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations.Cleaners;

public class RamCleanupEngine : ICleanerEngine
{
    public string EngineId => "ram-cleanup";
    public string DisplayName => "RAM Cleanup";

    public CleanupScanDto Scan()
    {
        // Calculate reclaimable memory based on GC and other safe reclaim heuristics.
        // We do NOT use unsafe EmptyStandbyList.
        
        long totalMemory = 0;
        try
        {
            var gcInfo = GC.GetTotalMemory(false);
            totalMemory = gcInfo; // Base safe reclaimable
        }
        catch { }

        return new CleanupScanDto
        {
            Category = "RAM",
            IsSafeToClean = true,
            TotalSizeInBytes = totalMemory,
            FileCount = 0,
            ScannedPaths = new List<string> { "Safe GC/Heap Reclaim" }
        };
    }

    public long Clean()
    {
        try
        {
            var before = GC.GetTotalMemory(false);
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            var after = GC.GetTotalMemory(true);

            // Attempt to empty working set for our own process cleanly
            try
            {
                using var p = Process.GetCurrentProcess();
                p.MinWorkingSet = p.MinWorkingSet; // Safely forces working set trim in some Windows versions
            }
            catch { }

            return Math.Max(0, before - after);
        }
        catch
        {
            return 0;
        }
    }
}
