using System;
using System.Runtime.InteropServices;

namespace BiosOptimizer.Core.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct FILTERKEYS
    {
        public uint cbSize;
        public uint dwFlags;
        public uint iWaitMSec;
        public uint iDelayMSec;
        public uint iRepeatMSec;
        public uint iBounceMSec;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STICKYKEYS
    {
        public uint cbSize;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOGGLEKEYS
    {
        public uint cbSize;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEKEYS
    {
        public uint cbSize;
        public uint dwFlags;
        public uint iMaxSpeed;
        public uint iTimeToMaxSpeed;
        public uint iCtrlSpeed;
        public uint dwReserved1;
        public uint dwReserved2;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIGHCONTRAST
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr lpszDefaultScheme;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SOUNDSENTRY
    {
        public uint cbSize;
        public uint dwFlags;
        public uint iFSTextEffect;
        public uint iFSTextEffectMSec;
        public uint iFSTextEffectColorBits;
        public uint iFSGrafEffect;
        public uint iFSGrafEffectMSec;
        public uint iFSGrafEffectColor;
        public uint iWindowsEffect;
        public uint iWindowsEffectMSec;
        public IntPtr lpszWindowsEffectDLL;
        public uint iWindowsEffectOrdinal;
    }

    public static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref FILTERKEYS pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref STICKYKEYS pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref TOGGLEKEYS pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref MOUSEKEYS pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref HIGHCONTRAST pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref SOUNDSENTRY pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, uint pvParam, uint fWinIni);

        [DllImport("gdi32.dll")]
        public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        public const uint SPI_GETFILTERKEYS = 0x0032;
        public const uint SPI_SETFILTERKEYS = 0x0033;
        public const uint SPI_GETSTICKYKEYS = 0x003A;
        public const uint SPI_SETSTICKYKEYS = 0x003B;
        public const uint SPI_GETTOGGLEKEYS = 0x0034;
        public const uint SPI_SETTOGGLEKEYS = 0x0035;
        public const uint SPI_GETMOUSEKEYS = 0x0036;
        public const uint SPI_SETMOUSEKEYS = 0x0037;
        public const uint SPI_GETHIGHCONTRAST = 0x0042;
        public const uint SPI_SETHIGHCONTRAST = 0x0043;
        public const uint SPI_GETSOUNDSENTRY = 0x0040;
        public const uint SPI_SETSOUNDSENTRY = 0x0041;
        public const uint SPI_GETMOUSE = 0x0003;
        public const uint SPI_SETMOUSE = 0x0004;
        public const uint SPI_GETMOUSETRAILS = 0x005E;
        public const uint SPI_SETMOUSETRAILS = 0x005D;
        public const uint SPI_GETKEYBOARDDELAY = 0x0016;
        public const uint SPI_SETKEYBOARDDELAY = 0x0017;
        public const uint SPI_GETKEYBOARDSPEED = 0x000A;
        public const uint SPI_SETKEYBOARDSPEED = 0x000B;

        public const uint SPIF_UPDATEINIFILE = 0x01;
        public const uint SPIF_SENDCHANGE = 0x02;

        public const int LOGPIXELSX = 88;
        public const int LOGPIXELSY = 90;
    }
}
