using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations.Cleaners;

public class DeepSystemCleanerEngine : ICleanerEngine
{
    public string EngineId => "deep-cleanup";
    public string DisplayName => "Deep System Cleanup";

    private readonly List<string> _paths = new()
    {
        @"C:\Windows\SoftwareDistribution\Download", // Update cache
        @"C:\Windows\Prefetch"
    };

    public CleanupScanDto Scan()
    {
        var scan = new CleanupScanDto
        {
            Category = "System",
            IsSafeToClean = true // Only targeting known safe temp caches
        };

        foreach (var dir in _paths)
        {
            if (Directory.Exists(dir))
            {
                try
                {
                    var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                    foreach (var f in files)
                    {
                        try
                        {
                            var info = new FileInfo(f);
                            scan.TotalSizeInBytes += info.Length;
                            scan.FileCount++;
                        }
                        catch { }
                    }
                    scan.ScannedPaths.Add(dir);
                }
                catch { }
            }
        }
        
        return scan;
    }

    public long Clean()
    {
        long cleanedBytes = 0;
        foreach (var dir in _paths)
        {
            if (Directory.Exists(dir))
            {
                try
                {
                    var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                    foreach (var f in files)
                    {
                        try
                        {
                            var info = new FileInfo(f);
                            var len = info.Length;
                            File.Delete(f);
                            cleanedBytes += len;
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }
        return cleanedBytes;
    }
}
