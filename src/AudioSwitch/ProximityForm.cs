namespace AudioSwitch;

internal sealed class ProximityForm : ChromeForm
{
    private readonly ComboBox phone = new DeviceComboBox();
    private readonly ComboBox mode = new DeviceComboBox();
    private readonly ComboBox delay = new DeviceComboBox();
    private readonly ComboBox threshold = new DeviceComboBox();
    private readonly Toggle enabled = new() { Text = "启用手机离开锁屏", Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, AutoEllipsis = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Func<List<PhoneSnapshot>> read;
    private readonly Func<string?> error;
    public ProximitySettings? Selection { get; private set; }
    public ProximityForm(ProximitySettings? settings, Func<List<PhoneSnapshot>> read, Func<string?> error)
    {
        this.read = read; this.error = error; settings ??= new();
        Text = "AudioSwitch · 手机离开锁屏"; Icon = BrandIcon.Load(); Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Color.FromArgb(243, 246, 251); ForeColor = Theme.Ink;
        ClientSize = new Size(660, 640); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(24), AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 42, 48, 236, 60, 82, 60 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.Controls.Add(new Label { Text = "带着手机离开，电脑自动锁定", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        root.Controls.Add(new Label { Text = "先在 Windows 中配对手机，再选择下面的监测方式。\n只会锁屏，不会自动解锁，也不会主动连接手机。", ForeColor = Theme.Muted, Dock = DockStyle.Fill }, 0, 1);
        var card = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = Padding.Empty };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, BackColor = Color.White };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); for (int i = 0; i < 4; i++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        enabled.Checked = settings.Enabled; fields.Controls.Add(enabled, 0, 0); fields.SetColumnSpan(enabled, 2);
        AddField(fields, "配对的手机", phone, 1); AddField(fields, "触发方式", mode, 2); AddField(fields, "持续离开时间", delay, 3); AddField(fields, "弱信号阈值", threshold, 4);
        mode.Items.AddRange([new Choice("disconnect", "断开连接"), new Choice("signal", "实时信号较弱（实验性）"), new Choice("either", "断开或信号较弱")]);
        mode.SelectedItem = mode.Items.Cast<Choice>().FirstOrDefault(c => c.Id == settings.Mode) ?? mode.Items[0];
        foreach (int value in new[] { 15, 30, 60, 120 }) delay.Items.Add(new Choice(value.ToString(), $"{value} 秒"));
        delay.SelectedItem = delay.Items.Cast<Choice>().FirstOrDefault(c => c.Id == settings.DelaySeconds.ToString()) ?? delay.Items[1];
        foreach (int value in new[] { -65, -75, -85, -95 }) threshold.Items.Add(new Choice(value.ToString(), $"{value}（越低，通常需要离得更远）"));
        threshold.SelectedItem = threshold.Items.Cast<Choice>().FirstOrDefault(c => c.Id == settings.SignalThreshold.ToString()) ?? threshold.Items[1];
        FillPhones(settings.PhoneId);
        mode.SelectedIndexChanged += (_, _) => threshold.Enabled = (mode.SelectedItem as Choice)?.Id != "disconnect";
        threshold.Enabled = settings.Mode != "disconnect";
        card.Controls.Add(fields); root.Controls.Add(card, 0, 2); root.Controls.Add(status, 0, 3);
        root.Controls.Add(new Label { Text = "信号模式只在 Windows 提供实时负值信号时可用，不代表精确米数。\n读取不到、信号过期或监测异常时暂停信号锁屏；手机回到附近会取消倒计时。\n首次需先检测到手机可用，重启或解锁后重新确认，避免启动即误锁。", ForeColor = Theme.Muted, Dock = DockStyle.Fill }, 0, 4);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var done = Button("完成", true); done.Click += (_, _) =>
        {
            string id = (phone.SelectedItem as Choice)?.Id ?? "";
            if (enabled.Checked && id.Length == 0) { AppDialog.Show(this, "请先配对并选择手机。", "尚未选择手机"); return; }
            Selection = new(enabled.Checked, id, (mode.SelectedItem as Choice)!.Id, int.Parse(((Choice)delay.SelectedItem!).Id), int.Parse(((Choice)threshold.SelectedItem!).Id));
            DialogResult = DialogResult.OK;
        };
        var cancel = Button("取消", false); cancel.DialogResult = DialogResult.Cancel;
        var refresh = Button("刷新手机", false); refresh.Click += (_, _) => FillPhones((phone.SelectedItem as Choice)?.Id ?? "");
        footer.Controls.AddRange([done, cancel, refresh]); root.Controls.Add(footer, 0, 5);
        Controls.Add(root); root.BringToFront(); AcceptButton = done; CancelButton = cancel;
        timer.Tick += (_, _) => RefreshStatus(); timer.Start(); RefreshStatus();
        Shown += (_, _) => done.Focus();
    }
    private void FillPhones(string preferred)
    {
        phone.Items.Clear();
        foreach (var item in read()) phone.Items.Add(new Choice(item.Id, item.Name));
        var match = phone.Items.Cast<Choice>().FirstOrDefault(c => c.Id == preferred);
        if (match == null && preferred.Length > 0) { match = new Choice(preferred, "已保存的手机 · 暂未识别"); phone.Items.Add(match); }
        phone.SelectedItem = match;
    }
    private void RefreshStatus()
    {
        if (error() is { } problem) { status.Text = problem; return; }
        var selected = read().FirstOrDefault(p => p.Id == (phone.SelectedItem as Choice)?.Id);
        bool fresh = selected?.SignalAt is { } seen && DateTimeOffset.UtcNow - seen <= TimeSpan.FromSeconds(20);
        status.Text = selected == null ? "等待选择手机。若列表为空，请先配对手机，再点击刷新。" :
            $"连接：{(selected.Connected == true ? "已连接" : selected.Connected == false ? "已断开" : "未知")}\n实时信号：{(fresh ? selected.Signal!.Value.ToString() : "暂不支持或未收到实时信号")}";
    }
    private static void AddField(TableLayoutPanel fields, string name, Control control, int row)
    {
        fields.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row); fields.Controls.Add(control, 1, row);
    }
    private static ThemeButton Button(string text, bool primary) => new() { Text = text, Primary = primary, Size = new Size(120, 42), Margin = new Padding(10, 8, 0, 0) };
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    private sealed record Choice(string Id, string Label) { public override string ToString() => Label; }
}
