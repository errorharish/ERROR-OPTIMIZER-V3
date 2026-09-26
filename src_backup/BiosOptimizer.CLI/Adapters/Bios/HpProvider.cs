using System.Management;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.CLI.Adapters.Bios;

public class HpProvider : IBiosProvider
{
    private const string WmiNamespace = @"root\hp\instrumentedBIOS";

    public BiosOptimizer.Core.Interfaces.ProviderInfo GetProviderInfo()
    {
#pragma warning disable CA1416
        try
        {
            var scope = new ManagementScope($@"\\.\{WmiNamespace}");
            scope.Connect();
            return new BiosOptimizer.Core.Interfaces.ProviderInfo { Name = "HP BIOS Provider", Status = "Available", Reason = "WMI Namespace reachable." };
        }
        catch (Exception ex)
        {
            return new BiosOptimizer.Core.Interfaces.ProviderInfo { Name = "HP BIOS Provider", Status = "Unavailable", Reason = $"Required WMI namespace not found: {ex.Message}" };
        }
#pragma warning restore CA1416
    }

    public List<BiosCapability> DiscoverCapabilities()
    {
        var capabilities = new List<BiosCapability>();
        if (GetProviderInfo().Status != "Available") return capabilities;

#pragma warning disable CA1416
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope($@"\\.\{WmiNamespace}"), new ObjectQuery("SELECT * FROM HP_BIOSEnumeration"));
            foreach (var obj in searcher.Get())
            {
                var attrName = obj["Name"]?.ToString() ?? "";
                var currentValue = obj["CurrentValue"]?.ToString() ?? "";
                var possibleValues = obj["PossibleValues"] as string[] ?? Array.Empty<string>();
                var isReadOnly = (bool)(obj["IsReadOnly"] ?? false);

                if (!IsForbidden(attrName))
                {
                    capabilities.Add(new BiosCapability
                    {
                        SettingId = attrName,
                        DisplayName = attrName,
                        Provider = "HP",
                        Supported = true,
                        Writable = !isReadOnly,
                        CurrentValue = currentValue,
                        AllowedValues = possibleValues.ToList(),
                        RiskLevel = DetermineRisk(attrName),
                        RequiresReboot = true
                    });
                }
            }
        }
        catch { }
#pragma warning restore CA1416

        return capabilities;
    }

    public BiosCapability? GetCapability(string settingId)
    {
        return DiscoverCapabilities().FirstOrDefault(c => c.SettingId.Equals(settingId, StringComparison.OrdinalIgnoreCase));
    }

    public ProviderOperationResult WriteSetting(string settingId, string value)
    {
        if (IsForbidden(settingId)) return new ProviderOperationResult { Success = false, Message = "Protected BIOS operation." };

#pragma warning disable CA1416
        try
        {
            var scope = new ManagementScope($@"\\.\{WmiNamespace}");
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM HP_BIOSSettingInterface"));
            foreach (ManagementObject service in searcher.Get())
            {
                var inParams = service.GetMethodParameters("SetBIOSSetting");
                inParams["Name"] = settingId;
                inParams["Value"] = value;
                inParams["Password"] = "<utf-16/>"; // HP standard empty password format

                var outParams = service.InvokeMethod("SetBIOSSetting", inParams, null);
                var returnVal = outParams?["Return"]?.ToString();
                
                if (returnVal == "0") 
                {
                    return new ProviderOperationResult { Success = true, RequiresReboot = true };
                }
                else
                {
                    return new ProviderOperationResult { Success = false, Message = $"SetBIOSSetting returned error code: {returnVal}" };
                }
            }
            return new ProviderOperationResult { Success = false, Message = "HP_BIOSSettingInterface not found." };
        }
        catch (Exception ex)
        {
            return new ProviderOperationResult { Success = false, Message = $"WMI exception: {ex.Message}" };
        }
#pragma warning restore CA1416
    }

    public bool VerifySetting(string settingId, string expectedValue)
    {
        var cap = GetCapability(settingId);
        return cap != null && string.Equals(cap.CurrentValue, expectedValue, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsForbidden(string settingId)
    {
        var forbidden = new[] { "TPM", "SecureBoot", "BootMode", "CSM", "SataMode", "Password", "BootOrder", "MemoryOverclock", "Undervolt" };
        return forbidden.Any(f => settingId.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    private string DetermineRisk(string settingId)
    {
        if (settingId.Contains("Wake On LAN", StringComparison.OrdinalIgnoreCase)) return "Low";
        if (settingId.Contains("Fast Boot", StringComparison.OrdinalIgnoreCase)) return "Low";
        return "Medium";
    }
}
