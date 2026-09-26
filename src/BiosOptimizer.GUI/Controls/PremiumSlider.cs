#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Master Universal Single-Thumb Circular Slider for Error Optimizer V3.
    /// Guarantees a single 18px circular thumb with active accent fill,
    /// glow halo, and smooth drag scaling.
    /// </summary>
    public class PremiumSlider : Slider
    {
        static PremiumSlider()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(PremiumSlider),
                new FrameworkPropertyMetadata(typeof(PremiumSlider)));
        }
    }
}
