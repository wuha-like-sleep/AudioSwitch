namespace AudioSwitch;

internal sealed class SettingsForm : ChromeForm
{
    private static readonly Color Ink = Theme.Ink;
    private static readonly Color Muted = Theme.Muted;
    private static readonly Color Accent = Theme.Accent;
    private List<OutputDevice> selectedDevices = [];
    private readonly DeviceSummaryPanel deviceSummary = new();
    private readonly Label cycleHeading = new() { Dock = DockStyle.Fill, ForeColor = Theme.Accent };
    private readonly ComboBox toastMonitor = DevicePicker();
    private readonly Toggle startup = new() { Text = "登录 Windows 后自动启动" };
    private readonly Toggle autoUpdate = new() { Text = "启动时检查新版本" };
    private readonly Label outputStatus = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Muted };

    private List<OutputDevice> knownDevices = [];
    private readonly Label shortcutHelp = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Padding = new Padding(0, 8, 0, 0) };
    private Draft? original;
    internal bool HasUnsavedChanges => original != null && CurrentDraft() != original;
    private readonly Toggle showToast = new() { Text = "显示切换提示", Height = 28, Margin = Padding.Empty };
    private ProximitySettings? proximity;
    private string toastCorner;
    private int toastDuration;
    private uint modifiers;
    private Keys hotkey;
    public Settings? Selection { get; private set; }
    public bool StartWithWindows => startup.Checked;

    public SettingsForm(List<OutputDevice> devices, Settings? settings, Func<Settings, bool, string?>? saveSettings = null, Func<Task>? checkUpdates = null, Action? switchOutput = null, Action? showDiagnostics = null, Func<ProximitySettings?, ProximitySettings?>? configureProximity = null)
    {
        selectedDevices = settings == null ? [] : SwitchTarget.Order(settings);
        proximity = settings?.Proximity;
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
        showToast.Checked = settings?.ShowSwitchNotifications ?? true;
        toastCorner = settings?.ToastCorner ?? "bottom-right";
        toastDuration = settings?.ToastDuration ?? 2200;

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
            Text = "管理多个输出设备，按自己的顺序随手切换。", ForeColor = Muted, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft, Padding = new Padding(1, 5, 0, 0), Margin = Padding.Empty
        }, 1, 1);
        var author = new Label
        {
            Text = $"{UpdateService.CurrentTag}\n开发者  wuha-like-sleep", ForeColor = Muted, Font = new Font(Font.FontFamily, 9),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Margin = Padding.Empty
        };
        var authorBox = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        authorBox.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); authorBox.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        authorBox.Controls.Add(author, 0, 0);
        var lockSettings = new ThemeButton { Text = proximity?.Enabled == true ? "离开锁屏 · 开启" : "离开锁屏 · 关闭", Dock = DockStyle.Fill, Margin = new Padding(20, 0, 0, 0), Enabled = configureProximity != null };
        lockSettings.Click += (_, _) =>
        {
            proximity = configureProximity?.Invoke(proximity);
            lockSettings.Text = proximity?.Enabled == true ? "离开锁屏 · 开启" : "离开锁屏 · 关闭";
        };
        authorBox.Controls.Add(lockSettings, 0, 1);
        hero.Controls.Add(authorBox, 2, 0); hero.SetRowSpan(authorBox, 2);
        root.Controls.Add(hero, 0, 0);
        var deviceHeading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        deviceHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        deviceHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        deviceHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        deviceHeading.Controls.Add(new Label { Text = "输出设备", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        deviceHeading.Controls.Add(outputStatus, 1, 0);
        var switchNow = new ThemeButton { Text = "立即切换", Dock = DockStyle.Fill, Margin = new Padding(12, 6, 0, 6), Enabled = settings != null && switchOutput != null, AccessibleName = "立即循环切换已保存的输出设备" };
        switchNow.Click += (_, _) => switchOutput?.Invoke();
        var hints = new ToolTip();
        hints.SetToolTip(switchNow, "按已保存的设备配置切换；修改设备后请先保存。");
        hints.SetToolTip(showToast, "关闭后仍会切换声音输出，但不显示成功提示；错误仍会提醒。");
        FormClosed += (_, _) => hints.Dispose();
        deviceHeading.Controls.Add(switchNow, 2, 0);
        root.Controls.Add(deviceHeading, 0, 1);
        var deviceCard = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(20), Margin = Padding.Empty };
        var deviceBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.White };
        deviceBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); deviceBody.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        deviceBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); deviceBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cycleHeading.Font = new Font(Font, FontStyle.Bold);
        deviceBody.Controls.Add(cycleHeading, 0, 0);
        deviceBody.Controls.Add(deviceSummary, 0, 1);
        var manageDevices = new ThemeButton { Text = "管理设备", Dock = DockStyle.Top, Height = 42 };
        manageDevices.Click += (_, _) =>
        {
            using var form = new DeviceSelectionForm(knownDevices, selectedDevices);
            if (form.ShowDialog(this) == DialogResult.OK) { selectedDevices = form.Selection; UpdateDeviceSummary(); }
        };
        Shown += (_, _) => { if (selectedDevices.Count == 0) manageDevices.Focus(); else switchNow.Focus(); };
        deviceBody.Controls.Add(manageDevices, 1, 1); deviceCard.Controls.Add(deviceBody); root.Controls.Add(deviceCard, 0, 2);
        var optionSurface = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(20), Margin = new Padding(0, 16, 0, 0) };
        var options = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = Padding.Empty, ColumnCount = 2, RowCount = 4, Margin = Padding.Empty };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        options.Controls.Add(new Label { Text = "偏好设置", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        options.Controls.Add(showToast, 1, 0);
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
        var monitorRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        var style = new ThemeButton { Text = "样式", Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
        style.Click += (_, _) =>
        {
            using var form = new ToastStyleForm((toastMonitor.SelectedItem as Choice)?.Id ?? "", toastCorner, toastDuration);
            if (form.ShowDialog(this) == DialogResult.OK) { toastCorner = form.Corner; toastDuration = form.Duration; }
        };
        var previewToast = new ThemeButton { Text = "预览", Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
        SwitchToast? sampleToast = null;
        previewToast.Click += (_, _) =>
        {
            sampleToast?.Close(); sampleToast?.Dispose();
            sampleToast = new SwitchToast("提示预览", "切换成功后会在这里提示", (toastMonitor.SelectedItem as Choice)?.Id ?? "", toastDuration, toastCorner);
            sampleToast.Show();
        };
        FormClosed += (_, _) => { sampleToast?.Close(); sampleToast?.Dispose(); };
        monitorRow.Controls.Add(toastMonitor, 0, 0); monitorRow.Controls.Add(style, 1, 0); monitorRow.Controls.Add(previewToast, 2, 0);
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
        var diagnostics = ActionButton("诊断信息", false); diagnostics.Enabled = showDiagnostics != null;
        diagnostics.Click += (_, _) => showDiagnostics?.Invoke();
        footer.Controls.AddRange([save, cancel, refresh, update, diagnostics]); root.Controls.Add(footer, 0, 5);
        Controls.Add(root); root.BringToFront(); AcceptButton = save; CancelButton = cancel;
        knownDevices = devices.ToList();
        UpdateDeviceSummary();
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
            try { UpdateDevices(AudioDevices.List(), AudioDevices.Default()); FillMonitors((toastMonitor.SelectedItem as Choice)?.Id ?? ""); }
            catch (Exception ex) { AppDialog.Show(this, ex.Message, "刷新失败"); }
        };
        save.Click += (_, _) =>
        {
            if (selectedDevices.Count == 0) { AppDialog.Show(this, "请先在管理设备中选择至少一台输出设备。", "尚未选择设备"); return; }
            var candidate = new Settings(selectedDevices[0].Id, selectedDevices.ElementAtOrDefault(1)?.Id ?? selectedDevices[0].Id, modifiers, hotkey, autoUpdate.Checked, (toastMonitor.SelectedItem as Choice)?.Id ?? "", showToast.Checked, toastCorner, toastDuration, selectedDevices.ToList(), proximity);
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
    private Draft CurrentDraft() => new(System.Text.Json.JsonSerializer.Serialize(selectedDevices.Select(d => d.Id)),
        modifiers, hotkey, startup.Checked, autoUpdate.Checked, (toastMonitor.SelectedItem as Choice)?.Id ?? "", showToast.Checked, toastCorner, toastDuration, System.Text.Json.JsonSerializer.Serialize(proximity));
    private sealed record Draft(string Devices, uint Modifiers, Keys Key, bool Startup, bool AutoUpdate, string Monitor, bool ShowToast, string Corner, int Duration, string Proximity);

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
    private static ThemeButton ActionButton(string text, bool primary)
    {
        return new ThemeButton { Text = text, Primary = primary, Size = new Size(132, 46), Font = new Font("Microsoft YaHei UI", 10, primary ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(12, 8, 0, 0) };
    }
    public void UpdateDevices(List<OutputDevice> devices, string? current)
    {
        knownDevices = devices.ToList();
        selectedDevices = selectedDevices.Select(d => devices.FirstOrDefault(online => online.Id == d.Id) ?? d).ToList();
        outputStatus.Text = "当前输出 · " + (devices.FirstOrDefault(d => d.Id == current)?.Name ?? "无可用输出");
        UpdateDeviceSummary();
    }
    private void UpdateDeviceSummary()
    {
        cycleHeading.Text = $"循环列表 · {selectedDevices.Count} 台设备";
        deviceSummary.UpdateSummary(selectedDevices, knownDevices);
    }
    private sealed record Choice(string Id, string Label) { public override string ToString() => Label; }
}
