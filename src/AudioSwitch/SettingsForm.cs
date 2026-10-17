namespace AudioSwitch;

internal sealed class SettingsForm : Form
{
    private static readonly Color Ink = Color.FromArgb(24, 35, 55);
    private static readonly Color Muted = Color.FromArgb(100, 113, 134);
    private static readonly Color Accent = Color.FromArgb(57, 91, 237);
    private readonly ComboBox headphones = DevicePicker();
    private readonly ComboBox speakers = DevicePicker();
    private readonly Toggle startup = new() { Text = "登录 Windows 后自动启动" };
    private readonly Toggle autoUpdate = new() { Text = "启动时检查新版本" };
    private uint modifiers;
    private Keys hotkey;
    public Settings? Selection { get; private set; }
    public bool StartWithWindows => startup.Checked;

    public SettingsForm(List<OutputDevice> devices, Settings? settings)
    {
        Text = "AudioSwitch · 设置";
        Icon = BrandIcon.Load();
        Font = new Font("Microsoft YaHei UI", 10);
        ForeColor = Ink; BackColor = Color.FromArgb(243, 246, 251);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 740); MinimumSize = new Size(836, 779);
        StartPosition = FormStartPosition.CenterScreen;
        modifiers = settings?.HotkeyModifiers ?? 3; hotkey = settings?.HotkeyKey ?? Keys.F11;
        startup.Checked = StartupService.Enabled;
        autoUpdate.Checked = settings?.AutoCheckUpdates ?? true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(28), AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 124, 64, 176, 168, 84, 68 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var hero = new RoundedPanel { Dock = DockStyle.Fill, SurfaceColor = Ink, Padding = new Padding(22), Margin = new Padding(0, 0, 0, 18) };
        hero.Controls.Add(new Label { Text = "你的声音，随手切换。   /   开发者 wuha-like-sleep", ForeColor = Color.FromArgb(194, 204, 225), BackColor = Ink, Location = new Point(86, 62), AutoSize = true });
        hero.Controls.Add(new Label { Text = "AudioSwitch", ForeColor = Color.White, BackColor = Ink, Font = new Font("Segoe UI", 24, FontStyle.Bold), Location = new Point(82, 14), AutoSize = true });
        using var brandIcon = BrandIcon.Load();
        hero.Controls.Add(new PictureBox { Image = brandIcon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(44, 44), Location = new Point(24, 26), BackColor = Ink });
        var badge = new Label { Text = "WINDOWS 10 / 11", ForeColor = Color.FromArgb(184, 201, 255), BackColor = Color.FromArgb(42, 55, 81), AutoSize = true, Padding = new Padding(10, 6, 10, 6), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        hero.Controls.Add(badge);
        hero.Resize += (_, _) => badge.Location = new Point(hero.ClientSize.Width - badge.Width - 22, 26);
        root.Controls.Add(hero, 0, 0);
        root.Controls.Add(new Label { Text = "01   选择你的设备", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, Padding = new Padding(0, 14, 0, 0) }, 0, 1);
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.Controls.Add(DeviceCard("耳机", "专注时刻 · 你的私人声场", headphones), 0, 0);
        cards.Controls.Add(DeviceCard("音箱", "放松时刻 · 让声音充满空间", speakers), 1, 0);
        root.Controls.Add(cards, 0, 2);
        var optionSurface = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(20), Margin = new Padding(0, 16, 0, 0) };
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = Padding.Empty, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        options.Controls.Add(new Label { Text = "02   按你的习惯设置", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0); options.SetColumnSpan(options.GetControlFromPosition(0, 0)!, 2);
        options.Controls.Add(new Label { Text = "切换快捷键", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 1);
        var capture = new TextBox { ReadOnly = true, Dock = DockStyle.Top, BackColor = Color.FromArgb(240, 244, 255), ForeColor = Accent, BorderStyle = BorderStyle.FixedSingle, Text = HotkeyWindow.Describe(modifiers, hotkey), AccessibleName = "点击后按下新快捷键" };
        capture.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            uint mods = (e.Control ? 2u : 0) | (e.Alt ? 1u : 0) | (e.Shift ? 4u : 0);
            if (HotkeyWindow.Valid(mods, e.KeyCode)) { modifiers = mods; hotkey = e.KeyCode; capture.Text = HotkeyWindow.Describe(mods, hotkey); }
        };
        options.Controls.Add(capture, 1, 1);
        options.Controls.Add(startup, 0, 2); options.Controls.Add(autoUpdate, 1, 2);
        optionSurface.Controls.Add(options);
        root.Controls.Add(optionSurface, 0, 3);
        root.Controls.Add(new Label { Text = "点击快捷键框后，按 Ctrl / Alt + 字母、数字或 F1–F11，可加 Shift。\n切换播放与通信输出，麦克风保持原设置。保存后即可在托盘使用。", ForeColor = Muted, Dock = DockStyle.Fill, Padding = new Padding(0, 16, 0, 0) }, 0, 4);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var save = ActionButton("保存设置", true);
        var cancel = ActionButton("取消", false); cancel.DialogResult = DialogResult.Cancel;
        var refresh = ActionButton("刷新设备", false);
        footer.Controls.AddRange([save, cancel, refresh]); root.Controls.Add(footer, 0, 5);
        Controls.Add(root); AcceptButton = save; CancelButton = cancel;
        Fill(devices, settings?.Headphones, settings?.Speakers);
        refresh.Click += (_, _) =>
        {
            try { Fill(AudioDevices.List(), (headphones.SelectedItem as Choice)?.Id, (speakers.SelectedItem as Choice)?.Id); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "刷新失败"); }
        };
        save.Click += (_, _) =>
        {
            if (headphones.SelectedItem is not Choice h || speakers.SelectedItem is not Choice s)
            { MessageBox.Show(this, "请选择耳机和音箱。", "设备未选全"); return; }
            if (h.Id == s.Id) { MessageBox.Show(this, "耳机和音箱必须是两个不同的输出设备。", "设备重复"); return; }
            Selection = new Settings(h.Id, s.Id, modifiers, hotkey, autoUpdate.Checked); DialogResult = DialogResult.OK;
        };
    }
    private static ComboBox DevicePicker() => new DeviceComboBox();
    private Panel DeviceCard(string title, string subtitle, ComboBox picker)
    {
        var card = new RoundedPanel { Dock = DockStyle.Fill, SurfaceColor = Color.White, Padding = new Padding(18), Margin = new Padding(0, 0, title == "耳机" ? 12 : 0, 0) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, BackColor = Color.White, ForeColor = Accent, Font = new Font(Font.FontFamily, 15, FontStyle.Bold), AutoSize = true }, 0, 0);
        layout.Controls.Add(new Label { Text = subtitle, BackColor = Color.White, ForeColor = Muted, AutoSize = true }, 0, 1);
        layout.Controls.Add(picker, 0, 2); card.Controls.Add(layout); return card;
    }
    private static ThemeButton ActionButton(string text, bool primary)
    {
        return new ThemeButton { Text = text, Primary = primary, Size = new Size(132, 46), Font = new Font("Microsoft YaHei UI", 10, primary ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(12, 8, 0, 0) };
    }
    private void Fill(List<OutputDevice> devices, string? h, string? s)
    {
        headphones.Items.Clear(); speakers.Items.Clear();
        foreach (var device in devices)
        {
            string label = devices.Count(d => d.Name == device.Name) > 1 ? $"{device.Name} [{device.Id[^Math.Min(12, device.Id.Length)..]}]" : device.Name;
            headphones.Items.Add(new Choice(device.Id, label)); speakers.Items.Add(new Choice(device.Id, label));
        }
        headphones.SelectedIndex = devices.FindIndex(d => d.Id == h); speakers.SelectedIndex = devices.FindIndex(d => d.Id == s);
    }
    private sealed record Choice(string Id, string Label) { public override string ToString() => Label; }
}
