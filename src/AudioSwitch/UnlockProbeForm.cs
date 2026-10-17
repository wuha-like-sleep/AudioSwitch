namespace AudioSwitch;

internal sealed class UnlockProbeForm : ChromeForm
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Label summary = new() { Dock = DockStyle.Fill, ForeColor = Theme.Accent, Text = "待检测 · 当前未实现 Windows 自动解锁" };
    private readonly TextBox report = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, ForeColor = Theme.Ink };
    private bool running;
    internal string Report => report.Text;
    public UnlockProbeForm(string phoneId, Func<string, CancellationToken, Task<UnlockProbeResult>>? probe = null)
    {
        probe ??= UnlockLinkProbe.RunAsync;
        Text = "AudioSwitch · 解锁链路研究检测"; Icon = BrandIcon.Load();
        Font = new Font("Microsoft YaHei UI", 10); ForeColor = Theme.Ink; BackColor = Color.FromArgb(243, 246, 251);
        ClientSize = new Size(660, 600); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(24), AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(new Label { Text = "先验证链路，再讨论登录", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 18, FontStyle.Bold) }, 0, 0);
        root.Controls.Add(new Label { Text = "不需要 iPhone App，也不输入密码或 PIN。\n检测会尝试建立并关闭加密蓝牙连接，不发送通话命令。\n请在手机未通话时测试；其他软件占用通道时可能失败。", Dock = DockStyle.Fill, ForeColor = Theme.Muted }, 0, 1);
        root.Controls.Add(summary, 0, 2);
        var card = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = Padding.Empty };
        report.Text = "检测范围：要求认证的加密蓝牙通道。\r\n\r\nAudioSwitch 本版未安装自定义登录组件；检测不会修改登录设置、锁屏或解锁。\r\n\r\n微软账户的 PIN / Windows Hello 不作为密码读取或重放。是否存在可用账户密码，需在本机登录选项或微软账户安全设置中确认；本工具不会仅凭注册表推断。";
        card.Controls.Add(report); root.Controls.Add(card, 0, 3);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var close = Button("关闭", false); close.Click += (_, _) => Close();
        var start = Button("开始检测", true); start.Enabled = phoneId.Length > 0;
        var copy = Button("复制结果", false); copy.Click += (_, _) => Clipboard.SetText(summary.Text + Environment.NewLine + report.Text);
        start.Click += async (_, _) =>
        {
            if (running) return; running = true; start.Enabled = false;
            summary.Text = "正在检测 · 最长 20 秒";
            try
            {
                var result = await probe(phoneId, lifetime.Token);
                if (IsDisposed) return;
                summary.Text = result.Title;
                summary.ForeColor = result.AuthenticatedChannel ? Color.FromArgb(27, 139, 103) : Theme.Muted;
                report.Text = result.Details.ReplaceLineEndings(Environment.NewLine);
            }
            catch (Exception ex) { if (!IsDisposed) { summary.Text = "检测未完成"; report.Text = ex.Message; } }
            finally { running = false; if (!start.IsDisposed) start.Enabled = true; }
        };
        footer.Controls.AddRange([close, start, copy]); root.Controls.Add(footer, 0, 4);
        Controls.Add(root); root.BringToFront(); AcceptButton = start; CancelButton = close;
        FormClosing += (_, _) => lifetime.Cancel();
    }
    private static ThemeButton Button(string text, bool primary) => new() { Text = text, Primary = primary, Size = new Size(120, 42), Margin = new Padding(10, 8, 0, 0) };
    protected override void Dispose(bool disposing) { if (disposing) lifetime.Dispose(); base.Dispose(disposing); }
}
