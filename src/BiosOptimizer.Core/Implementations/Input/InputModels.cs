using System;
using System.Collections.Generic;

namespace BiosOptimizer.Core.Implementations.Input
{
    public enum InputDeviceType
    {
        Mouse,
        Keyboard,
        Touchpad,
        Gamepad
    }

    public enum InputProfileType
    {
        CompetitiveGaming,
        CreativeEditing,
        General,
        Custom
    }

    public class InputDeviceInfo
    {
        public InputDeviceType DeviceType { get; set; }
        public string Name { get; set; } = "Unknown Device";
        public string Vendor { get; set; } = "UNKNOWN";
        public string Product { get; set; } = "UNKNOWN";
        public string Vid { get; set; } = "UNKNOWN";
        public string Pid { get; set; } = "UNKNOWN";
        public string DevicePath { get; set; } = string.Empty;
        public bool IsConnected { get; set; } = true;
        public bool IsWireless { get; set; }
        public string Capabilities { get; set; } = "Standard HID Input";

        public string DisplayHeader => $"{Vendor} {Product}".Trim();
        public string TypeBadge => DeviceType switch
        {
            InputDeviceType.Mouse => "MOUSE",
            InputDeviceType.Keyboard => "KEYBOARD",
            InputDeviceType.Touchpad => "TOUCHPAD",
            InputDeviceType.Gamepad => "GAMEPAD",
            _ => "HID"
        };
    }

    public class WindowsMouseSettings
    {
        public int PointerSpeed { get; set; } = 10; // 1 to 20 (Windows 10 = 6/11 default)
        public bool AccelerationEnabled { get; set; }
        public int MouseThreshold1 { get; set; } = 6;
        public int MouseThreshold2 { get; set; } = 10;
        public int MouseSpeedVal { get; set; } = 1; // 0 = no accel, 1 or 2 = accel
        public int MouseTrails { get; set; } // 0 = off, >0 = trail count
        public bool SnapToDefault { get; set; }
        public bool EnhancedPrecisionEnabled { get; set; }

        public WindowsMouseSettings Clone()
        {
            return (WindowsMouseSettings)this.MemberwiseClone();
        }
    }

    public class WindowsKeyboardSettings
    {
        public int KeyboardDelay { get; set; } = 1; // 0 = 250ms, 1 = 500ms, 2 = 750ms, 3 = 1000ms
        public int KeyboardRepeatSpeed { get; set; } = 31; // 0 to 31 (~2.5 to 30 reps/sec)
        public bool FilterKeysEnabled { get; set; }

        public WindowsKeyboardSettings Clone()
        {
            return (WindowsKeyboardSettings)this.MemberwiseClone();
        }
    }

    public class InputProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "General";
        public InputProfileType ProfileType { get; set; } = InputProfileType.General;
        public string Description { get; set; } = string.Empty;
        public List<string> TargetProcesses { get; set; } = new();
        public WindowsMouseSettings MouseSettings { get; set; } = new();
        public WindowsKeyboardSettings KeyboardSettings { get; set; } = new();
        public bool IsActive { get; set; }
    }

    public class InputOptimizationResult
    {
        public bool Success { get; set; }
        public string SettingName { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;
        public string TargetValue { get; set; } = string.Empty;
        public bool Verified { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
