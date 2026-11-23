using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace EzCompany.Core.Telemetry;

/// <summary>
/// Safe Win32 activity interop (polling only — zero global keyboard/mouse hooks, zero screen-capture APIs).
/// </summary>
internal static class Win32ActivityInterop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    public static double GetIdleSeconds()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref lii))
        {
            return 0;
        }

        uint tickCount = unchecked((uint)Environment.TickCount);
        uint elapsedMs = unchecked(tickCount - lii.dwTime);
        return elapsedMs / 1000.0;
    }

    public static bool TryGetForegroundWindowInfo(out string processName, out string windowTitle)
    {
        processName = string.Empty;
        windowTitle = string.Empty;

        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var sb = new StringBuilder(512);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        windowTitle = sb.ToString();

        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != 0)
        {
            try
            {
                using Process proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch
            {
                processName = $"pid:{pid}";
            }
        }

        return !string.IsNullOrWhiteSpace(processName) || !string.IsNullOrWhiteSpace(windowTitle);
    }
}
