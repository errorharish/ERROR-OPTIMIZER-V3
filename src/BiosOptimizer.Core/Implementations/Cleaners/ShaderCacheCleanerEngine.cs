using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations.Cleaners;

public class ShaderCacheCleanerEngine : ICleanerEngine
{
    public string EngineId => "shader-cache";
    public string DisplayName => "Shader Cache Cleanup";

    private readonly List<string> _cachePaths = new()
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NVIDIA", "DXCache"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NVIDIA", "GLCache"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD", "DxCache"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Intel", "ShaderCache")
    };

    public CleanupScanDto Scan()
    {
        var scan = new CleanupScanDto
        {
            Category = "Shader Cache",
            IsSafeToClean = true
        };

        foreach (var dir in _cachePaths)
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
        foreach (var dir in _cachePaths)
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
