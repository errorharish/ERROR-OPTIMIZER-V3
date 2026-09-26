using BiosOptimizer.Core.Interfaces;
using System.Linq;
using System.Collections.Generic;

namespace BiosOptimizer.Safety.Mocks;

public class MockServiceManager : IServiceManager
{
    private readonly Dictionary<string, ServiceState> _services = new(StringComparer.OrdinalIgnoreCase);

    public void AddMockService(string name, ServiceStartupType startup, ServiceRunningState state = ServiceRunningState.Running)
    {
        _services[name] = new ServiceState { ServiceName = name, DisplayName = name, StartupType = startup, RunningState = state };
    }

    public virtual ServiceState? GetService(string serviceName)
    {
        _services.TryGetValue(serviceName, out var service);
        return service;
    }

    public virtual ServiceStartupType GetStartupType(string serviceName)
    {
        if (_services.TryGetValue(serviceName, out var service)) return service.StartupType;
        return ServiceStartupType.Unknown;
    }

    public virtual ServiceRunningState GetRunningState(string serviceName)
    {
        if (_services.TryGetValue(serviceName, out var service)) return service.RunningState;
        return ServiceRunningState.Unknown;
    }

    public virtual bool SetStartupType(string serviceName, ServiceStartupType startupType)
    {
        if (_services.TryGetValue(serviceName, out var service))
        {
            service.StartupType = startupType;
            return true;
        }
        return false;
    }

    public virtual bool Start(string serviceName) => true;
    public virtual bool Stop(string serviceName) => true;

    public virtual List<ServiceState> GetAllServices() => _services.Values.ToList();
}
