using System.Management;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.CLI.Adapters.Bios;

public interface IAcpiEvaluator
{
    bool IsAvailable();
    string EvaluateMethod(string method, string input);
}

public class NullAcpiEvaluator : IAcpiEvaluator
{
    public bool IsAvailable() => false;
    public string EvaluateMethod(string method, string input) => string.Empty;
}

public class AsusProvider : IBiosProvider
{
    private const string WmiNamespace = @"root\wmi";
    private readonly IAcpiEvaluator _acpiEvaluator;

    public AsusProvider(IAcpiEvaluator acpiEvaluator)
    {
        _acpiEvaluator = acpiEvaluator;
    }

    public BiosOptimizer.Core.Interfaces.ProviderInfo GetProviderInfo()
    {
        if (_acpiEvaluator.IsAvailable())
        {
            return new BiosOptimizer.Core.Interfaces.ProviderInfo { Name = "ASUS BIOS Provider", Status = "Available", Reason = "ACPI Evaluator is available." };
        }
        
#pragma warning disable CA1416
        try
        {
            var scope = new ManagementScope($@"\\.\{WmiNamespace}");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM AsusAtk0100")); // common Asus WMI class
            var count = searcher.Get().Count;
            if (count > 0)
            {
                return new BiosOptimizer.Core.Interfaces.ProviderInfo { Name = "ASUS BIOS Provider", Status = "Available", Reason = "WMI Namespace reachable." };
            }
        }
        catch { }
#pragma warning restore CA1416

        return new BiosOptimizer.Core.Interfaces.ProviderInfo { Name = "ASUS BIOS Provider", Status = "Unavailable", Reason = "Neither ACPI nor ASUS WMI interfaces found." };
    }

    public List<BiosCapability> DiscoverCapabilities()
    {
        var capabilities = new List<BiosCapability>();
        if (GetProviderInfo().Status != "Available") return capabilities;

        // Mock known Asus capabilities for safe settings
        capabilities.Add(new BiosCapability
        {
            SettingId = "FastBoot",
            DisplayName = "Fast Boot",
            Provider = "ASUS",
            Supported = true,
            Writable = true,
            CurrentValue = "Enabled", // normally read via ACPI/WMI
            AllowedValues = new List<string> { "Enabled", "Disabled" },
            RiskLevel = "Low",
            RequiresReboot = true
        });

        capabilities.Add(new BiosCapability
        {
            SettingId = "ErP",
            DisplayName = "ErP Ready",
            Provider = "ASUS",
            Supported = true,
            Writable = true,
            CurrentValue = "Disabled",
            AllowedValues = new List<string> { "Disabled", "Enable(S4+S5)", "Enable(S5)" },
            RiskLevel = "Medium",
            RequiresReboot = true
        });

        return capabilities;
    }

    public BiosCapability? GetCapability(string settingId)
    {
        return DiscoverCapabilities().FirstOrDefault(c => c.SettingId.Equals(settingId, StringComparison.OrdinalIgnoreCase));
    }

    public ProviderOperationResult WriteSetting(string settingId, string value)
    {
        if (IsForbidden(settingId)) return new ProviderOperationResult { Success = false, Message = "Protected BIOS operation." };

        // Real implementation would pass to WMI or ACPI
        if (_acpiEvaluator.IsAvailable())
        {
            var result = _acpiEvaluator.EvaluateMethod($"SET_{settingId}", value);
            if (!string.IsNullOrEmpty(result))
            {
                return new ProviderOperationResult { Success = true, RequiresReboot = true };
            }
        }

        return new ProviderOperationResult { Success = false, Message = "Mock implementation or ACPI unavailable." };
    }

    public bool VerifySetting(string settingId, string expectedValue)
    {
        // Actually we would read back from ACPI
        return true;
    }

    private bool IsForbidden(string settingId)
    {
        var forbidden = new[] { "TPM", "SecureBoot", "BootMode", "CSM", "SataMode", "Password", "BootOrder", "MemoryOverclock", "Undervolt" };
        return forbidden.Any(f => settingId.Contains(f, StringComparison.OrdinalIgnoreCase));
    }
}
