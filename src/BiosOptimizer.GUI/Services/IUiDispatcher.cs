using System;
using System.Threading.Tasks;

namespace BiosOptimizer.GUI.Services
{
    public interface IUiDispatcher
    {
        void Invoke(Action action);
        Task InvokeAsync(Action action);
        Task<T> InvokeAsync<T>(Func<T> action);
        bool CheckAccess();
    }
}
