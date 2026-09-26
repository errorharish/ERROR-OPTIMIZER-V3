
using BiosOptimizer.GUI.ViewModels.Base;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.IPC.Contracts;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Collections.ObjectModel;

namespace BiosOptimizer.GUI.ViewModels
{
    public class RegistryTweakPlan
    {
        public ulong DetectedRamBytes { get; set; }
        public string DetectedRamGb { get; set; } = string.Empty;
        public string SelectedBaselineGb { get; set; } = string.Empty;
        
        public string CurrentValueHex { get; set; } = string.Empty;
        public string CurrentValueDec { get; set; } = string.Empty;
        public string CurrentType { get; set; } = string.Empty;

        public string TargetValueHex { get; set; } = string.Empty;
        public string TargetValueDec { get; set; } = string.Empty;
        
        public string Status { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }
    public class RegistryTweakViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;

        private string _detectedRam = "Unavailable";
        public string DetectedRam { get => _detectedRam; set { _detectedRam = value; OnPropertyChanged(); } }

        private string _selectedBaseline = "Unavailable";
        public string SelectedBaseline { get => _selectedBaseline; set { _selectedBaseline = value; OnPropertyChanged(); } }

        private string _currentHex = "Unavailable";
        public string CurrentHex { get => _currentHex; set { _currentHex = value; OnPropertyChanged(); } }

        private string _targetHex = "Unavailable";
        public string TargetHex { get => _targetHex; set { _targetHex = value; OnPropertyChanged(); } }

        private string _targetDec = "Unavailable";
        public string TargetDec { get => _targetDec; set { _targetDec = value; OnPropertyChanged(); } }

        private string _registryType = "Unavailable";
        public string RegistryType { get => _registryType; set { _registryType = value; OnPropertyChanged(); } }

        private string _status = "SERVICE OFFLINE";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); UpdateCanApply(); } }

        private string _reason = "";
        public string Reason { get => _reason; set { _reason = value; OnPropertyChanged(); } }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); UpdateCanApply(); } }

        private bool _canApply;
        public bool CanApply { get => _canApply; set { _canApply = value; OnPropertyChanged(); } }

        public ObservableCollection<string> LiveLogs { get; } = new ObservableCollection<string>();

        public ICommand ScanCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand RestoreCommand { get; }

        public RegistryTweakViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            ScanCommand = new RelayCommand(async _ => await ScanAsync());
            ApplyCommand = new RelayCommand(async _ => await ApplyAsync(), _ => CanApply);
            RestoreCommand = new RelayCommand(async _ => await RestoreAsync());

        }

        public void OnLoaded()
        {
            _ = ScanAsync();
        }

        private void Log(string msg)
        {
            App.Current.Dispatcher.Invoke(() => LiveLogs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}"));
        }

        private void UpdateCanApply()
        {
            CanApply = !IsBusy && (Status == "READY" || Status == "FAILED" || Status == "RESTORED" || Status == "RESTORE FAILED");
        }

        private async Task ScanAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            Log("DETECTING RAM...");
            try
            {
                var res = await _ipc.SendRequestAsync(IpcMessageType.PlanRegistryTweak);
                if (res.Success && res.Data != null)
                {
                    var plan = JsonSerializer.Deserialize<RegistryTweakPlan>(res.Data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (plan != null)
                    {
                        DetectedRam = plan.DetectedRamGb;
                        Log($"Detected: {plan.DetectedRamGb}");

                        SelectedBaseline = plan.SelectedBaselineGb;
                        Log($"SELECTING PROFILE... Selected: {plan.SelectedBaselineGb} baseline");

                        CurrentHex = plan.CurrentValueHex;
                        Log($"READING REGISTRY... Current: {plan.CurrentValueHex}");

                        TargetHex = plan.TargetValueHex;
                        TargetDec = plan.TargetValueDec;
                        RegistryType = plan.CurrentType;

                        Status = plan.Status;
                        Reason = plan.Reason;
                        
                        if (Status == "ALREADY OPTIMIZED")
                        {
                            Log("✓ ALREADY OPTIMIZED");
                        }
                    }
                }
                else
                {
                    Status = "FAILED";
                    Reason = res.ErrorMessage ?? "Unknown error.";
                }
            }
            catch (Exception ex)
            {
                Status = "FAILED";
                Reason = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ApplyAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            Log("CREATING BACKUP...");
            Log($"APPLYING... Target: {TargetHex}");
            try
            {
                var res = await _ipc.SendRequestAsync(IpcMessageType.ApplyRegistryTweak);
                if (res.Success && res.Data != null)
                {
                    var obj = JsonDocument.Parse(res.Data).RootElement;
                    Status = obj.GetProperty("Status").GetString() ?? "VERIFIED";
                    Reason = obj.GetProperty("Message").GetString() ?? "";
                    Log("VERIFYING...");
                    Log(Status == "APPLIED — RESTART RECOMMENDED" ? "VERIFIED (Restart Recommended)" : Status);
                }
                else
                {
                    Status = "FAILED";
                    Reason = res.ErrorMessage ?? "Write failed.";
                    Log($"✕ REGISTRY TWEAK FAILED: {Reason}");
                }
                await ScanAsync(); // refresh UI
            }
            catch (Exception ex)
            {
                Status = "FAILED";
                Reason = ex.Message;
                Log($"✕ ERROR: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RestoreAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            Log("RESTORING BACKUP...");
            try
            {
                var res = await _ipc.SendRequestAsync(IpcMessageType.RestoreRegistryTweak);
                if (res.Success && res.Data != null)
                {
                    var obj = JsonDocument.Parse(res.Data).RootElement;
                    Status = obj.GetProperty("Status").GetString() ?? "RESTORED";
                    Reason = obj.GetProperty("Message").GetString() ?? "";
                    Log("RESTORED");
                }
                else
                {
                    Status = "RESTORE FAILED";
                    Reason = res.ErrorMessage ?? "Restore failed.";
                    Log($"✕ RESTORE FAILED: {Reason}");
                }
                await ScanAsync(); // refresh UI
            }
            catch (Exception ex)
            {
                Status = "FAILED";
                Reason = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}




