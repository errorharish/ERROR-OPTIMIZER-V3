using System;







using System.IO;







using System.IO.Pipes;







using System.Text;







using System.Text.Json;







using System.Threading;







using System.Threading.Tasks;







using System.Threading.Channels;







using BiosOptimizer.Core.Interfaces;







using BiosOptimizer.Core.Services;







using BiosOptimizer.IPC.Contracts;







using BiosOptimizer.Core.Implementations;







using BiosOptimizer.Core.Implementations.Storage;







using BiosOptimizer.Core.Implementations.Diagnostics;







using BiosOptimizer.Core.Models;







using BiosOptimizer.Core.Implementations.Cleaners;







using Microsoft.Extensions.Hosting;







using Microsoft.Extensions.Logging;







using Microsoft.Extensions.DependencyInjection;







namespace BiosOptimizer.Service







{







    public class NamedPipeServer : BackgroundService







    {







        private readonly string PipeName;







        private readonly string _sessionId;







        private readonly ILogger<NamedPipeServer> _logger;







        private readonly IServiceProvider _services;







        public NamedPipeServer(







            ILogger<NamedPipeServer> logger, 







            IServiceProvider services,







            Microsoft.Extensions.Configuration.IConfiguration config)







        {







            _logger = logger;







            _services = services;







            _sessionId = config["session"] ?? "default";







            PipeName = $"BiosOptimizer_IPC_v2_{_sessionId}";







        }







        private readonly System.Collections.Concurrent.ConcurrentBag<NamedPipeServerStream> _activePipes = new();







        protected override async Task ExecuteAsync(CancellationToken stoppingToken)







        {







            _logger.LogInformation("PIPE_INIT_STARTED");







            







            if (await PerformSelfTestAsync(stoppingToken))







            {







                _logger.LogInformation("IPC_READY");







            }







            else







            {







                _logger.LogWarning("SERVICE_ERROR: Self-test failed, but continuing listener...");







            }







            _logger.LogInformation("PIPE_LISTENING on '{Pipe}'", PipeName);







            







            int consecutiveUacFailures = 0;







            var pipeSecurity = new System.IO.Pipes.PipeSecurity();







            // Local System







            pipeSecurity.AddAccessRule(new System.IO.Pipes.PipeAccessRule(







                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null),







                System.IO.Pipes.PipeAccessRights.FullControl,







                System.Security.AccessControl.AccessControlType.Allow));







                







            // Builtin Administrators







            pipeSecurity.AddAccessRule(new System.IO.Pipes.PipeAccessRule(







                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null),







                System.IO.Pipes.PipeAccessRights.FullControl,







                System.Security.AccessControl.AccessControlType.Allow));







                







            // Everyone (World) - allows GUI running as any user to connect







            pipeSecurity.AddAccessRule(new System.IO.Pipes.PipeAccessRule(







                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),







                System.IO.Pipes.PipeAccessRights.ReadWrite,







                System.Security.AccessControl.AccessControlType.Allow));







            try







            {







                while (!stoppingToken.IsCancellationRequested)







                {







                    try







                    {







                        var pipe = System.IO.Pipes.NamedPipeServerStreamAcl.Create(







                            PipeName, PipeDirection.InOut,







                            NamedPipeServerStream.MaxAllowedServerInstances,







                            // BYTE mode: required for ReadLineAsync-based streaming in the client







                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous,







                            0, 0, pipeSecurity);







                            







                        consecutiveUacFailures = 0; // Reset on successful pipe creation







                        _activePipes.Add(pipe);







                        _logger.LogInformation("PIPE_CREATED");







                        







                        await pipe.WaitForConnectionAsync(stoppingToken);







                        _logger.LogInformation("PIPE_CLIENT_CONNECTED");







                        _ = HandleClientAsync(pipe, stoppingToken);







                    }







                    catch (UnauthorizedAccessException ex)







                    {







                        consecutiveUacFailures++;







                        _logger.LogWarning("PIPE_WARNING: UnauthorizedAccessException. Stale handle or duplicate instance. Retrying...");







                        







                        // Wait up to 100 seconds for the previous long-running client to disconnect







                        if (consecutiveUacFailures >= 100)







                        {







                            _logger.LogCritical(ex, "PIPE_FATAL: Maximum UAC failures reached. Stopping pipe listener.");







                            break;







                        }







                        







                        await Task.Delay(1000, stoppingToken);







                    }







                    catch (System.IO.IOException)







                    {







                        // Likely MaxAllowedServerInstances reached







                        _logger.LogWarning("PIPE_WARNING: IOException. Max instances reached or pipe in use. Retrying in 2s...");







                        await Task.Delay(2000, stoppingToken);







                    }







                    catch (OperationCanceledException) { break; }







                    catch (Exception ex)







                    {







                        _logger.LogError(ex, "PIPE_ERROR: Unexpected error in listener.");







                        await Task.Delay(2000, stoppingToken);







                    }







                }







            } catch (Exception ex) { _logger.LogError(ex, "PIPE_FATAL_ERROR"); } finally {







                _logger.LogInformation("PIPE_SHUTDOWN");







                foreach (var active in _activePipes)







                {







                    try { active.Dispose(); } catch { }







                }







            }







        }







        







        // Use a unique test pipe name distinct from the main listener to avoid kernel handle conflicts







        private const string SelfTestPipeName = "BiosOptimizer_IPC_v2_selftest";







        private async Task<bool> PerformSelfTestAsync(CancellationToken ct)







        {







            try







            {







                _logger.LogInformation("IPC_STARTING (Self-test on '{TestPipe}')", SelfTestPipeName);







                







                var pipeSecurity = new System.IO.Pipes.PipeSecurity();







                pipeSecurity.AddAccessRule(new System.IO.Pipes.PipeAccessRule(







                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null),







                    System.IO.Pipes.PipeAccessRights.FullControl,







                    System.Security.AccessControl.AccessControlType.Allow));







                pipeSecurity.AddAccessRule(new System.IO.Pipes.PipeAccessRule(







                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null),







                    System.IO.Pipes.PipeAccessRights.FullControl,







                    System.Security.AccessControl.AccessControlType.Allow));







                pipeSecurity.AddAccessRule(new System.IO.Pipes.PipeAccessRule(







                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null),







                    System.IO.Pipes.PipeAccessRights.ReadWrite,







                    System.Security.AccessControl.AccessControlType.Allow));







                // Use a SEPARATE test pipe name -Â never the same as PipeName -Â to avoid







                // creating a zombie handle that blocks the main listener's first Create call.







                using var server = System.IO.Pipes.NamedPipeServerStreamAcl.Create(







                    SelfTestPipeName, PipeDirection.InOut,







                    1,







                    PipeTransmissionMode.Message, PipeOptions.Asynchronous,







                    0, 0, pipeSecurity);







                using var client = new System.IO.Pipes.NamedPipeClientStream(".", SelfTestPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);







                







                using var cts = new CancellationTokenSource(3000);







                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);







                var connectTask = client.ConnectAsync(linked.Token);







                var waitTask = server.WaitForConnectionAsync(linked.Token);







                







                await Task.WhenAll(connectTask, waitTask);







                _logger.LogInformation("IPC_SELF_TEST_PASSED");







                return true;







            }







            catch (Exception ex)







            {







                _logger.LogWarning(ex, "PIPE_WARNING: Self-test failed (non-fatal -Â main listener will start anyway).");







                return false;







            }







        }







        private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)







        {







            // A bounded channel serialises ALL writes to the pipe on a single loop.







            // This prevents concurrent pipe.Write from the Progress<T> callback thread







            // clashing with the final response write -Â which was the root cause of







            // streaming silently failing.







            var writeChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });







            // Dedicated write pump -Â the only place that touches pipe.WriteAsync







            async Task WritePumpAsync()







            {







                await foreach (var chunk in writeChannel.Reader.ReadAllAsync(ct))







                {







                    if (!pipe.IsConnected) break;







                    try







                    {







                        await pipe.WriteAsync(chunk, 0, chunk.Length, ct);







                        await pipe.FlushAsync(ct);







                    }







                    catch { break; }







                }







            }







            var writePump = WritePumpAsync();







            try







            {







                using var reader = new System.IO.StreamReader(pipe, Encoding.UTF8, false, 65536, leaveOpen: true);







                while (pipe.IsConnected && !ct.IsCancellationRequested)







                {







                    var line = await reader.ReadLineAsync();







                    if (line == null) break;







                    if (string.IsNullOrWhiteSpace(line)) continue;







                    IpcResponse response;







                    try







                    {







                        var msg = JsonSerializer.Deserialize<IpcMessage>(line);







                        System.Console.WriteLine($"[SERVICE] REQUEST_RECEIVED type={msg?.Type}");







                        // Progress callback: enqueue bytes onto the channel (non-blocking, thread-safe)







                        Action<IpcResponse> progressCallback = (resp) =>







                        {







                            var respBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(resp) + "\n");







                            writeChannel.Writer.TryWrite(respBytes);







                        };







                        response = await RouteAsync(msg!, progressCallback, ct);







                    }







                    catch (Exception ex)







                    {







                        response = Fail("Internal error: " + ex.Message);







                        System.Console.WriteLine($"[SERVICE] ROUTE_ERROR: {ex.Message}");







                    }







                    // Enqueue final response through the same channel







                    var finalBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response) + "\n");







                    writeChannel.Writer.TryWrite(finalBytes);







                }







            }







            catch (Exception ex) when (ex is not OperationCanceledException)







            {







                _logger.LogWarning(ex, "Client handler exception.");







            }







            finally







            {







                writeChannel.Writer.TryComplete();







                try { await writePump; } catch { }







                pipe.Dispose();







            }







        }







        private async Task<IpcResponse> RouteAsync(IpcMessage msg, Action<IpcResponse> progress, CancellationToken ct)







        {







            if (msg == null) return Fail("Null message");







            return msg.Type switch







            {







                IpcMessageType.GetVersion             => GetAssemblyVersion(),







                IpcMessageType.GetOptimizationScore   => GetScore(),







                IpcMessageType.GetOptimizationSummary => GetScore(),







                IpcMessageType.GetDashboard           => GetLiveMetrics(),







                IpcMessageType.GetSystemInfo          => GetSystemInfo(),







                IpcMessageType.GetHardwareInfo        => GetSystemInfo(),







                IpcMessageType.GetStartupItems        => GetStartupItems(),







                IpcMessageType.EnableStartupItem      => EnableStartupItem(msg.Payload),







                IpcMessageType.DisableStartupItem     => DisableStartupItem(msg.Payload),







                IpcMessageType.GetServices            => GetServices(),







                IpcMessageType.SetServiceStartupType  => SetServiceStartupType(msg.Payload),







                IpcMessageType.ChangeServiceState     => ChangeServiceState(msg.Payload),







                IpcMessageType.GetNetworkStatus       => GetNetworkStatus(),







                IpcMessageType.GetStorageStatus       => GetStorage(),







                IpcMessageType.PreviewTier            => await PreviewTierAsync(msg.Payload ?? "Normal", ct),







                IpcMessageType.ApplyTier              => await ApplyTierAsync(msg.Payload, progress, ct),







                IpcMessageType.RestoreTier            => await RestoreTierAsync(msg.Payload ?? "Normal", progress, ct),







                IpcMessageType.ScanCleaner            => ScanCleaner(),







                IpcMessageType.ApplyCleaner           => ApplyCleaner(),







                IpcMessageType.GetDebloatItems        => GetDebloatItems(),







                IpcMessageType.ApplyDebloatItems      => await ApplyDebloatItemsAsync(msg.Payload!, progress, ct),







                IpcMessageType.GetBiosCapabilities    => GetBiosCapabilities(),







                IpcMessageType.ApplyBiosSetting       => await ApplyTierAsync("BiosSafe", progress, ct),







                IpcMessageType.GetBackups             => Ok("Snapshot store does not expose a public listing API."),







                IpcMessageType.RestoreBackup          => await RestoreLastAsync(progress, ct),







                IpcMessageType.GetMachineProfile      => GetMachineProfile(),







                IpcMessageType.GetProcessCandidates   => await GetProcessCandidatesAsync(),







                IpcMessageType.TerminateProcesses     => TerminateProcesses(msg.Payload),







                IpcMessageType.RestoreAllSystem       => RestoreAllSystem(),







                IpcMessageType.PlanInputOptimization  => await PlanInputOptimizationAsync(),







                IpcMessageType.ApplyInputOptimization => await ApplyInputOptimizationAsync(msg.Payload),







                IpcMessageType.RestoreInputOptimization => await RestoreInputOptimizationAsync(),







                IpcMessageType.PlanRegistryTweak        => await PlanRegistryTweakAsync(),







                IpcMessageType.ApplyRegistryTweak       => await ApplyRegistryTweakAsync(msg.Payload),







                IpcMessageType.RestoreRegistryTweak     => await RestoreRegistryTweakAsync(msg.Payload),







                IpcMessageType.PlanNetworkOptimization  => await PlanNetworkOptimizationAsync(msg.Payload),







                IpcMessageType.ApplyNetworkOptimization => await ApplyNetworkOptimizationAsync(msg.Payload),







                IpcMessageType.RestoreNetworkOptimization => await RestoreNetworkOptimizationAsync(),







                IpcMessageType.FlushDnsCache            => await FlushDnsCacheAsync(),







                IpcMessageType.RunNetworkDiagnostics    => await RunNetworkDiagnosticsAsync(),







                // Storage Optimizer — new authoritative storage engine
                IpcMessageType.ScanStorageCategories    => await ScanStorageCategoriesAsync(msg.Payload, ct),
                IpcMessageType.ApplyStorageCleanup      => await ApplyStorageCleanupAsync(msg.Payload, progress, ct),
                IpcMessageType.GetRecycleBinInfo        => GetRecycleBinInfoImpl(),
                IpcMessageType.EmptyRecycleBin          => EmptyRecycleBinImpl(),
                IpcMessageType.ScanLargeFiles           => await ScanLargeFilesAsync(msg.Payload, ct),
                IpcMessageType.ScanDuplicateFiles       => await ScanDuplicateFilesAsync(msg.Payload, ct),
                IpcMessageType.DeleteStorageFile        => DeleteStorageFileImpl(msg.Payload),

                // Power Plan Optimization
                IpcMessageType.GetPowerPlans            => await GetPowerPlansAsync(ct),
                IpcMessageType.ApplyPowerPlan           => await ApplyPowerPlanAsync(msg.Payload, ct),
                IpcMessageType.RestorePowerPlan         => await RestorePowerPlanAsync(ct),
                IpcMessageType.EnableUltimatePerformance => await EnableUltimatePerformanceAsync(ct),
                IpcMessageType.CreateCustomAiPlan       => await CreateCustomAiPlanAsync(ct),
                IpcMessageType.DeleteCustomAiPlan       => await DeleteCustomAiPlanAsync(msg.Payload, ct),
                IpcMessageType.CreateRestorePoint       => await CreateRestorePointAsync(msg.Payload),
                IpcMessageType.GetRestorePoints         => await GetRestorePointsAsync(),
                IpcMessageType.CheckSystemRestoreStatus => await CheckSystemRestoreStatusAsync(),
                IpcMessageType.EnableSystemProtection   => await EnableSystemProtectionAsync(msg.Payload),
                IpcMessageType.GetSystemProtectionStatus => await GetSystemProtectionStatusAsync(msg.Payload),
                IpcMessageType.DisableSystemProtection  => await DisableSystemProtectionAsync(msg.Payload),
                IpcMessageType.ExecuteServicingTool     => await ExecuteServicingToolAsync(msg.Payload, ct),
                IpcMessageType.GetServicingEnvironment  => await GetServicingEnvironmentAsync(),
                IpcMessageType.EnsureServicingPrerequisites => await EnsureServicingPrerequisitesAsync(msg.Payload),







                _                                     => Fail("Unknown message type: " + msg.Type)







            };







        }







        // --- Implementations ---







        private IpcResponse GetAssemblyVersion()







        {







            var attr = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);







            var buildId = attr != null && attr.Length > 0 







                ? ((System.Reflection.AssemblyInformationalVersionAttribute)attr[0]).InformationalVersion 







                : (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown");







            var process = System.Diagnostics.Process.GetCurrentProcess();







            var handshake = new IpcHandshakeResponse







            {







                ServiceName = "BiosOptimizerService",







                ServiceVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0",







                BuildId = buildId,







                ProcessId = process.Id,







                ExecutablePath = process.MainModule?.FileName ?? "Unknown",







                ProtocolVersion = "v2",







                SessionId = _sessionId,







                Status = "Ready",







                Timestamp = DateTime.UtcNow.ToString("o"),







                Capabilities = new System.Collections.Generic.List<string> { 







                    "Optimization", "Storage", "Network", "Diagnostics", "Debloat", "BIOS", "StartupManager", "ServiceManager" 







                }







            };







            return Ok(JsonSerializer.Serialize(handshake));







        }







        private IpcResponse GetLiveMetrics()







        {







            try







            {







                // This is instant, 0ms latency







                var metrics = BiosOptimizer.Service.Metrics.LiveSystemMetricsService.CurrentMetrics;







                return Ok(JsonSerializer.Serialize(metrics));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private IpcResponse GetScore()







        {







            try







            {







                var envDetector = _services.GetRequiredService<IEnvironmentDetector>();







                var scoreEngine = _services.GetRequiredService<IOptimizationScoreEngine>();







                var profileRepo = _services.GetRequiredService<IProfileRepository>();







                var actionRegistry = _services.GetRequiredService<IActionRegistry>();







                var env = envDetector.Detect();







                var score = scoreEngine.CalculateScore(env, profileRepo, actionRegistry);







                var label = score.OverallScore switch







                {







                    >= 90 => "Excellent",







                    >= 70 => "Good",







                    >= 50 => "Fair",







                    >= 30 => "Needs Optimization",







                    _     => "Critical"







                };







                var result = new







                {







                    Score          = (double)score.OverallScore,







                    Label          = label,







                    TotalActions   = score.TotalAvailable,







                    AppliedActions = score.Applied,







                    AvailableActions = score.Remaining







                };







                return Ok(JsonSerializer.Serialize(result));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private IpcResponse GetSystemInfo()







        {







            try







            {







                var envDetector = _services.GetRequiredService<IEnvironmentDetector>();







                var diagEngine  = _services.GetRequiredService<DiagnosticsEngine>();







                var env = envDetector.Detect();







                var diag = diagEngine.GatherDiagnostics(env);







                var gpus = string.Join("\nGPU: ", diag.AllGpuNames);







                var info = $"CPU: {diag.CpuName}\n" +







                           $"Physical Cores: {diag.PhysicalCores} / Logical: {diag.LogicalProcessors}\n" +







                           $"RAM: {diag.RamTotalBytes / (1024 * 1024 * 1024L)} GB\n" +







                           $"GPU: {gpus}\n" +







                           $"Motherboard: {diag.Motherboard}\n" +







                           $"BIOS Version: {diag.BiosVersion}\n" +







                           $"Windows: {diag.WindowsVersion}\n" +







                           $"HAGS: {diag.HagsStatus}\n" +







                           $"Secure Boot: {diag.SecureBootStatus}\n" +







                           $"TPM: {diag.TpmStatus}\n" +







                           $"BitLocker: {diag.BitLockerStatus}\n" +







                           $"Manufacturer: {env.Manufacturer}  Model: {env.Model}\n" +







                           $"RAM (GB): {env.RamSizeGb}  BitLocker Active: {env.BitLockerActive}";







                return Ok(info);







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private IpcResponse GetMachineProfile()







        {







            try







            {







                var envDetector = _services.GetRequiredService<IEnvironmentDetector>();







                var env = envDetector.Detect();







                







                // Ensure supported targets count is populated by calling preview on all tiers







                return Ok(JsonSerializer.Serialize(env.MachineProfile));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private IpcResponse GetStartupItems()







        {







            try







            {







                var mgr = _services.GetRequiredService<IStartupManager>();







                var items = mgr.GetStartupEntries();







                return Ok(JsonSerializer.Serialize(items));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private class StartupPayload







        {







            public string Source { get; set; } = string.Empty;







            public string Path { get; set; } = string.Empty;







            public string ValueName { get; set; } = string.Empty;







        }







        private IpcResponse EnableStartupItem(string? payload)







        {







            try







            {







                if (string.IsNullOrEmpty(payload)) return Fail("Empty payload");







                var dto = JsonSerializer.Deserialize<StartupPayload>(payload);







                if (dto == null) return Fail("Invalid payload");







                var mgr = _services.GetRequiredService<IStartupManager>();







                var success = mgr.EnableEntry(dto.Source, dto.Path, dto.ValueName);







                return success ? Ok("Enabled") : Fail("Failed to enable entry");







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private IpcResponse DisableStartupItem(string? payload)







        {







            try







            {







                if (string.IsNullOrEmpty(payload)) return Fail("Empty payload");







                var dto = JsonSerializer.Deserialize<StartupPayload>(payload);







                if (dto == null) return Fail("Invalid payload");







                var mgr = _services.GetRequiredService<IStartupManager>();







                var success = mgr.DisableEntry(dto.Source, dto.Path, dto.ValueName);







                return success ? Ok("Disabled") : Fail("Failed to disable entry");







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private IpcResponse GetServices()







        {







            try







            {







                var mgr = _services.GetRequiredService<IServiceManager>();







                var allServices = mgr.GetAllServices();







                var dtos = allServices.Select(s => new ServiceInfoDto







                {







                    ServiceName = s.ServiceName,







                    DisplayName = s.DisplayName,







                    Description = s.Description,







                    ExePath = s.ExePath,







                    StartupType = s.StartupType.ToString(),







                    RunningState = s.RunningState.ToString(),







                    Risk = s.Risk,







                    ActionSafety = s.ActionSafety,







                    ActionSafetyReason = s.ActionSafetyReason,







                    Publisher = s.Publisher,







                    Version = s.Version,







                    Category = s.Category,







                    ServiceType = s.ServiceType,







                    Account = s.Account,







                    IsDelayedStart = s.IsDelayedStart,







                    IsCritical = s.IsCritical,







                    FileExists = s.FileExists,







                    IsMicrosoft = s.IsMicrosoft,







                    DependsOn = s.DependsOn,







                    DependentServices = s.DependentServices







                }).ToList();







                return Ok(JsonSerializer.Serialize(dtos));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private class ServiceControlPayload







        {







            public string ServiceName { get; set; } = string.Empty;







            public string State { get; set; } = string.Empty;







        }







        private IpcResponse ChangeServiceState(string? payload)







        {







            try







            {







                if (string.IsNullOrEmpty(payload)) return Fail("Empty payload");







                var dto = JsonSerializer.Deserialize<ServiceControlPayload>(payload);







                if (dto == null) return Fail("Invalid payload");







                var mgr = _services.GetRequiredService<IServiceManager>();







                bool success = false;

                if (dto.State.Equals("Start", StringComparison.OrdinalIgnoreCase))

                    success = mgr.Start(dto.ServiceName);

                else if (dto.State.Equals("Stop", StringComparison.OrdinalIgnoreCase))

                    success = mgr.Stop(dto.ServiceName);

                else if (dto.State.Equals("Restart", StringComparison.OrdinalIgnoreCase))

                    success = mgr.Restart(dto.ServiceName);







                return success ? Ok(dto.State) : Fail($"Failed to {dto.State.ToLower()} service");







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private IpcResponse SetServiceStartupType(string? payload)







        {







            try







            {







                if (string.IsNullOrEmpty(payload)) return Fail("Empty payload");







                var dto = JsonSerializer.Deserialize<ServiceControlPayload>(payload);







                if (dto == null) return Fail("Invalid payload");







                var mgr = _services.GetRequiredService<IServiceManager>();







                







                ServiceStartupType st = ServiceStartupType.Unknown;







                if (dto.State.Equals("AutomaticDelayedStart", StringComparison.OrdinalIgnoreCase) ||

                    dto.State.Equals("Automatic (Delayed)", StringComparison.OrdinalIgnoreCase))

                {

                    st = ServiceStartupType.AutomaticDelayedStart;

                }

                else if (dto.State.Equals("Automatic", StringComparison.OrdinalIgnoreCase)) 

                {

                    st = ServiceStartupType.Automatic;

                }

                else if (dto.State.Equals("Manual", StringComparison.OrdinalIgnoreCase)) 

                {

                    st = ServiceStartupType.Manual;

                }

                else if (dto.State.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) 

                {

                    st = ServiceStartupType.Disabled;

                }







                if (st == ServiceStartupType.Unknown) return Fail("Invalid startup type");







                bool success = mgr.SetStartupType(dto.ServiceName, st);







                return success ? Ok("Changed") : Fail("Failed to change startup type");







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private string GetServiceRisk(string serviceName)







        {







            var lower = serviceName.ToLowerInvariant();







            if (lower.Contains("windows") || lower.Contains("microsoft") || lower.StartsWith("wuauserv") || lower.StartsWith("vss")) 







                return "High";







            if (lower.Contains("intel") || lower.Contains("amd") || lower.Contains("nvidia")) 







                return "Medium";







            return "Low";







        }







        private IpcResponse GetNetworkStatus()







        {







            try







            {







                var adapters = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();







                var sb = new System.Text.StringBuilder();







                int idx = 1;







                foreach (var adapter in adapters)







                {







                    // Only show operational, non-loopback adapters







                    if (adapter.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)







                        continue;







                    if (adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)







                        continue;







                    var props = adapter.GetIPProperties();







                    var ipv4 = props.UnicastAddresses







                        .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);







                    var dns = props.DnsAddresses.Count > 0 ? props.DnsAddresses[0].ToString() : "N/A";







                    var speedMbps = adapter.Speed > 0 ? $"{adapter.Speed / 1_000_000} Mbps" : "Unknown";







                    var adapterType = adapter.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211







                        ? "Wi-Fi" : "Ethernet";







                    sb.AppendLine($"Adapter {idx}: {adapter.Name}");







                    sb.AppendLine($"Type: {adapterType}");







                    sb.AppendLine($"Description: {adapter.Description}");







                    sb.AppendLine($"Speed: {speedMbps}");







                    sb.AppendLine($"IP: {ipv4?.Address?.ToString() ?? "N/A"}");







                    sb.AppendLine($"DNS: {dns}");







                    sb.AppendLine($"MAC: {adapter.GetPhysicalAddress()}");







                    sb.AppendLine();







                    idx++;







                }







                if (sb.Length == 0)







                    return Ok("No active network adapters detected.");







                return Ok(sb.ToString().TrimEnd());







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private IpcResponse GetStorage()







        {







            try







            {







                var eng = _services.GetRequiredService<StorageEngine>();







                var drives = eng.GetDrives();







                return Ok(JsonSerializer.Serialize(drives));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private async Task<IpcResponse> PreviewTierAsync(string tierId, CancellationToken ct)



        {



            try



            {



                string targetTier = tierId ?? "Normal";



                if (!string.IsNullOrEmpty(targetTier) && targetTier.TrimStart().StartsWith("{"))



                {



                    try



                    {



                        using var doc = JsonDocument.Parse(targetTier);



                        if (doc.RootElement.TryGetProperty("TierId", out var tProp) && !string.IsNullOrEmpty(tProp.GetString()))



                        {



                            targetTier = tProp.GetString()!;



                        }



                    }



                    catch { }



                }







                var engine = _services.GetRequiredService<ITierEngine>();



                var preview = await engine.PreviewAsync(targetTier, ct);



                



                var dto = new TierPreviewDto



                {



                    TierId = targetTier,



                    TierName = targetTier,



                    ActionCount = preview.PlannedChanges?.Count ?? 0,



                    CanApply = true,



                    CanRestore = false,



                    Actions = new List<OptimizationActionDto>()



                };







                                                var allChanges = new List<BiosOptimizer.Core.Models.PlannedChange>();
                if (preview.PlannedChanges != null) allChanges.AddRange(preview.PlannedChanges);
                if (preview.InheritedChanges != null) allChanges.AddRange(preview.InheritedChanges);

                dto.ActionCount = allChanges.Count;

                foreach (var p in allChanges)
                {
                    bool isAlreadyOptimized = StateNormalizer.IsSatisfied(p.Current, p.Target);
                    string status = isAlreadyOptimized ? "AlreadyOptimized" : "Recommended";
                    bool isSelected = !isAlreadyOptimized;

                    dto.Actions.Add(new OptimizationActionDto
                    {
                        ItemId = p.Id ?? "",
                        ActionName = p.ActionName ?? "",
                        DisplayName = p.Item ?? "",
                        Description = p.Description ?? "",
                        Category = p.Category ?? "General",
                        IsInherited = p.IsInherited,
                        CurrentState = p.Current ?? "",
                        TargetState = p.Target ?? "",
                        Risk = p.Risk ?? "",
                        Warning = p.Warning ?? "",
                        ExecutionType = p.ExecutionType ?? "WINDOWS_AUTOMATIC",
                        Status = status,
                        IsSelected = isSelected
                    });
                }

                foreach (var s in preview.SkippedChanges ?? new List<BiosOptimizer.Core.Models.SkippedChange>())
                {
                    string status = string.Equals(s.Category, "Unsupported", StringComparison.OrdinalIgnoreCase) ? "Unsupported" : "NotApplicable";
                    dto.Actions.Add(new OptimizationActionDto
                    {
                        ItemId = s.Id ?? "",
                        DisplayName = s.Item ?? "",
                        Category = s.Category ?? "General",
                        Reason = s.Reason ?? "",
                        Status = status,
                        ExecutionType = s.ExecutionType ?? "WINDOWS_AUTOMATIC",
                        Applicable = false,
                        Supported = (status != "Unsupported"),
                        IsSelected = false
                    });
                }







                return Ok(JsonSerializer.Serialize(dto));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private async Task<IpcResponse> ApplyTierAsync(string? payload, Action<IpcResponse> progressCallback, CancellationToken ct)







        {







            try







            {







                if (string.IsNullOrEmpty(payload)) return Fail("Empty payload");







                







                string tierId = "Normal";







                System.Collections.Generic.HashSet<string>? allowedItemIds = null;







                







                try 







                {







                    var dto = JsonSerializer.Deserialize<ApplyTierRequestDto>(payload);







                    if (dto != null && !string.IsNullOrEmpty(dto.TierId))







                    {







                        tierId = dto.TierId;







                        if (dto.SelectedItemIds != null)







                        {







                            allowedItemIds = new System.Collections.Generic.HashSet<string>(dto.SelectedItemIds);







                        }







                    }







                    else







                    {







                        tierId = payload;







                    }







                }







                catch







                {







                    tierId = payload;







                }







                var engine = _services.GetRequiredService<ITierEngine>();







                var preview = await engine.PreviewAsync(tierId, ct);







                







                int total = preview.PlannedChanges?.Count ?? 0;







                int current = 0;







                var confirmation = new ConfirmationContext { IsConfirmed = true };







                var progress = new SynchronousProgress<OptimizationResult>(r =>
                {
                    if (r.Status != BiosOptimizer.Core.Models.ResultStatus.Running && r.Status != BiosOptimizer.Core.Models.ResultStatus.Verifying)
                    {
                        current++;
                    }

                    var dto = new OptimizationActionDto
                    {
                        ItemId = r.ItemId,
                        ActionName = r.ActionName,
                        Category = r.Category,
                        DisplayName = r.DisplayName,
                        Reason = r.Message,
                        Status = r.Status.ToString()
                    };

                    progressCallback(Ok(JsonSerializer.Serialize(new { ProgressUpdate = true, Action = dto, Current = current, Total = total })));
                });







                var results = await engine.ApplyAsync(tierId, confirmation, false, ct, progress, allowedItemIds);







                int success = 0, failed = 0, blocked = 0, notAvailable = 0, alreadyOptimized = 0, notApplicable = 0;







                var failedReasons = new System.Collections.Generic.List<string>();







                var blockedReasons = new System.Collections.Generic.List<string>();







                







                foreach (var r in results)







                {







                    if (r.Status == ResultStatus.Success || r.Status == ResultStatus.Verified) success++;







                    else if (r.Status == ResultStatus.Failed) 







                    {







                        failed++;







                        failedReasons.Add(r.Message);







                    }







                    else if (r.Status == ResultStatus.Blocked)







                    {







                        blocked++;







                        blockedReasons.Add(r.Message);







                    }







                    else if (r.Status == ResultStatus.AlreadyOptimized) alreadyOptimized++;







                    else if (r.Status == ResultStatus.NotApplicable) notApplicable++;







                    else if (r.Status == ResultStatus.NotAvailable) notAvailable++;







                }







                







                var summary = new







                {







                    Success = success,







                    Failed = failed,







                    Blocked = blocked,







                    NotAvailable = notAvailable,







                    AlreadyOptimized = alreadyOptimized,







                    NotApplicable = notApplicable,







                    FailedReasons = failedReasons,







                    BlockedReasons = blockedReasons







                };







                try
                {
                    string debugDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ErrorOptimizer", "logs");
                    Directory.CreateDirectory(debugDir);
                    System.IO.File.WriteAllText(Path.Combine(debugDir, "ApplyTierDebug.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch { }







                return Ok(JsonSerializer.Serialize(new { FinalResult = true, Summary = summary }));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private Task<IpcResponse> RestoreTierAsync(string tierId, Action<IpcResponse> progressCallback, CancellationToken ct)







        {







            return Task.FromResult(Fail("Restore functionality has been removed."));







        }







        private Task<IpcResponse> RestoreLastAsync(Action<IpcResponse> progressCallback, CancellationToken ct)







        {







            // Restore the "Normal" tier as the safest fallback if no specific snapshot ID exists







            return RestoreTierAsync("Normal", progressCallback, ct);







        }







        private IpcResponse ScanCleaner()







        {







            try







            {







                var ram  = _services.GetRequiredService<RamCleanupEngine>();







                var temp = _services.GetRequiredService<TempCleanerEngine>();







                var ramScan  = ram.Scan();







                var tempScan = temp.Scan();







                var ramMb  = ramScan.TotalSizeInBytes / (1024 * 1024);







                var tempMb = tempScan.TotalSizeInBytes / (1024 * 1024);







                return Ok($"Reclaimable RAM: ~{ramMb} MB\nTemp Files: ~{tempMb} MB ({tempScan.FileCount} files)");







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private IpcResponse ApplyCleaner()







        {







            try







            {







                var ram  = _services.GetRequiredService<RamCleanupEngine>();







                var temp = _services.GetRequiredService<TempCleanerEngine>();







                var ramFreed  = ram.Clean();







                var tempFreed = temp.Clean();







                var totalMb = (ramFreed + tempFreed) / (1024 * 1024);







                return Ok($"Cleanup complete. Freed approximately {totalMb} MB.");







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private IpcResponse GetDebloatItems()







        {







            try







            {







                var packageManager = _services.GetRequiredService<BiosOptimizer.Core.Interfaces.IPackageManager>();







                var packages = packageManager.GetInstalledPackages();







                







                var actions = new List<OptimizationActionDto>();







                foreach (var pkg in packages)







                {







                    string status = "SAFE TO REMOVE";







                    if (pkg.IsSystemComponent) status = "PROTECTED";







                    else if (pkg.IsFramework) status = "FRAMEWORK";







                    else if (pkg.IsResource) status = "RESOURCE";







                    else







                    {







                        var lowerName = pkg.Name.ToLowerInvariant();







                        if (lowerName.Contains("cortana") || lowerName.Contains("edge") || lowerName.Contains("store") || lowerName.Contains("windows.client"))







                            status = "HIGH RISK";







                        else if (pkg.PublisherDisplayName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))







                            status = "MEDIUM";







                        else if (lowerName.Contains("xbox") || lowerName.Contains("zune") || lowerName.Contains("bing") || lowerName.Contains("solitaire"))







                            status = "SAFE TO REMOVE";







                        else







                            status = "OPTIONAL";







                    }







                    actions.Add(new OptimizationActionDto







                    {







                        ItemId = pkg.FullName,







                        DisplayName = pkg.DisplayName,







                        Category = pkg.PublisherDisplayName,







                        Status = status







                    });







                }







                var dto = new TierPreviewDto { Actions = actions };







                return Ok(JsonSerializer.Serialize(dto));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private async Task<IpcResponse> ApplyDebloatItemsAsync(string payload, Action<IpcResponse> progressCallback, CancellationToken ct)







        {







            try







            {







                var request = JsonSerializer.Deserialize<DebloatRequestDto>(payload);







                if (request == null || request.SelectedIds.Count == 0) return Ok("No items selected.");







                







                var profile = new ProfileDef { Id = "Debloat", DisplayName = "Debloat" };







                foreach (var id in request.SelectedIds)







                {







                    profile.Entries.Add(new OptimizationEntry 







                    { 







                        Id = "debloat.appx." + id.GetHashCode(), 







                        Action = "RemoveAppxPackage", 







                        Target = id, 







                        Category = "AppX",







                        DisplayName = "Remove AppX Package",







                        Risk = "Low"







                    });







                }







                







                var engine = _services.GetRequiredService<ITierEngine>();







                var confirmation = new ConfirmationContext { IsConfirmed = true };







                







                int total = profile.Entries.Count;







                int current = 0;







                var progress = new Progress<OptimizationResult>(r =>







                {







                    if (r.Status != BiosOptimizer.Core.Models.ResultStatus.Running)







                    {







                        current++;







                    }







                    var dto = new OptimizationActionDto







                    {







                        ItemId = r.ItemId,







                        DisplayName = r.DisplayName,







                        Reason = r.Message,







                        Status = r.Status.ToString()







                    };







                    progressCallback(Ok(JsonSerializer.Serialize(new { ProgressUpdate = true, Action = dto, Current = current, Total = total })));







                });







                







                var results = await engine.ApplyAsync(profile, confirmation, false, ct, progress);







                







                int removed = results.Count(r => r.Status == BiosOptimizer.Core.Models.ResultStatus.Success);







                int failed = results.Count(r => r.Status == BiosOptimizer.Core.Models.ResultStatus.Failed);







                







                var summary = new







                {







                    FinalResult = true,







                    Success = removed,







                    Failed = failed,







                    Message = $"Debloat complete. Applied: {removed}, Failed/Skipped: {failed}"







                };







                







                return Ok(JsonSerializer.Serialize(summary));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private IpcResponse GetBiosCapabilities()







        {







            try







            {







                var provider = _services.GetRequiredService<IProviderManager>();







                var active = provider.GetActiveProvider();







                if (active == null) return Ok("No BIOS provider detected for this hardware.");







                var info = active.GetProviderInfo();







                return Ok($"Provider: {info.Name}  Status: {info.Status}  Reason: {info.Reason}");







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private async Task<IpcResponse> GetProcessCandidatesAsync()







        {







            Console.WriteLine("[PROCESS REDUCTION] Handler received GetProcessCandidates");







            try







            {







                var engine = _services.GetRequiredService<IProcessReductionEngine>();







                var list = await engine.ScanProcessesAsync();







                var options = new JsonSerializerOptions();







                options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());







                return Ok(JsonSerializer.Serialize(list, options));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private IpcResponse TerminateProcesses(string? payload)







        {







            if (string.IsNullOrWhiteSpace(payload)) return Fail("No processes specified.");







            try







            {







                var pids = JsonSerializer.Deserialize<List<int>>(payload) ?? new List<int>();







                var engine = _services.GetRequiredService<IProcessReductionEngine>();







                int killed = engine.TerminateProcesses(pids);







                return Ok(JsonSerializer.Serialize(new { Killed = killed }));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private IpcResponse RestoreAllSystem()







        {







            try







            {







                var backup = _services.GetRequiredService<IBackupManager>();







                bool restored = backup.RestoreAll();







                return Ok(JsonSerializer.Serialize(new { Success = restored }));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private async Task<IpcResponse> PlanInputOptimizationAsync()







        {







            try







            {







                var engine = _services.GetRequiredService<IInputOptimizerEngine>();







                var plan = await engine.PlanInputOptimizationAsync();







                return Ok(JsonSerializer.Serialize(plan, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private async Task<IpcResponse> ApplyInputOptimizationAsync(string? riskLevel)







        {







            try







            {







                var engine = _services.GetRequiredService<IInputOptimizerEngine>();







                var res = await engine.ApplyInputOptimizationAsync(riskLevel ?? "CORE"); return res.Success ? Ok(res.Message) : Fail(res.Message);







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private async Task<IpcResponse> RestoreInputOptimizationAsync()







        {







            try







            {







                var engine = _services.GetRequiredService<IInputOptimizerEngine>();







                var res = await engine.RestoreInputOptimizationAsync();







                return res.Success ? Ok(res.Message) : Fail(res.Message);







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private async Task<IpcResponse> PlanRegistryTweakAsync()







        {







            try







            {







                var engine = _services.GetRequiredService<IRegistryTweakEngine>();







                var plan = await engine.PlanRegistryTweakAsync();







                return Ok(JsonSerializer.Serialize(plan, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private async Task<IpcResponse> ApplyRegistryTweakAsync(string? payload)







        {







            try







            {







                var tweakIds = string.IsNullOrEmpty(payload) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(payload) ?? new List<string>();







                var engine = _services.GetRequiredService<IRegistryTweakEngine>();







                var res = await engine.ApplyRegistryTweaksAsync(tweakIds);







                return res.Success ? Ok(JsonSerializer.Serialize(new { Message = res.Message, Status = res.Status, AppliedCount = res.AppliedCount, VerifiedCount = res.VerifiedCount, RestartRequiredCount = res.RestartRequiredCount, FailedCount = res.FailedCount })) : Fail(res.Message);







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private async Task<IpcResponse> RestoreRegistryTweakAsync(string? payload)







        {







            try







            {







                var tweakIds = string.IsNullOrEmpty(payload) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(payload) ?? new List<string>();







                var engine = _services.GetRequiredService<IRegistryTweakEngine>();







                var res = await engine.RestoreRegistryTweaksAsync(tweakIds);







                return res.Success ? Ok(JsonSerializer.Serialize(new { Message = res.Message, Status = res.Status, RestoredCount = res.RestoredCount, FailedCount = res.FailedCount })) : Fail(res.Message);







            }







            catch (Exception ex) { return Fail(ex.Message); }







        }







        private async Task<IpcResponse> PlanNetworkOptimizationAsync(string? payload)
        {
            try
            {
                var engine = _services.GetRequiredService<INetworkOptimizerEngine>();
                bool includeDiag = bool.TryParse(payload, out bool b) && b;
                var plan = await engine.PlanNetworkOptimizationAsync(includeDiag);
                return Ok(JsonSerializer.Serialize(plan));
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        private async Task<IpcResponse> ApplyNetworkOptimizationAsync(string? payload)
        {
            try
            {
                if (string.IsNullOrEmpty(payload)) return Fail("Missing actionId payload");
                var engine = _services.GetRequiredService<INetworkOptimizerEngine>();
                var (success, message, verifiedState) = await engine.ApplyNetworkActionAsync(payload);
                return success 
                    ? Ok(JsonSerializer.Serialize(new { Success = true, Message = message, VerifiedState = verifiedState }))
                    : Fail(message);
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        private async Task<IpcResponse> RestoreNetworkOptimizationAsync()
        {
            try
            {
                var engine = _services.GetRequiredService<INetworkOptimizerEngine>();
                var (success, message) = await engine.RestoreNetworkOptimizationAsync();
                return success ? Ok(message) : Fail(message);
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        private async Task<IpcResponse> FlushDnsCacheAsync()
        {
            try
            {
                var engine = _services.GetRequiredService<INetworkOptimizerEngine>();
                var (success, message, timestamp) = await engine.FlushDnsCacheAsync();
                return Ok(JsonSerializer.Serialize(new { Success = success, Message = message, Timestamp = timestamp }));
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        private async Task<IpcResponse> RunNetworkDiagnosticsAsync()
        {
            try
            {
                var engine = _services.GetRequiredService<INetworkOptimizerEngine>();
                var diag = await engine.RunDiagnosticsAsync();
                return Ok(JsonSerializer.Serialize(diag));
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        private static IpcResponse Ok(string data)   => new() { Success = true,  Data = data };







        private static IpcResponse Fail(string error) => new() { Success = false, ErrorMessage = error };

        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
        // STORAGE OPTIMIZER â€” Authoritative Handlers
        // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

        private Task<IpcResponse> ScanStorageCategoriesAsync(string? payload, CancellationToken ct)
        {
            return Task.Run<IpcResponse>(() =>
            {
                try
                {
                    var engine = new StorageCleanerEngine();
                    var categories = StorageCleanerEngine.BuildCategories();

                    // Scan each category for real file sizes
                    foreach (var cat in categories)
                    {
                        if (ct.IsCancellationRequested) break;
                        engine.ScanCategory(cat);
                    }

                    // Also include drive info
                    var storageEngine = _services.GetRequiredService<StorageEngine>();
                    var drives = storageEngine.GetDrives();

                    var result = new
                    {
                        Drives = drives.Select(d => new
                        {
                            d.Name,
                            d.Letter,
                            d.VolumeLabel,
                            d.DriveFormat,
                            d.DriveType,
                            d.MediaType,
                            d.Model,
                            d.InterfaceType,
                            d.TotalGb,
                            d.UsedGb,
                            d.FreeGb,
                            d.UsagePercentage,
                            d.HealthStatus,
                            d.SmartStatus,
                            d.TemperatureText,
                            d.StoragePressure,
                            d.IsSystemDrive
                        }).ToList(),
                        Categories = categories.Select(c => new
                        {
                            c.Id,
                            c.Name,
                            c.Description,
                            c.Icon,
                            c.CategoryType,
                            c.RiskLevel,
                            c.DetectedBytes,
                            c.FileCount,
                            c.IsSelected,
                            c.IsScanned,
                            c.FormattedSize
                        }).ToList(),
                        TotalReclaimableBytes = categories.Sum(c => c.DetectedBytes),
                        TotalFilesFound = categories.Sum(c => c.FileCount)
                    };

                    return Ok(JsonSerializer.Serialize(result));
                }
                catch (Exception ex) { return Fail(ex.Message); }
            }, ct);
        }

        private Task<IpcResponse> ApplyStorageCleanupAsync(string? payload, Action<IpcResponse> progressCallback, CancellationToken ct)
        {
            return Task.Run<IpcResponse>(() =>
            {
                try
                {
                    // Deserialize selected category IDs and drive letter
                    var req = string.IsNullOrEmpty(payload)
                        ? null
                        : JsonSerializer.Deserialize<StorageCleanupRequestDto>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    var selectedIds  = req?.SelectedCategoryIds ?? new List<string>();
                    string driveLetter = req?.DriveLetter ?? "C";

                    var engine = new StorageCleanerEngine();
                    var allCategories = StorageCleanerEngine.BuildCategories();

                    // Filter to selected categories only
                    var targetCategories = allCategories
                        .Where(c => selectedIds.Count == 0 || selectedIds.Contains(c.Id))
                        .ToList();

                    // Re-scan to get current sizes
                    foreach (var cat in targetCategories)
                        engine.ScanCategory(cat);

                    // Progress callback adapter
                    void OnProgress(StorageCleanupProgressReport report)
                    {
                        progressCallback(Ok(JsonSerializer.Serialize(new
                        {
                            ProgressUpdate = true,
                            report.CurrentStage,
                            report.ProgressPercent,
                            report.CurrentItemName,
                            report.FilesProcessed,
                            report.FilesRemoved,
                            report.FilesSkipped,
                            report.BytesReclaimed,
                            report.StatusMessage
                        })));
                    }

                    var result = engine.CleanCategories(targetCategories, driveLetter, OnProgress, ct);

                    // Send final result
                    progressCallback(Ok(JsonSerializer.Serialize(new
                    {
                        FinalResult = true,
                        result.Success,
                        result.BytesReclaimed,
                        FormattedReclaimed = result.FormattedReclaimed,
                        result.FilesRemoved,
                        result.FilesSkipped,
                        result.FilesFailed,
                        InitialFreeGb = Math.Round(result.InitialFreeBytes / (1024.0 * 1024 * 1024), 2),
                        FinalFreeGb   = Math.Round(result.FinalFreeBytes   / (1024.0 * 1024 * 1024), 2),
                        result.SummaryMessage
                    })));

                    return Ok(JsonSerializer.Serialize(result));
                }
                catch (Exception ex) { return Fail(ex.Message); }
            }, ct);
        }

        private IpcResponse GetRecycleBinInfoImpl()
        {
            try
            {
                var engine = new StorageCleanerEngine();
                var (sizeBytes, count) = engine.GetRecycleBinInfo();
                return Ok(JsonSerializer.Serialize(new
                {
                    SizeBytes = sizeBytes,
                    FormattedSize = StorageCleanupCategory.FormatBytes(sizeBytes),
                    ItemCount = count
                }));
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        private IpcResponse EmptyRecycleBinImpl()
        {
            try
            {
                var engine = new StorageCleanerEngine();
                var (success, freedBytes, message) = engine.EmptyRecycleBin();
                return Ok(JsonSerializer.Serialize(new
                {
                    Success = success,
                    FreedBytes = freedBytes,
                    FormattedFreed = StorageCleanupCategory.FormatBytes(freedBytes),
                    Message = message
                }));
            }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        private Task<IpcResponse> ScanLargeFilesAsync(string? payload, CancellationToken ct)
        {
            return Task.Run<IpcResponse>(() =>
            {
                try
                {
                    string driveLetter = "C:\\";
                    long minSizeBytes  = 500L * 1024 * 1024; // 500 MB default

                    if (!string.IsNullOrEmpty(payload))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(payload);
                            if (doc.RootElement.TryGetProperty("DriveLetter", out var dl)) driveLetter = (dl.GetString() ?? "C") + ":\\";
                            if (doc.RootElement.TryGetProperty("MinSizeMb", out var ms)) minSizeBytes = (long)ms.GetDouble() * 1024 * 1024;
                        }
                        catch { }
                    }

                    var engine = new StorageCleanerEngine();
                    var files = engine.FindLargeFiles(driveLetter, minSizeBytes, ct);

                    var dtos = files.Select(f => new
                    {
                        f.FileName,
                        f.FullPath,
                        f.Extension,
                        f.SizeBytes,
                        f.SizeMb,
                        f.FormattedSize,
                        LastModified = f.LastModified.ToString("yyyy-MM-dd"),
                        f.DriveLetter
                    }).ToList();

                    return Ok(JsonSerializer.Serialize(dtos));
                }
                catch (Exception ex) { return Fail(ex.Message); }
            }, ct);
        }

        private Task<IpcResponse> ScanDuplicateFilesAsync(string? payload, CancellationToken ct)
        {
            return Task.Run<IpcResponse>(() =>
            {
                try
                {
                    string rootPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    long minSizeBytes = 1L * 1024 * 1024; // 1 MB minimum for duplicates

                    if (!string.IsNullOrEmpty(payload))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(payload);
                            if (doc.RootElement.TryGetProperty("RootPath", out var rp)) rootPath = rp.GetString() ?? rootPath;
                            if (doc.RootElement.TryGetProperty("MinSizeMb", out var ms)) minSizeBytes = (long)ms.GetDouble() * 1024 * 1024;
                        }
                        catch { }
                    }

                    var engine = new StorageCleanerEngine();
                    var groups = engine.FindDuplicates(rootPath, minSizeBytes, ct);

                    var dtos = groups.Select(g => new
                    {
                        g.GroupId,
                        g.FileSizeBytes,
                        g.FormattedFileSize,
                        g.Hash,
                        g.FilePaths,
                        g.ReclaimableBytes,
                        g.FormattedReclaimable
                    }).ToList();

                    return Ok(JsonSerializer.Serialize(new
                    {
                        Groups = dtos,
                        TotalGroups = dtos.Count,
                        TotalFiles = dtos.Sum(g => g.FilePaths.Count),
                        TotalReclaimableBytes = dtos.Sum(g => g.ReclaimableBytes),
                        FormattedTotalReclaimable = StorageCleanupCategory.FormatBytes(dtos.Sum(g => (long)g.ReclaimableBytes))
                    }));
                }
                catch (Exception ex) { return Fail(ex.Message); }
            }, ct);
        }

        private IpcResponse DeleteStorageFileImpl(string? payload)
        {
            try
            {
                if (string.IsNullOrEmpty(payload)) return Fail("Empty payload");

                string filePath = payload.Trim('"', ' ');

                // Safety check â€” never delete system or Windows directories
                string lower = filePath.ToLowerInvariant();
                if (lower.Contains("\\windows\\") && !lower.Contains("\\temp\\"))
                    return Fail("Blocked: cannot delete files in protected Windows system directories.");
                if (lower.Contains("\\program files") || lower.Contains("\\program files (x86)"))
                    return Fail("Blocked: cannot delete files in Program Files.");

                if (!File.Exists(filePath)) return Fail("File not found.");
                long size = new FileInfo(filePath).Length;
                File.Delete(filePath);
                return Ok(JsonSerializer.Serialize(new
                {
                    Success = true,
                    DeletedBytes = size,
                    FormattedSize = StorageCleanupCategory.FormatBytes(size),
                    Message = $"Deleted: {Path.GetFileName(filePath)}"
                }));
            }
            catch (UnauthorizedAccessException ex) { return Fail($"Access denied: {ex.Message}"); }
            catch (IOException ex) { return Fail($"File in use: {ex.Message}"); }
            catch (Exception ex) { return Fail(ex.Message); }
        }

        private class StorageCleanupRequestDto
        {
            public List<string> SelectedCategoryIds { get; set; } = new();
            public string DriveLetter { get; set; } = "C";
        }

        // --- Power Plan Optimization Backend Handlers ---
        private async Task<IpcResponse> GetPowerPlansAsync(CancellationToken ct)
        {
            try
            {
                var plans = await BiosOptimizer.Core.Implementations.Power.PowerPlanEngine.Instance.DiscoverPowerPlansAsync(ct);
                return Ok(JsonSerializer.Serialize(plans));
            }
            catch (Exception ex)
            {
                return Fail($"Failed to discover power plans: {ex.Message}");
            }
        }

        private async Task<IpcResponse> ApplyPowerPlanAsync(string? guid, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(guid)) return Fail("Target GUID cannot be empty.");
                var res = await BiosOptimizer.Core.Implementations.Power.PowerPlanEngine.Instance.ApplyPowerPlanAsync(guid, BiosOptimizer.Core.Implementations.Power.PowerPlanChangeSource.USER_REQUEST, ct);
                return Ok(JsonSerializer.Serialize(res));
            }
            catch (Exception ex)
            {
                return Fail($"Failed to apply power plan: {ex.Message}");
            }
        }

        private async Task<IpcResponse> RestorePowerPlanAsync(CancellationToken ct)
        {
            try
            {
                var res = await BiosOptimizer.Core.Implementations.Power.PowerPlanEngine.Instance.RestorePreviousPowerPlanAsync(ct);
                return Ok(JsonSerializer.Serialize(res));
            }
            catch (Exception ex)
            {
                return Fail($"Failed to restore power plan: {ex.Message}");
            }
        }

        private async Task<IpcResponse> EnableUltimatePerformanceAsync(CancellationToken ct)
        {
            try
            {
                var res = await BiosOptimizer.Core.Implementations.Power.PowerPlanEngine.Instance.EnableUltimatePerformanceAsync(ct);
                return Ok(JsonSerializer.Serialize(res));
            }
            catch (Exception ex)
            {
                return Fail($"Failed to enable Ultimate Performance: {ex.Message}");
            }
        }

        private async Task<IpcResponse> CreateCustomAiPlanAsync(CancellationToken ct)
        {
            try
            {
                var res = await BiosOptimizer.Core.Implementations.Power.PowerPlanEngine.Instance.CreateCustomAiPlanAsync(ct);
                return Ok(JsonSerializer.Serialize(res));
            }
            catch (Exception ex)
            {
                return Fail($"Failed to create custom AI power plan: {ex.Message}");
            }
        }

        private async Task<IpcResponse> DeleteCustomAiPlanAsync(string? guid, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(guid)) return Fail("GUID cannot be empty.");
                var res = await BiosOptimizer.Core.Implementations.Power.PowerPlanEngine.Instance.DeleteCustomAiPlanAsync(guid, ct);
                return Ok(JsonSerializer.Serialize(res));
            }
            catch (Exception ex)
            {
                return Fail($"Failed to delete custom AI power plan: {ex.Message}");
            }
        }

        private async Task<IpcResponse> CreateRestorePointAsync(string? payload)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var res = BiosOptimizer.Core.Implementations.WindowsSystemRestoreEngine.Instance.CreateRestorePoint(payload);
                    string json = JsonSerializer.Serialize(res);
                    return new IpcResponse
                    {
                        Success = res.Success,
                        Data = json,
                        ErrorMessage = res.Success ? null : (res.ErrorMessage ?? res.StatusMessage)
                    };
                }
                catch (Exception ex)
                {
                    return Fail($"System Restore execution exception: {ex.Message}");
                }
            });
        }

        private async Task<IpcResponse> GetRestorePointsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var list = BiosOptimizer.Core.Implementations.WindowsSystemRestoreEngine.Instance.EnumerateRestorePoints();
                    return Ok(JsonSerializer.Serialize(list));
                }
                catch (Exception ex)
                {
                    return Fail($"Failed to enumerate restore points: {ex.Message}");
                }
            });
        }

        private async Task<IpcResponse> CheckSystemRestoreStatusAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var (isAvail, reason) = BiosOptimizer.Core.Implementations.WindowsSystemRestoreEngine.Instance.CheckSystemRestoreAvailability();
                    return Ok(JsonSerializer.Serialize(new { IsAvailable = isAvail, Reason = reason }));
                }
                catch (Exception ex)
                {
                    return Fail($"Failed to check system restore status: {ex.Message}");
                }
            });
        }

        private async Task<IpcResponse> GetSystemProtectionStatusAsync(string? payload)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var (isEnabled, drive, reason) = BiosOptimizer.Core.Implementations.WindowsSystemRestoreEngine.Instance.CheckSystemProtectionStatus(payload);
                    return Ok(JsonSerializer.Serialize(new { IsEnabled = isEnabled, Drive = drive, Reason = reason }));
                }
                catch (Exception ex)
                {
                    return Fail($"Failed to check system protection status: {ex.Message}");
                }
            });
        }

        private async Task<IpcResponse> EnableSystemProtectionAsync(string? payload)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var (success, message) = BiosOptimizer.Core.Implementations.WindowsSystemRestoreEngine.Instance.EnableSystemProtection(payload);
                    return new IpcResponse
                    {
                        Success = success,
                        Data = JsonSerializer.Serialize(new { Success = success, Message = message }),
                        ErrorMessage = success ? null : message
                    };
                }
                catch (Exception ex)
                {
                    return Fail($"Failed to enable system protection: {ex.Message}");
                }
            });
        }

        private async Task<IpcResponse> DisableSystemProtectionAsync(string? payload)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var (success, message) = BiosOptimizer.Core.Implementations.WindowsSystemRestoreEngine.Instance.DisableSystemProtection(payload);
                    return new IpcResponse
                    {
                        Success = success,
                        Data = JsonSerializer.Serialize(new { Success = success, Message = message }),
                        ErrorMessage = success ? null : message
                    };
                }
                catch (Exception ex)
                {
                    return Fail($"Failed to disable system protection: {ex.Message}");
                }
            });
        }

        private async Task<IpcResponse> ExecuteServicingToolAsync(string? payload, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(payload)) return Fail("Tool ID payload is required.");

                var req = JsonSerializer.Deserialize<ServicingToolRequestDto>(payload) 
                    ?? new ServicingToolRequestDto { ToolId = payload };

                var timeout = req.TimeoutSeconds > 0 ? TimeSpan.FromSeconds(req.TimeoutSeconds) : TimeSpan.FromHours(2);

                var res = await WindowsServicingHealthEngine.Instance.ExecuteServicingToolAsync(
                    req.ToolId,
                    req.Arguments,
                    timeout,
                    ct
                );

                var dto = new ServicingToolResponseDto
                {
                    ToolId = res.ToolId,
                    ToolName = res.ToolName,
                    Command = res.Command,
                    ProcessStarted = res.ProcessStarted,
                    ProcessId = res.ProcessId,
                    ExitCode = res.ExitCode,
                    Success = res.Success,
                    Summary = res.Summary,
                    DiagnosedRootCause = res.DiagnosedRootCause,
                    RecommendedRemediation = res.RecommendedRemediation,
                    DurationSeconds = res.Duration.TotalSeconds,
                    IsCancelled = res.IsCancelled,
                    IsTimedOut = res.IsTimedOut,
                    IsNotApplicable = res.IsNotApplicable,
                    StandardOutput = res.StandardOutput,
                    StandardError = res.StandardError,
                    RelevantLogExcerpts = res.RelevantLogExcerpts
                };

                return Ok(JsonSerializer.Serialize(dto));
            }
            catch (Exception ex)
            {
                return Fail($"Failed to execute servicing tool: {ex.Message}");
            }
        }

        private async Task<IpcResponse> GetServicingEnvironmentAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var snap = WindowsServicingHealthEngine.Instance.DetectServicingEnvironment();
                    return Ok(JsonSerializer.Serialize(snap));
                }
                catch (Exception ex)
                {
                    return Fail($"Failed to query servicing environment: {ex.Message}");
                }
            });
        }

        private async Task<IpcResponse> EnsureServicingPrerequisitesAsync(string? payload)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string toolId = payload ?? "sfc";
                    var (repaired, msg) = WindowsServicingHealthEngine.Instance.EnsureServicingPrerequisites(toolId);
                    return Ok(JsonSerializer.Serialize(new { Repaired = repaired, Message = msg }));
                }
                catch (Exception ex)
                {
                    return Fail($"Failed to remediate servicing prerequisites: {ex.Message}");
                }
            });
        }
    }
}