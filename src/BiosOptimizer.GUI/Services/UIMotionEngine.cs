#nullable enable
using System;
using System.Windows.Media.Animation;

namespace BiosOptimizer.GUI.Services
{
    /// <summary>
    /// Legacy compatibility bridge and alias for UI3DMotionEngine.
    /// Redirects all calls to the central UI3DMotionEngine singleton.
    /// </summary>
    public sealed class UIMotionEngine
    {
        public static UI3DMotionEngine Instance => UI3DMotionEngine.Instance;
    }
}
