using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Core.Implementations
{
    public class NetworkDiagnosticsService : INetworkDiagnosticsService
    {
        private static readonly object _cacheLock = new object();
        private static NetworkDiagnosticResult _cachedResult = new NetworkDiagnosticResult();
        private static string _lastFingerprint = string.Empty;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public List<NetworkAdapterInfo> DetectInterfaces()
        {
            var list = new List<NetworkAdapterInfo>();
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var info = new NetworkAdapterInfo
                    {
                        Id = ni.Id,
                        Name = ni.Name,
                        Description = ni.Description,
                        Status = ni.OperationalStatus == OperationalStatus.Up ? "Up" : "Down",
                        LinkSpeedMbps = ni.Speed > 0 ? $"{ni.Speed / 1_000_000} Mbps" : "Unknown",
                        MacAddress = ni.GetPhysicalAddress()?.ToString() ?? string.Empty
                    };

                    // Interface Type Detection
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                        info.InterfaceType = "Wi-Fi";
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                        info.InterfaceType = "Ethernet";
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ppp || ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase))
                        info.InterfaceType = "VPN";
                    else if (ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || ni.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) || ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
                        info.InterfaceType = "Virtual";
                    else
                        info.InterfaceType = ni.NetworkInterfaceType.ToString();

                    try
                    {
                        var ipProps = ni.GetIPProperties();

                        var ipv4 = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                        if (ipv4 != null) info.Ipv4Address = ipv4.Address.ToString();

                        var ipv6 = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6);
                        if (ipv6 != null) info.Ipv6Address = ipv6.Address.ToString();

                        var dnsList = ipProps.DnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork).Select(d => d.ToString()).ToList();
                        info.DnsServers = dnsList.Count > 0 ? string.Join(", ", dnsList) : "Default DHCP";

                        var gw = ipProps.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                        if (gw != null && !gw.Address.Equals(IPAddress.Any))
                        {
                            info.Gateway = gw.Address.ToString();
                        }

                        var ipv4Props = ipProps.GetIPv4Properties();
                        if (ipv4Props != null)
                        {
                            info.Mtu = $"{ipv4Props.Mtu} Bytes";
                            info.IsDhcpEnabled = ipv4Props.IsDhcpEnabled;
                        }

                        // Determine active physical adapter
                        if (ni.OperationalStatus == OperationalStatus.Up &&
                            !string.IsNullOrEmpty(info.Gateway) &&
                            !string.IsNullOrEmpty(info.Ipv4Address) &&
                            info.InterfaceType != "Virtual" &&
                            info.InterfaceType != "VPN")
                        {
                            info.IsActivePhysicalAdapter = true;
                        }
                    }
                    catch { }

                    list.Add(info);
                }

                // Fallback if none explicitly tagged
                if (!list.Any(a => a.IsActivePhysicalAdapter))
                {
                    var fallback = list.FirstOrDefault(a => a.Status == "Up" && !string.IsNullOrEmpty(a.Ipv4Address));
                    if (fallback != null) fallback.IsActivePhysicalAdapter = true;
                }
            }
            catch { }

            return list;
        }

        public NetworkAdapterInfo? GetActiveInterface()
        {
            var adapters = DetectInterfaces();
            return adapters.FirstOrDefault(a => a.IsActivePhysicalAdapter) ?? adapters.FirstOrDefault(a => a.Status == "Up");
        }

        public bool HasNetworkEnvironmentChanged(string previousFingerprint)
        {
            if (string.IsNullOrEmpty(previousFingerprint)) return false;
            var active = GetActiveInterface();
            if (active == null) return true;
            return !string.Equals(active.Fingerprint, previousFingerprint, StringComparison.OrdinalIgnoreCase);
        }

        public void InvalidateDiagnostics()
        {
            lock (_cacheLock)
            {
                _cachedResult = new NetworkDiagnosticResult
                {
                    LatencyMs = "Not Tested",
                    JitterMs = "Not Tested",
                    PacketLossPercent = "Not Tested",
                    DnsResolutionMs = "Not Tested",
                    GatewayPingMs = "Not Tested",
                    InternetPingMs = "Not Tested",
                    DownloadThroughputMbps = "Not Tested",
                    UploadThroughputMbps = "Not Tested",
                    State = NetworkDiagnosticState.NotTested,
                    NetworkHealth = "NOT TESTED",
                    StatusSummary = "Ready to test latency and connectivity",
                    IsStale = true
                };
            }
        }

        public NetworkDiagnosticResult GetCachedDiagnostics()
        {
            lock (_cacheLock)
            {
                var active = GetActiveInterface();
                if (active != null && !string.IsNullOrEmpty(_lastFingerprint) && HasNetworkEnvironmentChanged(_lastFingerprint))
                {
                    InvalidateDiagnostics();
                }
                return _cachedResult;
            }
        }

        public async Task<(bool Reachable, double MinMs, double AvgMs, double MaxMs, string Display)> MeasureGatewayLatencyAsync(string gatewayIp, int samples = 10, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(gatewayIp) || gatewayIp == "N/A" || gatewayIp == "0.0.0.0")
            {
                return (false, 0, 0, 0, "UNREACHABLE (No Gateway)");
            }

            var rtts = new List<long>();
            int maxProbes = Math.Max(3, Math.Min(samples, 20));

            for (int i = 0; i < maxProbes; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(gatewayIp, 800);
                    if (reply.Status == IPStatus.Success)
                    {
                        rtts.Add(reply.RoundtripTime);
                    }
                }
                catch { }

                try
                {
                    await Task.Delay(30, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (rtts.Count == 0)
            {
                return (false, 0, 0, 0, "UNREACHABLE");
            }

            double min = rtts.Min();
            double max = rtts.Max();
            double avg = rtts.Average();

            string display = $"{avg:F1} ms ({min:F0} min / {max:F0} max)";
            return (true, min, avg, max, display);
        }

        public async Task<(bool Reachable, List<long> Rtts, double MinMs, double AvgMs, double MaxMs, int Lost, string Display)> MeasureInternetLatencyAsync(int samples = 15, CancellationToken cancellationToken = default)
        {
            string[] endpoints = new[] { "1.1.1.1", "8.8.8.8", "9.9.9.9", "208.67.222.222" };
            var rtts = new List<long>();
            int totalSent = 0;
            int totalLost = 0;
            int probes = Math.Max(5, Math.Min(samples, 25));

            for (int i = 0; i < probes; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                string target = endpoints[i % endpoints.Length];
                totalSent++;

                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(target, 1200);
                    if (reply.Status == IPStatus.Success)
                    {
                        rtts.Add(reply.RoundtripTime);
                    }
                    else
                    {
                        totalLost++;
                    }
                }
                catch
                {
                    totalLost++;
                }

                try
                {
                    await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (rtts.Count == 0)
            {
                return (false, rtts, 0, 0, 0, totalLost, "UNREACHABLE");
            }

            double min = rtts.Min();
            double max = rtts.Max();
            double avg = rtts.Average();

            string display = $"{avg:F1} ms ({min:F0} min / {max:F0} max)";
            return (true, rtts, min, avg, max, totalLost, display);
        }

        public (double JitterMs, string Display) CalculateJitter(IReadOnlyList<long> rtts)
        {
            if (rtts == null || rtts.Count < 2)
            {
                return (0, "INSUFFICIENT SAMPLES");
            }

            double sumDiff = 0;
            for (int i = 1; i < rtts.Count; i++)
            {
                sumDiff += Math.Abs(rtts[i] - rtts[i - 1]);
            }
            double jitter = sumDiff / (rtts.Count - 1);
            return (jitter, $"{jitter:F1} ms");
        }

        public (double LossPercent, string Display) CalculatePacketLoss(int sent, int lost, bool reachable)
        {
            if (!reachable && sent == lost)
            {
                return (100.0, "UNREACHABLE");
            }

            if (sent <= 0) return (0, "0%");
            double pct = (lost * 100.0) / sent;
            return (pct, $"{pct:F0}%");
        }

        public async Task<(bool Success, double DnsMs, string Display)> MeasureDnsLatencyAsync(string targetHost = "dns.google", CancellationToken cancellationToken = default)
        {
            string[] testHosts = new[] { targetHost, "cloudflare.com", "microsoft.com" };
            var times = new List<double>();

            foreach (var host in testHosts)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    var sw = Stopwatch.StartNew();
                    var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
                    sw.Stop();

                    if (addresses != null && addresses.Length > 0)
                    {
                        times.Add(sw.Elapsed.TotalMilliseconds);
                    }
                }
                catch { }
            }

            if (times.Count == 0)
            {
                return (false, 0, "FAILED");
            }

            double avg = times.Average();
            return (true, avg, $"{avg:F1} ms");
        }

        public async Task<(bool Success, double Mbps, double MBps, string Display)> MeasureDownloadThroughputAsync(CancellationToken cancellationToken = default)
        {
            string[] probeUrls = new[]
            {
                "http://speed.cloudflare.com/__down?bytes=5000000",
                "https://www.google.com/favicon.ico"
            };

            foreach (var url in probeUrls)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(3.5));

                    var sw = Stopwatch.StartNew();
                    var resp = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
                        var buffer = new byte[16384];
                        long totalBytes = 0;
                        int read;

                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false)) > 0)
                        {
                            totalBytes += read;
                            if (sw.ElapsedMilliseconds >= 2500) break;
                        }

                        sw.Stop();
                        double seconds = sw.Elapsed.TotalSeconds;
                        if (seconds > 0.1 && totalBytes > 20000)
                        {
                            double mbps = (totalBytes * 8.0 / 1_000_000.0) / seconds;
                            double mbpsByte = (totalBytes / 1_000_000.0) / seconds;
                            return (true, mbps, mbpsByte, $"{mbps:F1} Mbps ({mbpsByte:F1} MB/s)");
                        }
                    }
                }
                catch { }
            }

            return (false, 0, 0, "MEASUREMENT UNAVAILABLE");
        }

        public async Task<(bool Success, double Mbps, double MBps, string Display)> MeasureUploadThroughputAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(3.0));

                // 256KB bounded payload
                byte[] uploadData = new byte[262144];
                new Random().NextBytes(uploadData);

                var content = new ByteArrayContent(uploadData);
                var sw = Stopwatch.StartNew();
                var resp = await _httpClient.PostAsync("http://speed.cloudflare.com/__up", content, cts.Token).ConfigureAwait(false);
                sw.Stop();

                double seconds = sw.Elapsed.TotalSeconds;
                if (resp.IsSuccessStatusCode && seconds > 0.05)
                {
                    double mbps = (uploadData.Length * 8.0 / 1_000_000.0) / seconds;
                    double mbpsByte = (uploadData.Length / 1_000_000.0) / seconds;
                    return (true, mbps, mbpsByte, $"{mbps:F1} Mbps ({mbpsByte:F1} MB/s)");
                }
            }
            catch { }

            return (false, 0, 0, "MEASUREMENT UNAVAILABLE");
        }

        public async Task<NetworkDiagnosticResult> RunFullNetworkTestAsync(IProgress<NetworkTestProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = new NetworkDiagnosticResult
            {
                TestedAt = DateTime.Now,
                State = NetworkDiagnosticState.Testing
            };

            // Phase 1: Detect Interface
            progress?.Report(new NetworkTestProgress { Phase = 1, Percentage = 10, PhaseName = "Interface Detection", StatusMessage = "Detecting active network adapter & configuration..." });
            var active = GetActiveInterface();
            if (active != null)
            {
                result.TestedInterfaceName = $"{active.Name} ({active.InterfaceType})";
                result.InterfaceFingerprint = active.Fingerprint;
            }
            else
            {
                result.TestedInterfaceName = "No Active Adapter";
            }

            // Phase 2: Gateway Latency
            progress?.Report(new NetworkTestProgress { Phase = 2, Percentage = 25, PhaseName = "Gateway Latency", StatusMessage = $"Measuring gateway ICMP response ({active?.Gateway ?? "None"})..." });
            var (gwReachable, gwMin, gwAvg, gwMax, gwDisplay) = await MeasureGatewayLatencyAsync(active?.Gateway ?? "", 10, cancellationToken).ConfigureAwait(false);
            result.GatewayReachability = gwReachable;
            result.GatewayMinMs = gwMin;
            result.GatewayAvgMs = gwAvg;
            result.GatewayMaxMs = gwMax;
            result.GatewayPingMs = gwDisplay;

            // Phase 3: Internet Latency
            progress?.Report(new NetworkTestProgress { Phase = 3, Percentage = 40, PhaseName = "Internet Latency", StatusMessage = "Probing multi-endpoint internet anycast latency..." });
            var (netReachable, rtts, netMin, netAvg, netMax, lost, netDisplay) = await MeasureInternetLatencyAsync(15, cancellationToken).ConfigureAwait(false);
            result.InternetReachability = netReachable;
            result.MinLatencyMs = netMin;
            result.AvgLatencyMs = netAvg;
            result.MaxLatencyMs = netMax;
            result.InternetPingMs = netDisplay;
            result.LatencyMs = netReachable ? $"{netAvg:F1} ms" : "UNREACHABLE";
            result.SampleCount = 15;
            result.SuccessCount = rtts.Count;
            result.FailureCount = lost;

            // Phase 4: Jitter & Packet Loss
            progress?.Report(new NetworkTestProgress { Phase = 4, Percentage = 55, PhaseName = "Jitter & Packet Loss", StatusMessage = "Calculating packet variance and transmission loss..." });
            var (jitterMs, jitterDisplay) = CalculateJitter(rtts);
            result.JitterMs = jitterDisplay;

            var (lossPct, lossDisplay) = CalculatePacketLoss(15, lost, netReachable);
            result.PacketLossPercent = lossDisplay;

            // Phase 5: DNS Resolution Time
            progress?.Report(new NetworkTestProgress { Phase = 5, Percentage = 70, PhaseName = "DNS Resolution", StatusMessage = "Measuring authoritative DNS resolution speed..." });
            var (dnsSuccess, dnsMs, dnsDisplay) = await MeasureDnsLatencyAsync("dns.google", cancellationToken).ConfigureAwait(false);
            result.DnsResolutionMs = dnsDisplay;

            // Phase 6: Download & Upload Throughput
            progress?.Report(new NetworkTestProgress { Phase = 6, Percentage = 85, PhaseName = "Throughput Probe", StatusMessage = "Measuring bounded real HTTP download & upload speed..." });
            var (downOk, downMbps, downMBps, downDisplay) = await MeasureDownloadThroughputAsync(cancellationToken).ConfigureAwait(false);
            result.DownloadThroughputMbps = downOk ? downDisplay : "MEASUREMENT UNAVAILABLE";

            var (upOk, upMbps, upMBps, upDisplay) = await MeasureUploadThroughputAsync(cancellationToken).ConfigureAwait(false);
            result.UploadThroughputMbps = upOk ? upDisplay : "MEASUREMENT UNAVAILABLE";

            // Phase 7: Evaluate Health
            progress?.Report(new NetworkTestProgress { Phase = 7, Percentage = 100, PhaseName = "Finalizing", StatusMessage = "Finalizing network health diagnostics..." });
            
            if (!netReachable && !gwReachable)
            {
                result.NetworkHealth = "DISCONNECTED";
                result.StatusSummary = "No local network or internet connection detected.";
                result.State = NetworkDiagnosticState.Unreachable;
            }
            else if (!netReachable)
            {
                result.NetworkHealth = "UNREACHABLE";
                result.StatusSummary = "Connected to local gateway, but internet is unreachable.";
                result.State = NetworkDiagnosticState.Unreachable;
            }
            else if (lossPct == 0 && netAvg <= 35 && jitterMs <= 4 && dnsMs <= 35)
            {
                result.NetworkHealth = "HEALTHY";
                result.StatusSummary = "Optimal network health. Low latency, jitter-free with 0% loss.";
                result.State = NetworkDiagnosticState.Measured;
            }
            else if (lossPct <= 2 && netAvg <= 75 && jitterMs <= 15)
            {
                result.NetworkHealth = "GOOD";
                result.StatusSummary = "Good network health. Stable connection suitable for real-time tasks.";
                result.State = NetworkDiagnosticState.Measured;
            }
            else if (lossPct <= 5 || netAvg <= 150)
            {
                result.NetworkHealth = "DEGRADED";
                result.StatusSummary = "Network performance degraded. Latency or variance is elevated.";
                result.State = NetworkDiagnosticState.Measured;
            }
            else
            {
                result.NetworkHealth = "POOR";
                result.StatusSummary = "High packet loss or extreme jitter detected on this connection.";
                result.State = NetworkDiagnosticState.Measured;
            }

            result.IsStale = false;

            lock (_cacheLock)
            {
                _cachedResult = result;
                _lastFingerprint = result.InterfaceFingerprint;
            }

            return result;
        }
    }
}
