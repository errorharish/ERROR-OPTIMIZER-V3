using System.Collections.Generic;

namespace BiosOptimizer.Core.Interfaces;

public interface IBackupManager
{
    bool CreateSystemRestorePoint(string description);
    bool BackupRegistryState(IEnumerable<string> registryPaths);
    bool BackupServiceState(IEnumerable<string> serviceNames);
    bool LogKilledProcesses(IEnumerable<string> processPaths);
    bool RestoreAll();

    bool BackupRegistryValue(string owner, string registryKey, string valueName, object oldValue, string oldType, object targetValue, string result);
    bool RestoreByOwner(string owner);
}
