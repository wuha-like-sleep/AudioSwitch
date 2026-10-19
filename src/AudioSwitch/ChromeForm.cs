using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace AudioSwitch;

internal class ChromeForm : Form
{
    private readonly Label caption = new();
    public ChromeForm()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 40, ColumnCount = 4, RowCount = 1,
            BackColor = Color.FromArgb(243, 246, 251), Margin = Padding.Empty
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        using var icon = BrandIcon.Load();
        var mark = new PictureBox { Image = icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(18, 18), Margin = new Padding(14, 11, 4, 0) };
        caption.Dock = DockStyle.Fill; caption.TextAlign = ContentAlignment.MiddleLeft;
        caption.Font = new Font("Microsoft YaHei UI", 9); caption.ForeColor = Theme.Muted;
        bar.Controls.Add(mark, 0, 0); bar.Controls.Add(caption, 1, 0);
        bar.Controls.Add(new CaptionButton(false, () => WindowState = FormWindowState.Minimized), 2, 0);
        bar.Controls.Add(new CaptionButton(true, Close), 3, 0);
        bar.MouseDown += DragWindow; caption.MouseDown += DragWindow; mark.MouseDown += DragWindow;
        Controls.Add(bar);
    }
    protected override CreateParams CreateParams
    {
        get { var parameters = base.CreateParams; parameters.ClassStyle |= 0x00020000; return parameters; }
    }
    protected override void OnShown(EventArgs e)
    {
        var area = Screen.FromControl(this).WorkingArea;
        int margin = Math.Max(4, (int)Math.Round(8 * DeviceDpi / 96f));
        var limit = new Size(Math.Max(1, area.Width - margin * 2), Math.Max(1, area.Height - margin * 2));
        MinimumSize = new Size(Math.Min(MinimumSize.Width, limit.Width), Math.Min(MinimumSize.Height, limit.Height));
        Size = new Size(Math.Min(Width, limit.Width), Math.Min(Height, limit.Height));
        Location = new Point(Math.Clamp(Left, area.Left + margin, area.Right - Width - margin), Math.Clamp(Top, area.Top + margin, area.Bottom - Height - margin));
        base.OnShown(e);
    }
    protected override void OnTextChanged(EventArgs e)
    {
        if (caption != null) caption.Text = Text;
        base.OnTextChanged(e);
    }
    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture(); SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowSurface.Apply(Handle);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Color.FromArgb(225, 230, 238));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}

internal sealed class CaptionButton(bool close, Action action) : Control
{
    private bool hover;
    protected override Size DefaultSize => new(44, 40);
    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Dock = DockStyle.Fill; Margin = Padding.Empty; TabStop = false;
        AccessibleRole = AccessibleRole.PushButton; AccessibleName = close ? "关闭设置窗口" : "最小化设置窗口";
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { action(); base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { action(); e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(hover ? (close ? Color.FromArgb(225, 64, 71) : Color.FromArgb(227, 232, 242)) : Color.FromArgb(243, 246, 251));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(hover && close ? Color.White : Theme.Muted, 1.3f * DeviceDpi / 96f);
        float cx = Width / 2f, cy = Height / 2f, r = 4 * DeviceDpi / 96f;
        if (close)
        {
            e.Graphics.DrawLine(pen, cx - r, cy - r, cx + r, cy + r);
            e.Graphics.DrawLine(pen, cx + r, cy - r, cx - r, cy + r);
        }
        else e.Graphics.DrawLine(pen, cx - r, cy, cx + r, cy);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
    }
}
