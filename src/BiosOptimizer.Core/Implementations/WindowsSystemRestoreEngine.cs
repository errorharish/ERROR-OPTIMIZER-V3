using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace BiosOptimizer.Core.Implementations
{
    public enum RestorePointStatus
    {
        CreatedAndVerified,
        SkippedByPolicy,
        SystemRestoreDisabled,
        NotElevated,
        CreationFailed,
        VerificationFailed
    }

    public class SystemRestorePointItem
    {
        public long SequenceNumber { get; set; }
        public string Description { get; set; } = string.Empty;
        public string CreationTimeString { get; set; } = string.Empty;
        public int RestorePointType { get; set; }
        public DateTime CreationTimeUtc { get; set; }
    }

    public class RestorePointExecutionResult
    {
        public RestorePointStatus Status { get; set; }
        public bool Success => Status == RestorePointStatus.CreatedAndVerified;
        public string Description { get; set; } = string.Empty;
        public long SequenceNumber { get; set; }
        public string SystemDrive { get; set; } = "C:";
        public string StatusMessage { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public SystemRestorePointItem? VerifiedPoint { get; set; }
        public int BeginWin32Status { get; set; }
        public int EndWin32Status { get; set; }
        public string DiagnosticsLog { get; set; } = string.Empty;
    }

    public class WindowsSystemRestoreEngine
    {
        private static readonly Lazy<WindowsSystemRestoreEngine> _instance = new(() => new WindowsSystemRestoreEngine());
        public static WindowsSystemRestoreEngine Instance => _instance.Value;

        #region Native Definitions & Win32 Interop

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct RESTOREPOINTINFOW
        {
            public int dwEventType;
            public int dwRestorePtType;
            public long llSequenceNumber;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szDescription;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct STATEMGRSTATUS
        {
            public int nStatus;
            public long llSequenceNumber;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = true)]
        private delegate bool SRSetRestorePointWDelegate(ref RESTOREPOINTINFOW pRestorePtSpec, out STATEMGRSTATUS pSMgrStatus);

        private const int BEGIN_SYSTEM_CHANGE = 100;
        private const int END_SYSTEM_CHANGE = 101;
        private const int APPLICATION_INSTALL = 0;
        private const int MODIFY_SETTINGS = 12;

        private const int ERROR_SUCCESS = 0;
        private const int ERROR_SERVICE_DISABLED = 1058;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string lpLibFileName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeSecurity(
            IntPtr pSecDesc,
            int cAuthSvc,
            IntPtr asAuthSvc,
            IntPtr pReserved1,
            uint dwAuthnLevel,
            uint dwImpLevel,
            IntPtr pAuthInfo,
            uint dwCapabilities,
            IntPtr pReserved3);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetVolumeNameForVolumeMountPointW(
            string lpszVolumeMountPoint,
            [Out] System.Text.StringBuilder lpszVolumeName,
            uint cchBufferLength);

        #endregion

        #region Privilege & COM Helpers

        public bool EnableProcessPrivilege(string privilegeName)
        {
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out tokenHandle))
                {
                    return false;
                }

                if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                {
                    return false;
                }

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED
                    }
                };

                return AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero)
                {
                    CloseHandle(tokenHandle);
                }
            }
        }

        public bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public string GetVolumeGuid(string drive)
        {
            try
            {
                string root = (drive.Trim().TrimEnd('\\') + "\\").ToUpperInvariant();
                var sb = new System.Text.StringBuilder(1024);
                if (GetVolumeNameForVolumeMountPointW(root, sb, (uint)sb.Capacity))
                {
                    return sb.ToString();
                }
            }
            catch { }
            return string.Empty;
        }

        public (bool IsEnabled, string SystemDrive, string StatusReason) CheckSystemProtectionStatus(string? drive = null)
        {
            string sysDrive = string.IsNullOrWhiteSpace(drive)
                ? (Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:")
                : drive.Trim().TrimEnd('\\');
            string sysDriveLetter = sysDrive.TrimEnd(':');
            string sysDriveRoot = sysDrive + "\\";

            try
            {
                // 1. Group Policy Check (DisableSR or DisableConfig)
                using (var polKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore"))
                {
                    if (polKey != null)
                    {
                        var dsr = polKey.GetValue("DisableSR");
                        if (dsr is int d && d == 1)
                        {
                            return (false, sysDrive, "System Protection is disabled by Windows Group Policy (DisableSR = 1).");
                        }
                        var dcfg = polKey.GetValue("DisableConfig");
                        if (dcfg is int dc && dc == 1)
                        {
                            return (false, sysDrive, "System Protection configuration is disabled by Windows Group Policy (DisableConfig = 1).");
                        }
                    }
                }

                // 2. SystemRestore Registry Policy Check
                using (var srKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore"))
                {
                    if (srKey != null)
                    {
                        var dsr = srKey.GetValue("DisableSR");
                        if (dsr is int d && d == 1)
                        {
                            return (false, sysDrive, "System Protection is disabled in Windows registry (DisableSR = 1).");
                        }
                    }
                }

                // 3. Disk Space Check
                var driveInfo = new DriveInfo(sysDriveRoot);
                if (driveInfo.AvailableFreeSpace < 1024L * 1024L * 1024L) // 1 GB required
                {
                    return (false, sysDrive, $"Insufficient disk space on {sysDrive} (less than 1 GB free).");
                }

                // 4. SPP Clients Registry Check (Authoritative per-drive registration in Windows 10/11)
                string volGuid = GetVolumeGuid(sysDrive);
                using (var sppKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients"))
                {
                    if (sppKey != null)
                    {
                        var clientsObj = sppKey.GetValue("{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}");
                        if (clientsObj is string[] clientsArr && clientsArr.Length > 0)
                        {
                            bool matchesDrive = clientsArr.Any(c =>
                                c.Contains($"({sysDriveLetter}%3A)", StringComparison.OrdinalIgnoreCase) ||
                                c.Contains($"({sysDriveLetter}:)", StringComparison.OrdinalIgnoreCase) ||
                                c.Contains($"({sysDrive})", StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrEmpty(volGuid) && c.Contains(volGuid.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                            );

                            if (matchesDrive)
                            {
                                return (true, sysDrive, $"System Protection is active and enabled for {sysDrive}.");
                            }
                        }
                        else if (clientsObj is string clientStr && !string.IsNullOrWhiteSpace(clientStr))
                        {
                            bool matchesDrive = clientStr.Contains($"({sysDriveLetter}%3A)", StringComparison.OrdinalIgnoreCase) ||
                                               clientStr.Contains($"({sysDriveLetter}:)", StringComparison.OrdinalIgnoreCase) ||
                                               clientStr.Contains($"({sysDrive})", StringComparison.OrdinalIgnoreCase) ||
                                               (!string.IsNullOrEmpty(volGuid) && clientStr.Contains(volGuid.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

                            if (matchesDrive)
                            {
                                return (true, sysDrive, $"System Protection is active and enabled for {sysDrive}.");
                            }
                        }
                    }
                }

                // 5. Query active Restore Points in Windows
                var existingPoints = EnumerateRestorePoints();
                if (existingPoints.Count > 0)
                {
                    return (true, sysDrive, $"System Protection is active on {sysDrive} (verified via {existingPoints.Count} active restore points).");
                }

                return (false, sysDrive, $"System Protection is currently disabled for the Windows system drive ({sysDrive}).");
            }
            catch (Exception ex)
            {
                return (false, sysDrive, $"Error verifying System Protection status: {ex.Message}");
            }
        }

        public (bool Success, string Message) EnableSystemProtection(string? drive = null)
        {
            string sysDrive = string.IsNullOrWhiteSpace(drive)
                ? (Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:")
                : drive.Trim().TrimEnd('\\');
            string sysDriveRoot = sysDrive + "\\";

            if (!IsAdministrator())
            {
                return (false, "Administrative privileges are required to enable Windows System Protection.");
            }

            EnableProcessPrivilege("SeRestorePrivilege");
            EnableProcessPrivilege("SeBackupPrivilege");
            EnableProcessPrivilege("SeSystemrestorePrivilege");

            // 1. Clear any DisableSR / DisableConfig policy keys
            try
            {
                using var polKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore", writable: true);
                if (polKey != null)
                {
                    polKey.DeleteValue("DisableSR", false);
                    polKey.DeleteValue("DisableConfig", false);
                }
            }
            catch { }

            try
            {
                using var srKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", writable: true);
                if (srKey != null)
                {
                    srKey.SetValue("DisableSR", 0, RegistryValueKind.DWord);
                    srKey.SetValue("RPSessionInterval", 1, RegistryValueKind.DWord);
                }
            }
            catch { }

            // 2. Primary WMI invocation: root\default:SystemRestore.Enable(sysDriveRoot, true)
            bool wmiInvoked = false;
            string lastError = "";
            try
            {
                var path = new ManagementPath(@"\\.\root\default:SystemRestore");
                using var srClass = new ManagementClass(path);
                using var inParams = srClass.GetMethodParameters("Enable");
                inParams["Drive"] = sysDriveRoot;
                inParams["WaitTillEnabled"] = true;

                using var outParams = srClass.InvokeMethod("Enable", inParams, null);
                if (outParams != null && outParams["ReturnValue"] != null)
                {
                    uint ret = Convert.ToUInt32(outParams["ReturnValue"]);
                    if (ret == 0)
                    {
                        wmiInvoked = true;
                    }
                    else
                    {
                        lastError = $"WMI ReturnValue = {ret}";
                    }
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }

            // 3. Fallback: Elevated PowerShell Enable-ComputerRestore
            if (!wmiInvoked)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Enable-ComputerRestore -Drive '{sysDriveRoot}'\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit(15000);
                        if (proc.ExitCode == 0)
                        {
                            wmiInvoked = true;
                        }
                        else
                        {
                            string err = proc.StandardError.ReadToEnd();
                            lastError = string.IsNullOrWhiteSpace(err) ? $"PowerShell exit code {proc.ExitCode}" : err;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
            }

            // 4. Ensure shadow storage is associated on system drive if needed
            try
            {
                var psiVss = new ProcessStartInfo
                {
                    FileName = "vssadmin.exe",
                    Arguments = $"add shadowstorage /for={sysDriveRoot} /on={sysDriveRoot} /maxsize=5%",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var procVss = Process.Start(psiVss);
                procVss?.WaitForExit(5000);
            }
            catch { }

            // 5. Wait for Windows configuration to initialize (Requirement #6)
            System.Threading.Thread.Sleep(1000);

            // 6. Query the REAL System Protection state again
            var (isNowEnabled, _, verifyReason) = CheckSystemProtectionStatus(sysDrive);
            if (isNowEnabled)
            {
                return (true, $"System Protection successfully enabled and verified on {sysDrive}.");
            }

            return (false, $"System Protection could not be verified after enabling on {sysDrive}. Current state: {verifyReason} (Detail: {lastError})");
        }

        public (bool Success, string Message) DisableSystemProtection(string? drive = null)
        {
            string sysDrive = string.IsNullOrWhiteSpace(drive)
                ? (Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:")
                : drive.Trim().TrimEnd('\\');
            string sysDriveRoot = sysDrive + "\\";

            if (!IsAdministrator())
            {
                return (false, "Administrative privileges are required.");
            }

            bool wmiInvoked = false;
            try
            {
                var path = new ManagementPath(@"\\.\root\default:SystemRestore");
                using var srClass = new ManagementClass(path);
                using var inParams = srClass.GetMethodParameters("Disable");
                inParams["Drive"] = sysDriveRoot;

                using var outParams = srClass.InvokeMethod("Disable", inParams, null);
                if (outParams != null && outParams["ReturnValue"] != null)
                {
                    uint ret = Convert.ToUInt32(outParams["ReturnValue"]);
                    if (ret == 0) wmiInvoked = true;
                }
            }
            catch { }

            if (!wmiInvoked)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Disable-ComputerRestore -Drive '{sysDriveRoot}'\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(15000);
                }
                catch { }
            }

            System.Threading.Thread.Sleep(1000);
            var (isNowEnabled, _, _) = CheckSystemProtectionStatus(sysDrive);
            return (!isNowEnabled, isNowEnabled ? "System Protection remains enabled." : $"System Protection disabled on {sysDrive}.");
        }

        public (bool IsAvailable, string StatusReason) CheckSystemRestoreAvailability(string? drive = null)
        {
            string sysDrive = string.IsNullOrWhiteSpace(drive)
                ? (Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:")
                : drive.Trim().TrimEnd('\\');

            var (isProtEnabled, _, protReason) = CheckSystemProtectionStatus(sysDrive);
            if (!isProtEnabled)
            {
                return (false, protReason);
            }

            return (true, $"System Protection is enabled and ready on {sysDrive}");
        }

        #endregion

        #region Enumeration & Verification

        public List<SystemRestorePointItem> EnumerateRestorePoints()
        {
            var results = new List<SystemRestorePointItem>();
            try
            {
                var scope = new ManagementScope(@"\\.\root\default");
                var query = new ObjectQuery("SELECT * FROM SystemRestore");
                using var searcher = new ManagementObjectSearcher(scope, query);

                foreach (ManagementObject mo in searcher.Get())
                {
                    long seq = Convert.ToInt64(mo["SequenceNumber"]);
                    string desc = mo["Description"]?.ToString() ?? string.Empty;
                    string rawTime = mo["CreationTime"]?.ToString() ?? string.Empty;
                    int type = Convert.ToInt32(mo["RestorePointType"]);

                    DateTime parsedTime = DateTime.UtcNow;
                    if (!string.IsNullOrEmpty(rawTime) && rawTime.Length >= 14)
                    {
                        try
                        {
                            string dtPart = rawTime[..14];
                            if (DateTime.TryParseExact(dtPart, "yyyyMMddHHmmss",
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal, out var dt))
                            {
                                parsedTime = dt.ToUniversalTime();
                            }
                        }
                        catch { }
                    }

                    results.Add(new SystemRestorePointItem
                    {
                        SequenceNumber = seq,
                        Description = desc,
                        CreationTimeString = rawTime,
                        RestorePointType = type,
                        CreationTimeUtc = parsedTime
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SR ENUM ERROR] {ex.Message}");
            }

            return results.OrderByDescending(r => r.SequenceNumber).ToList();
        }

        #endregion

        #region Authoritative Native Execution

        public RestorePointExecutionResult CreateRestorePoint(string? customName = null)
        {
            string systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
            string name = string.IsNullOrWhiteSpace(customName)
                ? $"Error Optimizer V3 - {DateTime.Now:yyyy-MM-dd HH:mm}"
                : customName.Trim();

            if (name.Length > 250) name = name[..250];

            var result = new RestorePointExecutionResult
            {
                Description = name,
                SystemDrive = systemDrive,
                Timestamp = DateTime.UtcNow
            };

            var diagLogs = new List<string>
            {
                $"[INIT] OS: {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})",
                $"[INIT] System Drive: {systemDrive}, Elevation: {IsAdministrator()}"
            };

            // 1. Elevation check
            if (!IsAdministrator())
            {
                result.Status = RestorePointStatus.NotElevated;
                result.ErrorMessage = "Administrative privileges are required to create a Windows System Restore Point.";
                result.DiagnosticsLog = string.Join(Environment.NewLine, diagLogs);
                return result;
            }

            // 2. Enable Required Security Privileges
            bool privRestore = EnableProcessPrivilege("SeRestorePrivilege");
            bool privBackup = EnableProcessPrivilege("SeBackupPrivilege");
            bool privSysRestore = EnableProcessPrivilege("SeSystemrestorePrivilege");
            diagLogs.Add($"[PRIVILEGES] SeRestore: {privRestore}, SeBackup: {privBackup}, SeSystemrestore: {privSysRestore}");

            // 3. Availability check
            var (isAvailable, availReason) = CheckSystemRestoreAvailability();
            if (!isAvailable)
            {
                result.Status = RestorePointStatus.SystemRestoreDisabled;
                result.ErrorMessage = availReason;
                diagLogs.Add($"[CHECK] System Restore Unavailable: {availReason}");
                result.DiagnosticsLog = string.Join(Environment.NewLine, diagLogs);
                return result;
            }

            // 4. Snapshot existing restore points before creation
            var existingBefore = EnumerateRestorePoints();
            long maxExistingSeq = existingBefore.Count > 0 ? existingBefore.Max(p => p.SequenceNumber) : 0;
            DateTime startTimeUtc = DateTime.UtcNow.AddSeconds(-10);
            diagLogs.Add($"[BASELINE] Existing points count: {existingBefore.Count}, Max Seq: {maxExistingSeq}");

            // 5. Handle SystemRestorePointCreationFrequency policy safely
            object? prevFreqValue = null;
            bool freqModified = false;
            try
            {
                using var srKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", writable: true);
                if (srKey != null)
                {
                    prevFreqValue = srKey.GetValue("SystemRestorePointCreationFrequency");
                    srKey.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
                    freqModified = true;
                    diagLogs.Add($"[POLICY] Temporarily adjusted SystemRestorePointCreationFrequency to 0 (previous: {prevFreqValue ?? "Default"})");
                }
            }
            catch (Exception ex)
            {
                diagLogs.Add($"[POLICY] Frequency registry access warning: {ex.Message}");
            }

            // 6. Initialize COM for System Restore API
            int coInitHr = CoInitializeEx(IntPtr.Zero, 0 /* COINIT_MULTITHREADED */);
            int coSecHr = CoInitializeSecurity(IntPtr.Zero, -1, IntPtr.Zero, IntPtr.Zero, 0, 3, IntPtr.Zero, 0, IntPtr.Zero);
            diagLogs.Add($"[COM] CoInitializeEx: 0x{coInitHr:X8}, CoInitializeSecurity: 0x{coSecHr:X8}");

            IntPtr hSrClient = IntPtr.Zero;
            bool beginSuccess = false;
            long capturedSequenceNumber = 0;
            int beginStatusVal = -1;
            int endStatusVal = -1;

            try
            {
                // Dynamic LoadLibrary of SrClient.dll
                hSrClient = LoadLibraryW("srclient.dll");
                if (hSrClient == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    result.Status = RestorePointStatus.CreationFailed;
                    result.ErrorMessage = $"Failed to load srclient.dll (Win32 Error: {err}).";
                    diagLogs.Add($"[LOAD] LoadLibrary srclient.dll failed: {err}");
                    result.DiagnosticsLog = string.Join(Environment.NewLine, diagLogs);
                    return result;
                }

                IntPtr pFunc = GetProcAddress(hSrClient, "SRSetRestorePointW");
                if (pFunc == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    result.Status = RestorePointStatus.CreationFailed;
                    result.ErrorMessage = $"SRSetRestorePointW export not found in srclient.dll (Win32 Error: {err}).";
                    diagLogs.Add($"[EXPORT] SRSetRestorePointW export missing: {err}");
                    result.DiagnosticsLog = string.Join(Environment.NewLine, diagLogs);
                    return result;
                }

                var srSetRestorePoint = Marshal.GetDelegateForFunctionPointer<SRSetRestorePointWDelegate>(pFunc);

                // ── STEP 1: BEGIN_SYSTEM_CHANGE ─────────────────────────────
                var beginInfo = new RESTOREPOINTINFOW
                {
                    dwEventType = BEGIN_SYSTEM_CHANGE,
                    dwRestorePtType = APPLICATION_INSTALL,
                    llSequenceNumber = 0,
                    szDescription = name
                };

                STATEMGRSTATUS beginStatus;
                bool beginCall = srSetRestorePoint(ref beginInfo, out beginStatus);
                int beginErr = Marshal.GetLastWin32Error();
                beginStatusVal = beginStatus.nStatus;
                result.BeginWin32Status = beginStatusVal;
                diagLogs.Add($"[BEGIN] Success: {beginCall}, Status: {beginStatusVal}, Seq: {beginStatus.llSequenceNumber}, Win32Err: {beginErr}");

                if (beginCall && beginStatusVal == ERROR_SUCCESS)
                {
                    beginSuccess = true;
                    capturedSequenceNumber = beginStatus.llSequenceNumber;

                    // ── STEP 2: END_SYSTEM_CHANGE ─────────────────────────────
                    var endInfo = new RESTOREPOINTINFOW
                    {
                        dwEventType = END_SYSTEM_CHANGE,
                        dwRestorePtType = APPLICATION_INSTALL,
                        llSequenceNumber = capturedSequenceNumber,
                        szDescription = name
                    };

                    STATEMGRSTATUS endStatus;
                    bool endCall = srSetRestorePoint(ref endInfo, out endStatus);
                    int endErr = Marshal.GetLastWin32Error();
                    endStatusVal = endStatus.nStatus;
                    result.EndWin32Status = endStatusVal;
                    diagLogs.Add($"[END] Success: {endCall}, Status: {endStatusVal}, Seq: {endStatus.llSequenceNumber}, Win32Err: {endErr}");

                    if (endStatus.llSequenceNumber > 0)
                    {
                        capturedSequenceNumber = endStatus.llSequenceNumber;
                    }
                }
                else
                {
                    if (beginStatusVal == ERROR_SERVICE_DISABLED)
                    {
                        result.Status = RestorePointStatus.SystemRestoreDisabled;
                        result.ErrorMessage = "Windows System Restore service is disabled or stopped (Error 1058).";
                        diagLogs.Add("[BEGIN] Service disabled (1058).");
                        result.DiagnosticsLog = string.Join(Environment.NewLine, diagLogs);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                diagLogs.Add($"[EXCEPTION] SRSetRestorePointW invocation failed: {ex.Message}");
            }
            finally
            {
                if (hSrClient != IntPtr.Zero)
                {
                    FreeLibrary(hSrClient);
                }

                if (freqModified)
                {
                    try
                    {
                        using var srKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", writable: true);
                        if (srKey != null)
                        {
                            if (prevFreqValue != null)
                            {
                                srKey.SetValue("SystemRestorePointCreationFrequency", prevFreqValue);
                            }
                            else
                            {
                                srKey.DeleteValue("SystemRestorePointCreationFrequency", false);
                            }
                            diagLogs.Add("[POLICY] Restored original SystemRestorePointCreationFrequency.");
                        }
                    }
                    catch { }
                }

                CoUninitialize();
            }

            // 7. Independent Windows Readback Verification
            // Poll for up to 4.5 seconds to allow Windows VSS to index and register the point
            SystemRestorePointItem? verifiedItem = null;
            for (int attempt = 0; attempt < 9; attempt++)
            {
                var currentPoints = EnumerateRestorePoints();

                // Match by exact captured sequence number, or new sequence > maxExistingSeq, or matching description
                verifiedItem = currentPoints.FirstOrDefault(p =>
                    (capturedSequenceNumber > 0 && p.SequenceNumber == capturedSequenceNumber) ||
                    (p.SequenceNumber > maxExistingSeq && string.Equals(p.Description, name, StringComparison.OrdinalIgnoreCase)) ||
                    (p.CreationTimeUtc >= startTimeUtc && string.Equals(p.Description, name, StringComparison.OrdinalIgnoreCase))
                );

                if (verifiedItem != null)
                {
                    diagLogs.Add($"[VERIFY] Matched in Windows System Restore: Seq #{verifiedItem.SequenceNumber}, Desc: '{verifiedItem.Description}' (Attempt {attempt + 1})");
                    break;
                }

                System.Threading.Thread.Sleep(500);
            }

            if (verifiedItem != null)
            {
                result.Status = RestorePointStatus.CreatedAndVerified;
                result.Description = verifiedItem.Description;
                result.SequenceNumber = verifiedItem.SequenceNumber;
                result.StatusMessage = $"VERIFIED & REGISTERED — Sequence #{verifiedItem.SequenceNumber} confirmed in Windows System Restore on {systemDrive}.";
                result.VerifiedPoint = verifiedItem;
                result.DiagnosticsLog = string.Join(Environment.NewLine, diagLogs);
                return result;
            }

            // If not found in WMI enumeration:
            if (beginSuccess && (capturedSequenceNumber == 0 || capturedSequenceNumber <= maxExistingSeq))
            {
                result.Status = RestorePointStatus.SkippedByPolicy;
                result.ErrorMessage = "Windows System Restore acknowledged the request but skipped creation because another restore point was created recently (Creation Frequency Policy).";
                diagLogs.Add("[VERIFY] Skipped by Windows creation frequency policy.");
                result.DiagnosticsLog = string.Join(Environment.NewLine, diagLogs);
                return result;
            }

            result.Status = RestorePointStatus.VerificationFailed;
            result.ErrorMessage = $"Restore point API call returned (Begin: {beginStatusVal}, End: {endStatusVal}), but point could not be verified in Windows System Restore enumeration.";
            diagLogs.Add("[VERIFY] Verification failed to find committed restore point.");
            result.DiagnosticsLog = string.Join(Environment.NewLine, diagLogs);
            return result;
        }

        #endregion
    }
}
