using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using BiosOptimizer.GUI.Services;

namespace BiosOptimizer.GUI.Controls
{
    /// <summary>
    /// Master High-Performance 3D Button Interaction & Dynamic Theme-Aware Glow Behavior.
    /// Manages dynamic theme-accent glow synchronization and elevation without rasterizing or softening button content.
    /// Strictly preserves semantic status colors (Red/Green) for danger/success buttons.
    /// </summary>
    public static class Premium3DButtonBehavior
    {
        private static readonly ConditionalWeakTable<Button, object> _trackedButtons = new();
        private static bool _themeHooked = false;

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(Premium3DButtonBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        private static void EnsureThemeHook()
        {
            if (!_themeHooked)
            {
                ThemeBrushService.Instance.AccentColorChanged += OnGlobalAccentColorChanged;
                _themeHooked = true;
            }
        }

        private static void OnGlobalAccentColorChanged(Color newColor)
        {
            foreach (var kvp in _trackedButtons)
            {
                if (kvp.Key is Button btn && btn.IsLoaded)
                {
                    UpdateGlowColor(btn, newColor);
                }
            }
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Button btn)
            {
                EnsureThemeHook();

                if ((bool)e.NewValue)
                {
                    _trackedButtons.AddOrUpdate(btn, true);
                    btn.Loaded += Btn_Loaded;
                    UpdateGlowColor(btn, ThemeBrushService.Instance.CurrentAccentColor);
                }
                else
                {
                    _trackedButtons.Remove(btn);
                    btn.Loaded -= Btn_Loaded;
                }
            }
        }

        private static void Btn_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                UpdateGlowColor(btn, ThemeBrushService.Instance.CurrentAccentColor);
            }
        }

        private static void UpdateGlowColor(Button btn, Color accentColor)
        {
            try
            {
                var shadowBorder = btn.Template?.FindName("BtnShadow", btn) as Border 
                                ?? btn.Template?.FindName("Shadow", btn) as Border;

                if (shadowBorder?.Effect is DropShadowEffect dropShadow)
                {
                    // Check if it's a Danger button (Keep Danger buttons Red #EF4444 / #DC2626)
                    if ((dropShadow.Color.R == 0xEF && dropShadow.Color.G == 0x44 && dropShadow.Color.B == 0x44) ||
                        (dropShadow.Color.R == 0xDC && dropShadow.Color.G == 0x26 && dropShadow.Color.B == 0x26) ||
                        (dropShadow.Color.R == 0x10 && dropShadow.Color.G == 0xB9 && dropShadow.Color.B == 0x81))
                    {
                        return; // Preserve semantic status glow
                    }

                    dropShadow.Color = accentColor;
                }
            }
            catch { }
        }
    }
}
