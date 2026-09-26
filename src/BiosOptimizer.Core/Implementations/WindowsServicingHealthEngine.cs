#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace BiosOptimizer.Core.Implementations
{
    public class ServicingServiceStatus
    {
        public string ServiceName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Status { get; set; } = "Unknown";
        public string StartType { get; set; } = "Unknown";
        public bool IsHealthy { get; set; }
        public string HealthDetails { get; set; } = string.Empty;
    }

    public class ServicingEnvironmentSnapshot
    {
        // OS & Architecture
        public string OsVersion { get; set; } = string.Empty;
        public string OsBuild { get; set; } = string.Empty;
        public string OsEdition { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public bool Is64BitOperatingSystem { get; set; }
        public bool Is64BitProcess { get; set; }

        // Security & Elevation
        public bool IsElevated { get; set; }
        public bool IsSystemAccount { get; set; }

        // Storage & Drive
        public string SystemDrive { get; set; } = "C:";
        public long FreeDiskSpaceBytes { get; set; }
        public long TotalDiskSpaceBytes { get; set; }
        public double FreeDiskSpaceGb { get; set; }
        public bool HasSufficientDiskSpace { get; set; }

        // Binary Availability
        public bool IsSfcAvailable { get; set; }
        public string SfcPath { get; set; } = string.Empty;
        public bool IsDismAvailable { get; set; }
        public string DismPath { get; set; } = string.Empty;

        // Servicing State & Locks
        public bool IsRebootPending { get; set; }
        public string RebootPendingReason { get; set; } = "None";
        public List<string> ActiveConflictingProcesses { get; set; } = new();
        public bool IsServicingLocked => ActiveConflictingProcesses.Count > 0;

        // Required Services
        public Dictionary<string, ServicingServiceStatus> RequiredServices { get; set; } = new();

        // Overall Readiness
        public string ReadinessState { get; set; } = "READY";
        public string ReadinessMessage { get; set; } = "Windows Servicing environment is healthy and ready.";
        public List<string> Warnings { get; set; } = new();
        public List<string> Blockers { get; set; } = new();

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"OS: {OsEdition} (Build {OsBuild}, {Architecture})");
            sb.AppendLine($"Elevation: {(IsElevated ? "Elevated / Administrator" : "NON-ELEVATED")}");
            sb.AppendLine($"System Drive: {SystemDrive} ({FreeDiskSpaceGb:F1} GB Free)");
            sb.AppendLine($"SFC: {(IsSfcAvailable ? SfcPath : "NOT AVAILABLE")}");
            sb.AppendLine($"DISM: {(IsDismAvailable ? DismPath : "NOT AVAILABLE")}");
            sb.AppendLine($"Reboot Pending: {(IsRebootPending ? $"YES ({RebootPendingReason})" : "NO")}");
            if (ActiveConflictingProcesses.Count > 0)
            {
                sb.AppendLine($"Active Servicing Processes: {string.Join(", ", ActiveConflictingProcesses)}");
            }
            sb.AppendLine($"Readiness: {ReadinessState} ({ReadinessMessage})");
            return sb.ToString();
        }
    }

    public class ServicingToolExecutionResult
    {
        public string ToolId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public bool ProcessStarted { get; set; }
        public int ProcessId { get; set; }
        public int ExitCode { get; set; } = -1;
        public bool Success { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string DiagnosedRootCause { get; set; } = string.Empty;
        public string RecommendedRemediation { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public bool IsCancelled { get; set; }
        public bool IsTimedOut { get; set; }
        public bool IsNotApplicable { get; set; }
        public string StandardOutput { get; set; } = string.Empty;
        public string StandardError { get; set; } = string.Empty;
        public List<string> RelevantLogExcerpts { get; set; } = new();
        public ServicingEnvironmentSnapshot? EnvironmentSnapshot { get; set; }
    }

    public sealed class WindowsServicingHealthEngine
    {
        private static readonly Lazy<WindowsServicingHealthEngine> _instance =
            new(() => new WindowsServicingHealthEngine());
        public static WindowsServicingHealthEngine Instance => _instance.Value;

        private readonly Regex _sfcPercentRegex = new(@"Verification\s+(\d+)%\s+complete", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly Regex _dismPercentRegex = new(@"\[=+ *(\d+[\.,]?\d*)% *=+\]", RegexOptions.Compiled);
        private readonly Regex _dismGenericPercentRegex = new(@"(\d+[\.,]\d+)%", RegexOptions.Compiled);

        private WindowsServicingHealthEngine() { }

        // =========================================================================
        // 1. DETECT WINDOWS SERVICING ENVIRONMENT
        // =========================================================================

        public ServicingEnvironmentSnapshot DetectServicingEnvironment()
        {
            var snap = new ServicingEnvironmentSnapshot();

            try
            {
                // 1. Architecture
                snap.Is64BitOperatingSystem = Environment.Is64BitOperatingSystem;
                snap.Is64BitProcess = Environment.Is64BitProcess;
                snap.Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";

                // 2. OS Details
                snap.OsVersion = Environment.OSVersion.VersionString;
                try
                {
                    using var rk = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                    if (rk != null)
                    {
                        snap.OsEdition = rk.GetValue("ProductName")?.ToString() ?? "Windows";
                        string displayVer = rk.GetValue("DisplayVersion")?.ToString() ?? rk.GetValue("ReleaseId")?.ToString() ?? "";
                        string build = rk.GetValue("CurrentBuild")?.ToString() ?? rk.GetValue("CurrentBuildNumber")?.ToString() ?? "";
                        string ubr = rk.GetValue("UBR")?.ToString() ?? "";

                        snap.OsBuild = !string.IsNullOrEmpty(ubr) ? $"{build}.{ubr}" : build;
                        if (!string.IsNullOrEmpty(displayVer)) snap.OsEdition += $" {displayVer}";
                    }
                }
                catch
                {
                    snap.OsEdition = Environment.OSVersion.ToString();
                    snap.OsBuild = Environment.OSVersion.Version.Build.ToString();
                }

                // 3. Elevation Check
                try
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    snap.IsElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
                    snap.IsSystemAccount = identity.IsSystem;
                }
                catch
                {
                    snap.IsElevated = false;
                }

                // 4. System Drive & Disk Space
                string sysRoot = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
                snap.SystemDrive = sysRoot;
                try
                {
                    var driveInfo = new DriveInfo(sysRoot);
                    snap.FreeDiskSpaceBytes = driveInfo.AvailableFreeSpace;
                    snap.TotalDiskSpaceBytes = driveInfo.TotalSize;
                    snap.FreeDiskSpaceGb = snap.FreeDiskSpaceBytes / (1024.0 * 1024.0 * 1024.0);
                    snap.HasSufficientDiskSpace = snap.FreeDiskSpaceGb >= 3.0; // 3 GB minimum for DISM/SFC staging
                }
                catch
                {
                    snap.HasSufficientDiskSpace = true;
                }

                // 5. Binary Availability
                snap.SfcPath = TaskExecutionSupervisor.ResolveNativeSystemBinary("sfc.exe");
                snap.IsSfcAvailable = File.Exists(snap.SfcPath);

                snap.DismPath = TaskExecutionSupervisor.ResolveNativeSystemBinary("dism.exe");
                snap.IsDismAvailable = File.Exists(snap.DismPath);

                // 6. Required Services State
                snap.RequiredServices["TrustedInstaller"] = InspectService("TrustedInstaller", "Windows Modules Installer");
                snap.RequiredServices["wuauserv"] = InspectService("wuauserv", "Windows Update");
                snap.RequiredServices["CryptSvc"] = InspectService("CryptSvc", "Cryptographic Services");
                snap.RequiredServices["BITS"] = InspectService("BITS", "Background Intelligent Transfer Service");

                // 7. Reboot Pending State
                snap.IsRebootPending = CheckPendingReboot(out string rebootReason);
                snap.RebootPendingReason = rebootReason;

                // 8. Active Conflicting Servicing Processes
                snap.ActiveConflictingProcesses = DetectActiveServicingProcesses();

                // 9. Overall Readiness Assessment
                if (!snap.IsSfcAvailable && !snap.IsDismAvailable)
                {
                    snap.ReadinessState = "BLOCKED";
                    snap.ReadinessMessage = "Neither SFC nor DISM executable was found on this system.";
                    snap.Blockers.Add("SFC / DISM executables missing from System32.");
                }
                else if (!snap.IsElevated)
                {
                    snap.ReadinessState = "REQUIRES ADMIN";
                    snap.ReadinessMessage = "Administrative elevation is required to execute Windows Servicing operations.";
                    snap.Warnings.Add("Administrator elevation required.");
                }
                else if (!snap.HasSufficientDiskSpace)
                {
                    snap.ReadinessState = "WARNING";
                    snap.ReadinessMessage = $"Low disk space on {snap.SystemDrive} ({snap.FreeDiskSpaceGb:F1} GB free). Servicing operations require at least 3.0 GB.";
                    snap.Warnings.Add($"Low disk space: {snap.FreeDiskSpaceGb:F1} GB available.");
                }
                else if (snap.RequiredServices.TryGetValue("TrustedInstaller", out var ti) && !ti.IsHealthy)
                {
                    snap.ReadinessState = "DEGRADED";
                    snap.ReadinessMessage = "Windows Modules Installer (TrustedInstaller) is disabled or degraded.";
                    snap.Warnings.Add("TrustedInstaller service is disabled.");
                }
                else if (snap.IsRebootPending)
                {
                    snap.ReadinessState = "REBOOT PENDING";
                    snap.ReadinessMessage = $"A system restart is pending: {snap.RebootPendingReason}";
                    snap.Warnings.Add($"Reboot pending: {snap.RebootPendingReason}");
                }
                else if (snap.IsServicingLocked)
                {
                    snap.ReadinessState = "BUSY";
                    snap.ReadinessMessage = $"Active Windows Servicing operations in progress: {string.Join(", ", snap.ActiveConflictingProcesses)}";
                    snap.Warnings.Add($"Servicing process active: {string.Join(", ", snap.ActiveConflictingProcesses)}");
                }
                else
                {
                    snap.ReadinessState = "READY";
                    snap.ReadinessMessage = "Windows Servicing environment is healthy and ready.";
                }
            }
            catch (Exception ex)
            {
                snap.ReadinessState = "WARNING";
                snap.ReadinessMessage = $"Diagnostics encounter: {ex.Message}";
            }

            return snap;
        }

        private ServicingServiceStatus InspectService(string serviceName, string displayName)
        {
            var status = new ServicingServiceStatus
            {
                ServiceName = serviceName,
                DisplayName = displayName
            };

            try
            {
                // Read start type from registry
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
                if (key != null)
                {
                    var startVal = key.GetValue("Start");
                    if (startVal is int startInt)
                    {
                        status.StartType = startInt switch
                        {
                            2 => "Automatic",
                            3 => "Manual (Demand)",
                            4 => "Disabled",
                            _ => $"Type {startInt}"
                        };
                    }
                }

                // Query live state via ServiceController
                try
                {
                    using var sc = new ServiceController(serviceName);
                    status.Status = sc.Status.ToString();
                }
                catch
                {
                    status.Status = "Not Registered";
                }

                // TrustedInstaller is standardly "Manual (Demand)" and "Stopped" when idle. It only fails if "Disabled".
                bool isDisabled = status.StartType == "Disabled";
                status.IsHealthy = !isDisabled;
                status.HealthDetails = isDisabled
                    ? $"{displayName} is DISABLED in service configuration. Servicing tools will fail."
                    : $"{displayName} is ready (Start: {status.StartType}, State: {status.Status}).";
            }
            catch (Exception ex)
            {
                status.Status = "Query Error";
                status.StartType = "Unknown";
                status.IsHealthy = true;
                status.HealthDetails = ex.Message;
            }

            return status;
        }

        private bool CheckPendingReboot(out string reason)
        {
            try
            {
                using var cbsReboot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
                if (cbsReboot != null)
                {
                    reason = "Component Based Servicing (CBS) has completed packages requiring restart";
                    return true;
                }

                using var cbsInProgress = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootInProgress");
                if (cbsInProgress != null)
                {
                    reason = "Servicing package installation in progress";
                    return true;
                }

                using var wuReboot = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
                if (wuReboot != null)
                {
                    reason = "Windows Update has staged updates requiring restart";
                    return true;
                }

                using var sessionMgr = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
                if (sessionMgr != null)
                {
                    var renameOps = sessionMgr.GetValue("PendingFileRenameOperations");
                    if (renameOps is string[] arr && arr.Length > 0)
                    {
                        reason = "Pending system file rename operations queued";
                        return true;
                    }
                }
            }
            catch { }

            reason = "None";
            return false;
        }

        private List<string> DetectActiveServicingProcesses()
        {
            var results = new List<string>();
            string[] targetProcessNames = { "TiWorker", "TrustedInstaller", "dism", "dismhost", "sfc" };

            try
            {
                int currentPid = Environment.ProcessId;
                foreach (var name in targetProcessNames)
                {
                    var procs = Process.GetProcessesByName(name);
                    foreach (var p in procs)
                    {
                        try
                        {
                            if (p.Id != currentPid)
                            {
                                results.Add($"{p.ProcessName} (PID: {p.Id})");
                            }
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
            }
            catch { }

            return results;
        }

        // =========================================================================
        // 2. AUTOMATIC PREREQUISITE AUTO-REPAIR
        // =========================================================================

        /// <summary>
        /// Ensures prerequisite services (TrustedInstaller, CryptSvc, wuauserv) are enabled and ready.
        /// If a customer has disabled TrustedInstaller or CryptSvc, this remediates the service start type.
        /// </summary>
        public (bool Repaired, string Message) EnsureServicingPrerequisites(string toolId)
        {
            var repairs = new List<string>();

            try
            {
                // 1. TrustedInstaller (Required for SFC & DISM)
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TrustedInstaller", writable: true);
                    if (key != null)
                    {
                        var startVal = key.GetValue("Start");
                        if (startVal is int startInt && startInt == 4) // Disabled
                        {
                            key.SetValue("Start", 3, RegistryValueKind.DWord); // Set to Manual (Demand)
                            repairs.Add("Windows Modules Installer (TrustedInstaller) set from Disabled to Manual");
                        }
                    }
                }
                catch { }

                // 2. Cryptographic Services (CryptSvc)
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\CryptSvc", writable: true);
                    if (key != null)
                    {
                        var startVal = key.GetValue("Start");
                        if (startVal is int startInt && startInt == 4) // Disabled
                        {
                            key.SetValue("Start", 2, RegistryValueKind.DWord); // Automatic
                            repairs.Add("Cryptographic Services (CryptSvc) set from Disabled to Automatic");
                        }
                    }
                }
                catch { }

                // 3. Windows Update (wuauserv) — needed for DISM RestoreHealth
                if (toolId.Contains("restore", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\wuauserv", writable: true);
                        if (key != null)
                        {
                            var startVal = key.GetValue("Start");
                            if (startVal is int startInt && startInt == 4) // Disabled
                            {
                                key.SetValue("Start", 3, RegistryValueKind.DWord); // Manual
                                repairs.Add("Windows Update (wuauserv) set from Disabled to Manual for component store retrieval");
                            }
                        }
                    }
                    catch { }
                }

                if (repairs.Count > 0)
                {
                    return (true, string.Join("; ", repairs));
                }

                return (false, "Prerequisites are already healthy.");
            }
            catch (Exception ex)
            {
                return (false, $"Prerequisite check notice: {ex.Message}");
            }
        }

        // =========================================================================
        // 3. EXECUTE SERVICING TOOL WITH STRICT PROCESS RULES & FULL TELEMETRY
        // =========================================================================

        public async Task<ServicingToolExecutionResult> ExecuteServicingToolAsync(
            string toolId,
            string? customArguments = null,
            TimeSpan? timeout = null,
            CancellationToken ct = default,
            Action<string>? onOutputLine = null,
            Action<string>? onHeartbeat = null,
            Action<int, string>? onProgressUpdate = null,
            Action<int, string>? onProcessStarted = null)
        {
            var envSnapshot = DetectServicingEnvironment();
            var result = new ServicingToolExecutionResult
            {
                ToolId = toolId,
                EnvironmentSnapshot = envSnapshot
            };

            // 1. Determine Binary & Arguments
            string binaryName;
            string defaultArgs;
            TimeSpan safetyTimeout = timeout ?? TimeSpan.FromHours(2); // 2 hours safety limit

            switch (toolId.ToLowerInvariant())
            {
                case "sfc":
                case "tool.repair.sfc":
                    result.ToolName = "System File Checker (SFC)";
                    binaryName = envSnapshot.SfcPath;
                    defaultArgs = "/scannow";
                    safetyTimeout = timeout ?? TimeSpan.FromHours(1);
                    break;

                case "dism_check":
                case "tool.repair.dism_check":
                    result.ToolName = "DISM Component Health Check";
                    binaryName = envSnapshot.DismPath;
                    defaultArgs = "/online /cleanup-image /scanhealth";
                    break;

                case "dism_restore":
                case "tool.repair.dism_restore":
                    result.ToolName = "DISM Component Store Restore";
                    binaryName = envSnapshot.DismPath;
                    defaultArgs = "/online /cleanup-image /restorehealth";
                    break;

                case "dism_cleanup":
                case "tool.repair.component_cleanup":
                    result.ToolName = "WinSxS Component Store Cleanup";
                    binaryName = envSnapshot.DismPath;
                    defaultArgs = "/online /cleanup-image /startcomponentcleanup /resetbase";
                    break;

                default:
                    result.ToolName = toolId;
                    binaryName = toolId;
                    defaultArgs = "";
                    break;
            }

            string arguments = customArguments ?? defaultArgs;

            // 2. Check Binary Existence
            if (!File.Exists(binaryName))
            {
                result.ProcessStarted = false;
                result.IsNotApplicable = true;
                result.Success = false;
                result.ExitCode = -1;
                result.Summary = $"Tool executable '{binaryName}' is not available in this Windows environment.";
                result.DiagnosedRootCause = "Windows Servicing binary does not exist in native System32 or Sysnative.";
                result.RecommendedRemediation = "Verify that Windows System32 directory contains required servicing tools.";
                return result;
            }

            // 3. Auto-Remediate Disabled Prerequisites
            var (repaired, repairMsg) = EnsureServicingPrerequisites(toolId);
            if (repaired)
            {
                onOutputLine?.Invoke($"[PRE-FLIGHT REPAIR] {repairMsg}");
            }

            // 4. Supervised Process Execution
            var sw = Stopwatch.StartNew();

            var execRes = await TaskExecutionSupervisor.ExecuteProcessAsync(
                binaryName,
                arguments,
                safetyTimeout,
                ct,
                line =>
                {
                    onOutputLine?.Invoke(line);

                    // Parse real progress percent
                    if (toolId.Contains("sfc", StringComparison.OrdinalIgnoreCase))
                    {
                        var m = _sfcPercentRegex.Match(line);
                        if (m.Success && int.TryParse(m.Groups[1].Value, out int pct))
                        {
                            onProgressUpdate?.Invoke(pct, $"System file verification: {pct}% complete");
                        }
                    }
                    else // DISM
                    {
                        var m = _dismPercentRegex.Match(line);
                        if (!m.Success) m = _dismGenericPercentRegex.Match(line);

                        if (m.Success)
                        {
                            string numStr = m.Groups[1].Value.Replace(',', '.');
                            if (double.TryParse(numStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pctD))
                            {
                                int pct = (int)Math.Round(pctD);
                                onProgressUpdate?.Invoke(pct, $"DISM servicing progress: {pctD:F1}% complete");
                            }
                        }
                    }
                },
                onErrorLine: line => onOutputLine?.Invoke($"[STDERR] {line}"),
                onHeartbeat: heartbeat => onHeartbeat?.Invoke(heartbeat),
                onProcessStarted: (pid, name) => onProcessStarted?.Invoke(pid, name)
            );

            sw.Stop();

            result.Command = execRes.Command;
            result.ProcessStarted = execRes.ProcessStarted;
            result.ProcessId = execRes.ProcessId;
            result.ExitCode = execRes.ExitCode;
            result.StandardOutput = execRes.StandardOutput;
            result.StandardError = execRes.StandardError;
            result.Duration = sw.Elapsed;
            result.IsCancelled = execRes.IsCancelled;
            result.IsTimedOut = execRes.IsTimedOut;

            // 5. Interpret Exit Code, Stdout, and Real Log Excerpts
            InterpretResult(result);

            return result;
        }

        // =========================================================================
        // 4. POST-VERIFICATION & DIAGNOSIS
        // =========================================================================

        private void InterpretResult(ServicingToolExecutionResult res)
        {
            if (res.IsCancelled)
            {
                res.Success = false;
                res.Summary = "Operation cancelled by user.";
                res.DiagnosedRootCause = "User requested cancellation.";
                return;
            }

            if (res.IsTimedOut)
            {
                res.Success = false;
                res.Summary = $"Operation timed out after {res.Duration.TotalMinutes:F0} minutes.";
                res.DiagnosedRootCause = "Execution exceeded safety timeout limit. Slow storage drive or locked servicing transaction.";
                res.RecommendedRemediation = "Ensure no background Windows Update is running, reboot the PC, and retry.";
                return;
            }

            string stdout = res.StandardOutput;
            string stderr = res.StandardError;
            int code = res.ExitCode;

            // ── SFC Interpretation ───────────────────────────────────────────
            if (res.ToolId.Contains("sfc", StringComparison.OrdinalIgnoreCase))
            {
                if (stdout.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase))
                {
                    res.Success = true;
                    res.Summary = "Windows Resource Protection did not find any integrity violations.";
                    res.DiagnosedRootCause = "All protected system files match official Microsoft manifest signatures.";
                    res.RecommendedRemediation = "No action required. System file integrity is optimal.";
                }
                else if (stdout.Contains("successfully repaired them", StringComparison.OrdinalIgnoreCase))
                {
                    res.Success = true;
                    res.Summary = "Windows Resource Protection found corrupt files and successfully repaired them.";
                    res.DiagnosedRootCause = "Corrupted system binaries were restored from the local component store.";
                    res.RecommendedRemediation = "Corrupted files resolved. A reboot is recommended to finalize changes.";
                }
                else if (stdout.Contains("found corrupt files but was unable to fix some of them", StringComparison.OrdinalIgnoreCase) || code == 1)
                {
                    res.Success = false;
                    res.Summary = "Corrupted system files detected that could not be repaired by SFC.";
                    res.DiagnosedRootCause = "Local Component Store (WinSxS) lacks clean payload copies for the corrupted files.";
                    res.RecommendedRemediation = "Run 'DISM Component Store Restore' to download fresh payloads, then re-run SFC.";
                    ExtractCbsLogExcerpts(res);
                }
                else if (stdout.Contains("could not start the repair service", StringComparison.OrdinalIgnoreCase) || code == 2)
                {
                    res.Success = false;
                    res.Summary = "Windows Resource Protection could not start the repair service.";
                    res.DiagnosedRootCause = "The Windows Modules Installer (TrustedInstaller) service is disabled or could not be initialized.";
                    res.RecommendedRemediation = "Ensure TrustedInstaller service is set to Manual and launch Error Optimizer as Administrator.";
                }
                else if (stdout.Contains("system repair pending which requires reboot", StringComparison.OrdinalIgnoreCase) || code == 3)
                {
                    res.Success = false;
                    res.Summary = "System repair pending which requires a reboot to complete.";
                    res.DiagnosedRootCause = "Previous Windows Update or component servicing is waiting for a restart to finalize file replacements.";
                    res.RecommendedRemediation = "Restart your computer to complete pending repairs, then run SFC again.";
                }
                else if (code == 5 || stdout.Contains("must be an administrator", StringComparison.OrdinalIgnoreCase))
                {
                    res.Success = false;
                    res.Summary = "Administrator privileges required to run System File Checker.";
                    res.DiagnosedRootCause = "The current process token lacks administrative privileges (ERROR_ACCESS_DENIED).";
                    res.RecommendedRemediation = "Restart Error Optimizer with 'Run as administrator' or ensure the elevated service is running.";
                }
                else if (code == 0)
                {
                    res.Success = true;
                    res.Summary = "System File Checker scan completed successfully.";
                    res.DiagnosedRootCause = "Process exited with code 0.";
                }
                else
                {
                    res.Success = false;
                    res.Summary = $"SFC scan ended with exit code {code}.";
                    res.DiagnosedRootCause = $"Unrecognized SFC exit code {code}: {GetHResultDescription(code)}";
                    res.RecommendedRemediation = "Check CBS.log for detailed diagnostics.";
                    ExtractCbsLogExcerpts(res);
                }
                return;
            }

            // ── DISM Interpretation ──────────────────────────────────────────
            bool hasDismSuccessText = stdout.Contains("The operation completed successfully", StringComparison.OrdinalIgnoreCase) ||
                                     stdout.Contains("The restore operation completed successfully", StringComparison.OrdinalIgnoreCase) ||
                                     stdout.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase);

            if (hasDismSuccessText && code == 0)
            {
                res.Success = true;
                if (stdout.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase))
                {
                    res.Summary = "No component store corruption detected.";
                    res.DiagnosedRootCause = "Windows Component Store (WinSxS) is healthy.";
                    res.RecommendedRemediation = "No action required.";
                }
                else if (stdout.Contains("The restore operation completed successfully", StringComparison.OrdinalIgnoreCase))
                {
                    res.Summary = "The component store restore operation completed successfully.";
                    res.DiagnosedRootCause = "Corrupted packages were restored and verified against Microsoft servicing manifests.";
                    res.RecommendedRemediation = "Component store restored. Run SFC scan to repair any dependent system files.";
                }
                else
                {
                    res.Summary = "DISM servicing operation completed successfully.";
                    res.DiagnosedRootCause = "Operation finished with exit code 0.";
                }
                return;
            }

            if (stdout.Contains("The component store is repairable", StringComparison.OrdinalIgnoreCase))
            {
                res.Success = true;
                res.Summary = "Component store corruption detected — repairable via RestoreHealth.";
                res.DiagnosedRootCause = "Manifest inconsistencies detected, but repairable packages are indexed.";
                res.RecommendedRemediation = "Run 'DISM Component Store Restore' to apply package fixes.";
                return;
            }

            // Failure Diagnosis based on code & HRESULT
            res.Success = false;

            switch (code)
            {
                case 87:
                    res.IsNotApplicable = true;
                    res.Summary = "DISM command parameter not supported on this Windows edition/build (Error 87).";
                    res.DiagnosedRootCause = "The requested DISM parameter is not applicable to the active Windows image.";
                    res.RecommendedRemediation = "Use standard ScanHealth or CheckHealth parameters.";
                    break;

                case 112:
                    res.Summary = "Insufficient disk space on system volume (Error 112).";
                    res.DiagnosedRootCause = "DISM staging directory ran out of free space during package decompression.";
                    res.RecommendedRemediation = $"Free up at least 3.0 GB on {res.EnvironmentSnapshot?.SystemDrive ?? "C:"} and retry.";
                    break;

                case 5:
                case -2147024891: // 0x80070005
                    res.Summary = "Administrator privileges required to execute DISM (Error 0x80070005).";
                    res.DiagnosedRootCause = "Access Denied. Elevated token is required for component servicing.";
                    res.RecommendedRemediation = "Run Error Optimizer as Administrator.";
                    break;

                case 3017:
                case -2146498529: // 0x800f081f (CBS_E_SOURCE_NOT_FOUND)
                    res.Summary = "DISM Error 0x800F081F: The source files could not be found.";
                    res.DiagnosedRootCause = "The files required to repair the component store are missing from local cache and Windows Update was unreachable.";
                    res.RecommendedRemediation = "Ensure Windows Update service (wuauserv) is enabled and an active internet connection is available.";
                    ExtractDismLogExcerpts(res);
                    break;

                case -2147023838: // 0x80070422 (ERROR_SERVICE_DISABLED)
                    res.Summary = "DISM Error 0x80070422: A required service is disabled.";
                    res.DiagnosedRootCause = "Windows Update (wuauserv) or Windows Modules Installer (TrustedInstaller) is disabled in service configuration.";
                    res.RecommendedRemediation = "Enable Windows Update and TrustedInstaller services and retry.";
                    break;

                case -2147024864: // 0x80070020 (ERROR_SHARING_VIOLATION)
                    res.Summary = "DISM Error 0x80070020: The component store is locked by another process.";
                    res.DiagnosedRootCause = "A background Windows Update (TiWorker.exe) is currently holding exclusive lock on the Component Store.";
                    res.RecommendedRemediation = "Wait a few minutes for background servicing to complete, or restart your PC.";
                    break;

                case -2146498554: // 0x800f0806 (CBS_E_PENDING)
                    res.Summary = "DISM Error 0x800F0806: A system reboot is pending.";
                    res.DiagnosedRootCause = "Pending servicing stack operations must complete before component repairs can be applied.";
                    res.RecommendedRemediation = "Restart your computer and re-run DISM.";
                    break;

                case -2146498522: // 0x800f0906 (CBS_E_DOWNLOAD_FAILURE)
                    res.Summary = "DISM Error 0x800F0906: The source files could not be downloaded.";
                    res.DiagnosedRootCause = "Windows Update connection timed out or was blocked by firewall/proxy.";
                    res.RecommendedRemediation = "Verify internet connection and ensure Windows Update endpoints are reachable.";
                    break;

                case -2146498521: // 0x800f0907
                    res.Summary = "DISM Error 0x800F0907: Servicing source is blocked by Group Policy.";
                    res.DiagnosedRootCause = "Group policy 'Specify settings for optional component installation and component repair' restricts download.";
                    res.RecommendedRemediation = "Configure Group Policy to allow contacting Windows Update for repair.";
                    break;

                default:
                    res.Summary = $"DISM completed with exit code {code} ({GetHResultDescription(code)}).";
                    res.DiagnosedRootCause = $"Exit code: {code}. Standard output details: {stdout.Split('\n').LastOrDefault()?.Trim()}";
                    res.RecommendedRemediation = "Inspect DISM.log for precise failure details.";
                    ExtractDismLogExcerpts(res);
                    break;
            }
        }

        private void ExtractCbsLogExcerpts(ServicingToolExecutionResult res)
        {
            try
            {
                string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string cbsLog = Path.Combine(windir, "Logs", "CBS", "CBS.log");
                if (File.Exists(cbsLog))
                {
                    var lines = new List<string>();
                    using var fs = new FileStream(cbsLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);

                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Contains("[SR]", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("Corrupt", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("Error", StringComparison.OrdinalIgnoreCase))
                        {
                            lines.Add(line.Trim());
                            if (lines.Count > 15) lines.RemoveAt(0);
                        }
                    }

                    if (lines.Count > 0)
                    {
                        res.RelevantLogExcerpts = lines;
                    }
                }
            }
            catch { }
        }

        private void ExtractDismLogExcerpts(ServicingToolExecutionResult res)
        {
            try
            {
                string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string dismLog = Path.Combine(windir, "Logs", "DISM", "dism.log");
                if (File.Exists(dismLog))
                {
                    var lines = new List<string>();
                    using var fs = new FileStream(dismLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs);

                    string? line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("0x800", StringComparison.OrdinalIgnoreCase))
                        {
                            lines.Add(line.Trim());
                            if (lines.Count > 15) lines.RemoveAt(0);
                        }
                    }

                    if (lines.Count > 0)
                    {
                        res.RelevantLogExcerpts = lines;
                    }
                }
            }
            catch { }
        }

        private static string GetHResultDescription(int code)
        {
            return code switch
            {
                0 => "S_OK",
                1 => "ERROR_INVALID_FUNCTION / CORRUPTION_DETECTED",
                2 => "ERROR_FILE_NOT_FOUND / SERVICE_CANNOT_START",
                3 => "ERROR_PATH_NOT_FOUND / REBOOT_PENDING",
                5 => "ERROR_ACCESS_DENIED",
                87 => "ERROR_INVALID_PARAMETER",
                112 => "ERROR_DISK_FULL",
                -2146498529 => "CBS_E_SOURCE_NOT_FOUND (0x800F081F)",
                -2147023838 => "ERROR_SERVICE_DISABLED (0x80070422)",
                -2147024864 => "ERROR_SHARING_VIOLATION (0x80070020)",
                -2146498554 => "CBS_E_PENDING (0x800F0806)",
                -2146498522 => "CBS_E_DOWNLOAD_FAILURE (0x800F0906)",
                -2146498521 => "CBS_E_POLICY_RESTRICTED (0x800F0907)",
                _ => $"0x{code:X8}"
            };
        }

        // ── PUBLIC TESTABLE DIAGNOSTIC WRAPPER ───────────────────────────────────
        // Exposes the exit-code interpretation logic for unit testing.
        // Returns a lightweight diagnosis record without triggering any execution.
        public ServicingExitCodeDiagnosis DiagnoseExitCode(int exitCode, string stdout, string stderr, string toolHint)
        {
            bool isNotApplicable = false;
            string rootCause;
            string remediation;

            bool isDism = toolHint.Contains("dism", StringComparison.OrdinalIgnoreCase);

            if (isDism)
            {
                switch (exitCode)
                {
                    case 0:
                        rootCause = "Operation completed successfully.";
                        remediation = string.Empty;
                        break;
                    case 87:
                        isNotApplicable = true;
                        rootCause = "The requested DISM parameter is not applicable to the active Windows edition or build.";
                        remediation = "Verify the Windows edition supports this DISM operation.";
                        break;
                    case 112:
                        rootCause = "DISM staging directory ran out of free space during package decompression. Minimum 3 GB is required.";
                        remediation = "Free at least 3 GB of disk space on the system drive and retry.";
                        break;
                    case 5:
                        rootCause = "Access Denied. Elevated Administrator token is required for component servicing.";
                        remediation = "Ensure the backend is running as elevated or SYSTEM.";
                        break;
                    case -2146498529: // 0x800F081F
                        rootCause = "The source files for repairing the component store are missing. Windows Update (wuauserv) or CBS source path unavailable.";
                        remediation = "Enable Windows Update service or specify a /Source: path with /LimitAccess.";
                        break;
                    case -2147023838: // 0x80070422
                        rootCause = "Windows Update (wuauserv) or Windows Modules Installer (TrustedInstaller) service is disabled. DISM cannot contact the servicing stack.";
                        remediation = "Enable TrustedInstaller and wuauserv services (Start=3 or Start=2) and retry.";
                        break;
                    case -2147024864: // 0x80070020
                        rootCause = "A background Windows Update component (TiWorker.exe or TrustedInstaller.exe) is currently holding an exclusive lock on the CBS servicing database.";
                        remediation = "Wait for the background update to finish or reboot, then retry.";
                        break;
                    case -2146498554: // 0x800F0806
                        rootCause = "Pending servicing stack operations must complete before component store repair can proceed.";
                        remediation = "Reboot to allow pending operations to finalize, then retry.";
                        break;
                    default:
                        rootCause = $"DISM exit code {exitCode} ({GetHResultDescription(exitCode)}).";
                        remediation = "Review CBS.log and DISM.log for detailed analysis.";
                        break;
                }
            }
            else // SFC
            {
                switch (exitCode)
                {
                    case 0:
                        rootCause = "All protected system files match official Microsoft manifests — no integrity violations.";
                        remediation = string.Empty;
                        break;
                    case 1:
                        rootCause = "Corrupted system binaries detected but could not be fully repaired. Local Component Store (WinSxS) may lack clean payload copies.";
                        remediation = "Run DISM /Online /Cleanup-Image /RestoreHealth to rebuild the component store, then re-run SFC.";
                        break;
                    case 2:
                        rootCause = "The Windows Modules Installer (TrustedInstaller) service is disabled. SFC cannot start the repair service.";
                        remediation = "Enable TrustedInstaller service (Start=3) and retry.";
                        break;
                    case 3:
                        rootCause = "Previous Windows Update or component servicing is waiting for a reboot to complete.";
                        remediation = "Reboot the machine and run SFC again.";
                        break;
                    case 5:
                        rootCause = "The current process token lacks administrative privileges (Access Denied).";
                        remediation = "Run as Administrator or NT AUTHORITY\\SYSTEM.";
                        break;
                    case 87:
                        isNotApplicable = true;
                        rootCause = "The SFC parameter is not applicable to this Windows edition or build.";
                        remediation = string.Empty;
                        break;
                    default:
                        rootCause = $"SFC exit code {exitCode} ({GetHResultDescription(exitCode)}).";
                        remediation = "Review CBS.log for detailed analysis.";
                        break;
                }
            }

            return new ServicingExitCodeDiagnosis
            {
                ExitCode = exitCode,
                IsNotApplicable = isNotApplicable,
                RootCause = rootCause,
                Remediation = remediation
            };
        }
    }

    /// <summary>Result of a single exit-code interpretation. Used by unit tests and UI reporting.</summary>
    public class ServicingExitCodeDiagnosis
    {
        public int ExitCode { get; set; }
        public bool IsNotApplicable { get; set; }
        public string RootCause { get; set; } = string.Empty;
        public string Remediation { get; set; } = string.Empty;
    }
}