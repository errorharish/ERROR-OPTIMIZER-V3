using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels.Base;
using BiosOptimizer.IPC.Contracts;

namespace BiosOptimizer.GUI.ViewModels
{
    public class BackupRestoreViewModel : ViewModelBase
    {
        private readonly IIpcClient _ipc;
        private readonly IBackupManager _backupManager;

        private string _status = "Universal Backup & Restore Ready";
        private bool _isLoading;
        private string _searchQuery = "";
        private string _selectedCategoryFilter = "ALL";

        // Summary Statistics Properties
        private int _totalBackupsCount;
        public int TotalBackupsCount { get => _totalBackupsCount; set { _totalBackupsCount = value; OnPropertyChanged(); } }

        private int _profileBackupsCount;
        public int ProfileBackupsCount { get => _profileBackupsCount; set { _profileBackupsCount = value; OnPropertyChanged(); } }

        private int _optimizationBackupsCount;
        public int OptimizationBackupsCount { get => _optimizationBackupsCount; set { _optimizationBackupsCount = value; OnPropertyChanged(); } }

        private int _registryBackupsCount;
        public int RegistryBackupsCount { get => _registryBackupsCount; set { _registryBackupsCount = value; OnPropertyChanged(); } }

        private int _systemSnapshotsCount;
        public int SystemSnapshotsCount { get => _systemSnapshotsCount; set { _systemSnapshotsCount = value; OnPropertyChanged(); } }

        private int _restorableCount;
        public int RestorableCount { get => _restorableCount; set { _restorableCount = value; OnPropertyChanged(); } }

        private int _partialCount;
        public int PartialCount { get => _partialCount; set { _partialCount = value; OnPropertyChanged(); } }

        private int _nonReversibleCount;
        public int NonReversibleCount { get => _nonReversibleCount; set { _nonReversibleCount = value; OnPropertyChanged(); } }

        private int _failedCount;
        public int FailedCount { get => _failedCount; set { _failedCount = value; OnPropertyChanged(); } }

        private bool _hasNoTransactions;
        public bool HasNoTransactions { get => _hasNoTransactions; set { _hasNoTransactions = value; OnPropertyChanged(); } }

        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery != value)
                {
                    _searchQuery = value;
                    OnPropertyChanged();
                    ApplyFilter();
                }
            }
        }

        public string SelectedCategoryFilter
        {
            get => _selectedCategoryFilter;
            set
            {
                if (_selectedCategoryFilter != value)
                {
                    _selectedCategoryFilter = value;
                    OnPropertyChanged();
                    ApplyFilter();
                }
            }
        }

        public ObservableCollection<ProfileBackupGroup> ProfileGroups { get; } = new();
        public ObservableCollection<BackupTransaction> AllTransactions { get; } = new();
        public ObservableCollection<BackupTransaction> FilteredTransactions { get; } = new();

        // ── Details Modal Properties
        private bool _isDetailsModalOpen;
        public bool IsDetailsModalOpen { get => _isDetailsModalOpen; set { _isDetailsModalOpen = value; OnPropertyChanged(); } }

        private BackupTransaction? _selectedTransaction;
        public BackupTransaction? SelectedTransaction { get => _selectedTransaction; set { _selectedTransaction = value; OnPropertyChanged(); } }

        // ── Confirmation Modal Properties
        private bool _isRestoreConfirmOpen;
        public bool IsRestoreConfirmOpen { get => _isRestoreConfirmOpen; set { _isRestoreConfirmOpen = value; OnPropertyChanged(); } }

        private string _restoreConfirmTitle = "CONFIRM RESTORE";
        public string RestoreConfirmTitle { get => _restoreConfirmTitle; set { _restoreConfirmTitle = value; OnPropertyChanged(); } }

        private string _restoreConfirmMessage = "";
        public string RestoreConfirmMessage { get => _restoreConfirmMessage; set { _restoreConfirmMessage = value; OnPropertyChanged(); } }

        private string _pendingRestoreType = "SINGLE"; // "SINGLE", "PROFILE", "SYSTEM"
        private string _pendingRestoreTarget = "";

        // ── Advanced Restore Result Modal Properties
        private bool _isRestoreResultModalOpen;
        public bool IsRestoreResultModalOpen { get => _isRestoreResultModalOpen; set { _isRestoreResultModalOpen = value; OnPropertyChanged(); } }

        private string _restoreResultTitle = "RESTORE SUCCESSFUL";
        public string RestoreResultTitle { get => _restoreResultTitle; set { _restoreResultTitle = value; OnPropertyChanged(); } }

        private string _restoreResultIcon = "\uE73E";
        public string RestoreResultIcon { get => _restoreResultIcon; set { _restoreResultIcon = value; OnPropertyChanged(); } }

        private string _restoreResultBrush = "#10B981";
        public string RestoreResultBrush { get => _restoreResultBrush; set { _restoreResultBrush = value; OnPropertyChanged(); } }

        private string _restoreResultBackupName = "";
        public string RestoreResultBackupName { get => _restoreResultBackupName; set { _restoreResultBackupName = value; OnPropertyChanged(); } }

        private string _restoreResultCategory = "";
        public string RestoreResultCategory { get => _restoreResultCategory; set { _restoreResultCategory = value; OnPropertyChanged(); } }

        private string _restoreResultItemsRestored = "";
        public string RestoreResultItemsRestored { get => _restoreResultItemsRestored; set { _restoreResultItemsRestored = value; OnPropertyChanged(); } }

        private string _restoreResultVerification = "";
        public string RestoreResultVerification { get => _restoreResultVerification; set { _restoreResultVerification = value; OnPropertyChanged(); } }

        private string _restoreResultExecutionTime = "";
        public string RestoreResultExecutionTime { get => _restoreResultExecutionTime; set { _restoreResultExecutionTime = value; OnPropertyChanged(); } }

        private string _restoreResultError = "";
        public string RestoreResultError { get => _restoreResultError; set { _restoreResultError = value; OnPropertyChanged(); } }

        public ObservableCollection<string> RestoreResultLogs { get; } = new();

        // Commands
        public ICommand LoadBackupsCommand { get; }
        public ICommand CreateSnapshotCommand { get; }
        public ICommand RestoreSingleCommand { get; }
        public ICommand RestoreProfileCommand { get; }
        public ICommand RestoreSystemCommand { get; }
        public ICommand OpenDetailsCommand { get; }
        public ICommand CloseDetailsCommand { get; }
        public ICommand ConfirmRestoreCommand { get; }
        public ICommand CancelRestoreCommand { get; }
        public ICommand CloseRestoreResultModalCommand { get; }
        public ICommand DeleteBackupCommand { get; }
        public ICommand SetFilterCommand { get; }
        public ICommand ResetFilterCommand { get; }

        public BackupRestoreViewModel(IIpcClient ipc)
        {
            _ipc = ipc;
            _backupManager = BackupManager.Instance;

            LoadBackupsCommand = new RelayCommand(async _ => await LoadBackupsAsync());
            CreateSnapshotCommand = new RelayCommand(async _ => await CreateFullSnapshotAsync());
            RestoreSingleCommand = new RelayCommand(p => PromptRestoreSingle(p as BackupTransaction));
            RestoreProfileCommand = new RelayCommand(p => PromptRestoreProfile(p?.ToString()));
            RestoreSystemCommand = new RelayCommand(_ => PromptRestoreSystem());
            OpenDetailsCommand = new RelayCommand(p => OpenDetails(p as BackupTransaction));
            CloseDetailsCommand = new RelayCommand(_ => IsDetailsModalOpen = false);
            ConfirmRestoreCommand = new RelayCommand(async _ => await ExecuteConfirmedRestoreAsync());
            CancelRestoreCommand = new RelayCommand(_ => IsRestoreConfirmOpen = false);
            CloseRestoreResultModalCommand = new RelayCommand(_ => IsRestoreResultModalOpen = false);
            DeleteBackupCommand = new RelayCommand(p => DeleteTransaction(p as BackupTransaction));
            SetFilterCommand = new RelayCommand(p => SelectedCategoryFilter = p?.ToString() ?? "ALL");
            ResetFilterCommand = new RelayCommand(_ => { SearchQuery = ""; SelectedCategoryFilter = "ALL"; });

            _backupManager.TransactionRecorded += OnTransactionRecorded;

            _ = LoadBackupsAsync();
        }

        private void OnTransactionRecorded(BackupTransaction tx)
        {
            UiDispatcher.Run(async () => await LoadBackupsAsync());
        }

        public async Task LoadBackupsAsync()
        {
            IsLoading = true;
            Status = "Loading universal backup transactions...";

            await Task.Run(() =>
            {
                var stats = _backupManager.GetSummaryStats();
                var groups = _backupManager.GetProfileBackupGroups();
                var txList = _backupManager.GetAllTransactions();

                UiDispatcher.Run(() =>
                {
                    TotalBackupsCount = stats.TotalBackups;
                    ProfileBackupsCount = stats.ProfileBackups;
                    OptimizationBackupsCount = stats.OptimizationBackups;
                    RegistryBackupsCount = stats.RegistryBackups;
                    SystemSnapshotsCount = stats.SystemSnapshots;
                    RestorableCount = stats.RestorableCount;
                    PartialCount = stats.PartialCount;
                    NonReversibleCount = stats.NonReversibleCount;
                    FailedCount = stats.FailedCount;

                    ProfileGroups.Clear();
                    foreach (var g in groups) ProfileGroups.Add(g);

                    AllTransactions.Clear();
                    foreach (var tx in txList) AllTransactions.Add(tx);

                    ApplyFilter();
                    Status = $"Loaded {stats.TotalBackups} universal transactions ({stats.RestorableCount} restorable).";
                    IsLoading = false;
                });
            });
        }

        public void ApplyFilter()
        {
            FilteredTransactions.Clear();
            var query = AllTransactions.AsEnumerable();

            if (!string.IsNullOrEmpty(SelectedCategoryFilter) && SelectedCategoryFilter != "ALL")
            {
                query = SelectedCategoryFilter switch
                {
                    "PROFILE" => query.Where(t => t.OperationType == "BatchContainer" || !t.ProfileId.Equals("Manual", StringComparison.OrdinalIgnoreCase)),
                    "OPTIMIZATION" => query.Where(t => t.OperationType != "BatchContainer"),
                    "REGISTRY" => query.Where(t => t.Category.Equals("Registry", StringComparison.OrdinalIgnoreCase)),
                    "POWER" => query.Where(t => t.Category.Equals("Power", StringComparison.OrdinalIgnoreCase)),
                    "INPUT" => query.Where(t => t.Category.Equals("Input", StringComparison.OrdinalIgnoreCase)),
                    "NETWORK" => query.Where(t => t.Category.Equals("Network", StringComparison.OrdinalIgnoreCase)),
                    "STORAGE" => query.Where(t => t.Category.Equals("Storage", StringComparison.OrdinalIgnoreCase)),
                    "MEMORY" => query.Where(t => t.Category.Equals("Memory", StringComparison.OrdinalIgnoreCase)),
                    "SERVICES" => query.Where(t => t.Category.Equals("Services", StringComparison.OrdinalIgnoreCase)),
                    "GPU" => query.Where(t => t.Category.Equals("GPU", StringComparison.OrdinalIgnoreCase)),
                    "BIOS" => query.Where(t => t.Category.Equals("BIOS", StringComparison.OrdinalIgnoreCase)),
                    "REPAIR" => query.Where(t => t.Category.Equals("Repair", StringComparison.OrdinalIgnoreCase)),
                    "RESTORABLE" => query.Where(t => t.CanRollback && t.RollbackStatus == "AVAILABLE"),
                    "NON-REVERSIBLE" => query.Where(t => !t.CanRollback || t.RollbackStatus == "NOT_SUPPORTED"),
                    "FAILED" => query.Where(t => t.RollbackStatus == "FAILED" || t.Status == "FAILED"),
                    _ => query
                };
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                string s = SearchQuery.Trim();
                query = query.Where(t =>
                    (t.OptimizationName != null && t.OptimizationName.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                    (t.ProfileName != null && t.ProfileName.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                    (t.Category != null && t.Category.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                    (t.TransactionId != null && t.TransactionId.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                    (t.TargetKey != null && t.TargetKey.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                    (t.Notes != null && t.Notes.Contains(s, StringComparison.OrdinalIgnoreCase))
                );
            }

            foreach (var item in query)
            {
                FilteredTransactions.Add(item);
            }

            HasNoTransactions = FilteredTransactions.Count == 0;
        }

        private void OpenDetails(BackupTransaction? tx)
        {
            if (tx == null) return;
            SelectedTransaction = tx;
            IsDetailsModalOpen = true;
        }

        private void PromptRestoreSingle(BackupTransaction? tx)
        {
            if (tx == null) return;
            _pendingRestoreType = "SINGLE";
            _pendingRestoreTarget = tx.TransactionId;

            RestoreConfirmTitle = "CONFIRM OPTIMIZATION RESTORE";
            RestoreConfirmMessage = $"Are you sure you want to restore:\n\n• {tx.OptimizationName}\nCategory: {tx.Category}\nTarget: {tx.TargetKey}\nPrevious State: {tx.BeforeState}\n\nThis will safely roll back only this specific optimization.";
            IsRestoreConfirmOpen = true;
        }

        private void PromptRestoreProfile(string? profileName)
        {
            if (string.IsNullOrEmpty(profileName)) return;
            _pendingRestoreType = "PROFILE";
            _pendingRestoreTarget = profileName;

            RestoreConfirmTitle = "CONFIRM PROFILE RESTORE";
            RestoreConfirmMessage = $"Are you sure you want to restore all optimizations for profile '{profileName}'?\n\nThis will roll back child transactions in reverse order and verify state readback.";
            IsRestoreConfirmOpen = true;
        }

        private void PromptRestoreSystem()
        {
            _pendingRestoreType = "SYSTEM";
            _pendingRestoreTarget = "ALL";

            RestoreConfirmTitle = "CONFIRM FULL SYSTEM RESTORE";
            RestoreConfirmMessage = "⚠ RESTORE ENTIRE SYSTEM\n\nAre you sure you want to roll back all recorded optimizations across all profiles?\n\nThis will restore all modified registry values, services, and power plans.";
            IsRestoreConfirmOpen = true;
        }

        private async Task ExecuteConfirmedRestoreAsync()
        {
            IsRestoreConfirmOpen = false;
            Status = "Executing transactional rollback and real read-back verification...";

            RestoreResult res;
            if (_pendingRestoreType == "SINGLE")
            {
                res = await _backupManager.RestoreSingleTransactionAsync(_pendingRestoreTarget);
            }
            else if (_pendingRestoreType == "PROFILE")
            {
                res = await _backupManager.RestoreProfileAsync(_pendingRestoreTarget);
            }
            else
            {
                res = await _backupManager.RestoreFullSystemSnapshotAsync();
            }

            Status = res.Success ? $"✓ {res.Message}" : $"⚠️ {res.Message}";

            if (res.Success)
            {
                RestoreResultTitle = _pendingRestoreType == "SYSTEM" ? "FULL RESTORE VERIFIED" : "RESTORE SUCCESSFUL";
                RestoreResultIcon = "\uE73E";
                RestoreResultBrush = "#10B981";
                RestoreResultBackupName = string.IsNullOrEmpty(res.BackupName) ? _pendingRestoreTarget : res.BackupName;
                RestoreResultCategory = string.IsNullOrEmpty(res.Category) ? _pendingRestoreType : res.Category;
                RestoreResultItemsRestored = $"{res.RestoredCount} Item(s) Restored & Verified";
                RestoreResultVerification = res.VerificationResult;
                RestoreResultExecutionTime = $"{res.ElapsedSeconds:F2}s";
                RestoreResultError = "";
            }
            else if (res.IsPartial)
            {
                RestoreResultTitle = "PARTIAL RESTORE";
                RestoreResultIcon = "\uE7BA";
                RestoreResultBrush = "#F59E0B";
                RestoreResultBackupName = string.IsNullOrEmpty(res.BackupName) ? _pendingRestoreTarget : res.BackupName;
                RestoreResultCategory = string.IsNullOrEmpty(res.Category) ? _pendingRestoreType : res.Category;
                RestoreResultItemsRestored = $"{res.RestoredCount} Restored, {res.FailedCount} Failed";
                RestoreResultVerification = res.VerificationResult;
                RestoreResultExecutionTime = $"{res.ElapsedSeconds:F2}s";
                RestoreResultError = string.Join("\n", res.ErrorLogs);
            }
            else
            {
                RestoreResultTitle = "RESTORE FAILED";
                RestoreResultIcon = "\uE711";
                RestoreResultBrush = "#EF4444";
                RestoreResultBackupName = string.IsNullOrEmpty(res.BackupName) ? _pendingRestoreTarget : res.BackupName;
                RestoreResultCategory = string.IsNullOrEmpty(res.Category) ? _pendingRestoreType : res.Category;
                RestoreResultItemsRestored = "0 Items Restored";
                RestoreResultVerification = res.VerificationResult;
                RestoreResultExecutionTime = $"{res.ElapsedSeconds:F2}s";
                RestoreResultError = res.ErrorLogs.Count > 0 ? string.Join("\n", res.ErrorLogs) : res.Message;
            }

            RestoreResultLogs.Clear();
            foreach (var item in res.RestoredItems)
            {
                RestoreResultLogs.Add($"[RESTORED & VERIFIED] {item}");
            }
            foreach (var err in res.ErrorLogs)
            {
                RestoreResultLogs.Add($"[FAILED] {err}");
            }
            if (RestoreResultLogs.Count == 0)
            {
                RestoreResultLogs.Add(res.Message);
            }

            IsRestoreResultModalOpen = true;

            await LoadBackupsAsync();
        }

        private async Task CreateFullSnapshotAsync()
        {
            var result = await OptimizationProgressService.Instance.ExecuteProtectionAwareRestorePointFlowAsync(
                defaultName: "Error Optimizer V3 - Before Optimization",
                onStatusUpdate: s => Status = s
            );

            if (result != null)
            {
                Status = result.Success
                    ? $"✓ Windows System Restore Point verified (Sequence #{result.SequenceNumber})."
                    : $"⚠️ {result.ErrorMessage ?? result.StatusMessage}";
            }

            await LoadBackupsAsync();
        }

        private void DeleteTransaction(BackupTransaction? tx)
        {
            if (tx == null) return;
            _backupManager.DeleteTransaction(tx.TransactionId);
            _ = LoadBackupsAsync();
        }
    }
}
