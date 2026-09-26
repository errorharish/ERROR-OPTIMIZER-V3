using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Core.Interfaces;

public interface ICleanerEngine
{
    string EngineId { get; }
    string DisplayName { get; }
    
    /// <summary>
    /// Scans the system and returns what can be cleaned.
    /// </summary>
    CleanupScanDto Scan();

    /// <summary>
    /// Performs the cleanup operation based on the last scan.
    /// Returns the number of bytes successfully cleaned, or 0 if failed.
    /// </summary>
    long Clean();
}
