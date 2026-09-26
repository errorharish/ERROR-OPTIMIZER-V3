#nullable enable
using System;

namespace BiosOptimizer.GUI.Services
{
    public class OptimizationStateChangedEventArgs
    {
        public string OptimizationId { get; set; } = "";
        public string CategoryKey { get; set; } = "";
        public string Current { get; set; } = "";
        public string Target { get; set; } = "";
        public string Status { get; set; } = "Verified"; // Verified, Applied, Failed, Pending
        public bool Verified { get; set; } = true;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Central event bus for cross-module optimization state synchronization.
    /// Whenever an optimization is applied or reverted in any module, this coordinator
    /// notifies all subscribing ViewModels and controls to synchronize state and invalidate caches.
    /// </summary>
    public static class OptimizationStateCoordinator
    {
        public static event Action? OptimizationStateChanged;
        public static event Action<OptimizationStateChangedEventArgs>? DetailedStateChanged;

        public static void NotifyOptimizationStateChanged()
        {
            try
            {
                OptimizationStateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OptimizationStateCoordinator] OptimizationStateChanged error: {ex.Message}");
            }

            try
            {
                UiDispatcher.Run(() => System.Windows.Input.CommandManager.InvalidateRequerySuggested());
            }
            catch { }
        }

        public static void NotifyOptimizationStateChanged(OptimizationStateChangedEventArgs args)
        {
            try
            {
                DetailedStateChanged?.Invoke(args);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OptimizationStateCoordinator] DetailedStateChanged error: {ex.Message}");
            }

            try
            {
                OptimizationStateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OptimizationStateCoordinator] OptimizationStateChanged error: {ex.Message}");
            }

            try
            {
                UiDispatcher.Run(() => System.Windows.Input.CommandManager.InvalidateRequerySuggested());
            }
            catch { }
        }
    }
}
