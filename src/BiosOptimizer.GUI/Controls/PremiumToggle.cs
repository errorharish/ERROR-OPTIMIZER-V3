#nullable enable
using System;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Smooth Pill-Track Animated Toggle Switch for Error Optimizer V3.
    /// Replaces basic checkboxes with an active accent pill and circular sliding thumb.
    /// </summary>
    public class PremiumToggle : ToggleButton
    {
        static PremiumToggle()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(PremiumToggle),
                new FrameworkPropertyMetadata(typeof(PremiumToggle)));
        }
    }
}
