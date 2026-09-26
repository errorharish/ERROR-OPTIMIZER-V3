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
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AMD", "DxCache")
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
        foreach (var dir in _cachePaths)
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
