#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BiosOptimizer.GUI.Tools
{
    public static class SmoothScrollBehavior
    {
        public static readonly DependencyProperty IsSmoothScrollEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsSmoothScrollEnabled",
                typeof(bool),
                typeof(SmoothScrollBehavior),
                new PropertyMetadata(false, OnIsSmoothScrollEnabledChanged));

        public static bool GetIsSmoothScrollEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsSmoothScrollEnabledProperty);
        }

        public static void SetIsSmoothScrollEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsSmoothScrollEnabledProperty, value);
        }

        private static readonly Dictionary<ScrollViewer, ScrollData> activeScrolls = new();

        private class ScrollData
        {
            public double TargetOffset;
            public double CurrentOffset;
            public double Velocity;
        }

        private static void OnIsSmoothScrollEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                if ((bool)e.NewValue)
                {
                    scrollViewer.PreviewMouseWheel += ScrollViewer_PreviewMouseWheel;
                }
                else
                {
                    scrollViewer.PreviewMouseWheel -= ScrollViewer_PreviewMouseWheel;
                    activeScrolls.Remove(scrollViewer);
                }
            }
        }

        private static void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer) return;

            e.Handled = true;

            if (!activeScrolls.TryGetValue(scrollViewer, out var data))
            {
                data = new ScrollData
                {
                    CurrentOffset = scrollViewer.VerticalOffset,
                    TargetOffset = scrollViewer.VerticalOffset
                };
                activeScrolls[scrollViewer] = data;
            }

            double scrollDelta = -e.Delta * 0.8;
            data.TargetOffset = Math.Clamp(data.TargetOffset + scrollDelta, 0, scrollViewer.ScrollableHeight);

            StartAnimationLoop();
        }

        private static bool isRendering;

        private static void StartAnimationLoop()
        {
            if (!isRendering)
            {
                CompositionTarget.Rendering += CompositionTarget_Rendering;
                isRendering = true;
            }
        }

        private static void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            var toRemove = new List<ScrollViewer>();

            foreach (var kvp in activeScrolls)
            {
                var sv = kvp.Key;
                var data = kvp.Value;

                if (!sv.IsLoaded)
                {
                    toRemove.Add(sv);
                    continue;
                }

                double diff = data.TargetOffset - sv.VerticalOffset;

                if (Math.Abs(diff) < 0.5)
                {
                    sv.ScrollToVerticalOffset(data.TargetOffset);
                    toRemove.Add(sv);
                }
                else
                {
                    double step = diff * 0.25;
                    sv.ScrollToVerticalOffset(sv.VerticalOffset + step);
                }
            }

            foreach (var sv in toRemove)
            {
                activeScrolls.Remove(sv);
            }

            if (activeScrolls.Count == 0)
            {
                CompositionTarget.Rendering -= CompositionTarget_Rendering;
                isRendering = false;
            }
        }

        private static ScrollViewer? GetScrollViewerUnderMouse()
        {
            try
            {
                var element = Mouse.DirectlyOver as DependencyObject;
                while (element != null)
                {
                    if (element is ScrollViewer sv && sv.ComputedVerticalScrollBarVisibility == Visibility.Visible)
                    {
                        return sv;
                    }
                    if (element is FrameworkContentElement fce)
                    {
                        element = fce.Parent;
                    }
                    else if (element is Visual || element is System.Windows.Media.Media3D.Visual3D)
                    {
                        element = VisualTreeHelper.GetParent(element);
                    }
                    else
                    {
                        element = LogicalTreeHelper.GetParent(element);
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
