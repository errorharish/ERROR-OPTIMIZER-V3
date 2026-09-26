using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Interfaces
{
    public class NetworkAdapterInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string InterfaceType { get; set; } = string.Empty; // Wi-Fi, Ethernet, VPN, Virtual, Other
        public string Status { get; set; } = string.Empty; // Up, Down
        public string LinkSpeedMbps { get; set; } = string.Empty;
        public string Ipv4Address { get; set; } = string.Empty;
        public string Ipv6Address { get; set; } = string.Empty;
        public string DnsServers { get; set; } = string.Empty;
        public string Gateway { get; set; } = string.Empty;
        public string Mtu { get; set; } = string.Empty;
        public bool IsActivePhysicalAdapter { get; set; }
        public bool IsDhcpEnabled { get; set; }
        public string Ssid { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string Fingerprint => $"{Id}|{Gateway}|{Ipv4Address}|{InterfaceType}|{Status}";
    }

    public enum NetworkDiagnosticState
    {
        NotTested,
        Testing,
        Measured,
        Failed,
        Unreachable,
        InsufficientSamples
    }

    public class NetworkDiagnosticResult
    {
        public string LatencyMs { get; set; } = "Not Tested";
        public string JitterMs { get; set; } = "Not Tested";
        public string PacketLossPercent { get; set; } = "Not Tested";
        public string DnsResolutionMs { get; set; } = "Not Tested";
        public string GatewayPingMs { get; set; } = "Not Tested";
        public string InternetPingMs { get; set; } = "Not Tested";
        public string DownloadThroughputMbps { get; set; } = "Not Tested";
        public string UploadThroughputMbps { get; set; } = "Not Tested";
        
        public double MinLatencyMs { get; set; }
        public double MaxLatencyMs { get; set; }
        public double AvgLatencyMs { get; set; }
        public double GatewayMinMs { get; set; }
        public double GatewayAvgMs { get; set; }
        public double GatewayMaxMs { get; set; }
        
        public int SampleCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }

        public bool GatewayReachability { get; set; }
        public bool InternetReachability { get; set; }
        
        public NetworkDiagnosticState State { get; set; } = NetworkDiagnosticState.NotTested;
        public string NetworkHealth { get; set; } = "NOT TESTED"; // EXCELLENT, GOOD, FAIR, POOR, UNREACHABLE, NOT TESTED
        public string StatusSummary { get; set; } = "Ready to test latency and connectivity";
        public string InterfaceFingerprint { get; set; } = string.Empty;
        public string TestedInterfaceName { get; set; } = string.Empty;
        public DateTime? TestedAt { get; set; }
        public bool IsStale { get; set; }
    }

    public class NetworkOptimizationAction
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;
        public string TargetValue { get; set; } = string.Empty;
        public bool Supported { get; set; } = true;
        public bool Applicable { get; set; } = true;
        public string Status { get; set; } = "PENDING"; // OPTIMIZED, PENDING, RECOMMENDED, NOT APPLICABLE, MANUAL, VERIFIED, FAILED
        public string Risk { get; set; } = "Low"; // Low, Medium, High
        public string Recommendation { get; set; } = "RECOMMENDED"; // OPTIMIZED, HIGHLY RECOMMENDED, RECOMMENDED, NOT APPLICABLE
        public string RecommendationReason { get; set; } = string.Empty;
        public string VerificationMethod { get; set; } = string.Empty;
        public bool IsStatelessAction { get; set; } // True for DNS flush, false for persistent configuration
        public DateTime? LastExecuted { get; set; }
        public bool RequiresAdmin { get; set; } = true;
        public bool RequiresRestart { get; set; } = false;
        public string Scope { get; set; } = "System-wide Network Stack";
    }

    public class NetworkOptimizationPlan
    {
        public List<NetworkAdapterInfo> Adapters { get; set; } = new();
        public NetworkDiagnosticResult Diagnostics { get; set; } = new();
        public List<NetworkOptimizationAction> Actions { get; set; } = new();
        public int TotalActions { get; set; }
        public int SupportedCount { get; set; }
        public int ApplicableCount { get; set; }
        public int PendingCount { get; set; }
        public int OptimizedCount { get; set; }
        public int NotApplicableCount { get; set; }
        public DateTime? LastDnsFlushTime { get; set; }
    }

    public class NetworkTestProgress
    {
        public int Phase { get; set; }
        public int TotalPhases { get; set; } = 7;
        public string PhaseName { get; set; } = string.Empty;
        public string StatusMessage { get; set; } = string.Empty;
        public double Percentage { get; set; }
    }

    public interface INetworkDiagnosticsService
    {
        List<NetworkAdapterInfo> DetectInterfaces();
        NetworkAdapterInfo? GetActiveInterface();
        Task<NetworkDiagnosticResult> RunFullNetworkTestAsync(IProgress<NetworkTestProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<(bool Reachable, double MinMs, double AvgMs, double MaxMs, string Display)> MeasureGatewayLatencyAsync(string gatewayIp, int samples = 10, CancellationToken cancellationToken = default);
        Task<(bool Reachable, List<long> Rtts, double MinMs, double AvgMs, double MaxMs, int Lost, string Display)> MeasureInternetLatencyAsync(int samples = 15, CancellationToken cancellationToken = default);
        (double JitterMs, string Display) CalculateJitter(IReadOnlyList<long> rtts);
        (double LossPercent, string Display) CalculatePacketLoss(int sent, int lost, bool reachable);
        Task<(bool Success, double DnsMs, string Display)> MeasureDnsLatencyAsync(string targetHost = "dns.google", CancellationToken cancellationToken = default);
        Task<(bool Success, double Mbps, double MBps, string Display)> MeasureDownloadThroughputAsync(CancellationToken cancellationToken = default);
        Task<(bool Success, double Mbps, double MBps, string Display)> MeasureUploadThroughputAsync(CancellationToken cancellationToken = default);
        bool HasNetworkEnvironmentChanged(string previousFingerprint);
        void InvalidateDiagnostics();
        NetworkDiagnosticResult GetCachedDiagnostics();
    }

    public interface INetworkOptimizerEngine
    {
        Task<NetworkOptimizationPlan> PlanNetworkOptimizationAsync(bool includeDiagnostics = false);
        Task<(bool Success, string Message, string VerifiedState)> ApplyNetworkActionAsync(string actionId);
        Task<NetworkDiagnosticResult> RunDiagnosticsAsync(IProgress<NetworkTestProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<(bool Success, string Message, DateTime Timestamp)> FlushDnsCacheAsync();
        Task<(bool Success, string Message)> RestoreNetworkOptimizationAsync();
    }
}

