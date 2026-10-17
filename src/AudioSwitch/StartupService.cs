using Microsoft.Win32;

namespace AudioSwitch;
internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool Enabled
    {
        get { using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32); using var key = root.OpenSubKey(RunKey); return key?.GetValue("AudioSwitch") is string; }
    }
    public static void SetEnabled(bool enabled)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32);
        using var key = root.CreateSubKey(RunKey, true);
        if (enabled)
        {
            var path = Environment.ProcessPath ?? throw new InvalidOperationException("无法获取程序位置。");
            if (!Path.GetFileName(path).Equals("AudioSwitch.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请从已安装的 AudioSwitch.exe 设置自启动。");
            key.SetValue("AudioSwitch", $"\"{path}\" --startup", RegistryValueKind.String);
        }
        else key.DeleteValue("AudioSwitch", false);
    }
}
