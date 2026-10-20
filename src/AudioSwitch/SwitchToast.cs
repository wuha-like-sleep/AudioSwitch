
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AudioSwitch;
// Non-activating, click-through desktop overlay. Exclusive fullscreen rendering may cover it.
internal sealed class SwitchToast : Form
{
    private readonly string preferredMonitor;
    private readonly string corner;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2200 };
    private readonly System.Windows.Forms.Timer motion = new() { Interval = 15 };
    private readonly Stopwatch elapsed = new();
    private Point settled, edge;
    private bool leaving;
    private bool animate;
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ClassStyle |= 0x00020000; p.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020; return p; }
    }
    public SwitchToast(string label, string name, string preferredMonitor = "", int duration = 2200, string corner = "bottom-right")
    {
        this.preferredMonitor = preferredMonitor;
        this.corner = corner;
        timer.Interval = Math.Clamp(duration, 1000, 30000);
        Text = "AudioSwitch · 切换提示";
        StartPosition = FormStartPosition.Manual;
        Location = ToastPlacement.Resolve(preferredMonitor).WorkArea.Location;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Microsoft YaHei UI", 10);
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        BackColor = Color.White; ForeColor = Theme.Ink;
        ClientSize = new Size(392, 100); Padding = new Padding(20, 18, 20, 16);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        using var icon = BrandIcon.Load();
        var mark = new PictureBox { Image = icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(32, 32), Margin = new Padding(0, 8, 10, 0) };
        body.Controls.Add(mark, 0, 0); body.SetRowSpan(mark, 2);
        body.Controls.Add(new Label { Text = label is "耳机" or "音箱" ? "已切换到" + label : label, Font = new Font(Font.FontFamily, 12, FontStyle.Bold), Dock = DockStyle.Fill }, 1, 0);
        body.Controls.Add(new Label { Text = name, ForeColor = Theme.Muted, Dock = DockStyle.Fill, AutoEllipsis = true }, 1, 1);
        Controls.Add(body);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!animate) { Close(); return; }
            leaving = true;
            elapsed.Restart();
            motion.Start();
        };
        motion.Tick += (_, _) => AdvanceMotion();
        Shown += (_, _) =>
        {
            if (animate)
            {
                elapsed.Restart();
                motion.Start();
            }
            else timer.Start();
        };
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PlaceOnSelectedMonitor();
        animate = !SystemInformation.TerminalServerSession
            && SystemParametersInfo(0x1042, 0, out bool enabled, 0) && enabled;
        if (animate) Location = edge;
    }
    private void PlaceOnSelectedMonitor()
    {
        var area = ToastPlacement.Resolve(preferredMonitor).WorkArea;
        if (Width > area.Width) Width = area.Width;
        if (Height > area.Height) Height = area.Height;
        settled = ToastPlacement.Position(area, Size, (int)Math.Round(24 * DeviceDpi / 96f), corner);
        // Stay inside this display: sliding from outside can flash on a neighbouring monitor.
        edge = new Point(corner is "top-left" or "bottom-left" ? area.Left : area.Right - Width, settled.Y);
        Location = settled;
        if (motion.Enabled) { motion.Stop(); if (leaving) Close(); else timer.Start(); }
    }
    private void AdvanceMotion()
    {
        double progress = Math.Clamp(elapsed.Elapsed.TotalMilliseconds / (leaving ? 180 : 260), 0, 1);
        double eased = leaving ? progress * progress * progress : 1 - Math.Pow(1 - progress, 3);
        Point from = leaving ? settled : edge, to = leaving ? edge : settled;
        Location = new Point((int)Math.Round(from.X + (to.X - from.X) * eased), settled.Y);
        if (progress < 1) return;
        motion.Stop();
        if (leaving) Close(); else timer.Start(); // Full configured dwell time after entry.
    }
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); WindowSurface.Apply(Handle); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Color.FromArgb(226, 231, 240));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); PlaceOnSelectedMonitor(); }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
        if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; } // HTTRANSPARENT
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); motion.Dispose(); } base.Dispose(disposing); }
}
