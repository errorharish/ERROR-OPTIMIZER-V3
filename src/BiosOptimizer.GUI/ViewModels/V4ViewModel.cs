using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using BiosOptimizer.GUI.ViewModels.Base;

namespace BiosOptimizer.GUI.ViewModels
{
    /// <summary>
    /// V4 planning/analysis surface for the combined Gaming + Editing maximum-performance engine.
    /// This view intentionally separates the V4 UX from the existing V3 profile execution path.
    /// </summary>
    public sealed class V4ViewModel : ViewModelBase
    {
        private int _currentProcessCount;
        private string _scanStatus = "Ready to analyze this PC.";
        private string _targetText = "70–80 when safely achievable";
        private string _reductionText = "Analyze first";
        private string _workloadText = "Gaming + Editing";
        private bool _isScanning;

        public int CurrentProcessCount
        {
            get => _currentProcessCount;
            private set { _currentProcessCount = value; OnPropertyChanged(); }
        }

        public string ScanStatus
        {
            get => _scanStatus;
            private set { _scanStatus = value; OnPropertyChanged(); }
        }

        public string TargetText
        {
            get => _targetText;
            private set { _targetText = value; OnPropertyChanged(); }
        }

        public string ReductionText
        {
            get => _reductionText;
            private set { _reductionText = value; OnPropertyChanged(); }
        }

        public string WorkloadText
        {
            get => _workloadText;
            private set { _workloadText = value; OnPropertyChanged(); }
        }

        public bool IsScanning
        {
            get => _isScanning;
            private set
            {
                _isScanning = value;
                OnPropertyChanged();
                (AnalyzeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand AnalyzeCommand { get; }

        public V4ViewModel()
        {
            AnalyzeCommand = new RelayCommand(_ => _ = AnalyzeAsync(), _ => !IsScanning);
        }

        private async Task AnalyzeAsync()
        {
            if (IsScanning)
                return;

            IsScanning = true;
            ScanStatus = "Scanning the current Windows process footprint...";
            ReductionText = "Calculating";

            try
            {
                var count = await Task.Run(() =>
                {
                    try
                    {
                        return Process.GetProcesses().Length;
                    }
                    catch
                    {
                        return 0;
                    }
                });

                CurrentProcessCount = count;

                if (count <= 80 && count > 0)
                {
                    TargetText = "Already within the 70–80 target range";
                    ReductionText = "0+ immediate process-count reduction";
                    ScanStatus = $"Current footprint: {count} processes. V4 can now analyze persistent background sources.";
                }
                else if (count > 0)
                {
                    var upperBoundReduction = Math.Max(0, count - 70);
                    TargetText = "70–80 when safely achievable";
                    ReductionText = $"Up to ~{upperBoundReduction} processes to evaluate";
                    ScanStatus = $"Current footprint: {count} processes. V4 should classify and reduce only safe, non-required background workload.";
                }
                else
                {
                    ScanStatus = "Process count could not be read on this pass.";
                    ReductionText = "Unavailable";
                }
            }
            finally
            {
                IsScanning = false;
            }
        }
    }
}
