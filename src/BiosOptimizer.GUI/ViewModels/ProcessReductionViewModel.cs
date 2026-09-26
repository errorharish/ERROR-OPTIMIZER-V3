#nullable enable
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
using System.Windows.Input;
using System.Windows.Threading;

namespace BiosOptimizer.GUI.ViewModels;

public class ProcessCandidateModel : INotifyPropertyChanged
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string ProcessPath { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SafetyLevel { get; set; } = "SAFE TO TERMINATE";
    public bool HasVisibleWindow { get; set; }
    public bool IsForeground { get; set; }
    public double CpuUsagePercent { get; set; }
    public double RamUsageMb { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string DetectionTime { get; set; } = string.Empty;

    public bool IsSafeToKill => Category == "SafeToKill" || Category == "4" || SafetyLevel == "SAFE TO TERMINATE";
    public bool IsCaution => Category == "ActiveApp" || Category == "3" || Category == "Browser" || Category == "2" || SafetyLevel == "CAUTION";
    public bool IsProtected => !IsSafeToKill && !IsCaution;
    public bool CanSelect => IsSafeToKill;

    public string FormattedRam => RamUsageMb >= 1024.0 ? $"{RamUsageMb / 1024.0:F1} GB" : $"{RamUsageMb:F0} MB";
    public string FormattedCpu => CpuUsagePercent > 0 ? $"{CpuUsagePercent:F1}%" : "0.0%";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class ProcessReductionViewModel : ViewModelBase
{
    private readonly IIpcClient _ipc;
    private readonly DispatcherTimer _autoRefreshTimer;

    private readonly List<ProcessCandidateModel> _allProcessCandidates = new();
    private ObservableCollection<ProcessCandidateModel> _filteredProcessCandidates = new();

    public ObservableCollection<ProcessCandidateModel> ProcessCandidates 
    {
        get => _filteredProcessCandidates;
        set { _filteredProcessCandidates = value; OnPropertyChanged(); }
    }

    // ── Search & Filter & Sort Properties ───────────────────────────
    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged();
                ApplyFilterAndSort();
            }
        }
    }

    private string _currentFilter = "ALL"; // ALL, SAFE, CAUTION, PROTECTED, HIGH_MEM, HIGH_CPU
    public string CurrentFilter
    {
        get => _currentFilter;
        set
        {
            if (_currentFilter != value)
            {
                _currentFilter = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsFilterAll));
                OnPropertyChanged(nameof(IsFilterSafe));
                OnPropertyChanged(nameof(IsFilterCaution));
                OnPropertyChanged(nameof(IsFilterProtected));
                OnPropertyChanged(nameof(IsFilterHighMemory));
                OnPropertyChanged(nameof(IsFilterHighCpu));
                ApplyFilterAndSort();
            }
        }
    }

    public bool IsFilterAll => CurrentFilter == "ALL";
    public bool IsFilterSafe => CurrentFilter == "SAFE";
    public bool IsFilterCaution => CurrentFilter == "CAUTION";
    public bool IsFilterProtected => CurrentFilter == "PROTECTED";
    public bool IsFilterHighMemory => CurrentFilter == "HIGH_MEM";
    public bool IsFilterHighCpu => CurrentFilter == "HIGH_CPU";

    private string _currentSort = "SAFETY_RAM"; // SAFETY_RAM, RAM_DESC, CPU_DESC, NAME_ASC
    public string CurrentSort
    {
        get => _currentSort;
        set
        {
            if (_currentSort != value)
            {
                _currentSort = value;
                OnPropertyChanged();
                ApplyFilterAndSort();
            }
        }
    }

    // ── Derived Counts & Metrics ────────────────────────────────────
    public int SafeCount => _allProcessCandidates.Count(p => p.IsSafeToKill);
    public int CautionCount => _allProcessCandidates.Count(p => p.IsCaution);
    public int ProtectedCount => _allProcessCandidates.Count(p => p.IsProtected);
    public int SelectedCount => _allProcessCandidates.Count(p => p.IsSelected);
    public bool HasSelected => SelectedCount > 0;
    public string SelectedCountText => $"{SelectedCount} Selected";

    public double SelectedRamMb => _allProcessCandidates.Where(p => p.IsSelected).Sum(p => p.RamUsageMb);
    public string SelectedRamText => SelectedRamMb >= 1024.0 ? $"{SelectedRamMb / 1024.0:F2} GB" : $"{SelectedRamMb:F0} MB";

    public string SystemStatus
    {
        get
        {
            if (SafeCount > 10 || SelectedRamMb > 1500.0) return "Elevated";
            if (SafeCount > 3 || SelectedRamMb > 500.0) return "Normal";
            return "Healthy";
        }
    }

    public string SystemStatusColor
    {
        get
        {
            return SystemStatus switch
            {
                "Elevated" => "#F59E0B",
                "Normal" => "#3B82F6",
                _ => "#10B981"
            };
        }
    }

    public bool HasProcesses => ProcessCandidates.Count > 0;
    public bool HasNoProcesses => ProcessCandidates.Count == 0;

    // ── Details Modal ───────────────────────────────────────────────
    private ProcessCandidateModel? _selectedProcessForDetails;
    public ProcessCandidateModel? SelectedProcessForDetails
    {
        get => _selectedProcessForDetails;
        set { _selectedProcessForDetails = value; OnPropertyChanged(); }
    }

    private bool _showDetailsModal;
    public bool ShowDetailsModal
    {
        get => _showDetailsModal;
        set { _showDetailsModal = value; OnPropertyChanged(); }
    }

    // ── Confirmation Modal ──────────────────────────────────────────
    private bool _showConfirmationModal;
    public bool ShowConfirmationModal
    {
        get => _showConfirmationModal;
        set { _showConfirmationModal = value; OnPropertyChanged(); }
    }

    // ── Result / Summary ────────────────────────────────────────────
    private bool _hasResult;
    public bool HasResult
    {
        get => _hasResult;
        set { _hasResult = value; OnPropertyChanged(); }
    }

    private string _resultText = string.Empty;
    public string ResultText
    {
        get => _resultText;
        set { _resultText = value; OnPropertyChanged(); }
    }

    // ── Status ──────────────────────────────────────────────────────
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

    // ── Commands ────────────────────────────────────────────────────
    public RelayCommand ScanCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand DeselectAllCommand { get; }
    public RelayCommand SetFilterCommand { get; }
    public RelayCommand SetSortCommand { get; }
    public RelayCommand ViewDetailsCommand { get; }
    public RelayCommand CloseDetailsCommand { get; }
    public RelayCommand OpenConfirmationCommand { get; }
    public RelayCommand CancelConfirmationCommand { get; }
    public RelayCommand ConfirmTerminateCommand { get; }

    public ProcessReductionViewModel(IIpcClient ipc)
    {
        _ipc = ipc;

        ScanCommand = new RelayCommand(async _ => await ScanAsync());
        SelectAllCommand = new RelayCommand(_ => SelectAllSafe());
        DeselectAllCommand = new RelayCommand(_ => DeselectAll());
        SetFilterCommand = new RelayCommand(p => CurrentFilter = p?.ToString() ?? "ALL");
        SetSortCommand = new RelayCommand(p => CurrentSort = p?.ToString() ?? "SAFETY_RAM");

        ViewDetailsCommand = new RelayCommand(p =>
        {
            if (p is ProcessCandidateModel model)
            {
                SelectedProcessForDetails = model;
                ShowDetailsModal = true;
            }
        });
        CloseDetailsCommand = new RelayCommand(_ => ShowDetailsModal = false);

        OpenConfirmationCommand = new RelayCommand(_ =>
        {
            if (HasSelected) ShowConfirmationModal = true;
        }, _ => HasSelected);

        CancelConfirmationCommand = new RelayCommand(_ => ShowConfirmationModal = false);
        ConfirmTerminateCommand = new RelayCommand(async _ =>
        {
            ShowConfirmationModal = false;
            await TerminateAsync();
        });

        // Periodic auto-refresh every 8 seconds (lightweight live sync)
        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(8.0)
        };
        _autoRefreshTimer.Tick += async (s, e) =>
        {
            if (!IsBusy && !ShowConfirmationModal && !ShowDetailsModal)
            {
                await ScanAsync(isAutoRefresh: true);
            }
        };
        _autoRefreshTimer.Start();

        _ = LoadInitialDataAsync();
    }

    public async Task LoadInitialDataAsync()
    {
        if (_allProcessCandidates.Count == 0)
            await ScanAsync();
    }

    private void SelectAllSafe()
    {
        foreach (var item in ProcessCandidates.Where(p => p.CanSelect))
        {
            item.IsSelected = true;
        }
        NotifyCountProperties();
    }

    private void DeselectAll()
    {
        foreach (var item in _allProcessCandidates)
        {
            item.IsSelected = false;
        }
        NotifyCountProperties();
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<ProcessCandidateModel> query = _allProcessCandidates;

        // Search text
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            string term = SearchText.Trim().ToLowerInvariant();
            query = query.Where(p => 
                p.ProcessName.ToLowerInvariant().Contains(term) ||
                p.DisplayName.ToLowerInvariant().Contains(term) ||
                p.Publisher.ToLowerInvariant().Contains(term) ||
                p.ProcessPath.ToLowerInvariant().Contains(term) ||
                p.ProcessId.ToString().Contains(term) ||
                p.Reason.ToLowerInvariant().Contains(term));
        }

        // Category filter
        query = CurrentFilter switch
        {
            "SAFE" => query.Where(p => p.IsSafeToKill),
            "CAUTION" => query.Where(p => p.IsCaution),
            "PROTECTED" => query.Where(p => p.IsProtected),
            "HIGH_MEM" => query.Where(p => p.RamUsageMb >= 100.0),
            "HIGH_CPU" => query.Where(p => p.CpuUsagePercent >= 1.0),
            _ => query
        };

        // Sort order
        query = CurrentSort switch
        {
            "RAM_DESC" => query.OrderByDescending(p => p.RamUsageMb),
            "CPU_DESC" => query.OrderByDescending(p => p.CpuUsagePercent),
            "NAME_ASC" => query.OrderBy(p => p.DisplayName),
            _ => query.OrderByDescending(p => p.IsSafeToKill)
                      .ThenByDescending(p => p.RamUsageMb)
                      .ThenByDescending(p => p.CpuUsagePercent)
        };

        var list = query.ToList();
        UiDispatcher.RunAsync(() =>
        {
            ProcessCandidates = new ObservableCollection<ProcessCandidateModel>(list);
            NotifyCountProperties();
        });
    }

    private void NotifyCountProperties()
    {
        OnPropertyChanged(nameof(SafeCount));
        OnPropertyChanged(nameof(CautionCount));
        OnPropertyChanged(nameof(ProtectedCount));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelected));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(SelectedRamMb));
        OnPropertyChanged(nameof(SelectedRamText));
        OnPropertyChanged(nameof(SystemStatus));
        OnPropertyChanged(nameof(SystemStatusColor));
        OnPropertyChanged(nameof(HasProcesses));
        OnPropertyChanged(nameof(HasNoProcesses));
        OpenConfirmationCommand.RaiseCanExecuteChanged();
    }

    private async Task ScanAsync(bool isAutoRefresh = false)
    {
        if (!isAutoRefresh)
        {
            IsBusy = true;
            HasResult = false;
            StatusText = "Scanning processes...";
        }

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
                    await UiDispatcher.RunAsync(() =>
                    {
                        // Preserve previous selections for active PIDs
                        var previouslySelectedPids = new HashSet<int>(
                            _allProcessCandidates.Where(p => p.IsSelected).Select(p => p.ProcessId));

                        _allProcessCandidates.Clear();

                        foreach (var p in list)
                        {
                            if (string.IsNullOrWhiteSpace(p.DisplayName))
                            {
                                p.DisplayName = p.ProcessName;
                            }

                            p.IsSelected = previouslySelectedPids.Contains(p.ProcessId) && p.CanSelect;
                            p.PropertyChanged += (s, e) =>
                            {
                                if (e.PropertyName == nameof(ProcessCandidateModel.IsSelected))
                                {
                                    NotifyCountProperties();
                                }
                            };
                            _allProcessCandidates.Add(p);
                        }

                        ApplyFilterAndSort();

                        if (!isAutoRefresh)
                        {
                            StatusText = $"Scan complete | {list.Count} processes analyzed ({SafeCount} safe to terminate)";
                        }
                    });
                }
            }
            else if (!isAutoRefresh)
            {
                await UiDispatcher.RunAsync(() =>
                {
                    _allProcessCandidates.Clear();
                    ApplyFilterAndSort();
                    StatusText = "Service unavailable — " + (response.ErrorMessage ?? "Unknown error");
                });
            }
        }
        catch (Exception ex)
        {
            if (!isAutoRefresh)
            {
                await UiDispatcher.RunAsync(() =>
                {
                    StatusText = "Scan error: " + ex.Message;
                });
            }
        }
        finally
        {
            if (!isAutoRefresh)
            {
                IsBusy = false;
            }
        }
    }

    private async Task TerminateAsync()
    {
        var toKill = _allProcessCandidates.Where(p => p.IsSelected).ToList();
        if (toKill.Count == 0) return;

        double ramBeforeMb = toKill.Sum(p => p.RamUsageMb);
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
                int failed = toKill.Count - killed;

                await UiDispatcher.RunAsync(() =>
                {
                    string ramStr = ramBeforeMb >= 1024.0 ? $"{ramBeforeMb / 1024.0:F2} GB" : $"{ramBeforeMb:F0} MB";
                    ResultText = $"Terminated: {killed} | Failed: {failed} | RAM Reclaimed: ≈{ramStr}";
                    HasResult = true;
                    StatusText = $"Complete — freed ≈{ramStr} RAM";
                });

                // Immediate rescan to update live Windows state
                await ScanAsync();
            }
            else
            {
                StatusText = "Failed: " + response.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
        }
    }
}
