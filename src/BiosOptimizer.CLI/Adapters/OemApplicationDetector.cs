using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.CLI.Adapters;

public class OemApplicationDetector : IOemApplicationDetector
{
    public List<string> DetectOemApplications()
    {
        var oemApps = new List<string>();
        // Add minimal mock logic for detecting OEM software
        // In a real environment, this would query Uninstall keys or known paths
        return oemApps;
    }
}
