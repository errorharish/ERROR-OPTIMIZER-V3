using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.ServiceProcess;
using BiosOptimizer.Core.Interfaces;
using Microsoft.Win32;

namespace BiosOptimizer.CLI.Adapters;

[SupportedOSPlatform("windows")]
public class WindowsServiceManager : IServiceManager
{
    private static readonly HashSet<string> CriticalServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "LSM", "PlugPlay", "SamSs", 
        "EventLog", "CryptSvc", "WinDefend", "SecurityHealthService", 
        "Schedule", "ProfSvc", "BrokerInfrastructure", "SystemEventsBroker",
        "KeyIso", "VaultSvc", "TokenBroker", "StateRepository"
    };

    private static readonly HashSet<string> HighRiskServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dhcp", "Dnscache", "LanmanWorkstation", "LanmanServer", "wuauserv", 
        "AudioSrv", "AudioEndpointBuilder", "WlanSvc", "mpssvc", "FontCache", 
        "CoreMessagingRegistrar", "BFE", "gpsvc", "UserManager"
    };

    private static readonly HashSet<string> MediumRiskServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "WSearch", "SysMain", "DiagTrack", "Spooler", "TabletInputService", 
        "MapsBroker", "RetailDemo", "XboxGipSvc", "wisvc", "WbioSrvc", 
        "WerSvc", "PcaSvc", "DusmSvc", "PrintNotify"
    };

    public ServiceState? GetService(string serviceName)
    {
        try
        {
            ServiceController? controller = null;
            try
            {
                var services = ServiceController.GetServices();
                controller = services.FirstOrDefault(s => s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
            }
            catch { }

            using var key = Registry.LocalMachine.OpenSubKey($@"System\CurrentControlSet\Services\{serviceName}", false);
            if (controller == null && key == null) return null;

            var result = new ServiceState
            {
                ServiceName = controller?.ServiceName ?? serviceName,
                DisplayName = controller?.DisplayName ?? key?.GetValue("DisplayName")?.ToString() ?? serviceName,
                StartupType = GetStartupType(serviceName),
                RunningState = controller != null ? MapRunningState(controller.Status) : GetRunningState(serviceName)
            };

            if (controller != null)
            {
                PopulateControllerDetails(controller, result);
            }

            PopulateRegistryDetails(result);
            ClassifyService(result);
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
        try
        {
            var services = ServiceController.GetServices();
            foreach (var service in services)
            {
                var state = new ServiceState
                {
                    ServiceName = service.ServiceName,
                    DisplayName = service.DisplayName,
                    RunningState = MapRunningState(service.Status)
                };

                PopulateControllerDetails(service, state);
                PopulateRegistryDetails(state);
                ClassifyService(state);
                list.Add(state);
            }
        }
        catch { }
        return list;
    }

    private static ServiceRunningState MapRunningState(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => ServiceRunningState.Running,
        ServiceControllerStatus.Stopped => ServiceRunningState.Stopped,
        ServiceControllerStatus.Paused => ServiceRunningState.Paused,
        _ => ServiceRunningState.Unknown
    };

    private void PopulateControllerDetails(ServiceController controller, ServiceState state)
    {
        try
        {
            state.ServiceType = controller.ServiceType.ToString();
        }
        catch { }

        // Dependencies (Depends On)
        try
        {
            var depended = controller.ServicesDependedOn;
            if (depended != null && depended.Length > 0)
            {
                foreach (var d in depended)
                {
                    try
                    {
                        var name = !string.IsNullOrEmpty(d.DisplayName) ? d.DisplayName : d.ServiceName;
                        if (!state.DependsOn.Contains(name))
                            state.DependsOn.Add(name);
                    }
                    catch { }
                }
            }
        }
        catch { }

        // Dependent Services (Who depends on this)
        try
        {
            var dependents = controller.DependentServices;
            if (dependents != null && dependents.Length > 0)
            {
                foreach (var d in dependents)
                {
                    try
                    {
                        var name = !string.IsNullOrEmpty(d.DisplayName) ? d.DisplayName : d.ServiceName;
                        if (!state.DependentServices.Contains(name))
                            state.DependentServices.Add(name);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private void PopulateRegistryDetails(ServiceState state)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"System\CurrentControlSet\Services\{state.ServiceName}", false);
            if (key != null)
            {
                var startObj = key.GetValue("Start");
                var delayedObj = key.GetValue("DelayedAutoStart");
                bool isDelayed = (delayedObj is int delayedVal && delayedVal == 1);
                state.IsDelayedStart = isDelayed;

                if (startObj is int startValue)
                {
                    state.StartupType = startValue switch
                    {
                        0 => ServiceStartupType.Automatic, // Boot
                        1 => ServiceStartupType.Automatic, // System
                        2 => isDelayed ? ServiceStartupType.AutomaticDelayedStart : ServiceStartupType.Automatic,
                        3 => ServiceStartupType.Manual,
                        4 => ServiceStartupType.Disabled,
                        _ => ServiceStartupType.Unknown
                    };
                }

                state.Description = key.GetValue("Description")?.ToString() ?? string.Empty;
                state.ExePath = key.GetValue("ImagePath")?.ToString() ?? string.Empty;
                state.Account = key.GetValue("ObjectName")?.ToString() ?? "LocalSystem";
            }
        }
        catch { }

        // File Version & Publisher Extraction
        try
        {
            var cleanPath = CleanExecutablePath(state.ExePath);
            if (!string.IsNullOrEmpty(cleanPath) && File.Exists(cleanPath))
            {
                state.FileExists = true;
                var info = FileVersionInfo.GetVersionInfo(cleanPath);
                if (!string.IsNullOrWhiteSpace(info.CompanyName))
                    state.Publisher = info.CompanyName.Trim();
                if (!string.IsNullOrWhiteSpace(info.FileVersion))
                    state.Version = info.FileVersion.Trim();
            }
        }
        catch { }
    }

    private void ClassifyService(ServiceState state)
    {
        var sName = state.ServiceName.ToLowerInvariant();
        var desc = state.Description.ToLowerInvariant();
        var pub = state.Publisher.ToLowerInvariant();
        var path = state.ExePath.ToLowerInvariant();

        // 1. Critical flag
        state.IsCritical = CriticalServices.Contains(state.ServiceName);

        // 2. Microsoft check
        state.IsMicrosoft = pub.Contains("microsoft") || 
                            path.Contains(@"\windows\system32\svchost.exe") || 
                            path.Contains(@"\windows\system32\services.exe") || 
                            path.Contains(@"\windows\system32\lsass.exe") ||
                            CriticalServices.Contains(state.ServiceName) ||
                            HighRiskServices.Contains(state.ServiceName);

        if (state.IsMicrosoft && (string.IsNullOrEmpty(state.Publisher) || state.Publisher == "Unknown"))
        {
            state.Publisher = "Microsoft Corporation";
        }

        // 3. Category
        if (sName.Contains("defender") || sName.Contains("sense") || sName.Contains("windefend") || 
            sName.Contains("wdnissvc") || sName.Contains("securityhealth") || sName.Contains("wscsvc") || 
            sName.Contains("samss") || desc.Contains("antivirus") || desc.Contains("bitlocker") || desc.Contains("security"))
        {
            state.Category = "SECURITY";
        }
        else if (sName.Contains("audiosrv") || sName.Contains("audioendpoint") || pub.Contains("realtek") || 
                 pub.Contains("nahimic") || desc.Contains("audio") || desc.Contains("sound"))
        {
            state.Category = "AUDIO";
        }
        else if (sName.Contains("nvsvc") || sName.Contains("nvcontainer") || pub.Contains("nvidia") || 
                 pub.Contains("amd") || pub.Contains("radeon") || desc.Contains("graphics") || desc.Contains("display"))
        {
            state.Category = "GRAPHICS";
        }
        else if (sName.Contains("dhcp") || sName.Contains("dnscache") || sName.Contains("lanman") || 
                 sName.Contains("wlansvc") || sName.Contains("netman") || sName.Contains("nlasvc") || 
                 sName.Contains("tcpip") || sName.Contains("iphlpsvc") || desc.Contains("network") || desc.Contains("wi-fi"))
        {
            state.Category = "NETWORK";
        }
        else if (sName.Contains("steam") || sName.Contains("epic") || sName.Contains("riot") || 
                 sName.Contains("vanguard") || sName.Contains("easyanticheat") || sName.Contains("battleye") || 
                 sName.StartsWith("xbl") || sName.Contains("xbox") || desc.Contains("gaming"))
        {
            state.Category = "GAMING";
        }
        else if (state.ServiceType.Contains("Driver", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\drivers\"))
        {
            state.Category = "DRIVER";
        }
        else if (!state.IsMicrosoft)
        {
            state.Category = "THIRD-PARTY";
        }
        else
        {
            state.Category = "SYSTEM";
        }

        // 4. Deterministic System Risk (How sensitive/important is this service to Windows?)
        if (state.IsCritical)
        {
            state.Risk = "CRITICAL";
        }
        else if (HighRiskServices.Contains(state.ServiceName))
        {
            state.Risk = "HIGH";
        }
        else if (MediumRiskServices.Contains(state.ServiceName))
        {
            state.Risk = "MEDIUM";
        }
        else if (!state.IsMicrosoft || state.Category == "GAMING" || state.Category == "THIRD-PARTY")
        {
            state.Risk = "LOW";
        }
        else
        {
            state.Risk = "LOW";
        }

        // 5. Independent Action Safety Analyzer (Can the user safely stop this service right now?)
        DetermineActionSafety(state, sName, desc, pub, path);
    }

    private void DetermineActionSafety(ServiceState state, string sName, string desc, string pub, string path)
    {
        // RULE 1: Core Critical Windows Infrastructure / OS Drivers -> DO NOT STOP
        if (state.IsCritical || state.Category == "DRIVER" || state.ServiceType.Contains("Driver", StringComparison.OrdinalIgnoreCase))
        {
            state.ActionSafety = "DO NOT STOP";
            state.ActionSafetyReason = "Required for core Windows security, kernel stability, or system hardware execution.";
            return;
        }

        // RULE 2: Active Dependent Services Graph
        if (state.DependentServices.Count > 0)
        {
            if (state.Risk == "CRITICAL" || state.Risk == "HIGH")
            {
                state.ActionSafety = "DO NOT STOP";
                state.ActionSafetyReason = $"{state.DependentServices.Count} active subsystem(s) depend on this service ({string.Join(", ", state.DependentServices.Take(3))}).";
                return;
            }

            state.ActionSafety = "NOT RECOMMENDED TO STOP";
            state.ActionSafetyReason = $"{state.DependentServices.Count} running service(s) depend on this service ({string.Join(", ", state.DependentServices.Take(3))}).";
            return;
        }

        // RULE 3: Core Subsystems (Networking, Audio Core, Windows Update, Security Subsystems)
        if (HighRiskServices.Contains(state.ServiceName))
        {
            state.ActionSafety = "NOT RECOMMENDED TO STOP";
            state.ActionSafetyReason = $"Core Windows subsystem service ({state.DisplayName}). Stopping may disrupt networking, audio routing, or system policy.";
            return;
        }

        // RULE 4: Application-Specific Ecosystem & Companion Services -> SAFE TO STOP WITH CAUTION
        if (sName.Contains("lghub") || sName.Contains("logi") || pub.Contains("logitech"))
        {
            state.ActionSafety = "SAFE TO STOP WITH CAUTION";
            state.ActionSafetyReason = "Stopping this service may disable Logitech G HUB lighting and macro synchronization.";
            return;
        }

        if (sName.Contains("razer") || pub.Contains("razer"))
        {
            state.ActionSafety = "SAFE TO STOP WITH CAUTION";
            state.ActionSafetyReason = "Stopping this service may disable Razer Synapse profile switching and Chroma RGB.";
            return;
        }

        if (sName.Contains("corsair") || sName.Contains("cue") || pub.Contains("corsair"))
        {
            state.ActionSafety = "SAFE TO STOP WITH CAUTION";
            state.ActionSafetyReason = "Stopping this service may disable Corsair iCUE lighting and pump/fan telemetry.";
            return;
        }

        if (sName.Contains("nvdisplay") || sName.Contains("nvcontainer") || pub.Contains("nvidia"))
        {
            state.ActionSafety = "SAFE TO STOP WITH CAUTION";
            state.ActionSafetyReason = "Stopping may disable NVIDIA GeForce Experience overlay, though display remains functional.";
            return;
        }

        if (sName.Contains("amd") || sName.Contains("radeon") || pub.Contains("advanced micro devices"))
        {
            state.ActionSafety = "SAFE TO STOP WITH CAUTION";
            state.ActionSafetyReason = "Stopping may disable AMD Software companion features, though core display remains active.";
            return;
        }

        if (sName.Contains("steam") || sName.Contains("epic") || sName.Contains("riot") || sName.Contains("vanguard"))
        {
            state.ActionSafety = "SAFE TO STOP WITH CAUTION";
            state.ActionSafetyReason = "Stopping this service may prevent game launching, cloud saves, or anti-cheat validation.";
            return;
        }

        if (sName.Contains("voicemod") || sName.Contains("discord"))
        {
            state.ActionSafety = "SAFE TO STOP WITH CAUTION";
            state.ActionSafetyReason = "Stopping this service may interrupt virtual audio device routing or auto-updates.";
            return;
        }

        // RULE 5: Optional Windows Telemetry, Search, and Spooler
        if (sName.Equals("diagtrack", StringComparison.OrdinalIgnoreCase))
        {
            state.ActionSafety = "SAFE TO STOP";
            state.ActionSafetyReason = "Optional diagnostic data collection service. Safe to stop for privacy and performance.";
            return;
        }

        if (sName.Equals("wsearch", StringComparison.OrdinalIgnoreCase))
        {
            state.ActionSafety = "SAFE TO STOP WITH CAUTION";
            state.ActionSafetyReason = "Stopping pauses background search indexing. File search will still function via direct disk scan.";
            return;
        }

        if (sName.Equals("spooler", StringComparison.OrdinalIgnoreCase))
        {
            state.ActionSafety = "SAFE TO STOP WITH CAUTION";
            state.ActionSafetyReason = "Safe to stop if physical printers or PDF export print queues are not in active use.";
            return;
        }

        if (sName.Equals("mapsbroker", StringComparison.OrdinalIgnoreCase) || 
            sName.Equals("retaildemo", StringComparison.OrdinalIgnoreCase) || 
            sName.Equals("wersvc", StringComparison.OrdinalIgnoreCase) || 
            sName.Equals("pcasvc", StringComparison.OrdinalIgnoreCase) ||
            sName.Equals("dussvc", StringComparison.OrdinalIgnoreCase))
        {
            state.ActionSafety = "SAFE TO STOP";
            state.ActionSafetyReason = "Optional Windows background helper service. Safe to stop on standard desktop workloads.";
            return;
        }

        // RULE 6: Standalone Third-Party Updaters (0 Dependents) -> SAFE TO STOP
        if (sName.Contains("update") || sName.Contains("updater") || sName.Contains("elevation") || desc.Contains("updater"))
        {
            state.ActionSafety = "SAFE TO STOP";
            state.ActionSafetyReason = "Optional background updater daemon. Applications will check for updates manually on launch.";
            return;
        }

        // RULE 7: Generic Third-Party Services (0 Dependents) -> SAFE TO STOP
        if (!state.IsMicrosoft && state.DependentServices.Count == 0)
        {
            state.ActionSafety = "SAFE TO STOP";
            state.ActionSafetyReason = "Non-essential third-party helper service with no detected system dependencies.";
            return;
        }

        // RULE 8: Standard Microsoft Background Services (0 Dependents)
        if (state.IsMicrosoft)
        {
            state.ActionSafety = "NOT RECOMMENDED TO STOP";
            state.ActionSafetyReason = "Windows system service. Stopping is not recommended without specific optimization intent.";
            return;
        }

        // RULE 9: Unclassified Fallback -> UNKNOWN
        state.ActionSafety = "UNKNOWN";
        state.ActionSafetyReason = "Insufficient metadata to reliably verify service safety. Exercise caution before stopping.";
    }

    private static string CleanExecutablePath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string expanded = Environment.ExpandEnvironmentVariables(raw).Trim();

        if (expanded.StartsWith("\""))
        {
            int closeQuote = expanded.IndexOf('\"', 1);
            if (closeQuote > 1)
                return expanded.Substring(1, closeQuote - 1);
        }

        int exeIndex = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex > 0)
        {
            return expanded.Substring(0, exeIndex + 4).Trim('\"', ' ');
        }

        int sysIndex = expanded.IndexOf(".sys", StringComparison.OrdinalIgnoreCase);
        if (sysIndex > 0)
        {
            return expanded.Substring(0, sysIndex + 4).Trim('\"', ' ');
        }

        int spaceIndex = expanded.IndexOf(' ');
        if (spaceIndex > 0)
        {
            return expanded.Substring(0, spaceIndex);
        }

        return expanded;
    }

    public ServiceStartupType GetStartupType(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"System\CurrentControlSet\Services\{serviceName}", false);
            if (key == null) return ServiceStartupType.Unknown;

            var startObj = key.GetValue("Start");
            var delayedObj = key.GetValue("DelayedAutoStart");
            bool isDelayed = (delayedObj is int delayedVal && delayedVal == 1);

            if (startObj is int startValue)
            {
                return startValue switch
                {
                    0 => ServiceStartupType.Automatic,
                    1 => ServiceStartupType.Automatic,
                    2 => isDelayed ? ServiceStartupType.AutomaticDelayedStart : ServiceStartupType.Automatic,
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
            return MapRunningState(controller.Status);
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
            ServiceStartupType.Automatic or ServiceStartupType.AutomaticDelayedStart => 2,
            ServiceStartupType.Manual => 3,
            ServiceStartupType.Disabled => 4,
            _ => -1
        };

        if (startValue == -1) return false;

        bool isDelayed = startupType == ServiceStartupType.AutomaticDelayedStart;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"System\CurrentControlSet\Services\{serviceName}", true);
            if (key != null)
            {
                key.SetValue("Start", startValue, RegistryValueKind.DWord);
                if (isDelayed)
                    key.SetValue("DelayedAutoStart", 1, RegistryValueKind.DWord);
                else
                    key.DeleteValue("DelayedAutoStart", false);
                return true;
            }
        }
        catch { }

        // Fallback to sc.exe
        try
        {
            string scType = startupType switch
            {
                ServiceStartupType.Automatic or ServiceStartupType.AutomaticDelayedStart => "auto",
                ServiceStartupType.Manual => "demand",
                ServiceStartupType.Disabled => "disabled",
                _ => "demand"
            };

            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"config \"{serviceName}\" start= {scType}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);

            if (isDelayed)
            {
                var psiDelayed = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"config \"{serviceName}\" Delayed-Auto-Start= 1",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var procDelayed = Process.Start(psiDelayed);
                procDelayed?.WaitForExit(3000);
            }

            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Start(string serviceName)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            if (controller.Status != ServiceControllerStatus.Running)
            {
                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(8));
            }
            return controller.Status == ServiceControllerStatus.Running;
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
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
            }
            return controller.Status == ServiceControllerStatus.Stopped;
        }
        catch
        {
            return false;
        }
    }

    public bool Restart(string serviceName)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            if (controller.Status == ServiceControllerStatus.Running)
            {
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
            }

            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(8));
            return controller.Status == ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }
}

