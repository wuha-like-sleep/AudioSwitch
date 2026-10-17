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
        ClientSize = new Size(700, 780); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 6, AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(new Label { Text = "原生登录组件 · 独立实验版", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        root.Controls.Add(new Label { Text = "本机初始化后，可在手机恢复认证时尝试自动提交。\n默认关闭；保留系统 Windows Hello / PIN，真机成功解锁仍待验证。", ForeColor = Theme.Muted, Dock = DockStyle.Fill }, 0, 1);
        var report = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Theme.Ink };
        var userSid = WindowsIdentity.GetCurrent().User?.Value ?? "";
        void RefreshReport()
        {
            string registration, binding, automatic, componentVersion = "未知", diagnostics = "";
            try
            {
                using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);
                using var key = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\Credential Providers\" + ProviderId);
                using var bound = registry.OpenSubKey(@"SOFTWARE\AudioSwitch\LoginBridge\Bindings\" + userSid);
                foreach (var stage in new[] { "Enrollment", "Phone", "ServiceProbe", "Commit", "Submission", "WindowsResult" })
                {
                    if (bound?.GetValue(stage) is not byte[] data || data.Length != 24 || BitConverter.ToUInt32(data, 0) != 1) continue;
                    var result = BitConverter.ToUInt32(data, 4); var error = BitConverter.ToUInt32(data, 8); var substatus = BitConverter.ToUInt32(data, 12);
                    string time;
                    try { time = DateTime.FromFileTimeUtc(BitConverter.ToInt64(data, 16)).ToLocalTime().ToString("MM-dd HH:mm:ss"); } catch { continue; }
                    var state = stage switch
                    {
                        "Enrollment" => result == 6 ? "账户密码及 SID 验证通过，已保存" : "初始化失败：" + ExplainError(error),
                        "Phone" or "ServiceProbe" when error != 0 => (stage == "ServiceProbe" ? "系统服务检测：" : "锁屏手机认证：") + PhoneFailure(error, substatus),
                        "ServiceProbe" => result == 1 ? "系统服务检测：认证加密链路通过（未提交凭据）" : "系统服务检测未通过，请确认同版本组件、绑定和服务状态",
                        "Phone" => result switch { 1 => "认证链路通过", 3 => "手机认证链路不可用：检查蓝牙、配对及 iPhone 免提服务", 5 => "手机已连接，等待锁屏后至少 12 秒的离开再返回", 7 => "本次锁屏已经尝试，等待重新锁屏", 4 => "会话或绑定发生变化，已停止", 2 => "未找到有效手机绑定", _ => "自动提交未启用或请求被拒绝" },
                        "Commit" => result == 1 ? "单次手机证明已消费" : "手机证明过期、会话变化或已使用，未允许提交",
                        "Submission" => "凭据已交给 Windows；不代表认证或解锁成功",
                        _ => result == 1 ? "Windows 返回认证通过；仍需现场确认桌面解锁" : "Windows 拒绝认证，已撤销自动凭据"
                    };
                    diagnostics += $"{time} · {state} [错误 {error} / 0x{error:X8}；{(stage is "Phone" or "ServiceProbe" ? "蓝牙阶段" : "子状态")} 0x{substatus:X8}]\r\n";
                }
                componentVersion = LoginComponentInstaller.InstalledVersion;
                registration = key == null ? "未注册实验组件" : "已注册实验组件";
                automatic = bound?.GetValue("Automatic") is int value && value == 1 ? "自动提交已配置（不代表已成功解锁）" : "自动提交未配置或已停用";
                binding = bound == null ? "当前账户未绑定：保留手动密码实验模式" : "当前账户已绑定：提交前必须通过手机验证";
            }
            catch { registration = "无法读取注册状态"; binding = "无法读取绑定状态"; automatic = "自动状态未知"; }
            report.Text = $"系统架构：{NativeRuntime} · {registration}\r\n主程序：{UpdateService.CurrentTag}；磁盘组件：{componentVersion}\r\n{binding}\r\n{automatic}\r\n\r\n最近诊断（按阶段显示，时间用于核对是否同一次测试）：\r\n{(diagnostics.Length == 0 ? "尚无记录；不代表测试成功。\r\n" : diagnostics)}\r\n" +
                "登录桥接服务使用已配对手机的经典蓝牙地址，现场建立要求认证和加密的免提服务连接。登录组件仅接受来自该系统服务、对应本次请求和会话的短时结果。服务或手机不可用时停止提交并清除密码。\r\n\r\n" +
                "自动提交只用于同一用户已登录后锁定的会话。初始化需在本机 Windows 凭据窗口输入微软账户邮箱和账户密码，经服务验证 SID 后，使用机器 DPAPI 加密并限制为 SYSTEM / 管理员访问。管理员仍有能力访问凭据，不保存 PIN。\r\n\r\n" +
                "默认关闭：先绑定手机，再点击“初始化并启用”。无可用账户密码时不能初始化；不支持无密码微软账户、组织账户或本地账户。Hello / PIN 继续使用系统原有入口。\r\n\r\n" +
                "锁屏后先连续至少 3 次验证不可用、跨越至少 12 秒，再恢复认证连接才触发。一次锁屏最多自动提交一次；自动认证失败会清除初始化凭据，需重新初始化。不会开机自动登录。\r\n\r\n" +
                "“停用并清除”会撤销并删除本机加密凭据；重新绑定/解除手机绑定或卸载实验组件也会清理。绑定和初始化即时生效，不依赖主设置保存。\r\n\r\n" +
                $"可选安装包：AudioSwitch-LoginLab-Setup-{NativeRuntime}.exe。需要管理员权限，固定在 Program Files。真实 iPhone、LocalSystem 蓝牙访问和微软账户锁屏自动解锁尚未验证。蓝牙认证不能证明人在场、精确距离或防止中继。";
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
                if (!remove) LoginComponentInstaller.RequireCurrentVersion();
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
        var copy = Button("复制诊断", false); copy.Width = 105; copy.Click += (_, _) => { RefreshReport(); try { Clipboard.SetText(report.Text); } catch (Exception ex) { AppDialog.Show(this, ex.Message, "无法复制诊断"); } };
        actions.Controls.AddRange([bind, unbind, refresh, copy]); root.Controls.Add(actions, 0, 3);
        var autoActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var initialize = Button("初始化并启用", true); var revoke = Button("停用并清除", false); var probe = Button("检测系统服务", false);
        async Task ConfigureAutomatic(bool clear)
        {
            initialize.Enabled = revoke.Enabled = false;
            try
            {
                var programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var helper = Path.Combine(programFiles, "AudioSwitch Login Lab", "AudioSwitch.LoginBridge.exe");
                if (!File.Exists(helper)) throw new InvalidOperationException("请先安装本版本 LoginLab 实验组件。");
                if (!clear) LoginComponentInstaller.RequireCurrentVersion();
                if (!AppDialog.ConfirmAction(this, clear ? "停用自动提交" : "初始化本机凭据", clear ? "将停用自动提交并删除本机加密凭据，保留手机绑定和系统 Hello / PIN。" : "将通过 Windows 本机窗口收集微软账户邮箱和账户密码（不是 PIN），由系统服务验证并加密保存。管理员可访问。自动解锁尚未经真机验证；请在有恢复手段的测试电脑上启用。", clear ? "停用并清除" : "继续初始化")) return;
                var start = new ProcessStartInfo(helper) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                start.ArgumentList.Add(clear ? "--revoke" : "--enroll"); start.ArgumentList.Add(userSid);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动本机初始化工具。");
                await process.WaitForExitAsync(); if (IsDisposed) return;
                if (process.ExitCode != 0) throw new System.ComponentModel.Win32Exception(process.ExitCode);
                RefreshReport();
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { if (!IsDisposed) AppDialog.Show(this, "初始化或管理员确认已取消。", "操作已取消"); }
            catch (Exception ex) { if (!IsDisposed) AppDialog.Show(this, (ex is System.ComponentModel.Win32Exception native ? ExplainError((uint)native.NativeErrorCode) + $"（错误 {native.NativeErrorCode}）\n" : "") + ex.Message + "\n\n未能启用时请继续使用系统 Hello / PIN。", "本机凭据操作未完成"); }
            finally { if (!IsDisposed) initialize.Enabled = revoke.Enabled = true; }
        }
        initialize.Click += async (_, _) => await ConfigureAutomatic(false); revoke.Click += async (_, _) => await ConfigureAutomatic(true);
        autoActions.Controls.AddRange([initialize, revoke, probe]); root.Controls.Add(autoActions, 0, 4);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var close = Button("关闭", false); close.Click += (_, _) => Close();
        var closing = new CancellationTokenSource();
        FormClosing += (_, _) => closing.Cancel();
        var download = Button("下载并安装组件", true);
        download.Click += async (_, _) =>
        {
            download.Enabled = probe.Enabled = bind.Enabled = unbind.Enabled = initialize.Enabled = revoke.Enabled = false;
            try { await LoginComponentInstaller.InstallAsync(this, closing.Token); if (!IsDisposed) RefreshReport(); }
            finally { if (!IsDisposed) { download.Enabled = probe.Enabled = unbind.Enabled = initialize.Enabled = revoke.Enabled = true; bind.Enabled = phoneId.Length > 0; } }
        };
        probe.Click += async (_, _) =>
        {
            probe.Enabled = download.Enabled = bind.Enabled = unbind.Enabled = initialize.Enabled = revoke.Enabled = false;
            probe.Text = "正在检测…";
            try
            {
                var programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var helper = Path.Combine(programFiles, "AudioSwitch Login Lab", "AudioSwitch.LoginBridge.exe");
                if (!File.Exists(helper)) throw new InvalidOperationException("请先点击“下载并安装组件”，安装同版本 LoginLab。");
                LoginComponentInstaller.RequireCurrentVersion();
                var start = new ProcessStartInfo(helper) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                start.ArgumentList.Add("--probe-service"); start.ArgumentList.Add(userSid);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动检测工具。");
                await process.WaitForExitAsync(); if (IsDisposed) return;
                RefreshReport();
                if (process.ExitCode != 0) throw new System.ComponentModel.Win32Exception(process.ExitCode);
                AppDialog.Show(this, "系统服务已建立要求认证和加密的蓝牙连接。未保存密码、未启用自动提交、未解锁。请复制诊断核对本次结果。", "服务链路检测通过");
            }
            catch (Exception ex) { if (!IsDisposed) { RefreshReport(); var detail = ex is System.ComponentModel.Win32Exception native ? $"退出码 {native.NativeErrorCode} / 0x{native.NativeErrorCode:X8}：{ex.Message}" : ex.Message; if (ex is System.ComponentModel.Win32Exception invalid && invalid.NativeErrorCode == 87) detail += "\n检测工具拒绝了命令参数。请求可能尚未进入桥接服务，不能据此判断手机认证失败。请核对组件版本。"; AppDialog.Show(this, detail + "\n\n请复制诊断中的“系统服务检测”或蓝牙阶段记录。若没有新记录，请重新安装同版本组件并按要求重启。", "服务链路检测未完成"); } }
            finally { if (!IsDisposed) { probe.Text = "检测系统服务"; probe.Enabled = download.Enabled = unbind.Enabled = initialize.Enabled = revoke.Enabled = true; bind.Enabled = phoneId.Length > 0; } }
        };
        var options = Button("系统登录选项", false); options.Click += (_, _) => Open("ms-settings:signinoptions");
        footer.Controls.AddRange([close, download, options]); root.Controls.Add(footer, 0, 5); Controls.Add(root); root.BringToFront();
        CancelButton = close; Shown += (_, _) => { report.Select(0, 0); close.Focus(); };
    }
    internal static string PhoneFailure(uint error, uint phase)
    {
        var step = phase switch { 1 => "服务无法访问蓝牙适配器", 2 => "服务未找到此手机的已认证配对记录", 3 => "创建蓝牙套接字失败", 4 => "要求蓝牙认证失败", 5 => "要求蓝牙加密失败", 6 => "设置连接事件失败", 7 => "连接免提服务失败", 8 => "等待免提服务连接失败", 9 => "连接对端校验失败", 10 => "服务停止，检测已取消", _ => "系统服务蓝牙验证失败" };
        var detail = error switch { 10013 => "系统拒绝访问或蓝牙认证失败", 10060 => "连接超时，手机可能未响应", 10048 => "通道被占用", 10064 => "手机拒绝连接或服务不可用", 10022 => "驱动、即插即用或参数错误", 1062 => "桥接服务不可用或版本不匹配", _ => new System.ComponentModel.Win32Exception(unchecked((int)error)).Message };
        return $"{step}：{detail}";
    }
    private static string ExplainError(uint error) => error switch
    {
        0 => "请求被拒绝或未返回详细错误，请查看阶段记录",
        1326 => "Windows 未接受账户密码。PIN 不能代替密码；请先在系统登录选项确认账户有可用密码",
        1317 => "验证所得账户与当前 Windows 用户不一致",
        1315 => "账户名格式不受支持，请输入个人微软账户邮箱",
        1062 => "桥接服务未运行或安全连接失败，请重新安装同版本 LoginLab",
        1168 => "未找到有效手机绑定，请先绑定已配对 iPhone",
        29 => "受保护凭据存储失败，请检查实验组件安装和权限",
        5 => "系统拒绝访问或会话身份不匹配，请保留 Hello / PIN 并检查组件版本",
        _ => "Windows 返回错误，请保留错误码供诊断；不要发送密码或 PIN"
    };
    private static ThemeButton Button(string text, bool primary) => new() { Text = text, Primary = primary, Size = new Size(142, 42), Margin = new Padding(10, 8, 0, 0) };
    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { AppDialog.Show(ex.Message, "无法打开页面", icon: MessageBoxIcon.Warning); }
    }
}
