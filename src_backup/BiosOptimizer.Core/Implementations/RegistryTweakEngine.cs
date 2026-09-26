using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Interop;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Implementations
{
    public class RegistryTweakEngine : IRegistryTweakEngine
    {
        private readonly IBackupManager _backupManager;
        private const string OwnerTag = "AntiGravity.RegistryTweak.SvcHost";
        private const string RegPath = @"SYSTEM\CurrentControlSet\Control";
        private const string ValName = "SvcHostSplitThresholdInKB";

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
            public MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private class RamProfile
        {
            public ulong GbBase { get; set; }
            public uint Value { get; set; }
        }

        private readonly List<RamProfile> _profiles = new List<RamProfile>
        {
            new RamProfile { GbBase = 2, Value = 2097152 },
            new RamProfile { GbBase = 4, Value = 4194304 },
            new RamProfile { GbBase = 6, Value = 6291456 },
            new RamProfile { GbBase = 8, Value = 8388608 },
            new RamProfile { GbBase = 12, Value = 12582912 },
            new RamProfile { GbBase = 16, Value = 16777216 },
            new RamProfile { GbBase = 24, Value = 25165824 },
            new RamProfile { GbBase = 32, Value = 33554432 },
            new RamProfile { GbBase = 48, Value = 50331648 },
            new RamProfile { GbBase = 64, Value = 67108864 },
            new RamProfile { GbBase = 96, Value = 100663296 },
            new RamProfile { GbBase = 128, Value = 134217728 }
        };

        public RegistryTweakEngine(IBackupManager backupManager)
        {
            _backupManager = backupManager;
        }

        private ulong GetTotalPhysicalRam()
        {
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                return memStatus.ullTotalPhys;
            }
            return 0;
        }

        public async Task<RegistryTweakPlan> PlanRegistryTweakAsync()
        {
            return await Task.Run(() =>
            {
                var plan = new RegistryTweakPlan();
                
                ulong ramBytes = GetTotalPhysicalRam();
                plan.DetectedRamBytes = ramBytes;
                double ramGb = (double)ramBytes / (1024 * 1024 * 1024);
                plan.DetectedRamGb = ramGb.ToString("0.0") + " GB";

                if (ramGb < 1.5) // Less than 2GB (with some wiggle room)
                {
                    plan.Status = "NOT AVAILABLE";
                    plan.Reason = "Installed RAM is below the minimum 2 GB supported baseline.";
                    return plan;
                }
                
                if (ramGb > 129.0)
                {
                    plan.Status = "UNSUPPORTED RAM CONFIGURATION";
                    plan.Reason = "Installed RAM exceeds the supported registry profile.";
                    return plan;
                }

                // Round up rule
                var profile = _profiles.FirstOrDefault(p => p.GbBase >= Math.Ceiling(ramGb));
                if (profile == null) profile = _profiles.Last(); // Should never happen given the >129 check, but safe.

                plan.SelectedBaselineGb = profile.GbBase + " GB";
                plan.TargetValueDec = profile.Value.ToString();
                plan.TargetValueHex = "0x" + profile.Value.ToString("X8");

                // Read Registry
                using var key = Registry.LocalMachine.OpenSubKey(RegPath, false);
                if (key != null)
                {
                    var val = key.GetValue(ValName);
                    if (val != null)
                    {
                        var kind = key.GetValueKind(ValName);
                        plan.CurrentType = kind.ToString();

                        if (kind == RegistryValueKind.DWord)
                        {
                            uint dVal = (uint)(int)val;
                            plan.CurrentValueDec = dVal.ToString();
                            plan.CurrentValueHex = "0x" + dVal.ToString("X8");

                            if (dVal == profile.Value)
                            {
                                plan.Status = "ALREADY OPTIMIZED";
                            }
                            else
                            {
                                plan.Status = "READY";
                            }
                        }
                        else
                        {
                            plan.CurrentValueDec = val.ToString() ?? "";
                            plan.CurrentValueHex = "Invalid";
                            plan.Status = "INVALID TYPE";
                        }
                    }
                    else
                    {
                        plan.CurrentType = "Not Present";
                        plan.CurrentValueDec = "Not Present";
                        plan.CurrentValueHex = "Not Present";
                        plan.Status = "READY";
                    }
                }
                else
                {
                    plan.Status = "NOT AVAILABLE";
                    plan.Reason = "Registry path not found.";
                }

                return plan;
            });
        }

        public async Task<(bool Success, string Message, string Status)> ApplyRegistryTweakAsync()
        {
            return await Task.Run(() =>
            {
                var plan = PlanRegistryTweakAsync().GetAwaiter().GetResult();
                
                if (plan.Status == "ALREADY OPTIMIZED") return (true, "Already optimized.", "ALREADY OPTIMIZED");
                if (plan.Status == "INVALID TYPE") return (false, "Registry type is invalid. Cannot safely overwrite.", "FAILED");
                if (plan.Status.StartsWith("UNSUPPORTED") || plan.Status == "NOT AVAILABLE") return (false, plan.Reason, "FAILED");

                uint targetDword = uint.Parse(plan.TargetValueDec);
                
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(RegPath, true);
                    if (key == null) return (false, "Access denied or path not found.", "FAILED");

                    var old = key.GetValue(ValName);
                    string oldStr = old == null ? "Not Present" : old.ToString() ?? "";
                    
                    _backupManager.BackupRegistryValue(OwnerTag, "HKEY_LOCAL_MACHINE\\" + RegPath, ValName, oldStr, "REG_DWORD", targetDword.ToString(), "Registry Tweak Apply");

                    key.SetValue(ValName, targetDword, RegistryValueKind.DWord);

                    var rb = key.GetValue(ValName);
                    if (rb != null && key.GetValueKind(ValName) == RegistryValueKind.DWord && (uint)(int)rb == targetDword)
                    {
                        return (true, "Windows restart is recommended for the configuration change to take full effect.", "APPLIED — RESTART RECOMMENDED");
                    }
                    else
                    {
                        // Rollback
                        _backupManager.RestoreByOwner(OwnerTag);
                        return (false, "Verification readback failed. Original value was restored.", "FAILED");
                    }
                }
                catch (Exception ex)
                {
                    return (false, ex.Message, "FAILED");
                }
            });
        }

        public async Task<(bool Success, string Message, string Status)> RestoreRegistryTweakAsync()
        {
            return await Task.Run(() =>
            {
                bool ok = _backupManager.RestoreByOwner(OwnerTag);
                if (ok)
                {
                    using var key = Registry.LocalMachine.OpenSubKey(RegPath, false);
                    var val = key?.GetValue(ValName);
                    return (true, "Original state restored.", "RESTORED");
                }
                return (false, "Restore failed.", "RESTORE FAILED");
            });
        }
    }
}
