using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations.Cleaners;

public class LargeFileScannerEngine : ICleanerEngine
{
    public string EngineId => "large-files";
    public string DisplayName => "Large File Scanner";

    public CleanupScanDto Scan()
    {
        // For demonstration, scan a restricted path (like Downloads) looking for files > 1GB
        // Do NOT auto-delete anything here. This returns a "scanned" summary.
        var scan = new CleanupScanDto
        {
            Category = "Storage",
            IsSafeToClean = false, // Explicit user selection required for Large Files
            ScannedPaths = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }
        };

        // Real implementation would recurse safely
        return scan;
    }

    public long Clean()
    {
        // Never automatically delete large files.
        return 0;
    }
}
