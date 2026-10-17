namespace AudioSwitch;
internal sealed record MonitorInfo(string Id, bool Primary, Rectangle WorkArea);
internal static class ToastPlacement
{
    public static MonitorInfo Select(string? preferred, IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) throw new InvalidOperationException("没有可用显示器。");
        return monitors.FirstOrDefault(m => !string.IsNullOrEmpty(preferred) && m.Id == preferred)
            ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
    }
    public static MonitorInfo Resolve(string? preferred) => Select(preferred,
        Screen.AllScreens.Select(s => new MonitorInfo(s.DeviceName, s.Primary, s.WorkingArea)).ToList());
    public static Point Position(Rectangle area, Size size, int margin)
    {
        int mx = Math.Min(margin, Math.Max(0, (area.Width - size.Width) / 2));
        int my = Math.Min(margin, Math.Max(0, (area.Height - size.Height) / 2));
        return new Point(
            Math.Clamp(area.Right - size.Width - mx, area.Left, Math.Max(area.Left, area.Right - size.Width)),
            Math.Clamp(area.Bottom - size.Height - my, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
    }
}
