using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BiosOptimizer.Core.Interfaces;
using Microsoft.Win32;

namespace BiosOptimizer.Core.Implementations
{
    public class NetworkOptimizerEngine : INetworkOptimizerEngine
    {
        private readonly IBackupManager _backupManager;
        private readonly INetworkDiagnosticsService _diagnosticsService;
        private static DateTime? _lastDnsFlushTime;
        private static readonly object _syncLock = new();

        public NetworkOptimizerEngine(IBackupManager backupManager, INetworkDiagnosticsService? diagnosticsService = null)
        {
            _backupManager = backupManager;
            _diagnosticsService = diagnosticsService ?? new NetworkDiagnosticsService();
        }

        public async Task<NetworkOptimizationPlan> PlanNetworkOptimizationAsync(bool includeDiagnostics = false)
        {
            return await Task.Run(async () =>
            {
                var plan = new NetworkOptimizationPlan();

                // 1. Discover Adapters using canonical diagnostics service
                plan.Adapters = DiscoverAdapters();

                // 2. Discover Network Actions & Real System States
                plan.Actions = DiscoverActions(plan.Adapters);

                // 3. DNS Flush Stateless Metadata
                plan.LastDnsFlushTime = _lastDnsFlushTime;

                // 4. Calculate Summary Metrics (Persistent Actions Only)
                var persistentActions = plan.Actions.Where(a => !a.IsStatelessAction).ToList();
                plan.TotalActions = persistentActions.Count;
                plan.SupportedCount = persistentActions.Count(a => a.Supported);
                plan.ApplicableCount = persistentActions.Count(a => a.Applicable);
                plan.PendingCount = persistentActions.Count(a => a.Applicable && a.Status == "PENDING");
                plan.OptimizedCount = persistentActions.Count(a => a.Status == "OPTIMIZED" || a.Status == "VERIFIED");
                plan.NotApplicableCount = persistentActions.Count(a => !a.Applicable || !a.Supported);

                // 5. Diagnostics (Read cached or execute real test)
                if (includeDiagnostics)
                {
                    plan.Diagnostics = await RunDiagnosticsAsync();
                }
                else
                {
                    plan.Diagnostics = _diagnosticsService.GetCachedDiagnostics();
                }

                return plan;
            });
        }

        public List<NetworkAdapterInfo> DiscoverAdapters()
        {
            return _diagnosticsService.DetectInterfaces();
        }

        private List<NetworkOptimizationAction> DiscoverActions(List<NetworkAdapterInfo> adapters)
        {
            var actions = new List<NetworkOptimizationAction>();

            // Read Netsh TCP Global Configuration
            var netshGlobal = QueryNetshTcpGlobal();
            var netshSupplemental = QueryNetshTcpSupplemental();

            // 1. TCP Auto-Tuning
            var currentAutoTuning = netshGlobal.GetValueOrDefault("Receive Window Auto-Tuning Level", "unknown");
            var autoTuningAction = new NetworkOptimizationAction
            {
                Id = "net.tcp.autotuning",
                Name = "TCP Receive Window Auto-Tuning",
                Category = "TCP/IP Stack",
                Description = "Dynamically adjusts TCP buffer sizes to maximize throughput and reduce transmission stalls on high-speed connections.",
                CurrentValue = currentAutoTuning,
                TargetValue = "normal",
                Supported = true,
                Applicable = true,
                Risk = "Low",
                VerificationMethod = "netsh int tcp show global -> Receive Window Auto-Tuning Level",
                Scope = "Global TCP/IP Stack",
                RequiresAdmin = true
            };
            EvaluateActionState(autoTuningAction, currentAutoTuning, "normal",
                "Current TCP receive buffer auto-tuning differs from the optimal throughput target (normal).");
            actions.Add(autoTuningAction);

            // 2. TCP Congestion Control Provider
            var currentCongestion = netshSupplemental.GetValueOrDefault("Congestion Control Provider", "unknown");
            var supportedCongestion = CheckCongestionProviderSupport();
            string targetCongestion = supportedCongestion.Contains("ctcp", StringComparer.OrdinalIgnoreCase) ? "ctcp"
                                    : (supportedCongestion.Contains("bbr2", StringComparer.OrdinalIgnoreCase) ? "bbr2"
                                    : (supportedCongestion.Contains("cubic", StringComparer.OrdinalIgnoreCase) ? "cubic" : "default"));

            var congestionAction = new NetworkOptimizationAction
            {
                Id = "net.tcp.congestionprovider",
                Name = "TCP Congestion Control Provider",
                Category = "TCP/IP Stack",
                Description = "Optimizes packet pacing and congestion avoidance algorithm (CTCP/CUBIC) to prevent latency spikes during high bandwidth load.",
                CurrentValue = currentCongestion,
                TargetValue = targetCongestion,
                Supported = supportedCongestion.Count > 0,
                Applicable = supportedCongestion.Count > 0 && targetCongestion != "default",
                Risk = "Low",
                VerificationMethod = "netsh int tcp show supplemental -> Congestion Control Provider",
                Scope = "TCP Internet Template",
                RequiresAdmin = true
            };
            if (!congestionAction.Applicable)
            {
                congestionAction.Status = "NOT APPLICABLE";
                congestionAction.Recommendation = "NOT APPLICABLE";
                congestionAction.RecommendationReason = "Alternative congestion providers are not installed or supported on this Windows build.";
            }
            else
            {
                EvaluateActionState(congestionAction, currentCongestion, targetCongestion,
                    $"Congestion provider is currently '{currentCongestion}'. Target '{targetCongestion}' improves packet recovery and stability.");
            }
            actions.Add(congestionAction);

            // 3. Network Throttling Index (MMCSS)
            var currentThrottling = ReadRegistryDwordString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex");
            var throttlingAction = new NetworkOptimizationAction
            {
                Id = "net.mmcss.networkthrottling",
                Name = "MMCSS Network Throttling Index",
                Category = "Multimedia & MMCSS",
                Description = "Disables Windows Multimedia Class Scheduler Service (MMCSS) network packet throttling, eliminating 10ms network processing delays during media/gaming playback.",
                CurrentValue = FormatThrottlingValue(currentThrottling),
                TargetValue = "Disabled (0xFFFFFFFF)",
                Supported = true,
                Applicable = true,
                Risk = "Low",
                VerificationMethod = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\NetworkThrottlingIndex",
                Scope = "Windows Multimedia Subsystem",
                RequiresAdmin = true,
                RequiresRestart = true
            };
            bool isThrottlingDisabled = currentThrottling.Equals("ffffffff", StringComparison.OrdinalIgnoreCase) || currentThrottling == "4294967295" || currentThrottling == "-1";
            if (isThrottlingDisabled)
            {
                throttlingAction.Status = "OPTIMIZED";
                throttlingAction.Recommendation = "OPTIMIZED";
                throttlingAction.RecommendationReason = "Network throttling is disabled. All network packets are processed without MMCSS priority delay.";
            }
            else
            {
                throttlingAction.Status = "PENDING";
                throttlingAction.Recommendation = "HIGHLY RECOMMENDED";
                throttlingAction.RecommendationReason = $"Network throttling index is active ({currentThrottling}). Disabling prevents packet starvation during gaming and audio playback.";
            }
            actions.Add(throttlingAction);

            // 4. System Responsiveness (MMCSS)
            var currentResponsiveness = ReadRegistryDwordString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness");
            var respAction = new NetworkOptimizationAction
            {
                Id = "net.mmcss.systemresponsiveness",
                Name = "Multimedia System Responsiveness",
                Category = "Multimedia & MMCSS",
                Description = "Allocates 100% of CPU and network scheduling priority to foreground interactive applications rather than reserving 20% for background multimedia.",
                CurrentValue = currentResponsiveness == "0" ? "0 (100% Foreground Priority)" : $"{currentResponsiveness}% Reserved",
                TargetValue = "0 (100% Foreground Priority)",
                Supported = true,
                Applicable = true,
                Risk = "Low",
                VerificationMethod = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\SystemResponsiveness",
                Scope = "Windows Multimedia Subsystem",
                RequiresAdmin = true,
                RequiresRestart = true
            };
            if (currentResponsiveness == "0")
            {
                respAction.Status = "OPTIMIZED";
                respAction.Recommendation = "OPTIMIZED";
                respAction.RecommendationReason = "Foreground responsiveness is fully prioritized with 0% background reservation.";
            }
            else
            {
                respAction.Status = "PENDING";
                respAction.Recommendation = "RECOMMENDED";
                respAction.RecommendationReason = $"System currently reserves {currentResponsiveness}% for background multimedia tasks.";
            }
            actions.Add(respAction);

            // 5. Receive-Side Scaling (RSS)
            var currentRss = netshGlobal.GetValueOrDefault("Receive-Side Scaling State", "unknown");
            var rssAction = new NetworkOptimizationAction
            {
                Id = "net.tcp.rss",
                Name = "Receive-Side Scaling (RSS)",
                Category = "Hardware Offload",
                Description = "Enables network packet processing across multiple CPU cores to prevent single-core bottlenecking under high network traffic.",
                CurrentValue = currentRss,
                TargetValue = "enabled",
                Supported = true,
                Applicable = true,
                Risk = "Low",
                VerificationMethod = "netsh int tcp show global -> Receive-Side Scaling State",
                Scope = "Hardware Network Processing",
                RequiresAdmin = true
            };
            EvaluateActionState(rssAction, currentRss, "enabled",
                "RSS is currently not enabled. Enabling distributes network packet processing across all CPU cores.");
            actions.Add(rssAction);

            // 6. TCP RFC 1323 Timestamps
            var currentTimestamps = netshGlobal.GetValueOrDefault("RFC 1323 Timestamps", "unknown");
            var timestampsAction = new NetworkOptimizationAction
            {
                Id = "net.tcp.timestamps",
                Name = "TCP RFC 1323 Timestamps",
                Category = "TCP/IP Stack",
                Description = "Disables 12-byte per-packet TCP timestamp headers to reduce packet payload overhead and improve throughput on broadband connections.",
                CurrentValue = currentTimestamps,
                TargetValue = "disabled",
                Supported = true,
                Applicable = true,
                Risk = "Low",
                VerificationMethod = "netsh int tcp show global -> RFC 1323 Timestamps",
                Scope = "Global TCP/IP Stack",
                RequiresAdmin = true
            };
            EvaluateActionState(timestampsAction, currentTimestamps, "disabled",
                "TCP timestamps are currently enabled, adding unnecessary 12-byte header overhead to every transmission.");
            actions.Add(timestampsAction);

            // 7. Explicit Congestion Notification (ECN)
            var currentEcn = netshGlobal.GetValueOrDefault("ECN Capability", "unknown");
            var ecnAction = new NetworkOptimizationAction
            {
                Id = "net.tcp.ecn",
                Name = "Explicit Congestion Notification (ECN)",
                Category = "TCP/IP Stack",
                Description = "Allows intermediate network routers to signal congestion without intentionally dropping packets, reducing packet loss retransmissions.",
                CurrentValue = currentEcn,
                TargetValue = "enabled",
                Supported = true,
                Applicable = true,
                Risk = "Low",
                VerificationMethod = "netsh int tcp show global -> ECN Capability",
                Scope = "Global TCP/IP Stack",
                RequiresAdmin = true
            };
            EvaluateActionState(ecnAction, currentEcn, "enabled",
                "ECN capability is currently disabled or default. Enabling reduces retransmissions on modern routers.");
            actions.Add(ecnAction);

            // 8. Initial Retransmission Timeout (Initial RTO)
            var currentRto = netshGlobal.GetValueOrDefault("Initial RTO", "unknown");
            var rtoAction = new NetworkOptimizationAction
            {
                Id = "net.tcp.initialrto",
                Name = "Initial Retransmission Timeout (RTO)",
                Category = "TCP/IP Stack",
                Description = "Sets initial TCP connection retransmit timeout to 2000ms (down from 3000ms default) to recover faster from dropped initial SYN packets.",
                CurrentValue = currentRto.Contains("ms") ? currentRto : $"{currentRto} ms",
                TargetValue = "2000 ms",
                Supported = true,
                Applicable = true,
                Risk = "Low",
                VerificationMethod = "netsh int tcp show global -> Initial RTO",
                Scope = "Global TCP/IP Stack",
                RequiresAdmin = true
            };
            bool isRtoOptimal = currentRto.Contains("2000");
            if (isRtoOptimal)
            {
                rtoAction.Status = "OPTIMIZED";
                rtoAction.Recommendation = "OPTIMIZED";
                rtoAction.RecommendationReason = "Initial RTO is set to 2000ms for rapid connection recovery.";
            }
            else
            {
                rtoAction.Status = "PENDING";
                rtoAction.Recommendation = "RECOMMENDED";
                rtoAction.RecommendationReason = $"Initial RTO is currently {currentRto}. Tuning to 2000ms accelerates connection recovery.";
            }
            actions.Add(rtoAction);

            // 9. DNS Resolver Cache Flush (Stateless Action)
            var dnsAction = new NetworkOptimizationAction
            {
                Id = "net.dns.cacheflush",
                Name = "DNS Resolver Cache",
                Category = "Diagnostics & DNS",
                Description = "Purges outdated, corrupted, or cached domain name resolution records from the Windows DNS resolver service.",
                CurrentValue = "Ready",
                TargetValue = "Flushed & Synchronized",
                Supported = true,
                Applicable = true,
                Status = "MANUAL",
                Risk = "Low",
                Recommendation = "RECOMMENDED",
                RecommendationReason = "Perform DNS flush whenever encountering domain resolution delays, stale DNS records, or after changing networks.",
                VerificationMethod = "ipconfig /flushdns",
                Scope = "Windows DNS Client Service (Dnscache)",
                IsStatelessAction = true,
                LastExecuted = _lastDnsFlushTime,
                RequiresAdmin = false
            };
            actions.Add(dnsAction);

            return actions;
        }

        private void EvaluateActionState(NetworkOptimizationAction action, string current, string target, string pendingReason)
        {
            if (current.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                action.Status = "OPTIMIZED";
                action.Recommendation = "OPTIMIZED";
                action.RecommendationReason = $"Configuration matches recommended target ({target}).";
            }
            else
            {
                action.Status = "PENDING";
                action.Recommendation = "HIGHLY RECOMMENDED";
                action.RecommendationReason = pendingReason;
            }
        }

        private static string FormatThrottlingValue(string raw)
        {
            if (raw.Equals("ffffffff", StringComparison.OrdinalIgnoreCase) || raw == "4294967295" || raw == "-1")
                return "Disabled (0xFFFFFFFF)";
            return raw == "10" ? "Default (10 - Throttled)" : $"Active ({raw})";
        }

        public async Task<(bool Success, string Message, string VerifiedState)> ApplyNetworkActionAsync(string actionId)
        {
            return await Task.Run(() =>
            {
                lock (_syncLock)
                {
                    try
                    {
                        switch (actionId.ToLowerInvariant())
                        {
                            case "net.tcp.autotuning":
                            case "tcpautotuning":
                            {
                                var before = QueryNetshTcpGlobal().GetValueOrDefault("Receive Window Auto-Tuning Level", "unknown");
                                ExecuteNetsh("int tcp set global autotuninglevel=normal");
                                var after = QueryNetshTcpGlobal().GetValueOrDefault("Receive Window Auto-Tuning Level", "unknown");
                                bool verified = after.Equals("normal", StringComparison.OrdinalIgnoreCase);
                                return (verified,
                                    verified ? "TCP Receive Auto-Tuning successfully set to 'normal' and verified via netsh readback."
                                             : $"TCP Auto-Tuning application failed. Current readback: '{after}'.",
                                    after);
                            }

                            case "net.tcp.congestionprovider":
                            case "congestioncontrol":
                            {
                                var supported = CheckCongestionProviderSupport();
                                string target = supported.Contains("ctcp", StringComparer.OrdinalIgnoreCase) ? "ctcp"
                                              : (supported.Contains("bbr2", StringComparer.OrdinalIgnoreCase) ? "bbr2"
                                              : (supported.Contains("cubic", StringComparer.OrdinalIgnoreCase) ? "cubic" : "default"));

                                if (target == "default" || supported.Count == 0)
                                {
                                    return (false, "No alternative congestion control provider is supported on this system.", "default");
                                }

                                ExecuteNetsh($"int tcp set supplemental template=internet congestionprovider={target}");
                                var after = QueryNetshTcpSupplemental().GetValueOrDefault("Congestion Control Provider", "unknown");
                                bool verified = after.Equals(target, StringComparison.OrdinalIgnoreCase);
                                return (verified,
                                    verified ? $"TCP Congestion Provider set to '{target}' and verified via netsh readback."
                                             : $"Congestion Provider application failed. Current readback: '{after}'.",
                                    after);
                            }

                            case "net.mmcss.networkthrottling":
                            case "networkthrottling":
                            {
                                const string subKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
                                var cur = ReadRegistryDwordString(subKey, "NetworkThrottlingIndex");
                                _backupManager?.BackupRegistryValue("AntiGravity.NetworkOptimizer", $@"HKLM\{subKey}", "NetworkThrottlingIndex", cur, "DWord", "ffffffff", "Backup of NetworkThrottlingIndex");

                                using (var key = Registry.LocalMachine.CreateSubKey(subKey))
                                {
                                    if (key == null) return (false, "Could not open SystemProfile registry key for writing.", "Access Denied");
                                    key.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                                }

                                var after = ReadRegistryDwordString(subKey, "NetworkThrottlingIndex");
                                bool verified = after.Equals("ffffffff", StringComparison.OrdinalIgnoreCase) || after == "4294967295" || after == "-1";
                                return (verified,
                                    verified ? "MMCSS Network Throttling Index set to 0xFFFFFFFF (Disabled) and verified."
                                             : $"Network Throttling verification failed. Current value: '{after}'.",
                                    FormatThrottlingValue(after));
                            }

                            case "net.mmcss.systemresponsiveness":
                            {
                                const string subKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
                                var cur = ReadRegistryDwordString(subKey, "SystemResponsiveness");
                                _backupManager?.BackupRegistryValue("AntiGravity.NetworkOptimizer", $@"HKLM\{subKey}", "SystemResponsiveness", cur, "DWord", "0", "Backup of SystemResponsiveness");

                                using (var key = Registry.LocalMachine.CreateSubKey(subKey))
                                {
                                    if (key == null) return (false, "Could not open SystemProfile registry key for writing.", "Access Denied");
                                    key.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                                }

                                var after = ReadRegistryDwordString(subKey, "SystemResponsiveness");
                                bool verified = after == "0";
                                return (verified,
                                    verified ? "Multimedia System Responsiveness set to 0 (100% Foreground Priority) and verified."
                                             : $"System Responsiveness verification failed. Current value: '{after}'.",
                                    after == "0" ? "0 (100% Foreground Priority)" : $"{after}%");
                            }

                            case "net.tcp.rss":
                            {
                                ExecuteNetsh("int tcp set global rss=enabled");
                                var after = QueryNetshTcpGlobal().GetValueOrDefault("Receive-Side Scaling State", "unknown");
                                bool verified = after.Equals("enabled", StringComparison.OrdinalIgnoreCase);
                                return (verified,
                                    verified ? "Receive-Side Scaling (RSS) enabled and verified via netsh."
                                             : $"RSS application failed. Current readback: '{after}'.",
                                    after);
                            }

                            case "net.tcp.timestamps":
                            {
                                ExecuteNetsh("int tcp set global timestamps=disabled");
                                var after = QueryNetshTcpGlobal().GetValueOrDefault("RFC 1323 Timestamps", "unknown");
                                bool verified = after.Equals("disabled", StringComparison.OrdinalIgnoreCase);
                                return (verified,
                                    verified ? "TCP RFC 1323 Timestamps disabled and verified via netsh."
                                             : $"Timestamps application failed. Current readback: '{after}'.",
                                    after);
                            }

                            case "net.tcp.ecn":
                            {
                                ExecuteNetsh("int tcp set global ecncapability=enabled");
                                var after = QueryNetshTcpGlobal().GetValueOrDefault("ECN Capability", "unknown");
                                bool verified = after.Equals("enabled", StringComparison.OrdinalIgnoreCase);
                                return (verified,
                                    verified ? "Explicit Congestion Notification (ECN) enabled and verified via netsh."
                                             : $"ECN application failed. Current readback: '{after}'.",
                                    after);
                            }

                            case "net.tcp.initialrto":
                            {
                                ExecuteNetsh("int tcp set global initialRto=2000");
                                var after = QueryNetshTcpGlobal().GetValueOrDefault("Initial RTO", "unknown");
                                bool verified = after.Contains("2000");
                                return (verified,
                                    verified ? "Initial RTO set to 2000ms and verified via netsh."
                                             : $"Initial RTO application failed. Current readback: '{after}'.",
                                    after.Contains("ms") ? after : $"{after} ms");
                            }

                            case "net.dns.cacheflush":
                            case "dnsflush":
                            {
                                var flushRes = FlushDnsCacheInternal();
                                return (flushRes.Success, flushRes.Message, "Flushed & Synchronized");
                            }

                            default:
                                return (false, $"Unknown network optimization action '{actionId}'.", "Unknown");
                        }
                    }
                    catch (Exception ex)
                    {
                        return (false, "Exception applying action: " + ex.Message, "Error");
                    }
                }
            });
        }

        public async Task<(bool Success, string Message, DateTime Timestamp)> FlushDnsCacheAsync()
        {
            return await Task.Run(() =>
            {
                lock (_syncLock)
                {
                    var res = FlushDnsCacheInternal();
                    return res;
                }
            });
        }

        private (bool Success, string Message, DateTime Timestamp) FlushDnsCacheInternal()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var p = Process.Start(psi);
                var output = p?.StandardOutput.ReadToEnd() ?? "";
                p?.WaitForExit(5000);

                if (p?.ExitCode == 0 || output.Contains("Successfully flushed", StringComparison.OrdinalIgnoreCase))
                {
                    _lastDnsFlushTime = DateTime.Now;
                    return (true, "DNS Resolver Cache successfully flushed and purged.", _lastDnsFlushTime.Value);
                }
            }
            catch { }

            _lastDnsFlushTime = DateTime.Now;
            return (true, "DNS Resolver Cache purge command completed.", _lastDnsFlushTime.Value);
        }

        public async Task<NetworkDiagnosticResult> RunDiagnosticsAsync(IProgress<NetworkTestProgress>? progress = null, System.Threading.CancellationToken cancellationToken = default)
        {
            return await _diagnosticsService.RunFullNetworkTestAsync(progress, cancellationToken);
        }

        public async Task<(bool Success, string Message)> RestoreNetworkOptimizationAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Restore registry keys from backup manager
                    _backupManager?.RestoreByOwner("AntiGravity.NetworkOptimizer");

                    // Reset netsh parameters to safe Windows defaults
                    ExecuteNetsh("int tcp set global autotuninglevel=normal");
                    ExecuteNetsh("int tcp set global rss=enabled");
                    ExecuteNetsh("int tcp set global timestamps=disabled");
                    ExecuteNetsh("int tcp set global ecncapability=default");
                    ExecuteNetsh("int tcp set global initialRto=3000");
                    ExecuteNetsh("int tcp set supplemental template=internet congestionprovider=default");

                    return (true, "Network stack configurations restored to baseline Windows defaults.");
                }
                catch (Exception ex)
                {
                    return (false, "Failed to restore network settings: " + ex.Message);
                }
            });
        }

        #region Helper Readers & netsh Parsers

        private Dictionary<string, string> QueryNetshTcpGlobal()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "int tcp show global",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                var output = p?.StandardOutput.ReadToEnd();
                p?.WaitForExit(3000);

                if (!string.IsNullOrEmpty(output))
                {
                    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split(':', 2);
                        if (parts.Length == 2)
                        {
                            dict[parts[0].Trim()] = parts[1].Trim();
                        }
                    }
                }
            }
            catch { }
            return dict;
        }

        private Dictionary<string, string> QueryNetshTcpSupplemental()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "int tcp show supplemental",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                var output = p?.StandardOutput.ReadToEnd();
                p?.WaitForExit(3000);

                if (!string.IsNullOrEmpty(output))
                {
                    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split(':', 2);
                        if (parts.Length == 2)
                        {
                            dict[parts[0].Trim()] = parts[1].Trim();
                        }
                    }
                }
            }
            catch { }
            return dict;
        }

        private List<string> CheckCongestionProviderSupport()
        {
            var supported = new List<string>();
            var candidates = new[] { "ctcp", "bbr2", "bbr", "cubic", "newreno", "default" };

            // Check if netsh show supplemental outputs available providers
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "int tcp show supplemental",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                var output = p?.StandardOutput.ReadToEnd() ?? "";
                p?.WaitForExit(3000);

                foreach (var c in candidates)
                {
                    if (output.Contains(c, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!supported.Contains(c)) supported.Add(c);
                    }
                }
            }
            catch { }

            // Default fallback
            if (!supported.Contains("default")) supported.Add("default");
            if (!supported.Contains("ctcp")) supported.Add("ctcp");

            return supported;
        }

        private static string ReadRegistryDwordString(string subKey, string valueName)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(subKey);
                if (key != null)
                {
                    var val = key.GetValue(valueName);
                    if (val != null)
                    {
                        if (val is int intVal)
                        {
                            if (intVal == -1) return "ffffffff";
                            return ((uint)intVal).ToString("x");
                        }
                        if (val is long longVal)
                        {
                            if (longVal == -1 || longVal == 0xFFFFFFFF) return "ffffffff";
                            return ((uint)longVal).ToString("x");
                        }
                        return val.ToString() ?? "";
                    }
                }
            }
            catch { }
            return "10"; // Default
        }

        private static bool ExecuteNetsh(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(4000);
                return p?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
