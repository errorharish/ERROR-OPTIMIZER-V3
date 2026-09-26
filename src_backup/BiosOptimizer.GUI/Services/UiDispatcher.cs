using System;
using System.Threading.Tasks;
using System.Windows;

namespace BiosOptimizer.GUI.Services
{
    public class UiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) { action(); return; }

            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        public async Task InvokeAsync(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) { action(); return; }

            if (dispatcher.CheckAccess())
                action();
            else
                await dispatcher.InvokeAsync(action);
        }

        public async Task<T> InvokeAsync<T>(Func<T> action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return action();

            if (dispatcher.CheckAccess())
                return action();
            else
                return await dispatcher.InvokeAsync(action);
        }

        public bool CheckAccess()
        {
            return Application.Current?.Dispatcher?.CheckAccess() ?? true;
        }

        // Keep static methods for compatibility
        public static void Run(Action action) => new UiDispatcher().Invoke(action);
        public static Task RunAsync(Action action) => new UiDispatcher().InvokeAsync(action);
    }
}
