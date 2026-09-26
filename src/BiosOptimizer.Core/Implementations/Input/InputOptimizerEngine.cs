using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace BiosOptimizer.Core.Implementations.Input
{
    public class InputOptimizerEngine
    {
        private static readonly Lazy<InputOptimizerEngine> _instance = new(() => new InputOptimizerEngine());
        public static InputOptimizerEngine Instance => _instance.Value;

        #region Win32 Constants & Interop

        public const uint SPI_GETMOUSESPEED = 0x0070;
        public const uint SPI_SETMOUSESPEED = 0x0071;
        public const uint SPI_GETMOUSE = 0x0003;
        public const uint SPI_SETMOUSE = 0x0004;
        public const uint SPI_GETMOUSETRAILS = 0x005E;
        public const uint SPI_SETMOUSETRAILS = 0x005D;
        public const uint SPI_GETSNAPTODEFBUTTON = 0x005F;
        public const uint SPI_SETSNAPTODEFBUTTON = 0x0060;
        public const uint SPI_GETKEYBOARDDELAY = 0x0016;
        public const uint SPI_SETKEYBOARDDELAY = 0x0017;
        public const uint SPI_GETKEYBOARDSPEED = 0x000A;
        public const uint SPI_SETKEYBOARDSPEED = 0x000B;

        public const uint SPIF_UPDATEINIFILE = 0x01;
        public const uint SPIF_SENDCHANGE = 0x02;

        public const uint RIDI_DEVICENAME = 0x20000007;
        public const uint RIDI_DEVICEINFO = 0x2000000b;

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetRawInputDeviceList(IntPtr pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(uint dwUserIndex, IntPtr pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState91(uint dwUserIndex, IntPtr pState);

        #endregion

        private readonly object _lock = new();
        private WindowsMouseSettings? _originalMouseSettings;
        private WindowsKeyboardSettings? _originalKeyboardSettings;
        private List<InputDeviceInfo>? _cachedDevices;
        private DateTime _lastScanTime = DateTime.MinValue;

        private readonly List<InputProfile> _profiles = new();
        private InputProfile? _activeProfile;
        private string _lastForegroundProcess = string.Empty;

        public InputOptimizerEngine()
        {
            InitializeDefaultProfiles();
            BackupOriginalSettings();
        }

        public List<InputProfile> Profiles
        {
            get { lock (_lock) return new List<InputProfile>(_profiles); }
        }

        public InputProfile? ActiveProfile
        {
            get { lock (_lock) return _activeProfile; }
        }

        public void BackupOriginalSettings()
        {
            lock (_lock)
            {
                if (_originalMouseSettings == null)
                {
                    _originalMouseSettings = GetMouseSettings();
                }
                if (_originalKeyboardSettings == null)
                {
                    _originalKeyboardSettings = GetKeyboardSettings();
                }
            }
        }

        #region Device Detection (Real HID / RawInput / XInput)

        public List<InputDeviceInfo> ScanDevices(bool forceRefresh = false)
        {
            lock (_lock)
            {
                if (!forceRefresh && _cachedDevices != null && (DateTime.UtcNow - _lastScanTime).TotalSeconds < 10)
                {
                    return new List<InputDeviceInfo>(_cachedDevices);
                }

                var list = new List<InputDeviceInfo>();
                uint numDevices = 0;
                uint structSize = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));

                GetRawInputDeviceList(IntPtr.Zero, ref numDevices, structSize);

                if (numDevices > 0)
                {
                    IntPtr pList = Marshal.AllocHGlobal((int)(structSize * numDevices));
                    try
                    {
                        if (GetRawInputDeviceList(pList, ref numDevices, structSize) != uint.MaxValue)
                        {
                            for (int i = 0; i < numDevices; i++)
                            {
                                var dev = (RAWINPUTDEVICELIST)Marshal.PtrToStructure(
                                    new IntPtr(pList.ToInt64() + (i * structSize)), typeof(RAWINPUTDEVICELIST))!;

                                uint nameSize = 0;
                                GetRawInputDeviceInfo(dev.hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref nameSize);
                                string path = string.Empty;
                                if (nameSize > 0)
                                {
                                    IntPtr pName = Marshal.AllocHGlobal((int)(nameSize * 2));
                                    try
                                    {
                                        if (GetRawInputDeviceInfo(dev.hDevice, RIDI_DEVICENAME, pName, ref nameSize) != uint.MaxValue)
                                        {
                                            path = Marshal.PtrToStringAuto(pName) ?? string.Empty;
                                        }
                                    }
                                    finally
                                    {
                                        Marshal.FreeHGlobal(pName);
                                    }
                                }

                                var info = ParseDeviceInfo(dev.dwType, path);
                                if (info != null && !list.Any(d => d.DevicePath.Equals(info.DevicePath, StringComparison.OrdinalIgnoreCase)))
                                {
                                    list.Add(info);
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pList);
                    }
                }

                // Check Touchpad
                var touchpad = DetectTouchpad(list);
                if (touchpad != null && !list.Any(d => d.DeviceType == InputDeviceType.Touchpad))
                {
                    list.Add(touchpad);
                }

                // Check Gamepads via XInput
                var gamepads = DetectGamepads();
                list.AddRange(gamepads);

                _cachedDevices = list;
                _lastScanTime = DateTime.UtcNow;
                return list;
            }
        }

        private static InputDeviceInfo? ParseDeviceInfo(uint dwType, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var dev = new InputDeviceInfo
            {
                DevicePath = path,
                IsConnected = true
            };

            if (dwType == 0) dev.DeviceType = InputDeviceType.Mouse;
            else if (dwType == 1) dev.DeviceType = InputDeviceType.Keyboard;
            else
            {
                if (path.Contains("ELAN", StringComparison.OrdinalIgnoreCase) || path.Contains("SYNAPTICS", StringComparison.OrdinalIgnoreCase) || path.Contains("Touchpad", StringComparison.OrdinalIgnoreCase))
                {
                    dev.DeviceType = InputDeviceType.Touchpad;
                }
                else
                {
                    return null; // Filter generic non-input HID collections
                }
            }

            // Extract VID & PID
            int vidIdx = path.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
            if (vidIdx >= 0 && path.Length >= vidIdx + 8)
            {
                dev.Vid = path.Substring(vidIdx + 4, 4).ToUpperInvariant();
            }

            int pidIdx = path.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
            if (pidIdx >= 0 && path.Length >= pidIdx + 8)
            {
                dev.Pid = path.Substring(pidIdx + 4, 4).ToUpperInvariant();
            }

            // Map Known Vendors
            dev.Vendor = ResolveVendor(dev.Vid, path);
            dev.Product = ResolveProduct(dev.Vendor, dev.Pid, dev.DeviceType);
            dev.Name = $"{dev.Vendor} {dev.Product}".Trim();

            if (path.Contains("WIRELESS", StringComparison.OrdinalIgnoreCase) || path.Contains("BLUETOOTH", StringComparison.OrdinalIgnoreCase) || path.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase))
            {
                dev.IsWireless = true;
            }

            return dev;
        }

        private static string ResolveVendor(string vid, string path)
        {
            return vid switch
            {
                "046D" => "Logitech",
                "1532" => "Razer",
                "1B1C" => "Corsair",
                "1038" => "SteelSeries",
                "045E" => "Microsoft",
                "04D9" => "Holtek",
                "048D" => "ITE Tech",
                "093A" => "PixArt",
                "04F2" => "Chicony",
                "06CB" => "Synaptics",
                "04F3" => "ELAN",
                _ => path.Contains("ELAN", StringComparison.OrdinalIgnoreCase) ? "ELAN Microelectronics" :
                     path.Contains("SYN", StringComparison.OrdinalIgnoreCase) ? "Synaptics" :
                     path.Contains("MSFT", StringComparison.OrdinalIgnoreCase) ? "Microsoft" : "Standard HID"
            };
        }

        private static string ResolveProduct(string vendor, string pid, InputDeviceType type)
        {
            if (vendor == "Logitech")
            {
                return pid switch
                {
                    "C08B" => "G502 HERO Gaming Mouse",
                    "C077" => "Optical Mouse",
                    "C52B" => "Unifying Receiver",
                    "C539" => "LIGHTSPEED Wireless Receiver",
                    _ => type == InputDeviceType.Mouse ? "Gaming Mouse" : "Gaming Keyboard"
                };
            }
            if (vendor == "Razer")
            {
                return type == InputDeviceType.Mouse ? "DeathAdder / Viper Mouse" : "Huntsman / BlackWidow Keyboard";
            }
            if (vendor == "ELAN" || vendor.Contains("ELAN", StringComparison.OrdinalIgnoreCase))
            {
                return "Precision Touchpad";
            }
            if (vendor == "Synaptics")
            {
                return "Touchpad Driver";
            }
            return type == InputDeviceType.Mouse ? "Optical Pointer" : "Standard PS/2 / USB Keyboard";
        }

        private static InputDeviceInfo? DetectTouchpad(List<InputDeviceInfo> existing)
        {
            bool hasPrecisionTouchpad = false;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status");
                if (key != null && key.GetValue("Enabled") is int enabled && enabled == 1)
                {
                    hasPrecisionTouchpad = true;
                }
            }
            catch { }

            if (hasPrecisionTouchpad)
            {
                return new InputDeviceInfo
                {
                    DeviceType = InputDeviceType.Touchpad,
                    Name = "Windows Precision Touchpad",
                    Vendor = "Microsoft",
                    Product = "Precision Touchpad",
                    Capabilities = "Multi-touch Gestures, Hardware Palm Rejection",
                    IsConnected = true
                };
            }

            var touchpadEntry = existing.FirstOrDefault(d => d.DeviceType == InputDeviceType.Touchpad);
            return touchpadEntry;
        }

        private static List<InputDeviceInfo> DetectGamepads()
        {
            var list = new List<InputDeviceInfo>();
            for (uint i = 0; i < 4; i++)
            {
                IntPtr pState = Marshal.AllocHGlobal(16);
                try
                {
                    uint res = 1;
                    try { res = XInputGetState14(i, pState); }
                    catch { try { res = XInputGetState91(i, pState); } catch { } }

                    if (res == 0) // ERROR_SUCCESS
                    {
                        list.Add(new InputDeviceInfo
                        {
                            DeviceType = InputDeviceType.Gamepad,
                            Name = $"XInput Controller {i + 1}",
                            Vendor = "Microsoft / Xbox Compatible",
                            Product = $"Controller Slot {i + 1}",
                            Capabilities = "XInput 1.4 Analog Triggers, Dual Rumble",
                            IsConnected = true
                        });
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pState);
                }
            }
            return list;
        }

        #endregion

        #region Mouse Settings Operations

        public WindowsMouseSettings GetMouseSettings()
        {
            var settings = new WindowsMouseSettings();
            try
            {
                int speed = 10;
                if (SystemParametersInfo(SPI_GETMOUSESPEED, 0, ref speed, 0))
                {
                    settings.PointerSpeed = speed;
                }

                int[] mouseArr = new int[3];
                if (SystemParametersInfo(SPI_GETMOUSE, 0, mouseArr, 0))
                {
                    settings.MouseThreshold1 = mouseArr[0];
                    settings.MouseThreshold2 = mouseArr[1];
                    settings.MouseSpeedVal = mouseArr[2];
                    settings.AccelerationEnabled = mouseArr[2] > 0;
                }

                int trails = 0;
                if (SystemParametersInfo(SPI_GETMOUSETRAILS, 0, ref trails, 0))
                {
                    settings.MouseTrails = trails;
                }

                int snap = 0;
                if (SystemParametersInfo(SPI_GETSNAPTODEFBUTTON, 0, ref snap, 0))
                {
                    settings.SnapToDefault = snap != 0;
                }

                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse");
                if (key != null)
                {
                    string ep = key.GetValue("MouseSpeed")?.ToString() ?? "0";
                    settings.EnhancedPrecisionEnabled = ep == "1" || ep == "2";
                }
            }
            catch { }

            return settings;
        }

        public InputOptimizationResult SetPointerSpeed(int speed)
        {
            speed = Math.Clamp(speed, 1, 20);
            var before = GetMouseSettings();

            bool applied = SystemParametersInfo(SPI_SETMOUSESPEED, 0, (IntPtr)speed, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            var after = GetMouseSettings();

            bool verified = after.PointerSpeed == speed;
            return new InputOptimizationResult
            {
                Success = applied && verified,
                SettingName = "Pointer Speed",
                CurrentValue = $"{after.PointerSpeed} / 20 ({(after.PointerSpeed == 10 ? "6/11 Default 1:1" : "Modified")})",
                TargetValue = $"{speed} / 20",
                Verified = verified,
                Message = verified ? $"Pointer Speed verified at {speed}/20." : "Failed to verify Pointer Speed change."
            };
        }

        public InputOptimizationResult SetMouseAcceleration(bool enable)
        {
            var before = GetMouseSettings();
            int[] mouseArr = enable ? new int[] { 6, 10, 1 } : new int[] { 0, 0, 0 };

            bool applied = SystemParametersInfo(SPI_SETMOUSE, 0, mouseArr, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

            // Also update Registry keys for Enhanced Pointer Precision
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", writable: true);
                if (key != null)
                {
                    key.SetValue("MouseSpeed", enable ? "1" : "0", RegistryValueKind.String);
                    key.SetValue("MouseThreshold1", enable ? "6" : "0", RegistryValueKind.String);
                    key.SetValue("MouseThreshold2", enable ? "10" : "0", RegistryValueKind.String);
                }
            }
            catch { }

            var after = GetMouseSettings();
            bool verified = enable ? after.AccelerationEnabled : !after.AccelerationEnabled;

            return new InputOptimizationResult
            {
                Success = applied && verified,
                SettingName = "Mouse Acceleration",
                CurrentValue = after.AccelerationEnabled ? "Enabled" : "Disabled (Raw 1:1 Response)",
                TargetValue = enable ? "Enabled" : "Disabled",
                Verified = verified,
                Message = verified ? (enable ? "Pointer acceleration enabled." : "Pointer acceleration removed. 1:1 linear input active.") : "Failed to verify acceleration change."
            };
        }

        public InputOptimizationResult SetMouseTrails(int trails)
        {
            trails = Math.Max(0, trails);
            bool applied = SystemParametersInfo(SPI_SETMOUSETRAILS, (uint)trails, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            var after = GetMouseSettings();

            bool verified = trails == 0 ? (after.MouseTrails <= 1) : (after.MouseTrails == trails);
            return new InputOptimizationResult
            {
                Success = applied && verified,
                SettingName = "Mouse Trails",
                CurrentValue = after.MouseTrails <= 1 ? "Disabled" : $"{after.MouseTrails} Trails Active",
                TargetValue = trails <= 1 ? "Disabled" : $"{trails} Trails",
                Verified = verified,
                Message = verified ? (trails <= 1 ? "Mouse trails disabled." : "Mouse trails configured.") : "Failed to verify mouse trails change."
            };
        }

        public InputOptimizationResult SetSnapToDefault(bool enable)
        {
            bool applied = SystemParametersInfo(SPI_SETSNAPTODEFBUTTON, enable ? 1u : 0u, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            var after = GetMouseSettings();

            bool verified = after.SnapToDefault == enable;
            return new InputOptimizationResult
            {
                Success = applied && verified,
                SettingName = "Snap to Default Button",
                CurrentValue = after.SnapToDefault ? "Enabled" : "Disabled",
                TargetValue = enable ? "Enabled" : "Disabled",
                Verified = verified,
                Message = verified ? "Snap to default button configured." : "Failed to verify snap setting."
            };
        }

        #endregion

        #region Keyboard Settings Operations

        public WindowsKeyboardSettings GetKeyboardSettings()
        {
            var settings = new WindowsKeyboardSettings();
            try
            {
                int delay = 1;
                if (SystemParametersInfo(SPI_GETKEYBOARDDELAY, 0, ref delay, 0))
                {
                    settings.KeyboardDelay = delay;
                }

                int speed = 31;
                if (SystemParametersInfo(SPI_GETKEYBOARDSPEED, 0, ref speed, 0))
                {
                    settings.KeyboardRepeatSpeed = speed;
                }
            }
            catch { }

            return settings;
        }

        public InputOptimizationResult SetKeyboardDelay(int delay)
        {
            delay = Math.Clamp(delay, 0, 3);
            bool applied = SystemParametersInfo(SPI_SETKEYBOARDDELAY, (uint)delay, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            var after = GetKeyboardSettings();

            bool verified = after.KeyboardDelay == delay;
            string desc = delay switch { 0 => "250ms (Shortest)", 1 => "500ms (Default)", 2 => "750ms", _ => "1000ms (Long)" };

            return new InputOptimizationResult
            {
                Success = applied && verified,
                SettingName = "Keyboard Repeat Delay",
                CurrentValue = desc,
                TargetValue = desc,
                Verified = verified,
                Message = verified ? $"Keyboard repeat delay set to {desc}." : "Failed to verify keyboard delay."
            };
        }

        public InputOptimizationResult SetKeyboardRepeatSpeed(int speed)
        {
            speed = Math.Clamp(speed, 0, 31);
            bool applied = SystemParametersInfo(SPI_SETKEYBOARDSPEED, (uint)speed, IntPtr.Zero, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            var after = GetKeyboardSettings();

            bool verified = after.KeyboardRepeatSpeed == speed;
            return new InputOptimizationResult
            {
                Success = applied && verified,
                SettingName = "Keyboard Repeat Rate",
                CurrentValue = $"{after.KeyboardRepeatSpeed} / 31 ({(after.KeyboardRepeatSpeed == 31 ? "Max ~30 reps/s" : "Standard")})",
                TargetValue = $"{speed} / 31",
                Verified = verified,
                Message = verified ? $"Keyboard repeat speed verified at {speed}/31." : "Failed to verify keyboard speed."
            };
        }

        #endregion

        #region Profile Engine & Automatic Foreground Switching

        private void InitializeDefaultProfiles()
        {
            _profiles.Clear();

            // 1. Competitive Gaming Profile
            _profiles.Add(new InputProfile
            {
                Name = "Competitive Gaming",
                ProfileType = InputProfileType.CompetitiveGaming,
                Description = "Zero acceleration, 1:1 pointer scaling, 0 delay & max 31 repeat rate.",
                TargetProcesses = new List<string> { "cs2.exe", "valorant.exe", "r5apex.exe", "freefire.exe", "hd-player.exe", "msiappplayer.exe", "fortniteclient-win64-shipping.exe", "overwatch.exe" },
                MouseSettings = new WindowsMouseSettings { PointerSpeed = 10, AccelerationEnabled = false, MouseTrails = 0, SnapToDefault = false },
                KeyboardSettings = new WindowsKeyboardSettings { KeyboardDelay = 0, KeyboardRepeatSpeed = 31 }
            });

            // 2. Creative / Editing Profile
            _profiles.Add(new InputProfile
            {
                Name = "Creative / Editing",
                ProfileType = InputProfileType.CreativeEditing,
                Description = "Smooth 1:1 precision, stable repeat delay for timeline navigation.",
                TargetProcesses = new List<string> { "afterfx.exe", "premiere.exe", "blender.exe", "photoshop.exe", "illustrator.exe", "resolve.exe" },
                MouseSettings = new WindowsMouseSettings { PointerSpeed = 10, AccelerationEnabled = false, MouseTrails = 0, SnapToDefault = false },
                KeyboardSettings = new WindowsKeyboardSettings { KeyboardDelay = 1, KeyboardRepeatSpeed = 28 }
            });

            // 3. General Profile
            _profiles.Add(new InputProfile
            {
                Name = "General",
                ProfileType = InputProfileType.General,
                Description = "Default balanced desktop input settings.",
                TargetProcesses = new List<string>(),
                MouseSettings = new WindowsMouseSettings { PointerSpeed = 10, AccelerationEnabled = false, MouseTrails = 0, SnapToDefault = false },
                KeyboardSettings = new WindowsKeyboardSettings { KeyboardDelay = 1, KeyboardRepeatSpeed = 31 }
            });
        }

        public void ApplyProfile(InputProfile profile)
        {
            lock (_lock)
            {
                BackupOriginalSettings();

                SetPointerSpeed(profile.MouseSettings.PointerSpeed);
                SetMouseAcceleration(profile.MouseSettings.AccelerationEnabled);
                SetMouseTrails(profile.MouseSettings.MouseTrails);
                SetSnapToDefault(profile.MouseSettings.SnapToDefault);

                SetKeyboardDelay(profile.KeyboardSettings.KeyboardDelay);
                SetKeyboardRepeatSpeed(profile.KeyboardSettings.KeyboardRepeatSpeed);

                foreach (var p in _profiles) p.IsActive = (p == profile);
                _activeProfile = profile;
            }
        }

        public bool UpdateForegroundProfile()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return false;

            string processName = string.Empty;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName.ToLowerInvariant() + ".exe";
            }
            catch { return false; }

            if (processName.Equals(_lastForegroundProcess, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _lastForegroundProcess = processName;

            lock (_lock)
            {
                var match = _profiles.FirstOrDefault(p => p.TargetProcesses.Any(t => t.Equals(processName, StringComparison.OrdinalIgnoreCase)));
                if (match != null)
                {
                    if (_activeProfile != match)
                    {
                        ApplyProfile(match);
                        return true;
                    }
                }
                else
                {
                    // If leaving game, restore General / Original settings safely
                    if (_activeProfile != null && _activeProfile.ProfileType != InputProfileType.General)
                    {
                        RestoreOriginalSettings();
                        return true;
                    }
                }
            }

            return false;
        }

        public void RestoreOriginalSettings()
        {
            lock (_lock)
            {
                if (_originalMouseSettings != null)
                {
                    SetPointerSpeed(_originalMouseSettings.PointerSpeed);
                    SetMouseAcceleration(_originalMouseSettings.AccelerationEnabled);
                    SetMouseTrails(_originalMouseSettings.MouseTrails);
                    SetSnapToDefault(_originalMouseSettings.SnapToDefault);
                }

                if (_originalKeyboardSettings != null)
                {
                    SetKeyboardDelay(_originalKeyboardSettings.KeyboardDelay);
                    SetKeyboardRepeatSpeed(_originalKeyboardSettings.KeyboardRepeatSpeed);
                }

                var gen = _profiles.FirstOrDefault(p => p.ProfileType == InputProfileType.General);
                foreach (var p in _profiles) p.IsActive = (p == gen);
                _activeProfile = gen;
            }
        }

        #endregion
    }
}
