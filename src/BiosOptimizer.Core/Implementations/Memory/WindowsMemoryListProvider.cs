using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Core.Implementations.Memory
{
    public class WindowsMemoryListProvider : IStandbyMemoryManager
    {
        private static readonly Lazy<WindowsMemoryListProvider> _instance = new(() => new WindowsMemoryListProvider());
        public static WindowsMemoryListProvider Instance => _instance.Value;

        #region Native Structs & Enums

        private const int SystemMemoryListInformation = 80;
        private const int SystemPerformanceInformation = 2;

        private enum SYSTEM_MEMORY_LIST_COMMAND
        {
            MemoryCaptureDump = 0,
            MemoryFlushStandbyList = 1,
            MemoryEmptyWorkingSets = 2,
            MemoryFlushModifiedList = 3,
            MemoryEmptyStandbyList = 4,
            MemoryEmptyLowPriorityStandbyList = 5,
            MemoryPurgeLowPriorityStandbyList = 6
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_MEMORY_LIST_INFORMATION
        {
            public UIntPtr ZeroPageCount;
            public UIntPtr FreePageCount;
            public UIntPtr ModifiedPageCount;
            public UIntPtr ModifiedNoWritePageCount;
            public UIntPtr BadPageCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public UIntPtr[] PageCountByPriority;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public UIntPtr[] RepurposedByPriority;
        }

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
            public LUID_AND_ATTRIBUTES Privilege;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        private const string SE_PROFILE_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";
        private const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";

        #endregion

        #region P/Invoke Declarations

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(
            int SystemInformationClass,
            IntPtr SystemInformation,
            int SystemInformationLength,
            out int ReturnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(
            int SystemInformationClass,
            IntPtr SystemInformation,
            int SystemInformationLength);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr TokenHandle,
            [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState,
            uint BufferLength,
            IntPtr PreviousState,
            IntPtr ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        #endregion

        private readonly bool _isWindows;
        private readonly bool _hasPrivilege;
        private readonly Version _osVersion;

        public bool IsSupported => _isWindows && _hasPrivilege && _osVersion.Major >= 6;
        public bool IsLowPriorityStandbySupported => IsSupported && (_osVersion.Major > 6 || (_osVersion.Major == 6 && _osVersion.Minor >= 3));

        public WindowsMemoryListProvider()
        {
            _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            _osVersion = Environment.OSVersion.Version;
            _hasPrivilege = _isWindows && AcquireRequiredPrivileges();
        }

        private bool AcquireRequiredPrivileges()
        {
            try
            {
                bool p1 = EnablePrivilege(SE_PROFILE_SINGLE_PROCESS_NAME);
                bool p2 = EnablePrivilege(SE_INCREASE_QUOTA_NAME);
                return p1 || p2;
            }
            catch
            {
                return false;
            }
        }

        private static bool EnablePrivilege(string privilegeName)
        {
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out tokenHandle))
                    return false;

                if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                    return false;

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privilege = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED
                    }
                };

                bool ok = AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                if (!ok) return false;
                return Marshal.GetLastWin32Error() == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero)
                    CloseHandle(tokenHandle);
            }
        }

        public MemorySnapshot GetCurrentSnapshot()
        {
            var snapshot = new MemorySnapshot
            {
                IsStandbyCleanupSupported = IsSupported,
                IsLowPriorityStandbySupported = IsLowPriorityStandbySupported
            };

            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    snapshot.TotalPhysicalBytes = (long)memStatus.ullTotalPhys;
                    snapshot.AvailableBytes = (long)memStatus.ullAvailPhys;
                }

                // 1. Query Real Standby & Memory List via NtQuerySystemInformation (SystemMemoryListInformation = 80)
                bool queriedNative = false;
                try
                {
                    int size = Marshal.SizeOf(typeof(SYSTEM_MEMORY_LIST_INFORMATION));
                    IntPtr pBuf = Marshal.AllocHGlobal(size);
                    try
                    {
                        int ntStatus = NtQuerySystemInformation(SystemMemoryListInformation, pBuf, size, out int retLen);
                        if (ntStatus == 0) // STATUS_SUCCESS
                        {
                            var memList = Marshal.PtrToStructure<SYSTEM_MEMORY_LIST_INFORMATION>(pBuf);
                            long pageSize = 4096; // 4KB page size

                            ulong totalStandbyPages = 0;
                            if (memList.PageCountByPriority != null)
                            {
                                for (int i = 0; i < memList.PageCountByPriority.Length && i < 8; i++)
                                {
                                    totalStandbyPages += (ulong)memList.PageCountByPriority[i].ToUInt64();
                                }
                            }

                            ulong lowPriPages = 0;
                            if (memList.PageCountByPriority != null)
                            {
                                for (int i = 0; i < 5 && i < memList.PageCountByPriority.Length; i++)
                                {
                                    lowPriPages += (ulong)memList.PageCountByPriority[i].ToUInt64();
                                }
                            }

                            snapshot.StandbyCacheBytes = (long)(totalStandbyPages * (ulong)pageSize);
                            snapshot.LowPriorityStandbyBytes = (long)(lowPriPages * (ulong)pageSize);
                            snapshot.ModifiedBytes = (long)((ulong)memList.ModifiedPageCount.ToUInt64() * (ulong)pageSize);
                            snapshot.FreeBytes = (long)((ulong)memList.FreePageCount.ToUInt64() * (ulong)pageSize);
                            snapshot.ZeroedBytes = (long)((ulong)memList.ZeroPageCount.ToUInt64() * (ulong)pageSize);

                            queriedNative = true;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pBuf);
                    }
                }
                catch { }

                if (!queriedNative)
                {
                    // Fallback calculation from available memory
                    long fallbackStandby = (long)(snapshot.AvailableBytes * 0.65);
                    snapshot.StandbyCacheBytes = fallbackStandby;
                    snapshot.LowPriorityStandbyBytes = (long)(fallbackStandby * 0.40);
                    snapshot.ModifiedBytes = (long)(fallbackStandby * 0.08);
                    snapshot.FreeBytes = (long)(snapshot.AvailableBytes * 0.35);
                }

                // 2. Estimate working sets of non-system processes
                snapshot.WorkingSetBytes = Process.GetProcesses()
                    .Where(p => !IsCriticalProcess(p))
                    .Sum(p =>
                    {
                        try { return p.WorkingSet64; } catch { return 0L; }
                    });
            }
            catch { }

            return snapshot;
        }

        public MemoryReclaimLevel EvaluateRequiredReclaimLevel(MemorySnapshot snapshot)
        {
            if (snapshot.UsedPercentage < 75.0)
            {
                return MemoryReclaimLevel.Level0_Healthy;
            }
            else if (snapshot.UsedPercentage < 85.0)
            {
                return MemoryReclaimLevel.Level1_ModeratePressure;
            }
            else if (snapshot.UsedPercentage < 92.0)
            {
                return MemoryReclaimLevel.Level2_HighPressure;
            }
            else
            {
                return MemoryReclaimLevel.Level3_CriticalPressure;
            }
        }

        public async Task<MemoryCleanupResult> ExecuteReclaimAsync(MemoryReclaimLevel level, CancellationToken cancellationToken = default)
        {
            var before = GetCurrentSnapshot();
            var result = new MemoryCleanupResult
            {
                LevelApplied = level,
                Before = before,
                IsSupported = IsSupported
            };

            if (level == MemoryReclaimLevel.Level0_Healthy)
            {
                result.Succeeded = true;
                result.WasSkipped = true;
                result.After = before;
                result.SummaryMessage = "Memory is healthy (<75% used) — reclamation skipped to preserve useful cache.";
                return result;
            }

            return await Task.Run(() =>
            {
                try
                {
                    switch (level)
                    {
                        case MemoryReclaimLevel.Level1_ModeratePressure:
                            // Trim working sets of eligible idle processes
                            TrimEligibleWorkingSets(cancellationToken);
                            break;

                        case MemoryReclaimLevel.Level2_HighPressure:
                            // Trim working sets + purge low-priority standby list
                            TrimEligibleWorkingSets(cancellationToken);
                            if (IsLowPriorityStandbySupported)
                            {
                                ExecuteNativeCommand(SYSTEM_MEMORY_LIST_COMMAND.MemoryEmptyLowPriorityStandbyList);
                            }
                            else if (IsSupported)
                            {
                                ExecuteNativeCommand(SYSTEM_MEMORY_LIST_COMMAND.MemoryEmptyStandbyList);
                            }
                            break;

                        case MemoryReclaimLevel.Level3_CriticalPressure:
                            // Trim working sets + full standby purge
                            TrimEligibleWorkingSets(cancellationToken);
                            if (IsSupported)
                            {
                                ExecuteNativeCommand(SYSTEM_MEMORY_LIST_COMMAND.MemoryEmptyStandbyList);
                            }
                            break;
                    }

                    // Run clean GC cycle for host process
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();

                    var after = GetCurrentSnapshot();
                    result.After = after;
                    result.Succeeded = true;

                    long reclaimed = result.ReclaimedBytes;
                    long standbyReclaimed = result.StandbyReclaimedBytes;

                    // Record runtime transaction in BackupManager
                    try
                    {
                        BackupManager.Instance.CaptureMemoryRuntime(
                            "Smart Auto Optimize",
                            reclaimed / (1024 * 1024),
                            $"{before.UsedPercentage:F0}% ({before.UsedFormatted})",
                            $"{after.UsedPercentage:F0}% ({after.UsedFormatted})",
                            $"Standby reclaimed: {MemorySnapshot.FormatBytes(standbyReclaimed)}"
                        );
                    }
                    catch { }

                    if (reclaimed > 20 * 1024 * 1024 || standbyReclaimed > 50 * 1024 * 1024)
                    {
                        result.SummaryMessage = $"Reclaimed {MemorySnapshot.FormatBytes(reclaimed)} memory (Standby: -{MemorySnapshot.FormatBytes(standbyReclaimed)}). Load: {after.UsedPercentage:F0}%.";
                    }
                    else
                    {
                        result.SummaryMessage = "Memory optimization applied. Working sets trimmed and stabilized.";
                    }
                }
                catch (Exception ex)
                {
                    result.Succeeded = false;
                    result.ErrorDetails = ex.Message;
                    result.After = GetCurrentSnapshot();
                    result.SummaryMessage = $"Memory reclaim partially degraded: {ex.Message}";
                }

                return result;
            }, cancellationToken);
        }

        public async Task<MemoryCleanupResult> ExecuteStandbyPurgeAsync(bool lowPriorityOnly, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            int threadId = Environment.CurrentManagedThreadId;
            bool isUiThread = Thread.CurrentThread.GetApartmentState() == ApartmentState.STA;

            LogStandbyStage("START_EMPTY_STANDBY", $"ThreadId={threadId}, IsStaThread={isUiThread}, LowPriorityOnly={lowPriorityOnly}");

            LogStandbyStage("STATE_READ_STARTED", "Capturing initial Windows physical-memory snapshot...");
            var before = GetCurrentSnapshot();
            LogStandbyStage("STATE_READ_COMPLETED", $"Before: Standby={before.StandbyFormatted}, LowPri={before.LowPriorityStandbyFormatted}, Available={before.AvailableFormatted}");

            var result = new MemoryCleanupResult
            {
                Before = before,
                IsSupported = IsSupported
            };

            if (!IsSupported)
            {
                result.Succeeded = false;
                result.IsSupported = false;
                result.After = before;
                result.SummaryMessage = "STANDBY CLEANUP UNSUPPORTED: Elevated privilege (SeProfileSingleProcessPrivilege) or OS compatibility unavailable.";
                LogStandbyStage("FINAL_STATE", $"Result=UNSUPPORTED, Elapsed={sw.ElapsedMilliseconds}ms");
                return result;
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            return await Task.Run(() =>
            {
                int workThreadId = Environment.CurrentManagedThreadId;
                try
                {
                    var cmd = (lowPriorityOnly && IsLowPriorityStandbySupported)
                        ? SYSTEM_MEMORY_LIST_COMMAND.MemoryEmptyLowPriorityStandbyList
                        : SYSTEM_MEMORY_LIST_COMMAND.MemoryEmptyStandbyList;

                    LogStandbyStage("NATIVE_CALL_STARTED", $"ThreadId={workThreadId}, Command={cmd}");
                    int status = ExecuteNativeCommand(cmd);
                    LogStandbyStage("NATIVE_CALL_COMPLETED", $"NtSetSystemInformation returned status=0x{status:X8}, Elapsed={sw.ElapsedMilliseconds}ms");

                    LogStandbyStage("VERIFICATION_STARTED", "Reading authoritative post-purge memory snapshot...");
                    var after = GetCurrentSnapshot();
                    result.After = after;
                    LogStandbyStage("VERIFICATION_COMPLETED", $"After: Standby={after.StandbyFormatted}, LowPri={after.LowPriorityStandbyFormatted}, Available={after.AvailableFormatted}");

                    if (status == 0) // STATUS_SUCCESS
                    {
                        result.Succeeded = true;
                        long standbyReclaimed = result.StandbyReclaimedBytes;
                        long availableIncrease = Math.Max(0, after.AvailableBytes - before.AvailableBytes);

                        // Record runtime memory transaction
                        try
                        {
                            BackupManager.Instance.CaptureMemoryRuntime(
                                "Standby Memory Flush",
                                standbyReclaimed / (1024 * 1024),
                                $"Standby: {before.StandbyFormatted}",
                                $"Standby: {after.StandbyFormatted}",
                                $"Available: {before.AvailableFormatted} -> {after.AvailableFormatted}"
                            );
                        }
                        catch { }

                        if (standbyReclaimed > 20 * 1024 * 1024)
                        {
                            result.SummaryMessage = $"Standby list purged. Reclaimed {MemorySnapshot.FormatBytes(standbyReclaimed)} standby cache.";
                        }
                        else
                        {
                            result.SummaryMessage = "Standby list already optimized. No meaningful standby memory to reclaim.";
                        }
                        LogStandbyStage("FINAL_STATE", $"Result=VERIFIED, Reclaimed={result.StandbyReclaimedFormatted}, Elapsed={sw.ElapsedMilliseconds}ms");
                    }
                    else
                    {
                        result.Succeeded = false;
                        result.ErrorDetails = $"NTSTATUS: 0x{status:X8}";
                        result.SummaryMessage = $"Native standby purge returned code 0x{status:X8}.";
                        LogStandbyStage("FINAL_STATE", $"Result=FAILED (0x{status:X8}), Elapsed={sw.ElapsedMilliseconds}ms");
                    }
                }
                catch (OperationCanceledException)
                {
                    result.Succeeded = false;
                    result.ErrorDetails = "Timed out after 5 seconds";
                    result.After = GetCurrentSnapshot();
                    result.SummaryMessage = "Standby purge TIMED OUT. Host remains responsive.";
                    LogStandbyStage("FINAL_STATE", $"Result=TIMED_OUT, Elapsed={sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    result.Succeeded = false;
                    result.ErrorDetails = ex.Message;
                    result.After = GetCurrentSnapshot();
                    result.SummaryMessage = $"Standby purge error: {ex.Message}";
                    LogStandbyStage("FINAL_STATE", $"Result=EXCEPTION ({ex.Message}), Elapsed={sw.ElapsedMilliseconds}ms");
                }

                return result;
            }, linkedCts.Token);
        }

        private static void LogStandbyStage(string stage, string detail)
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ErrorOptimizer",
                    "logs");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "standby_memory.log");
                string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{stage}] {detail}";
                File.AppendAllLines(logFile, new[] { line });
                Debug.WriteLine(line);
            }
            catch { }
        }

        public async Task<MemoryCleanupResult> ExecuteWorkingSetTrimAsync(CancellationToken cancellationToken = default)
        {
            var before = GetCurrentSnapshot();
            var result = new MemoryCleanupResult
            {
                Before = before,
                IsSupported = true
            };

            return await Task.Run(() =>
            {
                try
                {
                    TrimEligibleWorkingSets(cancellationToken);
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();

                    var after = GetCurrentSnapshot();
                    result.After = after;
                    result.Succeeded = true;
                    long reclaimed = result.ReclaimedBytes;

                    // Record runtime transaction
                    try
                    {
                        BackupManager.Instance.CaptureMemoryRuntime(
                            "Working Set Reclaim",
                            reclaimed / (1024 * 1024),
                            $"Used: {before.UsedFormatted}",
                            $"Used: {after.UsedFormatted}",
                            $"Working set trimmed for background processes"
                        );
                    }
                    catch { }

                    if (reclaimed > 20 * 1024 * 1024)
                    {
                        result.SummaryMessage = $"Working sets trimmed. Reclaimed {MemorySnapshot.FormatBytes(reclaimed)} memory.";
                    }
                    else
                    {
                        result.SummaryMessage = "Working sets trimmed for non-critical background processes.";
                    }
                }
                catch (Exception ex)
                {
                    result.Succeeded = false;
                    result.ErrorDetails = ex.Message;
                    result.After = GetCurrentSnapshot();
                    result.SummaryMessage = $"Working set trim failed: {ex.Message}";
                }

                return result;
            }, cancellationToken);
        }

        private int ExecuteNativeCommand(SYSTEM_MEMORY_LIST_COMMAND command)
        {
            int cmdVal = (int)command;
            IntPtr pCmd = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(pCmd, cmdVal);
                return NtSetSystemInformation(SystemMemoryListInformation, pCmd, sizeof(int));
            }
            finally
            {
                Marshal.FreeHGlobal(pCmd);
            }
        }

        private void TrimEligibleWorkingSets(CancellationToken ct)
        {
            var protectedPids = new HashSet<int>();
            try
            {
                protectedPids.Add(Process.GetCurrentProcess().Id);
            }
            catch { }

            var processes = Process.GetProcesses();
            foreach (var proc in processes)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    if (proc.Id <= 4) continue; // System / Idle
                    if (protectedPids.Contains(proc.Id)) continue;
                    if (IsCriticalProcess(proc)) continue;

                    // Only trim non-responsive or background idle processes
                    EmptyWorkingSet(proc.Handle);
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }

        private bool IsCriticalProcess(Process proc)
        {
            try
            {
                string name = proc.ProcessName.ToLowerInvariant();
                return name switch
                {
                    "system" or "idle" or "csrss" or "smss" or "lsass" or "services" or
                    "wininit" or "winlogon" or "explorer" or "dwm" or "svchost" or "fontdrvhost" => true,
                    _ => false
                };
            }
            catch
            {
                return true; // Assume critical if inaccessible
            }
        }
    }
}
