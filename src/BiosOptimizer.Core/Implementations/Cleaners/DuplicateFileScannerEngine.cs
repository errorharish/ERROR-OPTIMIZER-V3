using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Implementations.Cleaners;

public class DuplicateFileScannerEngine : ICleanerEngine
{
    public string EngineId => "duplicate-files";
    public string DisplayName => "Duplicate File Scanner";

    public CleanupScanDto Scan()
    {
        var scan = new CleanupScanDto
        {
            Category = "Storage",
            IsSafeToClean = false, // Explicit user selection required
            ScannedPaths = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) }
        };

        // Real implementation would hash and group sizes
        return scan;
    }

    public long Clean()
    {
        // Never automatically delete
        return 0;
    }
}
