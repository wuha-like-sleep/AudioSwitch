namespace AudioSwitch;

internal sealed class ProximityForm : ChromeForm
{
    private readonly ComboBox phone = new DeviceComboBox();
    private readonly ComboBox mode = new DeviceComboBox();
    private readonly ComboBox delay = new DeviceComboBox();
    private readonly NumericUpDown threshold = new() { Minimum = -120, Maximum = -20, Value = -75, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, AccessibleName = "弱信号阈值", BackColor = Color.FromArgb(244, 246, 252) };
    private readonly NumericUpDown hysteresis = new() { Minimum = 1, Maximum = 20, Value = 5, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, AccessibleName = "恢复缓冲", BackColor = Color.FromArgb(244, 246, 252) };
    private readonly RoundedPanel statusCard = new() { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8), Margin = new Padding(0, 8, 0, 8) };
    private readonly Queue<(int Value, DateTimeOffset At)> readings = new();
    private DateTimeOffset? lastReading;
    private readonly Toggle enabled = new() { Text = "启用手机离开锁屏", Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, AutoEllipsis = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Func<List<PhoneSnapshot>> read;
    private readonly Func<string?> error;
    private readonly ProximityGuard testGuard = new();
    private bool testing, testWouldLock;
    internal bool IsTesting => testing;
    internal string StatusText => status.Text;
    public ProximitySettings? Selection { get; private set; }
    public ProximityForm(ProximitySettings? settings, Func<List<PhoneSnapshot>> read, Func<string?> error)
    {
        this.read = read; this.error = error; settings ??= new();
        Text = "AudioSwitch · 手机离开锁屏"; Icon = BrandIcon.Load(); Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Color.FromArgb(243, 246, 251); ForeColor = Theme.Ink;
        ClientSize = new Size(660, 700); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(24), AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 42, 48, 278, 94, 82, 60 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.Controls.Add(new Label { Text = "带着手机离开，电脑自动锁定", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        root.Controls.Add(new Label { Text = "先在 Windows 中配对手机，再选择下面的监测方式。\n离开监测只会锁屏；解锁检测需手动点击，不会解锁。", ForeColor = Theme.Muted, Dock = DockStyle.Fill }, 0, 1);
        var card = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = Padding.Empty };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, BackColor = Color.White };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); for (int i = 0; i < 5; i++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        enabled.Checked = settings.Enabled; fields.Controls.Add(enabled, 0, 0); fields.SetColumnSpan(enabled, 2);
        AddField(fields, "配对的手机", phone, 1); AddField(fields, "触发方式", mode, 2); AddField(fields, "持续离开时间", delay, 3); var signalRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        signalRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        signalRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); signalRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        var calibrate = new ThemeButton { Text = "参考标定", Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
        calibrate.Click += (_, _) =>
        {
            try
            {
                if (lastReading == null || DateTimeOffset.UtcNow < lastReading.Value || DateTimeOffset.UtcNow - lastReading.Value > TimeSpan.FromSeconds(20)) throw new InvalidOperationException("当前没有实时信号，请等待手机更新信号后再标定。");
                threshold.Value = SignalCalibration.Recommend(readings.Select(r => r.Value));
            }
            catch (InvalidOperationException ex) { AppDialog.Show(this, ex.Message, "参考标定"); }
        };
        var thresholdSurface = NumberSurface(threshold);
        var recoverySurface = NumberSurface(hysteresis);
        signalRow.Controls.Add(thresholdSurface, 0, 0); signalRow.Controls.Add(calibrate, 1, 0);
        AddField(fields, "弱信号阈值", signalRow, 4); AddField(fields, "恢复缓冲值", recoverySurface, 5);
        mode.Items.AddRange([new Choice("disconnect", "断开连接"), new Choice("signal", "实时信号较弱（实验性）"), new Choice("either", "断开或信号较弱")]);
        mode.SelectedItem = mode.Items.Cast<Choice>().FirstOrDefault(c => c.Id == settings.Mode) ?? mode.Items[0];
        foreach (int value in new[] { 15, 30, 60, 120 }) delay.Items.Add(new Choice(value.ToString(), $"{value} 秒"));
        delay.SelectedItem = delay.Items.Cast<Choice>().FirstOrDefault(c => c.Id == settings.DelaySeconds.ToString()) ?? delay.Items[1];
        threshold.Value = Math.Clamp(settings.SignalThreshold, -120, -20);
        hysteresis.Value = Math.Clamp(settings.SignalHysteresis, 1, 20);
        FillPhones(settings.PhoneId);
        var tips = new ToolTip();
        tips.SetToolTip(hysteresis, "实时信号高于“阈值 + 缓冲值”才视为回到附近；这里不是秒数。");
        tips.SetToolTip(calibrate, "手机放在正常使用位置，至少采集 3 次实时更新；参考值取中位数减 15，可继续手动调整。");
        FormClosed += (_, _) => tips.Dispose();
        mode.SelectedIndexChanged += (_, _) => { signalRow.Enabled = (mode.SelectedItem as Choice)?.Id != "disconnect"; hysteresis.Enabled = signalRow.Enabled; };
        signalRow.Enabled = (mode.SelectedItem as Choice)?.Id != "disconnect"; hysteresis.Enabled = signalRow.Enabled;
        card.Controls.Add(fields); root.Controls.Add(card, 0, 2); statusCard.Controls.Add(status); root.Controls.Add(statusCard, 0, 3);
        root.Controls.Add(new Label { Text = "信号模式只在 Windows 提供实时负值信号时可用，不代表精确米数。\n读取不到、信号过期或监测异常时暂停信号锁屏；手机回到附近会取消倒计时。\n首次需先检测到手机可用，重启或解锁后重新确认。\n完成后请回到设置页保存并关闭窗口，正式监测才会开始。", ForeColor = Theme.Muted, Dock = DockStyle.Fill }, 0, 4);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var done = Button("完成", true); done.Click += (_, _) =>
        {
            string id = (phone.SelectedItem as Choice)?.Id ?? "";
            if (enabled.Checked && id.Length == 0) { AppDialog.Show(this, "请先配对并选择手机。", "尚未选择手机"); return; }
            Selection = CurrentSettings();
            DialogResult = DialogResult.OK;
        };
        var cancel = Button("取消", false); cancel.DialogResult = DialogResult.Cancel;
        var test = Button("测试判定", false);
        test.Click += (_, _) =>
        {
            testing = !testing; testWouldLock = false; testGuard.Reset();
            test.Text = testing ? "停止测试" : "测试判定";
            RefreshStatus();
        };
                foreach (var picker in new[] { phone, mode, delay }) picker.SelectedIndexChanged += (_, _) => { testGuard.Reset(); testWouldLock = false; };
        phone.SelectedIndexChanged += (_, _) => { readings.Clear(); lastReading = null; };
        threshold.ValueChanged += (_, _) => { testGuard.Reset(); testWouldLock = false; };
        hysteresis.ValueChanged += (_, _) => { testGuard.Reset(); testWouldLock = false; };
        var refresh = Button("刷新手机", false); refresh.Click += (_, _) => FillPhones((phone.SelectedItem as Choice)?.Id ?? "");
        var unlock = Button("解锁检测", false);
        unlock.Click += (_, _) =>
        {
            using var form = new UnlockProbeForm((phone.SelectedItem as Choice)?.Id ?? "");
            form.ShowDialog(this);
        };
        footer.Controls.AddRange([done, cancel, refresh, test, unlock]); root.Controls.Add(footer, 0, 5);
        Controls.Add(root); root.BringToFront(); AcceptButton = done; CancelButton = cancel;
        timer.Tick += (_, _) => RefreshStatus(); timer.Start(); RefreshStatus();
        Shown += (_, _) => done.Focus();
    }
    private ProximitySettings CurrentSettings() => new(enabled.Checked, (phone.SelectedItem as Choice)?.Id ?? "", ((Choice)mode.SelectedItem!).Id, int.Parse(((Choice)delay.SelectedItem!).Id), decimal.ToInt32(threshold.Value), decimal.ToInt32(hysteresis.Value));
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
        statusCard.SurfaceColor = testing ? Color.FromArgb(235, 240, 255) : Color.White;
        statusCard.Invalidate();
        if (error() is { } problem) { testGuard.Reset(); testWouldLock = false; status.Text = problem; return; }
        var selected = read().FirstOrDefault(p => p.Id == (phone.SelectedItem as Choice)?.Id);
        bool fresh = selected?.SignalAt is { } seen && DateTimeOffset.UtcNow >= seen && DateTimeOffset.UtcNow - seen <= TimeSpan.FromSeconds(20) && selected.Signal is >= -127 and < 0;
        if (fresh && selected!.SignalAt != lastReading)
        {
            readings.Enqueue((selected.Signal!.Value, selected.SignalAt!.Value)); lastReading = selected.SignalAt;
        }
        while (readings.Count > 0 && (readings.Count > 9 || DateTimeOffset.UtcNow - readings.Peek().At > TimeSpan.FromSeconds(30))) readings.Dequeue();
        status.Text = selected == null ? "等待选择手机。若列表为空，请先配对手机，再点击刷新。" :
            $"连接：{(selected.Connected == true ? "已连接" : selected.Connected == false ? "已断开" : "未知")} · 实时信号：{(fresh ? selected.Signal!.Value.ToString() : "暂无可用值")}";
        status.Text += $"\n弱信号 ≤ {threshold.Value:0} · 恢复需 > {threshold.Value + hysteresis.Value:0} · 已采集 {readings.Count} 次";
        if (testing)
        {
            var result = testGuard.Evaluate(CurrentSettings() with { Enabled = true }, selected, DateTimeOffset.UtcNow);
            testWouldLock |= result.ShouldLock;
            if (result.Healthy || selected == null) testWouldLock = false;
            status.Text += "\n测试（不会锁屏）：" + (testWouldLock ? "本次达到锁屏条件，测试没有执行锁屏。" : result.Message);
        }
        else status.Text += "\n可测试判定，也可在正常距离采集读数后点击参考标定。";
    }
    private static RoundedPanel NumberSurface(Control number)
    {
        var surface = new RoundedPanel { Dock = DockStyle.Fill, SurfaceColor = Color.FromArgb(244, 246, 252), Padding = new Padding(10, 6, 6, 2), Margin = new Padding(0, 3, 0, 3) };
        surface.Controls.Add(number); return surface;
    }
    private static void AddField(TableLayoutPanel fields, string name, Control control, int row)
    {
        fields.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row); fields.Controls.Add(control, 1, row);
    }
    private static ThemeButton Button(string text, bool primary) => new() { Text = text, Primary = primary, Size = new Size(104, 42), Margin = new Padding(8, 8, 0, 0) };
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    private sealed record Choice(string Id, string Label) { public override string ToString() => Label; }
}
