using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;

namespace AudioSwitch;
internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string Sid
    {
        get { using var identity = WindowsIdentity.GetCurrent(); return identity.User?.Value ?? throw new InvalidOperationException("无法读取当前用户。"); }
    }
    private static string TaskName => "AudioSwitch-Logon-" + Sid;
    private static bool Missing(COMException error) => error.HResult == unchecked((int)0x80070002);
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    private static T WithFolder<T>(Func<dynamic, T> action)
    {
        object? service = null, folder = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            ((dynamic)service!).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            return action((dynamic)folder);
        }
        finally { Release(folder); Release(service); }
    }
    private static bool TaskEnabled() => WithFolder<bool>(folder =>
    {
        object? task = null;
        try { task = folder.GetTask(TaskName); return (bool)((dynamic)task).Enabled; }
        catch (COMException ex) when (Missing(ex)) { return false; }
        finally { Release(task); }
    });
    private static bool RunEnabled
    {
        get { using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32); using var key = root.OpenSubKey(RunKey); return key?.GetValue("AudioSwitch") is string; }
    }
    public static bool Enabled
    {
        get { try { if (TaskEnabled()) return true; } catch (COMException) { } return RunEnabled; }
    }
    public static string Describe()
    {
        try { if (TaskEnabled()) return "登录任务 · 当前用户登录触发，无额外延时"; }
        catch (COMException) { }
        return RunEnabled ? "普通启动项 · 系统可能延后调度" : "关闭";
    }
    public static void SetEnabled(bool enabled)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32);
        using var key = root.CreateSubKey(RunKey, true);
        if (!enabled)
        {
            key.DeleteValue("AudioSwitch", false);
            WithFolder<bool>(folder =>
            {
                try { folder.DeleteTask(TaskName, 0); }
                catch (COMException ex) when (Missing(ex)) { }
                return true;
            });
            return;
        }
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("无法获取程序位置。");
        if (!Path.GetFileName(path).Equals("AudioSwitch.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请从已安装的 AudioSwitch.exe 设置自启动。");
        string user = Sid;
        string xml = $"""
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>AudioSwitch 当前用户登录自启动</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="User"><UserId>{SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
              <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings>
              <Actions Context="User"><Exec><Command>{SecurityElement.Escape(path)}</Command><Arguments>--startup</Arguments></Exec></Actions>
            </Task>
            """;
        try
        {
            WithFolder<bool>(folder =>
            {
                object? task = null;
                try { task = folder.RegisterTask(TaskName, xml, 6, user, null, 3, null); return true; }
                finally { Release(task); }
            });
            key.DeleteValue("AudioSwitch", false); // One startup source only after registration succeeded.
        }
        catch (COMException)
        {
            key.SetValue("AudioSwitch", $"\"{path}\" --startup", RegistryValueKind.String);
        }
    }
}
