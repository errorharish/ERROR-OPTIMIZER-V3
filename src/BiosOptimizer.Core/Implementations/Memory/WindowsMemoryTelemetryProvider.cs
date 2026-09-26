using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Core.Implementations.Memory
{
    public static class MemoryTelemetryDiagnostics
    {
        private static readonly object _logLock = new();
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ErrorOptimizer", "Diagnostics");
        private static readonly string LogFile = Path.Combine(LogDir, "MemoryTelemetryDiagnostics.log");

        public static void LogSnapshot(MemoryTelemetryState state, string context = "TelemetryPoll")
        {
            try
            {
                lock (_logLock)
                {
                    if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
                    string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{context}] Total={state.TotalFormatted} | Used={state.UsedFormatted} ({state.MemoryPressurePercent:F1}%) | Avail={state.AvailableFormatted} | Cached(TaskManager)={state.CachedFormatted} | Standby(Kernel)={state.StandbyFormatted} | LowPriStandby={state.LowPriorityStandbyFormatted} | Modified={state.ModifiedFormatted} | Free={state.FreeFormatted} | Zero={state.ZeroedBytes / (1024*1024):F1}MB | PagedPool={state.PagedPoolBytes / (1024*1024):F1}MB | NonPagedPool={state.NonPagedPoolBytes / (1024*1024):F1}MB | NativeQuery={state.IsNativeKernelQueried}";
                    File.AppendAllText(LogFile, line + Environment.NewLine);
                    Debug.WriteLine(line);
                }
            }
            catch { }
        }
    }

    public class WindowsMemoryTelemetryProvider : IMemoryTelemetryProvider
    {
        private static readonly Lazy<WindowsMemoryTelemetryProvider> _lazy = new(() => new WindowsMemoryTelemetryProvider());
        public static WindowsMemoryTelemetryProvider Instance => _lazy.Value;

        #region Native Structs & P/Invoke

        private const int SystemMemoryListInformation = 80;
        private const int SystemFileCacheInformation = 21;

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
        public struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public UIntPtr CommitTotal;
            public UIntPtr CommitLimit;
            public UIntPtr CommitPeak;
            public UIntPtr PhysicalTotal;
            public UIntPtr PhysicalAvailable;
            public UIntPtr SystemCache; // Task Manager Cached pages
            public UIntPtr KernelTotal;
            public UIntPtr KernelPaged;
            public UIntPtr KernelNonpaged;
            public UIntPtr PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(
            int SystemInformationClass,
            IntPtr SystemInformation,
            int SystemInformationLength,
            out int ReturnLength);

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

        #endregion

        private readonly bool _hasPrivilege;

        public bool IsStandbyReclaimSupported => _hasPrivilege;

        public WindowsMemoryTelemetryProvider()
        {
            _hasPrivilege = EnablePrivilege("SeProfileSingleProcessPrivilege") || EnablePrivilege("SeIncreaseQuotaPrivilege");
        }

        private static bool EnablePrivilege(string privilegeName)
        {
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0020 | 0x0008, out tokenHandle))
                    return false;

                if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                    return false;

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privilege = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = 0x00000002
                    }
                };

                bool ok = AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                return ok && Marshal.GetLastWin32Error() == 0;
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

        public MemoryTelemetryState SampleCurrentMemoryState()
        {
            var state = new MemoryTelemetryState();
            long pageSize = 4096;

            // 1. GetPerformanceInfo for Task Manager-equivalent SystemCache, Committed, Paged/NonPaged pools
            try
            {
                var perf = new PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>() };
                if (GetPerformanceInfo(out perf, perf.cb))
                {
                    pageSize = (long)perf.PageSize.ToUInt64();
                    if (pageSize <= 0) pageSize = 4096;

                    state.TotalPhysicalBytes = (long)perf.PhysicalTotal.ToUInt64() * pageSize;
                    state.AvailableBytes = (long)perf.PhysicalAvailable.ToUInt64() * pageSize;
                    state.UsedBytes = state.TotalPhysicalBytes - state.AvailableBytes;
                    state.CachedBytes = (long)perf.SystemCache.ToUInt64() * pageSize; // Exact Task Manager Cached metric
                    state.CommittedBytes = (long)perf.CommitTotal.ToUInt64() * pageSize;
                    state.CommitLimitBytes = (long)perf.CommitLimit.ToUInt64() * pageSize;
                    state.PagedPoolBytes = (long)perf.KernelPaged.ToUInt64() * pageSize;
                    state.NonPagedPoolBytes = (long)perf.KernelNonpaged.ToUInt64() * pageSize;
                }
            }
            catch { }

            // 2. GlobalMemoryStatusEx fallback / supplement
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    if (state.TotalPhysicalBytes <= 0) state.TotalPhysicalBytes = (long)memStatus.ullTotalPhys;
                    if (state.AvailableBytes <= 0) state.AvailableBytes = (long)memStatus.ullAvailPhys;
                    if (state.UsedBytes <= 0) state.UsedBytes = state.TotalPhysicalBytes - state.AvailableBytes;
                }
            }
            catch { }

            if (state.TotalPhysicalBytes > 0)
            {
                state.MemoryPressurePercent = Math.Clamp((state.UsedBytes * 100.0) / state.TotalPhysicalBytes, 0.0, 100.0);
            }

            // 3. NtQuerySystemInformation (SystemMemoryListInformation = 80) for full kernel Standby lists
            try
            {
                int bufSize = 512;
                IntPtr pBuf = Marshal.AllocHGlobal(bufSize);
                try
                {
                    int ntStatus = NtQuerySystemInformation(SystemMemoryListInformation, pBuf, bufSize, out int retLen);
                    if (ntStatus == 0)
                    {
                        state.IsNativeKernelQueried = true;
                        state.ZeroedBytes = Marshal.ReadIntPtr(pBuf, 0).ToInt64() * pageSize;
                        state.FreeBytes = Marshal.ReadIntPtr(pBuf, IntPtr.Size).ToInt64() * pageSize;
                        state.ModifiedBytes = Marshal.ReadIntPtr(pBuf, IntPtr.Size * 2).ToInt64() * pageSize;

                        ulong totalStandby = 0;
                        ulong lowPriStandby = 0;
                        for (int i = 0; i < 8; i++)
                        {
                            long priBytes = Marshal.ReadIntPtr(pBuf, IntPtr.Size * (5 + i)).ToInt64() * pageSize;
                            totalStandby += (ulong)priBytes;
                            if (i < 3) lowPriStandby += (ulong)priBytes; // Priorities 0, 1, 2
                        }

                        state.StandbyBytes = (long)totalStandby;
                        state.LowPriorityStandbyBytes = (long)lowPriStandby;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pBuf);
                }
            }
            catch { }

            // If Task Manager Cached couldn't be read via GetPerformanceInfo, fallback to Standby + Modified
            if (state.CachedBytes <= 0 && state.StandbyBytes > 0)
            {
                state.CachedBytes = state.StandbyBytes + state.ModifiedBytes;
            }

            state.DiagnosticSummary = $"Used: {state.UsedFormatted} | Avail: {state.AvailableFormatted} | Cached: {state.CachedFormatted} | Standby: {state.StandbyFormatted} | LowPri: {state.LowPriorityStandbyFormatted}";
            return state;
        }
    }
}
