using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AudioSwitch;

// Only timestamps and readiness flags; no device IDs, account names or credentials.
internal sealed class StartupTiming
{
    internal static readonly string ReportPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioSwitch", "startup-timing.json");
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DateTimeOffset processStart;
    private readonly double initialMilliseconds;
    private readonly Dictionary<string, long> stages = new();
    private readonly SemaphoreSlim writer = new(1, 1);
    internal StartupTiming()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            processStart = process.StartTime.ToUniversalTime();
        }
        catch { processStart = DateTimeOffset.UtcNow; }
        initialMilliseconds = Math.Max(0, (DateTimeOffset.UtcNow - processStart).TotalMilliseconds);
        clock.Restart();
        Mark("ManagedEntry");
    }
    internal bool Mark(string stage) => stages.TryAdd(stage, (long)(initialMilliseconds + clock.Elapsed.TotalMilliseconds));
    internal async Task SaveAsync()
    {
        // Capture on the UI thread; serialise disk writes so an older report cannot overwrite a newer one.
        var report = JsonSerializer.Serialize(new { Version = UpdateService.CurrentTag, ProcessStartUtc = processStart,
            StagesMilliseconds = new Dictionary<string, long>(stages) }, new JsonSerializerOptions { WriteIndented = true });
        await writer.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
                File.WriteAllText(ReportPath + ".tmp", report);
                File.Move(ReportPath + ".tmp", ReportPath, true);
            });
        }
        catch { /* Optional diagnostics must not prevent startup. */ }
        finally { writer.Release(); }
    }
    internal static string DescribeLastRun()
    {
        try
        {
            if (!File.Exists(ReportPath)) return "自启动计时：暂无记录（下次登录 Windows 后生成）。";
            using var report = JsonDocument.Parse(File.ReadAllText(ReportPath));
            var root = report.RootElement;
            var result = new StringBuilder("最近自启动：");
            result.AppendLine(root.GetProperty("ProcessStartUtc").GetDateTimeOffset().ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"));
            result.AppendLine("记录版本：" + root.GetProperty("Version").GetString());
            var stages = root.GetProperty("StagesMilliseconds");
            foreach (var (key, label) in new[] { ("ManagedEntry", "进入程序"), ("TrayCreated", "托盘已创建"),
                ("HotkeyReady", "快捷键已注册"), ("HotkeyUnavailable", "快捷键注册失败"), ("MessageLoopReady", "消息循环就绪"),
                ("AudioReady", "首次音频读取完成"), ("AudioFailed", "首次音频读取失败") })
                if (stages.TryGetProperty(key, out var value)) result.AppendLine($"  {label}：{value.GetInt64()} ms");
            if (!stages.TryGetProperty("AudioReady", out _) && !stages.TryGetProperty("AudioFailed", out _))
                result.AppendLine("  首次音频读取尚未完成或程序提前退出。");
            result.Append("计时从进程创建开始，不包含 Windows 拉起程序前的等待；托盘图标实际显示还取决于系统外壳。");
            return result.ToString();
        }
        catch { return "自启动计时：记录暂时无法读取。"; }
    }
}
