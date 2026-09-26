using System;
using System.Runtime.InteropServices;

namespace BiosOptimizer.GUI.Services
{
    public class TimerResolutionManager
    {
        [DllImport("ntdll.dll", SetLastError = true)]
        static extern int NtQueryTimerResolution(out uint MaximumTime, out uint MinimumTime, out uint CurrentTime);

        [DllImport("ntdll.dll", SetLastError = true)]
        static extern int NtSetTimerResolution(uint DesiredTime, bool SetResolution, out uint CurrentTime);

        public double GetCurrentResolutionMs()
        {
            NtQueryTimerResolution(out _, out _, out uint current);
            return current / 10000.0;
        }

        public void RequestHighResolution()
        {
            // Request 0.5ms (5000 100-ns units)
            NtSetTimerResolution(5000, true, out _);
        }

        public void ReleaseHighResolution()
        {
            NtSetTimerResolution(0, false, out _);
        }
    }
}
