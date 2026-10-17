using System.Diagnostics;
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
    private readonly Action<string> notify;
    private Task<JsonElement?>? fetch;
    private bool manualOpen;
    private UpdateProgressForm? activeProgress;
    internal static string Repository => Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "GitHubRepository")?.Value ?? "";
    internal static string CurrentTag => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.3.4-preview.1";
    public UpdateService(Action<string> notify)
    {
        this.notify = notify;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AudioSwitch/" + CurrentTag);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    private async Task<JsonElement?> FetchReleaseAsync()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await http.GetAsync($"https://api.github.com/repos/{Repository}/releases?per_page=20", deadline.Token);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));
        if (!ReleaseVersion.TryParse(CurrentTag, out var current)) throw new InvalidOperationException("当前版本标识无效。");
        return SelectRelease(document.RootElement, current);
    }
    internal static JsonElement? SelectRelease(JsonElement releases, ReleaseVersion current)
    {
        var candidates = releases.EnumerateArray()
            .Where(r => !r.GetProperty("draft").GetBoolean())
            .Where(r => current.IsPreview || !r.GetProperty("prerelease").GetBoolean())
            .Select(r => (Release: r, Parsed: ReleaseVersion.TryParse(r.GetProperty("tag_name").GetString() ?? "", out var v), Version: v))
            .Where(r => r.Parsed).OrderByDescending(r => r.Version).ToList();
        return candidates.Count == 0 ? null : candidates[0].Release.Clone();
    }

    public async Task CheckAsync(bool silent)
    {
        if (manualOpen)
        {
            if (!silent && activeProgress is { IsDisposed: false }) activeProgress.Activate();
            return;
        }
        if (!silent) manualOpen = true;
        UpdateProgressForm? checking = null;
        string? downloadDirectory = null;
        bool userCancelled = false;
        CancellationToken downloadToken = default;
        try
        {
            if (!Regex.IsMatch(Repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) throw new InvalidOperationException("此构建尚未连接 GitHub 更新源。");
            if (!silent)
            {
                checking = new UpdateProgressForm("检查更新", "正在连接 GitHub…");
                activeProgress = checking; checking.Show();
            }
            if (fetch == null || fetch.IsCompleted) fetch = FetchReleaseAsync();
            var releaseResult = await fetch.WaitAsync(checking?.Token ?? lifetime.Token);
            userCancelled = checking?.Token.IsCancellationRequested ?? false;
            checking?.Close(); checking?.Dispose(); checking = null;
            if (!silent) activeProgress = null;
            if (releaseResult == null)
            { if (!silent) AppDialog.Show("更新源中还没有可用版本。你可以稍后再检查。", "暂无更新"); return; }
            var release = releaseResult.Value;
            var tag = release.GetProperty("tag_name").GetString()!;
            _ = ReleaseVersion.TryParse(tag, out var latest);
            _ = ReleaseVersion.TryParse(CurrentTag, out var current);
            if (latest.CompareTo(current) <= 0)
            { if (!silent) AppDialog.Show($"当前版本 {CurrentTag} 已是最新版本。\n\n更新通道：{(current.IsPreview ? "测试版与正式版" : "正式版")}", "已经是最新版本"); return; }
            if (silent) { notify($"发现新版本 {tag}，请从托盘菜单选择“检查更新”安装。"); return; }
            string runtime = RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => "win-x64", Architecture.Arm64 => "win-arm64", Architecture.X86 => "win-x86", _ => throw new NotSupportedException("不支持此处理器架构。") };
            string filename = $"AudioSwitch-Setup-{runtime}.exe";
            var asset = release.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == filename);
            if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("新版尚未提供此架构的安装包，请稍后重试。");
            string digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
            if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")) throw new InvalidOperationException("安装包缺少 SHA-256 校验信息，无法自动安装。");
            if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("安装包地址不属于项目的 GitHub Releases。");
            string notes = release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
            if (notes.Length > 1200) notes = notes[..1200] + "…";
            if (AppDialog.Show($"{CurrentTag}  →  {tag}\n\n{notes}\n\n安装时会退出应用，并保留设备和快捷键设置。", "发现新版本", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            downloadDirectory = Path.Combine(Path.GetTempPath(), "AudioSwitch-Update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(downloadDirectory);
            string target = Path.Combine(downloadDirectory, filename);
            using var progress = new UpdateProgressForm();
            activeProgress = progress; downloadToken = progress.Token; progress.Show();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, downloadToken);
            cancellation.CancelAfter(TimeSpan.FromMinutes(5));
            using (var download = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation.Token))
            {
                download.EnsureSuccessStatusCode();
                await using var input = await download.Content.ReadAsStreamAsync(cancellation.Token);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                byte[] buffer = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, cancellation.Token)) != 0)
                {
                    total += read;
                    if (total > 300L * 1024 * 1024) throw new InvalidOperationException("安装包超过预期大小。");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation.Token);
                    progress.Report(total, download.Content.Headers.ContentLength);
                }
            }
            cancellation.Token.ThrowIfCancellationRequested();
            progress.SetStatus("正在校验安装包…");
            await using (var file = File.OpenRead(target))
            {
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellation.Token));
                if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("安装包校验失败，请重新检查更新。");
            }
            progress.Close();
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            downloadDirectory = null;
            Application.Exit();
        }
        catch (OperationCanceledException)
        {
            userCancelled = checking?.Token.IsCancellationRequested ?? userCancelled;
            checking?.Close();
            var message = CancellationMessage(silent, userCancelled || downloadToken.IsCancellationRequested, lifetime.IsCancellationRequested, downloadDirectory != null);
            if (message != null) AppDialog.Show(message, "更新超时", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            checking?.Close();
            if (!silent || downloadDirectory != null) AppDialog.Show("更新未完成：" + ex.Message, "更新未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            checking?.Dispose();
            if (!silent) activeProgress = null;
            if (downloadDirectory != null) { try { Directory.Delete(downloadDirectory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            if (!silent) manualOpen = false;
        }
    }
    internal static string? CancellationMessage(bool silent, bool userCancelled, bool exiting, bool downloading)
    {
        if (silent || userCancelled || exiting) return null;
        return downloading ? "下载安装包超时，未完成的文件已清理。请检查网络后重新检查更新。" : "连接 GitHub 超时。请检查网络后重试。";
    }
    public void Dispose() { lifetime.Cancel(); http.Dispose(); }
}

internal sealed class UpdateProgressForm : ChromeForm
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly CancellationToken token;
    private readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Text = "正在下载安装包…" };
    private readonly AccentProgress bar = new() { Dock = DockStyle.Top, Height = 8, Style = ProgressBarStyle.Marquee };
    public CancellationToken Token => token;
    public UpdateProgressForm(string title = "下载更新", string initialStatus = "正在下载安装包…")
    {
        token = cancellation.Token;
        Text = "AudioSwitch · " + title; Icon = BrandIcon.Load(); ClientSize = new Size(500, 280);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(243, 246, 251);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 14, 28, 20), ColumnCount = 1, RowCount = 4 };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        body.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 18, FontStyle.Bold), ForeColor = Theme.Ink }, 0, 0);
        status.Text = initialStatus; body.Controls.Add(status, 0, 1); body.Controls.Add(bar, 0, 2);
        var footer = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var cancel = new ThemeButton { Text = "取消", Size = new Size(110, 42) };
        cancel.Click += (_, _) => Close(); footer.Controls.Add(cancel); body.Controls.Add(footer, 0, 3);
        Controls.Add(body); body.BringToFront(); CancelButton = cancel;
        FormClosing += (_, _) => cancellation.Cancel();
    }
    public void SetStatus(string text) => status.Text = text;
    public void Report(long bytes, long? total)
    {
        if (IsDisposed) return;
        status.Text = total > 0 ? $"正在下载 · {bytes / 1048576.0:F1} / {total / 1048576.0:F1} MB" : $"正在下载 · {bytes / 1048576.0:F1} MB";
        if (total > 0) { bar.Style = ProgressBarStyle.Continuous; bar.Value = (int)Math.Clamp(bytes * 100 / total.Value, 0, 100); }
    }
    protected override void Dispose(bool disposing) { if (disposing) cancellation.Dispose(); base.Dispose(disposing); }
}
