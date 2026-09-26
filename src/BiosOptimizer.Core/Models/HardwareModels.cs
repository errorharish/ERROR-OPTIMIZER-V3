namespace BiosOptimizer.Core.Models;

public enum HardwareAccessResult
{
    Success,
    NotSupported,
    AccessDenied,
    HardwareError
}

public class ProviderInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; }
}

public class Capability
{
    public string Name { get; set; } = string.Empty;
    public bool Supported { get; set; }
}

public class BiosSetting
{
    public string SettingId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public object? CurrentValue { get; set; }
}

public class OperationResult
{
    public ResultStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class VerificationResult
{
    public bool IsMatch { get; set; }
    public string Message { get; set; } = string.Empty;
}
