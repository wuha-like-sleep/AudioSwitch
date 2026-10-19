namespace AudioSwitch;

internal sealed class AppDialog : ChromeForm
{
    private readonly string message;
    private AppDialog(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon, string yesText = "下载并安装", string noText = "稍后再说", bool safeDefault = false)
    {
        message = text; Text = "AudioSwitch"; Icon = BrandIcon.Load();
        Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(243, 246, 251); ForeColor = Theme.Ink;
        ClientSize = new Size(560, text.Length > 260 || text.Count(c => c == '\n') > 6 ? 440 : 340); StartPosition = FormStartPosition.CenterScreen;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 14, 28, 20), ColumnCount = 1, RowCount = 4 };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        body.Controls.Add(new Label { Text = title.Replace("AudioSwitch · ", ""), AutoSize = false, Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 18, FontStyle.Bold) }, 0, 0);
        body.Controls.Add(new Label { Text = icon == MessageBoxIcon.Error || icon == MessageBoxIcon.Warning ? "操作未完成 · 请查看详细信息" : buttons == MessageBoxButtons.YesNo ? "需要你的确认" : "状态提示", Dock = DockStyle.Fill, ForeColor = Theme.Muted }, 0, 1);
        var details = new TextBox { Text = text.ReplaceLineEndings(Environment.NewLine), Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Theme.Ink, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, TabStop = true };
        var content = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 14), Margin = new Padding(0, 8, 0, 4) };
        content.Controls.Add(details); body.Controls.Add(content, 0, 2);
        void UpdateScrolling()
        {
            if (!details.IsHandleCreated || details.ClientSize.Width <= 4) return;
            int needed = TextRenderer.MeasureText(details.Text, details.Font, new Size(details.ClientSize.Width - 4, 10000), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
            details.ScrollBars = needed > details.ClientSize.Height ? ScrollBars.Vertical : ScrollBars.None;
        }
        Shown += (_, _) => { UpdateScrolling(); details.Select(0, 0); if (AcceptButton is Control target) target.Focus(); };
        var footer = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, WrapContents = false };
        var accept = new ThemeButton { Text = buttons == MessageBoxButtons.YesNo ? yesText : "知道了", Primary = true, Size = new Size(128, 42), Margin = new Padding(10, 8, 0, 0), DialogResult = buttons == MessageBoxButtons.YesNo ? DialogResult.Yes : DialogResult.OK };
        footer.Controls.Add(accept); AcceptButton = accept;
        if (buttons == MessageBoxButtons.YesNo)
        {
            var later = new ThemeButton { Text = noText, Size = new Size(110, 42), Margin = new Padding(10, 8, 0, 0), DialogResult = DialogResult.No };
            footer.Controls.Add(later); CancelButton = later;
            if (safeDefault) AcceptButton = later;
        }
        else CancelButton = accept;
        var copy = new ThemeButton { Text = "复制信息", Size = new Size(110, 42), Margin = new Padding(10, 8, 0, 0) };
        copy.Click += (_, _) => { try { Clipboard.SetText(message); copy.Text = "已复制"; } catch (System.Runtime.InteropServices.ExternalException) { copy.Text = "请重试复制"; } }; footer.Controls.Add(copy);
        body.Controls.Add(footer, 0, 3); Controls.Add(body); body.BringToFront();
    }
    public static DialogResult Show(string text, string title, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.Information)
    {
        using var dialog = new AppDialog(text, title, buttons, icon);
        return dialog.Present();
    }
    public static DialogResult Show(IWin32Window owner, string text, string title)
    {
        using var dialog = new AppDialog(text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return dialog.Present(owner);
    }
    public static bool ConfirmAction(IWin32Window owner, string title, string text, string accept)
    {
        using var dialog = new AppDialog(text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, accept, "取消", true);
        return dialog.Present(owner) == DialogResult.Yes;
    }
    public static bool ConfirmDiscard(IWin32Window owner)
    {
        using var dialog = new AppDialog("你修改了设置，但还没有保存。\n\n关闭后将保留原来的设置，刚才的修改会被放弃。", "有未保存的修改", MessageBoxButtons.YesNo, MessageBoxIcon.Question, "放弃修改", "继续编辑", true);
        return dialog.Present(owner) == DialogResult.Yes;
    }
    internal static Form? ResolveOwner(IWin32Window? requested = null)
    {
        var owner = requested as Form;
        owner ??= Form.ActiveForm is { Visible: true, IsDisposed: false } active && active is not SwitchToast && active is not UpdateProgressForm ? active : null;
        owner ??= Application.OpenForms.Cast<Form>().LastOrDefault(f => f.Visible && !f.IsDisposed && f is not SwitchToast && f is not UpdateProgressForm);
        while (owner != null)
        {
            var child = owner.OwnedForms.LastOrDefault(f => f.Visible && !f.IsDisposed && f is not SwitchToast && f is not UpdateProgressForm);
            if (child == null) break;
            owner = child;
        }
        return owner;
    }
    private DialogResult Present(IWin32Window? requested = null)
    {
        var owner = ResolveOwner(requested);
        StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
        ShowInTaskbar = owner == null;
        if (owner?.WindowState == FormWindowState.Minimized) owner.WindowState = FormWindowState.Normal;
        return owner == null ? ShowDialog() : ShowDialog(owner);
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BringToFront(); Activate();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.C)) { Clipboard.SetText(message); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
