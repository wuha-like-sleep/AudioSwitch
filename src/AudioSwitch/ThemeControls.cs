using System.Drawing.Drawing2D;

namespace AudioSwitch;
internal static class Theme
{
    public static readonly Color Ink = Color.FromArgb(25, 35, 57);
    public static readonly Color Accent = Color.FromArgb(70, 92, 232);
    public static readonly Color Muted = Color.FromArgb(111, 122, 143);
    public static GraphicsPath Round(RectangleF bounds, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
}
internal sealed class RoundedPanel : Panel
{
    public Color SurfaceColor { get; set; } = Color.White;
    public RoundedPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        using var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 16 * scale);
        using var fill = new SolidBrush(SurfaceColor);
        e.Graphics.FillPath(fill, path);
        if (SurfaceColor == Color.White) { using var pen = new Pen(Color.FromArgb(229, 234, 244)); e.Graphics.DrawPath(pen, path); }
        base.OnPaint(e);
    }
}
internal sealed class ThemeButton : Control, IButtonControl
{
    public bool Primary { get; init; }
    public DialogResult DialogResult { get; set; }
    public void NotifyDefault(bool value) => Invalidate();
    public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }
    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (DialogResult != DialogResult.None && FindForm() is { } form) form.DialogResult = DialogResult;
    }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { PerformClick(); e.Handled = true; e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }
    private bool hover;
    public ThemeButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        TabStop = true; AccessibleRole = AccessibleRole.PushButton; BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Color.FromArgb(243, 246, 251));
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new RectangleF(1, 1, Width - 2, Height - 2), 10 * DeviceDpi / 96f);
        var color = Primary ? (hover ? Color.FromArgb(53, 72, 202) : Theme.Accent) : (hover ? Color.FromArgb(234, 239, 249) : Color.White);
        using var fill = new SolidBrush(color); e.Graphics.FillPath(fill, path);
        if (!Primary || Focused) { using var border = new Pen(Focused ? Theme.Accent : Color.FromArgb(221, 228, 241), Focused ? 2 : 1); e.Graphics.DrawPath(border, path); }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Primary ? Color.White : Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -6, -6));
    }
}
internal sealed class Toggle : Control
{
    private bool isChecked;
    public bool Checked { get => isChecked; set { isChecked = value; Invalidate(); } }
    public Toggle() { TabStop = true; AccessibleRole = AccessibleRole.CheckButton; AutoSize = false; Size = new Size(285, 32); Cursor = Cursors.Hand; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Color.White);
    protected override void OnClick(EventArgs e) { Focus(); Checked = !Checked; base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override AccessibleObject CreateAccessibilityInstance() => new ToggleAccessibility(this);
    private sealed class ToggleAccessibility(Toggle owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State => base.State | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None);
        public override string DefaultAction => "切换";
        public override void DoDefaultAction() => owner.OnClick(EventArgs.Empty);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float s = DeviceDpi / 96f;
        using var path = Theme.Round(new RectangleF(0, 5 * s, 34 * s, 20 * s), 10 * s);
        using var fill = new SolidBrush(Checked ? Theme.Accent : Color.FromArgb(207, 215, 229));
        e.Graphics.FillPath(fill, path);
        using var knob = new SolidBrush(Color.White); e.Graphics.FillEllipse(knob, (Checked ? 17 : 3) * s, 8 * s, 14 * s, 14 * s);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(44 * s), 0, Width - (int)(44 * s), Height), Theme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
}
internal sealed class DeviceComboBox : ComboBox
{
    public DeviceComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList; Dock = DockStyle.Top; FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 34; BackColor = Color.FromArgb(244, 246, 252); DropDownWidth = 480;
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            using var fill = new SolidBrush(BackColor); e.Graphics.FillRectangle(fill, e.Bounds);
            TextRenderer.DrawText(e.Graphics, "请选择设备", Font, Rectangle.Inflate(e.Bounds, -10, 0), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); return;
        }
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var brush = new SolidBrush(selected ? Color.FromArgb(232, 237, 255) : BackColor);
        e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, Items[e.Index]?.ToString() ?? "", Font, Rectangle.Inflate(e.Bounds, -10, 0), selected ? Theme.Accent : Theme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
}

internal sealed class AccentProgress : Control
{
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 32 };
    private int offset;
    private int value;
    private ProgressBarStyle style = ProgressBarStyle.Marquee;
    public int Value { get => value; set { this.value = Math.Clamp(value, 0, 100); Invalidate(); } }
    public ProgressBarStyle Style { get => style; set { style = value; Invalidate(); } }
    public AccentProgress()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 8; AccessibleRole = AccessibleRole.ProgressBar; TabStop = false;
        animation.Tick += (_, _) => { if (Visible && style == ProgressBarStyle.Marquee) { offset += 5; Invalidate(); } };
        animation.Start();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb(243, 246, 251));
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var track = Theme.Round(new RectangleF(0, 0, Width, Height), Height / 2f);
        using var background = new SolidBrush(Color.FromArgb(222, 228, 242));
        using var fill = new SolidBrush(Theme.Accent);
        e.Graphics.FillPath(background, track); e.Graphics.SetClip(track);
        if (Style == ProgressBarStyle.Marquee)
            e.Graphics.FillRectangle(fill, offset % (Width + 100) - 100, 0, 100, Height);
        else e.Graphics.FillRectangle(fill, 0, 0, Width * Value / 100f, Height);
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}
