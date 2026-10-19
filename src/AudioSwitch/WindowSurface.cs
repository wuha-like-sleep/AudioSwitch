using System.Runtime.InteropServices;

namespace AudioSwitch;

// Let the desktop compositor draw the outer edge; a window Region cuts it into
// hard pixels and prevents Windows 11 from supplying its own rounded corners.
internal static class WindowSurface
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }
    internal static void Apply(IntPtr handle)
    {
        if (DwmIsCompositionEnabled(out var enabled) != 0 || !enabled) return;
        var policy = 2; // DWMNCRP_ENABLED
        _ = DwmSetWindowAttribute(handle, 2, ref policy, sizeof(int));
        var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
        _ = DwmExtendFrameIntoClientArea(handle, ref margins);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var corners = 2; // DWMWCP_ROUND; a hint, subject to system policy.
            _ = DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int));
        }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
