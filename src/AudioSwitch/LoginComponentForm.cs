using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AudioSwitch;

internal sealed class LoginComponentForm : ChromeForm
{
    internal const string ProviderId = "{A3AC9286-65AB-4438-B97E-84A4C9A65320}";
    internal static string NativeRuntime => RuntimeInformation.OSArchitecture switch { Architecture.Arm64 => "win-arm64", Architecture.X86 => "win-x86", _ => "win-x64" };
    public LoginComponentForm()
    {
        Text = "AudioSwitch · Windows 登录组件"; Icon = BrandIcon.Load();
        Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(243, 246, 251); ForeColor = Theme.Ink;
        ClientSize = new Size(700, 640); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 4, AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(new Label { Text = "原生登录组件 · 独立实验版", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        root.Controls.Add(new Label { Text = "已实现微软账户密码凭据提交；真实账户登录仍待验证。\n保留系统 Windows Hello / PIN，尚未实现手机靠近自动解锁。", ForeColor = Theme.Muted, Dock = DockStyle.Fill }, 0, 1);
        var report = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Theme.Ink };
        string registration;
        try
        {
            using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
            using var key = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\" + ProviderId);
            registration = key == null ? "未注册实验组件" : "已找到实验组件注册（不代表账户认证通过）";
        }
        catch { registration = "无法读取组件注册状态"; }
        report.Text = $"系统架构：{NativeRuntime} · {registration}\r\n\r\n" +
            "这是独立的 Credential Provider，通过 Windows 登录页手动输入微软账户密码，将受保护凭据提交给系统。密码不保存，提交、取消或失败后清除。\r\n\r\n" +
            "支持范围：具备可用密码的个人微软账户。PIN 不是账户密码；无密码微软账户、组织账户和本地账户不在本次实验范围。Hello / PIN 通过系统原有入口使用，不由本组件代理或重放。\r\n\r\n" +
            $"可选安装包：AudioSwitch-LoginLab-Setup-{NativeRuntime}.exe\r\n需要管理员权限，安装到 Program Files。普通音频切换安装包不注册登录组件。请先在具备恢复手段的测试电脑验证。\r\n\r\n" +
            "安装后从登录页的“登录选项”选择 AudioSwitch 登录实验。失败时切回系统 PIN / Hello。卸载 Windows“已安装的应用”中的 AudioSwitch 登录实验组件，仅移除自身注册，可能需要重启。\r\n\r\n" +
            "后续工作：真实微软账户和锁定会话验证、手机身份协议、受保护后台服务、撤销和自动提交。蓝牙检测通过不能代替这些步骤。";
        var card = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = Padding.Empty }; card.Controls.Add(report); root.Controls.Add(card, 0, 2);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var close = Button("关闭", false); close.Click += (_, _) => Close();
        var download = Button("获取实验组件", true); download.Click += (_, _) => Open("https://github.com/" + UpdateService.Repository + "/releases/tag/v" + UpdateService.CurrentTag);
        var options = Button("系统登录选项", false); options.Click += (_, _) => Open("ms-settings:signinoptions");
        footer.Controls.AddRange([close, download, options]); root.Controls.Add(footer, 0, 3); Controls.Add(root); root.BringToFront();
        CancelButton = close; Shown += (_, _) => { report.Select(0, 0); close.Focus(); };
    }
    private static ThemeButton Button(string text, bool primary) => new() { Text = text, Primary = primary, Size = new Size(142, 42), Margin = new Padding(10, 8, 0, 0) };
    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { AppDialog.Show(ex.Message, "无法打开页面", icon: MessageBoxIcon.Warning); }
    }
}
