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
        BackColor = Color.White; ForeColor = Theme.Ink;
        Opacity = 0.97;
        ClientSize = new Size(380, 96); Padding = new Padding(18, 16, 18, 16);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        using var icon = BrandIcon.Load();
        var mark = new PictureBox { Image = icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(32, 32), Margin = new Padding(0, 8, 10, 0) };
        body.Controls.Add(mark, 0, 0); body.SetRowSpan(mark, 2);
        body.Controls.Add(new Label { Text = "已切换到" + label, Font = new Font(Font.FontFamily, 12, FontStyle.Bold), Dock = DockStyle.Fill }, 1, 0);
        body.Controls.Add(new Label { Text = name, ForeColor = Theme.Muted, Dock = DockStyle.Fill, AutoEllipsis = true }, 1, 1);
        Controls.Add(body);
        using var round = Theme.Round(new RectangleF(0, 0, Width, Height), 14);
        Region = new Region(round);
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
