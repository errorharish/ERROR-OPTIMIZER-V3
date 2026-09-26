using System.Management;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.CLI.Adapters.Bios;

public class LenovoProvider : IBiosProvider
{
    private const string WmiNamespace = @"root\wmi";

    public BiosOptimizer.Core.Interfaces.ProviderInfo GetProviderInfo()
    {
#pragma warning disable CA1416
        try
        {
            var scope = new ManagementScope($@"\\.\{WmiNamespace}");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Lenovo_BiosSetting"));
            var count = searcher.Get().Count;
            if (count > 0)
            {
                return new BiosOptimizer.Core.Interfaces.ProviderInfo { Name = "Lenovo BIOS Provider", Status = "Available", Reason = "WMI Namespace and Lenovo classes reachable." };
            }
            return new BiosOptimizer.Core.Interfaces.ProviderInfo { Name = "Lenovo BIOS Provider", Status = "Unavailable", Reason = "Lenovo_BiosSetting class not found or empty." };
        }
        catch (Exception ex)
        {
            return new BiosOptimizer.Core.Interfaces.ProviderInfo { Name = "Lenovo BIOS Provider", Status = "Unavailable", Reason = $"Required WMI namespace/class not found: {ex.Message}" };
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
            using var searcher = new ManagementObjectSearcher(new ManagementScope($@"\\.\{WmiNamespace}"), new ObjectQuery("SELECT * FROM Lenovo_BiosSetting"));
            foreach (var obj in searcher.Get())
            {
                var currentSetting = obj["CurrentSetting"]?.ToString() ?? "";
                var parts = currentSetting.Split(',');
                if (parts.Length >= 2)
                {
                    var attrName = parts[0];
                    var currentValue = parts[1];
                    var allowedValues = parts.Length > 2 ? parts[2].Split(';').ToList() : new List<string>();

                    if (!IsForbidden(attrName))
                    {
                        capabilities.Add(new BiosCapability
                        {
                            SettingId = attrName,
                            DisplayName = attrName,
                            Provider = "Lenovo",
                            Supported = true,
                            Writable = true, // Lenovo doesn't explicitly expose read-only via this string format typically without parsing deeper, assuming writable if exposed
                            CurrentValue = currentValue,
                            AllowedValues = allowedValues,
                            RiskLevel = DetermineRisk(attrName),
                            RequiresReboot = true
                        });
                    }
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
            
            // 1. Set the setting
            using var setClass = new ManagementClass(scope, new ManagementPath("Lenovo_SetBiosSetting"), null);
            foreach (ManagementObject obj in setClass.GetInstances())
            {
                var inParams = obj.GetMethodParameters("SetBiosSetting");
                inParams["Parameter"] = $"{settingId},{value};";
                var outParams = obj.InvokeMethod("SetBiosSetting", inParams, null);
                var ret = outParams?["return"]?.ToString();
                if (ret != "Success")
                {
                    return new ProviderOperationResult { Success = false, Message = $"SetBiosSetting failed: {ret}" };
                }
            }

            // 2. Save the settings
            using var saveClass = new ManagementClass(scope, new ManagementPath("Lenovo_SaveBiosSettings"), null);
            foreach (ManagementObject obj in saveClass.GetInstances())
            {
                var inParams = obj.GetMethodParameters("SaveBiosSettings");
                inParams["Parameter"] = ";"; // empty password format typically
                var outParams = obj.InvokeMethod("SaveBiosSettings", inParams, null);
                var ret = outParams?["return"]?.ToString();
                if (ret != "Success")
                {
                    return new ProviderOperationResult { Success = false, Message = $"SaveBiosSettings failed: {ret}" };
                }
            }

            return new ProviderOperationResult { Success = true, RequiresReboot = true };
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
        var forbidden = new[] { "TPM", "SecureBoot", "BootMode", "CSM", "SataMode", "Password", "BootOrder", "Computrace", "Flash" };
        return forbidden.Any(f => settingId.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    private string DetermineRisk(string settingId)
    {
        if (settingId.Contains("WakeOnLAN", StringComparison.OrdinalIgnoreCase)) return "Low";
        if (settingId.Contains("FastBoot", StringComparison.OrdinalIgnoreCase)) return "Low";
        return "Medium";
    }
}
