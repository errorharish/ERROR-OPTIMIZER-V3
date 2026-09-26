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
    public static class InputOptimizationState
    {
        public const string Discovered = "DISCOVERED";
        public const string Applicable = "APPLICABLE";
        public const string AlreadyOptimal = "ALREADY_OPTIMAL";
        public const string NotApplicable = "NOT_APPLICABLE";
        public const string Unsupported = "UNSUPPORTED";
        public const string Pending = "PENDING";
        public const string Applying = "APPLYING";
        public const string Applied = "APPLIED";
        public const string AppliedPendingReboot = "APPLIED_PENDING_REBOOT";
        public const string PostRebootVerifying = "POST_REBOOT_VERIFYING";
        public const string Verified = "VERIFIED";
        public const string Failed = "FAILED";
        public const string VerificationFailed = "VERIFICATION_FAILED";
        public const string RolledBack = "ROLLED_BACK";
        public const string NotAvailable = "NOT_AVAILABLE";
        public const string NotSupported = "NOT_SUPPORTED";
        public const string RestartRequired = "RESTART_REQUIRED";
    }

    public class InputTransactionItem
    {
        public string OptimizationId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string RegistryPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public string ValueType { get; set; } = "REG_DWORD";
        public string BeforeState { get; set; } = string.Empty;
        public string TargetState { get; set; } = string.Empty;
        public string State { get; set; } = InputOptimizationState.Pending;
        public bool RequiresReboot { get; set; }
        public InputVerificationMode VerificationMode { get; set; } = InputVerificationMode.ImmediateReadback;
        public string ApplyResult { get; set; } = string.Empty;
        public bool VerificationPending { get; set; }
        public string AfterState { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public long BootUptimeAtApplyMs { get; set; } = Environment.TickCount64;
        public string FailureReason { get; set; } = string.Empty;
        public bool IsBinary { get; set; }
    }

    public class InputOptimizerTransaction
    {
        public string TransactionId { get; set; } = Guid.NewGuid().ToString();
        public string Profile { get; set; } = "Default";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public long SystemBootTimeMs { get; set; } = Environment.TickCount64;
        public string Status { get; set; } = "RUNNING"; 
        public List<InputTransactionItem> Items { get; set; } = new List<InputTransactionItem>();
    }

    public class InputTransactionJournal
    {
        private static readonly object _lock = new object();
        private readonly string _journalPath;
        private const string REG_TX_KEY = @"Software\ErrorOptimizer\InputOptimizer\Transactions";

        public InputTransactionJournal()
        {
            string folder;
            try
            {
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ErrorOptimizer", "InputOptimizer");
                Directory.CreateDirectory(folder);
            }
            catch
            {
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdvancedInputOptimizer");
                Directory.CreateDirectory(folder);
            }
            _journalPath = Path.Combine(folder, "input_transaction_journal.json");
        }

        public InputOptimizerTransaction? LoadLatestTransaction()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_journalPath))
                    {
                        var json = File.ReadAllText(_journalPath);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var tx = JsonSerializer.Deserialize<InputOptimizerTransaction>(json);
                            if (tx != null) return tx;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[InputTransactionJournal] File load error: {ex.Message}");
                }

                // Fallback: Check Registry
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(REG_TX_KEY, false);
                    if (key != null)
                    {
                        string? json = key.GetValue("LatestTransaction") as string;
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            return JsonSerializer.Deserialize<InputOptimizerTransaction>(json);
                        }
                    }
                }
                catch { }

                return null;
            }
        }

        public void SaveTransaction(InputOptimizerTransaction tx)
        {
            lock (_lock)
            {
                try
                {
                    tx.UpdatedAt = DateTime.UtcNow;
                    var json = JsonSerializer.Serialize(tx, new JsonSerializerOptions { WriteIndented = true });
                    string tempPath = _journalPath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Copy(tempPath, _journalPath, true);
                    try { File.Delete(tempPath); } catch { }

                    // Mirror to Registry
                    try
                    {
                        using var key = Registry.CurrentUser.CreateSubKey(REG_TX_KEY, true);
                        key?.SetValue("LatestTransaction", json, RegistryValueKind.String);
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[InputTransactionJournal] Save error: {ex.Message}");
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_journalPath)) File.Delete(_journalPath);
                }
                catch { }

                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(REG_TX_KEY, true);
                    key?.DeleteValue("LatestTransaction", false);
                }
                catch { }
            }
        }
    }

    #region Optimization Detectors Interface & Implementations

    public interface IInputOptimizationDetector
    {
        string Id { get; }
        string Name { get; }
        string Category { get; }
        string Risk { get; }
        bool RequiresReboot { get; }
        InputVerificationMode VerificationMode { get; }
        string TechnicalLocation { get; }
        string Description { get; }

        (string ApplicabilityState, string Reason) DetectApplicability();
        string ReadCurrentState();
        string GetTargetState();
        (bool Success, string NewState, string ApplyResult, string FailureReason) Apply(IBackupManager backup);
        (bool Verified, string Message) Verify();
    }

    public abstract class RegistryDwordDetectorBase : IInputOptimizationDetector
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Category { get; }
        public abstract string Risk { get; }
        public abstract bool RequiresReboot { get; }
        public abstract InputVerificationMode VerificationMode { get; }
        public abstract string TechnicalLocation { get; }
        public abstract string Description { get; }
        public abstract RegistryKey RootKey { get; }
        public abstract string SubKeyPath { get; }
        public abstract string ValueName { get; }
        public abstract uint TargetDwordValue { get; }
        public virtual bool AllowKeyCreation => true;

        public virtual (string ApplicabilityState, string Reason) DetectApplicability()
        {
            try
            {
                using var key = RootKey.OpenSubKey(SubKeyPath, false);
                if (key == null)
                {
                    // Check if parent service/component exists
                    string[] parts = SubKeyPath.Split('\\');
                    if (parts.Length > 1)
                    {
                        string parentPath = string.Join("\\", parts.Take(parts.Length - 1));
                        using var parentKey = RootKey.OpenSubKey(parentPath, false);
                        if (parentKey == null)
                        {
                            return (InputOptimizationState.NotApplicable, $"Parent service '{parentPath}' is not installed on this PC.");
                        }
                    }
                    else
                    {
                        return (InputOptimizationState.NotApplicable, $"Registry path '{SubKeyPath}' does not exist on this machine.");
                    }
                }

                string current = ReadCurrentState();
                if (current == GetTargetState())
                {
                    return (InputOptimizationState.AlreadyOptimal, "Current value matches target optimization.");
                }

                return (InputOptimizationState.Applicable, "Setting is applicable and ready for optimization.");
            }
            catch (Exception ex)
            {
                return (InputOptimizationState.Unsupported, $"Access error: {ex.Message}");
            }
        }

        public virtual string ReadCurrentState()
        {
            try
            {
                using var key = RootKey.OpenSubKey(SubKeyPath, false);
                if (key == null) return "Not Found";
                var val = key.GetValue(ValueName);
                if (val == null) return "Default";

                if (val is int intVal) return ((uint)intVal).ToString();
                if (val is uint uintVal) return uintVal.ToString();
                if (uint.TryParse(val.ToString(), out uint parsed)) return parsed.ToString();
                return val.ToString() ?? "Unknown";
            }
            catch (UnauthorizedAccessException)
            {
                return "Access Denied";
            }
            catch
            {
                return "Error";
            }
        }

        public virtual string GetTargetState() => TargetDwordValue.ToString();

        public virtual (bool Success, string NewState, string ApplyResult, string FailureReason) Apply(IBackupManager backup)
        {
            try
            {
                using var key = RootKey.CreateSubKey(SubKeyPath, true);
                if (key == null)
                {
                    return (false, ReadCurrentState(), "FAILED", "Unable to open or create registry subkey.");
                }

                var existingVal = key.GetValue(ValueName);
                if (existingVal != null)
                {
                    backup.BackupRegistryValue("AntiGravity.InputOptimizer", SubKeyPath, ValueName, existingVal, "REG_DWORD", TargetDwordValue, "APPLIED");
                }

                key.SetValue(ValueName, unchecked((int)TargetDwordValue), RegistryValueKind.DWord);

                // Immediate readback check
                string readback = ReadCurrentState();
                bool written = (readback == GetTargetState());

                if (!written)
                {
                    return (false, readback, "WRITE_FAILED", "Immediate readback failed after registry write.");
                }

                if (RequiresReboot)
                {
                    return (true, readback, "SUCCESS", string.Empty);
                }

                return (true, readback, "SUCCESS", string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ReadCurrentState(), "FAILED", ex.Message);
            }
        }

        public virtual (bool Verified, string Message) Verify()
        {
            string cur = ReadCurrentState();
            bool match = (cur == GetTargetState());
            return (match, match ? "Target state verified." : $"Current value '{cur}' does not match target '{GetTargetState()}'.");
        }
    }

    public abstract class RegistryStringDetectorBase : IInputOptimizationDetector
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Category { get; }
        public abstract string Risk { get; }
        public abstract bool RequiresReboot { get; }
        public abstract InputVerificationMode VerificationMode { get; }
        public abstract string TechnicalLocation { get; }
        public abstract string Description { get; }
        public abstract RegistryKey RootKey { get; }
        public abstract string SubKeyPath { get; }
        public abstract string ValueName { get; }
        public abstract string TargetStringValue { get; }

        public virtual (string ApplicabilityState, string Reason) DetectApplicability()
        {
            try
            {
                using var key = RootKey.OpenSubKey(SubKeyPath, false);
                if (key == null)
                {
                    return (InputOptimizationState.NotApplicable, $"Registry path '{SubKeyPath}' does not exist.");
                }

                string current = ReadCurrentState();
                if (current == GetTargetState())
                {
                    return (InputOptimizationState.AlreadyOptimal, "Current value matches target optimization.");
                }

                return (InputOptimizationState.Applicable, "Setting is applicable.");
            }
            catch (Exception ex)
            {
                return (InputOptimizationState.Unsupported, $"Access error: {ex.Message}");
            }
        }

        public virtual string ReadCurrentState()
        {
            try
            {
                using var key = RootKey.OpenSubKey(SubKeyPath, false);
                if (key == null) return "Not Found";
                var val = key.GetValue(ValueName);
                return val != null ? (val.ToString() ?? "Not Found") : "Default";
            }
            catch (UnauthorizedAccessException)
            {
                return "Access Denied";
            }
            catch
            {
                return "Error";
            }
        }

        public virtual string GetTargetState() => TargetStringValue;

        public virtual (bool Success, string NewState, string ApplyResult, string FailureReason) Apply(IBackupManager backup)
        {
            try
            {
                using var key = RootKey.CreateSubKey(SubKeyPath, true);
                if (key == null)
                {
                    return (false, ReadCurrentState(), "FAILED", "Unable to open or create registry subkey.");
                }

                var existingVal = key.GetValue(ValueName);
                if (existingVal != null)
                {
                    backup.BackupRegistryValue("AntiGravity.InputOptimizer", SubKeyPath, ValueName, existingVal, "REG_SZ", TargetStringValue, "APPLIED");
                }

                key.SetValue(ValueName, TargetStringValue, RegistryValueKind.String);

                string readback = ReadCurrentState();
                bool written = string.Equals(readback, TargetStringValue, StringComparison.OrdinalIgnoreCase);

                if (!written)
                {
                    return (false, readback, "WRITE_FAILED", "Immediate readback failed after registry write.");
                }

                return (true, readback, "SUCCESS", string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ReadCurrentState(), "FAILED", ex.Message);
            }
        }

        public virtual (bool Verified, string Message) Verify()
        {
            string cur = ReadCurrentState();
            bool match = string.Equals(cur, GetTargetState(), StringComparison.OrdinalIgnoreCase);
            return (match, match ? "Target state verified." : $"Current value '{cur}' does not match target '{GetTargetState()}'.");
        }
    }

    // ── Mouse Core Detectors ──────────────────────────────────────────

    public class MouseSpeedDetector : RegistryStringDetectorBase
    {
        public override string Id => "mouse.speed";
        public override string Name => "Pointer Speed (1:1 Raw Scaling)";
        public override string Category => "Mouse";
        public override string Risk => "CORE";
        public override bool RequiresReboot => false;
        public override InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public override string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Mouse\MouseSpeed";
        public override string Description => "Disables Windows mouse acceleration multiplier to ensure 1:1 hardware sensor tracking.";
        public override RegistryKey RootKey => Registry.CurrentUser;
        public override string SubKeyPath => @"Control Panel\Mouse";
        public override string ValueName => "MouseSpeed";
        public override string TargetStringValue => "0";
    }

    public class MouseThreshold1Detector : RegistryStringDetectorBase
    {
        public override string Id => "mouse.threshold1";
        public override string Name => "Pointer Acceleration Threshold 1";
        public override string Category => "Mouse";
        public override string Risk => "CORE";
        public override bool RequiresReboot => false;
        public override InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public override string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Mouse\MouseThreshold1";
        public override string Description => "Zeros primary acceleration threshold curve to eliminate velocity non-linearities.";
        public override RegistryKey RootKey => Registry.CurrentUser;
        public override string SubKeyPath => @"Control Panel\Mouse";
        public override string ValueName => "MouseThreshold1";
        public override string TargetStringValue => "0";
    }

    public class MouseThreshold2Detector : RegistryStringDetectorBase
    {
        public override string Id => "mouse.threshold2";
        public override string Name => "Pointer Acceleration Threshold 2";
        public override string Category => "Mouse";
        public override string Risk => "CORE";
        public override bool RequiresReboot => false;
        public override InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public override string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Mouse\MouseThreshold2";
        public override string Description => "Zeros secondary acceleration threshold curve for uniform physical displacement.";
        public override RegistryKey RootKey => Registry.CurrentUser;
        public override string SubKeyPath => @"Control Panel\Mouse";
        public override string ValueName => "MouseThreshold2";
        public override string TargetStringValue => "0";
    }

    public class MouseSensitivityDetector : RegistryStringDetectorBase
    {
        public override string Id => "mouse.sensitivity";
        public override string Name => "Pointer Base Sensitivity (6/11 Default)";
        public override string Category => "Mouse";
        public override string Risk => "CORE";
        public override bool RequiresReboot => false;
        public override InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public override string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Mouse\MouseSensitivity";
        public override string Description => "Sets base sensitivity to 10 (Windows 6/11 notch), avoiding software interpolation skips.";
        public override RegistryKey RootKey => Registry.CurrentUser;
        public override string SubKeyPath => @"Control Panel\Mouse";
        public override string ValueName => "MouseSensitivity";
        public override string TargetStringValue => "10";
    }

    public class MouseTrailsDetector : RegistryStringDetectorBase
    {
        public override string Id => "mouse.trails";
        public override string Name => "Pointer Trails";
        public override string Category => "Mouse";
        public override string Risk => "CORE";
        public override bool RequiresReboot => false;
        public override InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public override string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Mouse\MouseTrails";
        public override string Description => "Disables pointer ghosting trails to eliminate visual compositing latency.";
        public override RegistryKey RootKey => Registry.CurrentUser;
        public override string SubKeyPath => @"Control Panel\Mouse";
        public override string ValueName => "MouseTrails";
        public override string TargetStringValue => "0";
    }

    public class ActiveWindowTrackingDetector : RegistryStringDetectorBase
    {
        public override string Id => "mouse.active_window_tracking";
        public override string Name => "Active Window Tracking Delay";
        public override string Category => "Mouse";
        public override string Risk => "CORE";
        public override bool RequiresReboot => false;
        public override InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public override string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Mouse\ActiveWindowTracking";
        public override string Description => "Disables asynchronous window activation delay under pointer movements.";
        public override RegistryKey RootKey => Registry.CurrentUser;
        public override string SubKeyPath => @"Control Panel\Mouse";
        public override string ValueName => "ActiveWindowTracking";
        public override string TargetStringValue => "0";
    }

    public class MouseHoverTimeDetector : RegistryStringDetectorBase
    {
        public override string Id => "mouse.hover_time";
        public override string Name => "Mouse Hover Response Time";
        public override string Category => "Mouse";
        public override string Risk => "CORE";
        public override bool RequiresReboot => false;
        public override InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public override string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Mouse\MouseHoverTime";
        public override string Description => "Sets standard hover response delay (400ms) to prevent accidental tooltip stalls.";
        public override RegistryKey RootKey => Registry.CurrentUser;
        public override string SubKeyPath => @"Control Panel\Mouse";
        public override string ValueName => "MouseHoverTime";
        public override string TargetStringValue => "400";
    }

    public class SnapToDefaultButtonDetector : RegistryStringDetectorBase
    {
        public override string Id => "mouse.snap_to_default";
        public override string Name => "Snap Pointer to Default Button";
        public override string Category => "Mouse";
        public override string Risk => "CORE";
        public override bool RequiresReboot => false;
        public override InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public override string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Mouse\SnapToDefaultButton";
        public override string Description => "Disables automatic pointer jumping on dialog popups.";
        public override RegistryKey RootKey => Registry.CurrentUser;
        public override string SubKeyPath => @"Control Panel\Mouse";
        public override string ValueName => "SnapToDefaultButton";
        public override string TargetStringValue => "0";
    }

    public class MouseCurvesDetector : IInputOptimizationDetector
    {
        public string Id => "mouse.pointer_curves";
        public string Name => "Linear MarkC Pointer Curves";
        public string Category => "Mouse";
        public string Risk => "ADVANCED";
        public bool RequiresReboot => false;
        public InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Mouse\SmoothMouseXCurve";
        public string Description => "Applies authentic linearized MarkC response curves for exact 1:1 hardware pixel ratio.";

        private static readonly byte[] TargetCurveX = new byte[] { 0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, 0x15,0x6E,0x00,0x00,0x00,0x00,0x00,0x00, 0x00,0x40,0x01,0x00,0x00,0x00,0x00,0x00, 0x29,0xDC,0x03,0x00,0x00,0x00,0x00,0x00, 0x00,0x00,0x28,0x00,0x00,0x00,0x00,0x00 };
        private static readonly byte[] TargetCurveY = new byte[] { 0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, 0xFD,0x11,0x01,0x00,0x00,0x00,0x00,0x00, 0x00,0x24,0x04,0x00,0x00,0x00,0x00,0x00, 0x00,0xFC,0x12,0x00,0x00,0x00,0x00,0x00, 0x00,0xC0,0xBB,0x01,0x00,0x00,0x00,0x00 };

        public (string ApplicabilityState, string Reason) DetectApplicability()
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", false);
            if (key == null) return (InputOptimizationState.NotApplicable, "Control Panel\\Mouse key not found.");

            string cur = ReadCurrentState();
            if (cur == GetTargetState())
            {
                return (InputOptimizationState.AlreadyOptimal, "Linear MarkC curves already applied.");
            }
            return (InputOptimizationState.Applicable, "Ready to apply linearized pointer curves.");
        }

        public string ReadCurrentState()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse", false);
                if (key == null) return "Not Found";
                var curX = key.GetValue("SmoothMouseXCurve") as byte[];
                var curY = key.GetValue("SmoothMouseYCurve") as byte[];
                if (curX != null && curY != null && curX.SequenceEqual(TargetCurveX) && curY.SequenceEqual(TargetCurveY))
                {
                    return "Linearized byte array";
                }
                return "Windows default curve";
            }
            catch { return "Error"; }
        }

        public string GetTargetState() => "Linearized byte array";

        public (bool Success, string NewState, string ApplyResult, string FailureReason) Apply(IBackupManager backup)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse", true);
                if (key == null) return (false, ReadCurrentState(), "FAILED", "Unable to open Control Panel\\Mouse");

                var oldX = key.GetValue("SmoothMouseXCurve") as byte[];
                var oldY = key.GetValue("SmoothMouseYCurve") as byte[];
                if (oldX != null) backup.BackupRegistryValue("AntiGravity.InputOptimizer", @"Control Panel\Mouse", "SmoothMouseXCurve", oldX, "REG_BINARY", TargetCurveX, "APPLIED");
                if (oldY != null) backup.BackupRegistryValue("AntiGravity.InputOptimizer", @"Control Panel\Mouse", "SmoothMouseYCurve", oldY, "REG_BINARY", TargetCurveY, "APPLIED");

                key.SetValue("SmoothMouseXCurve", TargetCurveX, RegistryValueKind.Binary);
                key.SetValue("SmoothMouseYCurve", TargetCurveY, RegistryValueKind.Binary);

                var rbX = key.GetValue("SmoothMouseXCurve") as byte[];
                var rbY = key.GetValue("SmoothMouseYCurve") as byte[];
                bool written = (rbX != null && rbY != null && rbX.SequenceEqual(TargetCurveX) && rbY.SequenceEqual(TargetCurveY));

                return (written, written ? "Linearized byte array" : "Write Failed", written ? "SUCCESS" : "FAILED", written ? string.Empty : "Curve byte mismatch on readback.");
            }
            catch (Exception ex)
            {
                return (false, ReadCurrentState(), "FAILED", ex.Message);
            }
        }

        public (bool Verified, string Message) Verify()
        {
            string cur = ReadCurrentState();
            bool match = (cur == GetTargetState());
            return (match, match ? "Curves verified." : "Curves not matching linear MarkC profile.");
        }
    }

    // ── Keyboard Core Detectors ───────────────────────────────────────

    public class KeyboardDelayDetector : RegistryStringDetectorBase
    {
        public override string Id => "keyboard.delay";
        public override string Name => "Keyboard Repeat Delay (Shortest Response)";
        public override string Category => "Keyboard";
        public override string Risk => "CORE";
        public override bool RequiresReboot => false;
        public override InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public override string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Keyboard\KeyboardDelay";
        public override string Description => "Sets repeat delay to 0 (250ms), accelerating held key input response.";
        public override RegistryKey RootKey => Registry.CurrentUser;
        public override string SubKeyPath => @"Control Panel\Keyboard";
        public override string ValueName => "KeyboardDelay";
        public override string TargetStringValue => "0";
    }

    public class KeyboardSpeedDetector : RegistryStringDetectorBase
    {
        public override string Id => "keyboard.speed";
        public override string Name => "Keyboard Repeat Rate (Max Frequency)";
        public override string Category => "Keyboard";
        public override string Risk => "CORE";
        public override bool RequiresReboot => false;
        public override InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public override string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Keyboard\KeyboardSpeed";
        public override string Description => "Sets repeat speed to 31 (~30 reps/sec) for maximum command throughput.";
        public override RegistryKey RootKey => Registry.CurrentUser;
        public override string SubKeyPath => @"Control Panel\Keyboard";
        public override string ValueName => "KeyboardSpeed";
        public override string TargetStringValue => "31";
    }

    public class FilterKeysFlagsDetector : RegistryStringDetectorBase
    {
        public override string Id => "keyboard.filter_keys";
        public override string Name => "FilterKeys Shortcut Bypass";
        public override string Category => "Keyboard";
        public override string Risk => "CORE";
        public override bool RequiresReboot => false;
        public override InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public override string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Accessibility\Keyboard Response\Flags";
        public override string Description => "Configures FilterKeys flags (122) to prevent accidental keystroke suppression.";
        public override RegistryKey RootKey => Registry.CurrentUser;
        public override string SubKeyPath => @"Control Panel\Accessibility\Keyboard Response";
        public override string ValueName => "Flags";
        public override string TargetStringValue => "122";
    }

    public class StickyKeysFlagsDetector : RegistryStringDetectorBase
    {
        public override string Id => "keyboard.sticky_keys";
        public override string Name => "StickyKeys Shortcut Bypass";
        public override string Category => "Keyboard";
        public override string Risk => "CORE";
        public override bool RequiresReboot => false;
        public override InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public override string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Accessibility\StickyKeys\Flags";
        public override string Description => "Configures StickyKeys flags (506) to disable shift-key interruption modals.";
        public override RegistryKey RootKey => Registry.CurrentUser;
        public override string SubKeyPath => @"Control Panel\Accessibility\StickyKeys";
        public override string ValueName => "Flags";
        public override string TargetStringValue => "506";
    }

    public class ToggleKeysFlagsDetector : RegistryStringDetectorBase
    {
        public override string Id => "keyboard.toggle_keys";
        public override string Name => "ToggleKeys Shortcut Bypass";
        public override string Category => "Keyboard";
        public override string Risk => "CORE";
        public override bool RequiresReboot => false;
        public override InputVerificationMode VerificationMode => InputVerificationMode.ImmediateReadback;
        public override string TechnicalLocation => @"HKEY_CURRENT_USER\Control Panel\Accessibility\ToggleKeys\Flags";
        public override string Description => "Configures ToggleKeys flags (58) to eliminate audio buffer interruptions.";
        public override RegistryKey RootKey => Registry.CurrentUser;
        public override string SubKeyPath => @"Control Panel\Accessibility\ToggleKeys";
        public override string ValueName => "Flags";
        public override string TargetStringValue => "58";
    }

    // ── Advanced / Driver Queue Detectors (Requires Reboot) ───────────

    public class MouclassQueueSizeDetector : RegistryDwordDetectorBase
    {
        public override string Id => "queue.mouclass_size";
        public override string Name => "Mouse Class Data Queue Size";
        public override string Category => "Mouse Queue";
        public override string Risk => "ADVANCED";
        public override bool RequiresReboot => true;
        public override InputVerificationMode VerificationMode => InputVerificationMode.PostRebootReadback;
        public override string TechnicalLocation => @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\mouclass\Parameters\MouseDataQueueSize";
        public override string Description => "Optimizes mouse driver buffer queue depth to 20 packets, reducing input packet buffering latency.";
        public override RegistryKey RootKey => Registry.LocalMachine;
        public override string SubKeyPath => @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters";
        public override string ValueName => "MouseDataQueueSize";
        public override uint TargetDwordValue => 20;

        public override (string ApplicabilityState, string Reason) DetectApplicability()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\mouclass", false);
            if (key == null) return (InputOptimizationState.NotApplicable, "Mouse class driver service (mouclass) not present.");
            return base.DetectApplicability();
        }
    }

    public class KbdclassQueueSizeDetector : RegistryDwordDetectorBase
    {
        public override string Id => "queue.kbdclass_size";
        public override string Name => "Keyboard Class Data Queue Size";
        public override string Category => "Keyboard Queue";
        public override string Risk => "ADVANCED";
        public override bool RequiresReboot => true;
        public override InputVerificationMode VerificationMode => InputVerificationMode.PostRebootReadback;
        public override string TechnicalLocation => @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters\KeyboardDataQueueSize";
        public override string Description => "Optimizes keyboard driver buffer queue depth to 20 packets, minimizing key event buffering delay.";
        public override RegistryKey RootKey => Registry.LocalMachine;
        public override string SubKeyPath => @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters";
        public override string ValueName => "KeyboardDataQueueSize";
        public override uint TargetDwordValue => 20;

        public override (string ApplicabilityState, string Reason) DetectApplicability()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\kbdclass", false);
            if (key == null) return (InputOptimizationState.NotApplicable, "Keyboard class driver service (kbdclass) not present.");
            return base.DetectApplicability();
        }
    }

    public class MouhidQueueLengthDetector : RegistryDwordDetectorBase
    {
        public override string Id => "queue.mouhid_length";
        public override string Name => "HID Mouse Data Queue Length";
        public override string Category => "HID Mouse Queue";
        public override string Risk => "ADVANCED";
        public override bool RequiresReboot => true;
        public override InputVerificationMode VerificationMode => InputVerificationMode.PostRebootReadback;
        public override string TechnicalLocation => @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\mouhid\Parameters\MouseDataQueueLength";
        public override string Description => "Sets USB HID mouse driver queue buffer to 20 packets for direct polling responsiveness.";
        public override RegistryKey RootKey => Registry.LocalMachine;
        public override string SubKeyPath => @"SYSTEM\CurrentControlSet\Services\mouhid\Parameters";
        public override string ValueName => "MouseDataQueueLength";
        public override uint TargetDwordValue => 20;

        public override (string ApplicabilityState, string Reason) DetectApplicability()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\mouhid", false);
            if (key == null) return (InputOptimizationState.NotApplicable, "HID mouse driver service (mouhid) not present.");
            return base.DetectApplicability();
        }
    }

    public class KbdhidQueueLengthDetector : RegistryDwordDetectorBase
    {
        public override string Id => "queue.kbdhid_length";
        public override string Name => "HID Keyboard Data Queue Length";
        public override string Category => "HID Keyboard Queue";
        public override string Risk => "ADVANCED";
        public override bool RequiresReboot => true;
        public override InputVerificationMode VerificationMode => InputVerificationMode.PostRebootReadback;
        public override string TechnicalLocation => @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\kbdhid\Parameters\KeyboardDataQueueLength";
        public override string Description => "Sets USB HID keyboard driver queue buffer to 20 packets for direct keystroke transmission.";
        public override RegistryKey RootKey => Registry.LocalMachine;
        public override string SubKeyPath => @"SYSTEM\CurrentControlSet\Services\kbdhid\Parameters";
        public override string ValueName => "KeyboardDataQueueLength";
        public override uint TargetDwordValue => 20;

        public override (string ApplicabilityState, string Reason) DetectApplicability()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\kbdhid", false);
            if (key == null) return (InputOptimizationState.NotApplicable, "HID keyboard driver service (kbdhid) not present.");
            return base.DetectApplicability();
        }
    }

    public class SystemResponsivenessDetector : RegistryDwordDetectorBase
    {
        public override string Id => "scheduling.system_responsiveness";
        public override string Name => "Multimedia System Responsiveness";
        public override string Category => "Multimedia Profile";
        public override string Risk => "ADVANCED";
        public override bool RequiresReboot => true;
        public override InputVerificationMode VerificationMode => InputVerificationMode.PostRebootReadback;
        public override string TechnicalLocation => @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\SystemResponsiveness";
        public override string Description => "Allocates 100% of CPU time to foreground interactive processes and input threads.";
        public override RegistryKey RootKey => Registry.LocalMachine;
        public override string SubKeyPath => @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        public override string ValueName => "SystemResponsiveness";
        public override uint TargetDwordValue => 0;
    }

    public class Win32PrioritySeparationDetector : RegistryDwordDetectorBase
    {
        public override string Id => "scheduling.priority_separation";
        public override string Name => "Win32 Foreground Priority Separation";
        public override string Category => "Priority Control";
        public override string Risk => "ADVANCED";
        public override bool RequiresReboot => true;
        public override InputVerificationMode VerificationMode => InputVerificationMode.PostRebootReadback;
        public override string TechnicalLocation => @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\PriorityControl\Win32PrioritySeparation";
        public override string Description => "Sets quantum scheduling ratio to 0x28 (40), providing short, variable quanta optimized for gaming input.";
        public override RegistryKey RootKey => Registry.LocalMachine;
        public override string SubKeyPath => @"SYSTEM\CurrentControlSet\Control\PriorityControl";
        public override string ValueName => "Win32PrioritySeparation";
        public override uint TargetDwordValue => 0x28;
    }

    public class MouclassThreadPriorityDetector : RegistryDwordDetectorBase
    {
        public override string Id => "scheduling.mouclass_priority";
        public override string Name => "Mouse Class Thread Priority";
        public override string Category => "Scheduling";
        public override string Risk => "EXPERIMENTAL";
        public override bool RequiresReboot => true;
        public override InputVerificationMode VerificationMode => InputVerificationMode.PostRebootReadback;
        public override string TechnicalLocation => @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\mouclass\Parameters\ThreadPriority";
        public override string Description => "Assigns real-time priority (31) to mouse class DPC processing threads.";
        public override RegistryKey RootKey => Registry.LocalMachine;
        public override string SubKeyPath => @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters";
        public override string ValueName => "ThreadPriority";
        public override uint TargetDwordValue => 31;

        public override (string ApplicabilityState, string Reason) DetectApplicability()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\mouclass", false);
            if (key == null) return (InputOptimizationState.NotApplicable, "mouclass driver not installed.");
            return base.DetectApplicability();
        }
    }

    public class KbdclassThreadPriorityDetector : RegistryDwordDetectorBase
    {
        public override string Id => "scheduling.kbdclass_priority";
        public override string Name => "Keyboard Class Thread Priority";
        public override string Category => "Scheduling";
        public override string Risk => "EXPERIMENTAL";
        public override bool RequiresReboot => true;
        public override InputVerificationMode VerificationMode => InputVerificationMode.PostRebootReadback;
        public override string TechnicalLocation => @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters\ThreadPriority";
        public override string Description => "Assigns real-time priority (31) to keyboard class DPC processing threads.";
        public override RegistryKey RootKey => Registry.LocalMachine;
        public override string SubKeyPath => @"SYSTEM\CurrentControlSet\Services\kbdclass\Parameters";
        public override string ValueName => "ThreadPriority";
        public override uint TargetDwordValue => 31;

        public override (string ApplicabilityState, string Reason) DetectApplicability()
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\kbdclass", false);
            if (key == null) return (InputOptimizationState.NotApplicable, "kbdclass driver not installed.");
            return base.DetectApplicability();
        }
    }

    public class UsbSelectiveSuspendDetector : IInputOptimizationDetector
    {
        public string Id => "usb.selective_suspend";
        public string Name => "USB Selective Suspend for Input";
        public string Category => "USB";
        public string Risk => "ADVANCED";
        public bool RequiresReboot => true;
        public InputVerificationMode VerificationMode => InputVerificationMode.PostRebootReadback;
        public string TechnicalLocation => @"USB Input Hub Power Parameters";
        public string Description => "Prevents USB controllers from putting connected mouse/keyboard ports into low-power suspend states.";

        public (string ApplicabilityState, string Reason) DetectApplicability()
        {
            // Device-specific detection
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\USB", false);
                if (key != null)
                {
                    string cur = ReadCurrentState();
                    if (cur == GetTargetState()) return (InputOptimizationState.AlreadyOptimal, "USB selective suspend already configured for input.");
                    return (InputOptimizationState.Applicable, "USB input subsystem supports power management tuning.");
                }
            }
            catch { }
            return (InputOptimizationState.NotApplicable, "USB Selective Suspend parameter not applicable to this controller configuration.");
        }

        public string ReadCurrentState()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\USB", false);
                if (key == null) return "Not Found";
                var val = key.GetValue("DisableSelectiveSuspend");
                return val != null ? val.ToString() ?? "0" : "0";
            }
            catch { return "Not Found"; }
        }

        public string GetTargetState() => "1";

        public (bool Success, string NewState, string ApplyResult, string FailureReason) Apply(IBackupManager backup)
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\USB", true);
                if (key == null) return (false, ReadCurrentState(), "FAILED", "Unable to create or open Services\\USB key");

                var old = key.GetValue("DisableSelectiveSuspend");
                if (old != null) backup.BackupRegistryValue("AntiGravity.InputOptimizer", @"SYSTEM\CurrentControlSet\Services\USB", "DisableSelectiveSuspend", old, "REG_DWORD", 1, "APPLIED");

                key.SetValue("DisableSelectiveSuspend", 1, RegistryValueKind.DWord);
                string rb = ReadCurrentState();
                return (rb == "1", rb, rb == "1" ? "SUCCESS" : "FAILED", string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ReadCurrentState(), "FAILED", ex.Message);
            }
        }

        public (bool Verified, string Message) Verify()
        {
            string cur = ReadCurrentState();
            bool match = (cur == "1");
            return (match, match ? "USB Selective Suspend disabled for input." : "Setting mismatch.");
        }
    }

    #endregion

    public class InputOptimizerEngine : IInputOptimizerEngine
    {
        private readonly IBackupManager _backupManager;
        private readonly InputTransactionJournal _journal;
        private readonly List<IInputOptimizationDetector> _detectors;

        public InputOptimizerEngine() : this(new InputBackupManager())
        {
        }

        public InputOptimizerEngine(IBackupManager backupManager)
        {
            _backupManager = backupManager;
            _journal = new InputTransactionJournal();
            
            _detectors = new List<IInputOptimizationDetector>
            {
                // Mouse Core
                new MouseSpeedDetector(),
                new MouseThreshold1Detector(),
                new MouseThreshold2Detector(),
                new MouseSensitivityDetector(),
                new MouseTrailsDetector(),
                new ActiveWindowTrackingDetector(),
                new MouseHoverTimeDetector(),
                new SnapToDefaultButtonDetector(),
                new MouseCurvesDetector(),

                // Keyboard Core
                new KeyboardDelayDetector(),
                new KeyboardSpeedDetector(),
                new FilterKeysFlagsDetector(),
                new StickyKeysFlagsDetector(),
                new ToggleKeysFlagsDetector(),

                // Advanced / Driver Queues (Requires Reboot)
                new MouclassQueueSizeDetector(),
                new KbdclassQueueSizeDetector(),
                new MouhidQueueLengthDetector(),
                new KbdhidQueueLengthDetector(),
                new SystemResponsivenessDetector(),
                new Win32PrioritySeparationDetector(),
                new MouclassThreadPriorityDetector(),
                new KbdclassThreadPriorityDetector(),
                new UsbSelectiveSuspendDetector()
            };

            ReconcilePendingReboots();
        }

        public void ProcessPendingReboots()
        {
            ReconcilePendingReboots();
        }

        /// <summary>
        /// Authoritative Startup Reconciliation Sequence:
        /// 1. Load persisted transactions.
        /// 2. Re-read real live system values.
        /// 3. If live value matches target -> mark VERIFIED and remove from pending reboot.
        /// 4. If live value does not match target:
        ///    - If system rebooted since apply timestamp -> mark VERIFICATION_FAILED.
        ///    - If system has not rebooted -> keep APPLIED_PENDING_REBOOT.
        /// 5. Update transaction journal.
        /// </summary>
        public void ReconcilePendingReboots()
        {
            var tx = _journal.LoadLatestTransaction();
            if (tx == null || tx.Items == null || tx.Items.Count == 0) return;

            long currentUptime = Environment.TickCount64;
            bool dirty = false;

            foreach (var item in tx.Items.Where(x => x.State == InputOptimizationState.AppliedPendingReboot || x.VerificationPending))
            {
                var detector = _detectors.FirstOrDefault(d => d.Id.Equals(item.OptimizationId, StringComparison.OrdinalIgnoreCase));
                string liveCurrent = detector != null ? detector.ReadCurrentState() : ReadRealRegistryValue(item.RegistryPath, item.ValueName, item.IsBinary);

                bool isMatch = false;
                if (item.IsBinary)
                {
                    isMatch = string.Equals(liveCurrent, item.TargetState, StringComparison.OrdinalIgnoreCase);
                }
                else if (uint.TryParse(item.TargetState, out uint expUint))
                {
                    if (uint.TryParse(liveCurrent, out uint actUint))
                    {
                        isMatch = (actUint == expUint);
                    }
                    else if (int.TryParse(liveCurrent, out int actInt))
                    {
                        isMatch = (unchecked((uint)actInt) == expUint);
                    }
                }
                else
                {
                    isMatch = string.Equals(liveCurrent, item.TargetState, StringComparison.OrdinalIgnoreCase);
                }

                bool hasRebooted = (currentUptime < item.BootUptimeAtApplyMs) || (DateTime.UtcNow - item.Timestamp).TotalHours > 12;

                if (isMatch)
                {
                    if (item.RequiresReboot && !hasRebooted)
                    {
                        // Registry write succeeded, but system has not restarted yet to load new driver parameters
                        item.State = InputOptimizationState.AppliedPendingReboot;
                        item.VerificationPending = true;
                        item.AfterState = liveCurrent;
                    }
                    else
                    {
                        item.State = InputOptimizationState.Verified;
                        item.VerificationPending = false;
                        item.AfterState = liveCurrent;
                        item.FailureReason = string.Empty;
                        dirty = true;
                        Debug.WriteLine($"[InputOptimizerReconciliation] Item {item.OptimizationId} successfully VERIFIED post-reboot.");
                    }
                }
                else
                {
                    if (hasRebooted)
                    {
                        item.State = InputOptimizationState.VerificationFailed;
                        item.VerificationPending = false;
                        item.AfterState = liveCurrent;
                        item.FailureReason = $"Target value not persisted after system reboot (Current: '{liveCurrent}', Expected: '{item.TargetState}')";
                        dirty = true;
                    }
                    else
                    {
                        // Still waiting for system reboot
                        item.State = InputOptimizationState.AppliedPendingReboot;
                        item.VerificationPending = true;
                    }
                }
            }

            if (dirty)
            {
                int verified = tx.Items.Count(x => x.State == InputOptimizationState.Verified || x.State == InputOptimizationState.AlreadyOptimal);
                int failed = tx.Items.Count(x => x.State == InputOptimizationState.Failed || x.State == InputOptimizationState.VerificationFailed);
                int pending = tx.Items.Count(x => x.VerificationPending || x.State == InputOptimizationState.AppliedPendingReboot);

                if (pending > 0) tx.Status = "WAITING_FOR_REBOOT";
                else if (failed == 0) tx.Status = "VERIFIED";
                else if (verified > 0) tx.Status = "PARTIAL";
                else tx.Status = "FAILED";

                _journal.SaveTransaction(tx);
            }
        }

        public async Task<InputOptimizationPlan> PlanInputOptimizationAsync()
        {
            return await Task.Run(() =>
            {
                // 1. Authoritative Reconciliation FIRST before building pending list
                ReconcilePendingReboots();

                var plan = new InputOptimizationPlan();
                var tx = _journal.LoadLatestTransaction();

                var journalMap = tx?.Items.ToDictionary(x => x.OptimizationId, StringComparer.OrdinalIgnoreCase)
                                 ?? new Dictionary<string, InputTransactionItem>(StringComparer.OrdinalIgnoreCase);

                foreach (var detector in _detectors)
                {
                    var (appState, appReason) = detector.DetectApplicability();
                    string currentVal = detector.ReadCurrentState();
                    string targetVal = detector.GetTargetState();

                    var action = new InputOptimizationAction
                    {
                        Id = detector.Id,
                        Name = detector.Name,
                        Category = detector.Category,
                        Risk = detector.Risk,
                        Reason = detector.TechnicalLocation,
                        RequiresReboot = detector.RequiresReboot,
                        VerificationMode = detector.VerificationMode,
                        CurrentValue = currentVal,
                        TargetValue = targetVal,
                        SupportState = appState
                    };

                    // Check if item is in pending reboot or verified journal
                    if (journalMap.TryGetValue(detector.Id, out var txItem))
                    {
                        if (txItem.State == InputOptimizationState.AppliedPendingReboot || txItem.VerificationPending)
                        {
                            action.Applicable = true;
                            action.AlreadyOptimized = false;
                            action.Status = "RESTART_REQUIRED";
                            action.ExecutionState = InputOptimizationState.AppliedPendingReboot;
                            action.VerificationState = "PENDING_REBOOT";
                            action.Reason = detector.TechnicalLocation;
                            plan.Actions.Add(action);
                            continue;
                        }
                        else if (txItem.State == InputOptimizationState.Verified)
                        {
                            action.Applicable = true;
                            action.AlreadyOptimized = true;
                            action.Status = "OPTIMIZED";
                            action.ExecutionState = InputOptimizationState.AlreadyOptimal;
                            action.VerificationState = InputOptimizationState.Verified;
                            plan.Actions.Add(action);
                            continue;
                        }
                    }

                    // Process Applicability State
                    if (appState == InputOptimizationState.NotApplicable)
                    {
                        action.Applicable = false;
                        action.AlreadyOptimized = false;
                        action.Status = "NOT_AVAILABLE";
                        action.ExecutionState = InputOptimizationState.NotAvailable;
                        action.VerificationState = "NOT_APPLICABLE";
                    }
                    else if (appState == InputOptimizationState.Unsupported)
                    {
                        action.Applicable = false;
                        action.AlreadyOptimized = false;
                        action.Status = "NOT_SUPPORTED";
                        action.ExecutionState = InputOptimizationState.NotSupported;
                        action.VerificationState = "UNSUPPORTED";
                    }
                    else if (appState == InputOptimizationState.AlreadyOptimal || currentVal == targetVal)
                    {
                        action.Applicable = true;
                        action.AlreadyOptimized = true;
                        action.Status = "OPTIMIZED";
                        action.ExecutionState = InputOptimizationState.AlreadyOptimal;
                        action.VerificationState = InputOptimizationState.Verified;
                    }
                    else
                    {
                        // Genuine Applicable & Pending optimization
                        action.Applicable = true;
                        action.AlreadyOptimized = false;
                        action.Status = "PENDING";
                        action.ExecutionState = InputOptimizationState.Pending;
                        action.VerificationState = "UNVERIFIED";
                    }

                    plan.Actions.Add(action);
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
                    a.Status != "OPTIMIZED" && 
                    a.Status != "ALREADY_OPTIMIZED" && 
                    a.Status != "RESTART_REQUIRED" && 
                    a.Status != "NOT_AVAILABLE" && 
                    a.Status != "NOT_SUPPORTED").ToList();
                
                if (!string.IsNullOrEmpty(riskLevel) && riskLevel.TrimStart().StartsWith("["))
                {
                    try
                    {
                        var ids = JsonSerializer.Deserialize<List<string>>(riskLevel);
                        if (ids != null && ids.Count > 0)
                        {
                            var idSet = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
                            actionsToApply = actionsToApply.Where(a => idSet.Contains(a.Id)).ToList();
                        }
                    }
                    catch { }
                }
                else if (riskLevel.Equals("CORE", StringComparison.OrdinalIgnoreCase))
                {
                    actionsToApply = actionsToApply.Where(a => a.Risk.Equals("CORE", StringComparison.OrdinalIgnoreCase)).ToList();
                }
                else if (riskLevel.Equals("ADVANCED", StringComparison.OrdinalIgnoreCase))
                {
                    actionsToApply = actionsToApply.Where(a => a.Risk.Equals("ADVANCED", StringComparison.OrdinalIgnoreCase) || a.Risk.Equals("EXPERIMENTAL", StringComparison.OrdinalIgnoreCase)).ToList();
                }

                if (actionsToApply.Count == 0)
                    return (true, "All selected items are already optimal or awaiting reboot verification.");

                var tx = new InputOptimizerTransaction
                {
                    Profile = riskLevel ?? "Default",
                    Status = "RUNNING",
                    SystemBootTimeMs = Environment.TickCount64
                };

                bool allSuccess = true;
                int appliedCount = 0;
                int restartRequiredCount = 0;
                int verifiedImmediateCount = 0;
                int failedCount = 0;

                foreach (var act in actionsToApply)
                {
                    var detector = _detectors.FirstOrDefault(d => d.Id.Equals(act.Id, StringComparison.OrdinalIgnoreCase));
                    if (detector == null) continue;

                    var txItem = new InputTransactionItem
                    {
                        OptimizationId = act.Id,
                        Name = act.Name,
                        Category = act.Category,
                        RegistryPath = act.Reason,
                        ValueName = act.Name,
                        BeforeState = act.CurrentValue,
                        TargetState = act.TargetValue,
                        State = InputOptimizationState.Applying,
                        RequiresReboot = act.RequiresReboot,
                        VerificationMode = act.VerificationMode,
                        BootUptimeAtApplyMs = Environment.TickCount64,
                        IsBinary = act.Id == "mouse.pointer_curves"
                    };
                    tx.Items.Add(txItem);

                    var (success, newState, applyResult, failureReason) = detector.Apply(_backupManager);

                    if (success)
                    {
                        appliedCount++;
                        if (act.RequiresReboot)
                        {
                            act.Status = "RESTART_REQUIRED";
                            txItem.State = InputOptimizationState.AppliedPendingReboot;
                            txItem.ApplyResult = "SUCCESS";
                            txItem.VerificationPending = true;
                            txItem.AfterState = newState;
                            restartRequiredCount++;
                        }
                        else
                        {
                            act.Status = "OPTIMIZED";
                            txItem.State = InputOptimizationState.Verified;
                            txItem.ApplyResult = "SUCCESS";
                            txItem.VerificationPending = false;
                            txItem.AfterState = newState;
                            verifiedImmediateCount++;
                        }
                    }
                    else
                    {
                        act.Status = "FAILED";
                        txItem.State = InputOptimizationState.Failed;
                        txItem.ApplyResult = "FAILED";
                        txItem.VerificationPending = false;
                        txItem.FailureReason = failureReason;
                        failedCount++;
                        allSuccess = false;
                    }
                }

                _journal.SaveTransaction(tx);

                // Broadcast Win32 SPI parameters
                try
                {
                    NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETMOUSE, 0, (int[])null!, NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);
                    NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETKEYBOARDDELAY, 0, 0, NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);
                    NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETKEYBOARDSPEED, 31, 0, NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);
                }
                catch { }

                if (restartRequiredCount > 0) tx.Status = "WAITING_FOR_REBOOT";
                else if (failedCount == 0) tx.Status = "VERIFIED";
                else if (appliedCount > 0) tx.Status = "PARTIAL";
                else tx.Status = "FAILED";

                _journal.SaveTransaction(tx);

                string msg = restartRequiredCount > 0 
                    ? $"{appliedCount} OPTIMIZATIONS APPLIED • {restartRequiredCount} WAITING FOR RESTART VERIFICATION" 
                    : $"Applied {appliedCount} optimizations ({verifiedImmediateCount} verified immediately).";

                if (failedCount > 0)
                    msg += $" ({failedCount} failed)";

                return (allSuccess, msg);
            });
        }

        public async Task<(bool Success, string Message)> RestoreInputOptimizationAsync()
        {
            return await Task.Run(() =>
            {
                bool ok = _backupManager.RestoreByOwner("AntiGravity.InputOptimizer");
                if (ok)
                {
                    _journal.Clear();
                }
                return (ok, ok ? "Restored original input configuration." : "Failed to restore input configuration.");
            });
        }

        public static string ReadRealRegistryValue(string pathWithValName, string valName, bool isBinary)
        {
            try
            {
                string[] parts = pathWithValName.Split('\\');
                if (parts.Length < 3) return "Not Found";

                RegistryKey root = parts[0].Contains("LocalMachine", StringComparison.OrdinalIgnoreCase) || parts[0].Contains("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)
                    ? Registry.LocalMachine
                    : Registry.CurrentUser;

                string subkey = string.Join("\\", parts.Skip(1).Take(parts.Length - 2));
                string actualValName = string.IsNullOrEmpty(valName) ? parts.Last() : valName;

                using var key = root.OpenSubKey(subkey, false);
                if (key == null) return "Not Found";

                if (isBinary)
                {
                    var bytes = key.GetValue(actualValName) as byte[];
                    return bytes != null ? BitConverter.ToString(bytes).Replace("-", " ") : "Not Found";
                }
                else
                {
                    var val = key.GetValue(actualValName);
                    return val != null ? val.ToString() ?? "Not Found" : "Not Found";
                }
            }
            catch (Exception ex)
            {
                return ex is UnauthorizedAccessException ? "Access Denied" : "Error";
            }
        }
    }

    internal class InputBackupManager : IBackupManager
    {
        private readonly IBackupManager _inner = BackupManager.Instance;

        public event Action<BackupTransaction>? TransactionRecorded
        {
            add => _inner.TransactionRecorded += value;
            remove => _inner.TransactionRecorded -= value;
        }

        public string BeginBatchTransaction(string profileId, string profileName, string category) => _inner.BeginBatchTransaction(profileId, profileName, category);
        public BackupTransaction RecordTransaction(BackupTransaction transaction) => _inner.RecordTransaction(transaction);
        public bool CommitBatchTransaction(string batchTransactionId) => _inner.CommitBatchTransaction(batchTransactionId);

        public BackupTransaction CaptureRegistryTweak(string profileName, string optimizationName, string hive, string subKey, string valueName, object? oldValue, string oldType, object? targetValue, string riskLevel = "SAFE", string? parentBatchId = null) =>
            _inner.CaptureRegistryTweak(profileName, optimizationName, hive, subKey, valueName, oldValue, oldType, targetValue, riskLevel, parentBatchId);

        public BackupTransaction CaptureServiceTweak(string profileName, string serviceName, string previousStartup, string newStartup, string previousState, string riskLevel = "SAFE", string? parentBatchId = null) =>
            _inner.CaptureServiceTweak(profileName, serviceName, previousStartup, newStartup, previousState, riskLevel, parentBatchId);

        public BackupTransaction CapturePowerPlanTweak(string profileName, string planName, string previousGuid, string newGuid, string riskLevel = "SAFE", string? parentBatchId = null) =>
            _inner.CapturePowerPlanTweak(profileName, planName, previousGuid, newGuid, riskLevel, parentBatchId);

        public BackupTransaction CaptureInputTweak(string profileName, string optimizationName, string targetSetting, object? previousValue, object? newValue, string riskLevel = "SAFE", string? parentBatchId = null) =>
            _inner.CaptureInputTweak(profileName, optimizationName, targetSetting, previousValue, newValue, riskLevel, parentBatchId);

        public BackupTransaction CaptureNetworkTweak(string profileName, string settingName, string targetInterface, object? previousValue, object? newValue, string riskLevel = "SAFE", string? parentBatchId = null) =>
            _inner.CaptureNetworkTweak(profileName, settingName, targetInterface, previousValue, newValue, riskLevel, parentBatchId);

        public BackupTransaction CaptureStorageCleanup(string profileName, string categoryName, long reclaimedBytes, int filesDeleted, List<string> targetPaths, bool wasQuarantined = false, string? parentBatchId = null) =>
            _inner.CaptureStorageCleanup(profileName, categoryName, reclaimedBytes, filesDeleted, targetPaths, wasQuarantined, parentBatchId);

        public BackupTransaction CaptureMemoryRuntime(string profileName, long ramReclaimedMb, string loadBefore, string loadAfter, string details, string? parentBatchId = null) =>
            _inner.CaptureMemoryRuntime(profileName, ramReclaimedMb, loadBefore, loadAfter, details, parentBatchId);

        public BackupTransaction CaptureSystemRepair(string profileName, string operationName, string beforeState, string afterState, string? parentBatchId = null) =>
            _inner.CaptureSystemRepair(profileName, operationName, beforeState, afterState, parentBatchId);

        public BackupTransaction CaptureGenericTweak(string profileName, string optimizationId, string name, string category, string operationType, string beforeState, string afterState, string rollbackMethod, bool canRollback, string riskLevel = "SAFE", string? parentBatchId = null) =>
            _inner.CaptureGenericTweak(profileName, optimizationId, name, category, operationType, beforeState, afterState, rollbackMethod, canRollback, riskLevel, parentBatchId);

        public Task<RestoreResult> RestoreSingleTransactionAsync(string transactionId, CancellationToken ct = default) => _inner.RestoreSingleTransactionAsync(transactionId, ct);
        public Task<RestoreResult> RestoreProfileAsync(string profileName, CancellationToken ct = default) => _inner.RestoreProfileAsync(profileName, ct);
        public Task<RestoreResult> RestoreFullSystemSnapshotAsync(CancellationToken ct = default) => _inner.RestoreFullSystemSnapshotAsync(ct);
        public Task<bool> CreateSystemRestorePointAsync(string description, CancellationToken ct = default) => _inner.CreateSystemRestorePointAsync(description, ct);

        public List<BackupTransaction> GetAllTransactions() => _inner.GetAllTransactions();
        public List<ProfileBackupGroup> GetProfileBackupGroups() => _inner.GetProfileBackupGroups();
        public BackupSummaryStats GetSummaryStats() => _inner.GetSummaryStats();
        public BackupTransaction? GetTransactionById(string transactionId) => _inner.GetTransactionById(transactionId);
        public bool DeleteTransaction(string transactionId) => _inner.DeleteTransaction(transactionId);
        public void ClearOldBackups(int retentionDays) => _inner.ClearOldBackups(retentionDays);

        public bool CreateSystemRestorePoint(string description) => _inner.CreateSystemRestorePoint(description);
        public bool BackupRegistryState(IEnumerable<string> registryPaths) => _inner.BackupRegistryState(registryPaths);
        public bool BackupServiceState(IEnumerable<string> serviceNames) => _inner.BackupServiceState(serviceNames);
        public bool LogKilledProcesses(IEnumerable<string> processPaths) => _inner.LogKilledProcesses(processPaths);
        public bool RestoreAll() => _inner.RestoreAll();
        public bool BackupRegistryValue(string owner, string registryKey, string valueName, object oldValue, string oldType, object targetValue, string result) =>
            _inner.BackupRegistryValue(owner, registryKey, valueName, oldValue, oldType, targetValue, result);
        public bool RestoreByOwner(string owner) => _inner.RestoreByOwner(owner);
    }
}
