using System;
using System.Threading.Tasks;
using System.Windows.Input;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels.Base;
using BiosOptimizer.IPC.Contracts;

namespace BiosOptimizer.GUI.ViewModels
{
    public class RegistryValuesViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;

        public RegistryValuesRamViewModel RamViewModel { get; }
        public GpuRegistryValuesViewModel GpuViewModel { get; }

        private ViewModelBase _currentSubView;
        public ViewModelBase CurrentSubView
        {
            get => _currentSubView;
            set
            {
                if (_currentSubView != value)
                {
                    _currentSubView = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _activeSubTab = "RAM";
        public string ActiveSubTab
        {
            get => _activeSubTab;
            set
            {
                if (_activeSubTab != value)
                {
                    _activeSubTab = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsRamTabActive));
                    OnPropertyChanged(nameof(IsGpuTabActive));
                    _ = SwitchSubViewAsync();
                }
            }
        }

        public bool IsRamTabActive => string.Equals(ActiveSubTab, "RAM", StringComparison.OrdinalIgnoreCase);
        public bool IsGpuTabActive => string.Equals(ActiveSubTab, "GPU", StringComparison.OrdinalIgnoreCase);

        public ICommand SelectSubTabCommand { get; }
        public ICommand SelectRamTabCommand { get; }
        public ICommand SelectGpuTabCommand { get; }

        public RegistryValuesViewModel(IIpcClient ipc)
        {
            _ipc = ipc;

            RamViewModel = new RegistryValuesRamViewModel(ipc);
            GpuViewModel = new GpuRegistryValuesViewModel(ipc);

            _currentSubView = RamViewModel;

            SelectSubTabCommand = new RelayCommand(p =>
            {
                if (p is string tab && !string.IsNullOrEmpty(tab))
                {
                    ActiveSubTab = tab.ToUpperInvariant();
                }
            });

            SelectRamTabCommand = new RelayCommand(_ => ActiveSubTab = "RAM");
            SelectGpuTabCommand = new RelayCommand(_ => ActiveSubTab = "GPU");
        }

        private async Task SwitchSubViewAsync()
        {
            var oldSubView = CurrentSubView;
            if (oldSubView != null)
            {
                try
                {
                    await oldSubView.OnNavigatedFromAsync();
                }
                catch { }
            }

            if (IsGpuTabActive)
            {
                CurrentSubView = GpuViewModel;
                try
                {
                    await GpuViewModel.OnNavigatedToAsync();
                }
                catch { }
            }
            else
            {
                CurrentSubView = RamViewModel;
                try
                {
                    await RamViewModel.OnNavigatedToAsync();
                }
                catch { }
            }
        }

        public override async Task OnNavigatedToAsync()
        {
            if (CurrentSubView != null)
            {
                try
                {
                    await CurrentSubView.OnNavigatedToAsync();
                }
                catch { }
            }
        }

        public override async Task OnNavigatedFromAsync()
        {
            if (CurrentSubView != null)
            {
                try
                {
                    await CurrentSubView.OnNavigatedFromAsync();
                }
                catch { }
            }
        }
    }
}
