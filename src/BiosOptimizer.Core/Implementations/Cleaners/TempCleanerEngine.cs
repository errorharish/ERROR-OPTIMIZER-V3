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
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")
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
                    foreach (var f in SafeEnumerateFiles(dir))
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
                    foreach (var f in SafeEnumerateFiles(dir))
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

    private static IEnumerable<string> SafeEnumerateFiles(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) yield break;

        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            string current = stack.Pop();

            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var sd in subDirs)
            {
                stack.Push(sd);
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var f in files)
            {
                yield return f;
            }
        }
    }
}
