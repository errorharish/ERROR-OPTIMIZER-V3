#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// 3D Glassmorphic Dropdown & ComboBox for Error Optimizer V3.
    /// Features smooth curved corners, rotating animated chevron arrow,
    /// floating glass panel, and staggered option presentation.
    /// </summary>
    public class PremiumDropdown : ComboBox
    {
        static PremiumDropdown()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(PremiumDropdown),
                new FrameworkPropertyMetadata(typeof(PremiumDropdown)));
        }
    }
}
