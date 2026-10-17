using System.Runtime.InteropServices;

namespace AudioSwitch;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--verify-audio")
        {
            try { AudioDevices.VerifyInterfaces(); File.WriteAllText(args[1], "Audio interfaces verified."); }
            catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 2 && args[0] == "--verify-settings")
        {
            try { SettingsStore.Verify(args[1]); File.WriteAllText(args[1], "Settings, hotkey and startup checks passed."); }
            catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); Environment.ExitCode = 1; }
            return;
        }
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, "Local\\AudioSwitch.Tray", out bool first);
        if (!first) { if (!args.Contains("--startup")) HotkeyWindow.OpenExistingSettings(); return; }
        if (args.Contains("--preview"))
        {
            using var preview = new SettingsForm(
                [new("preview-headphones", "USB 耳机 · Headphones"), new("preview-speakers", "USB 音箱 · Speakers")],
                new Settings("preview-headphones", "preview-speakers"), checkUpdates: () =>
                {
                    AppDialog.Show($"当前版本 {UpdateService.CurrentTag} 已是最新版本。", "已经是最新版本");
                    return Task.CompletedTask;
                });
            preview.UpdateDevices([new("preview-headphones", "USB 耳机 · Headphones"), new("preview-speakers", "USB 音箱 · Speakers")], "preview-headphones");
            preview.ShowDialog();
            return;
        }
        if (args.Contains("--preview-update"))
        {
            using var preview = new UpdateProgressForm(); preview.Report(24 * 1048576, 48 * 1048576);
            preview.ShowDialog(); return;
        }
        if (args.Contains("--preview-dialog"))
        {
            AppDialog.Show($"当前版本 {UpdateService.CurrentTag} 已是最新版本。\n\n更新通道：测试版与正式版", "已经是最新版本"); return;
        }
        if (args.Contains("--preview-toast"))
        {
            using var preview = new SwitchToast("耳机", "USB 耳机 · Headphones", "", 15000);
            Application.Run(preview); return;
        }
        using var context = new TrayContext(args.Contains("--startup"));
        Application.Run(context);
    }
}

internal sealed record Settings(string Headphones, string Speakers, uint HotkeyModifiers = 3, Keys HotkeyKey = Keys.F11, bool AutoCheckUpdates = true, string ToastMonitor = "");

internal sealed class TrayContext : ApplicationContext
{
    private readonly string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioSwitch", "settings.json");
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly HotkeyWindow hotkey;
    private readonly ToolStripMenuItem switchItem;
    private readonly ToolStripMenuItem startupItem;
    private readonly ToolStripMenuItem outputItem = new("正在读取输出…") { Enabled = false };
    private readonly ToolStripMenuItem headphoneItem;
    private readonly ToolStripMenuItem speakerItem;
    private readonly UpdateService updates;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2500 };
    private Settings? settings;
    private bool configuring;
    private SettingsForm? settingsForm;
    private SwitchToast? toast;
    private string? lastError;
    private string? previousOutput;

    public TrayContext(bool startup)
    {
        tray = new NotifyIcon { Icon = BrandIcon.Load(), Text = "音频切换", ContextMenuStrip = menu, Visible = true };
        updates = new UpdateService(message => Notify(message));
        try
        {
            settings = SettingsStore.Read(configPath);
        }
        catch (Exception ex) { Notify("配置读取失败，请重新选择设备。" + ex.Message, true); }
        hotkey = new HotkeyWindow(Switch, Configure);
        hotkey.TryChange(settings?.HotkeyModifiers ?? 3, settings?.HotkeyKey ?? Keys.F11);
        switchItem = new ToolStripMenuItem("切换耳机 / 音箱", null, (_, _) => Switch());
        menu.Items.Add(outputItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(switchItem);
        headphoneItem = new ToolStripMenuItem("耳机", null, (_, _) => SelectOutput(true));
        speakerItem = new ToolStripMenuItem("音箱", null, (_, _) => SelectOutput(false));
        menu.Items.Add(headphoneItem); menu.Items.Add(speakerItem);
        menu.Items.Add("设备设置…", null, (_, _) => Configure());
        startupItem = new ToolStripMenuItem("登录 Windows 后自动启动", null, (_, _) =>
        {
            try { StartupService.SetEnabled(!StartupService.Enabled); RefreshMenu(); }
            catch (Exception ex) { Notify("无法修改自启动：" + ex.Message, true); }
        });
        menu.Items.Add(startupItem);
        menu.Items.Add("检查更新…", null, async (_, _) => await updates.CheckAsync(false));
        menu.Opening += (_, _) => { RefreshMenu(); RefreshStatus(); };
        RefreshMenu();
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Switch(); };
        timer.Tick += (_, _) => RefreshStatus();
        timer.Start();
        if (!hotkey.Registered) Notify("快捷键注册失败，可能已被其他程序占用。请在设置中更换。", true);
        // Open setup after the WinForms message loop starts.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        SynchronizationContext.Current!.Post(async _ =>
        {
            if (!startup) Configure(); else RefreshStatus();
            if (settings?.AutoCheckUpdates == true) await updates.CheckAsync(true);
        }, null);
    }

    private void RefreshMenu()
    {
        switchItem.Text = "切换耳机 / 音箱（" + HotkeyWindow.Describe(settings?.HotkeyModifiers ?? 3, settings?.HotkeyKey ?? Keys.F11) + "）";
        try { startupItem.Checked = StartupService.Enabled; }
        catch { startupItem.Checked = false; }
    }

    private void Configure()
    {
        if (settingsForm is { IsDisposed: false })
        {
            if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
            settingsForm.Activate(); return;
        }
        configuring = true;
        try
        {
            using var form = new SettingsForm(AudioDevices.List(), settings, SaveSettings, () => updates.CheckAsync(false));
            settingsForm = form;
            RefreshStatus();
            form.ShowDialog();
        }
        catch (Exception ex) { AppDialog.Show("读取音频设备失败：" + ex.Message, "无法打开设置"); }
        finally { settingsForm = null; configuring = false; }
    }

    private string? SaveSettings(Settings candidate, bool startWithWindows)
    {
        uint oldModifiers = settings?.HotkeyModifiers ?? 3;
        Keys oldKey = settings?.HotkeyKey ?? Keys.F11;
        try
        {
            bool oldStartup = StartupService.Enabled;
            if (!hotkey.TryChange(candidate.HotkeyModifiers, candidate.HotkeyKey))
                return "该快捷键被其他程序占用，请换一个组合。你刚才的选择已保留。";
            try
            {
                if (startWithWindows != oldStartup) StartupService.SetEnabled(startWithWindows);
                SettingsStore.Write(configPath, candidate);
            }
            catch
            {
                hotkey.TryChange(oldModifiers, oldKey);
                if (StartupService.Enabled != oldStartup) StartupService.SetEnabled(oldStartup);
                throw;
            }
            settings = candidate;
            RefreshMenu(); RefreshStatus();
            Notify("设置已保存。按 " + HotkeyWindow.Describe(candidate.HotkeyModifiers, candidate.HotkeyKey) + " 即可切换。");
            return null;
        }
        catch (Exception ex) { return $"保存设置失败：{ex.Message}\r\n错误码：0x{ex.HResult:X8}\r\n你的选择已保留，可以修改后重试。"; }
    }

    private void SelectOutput(bool headphones)
    {
        if (settings == null) { Configure(); return; }
        Apply(headphones ? settings.Headphones : settings.Speakers);
    }

    private void Switch()
    {
        if (configuring) return;
        if (settings == null) { Configure(); return; }
        try
        {
            var devices = AudioDevices.List();
            var target = SwitchTarget.Choose(settings, AudioDevices.Default(), devices.Select(d => d.Id));
            if (target == null) { Notify("耳机和音箱都未连接，请连接设备后重试。", true); return; }
            Apply(target);
        }
        catch (Exception ex) { Notify("切换失败：" + ex.Message, true); }
    }

    private void Apply(string id)
    {
        try
        {
            var device = AudioDevices.List().FirstOrDefault(d => d.Id == id);
            if (device == null) { Notify("目标设备未连接。请插入 USB 设备，或在设置中重新选择。", true); return; }
            if (AudioDevices.Default() == id)
            {
                ShowSwitchToast("正在使用" + (id == settings?.Headphones ? "耳机" : "音箱"), device.Name);
                return;
            }
            AudioDevices.SetDefault(id);
            RefreshStatus();
            ShowSwitchToast(id == settings?.Headphones ? "耳机" : "音箱", device.Name);
        }
        catch (Exception ex) { Notify("切换未完成，部分默认输出可能已经改变。请检查 Windows 声音设置。\n" + ex.Message, true); }
    }

    private void ShowSwitchToast(string title, string name)
    {
        toast?.Close(); toast?.Dispose();
        toast = new SwitchToast(title, name, settings?.ToastMonitor ?? "");
        toast.Show();
    }

    private void RefreshStatus()
    {
        try
        {
            var devices = AudioDevices.List();
            var current = AudioDevices.Default();
            var name = devices.FirstOrDefault(d => d.Id == current)?.Name ?? "无输出设备";
            outputItem.Text = "当前输出：" + name;
            UpdateOutputItem(headphoneItem, "耳机", settings?.Headphones, devices, current);
            UpdateOutputItem(speakerItem, "音箱", settings?.Speakers, devices, current);
            switchItem.Enabled = settings == null || headphoneItem.Enabled || speakerItem.Enabled;
            settingsForm?.UpdateDevices(devices, current);
            string text = "当前：" + name;
            tray.Text = text.Length > 63 ? text[..60] + "…" : text;
            // If the current configured endpoint disappeared, restore the other configured endpoint.
            if (settings != null && previousOutput != null
                && (previousOutput == settings.Headphones || previousOutput == settings.Speakers)
                && !devices.Any(d => d.Id == previousOutput)
                && current != settings.Headphones && current != settings.Speakers)
            {
                bool h = devices.Any(d => d.Id == settings.Headphones);
                bool s = devices.Any(d => d.Id == settings.Speakers);
                if (h != s) AudioDevices.SetDefault(h ? settings.Headphones : settings.Speakers);
            }
            previousOutput = current;
            lastError = null;
        }
        catch (Exception ex)
        {
            tray.Text = "音频切换：无法读取音频状态";
            outputItem.Text = "暂时无法读取音频状态";
            headphoneItem.Enabled = false; speakerItem.Enabled = false;
            if (lastError != ex.Message) { Notify("音频状态读取失败：" + ex.Message, true); lastError = ex.Message; }
        }
    }

    private static void UpdateOutputItem(ToolStripMenuItem item, string label, string? id, List<OutputDevice> devices, string? current)
    {
        var device = devices.FirstOrDefault(d => d.Id == id);
        item.Text = label + (id == null ? " · 尚未设置" : device == null ? " · 未连接" : " · " + device.Name);
        item.Enabled = device != null;
        item.Checked = id != null && id == current;
    }

    private void Notify(string message, bool error = false) => tray.ShowBalloonTip(3500, "音频切换", message, error ? ToolTipIcon.Warning : ToolTipIcon.Info);

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Stop(); timer.Dispose(); toast?.Dispose(); updates.Dispose(); hotkey.Dispose(); tray.Visible = false; tray.Dispose(); menu.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly Action callback;
    private readonly Action openSettings;
    private const int OpenSettingsMessage = 0x8001;
    private int activeId;
    private uint modifiers;
    private Keys key;
    public bool Registered => activeId != 0;
    public HotkeyWindow(Action callback, Action? openSettings = null)
    {
        this.callback = callback;
        this.openSettings = openSettings ?? (() => { });
        CreateHandle(new CreateParams { Caption = "AudioSwitch.Hotkey", Parent = new IntPtr(-3) });
    }
    public bool TryChange(uint newModifiers, Keys newKey)
    {
        if (Registered && modifiers == newModifiers && key == newKey) return true;
        if (!Valid(newModifiers, newKey)) return false;
        int next = activeId == 1 ? 2 : 1;
        if (!RegisterHotKey(Handle, next, newModifiers | 0x4000, (uint)newKey)) return false;
        if (Registered) UnregisterHotKey(Handle, activeId);
        activeId = next; modifiers = newModifiers; key = newKey;
        return true;
    }
    public static bool Valid(uint mods, Keys key) => (mods & ~7u) == 0 && (mods & 3) != 0
        && key != Keys.F12 && ((key >= Keys.A && key <= Keys.Z) || (key >= Keys.D0 && key <= Keys.D9) || (key >= Keys.F1 && key <= Keys.F11));
    public static string Describe(uint mods, Keys key) => ((mods & 2) != 0 ? "Ctrl + " : "") + ((mods & 1) != 0 ? "Alt + " : "") + ((mods & 4) != 0 ? "Shift + " : "") + key;
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam.ToInt32() == activeId) callback();
        if (m.Msg == OpenSettingsMessage) openSettings();
        base.WndProc(ref m);
    }
    public static void OpenExistingSettings()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var window = FindWindowEx(new IntPtr(-3), IntPtr.Zero, null, "AudioSwitch.Hotkey");
            if (window != IntPtr.Zero) { PostMessage(window, OpenSettingsMessage, IntPtr.Zero, IntPtr.Zero); return; }
            Thread.Sleep(100);
        }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string title);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    public void Dispose() { if (Registered) UnregisterHotKey(Handle, activeId); DestroyHandle(); }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
