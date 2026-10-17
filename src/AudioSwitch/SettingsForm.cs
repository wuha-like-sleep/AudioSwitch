namespace AudioSwitch;

internal sealed class SettingsForm : ChromeForm
{
    private static readonly Color Ink = Theme.Ink;
    private static readonly Color Muted = Theme.Muted;
    private static readonly Color Accent = Theme.Accent;
    private readonly ComboBox headphones = DevicePicker();
    private readonly ComboBox speakers = DevicePicker();
    private readonly ComboBox toastMonitor = DevicePicker();
    private readonly Toggle startup = new() { Text = "登录 Windows 后自动启动" };
    private readonly Toggle autoUpdate = new() { Text = "启动时检查新版本" };
    private readonly Label outputStatus = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Muted };
    private readonly Label headphoneStatus = new() { AutoSize = true, ForeColor = Theme.Muted };
    private readonly Label speakerStatus = new() { AutoSize = true, ForeColor = Theme.Muted };
    private List<OutputDevice> knownDevices = [];
    private readonly Label shortcutHelp = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Padding = new Padding(0, 8, 0, 0) };
    private Draft? original;
    internal bool HasUnsavedChanges => original != null && CurrentDraft() != original;
    private uint modifiers;
    private Keys hotkey;
    public Settings? Selection { get; private set; }
    public bool StartWithWindows => startup.Checked;

    public SettingsForm(List<OutputDevice> devices, Settings? settings, Func<Settings, bool, string?>? saveSettings = null, Func<Task>? checkUpdates = null)
    {
        Text = "AudioSwitch · 设置";
        Icon = BrandIcon.Load();
        Font = new Font("Microsoft YaHei UI", 10);
        ForeColor = Ink; BackColor = Color.FromArgb(243, 246, 251);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 700); MinimumSize = new Size(776, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Shown += (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            if (Height > area.Height - 16)
            {
                MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width - 16), 400);
                Height = Math.Max(400, area.Height - 16);
                Top = area.Top + 8;
            }
        };
        modifiers = settings?.HotkeyModifiers ?? 3; hotkey = settings?.HotkeyKey ?? Keys.F11;
        startup.Checked = StartupService.Enabled;
        autoUpdate.Checked = settings?.AutoCheckUpdates ?? true;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(28), AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 80, 48, 156, 200, 48, 64 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var hero = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = BackColor, ColumnCount = 3, RowCount = 2,
            Margin = new Padding(0, 0, 0, 16)
        };
        hero.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hero.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        hero.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        hero.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        using var brandIcon = BrandIcon.Load();
        var mark = new PictureBox
        {
            Image = brandIcon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(42, 42), Margin = new Padding(0, 9, 16, 0), BackColor = BackColor
        };
        hero.Controls.Add(mark, 0, 0);
        hero.SetRowSpan(mark, 2);
        hero.Controls.Add(new Label
        {
            Text = "音频设置", ForeColor = Ink, Font = new Font("Segoe UI", 21, FontStyle.Bold),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Margin = Padding.Empty
        }, 1, 0);
        hero.Controls.Add(new Label
        {
            Text = "选择输出设备，设置你的切换方式。", ForeColor = Muted, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft, Padding = new Padding(1, 5, 0, 0), Margin = Padding.Empty
        }, 1, 1);
        var author = new Label
        {
            Text = $"{UpdateService.CurrentTag}\n开发者  wuha-like-sleep", ForeColor = Muted, Font = new Font(Font.FontFamily, 9),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Margin = Padding.Empty
        };
        hero.Controls.Add(author, 2, 0);
        hero.SetRowSpan(author, 2);
        root.Controls.Add(hero, 0, 0);
        var deviceHeading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        deviceHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        deviceHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        deviceHeading.Controls.Add(new Label { Text = "输出设备", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        deviceHeading.Controls.Add(outputStatus, 1, 0);
        root.Controls.Add(deviceHeading, 0, 1);
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.Controls.Add(DeviceCard("耳机", "个人聆听、游戏与通话", headphones), 0, 0);
        cards.Controls.Add(DeviceCard("音箱", "外放音乐与共享播放", speakers), 1, 0);
        root.Controls.Add(cards, 0, 2);
        var optionSurface = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(20), Margin = new Padding(0, 16, 0, 0) };
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = Padding.Empty, ColumnCount = 2, RowCount = 4, Margin = Padding.Empty };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        options.Controls.Add(new Label { Text = "偏好设置", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0); options.SetColumnSpan(options.GetControlFromPosition(0, 0)!, 2);
        options.Controls.Add(new Label { Text = "切换快捷键", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 1);
        var capture = new TextBox { ReadOnly = true, Dock = DockStyle.Top, BackColor = Color.FromArgb(240, 244, 255), ForeColor = Accent, BorderStyle = BorderStyle.FixedSingle, Text = HotkeyWindow.Describe(modifiers, hotkey), AccessibleName = "点击后按下新快捷键" };
        capture.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Tab or Keys.Escape or Keys.Enter) return;
            e.SuppressKeyPress = true;
            uint mods = (e.Control ? 2u : 0) | (e.Alt ? 1u : 0) | (e.Shift ? 4u : 0);
            if (e.KeyCode is Keys.ControlKey or Keys.Menu or Keys.ShiftKey) return;
            if (HotkeyWindow.Valid(mods, e.KeyCode))
            {
                modifiers = mods; hotkey = e.KeyCode; capture.Text = HotkeyWindow.Describe(mods, hotkey);
                ShowShortcutHelp("快捷键已选择，保存后生效。", false);
            }
            else ShowShortcutHelp(e.KeyCode == Keys.F12 ? "F12 为系统保留键，请选择 F1–F11 或字母、数字。" : "请按 Ctrl / Alt + 字母、数字或 F1–F11，可加 Shift。", true);
        };
        var shortcutRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        shortcutRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shortcutRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        var resetShortcut = new ThemeButton { Text = "重置", Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0), AccessibleName = "恢复默认快捷键 Ctrl Alt F11" };
        resetShortcut.Click += (_, _) =>
        {
            modifiers = 3; hotkey = Keys.F11; capture.Text = HotkeyWindow.Describe(modifiers, hotkey);
            ShowShortcutHelp("已恢复默认快捷键，保存后生效。", false);
        };
        shortcutRow.Controls.Add(capture, 0, 0); shortcutRow.Controls.Add(resetShortcut, 1, 0);
        options.Controls.Add(shortcutRow, 1, 1);
        options.Controls.Add(startup, 0, 2); options.Controls.Add(autoUpdate, 1, 2);
        options.Controls.Add(new Label { Text = "切换提示屏幕", AutoSize = true, Padding = new Padding(0, 8, 0, 0) }, 0, 3);
        var monitorRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        var previewToast = new ThemeButton { Text = "预览", Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
        SwitchToast? sampleToast = null;
        previewToast.Click += (_, _) =>
        {
            sampleToast?.Close(); sampleToast?.Dispose();
            sampleToast = new SwitchToast("提示预览", "切换成功后会在这里提示", (toastMonitor.SelectedItem as Choice)?.Id ?? "");
            sampleToast.Show();
        };
        FormClosed += (_, _) => { sampleToast?.Close(); sampleToast?.Dispose(); };
        monitorRow.Controls.Add(toastMonitor, 0, 0); monitorRow.Controls.Add(previewToast, 1, 0);
        options.Controls.Add(monitorRow, 1, 3);
        FillMonitors(settings?.ToastMonitor ?? "");
        optionSurface.Controls.Add(options);
        root.Controls.Add(optionSurface, 0, 3);
        ShowShortcutHelp("点击快捷键框后，按 Ctrl / Alt + 字母、数字或 F1–F11，可加 Shift。", false);
        root.Controls.Add(shortcutHelp, 0, 4);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var save = ActionButton("保存设置", true);
        var cancel = ActionButton("取消", false); cancel.DialogResult = DialogResult.Cancel;
        var refresh = ActionButton("刷新设备", false);
        var update = ActionButton("检查更新", false); update.Enabled = checkUpdates != null;
        update.Click += async (_, _) =>
        {
            if (checkUpdates == null) return;
            update.Enabled = false;
            try { await checkUpdates(); }
            catch (Exception ex) { if (!IsDisposed) AppDialog.Show(this, ex.Message, "检查更新失败"); }
            finally { if (!update.IsDisposed) update.Enabled = true; }
        };
        footer.Controls.AddRange([save, cancel, refresh, update]); root.Controls.Add(footer, 0, 5);
        Controls.Add(root); root.BringToFront(); AcceptButton = save; CancelButton = cancel;
        Fill(devices, settings?.Headphones, settings?.Speakers);
        headphones.SelectedIndexChanged += (_, _) => UpdateConnectionStatus();
        speakers.SelectedIndexChanged += (_, _) => UpdateConnectionStatus();
        UpdateConnectionStatus();
        outputStatus.Text = "设备状态自动刷新";
        original = CurrentDraft();
        FormClosing += (_, e) =>
        {
            if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing or CloseReason.ApplicationExitCall) return;
            if (DialogResult != DialogResult.OK && HasUnsavedChanges && !AppDialog.ConfirmDiscard(this))
            { e.Cancel = true; DialogResult = DialogResult.None; }
        };
        refresh.Click += (_, _) =>
        {
            try { Fill(AudioDevices.List(), (headphones.SelectedItem as Choice)?.Id, (speakers.SelectedItem as Choice)?.Id); FillMonitors((toastMonitor.SelectedItem as Choice)?.Id ?? ""); }
            catch (Exception ex) { AppDialog.Show(this, ex.Message, "刷新失败"); }
        };
        save.Click += (_, _) =>
        {
            if (headphones.SelectedItem is not Choice h || speakers.SelectedItem is not Choice s)
            { AppDialog.Show(this, "请选择耳机和音箱。", "设备未选全"); return; }
            if (h.Id == s.Id) { AppDialog.Show(this, "耳机和音箱必须是两个不同的输出设备。", "设备重复"); return; }
            var candidate = new Settings(h.Id, s.Id, modifiers, hotkey, autoUpdate.Checked, (toastMonitor.SelectedItem as Choice)?.Id ?? "");
            var error = saveSettings?.Invoke(candidate, StartWithWindows);
            if (error != null) { AppDialog.Show(this, error, "设置未保存"); return; }
            Selection = candidate; DialogResult = DialogResult.OK;
        };
    }
    private void ShowShortcutHelp(string text, bool invalid)
    {
        shortcutHelp.Text = text + "\n切换播放与通信输出，麦克风保持原设置。";
        shortcutHelp.ForeColor = invalid ? Color.FromArgb(185, 65, 65) : Muted;
    }
    private Draft CurrentDraft() => new((headphones.SelectedItem as Choice)?.Id, (speakers.SelectedItem as Choice)?.Id,
        modifiers, hotkey, startup.Checked, autoUpdate.Checked, (toastMonitor.SelectedItem as Choice)?.Id ?? "");
    private sealed record Draft(string? Headphones, string? Speakers, uint Modifiers, Keys Key, bool Startup, bool AutoUpdate, string Monitor);

    private void FillMonitors(string preferred)
    {
        toastMonitor.Items.Clear();
        toastMonitor.Items.Add(new Choice("", "主显示器（跟随系统主屏）"));
        int number = 0;
        foreach (var screen in Screen.AllScreens)
        {
            number++;
            toastMonitor.Items.Add(new Choice(screen.DeviceName,
                $"显示器 {number}{(screen.Primary ? " · 主屏" : "")} · {screen.Bounds.Width} × {screen.Bounds.Height}"));
        }
        var match = toastMonitor.Items.Cast<Choice>().FirstOrDefault(c => c.Id == preferred);
        if (match == null)
        {
            match = new Choice(preferred, "指定屏幕未连接 · 暂用主屏");
            toastMonitor.Items.Add(match);
        }
        toastMonitor.SelectedItem = match;
    }
    private static ComboBox DevicePicker() => new DeviceComboBox();
    private Panel DeviceCard(string title, string subtitle, ComboBox picker)
    {
        var card = new RoundedPanel { Dock = DockStyle.Fill, SurfaceColor = Color.White, Padding = new Padding(18), Margin = new Padding(0, 0, title == "耳机" ? 12 : 0, 0) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, BackColor = Color.White, ForeColor = Accent, Font = new Font(Font.FontFamily, 15, FontStyle.Bold), AutoSize = true }, 0, 0);
        var status = title == "耳机" ? headphoneStatus : speakerStatus;
        status.Text = subtitle;
        layout.Controls.Add(status, 0, 1);
        layout.Controls.Add(picker, 0, 2); card.Controls.Add(layout); return card;
    }
    private static ThemeButton ActionButton(string text, bool primary)
    {
        return new ThemeButton { Text = text, Primary = primary, Size = new Size(132, 46), Font = new Font("Microsoft YaHei UI", 10, primary ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(12, 8, 0, 0) };
    }
    private void Fill(List<OutputDevice> devices, string? h, string? s)
    {
        knownDevices = devices.ToList();
        headphones.Items.Clear(); speakers.Items.Clear();
        foreach (var device in devices)
        {
            string label = devices.Count(d => d.Name == device.Name) > 1 ? $"{device.Name} [{device.Id[^Math.Min(12, device.Id.Length)..]}]" : device.Name;
            headphones.Items.Add(new Choice(device.Id, label)); speakers.Items.Add(new Choice(device.Id, label));
        }
        RestoreChoice(headphones, h, "已保存的耳机 · 未连接");
        RestoreChoice(speakers, s, "已保存的音箱 · 未连接");
    }
    public void UpdateDevices(List<OutputDevice> devices, string? current)
    {
        // Preserve focus and an open picker while devices are plugged in or removed.
        if (!headphones.DroppedDown && !speakers.DroppedDown && !knownDevices.SequenceEqual(devices))
            Fill(devices, (headphones.SelectedItem as Choice)?.Id, (speakers.SelectedItem as Choice)?.Id);
        outputStatus.Text = "当前输出 · " + (devices.FirstOrDefault(d => d.Id == current)?.Name ?? "无可用输出");
        UpdateConnectionStatus();
    }
    private void UpdateConnectionStatus()
    {
        UpdateConnection(headphones, headphoneStatus);
        UpdateConnection(speakers, speakerStatus);
    }
    private void UpdateConnection(ComboBox picker, Label label)
    {
        bool chosen = picker.SelectedItem is Choice;
        bool online = picker.SelectedItem is Choice choice && knownDevices.Any(d => d.Id == choice.Id);
        label.Text = !chosen ? "请选择一个输出设备" : online ? "已连接 · 可以切换" : "未连接 · 插入后自动识别";
        label.ForeColor = online ? Color.FromArgb(27, 139, 103) : Muted;
    }
    private static void RestoreChoice(ComboBox picker, string? id, string offlineLabel)
    {
        if (string.IsNullOrEmpty(id)) { picker.SelectedIndex = -1; return; }
        var choice = picker.Items.Cast<Choice>().FirstOrDefault(c => c.Id == id);
        if (choice == null) { choice = new Choice(id, offlineLabel); picker.Items.Add(choice); }
        picker.SelectedItem = choice;
    }
    private sealed record Choice(string Id, string Label) { public override string ToString() => Label; }
}
