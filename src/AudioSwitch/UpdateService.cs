using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AudioSwitch;
internal sealed class UpdateService : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly CancellationTokenSource lifetime = new();
    private bool busy;
    private readonly Action<string> notify;
    private static string Repository => Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "GitHubRepository")?.Value ?? "";
    public UpdateService(Action<string> notify)
    {
        this.notify = notify;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AudioSwitch/0.2.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    public async Task CheckAsync(bool silent)
    {
        if (busy) return;
        busy = true;
        string? downloadDirectory = null;
        try
        {
            if (!Regex.IsMatch(Repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
            {
                if (!silent) MessageBox.Show("此构建尚未连接 GitHub 更新源。正式发布后将启用在线更新。", "AudioSwitch · 更新");
                return;
            }
            using var response = await http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest", lifetime.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            { if (!silent) MessageBox.Show("还没有可用的正式版本。", "AudioSwitch · 更新"); return; }
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(lifetime.Token));
            var release = document.RootElement;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) throw new InvalidOperationException("发布版本号应为 v主版本.次版本.修订号。");
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            if (new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build)) <= new Version(current.Major, current.Minor, current.Build))
            { if (!silent) MessageBox.Show($"当前已是最新版本 {current.ToString(3)}。", "AudioSwitch · 更新"); return; }
            string runtime = RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => "win-x64", Architecture.Arm64 => "win-arm64", Architecture.X86 => "win-x86", _ => throw new NotSupportedException("不支持此处理器架构。") };
            string filename = $"AudioSwitch-Setup-{runtime}.exe";
            JsonElement asset = release.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == filename);
            if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("新版尚未提供此架构的安装包，请稍后重试。");
            string digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
            if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")) throw new InvalidOperationException("安装包缺少 SHA-256 校验信息，无法自动安装。");
            string url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("安装包地址不属于项目的 GitHub Releases。");
            string notes = release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            if (notes.Length > 1000) notes = notes[..1000] + "…";
            if (silent) { notify($"发现新版本 {latest}，请从托盘菜单选择“检查更新”安装。"); return; }
            if (MessageBox.Show($"发现新版本 {latest}（当前 {current.ToString(3)}）。\n\n{notes}\n\n下载完成后将退出应用并打开安装向导，保留设备设置。现在更新？", "AudioSwitch · 发现更新", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            downloadDirectory = Path.Combine(Path.GetTempPath(), "AudioSwitch-Update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(downloadDirectory);
            string target = Path.Combine(downloadDirectory, filename);
            using var progress = new UpdateProgressForm();
            progress.Show();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, progress.Token);
            using (var download = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation.Token))
            {
                download.EnsureSuccessStatusCode();
                await using var input = await download.Content.ReadAsStreamAsync(cancellation.Token);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                byte[] buffer = new byte[81920]; long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellation.Token)) != 0)
                {
                    total += read;
                    if (total > 300L * 1024 * 1024) throw new InvalidOperationException("安装包超过预期大小。");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation.Token);
                    progress.Report(total, download.Content.Headers.ContentLength);
                }
            }
            cancellation.Token.ThrowIfCancellationRequested();
            await using (var file = File.OpenRead(target))
            {
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellation.Token));
                if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("安装包校验失败，请重新检查更新。");
            }
            progress.Close();
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            downloadDirectory = null; // Keep the installer while Windows executes it; the OS temp cleanup can reclaim it later.
            Application.Exit();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!silent || downloadDirectory != null) MessageBox.Show("更新未完成：" + ex.Message, "AudioSwitch · 更新", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally
        {
            if (downloadDirectory != null) { try { Directory.Delete(downloadDirectory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            busy = false;
        }
    }
    public void Dispose() { lifetime.Cancel(); http.Dispose(); lifetime.Dispose(); }
}

internal sealed class UpdateProgressForm : Form
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly Label status = new() { Dock = DockStyle.Top, Height = 42, Text = "正在下载安装包…" };
    private readonly ProgressBar bar = new() { Dock = DockStyle.Top, Height = 12, Style = ProgressBarStyle.Marquee };
    public CancellationToken Token => cancellation.Token;
    public UpdateProgressForm()
    {
        Text = "AudioSwitch · 下载更新"; ClientSize = new Size(420, 140); Padding = new Padding(22);
        StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(243, 246, 251);
        var cancel = new Button { Text = "取消下载", Dock = DockStyle.Bottom, Height = 32 };
        cancel.Click += (_, _) => Close(); Controls.AddRange([bar, status, cancel]);
        FormClosing += (_, _) => cancellation.Cancel();
    }
    public void Report(long bytes, long? total)
    {
        if (IsDisposed) return;
        status.Text = total > 0 ? $"正在下载 · {bytes / 1048576.0:F1} / {total / 1048576.0:F1} MB" : $"正在下载 · {bytes / 1048576.0:F1} MB";
        if (total > 0) { bar.Style = ProgressBarStyle.Continuous; bar.Value = (int)Math.Clamp(bytes * 100 / total.Value, 0, 100); }
    }
    protected override void Dispose(bool disposing) { if (disposing) cancellation.Dispose(); base.Dispose(disposing); }
}
