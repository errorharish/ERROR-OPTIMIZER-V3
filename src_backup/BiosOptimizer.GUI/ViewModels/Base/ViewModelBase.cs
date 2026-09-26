using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BiosOptimizer.GUI.ViewModels.Base
{
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected static System.Windows.Media.SolidColorBrush GetFrozenBrush(System.Windows.Media.Color color)
        {
            var brush = new System.Windows.Media.SolidColorBrush(color);
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
    }
}
