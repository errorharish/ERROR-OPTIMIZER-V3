using BiosOptimizer.GUI.Services;
using BiosOptimizer.IPC.Contracts;
using BiosOptimizer.GUI.ViewModels.Base;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BiosOptimizer.GUI.ViewModels;

public class ProcessCandidateModel : INotifyPropertyChanged
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; }
    public string ProcessPath { get; set; }
    public string Category { get; set; }
    public double CpuUsagePercent { get; set; }
    public string Reason { get; set; }
    
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    public event PropertyChangedEventHandler PropertyChanged;
}

public class ProcessReductionViewModel : ViewModelBase
{
    private readonly IIpcClient _ipc;

    public ObservableCollection<ProcessCandidateModel> SafeToKillProcesses { get; } = new();

    private string _statusText = "Ready to scan.";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }
    
    private string _resultText;
    public string ResultText
    {
        get => _resultText;
        set { _resultText = value; OnPropertyChanged(); }
    }

    public RelayCommand ScanCommand { get; }
    public RelayCommand TerminateCommand { get; }

    public ProcessReductionViewModel(IIpcClient ipc)
    {
        System.Diagnostics.Debug.WriteLine("[PROCESS REDUCTION] ViewModel created");
        _ipc = ipc;
        ScanCommand = new RelayCommand(async _ => await ScanAsync());
        TerminateCommand = new RelayCommand(async _ => await TerminateAsync(), _ => SafeToKillProcesses.Any(p => p.IsSelected));

        // Listen for selection changes to update TerminateCommand canExecute
        SafeToKillProcesses.CollectionChanged += (s, e) => TerminateCommand.RaiseCanExecuteChanged();

        System.Diagnostics.Debug.WriteLine("[PROCESS REDUCTION] Initialization started");
        _ = LoadInitialDataAsync();
    }

    public async Task LoadInitialDataAsync()
    {
        if (SafeToKillProcesses.Count == 0)
        {
            await ScanAsync();
        }
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusText = "Scanning processes...";
        
        System.Diagnostics.Debug.WriteLine("[PROCESS REDUCTION] Requesting process candidates...");

        try
        {
            var response = await _ipc.SendRequestAsync(IpcMessageType.GetProcessCandidates);
            if (response.Success && !string.IsNullOrEmpty(response.Data))
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                var list = JsonSerializer.Deserialize<List<ProcessCandidateModel>>(response.Data, options);
                if (list != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[PROCESS REDUCTION] Response received\nCandidates={list.Count}");
                    
                    // The UI abstraction requires updates on the Dispatcher thread
                    await BiosOptimizer.GUI.Services.UiDispatcher.RunAsync(() =>
                    {
                        SafeToKillProcesses.Clear();
                        // Only show SafeToKill per requirements
                        var safeList = list.Where(p => p.Category == "SafeToKill" || p.Category == "4" /* enum int */).ToList();
                        foreach (var p in safeList)
                        {
                            p.IsSelected = false; // "Every process is UNCHECKED by default"
                            p.PropertyChanged += (s, e) => { if (e.PropertyName == "IsSelected") TerminateCommand.RaiseCanExecuteChanged(); };
                            SafeToKillProcesses.Add(p);
                        }

                        if (safeList.Count == 0)
                        {
                            ResultText = "NO SAFE PROCESS CANDIDATES FOUND\n\nReason:\nAll detected processes are protected, active, or required.";
                        }
                        
                        StatusText = $"Detected: {list.Count} total processes | Safe to terminate: {safeList.Count}";
                    });
                }
            }
            else
            {
                await BiosOptimizer.GUI.Services.UiDispatcher.RunAsync(() =>
                {
                    SafeToKillProcesses.Clear();
                    ResultText = "PROCESS REDUCTION SERVICE UNAVAILABLE\n\n" + (response.ErrorMessage ?? "Unknown error");
                    StatusText = "Failed to scan processes.";
                });
            }
        }
        catch (Exception ex)
        {
            await BiosOptimizer.GUI.Services.UiDispatcher.RunAsync(() =>
            {
                SafeToKillProcesses.Clear();
                ResultText = "PROCESS SCAN FAILED\n\n" + ex.Message;
                StatusText = "Error during process scan.";
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TerminateAsync()
    {
        var toKill = SafeToKillProcesses.Where(p => p.IsSelected).ToList();
        if (toKill.Count == 0) return;

        IsBusy = true;
        StatusText = $"Terminating {toKill.Count} processes...";

        try
        {
            var pids = toKill.Select(p => p.ProcessId).ToList();
            var payload = JsonSerializer.Serialize(pids);
            
            var response = await _ipc.SendRequestAsync(IpcMessageType.TerminateProcesses, payload);
            if (response.Success && !string.IsNullOrEmpty(response.Data))
            {
                var result = JsonSerializer.Deserialize<Dictionary<string, int>>(response.Data);
                int killed = result != null && result.ContainsKey("Killed") ? result["Killed"] : 0;
                
                ResultText = $"Termination complete.\nBefore: {SafeToKillProcesses.Count} safe targets | Terminated: {killed}";
                
                // Remove killed from list
                foreach (var p in toKill)
                {
                    SafeToKillProcesses.Remove(p);
                }
                StatusText = $"Successfully terminated {killed} processes.";
            }
            else
            {
                StatusText = "Failed to terminate: " + response.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
