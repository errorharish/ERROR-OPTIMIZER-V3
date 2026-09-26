using System.Windows.Input;
using BiosOptimizer.GUI.ViewModels.Base;

namespace BiosOptimizer.GUI.ViewModels
{
    public class ErrorViewModel : ViewModelBase
    {
        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        private string _stackTrace = string.Empty;
        public string StackTrace
        {
            get => _stackTrace;
            set { _stackTrace = value; OnPropertyChanged(); }
        }

        public ICommand RetryCommand { get; }

        public ErrorViewModel(string error, string stack, System.Action retryAction)
        {
            ErrorMessage = error;
            StackTrace = stack;
            RetryCommand = new RelayCommand(_ => retryAction?.Invoke());
        }
    }
}
