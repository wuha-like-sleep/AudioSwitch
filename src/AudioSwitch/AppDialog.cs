namespace AudioSwitch;

internal sealed class AppDialog : ChromeForm
{
    private readonly string message;
    private AppDialog(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        message = text; Text = "AudioSwitch"; Icon = BrandIcon.Load();
        Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(243, 246, 251); ForeColor = Theme.Ink;
        ClientSize = new Size(560, 370); StartPosition = FormStartPosition.CenterScreen;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 14, 28, 24), ColumnCount = 1, RowCount = 4 };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        body.Controls.Add(new Label { Text = title.Replace("AudioSwitch · ", ""), AutoSize = false, Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 18, FontStyle.Bold) }, 0, 0);
        body.Controls.Add(new Label { Text = icon == MessageBoxIcon.Error || icon == MessageBoxIcon.Warning ? "请查看以下信息后重试" : "音频切换 · 版本与使用提示", Dock = DockStyle.Fill, ForeColor = Theme.Muted }, 0, 1);
        var details = new TextBox { Text = text, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = BackColor, ForeColor = Theme.Ink, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, TabStop = true };
        body.Controls.Add(details, 0, 2);
        var footer = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, WrapContents = false };
        var accept = new ThemeButton { Text = buttons == MessageBoxButtons.YesNo ? "下载并安装" : "知道了", Primary = true, Size = new Size(128, 42), Margin = new Padding(10, 8, 0, 0), DialogResult = buttons == MessageBoxButtons.YesNo ? DialogResult.Yes : DialogResult.OK };
        footer.Controls.Add(accept); AcceptButton = accept;
        if (buttons == MessageBoxButtons.YesNo)
        {
            var later = new ThemeButton { Text = "稍后再说", Size = new Size(110, 42), Margin = new Padding(10, 8, 0, 0), DialogResult = DialogResult.No };
            footer.Controls.Add(later); CancelButton = later;
        }
        else CancelButton = accept;
        var copy = new ThemeButton { Text = "复制信息", Size = new Size(110, 42), Margin = new Padding(10, 8, 0, 0) };
        copy.Click += (_, _) => Clipboard.SetText(message); footer.Controls.Add(copy);
        body.Controls.Add(footer, 0, 3); Controls.Add(body); body.BringToFront();
    }
    public static DialogResult Show(string text, string title, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.Information)
    {
        using var dialog = new AppDialog(text, title, buttons, icon);
        return dialog.ShowDialog();
    }
    public static DialogResult Show(IWin32Window owner, string text, string title)
    {
        using var dialog = new AppDialog(text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return dialog.ShowDialog(owner);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.C)) { Clipboard.SetText(message); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
