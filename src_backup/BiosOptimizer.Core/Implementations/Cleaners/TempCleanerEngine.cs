using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations.Cleaners;

public class TempCleanerEngine : ICleanerEngine
{
    public string EngineId => "temp-cleanup";
    public string DisplayName => "Fast Temp Clean";

    private readonly List<string> _tempPaths = new()
    {
        Path.GetTempPath(),
        @"C:\Windows\Temp"
    };

    public CleanupScanDto Scan()
    {
        var scan = new CleanupScanDto
        {
            Category = "Temp",
            IsSafeToClean = true
        };

        foreach (var dir in _tempPaths)
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
        foreach (var dir in _tempPaths)
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
