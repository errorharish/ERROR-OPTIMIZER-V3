#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Centralized 3D Glassmorphic Card Container for Error Optimizer V3.
    /// Inherits Card3D to provide full spatial tilt, dynamic spotlight tracking,
    /// dynamic glass opacity, and 3D elevation.
    /// </summary>
    public class GlassCard3D : Card3D
    {
        static GlassCard3D()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(GlassCard3D),
                new FrameworkPropertyMetadata(typeof(GlassCard3D)));
        }
    }
}
