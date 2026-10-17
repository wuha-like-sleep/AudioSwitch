using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Security.Principal;
using Windows.Devices.Bluetooth;

namespace AudioSwitch;

internal sealed class LoginComponentForm : ChromeForm
{
    internal const string ProviderId = "{A3AC9286-65AB-4438-B97E-84A4C9A65320}";
    internal static string NativeRuntime => RuntimeInformation.OSArchitecture switch { Architecture.Arm64 => "win-arm64", Architecture.X86 => "win-x86", _ => "win-x64" };
    public LoginComponentForm(string phoneId = "")
    {
        Text = "AudioSwitch · Windows 登录组件"; Icon = BrandIcon.Load();
        Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(243, 246, 251); ForeColor = Theme.Ink;
        ClientSize = new Size(700, 720); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5, AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(new Label { Text = "原生登录组件 · 独立实验版", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        root.Controls.Add(new Label { Text = "绑定手机后，提交密码前需由系统服务现场认证手机。\n保留系统 Windows Hello / PIN，尚未实现手机靠近自动解锁。", ForeColor = Theme.Muted, Dock = DockStyle.Fill }, 0, 1);
        var report = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Theme.Ink };
        var userSid = WindowsIdentity.GetCurrent().User?.Value ?? "";
        void RefreshReport()
        {
            string registration, binding;
            try
            {
                using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
                using var key = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\" + ProviderId);
                using var bound = registry.OpenSubKey(@"SOFTWARE\AudioSwitch\LoginBridge\Bindings\" + userSid);
                registration = key == null ? "未注册实验组件" : "已注册实验组件";
                binding = bound == null ? "当前账户未绑定：保留手动密码实验模式" : "当前账户已绑定：提交前必须通过手机验证";
            }
            catch { registration = "无法读取注册状态"; binding = "无法读取绑定状态"; }
            report.Text = $"系统架构：{NativeRuntime} · {registration}\r\n{binding}\r\n\r\n" +
                "登录桥接服务使用已配对手机的经典蓝牙地址，现场建立要求认证和加密的免提服务连接。登录组件仅接受来自该系统服务、对应本次请求和会话的短时结果。服务或手机不可用时停止提交并清除密码。\r\n\r\n" +
                "实验仅适用于已登录后锁定的同一用户会话。仍需在登录页手动输入微软账户密码，不保存密码；尚未实现无确认自动解锁。系统 Hello / PIN 入口继续保留。无密码微软账户、组织账户、本地账户不支持本实验。\r\n\r\n" +
                $"先安装 AudioSwitch-LoginLab-Setup-{NativeRuntime}.exe，再从手机锁屏设置选择手机进入此页绑定。绑定或解除需要管理员确认，即时生效，不依赖主设置保存。仅绑定已配对的经典蓝牙端点，不会启动配对向导。\r\n\r\n" +
                "已绑定后，系统登录页选择 AudioSwitch 登录实验并提交账户密码时，服务验证目标手机，最长约 8 秒。未绑定保留原手动实验模式。失败时切回系统 PIN / Hello；解除绑定恢复手动实验模式。\r\n\r\n" +
                "真实 iPhone、系统服务蓝牙访问和微软账户锁定会话认证尚未验证。通道认证不能证明人在场或精确距离，也未解决中继风险。后续仍需凭据初始化与受保护存储、自动提交和端到端真机验证。";
        }
        RefreshReport();
        var card = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = Padding.Empty }; card.Controls.Add(report); root.Controls.Add(card, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var bind = Button("绑定所选手机", false); bind.Enabled = phoneId.Length > 0;
        var unbind = Button("解除绑定", false);
        var refresh = Button("刷新状态", false); refresh.Click += (_, _) => RefreshReport();
        async Task Configure(bool remove)
        {
            bind.Enabled = unbind.Enabled = false;
            try
            {
                var programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var helper = Path.Combine(programFiles, "AudioSwitch Login Lab", "AudioSwitch.LoginBridge.exe");
                if (!File.Exists(helper)) throw new InvalidOperationException("请先安装本版本的独立 LoginLab 实验组件，再进行绑定。");
                string address = "";
                if (!remove)
                {
                    using var phone = await BluetoothDevice.FromIdAsync(phoneId);
                    if (IsDisposed) return;
                    if (phone == null) throw new InvalidOperationException("无法打开经典蓝牙端点。请选择已配对手机的经典蓝牙项目。");
                    address = phone.BluetoothAddress.ToString("X12");
                }
                if (IsDisposed) return;
                if (!AppDialog.ConfirmAction(this, remove ? "解除登录手机绑定" : "绑定登录验证手机", remove ? "解除后，AudioSwitch 登录实验恢复手动密码模式。系统 Hello / PIN 不受此设置影响。" : "绑定后，AudioSwitch 登录实验提交密码前必须现场验证这台手机。手机或服务不可用时停止提交。此实验尚未经真实 iPhone 和锁定会话验证，仍需手动输入账户密码。", remove ? "解除绑定" : "绑定手机")) return;
                var start = new ProcessStartInfo(helper) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                start.ArgumentList.Add(remove ? "--unbind" : "--bind"); start.ArgumentList.Add(userSid);
                if (!remove) start.ArgumentList.Add(address);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动管理员绑定工具。");
                await process.WaitForExitAsync();
                if (IsDisposed) return;
                if (process.ExitCode != 0) throw new System.ComponentModel.Win32Exception(process.ExitCode);
                RefreshReport();
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { if (!IsDisposed) AppDialog.Show(this, "已取消管理员确认，绑定未更改。", "操作已取消"); }
            catch (Exception ex) { if (!IsDisposed) AppDialog.Show(this, ex.Message, "无法更新绑定"); }
            finally { if (!IsDisposed) { bind.Enabled = phoneId.Length > 0; unbind.Enabled = true; } }
        }
        bind.Click += async (_, _) => await Configure(false); unbind.Click += async (_, _) => await Configure(true);
        actions.Controls.AddRange([bind, unbind, refresh]); root.Controls.Add(actions, 0, 3);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var close = Button("关闭", false); close.Click += (_, _) => Close();
        var download = Button("获取实验组件", true); download.Click += (_, _) => Open("https://github.com/" + UpdateService.Repository + "/releases/tag/v" + UpdateService.CurrentTag);
        var options = Button("系统登录选项", false); options.Click += (_, _) => Open("ms-settings:signinoptions");
        footer.Controls.AddRange([close, download, options]); root.Controls.Add(footer, 0, 4); Controls.Add(root); root.BringToFront();
        CancelButton = close; Shown += (_, _) => { report.Select(0, 0); close.Focus(); };
    }
    private static ThemeButton Button(string text, bool primary) => new() { Text = text, Primary = primary, Size = new Size(142, 42), Margin = new Padding(10, 8, 0, 0) };
    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { AppDialog.Show(ex.Message, "无法打开页面", icon: MessageBoxIcon.Warning); }
    }
}
