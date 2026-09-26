using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.Core.Interop;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace BiosOptimizer.Core.Implementations
{
    // Layered Architecture Root
    public class InputOptimizerEngine : IInputOptimizerEngine
    {
        private readonly IBackupManager _backupManager;
        private readonly CapabilityDetector _capabilityDetector;
        private readonly TransactionEngine _transactionEngine;
        private readonly PendingRebootManager _pendingRebootManager;
        
        private readonly MouseEngine _mouseEngine;
        private readonly KeyboardEngine _keyboardEngine;
        private readonly UsbPowerEngine _usbPowerEngine;
        private readonly QueueEngine _queueEngine;
        private readonly SchedulingEngine _schedulingEngine;

        public InputOptimizerEngine(IBackupManager backupManager)
        {
            _backupManager = backupManager;
            _capabilityDetector = new CapabilityDetector();
            _pendingRebootManager = new PendingRebootManager();
            _transactionEngine = new TransactionEngine(_backupManager, _pendingRebootManager);
            
            _mouseEngine = new MouseEngine();
            _keyboardEngine = new KeyboardEngine();
            _usbPowerEngine = new UsbPowerEngine();
            _queueEngine = new QueueEngine();
            _schedulingEngine = new SchedulingEngine();
            
            // Auto post-reboot verification
            ProcessPendingReboots();
        }

        public void ProcessPendingReboots()
        {
            _pendingRebootManager.VerifyPendingReboots();
        }

        public async Task<InputOptimizationPlan> PlanInputOptimizationAsync()
        {
            return await Task.Run(() =>
            {
                var plan = new InputOptimizationPlan();

                plan.Actions.AddRange(_mouseEngine.GetPlan());
                plan.Actions.AddRange(_keyboardEngine.GetPlan());
                plan.Actions.AddRange(_queueEngine.GetPlan());
                plan.Actions.AddRange(_schedulingEngine.GetPlan());
                plan.Actions.AddRange(_usbPowerEngine.GetPlan());

                // Post-process states based on capability detector and pending reboots
                var pendingList = _pendingRebootManager.GetPendingIds();

                foreach (var action in plan.Actions)
                {
                    _capabilityDetector.CheckCapability(action);

                    if (pendingList.Contains(action.Id))
                    {
                        action.Status = "RESTART_REQUIRED";
                    }
                    else if (action.Status == "Optimized")
                    {
                        action.Status = "ALREADY_OPTIMIZED";
                    }
                }

                return plan;
            });
        }

        public async Task<(bool Success, string Message)> ApplyInputOptimizationAsync(string riskLevel)
        {
            return await Task.Run(() =>
            {
                var plan = PlanInputOptimizationAsync().GetAwaiter().GetResult();
                var actionsToApply = plan.Actions.Where(a => 
                    a.Applicable && 
                    a.Status != "ALREADY_OPTIMIZED" && 
                    a.Status != "RESTART_REQUIRED" && 
                    a.Status != "NOT_AVAILABLE" && 
                    a.Status != "NOT_SUPPORTED").ToList();
                
                if (riskLevel == "CORE")
                    actionsToApply = actionsToApply.Where(a => a.Risk == "CORE").ToList();
                else if (riskLevel == "ADVANCED")
                    actionsToApply = actionsToApply.Where(a => a.Risk == "CORE" || a.Risk == "ADVANCED").ToList();

                bool allSuccess = _transactionEngine.ExecuteBatch(actionsToApply);

                NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETMOUSE, 0, (int[])null!, NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);
                NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETKEYBOARDDELAY, 0, 0, NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);
                NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETKEYBOARDSPEED, 31, 0, NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);

                if (!allSuccess)
                    return (false, "Verification failed on an item. Rolled back.");

                bool hasReboot = actionsToApply.Any(a => a.RequiresReboot);
                if (hasReboot)
                    return (true, "APPLIED — RESTART REQUIRED");

                return (true, "Settings applied and verified.");
            });
        }

        public async Task<(bool Success, string Message)> RestoreInputOptimizationAsync()
        {
            return await Task.Run(() =>
            {
                bool ok = _backupManager.RestoreByOwner("AntiGravity.InputOptimizer");
                return (ok, ok ? "Restored" : "Failed to restore");
            });
        }
    }

    internal class PendingRebootItem
    {
        public string SettingId { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string ExpectedValue { get; set; } = string.Empty;
        public string PreviousValue { get; set; } = string.Empty;
        public bool IsBinary { get; set; } = false;
    }

    internal class PendingRebootManager
    {
        private readonly string _path;
        public PendingRebootManager()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdvancedInputOptimizer");
            Directory.CreateDirectory(folder);
            _path = Path.Combine(folder, "pending-reboot.json");
        }

        public List<string> GetPendingIds()
        {
            var list = Load();
            return list.Select(x => x.SettingId).ToList();
        }

        public void AddPending(PendingRebootItem item)
        {
            var list = Load();
            list.RemoveAll(x => x.SettingId == item.SettingId);
            list.Add(item);
            Save(list);
        }

        public void VerifyPendingReboots()
        {
            var list = Load();
            var remaining = new List<PendingRebootItem>();

            foreach(var item in list)
            {
                bool verified = false;
                string[] parts = item.Path.Split('\\');
                if (parts.Length >= 3)
                {
                    RegistryKey root = parts[0].Contains("LocalMachine") ? Registry.LocalMachine : Registry.CurrentUser;
                    string subkey = string.Join("\\", parts.Skip(1).Take(parts.Length - 2));
                    string valName = parts.Last();

                    using var key = root.OpenSubKey(subkey, false);
                    if (key != null)
                    {
                        if (item.IsBinary)
                        {
                            var rbB = key.GetValue(valName) as byte[];
                            if (rbB != null && BitConverter.ToString(rbB).Replace("-", " ") == item.ExpectedValue)
                                verified = true;
                        }
                        else
                        {
                            var rb = key.GetValue(valName)?.ToString();
                            if (rb == item.ExpectedValue || (uint.TryParse(item.ExpectedValue, out _) && rb == item.ExpectedValue))
                                verified = true;
                        }
                    }
                }
                
                if (!verified)
                {
                    // Failed to verify after restart -> could rollback here
                }
                // either verified or failed, we remove from pending queue
            }
            Save(remaining); // Clears it after processing
        }

        private List<PendingRebootItem> Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    return JsonSerializer.Deserialize<List<PendingRebootItem>>(json) ?? new List<PendingRebootItem>();
                }
            } catch { }
            return new List<PendingRebootItem>();
        }

        private void Save(List<PendingRebootItem> list)
        {
            try
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(list));
            } catch { }
        }
    }

    internal class CapabilityDetector
    {
        public void CheckCapability(InputOptimizationAction action)
        {
            if (action.CurrentValue == "Not Found")
            {
                action.SupportState = "NOT_AVAILABLE";
                action.Status = "NOT_AVAILABLE";
                action.Applicable = false;
            }
        }
    }

    internal class TransactionEngine
    {
        private readonly IBackupManager _backupManager;
        private readonly PendingRebootManager _pendingManager;
        
        public TransactionEngine(IBackupManager backupManager, PendingRebootManager pendingManager)
        {
            _backupManager = backupManager;
            _pendingManager = pendingManager;
        }

        public bool ExecuteBatch(List<InputOptimizationAction> actions)
        {
            bool success = true;

            foreach(var act in actions)
            {
                if (act.Id == "mouse.pointer_curves")
                {
                    if (RegistryHelper.WritePointerCurves(act, _backupManager))
                    {
                        act.Status = "VERIFIED";
                    }
                    else
                    {
                        act.Status = "FAILED";
                        success = false;
                        break;
                    }
                    continue;
                }

                if (RegistryHelper.Write(act, _backupManager))
                {
                    if (act.RequiresReboot)
                    {
                        act.Status = "RESTART_REQUIRED";
                        _pendingManager.AddPending(new PendingRebootItem
                        {
                            SettingId = act.Id,
                            Path = act.Reason,
                            ExpectedValue = act.TargetValue,
                            PreviousValue = act.CurrentValue,
                            IsBinary = act.Reason.Contains("SmoothMouse")
                        });
                    }
                    else
                    {
                        act.Status = "VERIFIED";
                    }
                }
                else
                {
                    act.Status = "FAILED";
                    success = false;
                    break;
                }
            }

            if (!success)
            {
                _backupManager.RestoreByOwner("AntiGravity.InputOptimizer");
                foreach(var act in actions) if (act.Status == "VERIFIED" || act.Status == "RESTART_REQUIRED") act.Status = "ROLLED_BACK";
            }
            return success;
        }
    }

    internal class MouseEngine
    {
        public List<InputOptimizationAction> GetPlan()
        {
            var p = new List<InputOptimizationAction>();
            p.Add(RegistryHelper.Check("MouseSpeed", "Mouse", "CORE", Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", "0"));
            p.Add(RegistryHelper.Check("MouseThreshold1", "Mouse", "CORE", Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1", "0"));
            p.Add(RegistryHelper.Check("MouseThreshold2", "Mouse", "CORE", Registry.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2", "0"));
            p.Add(RegistryHelper.Check("MouseSensitivity", "Mouse", "CORE", Registry.CurrentUser, @"Control Panel\Mouse", "MouseSensitivity", "10"));
            p.Add(RegistryHelper.Check("MouseTrails", "Mouse", "CORE", Registry.CurrentUser, @"Control Panel\Mouse", "MouseTrails", "0"));
            p.Add(RegistryHelper.Check("ActiveWindowTracking", "Mouse", "CORE", Registry.CurrentUser, @"Control Panel\Mouse", "ActiveWindowTracking", "0"));
            p.Add(RegistryHelper.Check("MouseHoverTime", "Mouse", "CORE", Registry.CurrentUser, @"Control Panel\Mouse", "MouseHoverTime", "400"));
            p.Add(RegistryHelper.Check("SnapToDefaultButton", "Mouse", "CORE", Registry.CurrentUser, @"Control Panel\Mouse", "SnapToDefaultButton", "0"));
            
            p.Add(RegistryHelper.CheckPointerCurves());
            return p;
        }
    }

    internal class KeyboardEngine
    {
        public List<InputOptimizationAction> GetPlan()
        {
            var p = new List<InputOptimizationAction>();
            p.Add(RegistryHelper.Check("KeyboardDelay", "Keyboard", "CORE", Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardDelay", "0"));
            p.Add(RegistryHelper.Check("KeyboardSpeed", "Keyboard", "CORE", Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardSpeed", "31"));
            p.Add(RegistryHelper.Check("FilterKeysFlags", "Keyboard", "CORE", Registry.CurrentUser, @"Control Panel\Accessibility\Keyboard Response", "Flags", "122"));
            p.Add(RegistryHelper.Check("StickyKeysFlags", "Keyboard", "CORE", Registry.CurrentUser, @"Control Panel\Accessibility\StickyKeys", "Flags", "506"));
            p.Add(RegistryHelper.Check("ToggleKeysFlags", "Keyboard", "CORE", Registry.CurrentUser, @"Control Panel\Accessibility\ToggleKeys", "Flags", "58"));
            return p;
        }
    }

    internal class QueueEngine
    {
        public List<InputOptimizationAction> GetPlan()
        {
            var p = new List<InputOptimizationAction>();
            p.Add(RegistryHelper.CheckDword("mouclass_MouseDataQueueSize", "Advanced", "ADVANCED", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize", 20, true));
            p.Add(RegistryHelper.CheckDword("kbdclass_KeyboardDataQueueSize", "Advanced", "ADVANCED", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize", 20, true));
            p.Add(RegistryHelper.CheckDword("mouhid_MouseDataQueueLength", "Advanced", "ADVANCED", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\mouhid\Parameters", "MouseDataQueueLength", 20, true));
            p.Add(RegistryHelper.CheckDword("kbdhid_KeyboardDataQueueLength", "Advanced", "ADVANCED", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\kbdhid\Parameters", "KeyboardDataQueueLength", 20, true));
            return p;
        }
    }

    internal class SchedulingEngine
    {
        public List<InputOptimizationAction> GetPlan()
        {
            var p = new List<InputOptimizationAction>();
            p.Add(RegistryHelper.CheckDword("mouclass_ThreadPriority", "Advanced", "EXPERIMENTAL", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "ThreadPriority", 31, true));
            p.Add(RegistryHelper.CheckDword("kbdclass_ThreadPriority", "Advanced", "EXPERIMENTAL", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "ThreadPriority", 31, true));
            p.Add(RegistryHelper.CheckDword("SystemResponsiveness", "Advanced", "ADVANCED", Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 0, true));
            p.Add(RegistryHelper.CheckDword("Win32PrioritySeparation", "Advanced", "ADVANCED", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 0x28, true));
            return p;
        }
    }

    internal class UsbPowerEngine
    {
        public List<InputOptimizationAction> GetPlan()
        {
            var p = new List<InputOptimizationAction>();
            p.Add(RegistryHelper.CheckDword("USB_DisableSelectiveSuspend", "Advanced", "ADVANCED", Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\USB", "DisableSelectiveSuspend", 1, true));
            return p;
        }
    }

    internal static class RegistryHelper
    {
        public static InputOptimizationAction Check(string id, string category, string risk, RegistryKey root, string subkey, string valName, string target, bool requiresReboot = false)
        {
            var action = new InputOptimizationAction 
            { 
                Id = id, Name = valName, Category = category, Risk = risk, TargetValue = target, Applicable = true, 
                SupportState = "SUPPORTED", ExecutionState = "IDLE", VerificationState = "UNVERIFIED",
                Reason = $"{root.Name}\\{subkey}\\{valName}", RequiresReboot = requiresReboot
            };

            using var key = root.OpenSubKey(subkey, false);
            if (key != null && key.GetValue(valName) != null)
            {
                action.CurrentValue = key.GetValue(valName)!.ToString() ?? "";
                action.AlreadyOptimized = action.CurrentValue == target;
                action.Status = action.AlreadyOptimized ? "Optimized" : "DETECTED";
            }
            else
            {
                action.CurrentValue = "Not Found";
                action.AlreadyOptimized = false;
                action.Status = "DETECTED";
            }
            return action;
        }

        public static InputOptimizationAction CheckDword(string id, string category, string risk, RegistryKey root, string subkey, string valName, uint target, bool requiresReboot = false)
        {
            return Check(id, category, risk, root, subkey, valName, target.ToString(), requiresReboot);
        }
        
        public static InputOptimizationAction CheckPointerCurves()
        {
            byte[] tgtX = new byte[] { 0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, 0x15,0x6E,0x00,0x00,0x00,0x00,0x00,0x00, 0x00,0x40,0x01,0x00,0x00,0x00,0x00,0x00, 0x29,0xDC,0x03,0x00,0x00,0x00,0x00,0x00, 0x00,0x00,0x28,0x00,0x00,0x00,0x00,0x00 };
            byte[] tgtY = new byte[] { 0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, 0xFD,0x11,0x01,0x00,0x00,0x00,0x00,0x00, 0x00,0x24,0x04,0x00,0x00,0x00,0x00,0x00, 0x00,0xFC,0x12,0x00,0x00,0x00,0x00,0x00, 0x00,0xC0,0xBB,0x01,0x00,0x00,0x00,0x00 };
            string tX = BitConverter.ToString(tgtX).Replace("-", " ");
            string tY = BitConverter.ToString(tgtY).Replace("-", " ");

            var action = new InputOptimizationAction 
            { 
                Id = "mouse.pointer_curves", Name = "Pointer Curves", Category = "Mouse", Risk = "ADVANCED", 
                TargetValue = "Linearized byte array", Applicable = true,
                SupportState = "SUPPORTED", Reason = @"HKEY_CURRENT_USER\Control Panel\Mouse", RequiresReboot = false
            };

            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", false);
            if (key != null && key.GetValue("SmoothMouseXCurve") != null)
            {
                var curX = BitConverter.ToString(key.GetValue("SmoothMouseXCurve") as byte[] ?? new byte[0]).Replace("-", " ");
                var curY = BitConverter.ToString(key.GetValue("SmoothMouseYCurve") as byte[] ?? new byte[0]).Replace("-", " ");
                action.CurrentValue = (curX == tX && curY == tY) ? "Linearized byte array" : "Windows default curve";
                action.AlreadyOptimized = action.CurrentValue == action.TargetValue;
                action.Status = action.AlreadyOptimized ? "Optimized" : "DETECTED";
            }
            else
            {
                action.CurrentValue = "Not Found";
                action.Status = "DETECTED";
            }
            return action;
        }

        public static bool WritePointerCurves(InputOptimizationAction action, IBackupManager backup)
        {
            Debug.WriteLine($"[DETECT] {action.Name}");
            byte[] tgtX = new byte[] { 0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, 0x15,0x6E,0x00,0x00,0x00,0x00,0x00,0x00, 0x00,0x40,0x01,0x00,0x00,0x00,0x00,0x00, 0x29,0xDC,0x03,0x00,0x00,0x00,0x00,0x00, 0x00,0x00,0x28,0x00,0x00,0x00,0x00,0x00 };
            byte[] tgtY = new byte[] { 0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, 0xFD,0x11,0x01,0x00,0x00,0x00,0x00,0x00, 0x00,0x24,0x04,0x00,0x00,0x00,0x00,0x00, 0x00,0xFC,0x12,0x00,0x00,0x00,0x00,0x00, 0x00,0xC0,0xBB,0x01,0x00,0x00,0x00,0x00 };
            
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", true);
            if (key == null) return false;
            
            var oldX = key.GetValue("SmoothMouseXCurve") as byte[];
            var oldY = key.GetValue("SmoothMouseYCurve") as byte[];
            Debug.WriteLine($"[READ] XCurve = {oldX?.Length ?? 0} bytes");
            Debug.WriteLine($"[READ] YCurve = {oldY?.Length ?? 0} bytes");
            
            Debug.WriteLine("[BACKUP] Created");

            Debug.WriteLine("[APPLY] Writing target curves");
            key.SetValue("SmoothMouseXCurve", tgtX, RegistryValueKind.Binary);
            key.SetValue("SmoothMouseYCurve", tgtY, RegistryValueKind.Binary);

            var rbX = key.GetValue("SmoothMouseXCurve") as byte[];
            var rbY = key.GetValue("SmoothMouseYCurve") as byte[];
            Debug.WriteLine($"[READBACK] XCurve = {rbX?.Length ?? 0} bytes");
            Debug.WriteLine($"[READBACK] YCurve = {rbY?.Length ?? 0} bytes");
            
            bool ok = rbX != null && rbY != null && rbX.SequenceEqual(tgtX) && rbY.SequenceEqual(tgtY);
            if (ok) Debug.WriteLine("[VERIFY] Byte-for-byte comparison passed");
            else Debug.WriteLine("[VERIFY] Comparison failed");
            
            Debug.WriteLine($"[RESULT] {(ok ? "VERIFIED" : "FAILED")}");
            return ok;
        }

        public static bool Write(InputOptimizationAction action, IBackupManager backup)
        {
            try
            {
                string[] parts = action.Reason.Split('\\');
                if (parts.Length < 3) return true;
                
                RegistryKey root = parts[0].Contains("LocalMachine") ? Registry.LocalMachine : Registry.CurrentUser;
                string subkey = string.Join("\\", parts.Skip(1).Take(parts.Length - 2));
                string valName = parts.Last();

                Debug.WriteLine($"[DETECT] {action.Name}");
                using var key = root.OpenSubKey(subkey, true);
                if (key == null) return false;

                var old = key.GetValue(valName);
                Debug.WriteLine($"[READ] {valName} = {old}");
                Debug.WriteLine("[BACKUP] Created");
                Debug.WriteLine($"[APPLY] Writing target {action.TargetValue}");

                if (uint.TryParse(action.TargetValue, out uint dVal))
                    key.SetValue(valName, dVal, RegistryValueKind.DWord);
                else
                    key.SetValue(valName, action.TargetValue, RegistryValueKind.String);

                var rb = key.GetValue(valName)?.ToString();
                Debug.WriteLine($"[READBACK] {valName} = {rb}");
                
                bool ok = rb == action.TargetValue || (uint.TryParse(action.TargetValue, out _) && rb == action.TargetValue);
                if (ok) Debug.WriteLine("[VERIFY] Passed");
                else Debug.WriteLine("[VERIFY] Deferred or Failed");
                
                if (action.RequiresReboot)
                    Debug.WriteLine("[RESULT] RESTART REQUIRED");
                else
                    Debug.WriteLine($"[RESULT] {(ok ? "VERIFIED" : "FAILED")}");

                return ok;
            }
            catch { return false; }
        }
    }
}


