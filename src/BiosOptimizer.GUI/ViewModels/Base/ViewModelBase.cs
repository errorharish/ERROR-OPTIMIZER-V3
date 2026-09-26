using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace BiosOptimizer.GUI.ViewModels.Base
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public virtual Task OnNavigatedToAsync() { return Task.CompletedTask; }
        public virtual Task OnNavigatedFromAsync() { return Task.CompletedTask; }

        protected static System.Windows.Media.SolidColorBrush GetFrozenBrush(System.Windows.Media.Color color)
        {
            var brush = new System.Windows.Media.SolidColorBrush(color);
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
    }
}
