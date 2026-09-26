using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace BiosOptimizer.GUI.Services
{
    public class UiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action)
        {
            if (action == null) return;

            var app = Application.Current;
            var dispatcher = app?.Dispatcher;

            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            try
            {
                dispatcher.Invoke(action, DispatcherPriority.Normal, CancellationToken.None, TimeSpan.FromMilliseconds(250));
            }
            catch
            {
                // Fallback to direct execution on timeout or if dispatcher queue is not pumping
                action();
            }
        }

        public async Task InvokeAsync(Action action)
        {
            if (action == null) return;

            var app = Application.Current;
            var dispatcher = app?.Dispatcher;

            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            try
            {
                var op = dispatcher.InvokeAsync(action, DispatcherPriority.Normal);
                var task = op.Task;
                var completed = await Task.WhenAny(task, Task.Delay(250));
                if (completed != task)
                {
                    action();
                }
            }
            catch
            {
                action();
            }
        }

        public async Task<T> InvokeAsync<T>(Func<T> action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            var app = Application.Current;
            var dispatcher = app?.Dispatcher;

            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
            {
                return action();
            }

            try
            {
                var op = dispatcher.InvokeAsync(action, DispatcherPriority.Normal);
                var task = op.Task;
                var completed = await Task.WhenAny(task, Task.Delay(250));
                if (completed == task)
                {
                    return await task;
                }
                else
                {
                    return action();
                }
            }
            catch
            {
                return action();
            }
        }

        public bool CheckAccess()
        {
            return Application.Current?.Dispatcher?.CheckAccess() ?? true;
        }

        // Static compatibility methods
        public static void Run(Action action) => new UiDispatcher().Invoke(action);
        public static Task RunAsync(Action action) => new UiDispatcher().InvokeAsync(action);
    }
}
