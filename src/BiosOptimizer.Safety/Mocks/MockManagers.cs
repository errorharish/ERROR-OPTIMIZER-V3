using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Models;

namespace BiosOptimizer.Safety.Mocks;

public sealed class MockEnvironmentDetector : IEnvironmentDetector
{
    public EnvironmentContext Detect() => new()
    {
        IsWindows10 = true,
        IsWindows11 = false,
        IsDesktop = true,
        IsLaptop = false,
        HasBattery = false,
        RamSizeGb = 16,
        RamSizeBytes = 16L * 1024 * 1024 * 1024,
        PhysicalCoreCount = 4,
        LogicalCoreCount = 8,
        Architecture = "x64",
        Edition = "Test",
        WindowsBuild = "test"
    };
}

public sealed class MockProcessSnapshot : IProcessSnapshot
{
    public int MockCount { get; set; }

    public ProcessSnapshotInfo TakeSnapshot() => new()
    {
        Timestamp = DateTime.UtcNow,
        ProcessCount = MockCount
    };
}

public class MockServiceManager : IServiceManager
{
    private readonly Dictionary<string, ServiceState> _services = new(StringComparer.OrdinalIgnoreCase);

    public void AddMockService(string serviceName, ServiceStartupType startupType)
    {
        _services[serviceName] = new ServiceState
        {
            ServiceName = serviceName,
            DisplayName = serviceName,
            StartupType = startupType,
            RunningState = ServiceRunningState.Stopped
        };
    }

    public ServiceState? GetService(string serviceName) =>
        _services.TryGetValue(serviceName, out var service) ? service : null;

    public virtual ServiceStartupType GetStartupType(string serviceName) =>
        GetService(serviceName)?.StartupType ?? ServiceStartupType.Unknown;

    public ServiceRunningState GetRunningState(string serviceName) =>
        GetService(serviceName)?.RunningState ?? ServiceRunningState.Unknown;

    public virtual bool SetStartupType(string serviceName, ServiceStartupType startupType)
    {
        if (!_services.TryGetValue(serviceName, out var service))
            return false;

        service.StartupType = startupType;
        return true;
    }

    public virtual bool Start(string serviceName)
    {
        var service = GetService(serviceName);
        if (service is null) return false;
        service.RunningState = ServiceRunningState.Running;
        return true;
    }

    public virtual bool Stop(string serviceName)
    {
        var service = GetService(serviceName);
        if (service is null) return false;
        service.RunningState = ServiceRunningState.Stopped;
        return true;
    }

    public virtual bool Restart(string serviceName)
    {
        var service = GetService(serviceName);
        if (service is null) return false;
        service.RunningState = ServiceRunningState.Running;
        return true;
    }

    public List<ServiceState> GetAllServices() => _services.Values.ToList();
}
