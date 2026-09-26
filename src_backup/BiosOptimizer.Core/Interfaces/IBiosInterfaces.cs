namespace BiosOptimizer.Core.Interfaces;

public class ProviderInfo
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class BiosCapability
{
    public string SettingId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public bool Supported { get; set; }
    public bool Writable { get; set; }
    public string CurrentValue { get; set; } = string.Empty;
    public List<string> AllowedValues { get; set; } = new();
    public string RiskLevel { get; set; } = "Low";
    public bool RequiresReboot { get; set; }
    public string Warning { get; set; } = string.Empty;
}

public class ProviderOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool RequiresReboot { get; set; }
}

public interface IBiosProvider
{
    ProviderInfo GetProviderInfo();
    List<BiosCapability> DiscoverCapabilities();
    BiosCapability? GetCapability(string settingId);
    ProviderOperationResult WriteSetting(string settingId, string value);
    bool VerifySetting(string settingId, string expectedValue);
}

public interface IProviderManager
{
    IBiosProvider GetActiveProvider();
}
