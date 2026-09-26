using System;
using System.Windows.Input;

namespace BiosOptimizer.GUI.ViewModels.Base
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        private event EventHandler _canExecuteChangedInternal;

        public event EventHandler CanExecuteChanged
        {
            add 
            { 
                CommandManager.RequerySuggested += value; 
                _canExecuteChangedInternal += value;
            }
            remove 
            { 
                CommandManager.RequerySuggested -= value; 
                _canExecuteChangedInternal -= value;
            }
        }

        public void RaiseCanExecuteChanged()
        {
            _canExecuteChangedInternal?.Invoke(this, EventArgs.Empty);
            CommandManager.InvalidateRequerySuggested();
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }
    }
}
