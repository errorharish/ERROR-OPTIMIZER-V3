using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations.Cleaners;

public class DeepSystemCleanerEngine : ICleanerEngine
{
    public string EngineId => "deep-cleanup";
    public string DisplayName => "Deep System Cleanup";

    private readonly List<string> _paths;

    public DeepSystemCleanerEngine()
    {
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(winDir))
        {
            winDir = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        }
        _paths = new List<string>
        {
            Path.Combine(winDir, "SoftwareDistribution", "Download"), // Update cache
            Path.Combine(winDir, "Prefetch")
        };
    }

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
        foreach (var dir in _paths)
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
