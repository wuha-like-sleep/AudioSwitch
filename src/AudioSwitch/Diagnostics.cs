using System.Runtime.InteropServices;
using System.Text;

namespace AudioSwitch;

internal static class Diagnostics
{
    public static string Build(Settings? settings, bool hotkeyActive)
    {
        var text = new StringBuilder();
        text.AppendLine("AudioSwitch " + UpdateService.CurrentTag);
        text.AppendLine("系统：" + RuntimeInformation.OSDescription);
        text.AppendLine($"架构：系统 {RuntimeInformation.OSArchitecture} / 程序 {RuntimeInformation.ProcessArchitecture}");
        text.AppendLine("运行环境：" + RuntimeInformation.FrameworkDescription);
        text.AppendLine("快捷键：" + HotkeyWindow.Describe(settings?.HotkeyModifiers ?? 3, settings?.HotkeyKey ?? Keys.F11) + (hotkeyActive ? " · 已注册" : " · 未注册"));
        try { text.AppendLine("自启动：" + StartupService.Describe()); }
        catch (Exception ex) { text.AppendLine("自启动读取失败：" + ex.Message); }
        text.AppendLine(StartupTiming.DescribeLastRun());
        text.AppendLine("切换提示：" + (settings?.ShowSwitchNotifications != false ? "开启" : "关闭"));
        text.AppendLine();
        try
        {
            var devices = AudioDevices.List();
            var current = AudioDevices.Default();
            text.AppendLine("当前输出：" + (devices.FirstOrDefault(d => d.Id == current)?.Name ?? "无可用输出"));
            if (settings != null) foreach (var item in SwitchTarget.Order(settings)) text.AppendLine("循环设备：" + Describe(item.Id, devices));
            text.AppendLine("在线输出设备数：" + devices.Count);
        }
        catch (Exception ex) { text.AppendLine($"音频状态读取失败：{ex.GetType().Name} · 0x{ex.HResult:X8}\n{ex.Message}"); }
        text.AppendLine();
        text.AppendLine("可复制此信息反馈问题，并补充操作步骤。设备名称可能包含自定义名称，请分享前检查；不包含设备标识和个人配置文件。");
        return text.ToString();
    }
    private static string Describe(string? id, List<OutputDevice> devices) => id == null ? "尚未设置" : devices.FirstOrDefault(d => d.Id == id)?.Name ?? "已保存 · 未连接";
}
