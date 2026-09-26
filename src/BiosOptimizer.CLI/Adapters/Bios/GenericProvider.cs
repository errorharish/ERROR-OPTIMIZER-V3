using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.CLI.Adapters.Bios;

public class GenericProvider : IBiosProvider
{
    private readonly EnvironmentContext _context;

    public GenericProvider(EnvironmentContext context)
    {
        _context = context;
    }

    public BiosOptimizer.Core.Interfaces.ProviderInfo GetProviderInfo()
    {
        return new BiosOptimizer.Core.Interfaces.ProviderInfo 
        { 
            Name = "Generic Read-Only Provider", 
            Status = "Available", 
            Reason = "Fallback provider." 
        };
    }

    public List<BiosCapability> DiscoverCapabilities()
    {
        var capabilities = new List<BiosCapability>();

        // We can expose read-only capabilities that are universally accessible via WMI
        capabilities.Add(new BiosCapability
        {
            SettingId = "SecureBoot",
            DisplayName = "Secure Boot",
            Provider = "Generic",
            Supported = true,
            Writable = false,
            CurrentValue = "Read-Only (Requires Admin to modify outside tool)",
            AllowedValues = new List<string>(),
            RiskLevel = "Medium"
        });

        capabilities.Add(new BiosCapability
        {
            SettingId = "Manufacturer",
            DisplayName = "System Manufacturer",
            Provider = "Generic",
            Supported = true,
            Writable = false,
            CurrentValue = _context.Manufacturer,
            AllowedValues = new List<string>(),
            RiskLevel = "Low"
        });

        capabilities.Add(new BiosCapability
        {
            SettingId = "BIOSVersion",
            DisplayName = "BIOS Version",
            Provider = "Generic",
            Supported = true,
            Writable = false,
            CurrentValue = _context.BIOSVersion,
            AllowedValues = new List<string>(),
            RiskLevel = "Low"
        });

        return capabilities;
    }

    public BiosCapability? GetCapability(string settingId)
    {
        return DiscoverCapabilities().FirstOrDefault(c => c.SettingId.Equals(settingId, StringComparison.OrdinalIgnoreCase));
    }

    public ProviderOperationResult WriteSetting(string settingId, string value)
    {
        return new ProviderOperationResult { Success = false, Message = "Generic provider is read-only. Modifying BIOS settings is not supported for this manufacturer." };
    }

    public bool VerifySetting(string settingId, string expectedValue)
    {
        return false;
    }
}
