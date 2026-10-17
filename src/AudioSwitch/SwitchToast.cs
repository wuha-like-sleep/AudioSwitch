using System.Runtime.InteropServices;

namespace AudioSwitch;
// Non-activating, click-through desktop overlay. Exclusive fullscreen rendering may cover it.
internal sealed class SwitchToast : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2200 };
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020; return p; }
    }
    public SwitchToast(string label, string name)
    {
        AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Microsoft YaHei UI", 10);
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        BackColor = Color.FromArgb(24, 35, 55); ForeColor = Color.White;
        Opacity = 0.97;
        ClientSize = new Size(360, 98); Padding = new Padding(20, 16, 20, 16);
        var title = new Label { Text = "已切换到" + label, Font = new Font(Font.FontFamily, 13, FontStyle.Bold), Dock = DockStyle.Top, Height = 32 };
        var device = new Label { Text = name, ForeColor = Color.FromArgb(194, 204, 225), Dock = DockStyle.Fill, AutoEllipsis = true };
        Controls.Add(device); Controls.Add(title);
        timer.Tick += (_, _) => Close();
        Shown += (_, _) =>
        {
            var area = Screen.FromHandle(GetForegroundWindow()).WorkingArea;
            Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);
            timer.Start();
        };
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
        if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; } // HTTRANSPARENT
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
