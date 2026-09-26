using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Safety;

public class ProtectedTargetsPolicy : ISafetyPolicy
{
    private readonly HashSet<string> _protectedTargets;

    public ProtectedTargetsPolicy()
    {
        var targets = new[]
        {
            "WinDefend", "SecurityHealthService", "mpssvc", "AudioSrv", "netprofm",
            "Dhcp", "Dnscache", "EventLog", "EventSystem", "Power", "PlugPlay",
            "RpcSs", "RpcEptMapper", "DcomLaunch", "BrokerInfrastructure",
            "SystemEventsBroker", "TimeBrokerSvc", "Themes", "Winmgmt", "Schedule",
            "CryptSvc", "LanmanWorkstation", "LanmanServer", "ProfSvc", "gpsvc",
            "LSM", "FontCache", "StateRepository",
            // Debloat specific
            "Microsoft.Windows.SecHealthUI",
            "Microsoft.StorePurchaseApp",
            "Microsoft.WindowsStore",
            "Microsoft.NET.Native.Framework",
            "Microsoft.NET.Native.Runtime",
            "Microsoft.VCLibs",
            "Microsoft.Windows.ShellExperienceHost",
            "Microsoft.Windows.StartMenuExperienceHost",
            "Microsoft.Windows.Client.WebExperience"
        };
        _protectedTargets = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsProtectedTarget(string targetName)
    {
        foreach (var t in _protectedTargets)
        {
            if (targetName.Equals(t, StringComparison.OrdinalIgnoreCase) || 
                targetName.StartsWith(t + "_", StringComparison.OrdinalIgnoreCase)) // Support partial AppX matching if needed
            {
                return true;
            }
        }
        return false;
    }
}
