using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels.Base;
using BiosOptimizer.IPC.Contracts;
using System.Collections.ObjectModel;
using System.Linq;
using System.Collections.Generic;

namespace BiosOptimizer.GUI.ViewModels
{
    public static class BrushHelper { public static Brush GetFrozenBrush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; } }
    public class LogLineViewModel : ViewModelBase
    {
        public string Timestamp { get; set; } = "";
        public string Message { get; set; } = "";
        public Brush Color { get; set; } = BrushHelper.GetFrozenBrush(System.Windows.Media.Color.FromRgb(200, 200, 200));
    }

    public class TierViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private readonly string _tierId;

        private string _status = "Ready";
        private string _resultText = "";
        private bool _isExecuting;
        private int _actionCount;
        private string _emptyStateMessage = "";
        private double _optimizationProgress = 0;
        private bool _showToast;
        private string _toastTitle = "";
        private string _toastMessage = "";

        public string TierName { get; }
        public string Description { get; }

        public string RiskLabel => _tierId switch
        {
            "Normal"             => "SAFE",
            "Pro"                => "MODERATE RISK",
            "Ultimate"           => "HIGH RISK",
            "MaximumPerformance" => "PERFORMANCE",
            "BiosSafe"           => "HARDWARE",
            _                    => "SAFE"
        };

        public string OptimizationTip => _tierId switch
        {
            "Normal"             => "Normal mode applies baseline optimizations that are universally safe for any PC, balancing performance with complete stability. Ideal for daily drivers and work laptops.",
            "Pro"                => "Pro mode dials up performance by disabling non-essential background services and telemetry. Great for moderate gaming without breaking Windows features.",
            "Ultimate"           => "Ultimate mode strips down the OS for maximum frame rates and minimal latency. Recommended only for dedicated gaming rigs, as some system features will be disabled.",
            "MaximumPerformance" => "Maximum Performance forces hardware-level power plans and registry configurations to completely eliminate power saving throttling. Expect higher temperatures.",
            "BiosSafe"           => "BIOS Safe mode detects and applies firmware-level optimizations via WMI. Changes are strictly hardware-supported and fully reversible.",
            "RamTest"            => "RAM Test optimizes memory allocation and clears standby lists to free up physical memory immediately.",
            _                    => "Run this profile to improve your system's performance and responsiveness."
        };

        

        public ObservableCollection<LogLineViewModel> LiveLogs { get; } = new();

        public string StatusMessage { get => _status; set { _status = value; OnPropertyChanged(); } }
        public string ResultText { get => _resultText; set { _resultText = value; OnPropertyChanged(); OnPropertyChanged(nameof(ResultVisibility)); } }
        
        public bool IsExecuting 
        { 
            get => _isExecuting; 
            set 
            { 
                if (_isExecuting == value) return;
                _isExecuting = value; 
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    OnPropertyChanged(); 
                    OnPropertyChanged(nameof(IsExecutingVisibility));
                    OnPropertyChanged(nameof(IsNotExecuting));
                    OnPropertyChanged(nameof(RunButtonText));
                    (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        OnPropertyChanged(nameof(IsExecuting)); 
                        OnPropertyChanged(nameof(IsExecutingVisibility));
                        OnPropertyChanged(nameof(IsNotExecuting));
                        OnPropertyChanged(nameof(RunButtonText));
                        (ApplyCommand as RelayCommand)?.RaiseCanExecuteChanged();
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    });
                }
            } 
        }

        public bool IsNotExecuting => !_isExecuting;
        
        public int ActionCount { get => _actionCount; set { _actionCount = value; OnPropertyChanged(); } }
        public string EmptyStateMessage { get => _emptyStateMessage; set { _emptyStateMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(EmptyStateVisibility)); } }
        
        

        public bool ShowToast { get => _showToast; set { _showToast = value; OnPropertyChanged(); } }
        public string ToastTitle { get => _toastTitle; set { _toastTitle = value; OnPropertyChanged(); } }
        public string ToastMessage { get => _toastMessage; set { _toastMessage = value; OnPropertyChanged(); } }


        public string RunButtonText => _isExecuting ? "OPTIMIZING..." : "RUN OPTIMIZATION";

        /// <summary>Global optimization progress 0-100. Drives the single WaterWaveProgress bar above RUN OPTIMIZATION.</summary>
        public double OptimizationProgress
        {
            get => _optimizationProgress;
            set { _optimizationProgress = value; OnPropertyChanged(); }
        }

        public Visibility ResultVisibility => string.IsNullOrEmpty(_resultText) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility IsExecutingVisibility => _isExecuting ? Visibility.Visible : Visibility.Collapsed;
        public Visibility EmptyStateVisibility => string.IsNullOrEmpty(_emptyStateMessage) ? Visibility.Collapsed : Visibility.Visible;

        public Brush StatusDotColor => _isExecuting
            ? BrushHelper.GetFrozenBrush(Color.FromRgb(229, 57, 53)) // Error Red for active
            : BrushHelper.GetFrozenBrush(Color.FromRgb(76, 175, 80));

        public ICommand PreviewCommand { get; }
        public ICommand ApplyCommand { get; }

        public TierViewModel(IIpcClient ipc, string tierId, string tierName, string description)
        {
            _ipc = ipc;
            _tierId = tierId;
            TierName = tierName;
            Description = description;

            PreviewCommand = new RelayCommand(async _ => await PreviewAsync());
            ApplyCommand   = new RelayCommand(async _ => await ApplyAsync(), _ => !IsExecuting);
            
            _ = PreviewAsync(); // auto-load on start
        }

        private void AddLog(string tag, string message, Color color)
        {
            if (Application.Current.Dispatcher.CheckAccess())
            {
                LiveLogs.Add(new LogLineViewModel
                {
                    Timestamp = System.DateTime.Now.ToString("HH:mm:ss"),
                    Message = $"[{tag}] {message}",
                    Color = BrushHelper.GetFrozenBrush(color)
                });
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() => AddLog(tag, message, color));
            }
        }

        private async Task PreviewAsync()
        {
            IsExecuting = false;
            StatusMessage = "Waiting to start...";
            EmptyStateMessage = "Ready to optimize.";
            ResultText = "";
            LiveLogs.Clear();

            var r = await _ipc.SendRequestAsync(IpcMessageType.PreviewTier, _tierId);

            if (r.Success && !string.IsNullOrEmpty(r.Data))
            {
                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var preview = System.Text.Json.JsonSerializer.Deserialize<BiosOptimizer.IPC.Contracts.TierPreviewDto>(r.Data, options);
                    
                    if (preview != null)
                    {
                        ActionCount = preview.ActionCount;
                        StatusMessage = $"{preview.ActionCount} actions available";
                    }
                }
                catch (System.Exception ex)
                {
                    EmptyStateMessage = "Error parsing preview data.";
                    StatusMessage = "âŒ " + ex.Message;
                }
            }
            else
            {
                EmptyStateMessage = "Service offline or error: " + r.ErrorMessage;
                StatusMessage = "âŒ " + r.ErrorMessage;
            }
        }

        private async Task ApplyAsync()
        {
            if (IsExecuting) return;
            IsExecuting = true;
            
            LiveLogs.Clear();
            AddLog("SYSTEM", $"Starting {TierName} optimization...", Color.FromRgb(100, 150, 255));
            
            StatusMessage = "WAITING TO START...";
            ResultText = "";
            OptimizationProgress = 0;
            
            // Re-evaluate Command execution constraints
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();

            try
            {
                await foreach (var r in _ipc.SendStreamingRequestAsync(IpcMessageType.ApplyTier, _tierId))
                {
                    if (r.Success && r.Data != null)
                    {
                        if (r.Data.Contains("ProgressUpdate"))
                        {
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(r.Data);
                                var actionNode = doc.RootElement.GetProperty("Action");
                                var dto = System.Text.Json.JsonSerializer.Deserialize<BiosOptimizer.IPC.Contracts.OptimizationActionDto>(actionNode.GetRawText());
                                int current = doc.RootElement.TryGetProperty("Current", out var cProp) ? cProp.GetInt32() : 0;
                                int total = doc.RootElement.TryGetProperty("Total", out var tProp) ? tProp.GetInt32() : 0;
                                
                                if (dto != null)
                                {
                                    if (total > 0)
                                    {
                                        OptimizationProgress = (current * 100.0) / total;
                                        StatusMessage = $"Optimizing ({current}/{total})...";
                                    }
                                    
                                    string category = string.IsNullOrEmpty(dto.Category) ? "SERVICE" : dto.Category.ToUpper();
                                    string actionName = string.IsNullOrEmpty(dto.ActionName) ? dto.DisplayName : dto.ActionName;
                                    
                                    if (dto.Status == "Running")
                                    {
                                        AddLog(category, $"{dto.Reason} {actionName}", Color.FromRgb(200, 200, 200));
                                    }
                                    else if (dto.Status == "Success" || dto.Status == "Verified" || dto.Status == "AlreadyOptimized")
                                    {
                                        AddLog("SUCCESS", $"{actionName} ({dto.Reason})", Color.FromRgb(76, 175, 80));
                                    }
                                    else if (dto.Status == "Failed")
                                    {
                                        AddLog("FAILED", $"{actionName} ({dto.Reason})", Color.FromRgb(244, 67, 54));
                                    }
                                    else
                                    {
                                        AddLog("SKIPPED", $"{actionName} ({dto.Reason})", Color.FromRgb(158, 158, 158));
                                    }
                                }
                            }
                            catch { }
                        }
                        else
                        {
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(r.Data);
                                if (doc.RootElement.TryGetProperty("FinalResult", out var finalResultProp) && finalResultProp.GetBoolean())
                                {
                                    var summaryNode = doc.RootElement.GetProperty("Summary");
                                    int appliedCount = summaryNode.GetProperty("Success").GetInt32();
                                    int failedCount = summaryNode.GetProperty("Failed").GetInt32();
                                    int blockedCount = summaryNode.GetProperty("Blocked").GetInt32();
                                    int alreadyOptimizedCount = summaryNode.GetProperty("AlreadyOptimized").GetInt32();
                                    int notApplicableCount = summaryNode.GetProperty("NotApplicable").GetInt32();
                                    
                                    int notAvailableCount = 0;
                                    if (summaryNode.TryGetProperty("NotAvailable", out var nav)) notAvailableCount = nav.GetInt32();


                                    var failedReasons = new List<string>();
                                    foreach (var reason in summaryNode.GetProperty("FailedReasons").EnumerateArray())
                                        failedReasons.Add(reason.GetString());
                                    
                                    var blockedReasons = new List<string>();
                                    foreach (var reason in summaryNode.GetProperty("BlockedReasons").EnumerateArray())
                                        blockedReasons.Add(reason.GetString());
                                        
                                    string primaryReason = string.Join("\n", failedReasons.Concat(blockedReasons).Distinct());
                                    if (string.IsNullOrWhiteSpace(primaryReason)) primaryReason = "None";

                                    if (failedCount == 0 && blockedCount == 0)
                                    {
                                        StatusMessage = "Optimization Successful";
                                        AddLog("SYSTEM", "Optimization completed successfully.", Color.FromRgb(76, 175, 80));
                                        Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
                                            {
                                                mainVm.ShowOptimizationResult("Success", 
                                                    "âœ“ OPTIMIZATION SUCCESS", 
                                                    "Optimization completed safely", 
                                                    appliedCount, alreadyOptimizedCount, notAvailableCount, notApplicableCount, blockedCount, failedCount, primaryReason);
                                            }
                                        });
                                    }
                                    else if (appliedCount > 0 && (failedCount > 0 || blockedCount > 0))
                                    {
                                        StatusMessage = "Optimization Partially Completed";
                                        AddLog("SYSTEM", "Optimization completed with some failures.", Color.FromRgb(255, 152, 0));
                                        Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
                                            {
                                                mainVm.ShowOptimizationResult("Partial", 
                                                    "âš  OPTIMIZATION PARTIAL", 
                                                    "Some actions were blocked or failed", 
                                                    appliedCount, alreadyOptimizedCount, notAvailableCount, notApplicableCount, blockedCount, failedCount, primaryReason);
                                            }
                                        });
                                    }
                                    else if (appliedCount == 0 && (failedCount > 0 || blockedCount > 0))
                                    {
                                        StatusMessage = "Optimization Failed";
                                        AddLog("SYSTEM", "Optimization failed.", Color.FromRgb(244, 67, 54));
                                        Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
                                            {
                                                mainVm.ShowOptimizationResult("Failed", 
                                                    "âœ• OPTIMIZATION FAILED", 
                                                    "No optimizations were successfully applied", 
                                                    appliedCount, alreadyOptimizedCount, notAvailableCount, notApplicableCount, blockedCount, failedCount, primaryReason);
                                            }
                                        });
                                    }
                                    else
                                    {
                                        StatusMessage = "System Already Optimized";
                                        AddLog("SYSTEM", "System is already optimized.", Color.FromRgb(76, 175, 80));
                                        Application.Current.Dispatcher.Invoke(() =>
                                        {
                                            if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
                                            {
                                                mainVm.ShowOptimizationResult("Success", 
                                                    "âœ“ ALREADY OPTIMIZED", 
                                                    "No changes were necessary", 
                                                    appliedCount, alreadyOptimizedCount, notAvailableCount, notApplicableCount, blockedCount, failedCount, primaryReason);
                                            }
                                        });
                                    }
                                }
                                else
                                {
                                    ResultText = r.Data;
                                }
                            }
                            catch (Exception ex) 
                            { 
                                ResultText = "UI Parsing Error: " + ex.Message;
                                AddLog("ERROR", ResultText, Color.FromRgb(244, 67, 54));
                            }
                            
                            OptimizationProgress = 100;
                        }
                        
                        // Force UI to render real-time progress before processing the next IPC message
                        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                    }
                    else if (!r.Success)
                    {
                        ResultText = r.ErrorMessage;
                        StatusMessage = "âŒ " + r.ErrorMessage;
                        AddLog("ERROR", StatusMessage, Color.FromRgb(244, 67, 54));
                    }
                }
            }
            catch (System.Exception ex)
            {
                ResultText = "Error: " + ex.Message;
                StatusMessage = "âŒ Exception occurred";
                AddLog("ERROR", ResultText, Color.FromRgb(244, 67, 54));
            }
            finally
            {
                IsExecuting = false;
                OptimizationProgress = 100; // Force wave animation to stop
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task HideToastAsync()
        {
            await System.Threading.Tasks.Task.Delay(5000);
            ShowToast = false;
        }
    }
}



