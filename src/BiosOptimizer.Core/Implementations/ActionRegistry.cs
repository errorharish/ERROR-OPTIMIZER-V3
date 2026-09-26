using BiosOptimizer.Core.Interfaces;

namespace BiosOptimizer.Core.Implementations;

public class ActionRegistry : IActionRegistry
{
    private readonly Dictionary<string, IActionHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public IActionHandler? GetHandler(string actionName)
    {
        _handlers.TryGetValue(actionName, out var handler);
        return handler;
    }

    public void RegisterHandler(IActionHandler handler)
    {
        _handlers[handler.ActionName] = handler;
    }
}
