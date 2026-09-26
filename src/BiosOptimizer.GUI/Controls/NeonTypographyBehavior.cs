#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BiosOptimizer.GUI.Controls
{
    public enum NeonGlowLevel
    {
        None,
        Subtle,
        CardTitle,
        Subheading,
        Value,
        Heading
    }

    /// <summary>
    /// Master Global Neon Typography Behavior for Error Optimizer V3.
    /// Guarantees 100% razor-sharp ClearType text rendering with zero pixel blur or softening.
    /// </summary>
    public static class NeonTypographyBehavior
    {
        public static readonly DependencyProperty LevelProperty =
            DependencyProperty.RegisterAttached(
                "Level",
                typeof(NeonGlowLevel),
                typeof(NeonTypographyBehavior),
                new PropertyMetadata(NeonGlowLevel.None, OnLevelChanged));

        public static NeonGlowLevel GetLevel(DependencyObject obj)
        {
            return (NeonGlowLevel)obj.GetValue(LevelProperty);
        }

        public static void SetLevel(DependencyObject obj, NeonGlowLevel value)
        {
            obj.SetValue(LevelProperty, value);
        }

        public static readonly DependencyProperty IsInteractiveProperty =
            DependencyProperty.RegisterAttached(
                "IsInteractive",
                typeof(bool),
                typeof(NeonTypographyBehavior),
                new PropertyMetadata(false));

        public static bool GetIsInteractive(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsInteractiveProperty);
        }

        public static void SetIsInteractive(DependencyObject obj, bool value)
        {
            obj.SetValue(IsInteractiveProperty, value);
        }

        private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement fe)
            {
                TextOptions.SetTextFormattingMode(fe, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(fe, TextRenderingMode.ClearType);
                RenderOptions.SetClearTypeHint(fe, ClearTypeHint.Enabled);
                fe.SnapsToDevicePixels = true;
                fe.Effect = null; // Strict rule: Text itself is NEVER blurred
            }
        }
    }
}
