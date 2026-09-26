using System;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI.Controls
{
    public partial class UniversalOptimizationModal : UserControl
    {
        public UniversalOptimizationModal()
        {
            InitializeComponent();
            DataContext = OptimizationProgressService.Instance;

            Loaded += (s, e) =>
            {
                try
                {
                    if (Resources["PulseHaloStoryboard"] is Storyboard pulseSb)
                    {
                        pulseSb.Begin(this, true);
                    }
                }
                catch { }
            };
        }
    }
}
