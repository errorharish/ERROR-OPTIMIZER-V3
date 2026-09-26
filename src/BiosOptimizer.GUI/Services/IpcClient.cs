using System;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.IPC.Contracts;

namespace BiosOptimizer.GUI.Services
{
    public enum IpcConnectionState
    {
        Connecting,
        Connected,
        Degraded,
        Offline
    }

    public interface IIpcClient
    {
        Task<IpcResponse> SendRequestAsync(IpcMessageType type, string payload = null,
            CancellationToken ct = default);
        System.Collections.Generic.IAsyncEnumerable<IpcResponse> SendStreamingRequestAsync(IpcMessageType type, string payload = null,
            CancellationToken ct = default);
        bool IsServiceAvailable { get; }
        IpcConnectionState ConnectionState { get; }
        string ServiceBuildId { get; }
        event Action<bool> ServiceAvailabilityChanged;
        event Action<IpcConnectionState> ConnectionStateChanged;

        /// <summary>
        /// Waits until the backend pipe is actually accepting connections and
        /// a real GetVersion handshake succeeds. Returns true when ready.
        /// </summary>
        Task<bool> WaitForReadyAsync(TimeSpan timeout, CancellationToken ct = default);
        void SetConnectionState(IpcConnectionState state);
    }

    public class IpcClient : IIpcClient
    {
        private string PipeName
        {
            get
            {
                var app = System.Windows.Application.Current as App;
                return $"BiosOptimizer_IPC_v2_{app?.BackendManager?.SessionId ?? "default"}";
            }
        }

        private const int DefaultRequestTimeoutMs = 10000;
        private const int ConnectTimeoutMs = 3000;

        private static int GetRequestTimeoutMs(IpcMessageType type)
        {
            return type switch
            {
                IpcMessageType.ExecuteServicingTool => 7200 * 1000, // 2 hours safety limit for DISM/SFC
                IpcMessageType.CreateRestorePoint => 120 * 1000, // 2 minutes for VSS snapshot creation
                IpcMessageType.EnableSystemProtection => 60 * 1000, // 1 minute for SPP initialization
                IpcMessageType.DisableSystemProtection => 60 * 1000,
                IpcMessageType.ApplyStorageCleanup => 300 * 1000, // 5 minutes
                IpcMessageType.ApplyCleaner => 60 * 1000,
                IpcMessageType.EmptyRecycleBin => 60 * 1000,
                _ => DefaultRequestTimeoutMs
            };
        }

        private bool _serviceAvailable = false;
        private IpcConnectionState _connectionState = IpcConnectionState.Connecting;
        private string _serviceBuildId = "Unknown";
        private readonly object _stateLock = new();

        public bool IsServiceAvailable
        {
            get => _serviceAvailable;
            private set
            {
                if (_serviceAvailable != value)
                {
                    _serviceAvailable = value;
                    ServiceAvailabilityChanged?.Invoke(value);
                }
            }
        }

        public IpcConnectionState ConnectionState
        {
            get => _connectionState;
            private set
            {
                bool stateChanged = false;
                lock (_stateLock)
                {
                    if (_connectionState != value)
                    {
                        _connectionState = value;
                        _serviceAvailable = (value == IpcConnectionState.Connected);
                        stateChanged = true;
                    }
                }

                if (stateChanged)
                {
                    ServiceAvailabilityChanged?.Invoke(_serviceAvailable);
                    ConnectionStateChanged?.Invoke(value);
                    if (value == IpcConnectionState.Connected)
                    {
                        OptimizationStateCoordinator.NotifyOptimizationStateChanged();
                    }
                }
            }
        }

        public void SetConnectionState(IpcConnectionState state)
        {
            ConnectionState = state;
        }

        public string ServiceBuildId => _serviceBuildId;
        public event Action<bool> ServiceAvailabilityChanged;
        public event Action<IpcConnectionState> ConnectionStateChanged;

        // ──────────────────────────────────────────────────────────────
        // Health / readiness polling
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Polls until the pipe answers a GetVersion request with a valid v2 handshake,
        /// or until <paramref name="timeout"/> elapses.
        /// This is the authoritative "Service Connected" gate.
        /// </summary>
        public async Task<bool> WaitForReadyAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var res = await SendRequestAsync(IpcMessageType.GetVersion, "", cts.Token);
                    if (res.Success && !string.IsNullOrEmpty(res.Data))
                    {
                        var hs = JsonSerializer.Deserialize<IpcHandshakeResponse>(res.Data);
                        var app = System.Windows.Application.Current as App;
                        var bm = app?.BackendManager;
                        if (hs?.ProtocolVersion == "v2" && (bm == null || string.IsNullOrEmpty(bm.SessionId) || hs.SessionId == bm.SessionId))
                        {
                            var attr = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
                            var guiBuildId = attr.Length > 0 ? ((System.Reflection.AssemblyInformationalVersionAttribute)attr[0]).InformationalVersion : "Unknown";
                            
                            if (!AreBuildIdsCompatible(guiBuildId, hs.BuildId))
                            {
                                SetConnectionState(IpcConnectionState.Offline);
                                return false; // Handshake fails due to version mismatch
                            }
                            
                            _serviceBuildId = hs.BuildId;
                            SetConnectionState(IpcConnectionState.Connected);
                            return true;
                        }
                    }
                }
                catch { /* pipe not ready yet */ }

                try { await Task.Delay(500, cts.Token); } catch { break; }
            }

            SetConnectionState(IpcConnectionState.Degraded);
            return false;
        }

        public static bool AreBuildIdsCompatible(string? guiBuild, string? serviceBuild)
        {
            if (string.IsNullOrWhiteSpace(guiBuild) || string.IsNullOrWhiteSpace(serviceBuild)) return true;
            if (guiBuild.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || serviceBuild.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return true;

            string normGui = guiBuild.Split('+')[0].Trim();
            string normSvc = serviceBuild.Split('+')[0].Trim();
            return string.Equals(normGui, normSvc, StringComparison.OrdinalIgnoreCase);
        }

        // ──────────────────────────────────────────────────────────────
        // Normal non-blocking request
        // ──────────────────────────────────────────────────────────────

        public async Task<IpcResponse> SendRequestAsync(IpcMessageType type, string payload = null,
            CancellationToken ct = default)
        {
            var request = new IpcMessage { Type = type, Payload = payload };
            int timeoutMs = GetRequestTimeoutMs(type);

            // First attempt — if it fails with a timeout or pipe error, retry once after 500ms
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(timeoutMs);

                    using var pipe = new NamedPipeClientStream(".", PipeName,
                        PipeDirection.InOut, PipeOptions.Asynchronous);

                    await pipe.ConnectAsync(ConnectTimeoutMs, cts.Token);

                    // Append \n so the server's StreamReader.ReadLineAsync() gets a complete line
                    var requestWithNewline = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request) + "\n");
                    await pipe.WriteAsync(requestWithNewline, 0, requestWithNewline.Length, cts.Token);
                    await pipe.FlushAsync(cts.Token);

                    // Read one response line (final, non-streaming response)
                    using var reader = new System.IO.StreamReader(pipe, System.Text.Encoding.UTF8, false, 65536, leaveOpen: false);
                    var json = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(json))
                        return Fail("Zero bytes read from service");

                    var response = JsonSerializer.Deserialize<IpcResponse>(json);

                    // Update service build ID and available flag on a successful GetVersion
                    if (type == IpcMessageType.GetVersion && response?.Success == true && response.Data != null)
                    {
                        try
                        {
                            var hs = JsonSerializer.Deserialize<IpcHandshakeResponse>(response.Data);
                            var app = System.Windows.Application.Current as App;
                            var bm = app?.BackendManager;
                            if (hs?.ProtocolVersion == "v2" && (bm == null || string.IsNullOrEmpty(bm.SessionId) || hs.SessionId == bm.SessionId))
                            {
                                var attr = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
                                var guiBuildId = attr.Length > 0 ? ((System.Reflection.AssemblyInformationalVersionAttribute)attr[0]).InformationalVersion : "Unknown";
                                
                                if (guiBuildId != "Unknown" && hs.BuildId != "Unknown" && !AreBuildIdsCompatible(guiBuildId, hs.BuildId))
                                {
                                    SetConnectionState(IpcConnectionState.Offline);
                                    return Fail($"SERVICE VERSION MISMATCH\nGUI: {guiBuildId}\nBackend: {hs.BuildId}");
                                }
                                
                                _serviceBuildId = hs.BuildId;
                                SetConnectionState(IpcConnectionState.Connected);
                            }
                            else
                            {
                                SetConnectionState(IpcConnectionState.Offline);
                                return Fail("Handshake validation failed. Session mismatch.");
                            }
                        }
                        catch { SetConnectionState(IpcConnectionState.Degraded); }
                    }
                    else if (response?.Success == true)
                    {
                        // Any successful response confirms the service is actively connected
                        SetConnectionState(IpcConnectionState.Connected);
                    }

                    return response ?? Fail("Empty response from service");
                }
                catch (OperationCanceledException)
                {
                    if (attempt == 0 && !ct.IsCancellationRequested)
                    {
                        // Wait 500ms and retry once
                        try { await Task.Delay(500, ct); } catch { break; }
                        continue;
                    }
                    return Fail("Service request timed out or cancelled");
                }
                catch (Exception ex)
                {
                    if (attempt == 0 && !ct.IsCancellationRequested)
                    {
                        try { await Task.Delay(500, ct); } catch { break; }
                        continue;
                    }
                    return Fail(ex.Message.Contains("pipe") || ex.Message.Contains("connect") || ex.Message.Contains("No connection")
                        ? "Service Offline"
                        : "IPC Error: " + ex.Message);
                }
            }

            return Fail("Service Offline");
        }

        // ──────────────────────────────────────────────────────────────
        // Streaming request (optimization progress)
        // ──────────────────────────────────────────────────────────────

        public async System.Collections.Generic.IAsyncEnumerable<IpcResponse> SendStreamingRequestAsync(
            IpcMessageType type, string payload = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var request = new IpcMessage { Type = type, Payload = payload };
            var requestBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

            NamedPipeClientStream pipe = null;
            try
            {
                pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var connectCts = new CancellationTokenSource(ConnectTimeoutMs);
                await pipe.ConnectAsync(connectCts.Token);
                IsServiceAvailable = true;

                // Append \n terminator so the server's StreamReader.ReadLineAsync gets a complete line
                var requestWithNewline = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request) + "\n");
                await pipe.WriteAsync(requestWithNewline, 0, requestWithNewline.Length, ct);
                await pipe.FlushAsync(ct);

                using var reader = new System.IO.StreamReader(pipe, System.Text.Encoding.UTF8, false, 4096, leaveOpen: false);
                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var response = JsonSerializer.Deserialize<IpcResponse>(line);
                    if (response != null)
                        yield return response;
                }
            }
            finally
            {
                pipe?.Dispose();
            }
        }

        private static IpcResponse Fail(string msg)
            => new() { Success = false, ErrorMessage = msg };
    }
}
