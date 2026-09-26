using System.Runtime.Versioning;
using System.ServiceProcess;
using BiosOptimizer.Core.Interfaces;
using Microsoft.Win32;

namespace BiosOptimizer.CLI.Adapters;

[SupportedOSPlatform("windows")]
public class WindowsServiceManager : IServiceManager
{
    public ServiceState? GetService(string serviceName)
    {
        try
        {
            var services = ServiceController.GetServices();
            var service = services.FirstOrDefault(s => s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
            if (service == null) return null;

            var result = new ServiceState
            {
                ServiceName = service.ServiceName,
                DisplayName = service.DisplayName,
                StartupType = GetStartupType(serviceName),
                RunningState = GetRunningState(serviceName)
            };
            
            PopulateRegistryDetails(result);
            return result;
        }
        catch
        {
            return null;
        }
    }

    public List<ServiceState> GetAllServices()
    {
        var list = new List<ServiceState>();
        var services = ServiceController.GetServices();
        foreach (var service in services)
        {
            var state = new ServiceState
            {
                ServiceName = service.ServiceName,
                DisplayName = service.DisplayName,
                RunningState = service.Status switch
                {
                    ServiceControllerStatus.Running => ServiceRunningState.Running,
                    ServiceControllerStatus.Stopped => ServiceRunningState.Stopped,
                    ServiceControllerStatus.Paused => ServiceRunningState.Paused,
                    _ => ServiceRunningState.Unknown
                }
            };
            PopulateRegistryDetails(state);
            list.Add(state);
        }
        return list;
    }

    private void PopulateRegistryDetails(ServiceState state)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"System\CurrentControlSet\Services\{state.ServiceName}", false);
            if (key != null)
            {
                var startObj = key.GetValue("Start");
                if (startObj is int startValue)
                {
                    state.StartupType = startValue switch
                    {
                        2 => ServiceStartupType.Automatic,
                        3 => ServiceStartupType.Manual,
                        4 => ServiceStartupType.Disabled,
                        _ => ServiceStartupType.Unknown
                    };
                }
                
                state.Description = key.GetValue("Description")?.ToString() ?? string.Empty;
                state.ExePath = key.GetValue("ImagePath")?.ToString() ?? string.Empty;
            }
        }
        catch { }
    }

    public ServiceStartupType GetStartupType(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"System\CurrentControlSet\Services\{serviceName}", false);
            if (key == null) return ServiceStartupType.Unknown;

            var startObj = key.GetValue("Start");
            if (startObj is int startValue)
            {
                // 2 = Auto, 3 = Manual, 4 = Disabled
                return startValue switch
                {
                    2 => ServiceStartupType.Automatic,
                    3 => ServiceStartupType.Manual,
                    4 => ServiceStartupType.Disabled,
                    _ => ServiceStartupType.Unknown
                };
            }
        }
        catch { }
        return ServiceStartupType.Unknown;
    }

    public ServiceRunningState GetRunningState(string serviceName)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            return controller.Status switch
            {
                ServiceControllerStatus.Running => ServiceRunningState.Running,
                ServiceControllerStatus.Stopped => ServiceRunningState.Stopped,
                ServiceControllerStatus.Paused => ServiceRunningState.Paused,
                _ => ServiceRunningState.Unknown
            };
        }
        catch
        {
            return ServiceRunningState.Unknown;
        }
    }

    public bool SetStartupType(string serviceName, ServiceStartupType startupType)
    {
        int startValue = startupType switch
        {
            ServiceStartupType.Automatic => 2,
            ServiceStartupType.Manual => 3,
            ServiceStartupType.Disabled => 4,
            _ => -1
        };

        if (startValue == -1) return false;

        using var key = Registry.LocalMachine.OpenSubKey($@"System\CurrentControlSet\Services\{serviceName}", true);
        if (key == null) return false; // Key doesn't exist?

        key.SetValue("Start", startValue, RegistryValueKind.DWord);
        return true;
    }

    public bool Start(string serviceName)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            if (controller.Status != ServiceControllerStatus.Running)
            {
                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Stop(string serviceName)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            if (controller.Status != ServiceControllerStatus.Stopped)
            {
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
