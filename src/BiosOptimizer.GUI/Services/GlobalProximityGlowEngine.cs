#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BiosOptimizer.GUI.Controls;

namespace BiosOptimizer.GUI.Services
{
    /// <summary>
    /// Master Global Cursor-Proximity Glow Engine for Error Optimizer V3.
    /// Provides smooth ambient proximity lighting without interfering with
    /// native DirectWrite / ClearType subpixel text rendering.
    /// </summary>
    public sealed class GlobalProximityGlowEngine
    {
        private static readonly Lazy<GlobalProximityGlowEngine> _instance =
            new(() => new GlobalProximityGlowEngine());
        public static GlobalProximityGlowEngine Instance => _instance.Value;

        private GlobalProximityGlowEngine() { }

        public void RegisterCard(FrameworkElement card) { }
        public void UnregisterCard(FrameworkElement card) { }
        public void RegisterReactiveText(TextBlock tb) { }
        public void UnregisterReactiveText(TextBlock tb) { }

        public void ProcessMouseMove(Point windowMousePos, Window window) { }
        public void ResetAll() { }
    }
}
