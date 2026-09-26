using System.Windows.Controls;

namespace BiosOptimizer.GUI.Views;

public partial class ProcessReductionView : UserControl
{
    public ProcessReductionView()
    {
        InitializeComponent();
        this.Loaded += (s, e) => System.Diagnostics.Debug.WriteLine("[PROCESS REDUCTION] View loaded");
        this.DataContextChanged += (s, e) => System.Diagnostics.Debug.WriteLine("[PROCESS REDUCTION] DataContext assigned");
    }
}
