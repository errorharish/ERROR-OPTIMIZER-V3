using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Core.Implementations.Services;

public class ServiceClassification
{
    public string ServiceName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = "Unknown"; // Protected, Required, Safe Optional, Medium Risk, High Risk
    public string TargetState { get; set; } = "Keep";
}

public class ServiceDebloaterEngine
{
    private readonly ISafetyPolicy _safetyPolicy;

    public ServiceDebloaterEngine(ISafetyPolicy safetyPolicy)
    {
        _safetyPolicy = safetyPolicy;
    }

    public ServiceClassification ClassifyService(string serviceName, string displayName)
    {
        if (_safetyPolicy.IsProtectedTarget(serviceName))
        {
            return new ServiceClassification
            {
                ServiceName = serviceName,
                DisplayName = displayName,
                Category = "Protected",
                TargetState = "Keep"
            };
        }

        // Extremely simplified mapping for Phase 5 proof of concept
        if (serviceName.StartsWith("DiagTrack") || serviceName.StartsWith("wuauserv"))
        {
            return new ServiceClassification
            {
                ServiceName = serviceName,
                DisplayName = displayName,
                Category = "Safe Optional",
                TargetState = "Disable"
            };
        }

        return new ServiceClassification
        {
            ServiceName = serviceName,
            DisplayName = displayName,
            Category = "Unknown",
            TargetState = "Keep"
        };
    }
}
