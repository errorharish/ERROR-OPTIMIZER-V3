using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Implementations.RegistryValues;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels.Base;
using BiosOptimizer.IPC.Contracts;
using Microsoft.Win32;

namespace BiosOptimizer.GUI.ViewModels
{
    public class RegistryValuesRamViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;

        private const string RegistrySubKey = @"SYSTEM\CurrentControlSet\Control";
        private const string ValueName = "SvcHostSplitThresholdInKB";
        private const string ItemId = "val.svchost.split_threshold";

        // RAM Size to DWORD mapping
        private static readonly Dictionary<string, uint> RamMapping = new()
        {
            { "4 GB",   0x00400000 }, // 4,194,304 KB
            { "8 GB",   0x00800000 }, // 8,388,608 KB
            { "12 GB",  0x00C00000 }, // 12,582,912 KB
            { "16 GB",  0x01000000 }, // 16,777,216 KB
            { "24 GB",  0x01800000 }, // 25,165,824 KB
            { "32 GB",  0x02000000 }, // 33,554,432 KB
            { "64 GB",  0x04000000 }, // 67,108,864 KB
            { "96 GB",  0x06000000 }, // 100,663,296 KB
            { "128 GB", 0x08000000 }  // 134,217,728 KB
        };

        public ObservableCollection<string> AvailableRamSizes { get; } = new()
        {
            "4 GB", "8 GB", "12 GB", "16 GB", "24 GB", "32 GB", "64 GB", "96 GB", "128 GB"
        };

        // ── Target Configuration ───────────────────────────────────────────
        public string TargetValueName => ValueName;
        public string TargetRegistryPath => @"HKLM\SYSTEM\CurrentControlSet\Control";
        public string TargetValueType => "REG_DWORD";
        public string TargetDescription => "Changes the configured Service Host split threshold according to the chosen RAM profile.";

        // ── State ──────────────────────────────────────────────────────────
        private string _selectedRamSize = "16 GB";
        public string SelectedRamSize
        {
            get => _selectedRamSize;
            set
            {
                if (_selectedRamSize != value && !string.IsNullOrEmpty(value))
                {
                    _selectedRamSize = value;
                    OnPropertyChanged();
                    UpdateCalculatedTarget();
                    OnPropertyChanged(nameof(IsConfigured));
                    OnPropertyChanged(nameof(CanApply));
                    OnPropertyChanged(nameof(ApplyButtonText));
                }
            }
        }

        private string _detectedRamText = "Detecting...";
        public string DetectedRamText
        {
            get => _detectedRamText;
            set { _detectedRamText = value; OnPropertyChanged(); }
        }

        private double _detectedRamGb = 16.0;

        private bool _isDifferentFromDetected;
        public bool IsDifferentFromDetected
        {
            get => _isDifferentFromDetected;
            set { _isDifferentFromDetected = value; OnPropertyChanged(); }
        }

        private string _targetHexValue = "0x01000000";
        public string TargetHexValue
        {
            get => _targetHexValue;
            set { _targetHexValue = value; OnPropertyChanged(); }
        }

        private string _targetDecimalValue = "16,777,216 KB";
        public string TargetDecimalValue
        {
            get => _targetDecimalValue;
            set { _targetDecimalValue = value; OnPropertyChanged(); }
        }

        private string _targetFormula = "16 GB × 1024 × 1024 = 16,777,216 KB";
        public string TargetFormula
        {
            get => _targetFormula;
            set { _targetFormula = value; OnPropertyChanged(); }
        }

        // ── Readback State ─────────────────────────────────────────────────
        private string _currentHexValue = "0x00380000";
        public string CurrentHexValue
        {
            get => _currentHexValue;
            set { _currentHexValue = value; OnPropertyChanged(); }
        }

        private string _currentDecimalValue = "3,670,016 KB";
        public string CurrentDecimalValue
        {
            get => _currentDecimalValue;
            set { _currentDecimalValue = value; OnPropertyChanged(); }
        }

        private uint _currentDwordValue = 0x00380000;

        // ── Live Metrics ───────────────────────────────────────────────────
        private int _liveSvchostCount = 0;
        public int LiveSvchostCount
        {
            get => _liveSvchostCount;
            set { _liveSvchostCount = value; OnPropertyChanged(); }
        }

        private double _liveSvchostMemoryMb = 0.0;
        public double LiveSvchostMemoryMb
        {
            get => _liveSvchostMemoryMb;
            set { _liveSvchostMemoryMb = value; OnPropertyChanged(); }
        }

        // ── Backup / Transaction Status ────────────────────────────────────
        private bool _hasBackup;
        public bool HasBackup
        {
            get => _hasBackup;
            set { _hasBackup = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanRestore)); }
        }

        private string _statusText = "READY";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private string _statusMessage = "Select RAM and click APPLY VALUE to configure svchost threshold.";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private static Brush GetResourceBrush(string key, Brush fallback)
        {
            try
            {
                if (Application.Current?.TryFindResource(key) is Brush b)
                    return b;
            }
            catch { }
            return fallback;
        }

        private Brush _statusBrush = Brushes.Gray;
        public Brush StatusBrush
        {
            get => _statusBrush;
            set { _statusBrush = value; OnPropertyChanged(); }
        }

        private bool _requiresRestart;
        public bool RequiresRestart
        {
            get => _requiresRestart;
            set { _requiresRestart = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanApply));
                OnPropertyChanged(nameof(CanRestore));
                OnPropertyChanged(nameof(ApplyButtonText));
            }
        }

        public bool IsConfigured => RamMapping.TryGetValue(_selectedRamSize, out uint targetDword) && _currentDwordValue == targetDword;
        public bool CanApply => !IsBusy && !IsConfigured;
        public bool CanRestore => HasBackup && !IsBusy;
        public string ApplyButtonText => IsBusy ? "APPLYING..." : (IsConfigured ? "100% OPTIMIZED" : "APPLY VALUE");

        // ── Details Modal ──────────────────────────────────────────────────
        private bool _isDetailsModalOpen;
        public bool IsDetailsModalOpen
        {
            get => _isDetailsModalOpen;
            set { _isDetailsModalOpen = value; OnPropertyChanged(); }
        }

        // ── Commands ───────────────────────────────────────────────────────
        public ICommand ApplyCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand ResetSelectionCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenDetailsCommand { get; }
        public ICommand CloseDetailsCommand { get; }
        public ICommand RestartLaterCommand { get; }

        public RegistryValuesRamViewModel(IIpcClient ipc)
        {
            _ipc = ipc;

            ApplyCommand = new RelayCommand(async _ => await ApplyRegistryValueAsync());
            RestoreCommand = new RelayCommand(async _ => await RestorePreviousValueAsync());
            ResetSelectionCommand = new RelayCommand(_ => ResetSelection());
            RefreshCommand = new RelayCommand(async _ => await ReadCurrentRegistryAndHardwareAsync());
            OpenDetailsCommand = new RelayCommand(_ => IsDetailsModalOpen = true);
            CloseDetailsCommand = new RelayCommand(_ => IsDetailsModalOpen = false);
            RestartLaterCommand = new RelayCommand(_ =>
            {
                StatusText = "RESTART SCHEDULED";
                StatusMessage = "Windows restart is required to apply the new Service Host grouping.";
                StatusBrush = GetResourceBrush("WarningBrush", Brushes.Orange);
            });

            _ = InitializeAsync();

            OptimizationStateCoordinator.OptimizationStateChanged += () =>
            {
                if (!IsBusy)
                {
                    _ = ReadCurrentRegistryAndHardwareAsync();
                }
            };
        }

        public async Task InitializeAsync()
        {
            await ReadCurrentRegistryAndHardwareAsync();
        }

        public override async Task OnNavigatedToAsync()
        {
            await ReadCurrentRegistryAndHardwareAsync();
        }

        public async Task ReadCurrentRegistryAndHardwareAsync()
        {
            IsBusy = true;
            try
            {
                // 1. Detect Real RAM Hardware via Authoritative Hardware Detection
                var snap = await Task.Run(() => HardwareDetectionService.Instance.GetSnapshot(forceRefresh: true));
                _detectedRamGb = snap.Memory.InstalledPhysicalGb > 0 ? snap.Memory.InstalledPhysicalGb : 16.0;
                DetectedRamText = $"{snap.Memory.InstalledFormatted} (Usable: {snap.Memory.UsableFormatted})";

                // Match nearest available option
                string matchedSize = MatchNearestRamOption(_detectedRamGb);
                _selectedRamSize = matchedSize;
                OnPropertyChanged(nameof(SelectedRamSize));
                UpdateCalculatedTarget();

                // 2. Read live registry value
                var (exists, val) = await Task.Run(() =>
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                        using var key = baseKey.OpenSubKey(RegistrySubKey, false);
                        if (key != null)
                        {
                            var raw = key.GetValue(ValueName);
                            if (raw != null)
                            {
                                return (true, Convert.ToUInt32(raw));
                            }
                        }
                    }
                    catch { }
                    return (false, 0x00380000u);
                });

                if (exists)
                {
                    _currentDwordValue = val;
                    CurrentHexValue = $"0x{val:X8}";
                    CurrentDecimalValue = $"{val:N0} KB";
                }
                else
                {
                    _currentDwordValue = 0x00380000; // 3.5GB default
                    CurrentHexValue = "0x00380000 (Default)";
                    CurrentDecimalValue = "3,670,016 KB (Default)";
                }

                // 3. Read live svchost metrics
                var (count, memMb) = await Task.Run(() =>
                {
                    try
                    {
                        var procs = Process.GetProcessesByName("svchost");
                        int c = procs.Length;
                        long bytes = procs.Sum(p =>
                        {
                            try { return p.WorkingSet64; } catch { return 0L; }
                        });
                        return (c, bytes / (1024.0 * 1024.0));
                    }
                    catch
                    {
                        return (0, 0.0);
                    }
                });
                LiveSvchostCount = count;
                LiveSvchostMemoryMb = memMb;

                // Check backup availability in BackupManager or snapshots
                var txs = BackupManager.Instance.GetAllTransactions();
                HasBackup = txs.Any(t => (t.OptimizationName != null && t.OptimizationName.Contains("svchost", StringComparison.OrdinalIgnoreCase)) ||
                                         (t.TargetKey != null && t.TargetKey.Contains("svchost", StringComparison.OrdinalIgnoreCase)));

                // Update Status
                uint targetDword = RamMapping[_selectedRamSize];
                if (_currentDwordValue == targetDword)
                {
                    StatusText = "ALREADY CONFIGURED";
                    StatusMessage = $"Registry is already set to {TargetHexValue} for {_selectedRamSize} RAM.";
                    StatusBrush = GetResourceBrush("SuccessBrush", Brushes.LimeGreen);
                }
                else
                {
                    StatusText = "READY TO APPLY";
                    StatusMessage = $"Target value {TargetHexValue} is ready to be applied.";
                    StatusBrush = GetResourceBrush("TextSecondaryBrush", Brushes.Gray);
                }
            }
            catch (Exception ex)
            {
                StatusText = "ERROR";
                StatusMessage = $"Failed to read registry or hardware state: {ex.Message}";
                StatusBrush = GetResourceBrush("ErrorBrush", Brushes.Red);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void UpdateCalculatedTarget()
        {
            if (RamMapping.TryGetValue(SelectedRamSize, out uint targetDword))
            {
                TargetHexValue = $"0x{targetDword:X8}";
                TargetDecimalValue = $"{targetDword:N0} KB";

                // Parse selected GB number
                int gb = int.Parse(SelectedRamSize.Replace(" GB", ""));
                TargetFormula = $"{gb} GB × 1024 × 1024 = {targetDword:N0} KB";

                // Highlight if differs from detected hardware
                double selectedGb = gb;
                IsDifferentFromDetected = Math.Abs(selectedGb - _detectedRamGb) > 1.5;
            }
        }

        private string MatchNearestRamOption(double ramGb)
        {
            double minDiff = double.MaxValue;
            string bestMatch = "16 GB";

            foreach (var size in RamMapping.Keys)
            {
                int gb = int.Parse(size.Replace(" GB", ""));
                double diff = Math.Abs(gb - ramGb);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    bestMatch = size;
                }
            }
            return bestMatch;
        }

        public void ResetSelection()
        {
            string matched = MatchNearestRamOption(_detectedRamGb);
            SelectedRamSize = matched;
        }

        public async Task ApplyRegistryValueAsync()
        {
            if (!CanApply) return;
            IsBusy = true;
            StatusText = "APPLYING...";
            StatusMessage = "Creating backup snapshot and writing target value to Windows Registry...";
            StatusBrush = GetResourceBrush("AccentBrush", Brushes.Cyan);

            try
            {
                uint targetDword = RamMapping[SelectedRamSize];

                // Transactional write with backup and readback verification
                var (success, message, verifiedValue) = await Task.Run(() =>
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

                        // 1. Read current for backup
                        object? oldValue = null;
                        using (var readKey = baseKey.OpenSubKey(RegistrySubKey, false))
                        {
                            oldValue = readKey?.GetValue(ValueName);
                        }

                        // 2. Backup to BackupManager
                        BackupManager.Instance.CaptureRegistryTweak(
                            "RAM Registry",
                            "RAM Service Host Split Threshold",
                            "HKLM",
                            RegistrySubKey,
                            ValueName,
                            oldValue,
                            "DWord",
                            targetDword,
                            "MEDIUM"
                        );

                        // 3. Write target value
                        using (var writeKey = baseKey.CreateSubKey(RegistrySubKey, true))
                        {
                            if (writeKey == null)
                            {
                                return (false, "Failed to open registry subkey with write permissions.", 0u);
                            }
                            writeKey.SetValue(ValueName, unchecked((int)targetDword), RegistryValueKind.DWord);
                            writeKey.Flush();
                        }

                        // 4. Readback verify
                        using (var verifyKey = baseKey.OpenSubKey(RegistrySubKey, false))
                        {
                            var readback = verifyKey?.GetValue(ValueName);
                            if (readback == null)
                            {
                                return (false, "Readback failed: value not found after write.", 0u);
                            }
                            uint written = Convert.ToUInt32(readback);
                            if (written != targetDword)
                            {
                                return (false, $"Verification failed: expected 0x{targetDword:X8}, read 0x{written:X8}", written);
                            }
                            return (true, "Verified successfully.", written);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return (false, "Administrator privileges required to write to HKEY_LOCAL_MACHINE.", 0u);
                    }
                    catch (Exception ex)
                    {
                        return (false, ex.Message, 0u);
                    }
                });

                if (success)
                {
                    _currentDwordValue = verifiedValue;
                    CurrentHexValue = $"0x{verifiedValue:X8}";
                    CurrentDecimalValue = $"{verifiedValue:N0} KB";
                    HasBackup = true;
                    RequiresRestart = true;

                    StatusText = "APPLIED & VERIFIED";
                    StatusMessage = $"Successfully verified registry write (0x{verifiedValue:X8}). Restart Windows for changes to take full effect.";
                    StatusBrush = GetResourceBrush("SuccessBrush", Brushes.LimeGreen);

                    OptimizationStateCoordinator.NotifyOptimizationStateChanged();
                }
                else
                {
                    StatusText = "VERIFICATION FAILED";
                    StatusMessage = message;
                    StatusBrush = GetResourceBrush("ErrorBrush", Brushes.Red);
                }
            }
            catch (Exception ex)
            {
                StatusText = "FAILED";
                StatusMessage = $"Write exception: {ex.Message}";
                StatusBrush = GetResourceBrush("ErrorBrush", Brushes.Red);
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task RestorePreviousValueAsync()
        {
            if (!CanRestore) return;
            IsBusy = true;
            StatusText = "RESTORING...";
            StatusMessage = "Restoring previous registry snapshot...";
            StatusBrush = GetResourceBrush("AccentBrush", Brushes.Cyan);

            try
            {
                var (success, message, restoredValue) = await Task.Run(() =>
                {
                    try
                    {
                        var scanItems = MachineRegistryValueEngine.Instance.ScanAllValues();
                        var svchostItem = scanItems.FirstOrDefault(i => i.Id == ItemId);
                        if (svchostItem != null)
                        {
                            bool ok = MachineRegistryValueEngine.Instance.RollbackRegistryValue(svchostItem, out string rollbackMsg);
                            if (ok)
                            {
                                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                                using var key = baseKey.OpenSubKey(RegistrySubKey, false);
                                var readback = key?.GetValue(ValueName);
                                uint val = readback != null ? Convert.ToUInt32(readback) : 0x00380000u;
                                return (true, "Restored successfully.", val);
                            }
                            return (false, rollbackMsg, 0u);
                        }

                        return (false, "Snapshot item not found for rollback.", 0u);
                    }
                    catch (Exception ex)
                    {
                        return (false, ex.Message, 0u);
                    }
                });

                if (success)
                {
                    _currentDwordValue = restoredValue;
                    CurrentHexValue = $"0x{restoredValue:X8}";
                    CurrentDecimalValue = $"{restoredValue:N0} KB";
                    RequiresRestart = true;

                    StatusText = "RESTORED";
                    StatusMessage = $"Successfully restored previous state (0x{restoredValue:X8}). Restart recommended.";
                    StatusBrush = GetResourceBrush("SuccessBrush", Brushes.LimeGreen);

                    OptimizationStateCoordinator.NotifyOptimizationStateChanged();
                }
                else
                {
                    StatusText = "RESTORE FAILED";
                    StatusMessage = message;
                    StatusBrush = GetResourceBrush("ErrorBrush", Brushes.Red);
                }
            }
            catch (Exception ex)
            {
                StatusText = "RESTORE FAILED";
                StatusMessage = $"Exception: {ex.Message}";
                StatusBrush = GetResourceBrush("ErrorBrush", Brushes.Red);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
