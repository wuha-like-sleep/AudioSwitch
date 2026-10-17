using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AudioSwitch;

internal static class LoginComponentInstaller
{
    internal static (Uri Url, string Digest) SelectAsset(JsonElement release, string repository, string tag, string runtime)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("tag_name").GetString() != "v" + tag)
            throw new InvalidOperationException("组件版本与当前应用不一致，请先更新应用。");
        var name = $"AudioSwitch-LoginLab-Setup-{runtime}.exe";
        var asset = release.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == name);
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("此版本尚未提供适合当前 Windows 架构的登录组件，请稍后重试。");
        var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
        if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")) throw new InvalidOperationException("组件缺少 SHA-256 校验信息，已停止安装。");
        if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url) ||
            url.Scheme != "https" || url.Host != "github.com" || url.AbsolutePath != $"/{repository}/releases/download/v{tag}/{name}" || url.Query.Length != 0 || url.Fragment.Length != 0)
            throw new InvalidOperationException("组件下载地址不属于当前项目与版本，已停止安装。");
        return (url, digest[7..]);
    }

    internal static async Task<bool> InstallAsync(Form owner, CancellationToken closing)
    {
        if (!AppDialog.ConfirmAction(owner, "安装登录实验组件", "将下载与当前应用同版本的独立登录组件，并启动安装向导。安装需要 Windows 管理员确认；不会自动绑定手机、保存账户密码或启用自动解锁。无密码微软账户暂不支持自动解锁。", "下载并安装")) return false;
        string? directory = null;
        var launched = false;
        using var progress = new UpdateProgressForm("安装登录组件", "正在获取同版本组件…");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(closing, progress.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        try
        {
            if (!Regex.IsMatch(UpdateService.Repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")) throw new InvalidOperationException("此构建未配置有效的 GitHub 下载源。");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AudioSwitch/" + UpdateService.CurrentTag);
            progress.Show(owner);
            using var response = await http.GetAsync($"https://api.github.com/repos/{UpdateService.Repository}/releases/tags/v{UpdateService.CurrentTag}", token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new InvalidOperationException("当前版本的登录组件尚未发布，请稍后重试或先更新应用。");
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var (url, digest) = SelectAsset(json.RootElement, UpdateService.Repository, UpdateService.CurrentTag, LoginComponentForm.NativeRuntime);
            directory = Path.Combine(Path.GetTempPath(), "AudioSwitch-LoginLab-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, $"AudioSwitch-LoginLab-Setup-{LoginComponentForm.NativeRuntime}.exe");
            using (var download = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                download.EnsureSuccessStatusCode();
                await using var input = await download.Content.ReadAsStreamAsync(token);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                byte[] buffer = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    total += read;
                    if (total > 30L * 1024 * 1024) throw new InvalidOperationException("组件超过预期大小，已停止下载。");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    progress.Report(total, download.Content.Headers.ContentLength);
                }
                if (total == 0 || download.Content.Headers.ContentLength is long length && total != length) throw new IOException("组件下载不完整，请重试。");
            }
            progress.SetStatus("正在校验组件…");
            await using (var file = File.OpenRead(target))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("组件校验失败，已停止安装，请重试。");
            token.ThrowIfCancellationRequested();
            progress.Close();
            using var installer = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Verb = "runas" }) ?? throw new InvalidOperationException("无法启动组件安装向导。");
            launched = true;
            // Once started, let the interactive installer finish even if the settings window closes.
            await installer.WaitForExitAsync();
            if (owner.IsDisposed || closing.IsCancellationRequested) return false;
            if (installer.ExitCode == 2) { AppDialog.Show(owner, "已取消组件安装。", "安装已取消"); return false; }
            if (installer.ExitCode != 0) throw new InvalidOperationException($"安装向导未完成（退出码 {installer.ExitCode}）。请重试；系统 Hello / PIN 仍可使用。");
            AppDialog.Show(owner, "登录组件安装完成。若安装向导要求重启，请先重启电脑。\n\n安装不会自动启用解锁；绑定和本机初始化需单独操作。", "组件安装完成");
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!owner.IsDisposed && !closing.IsCancellationRequested && !progress.Token.IsCancellationRequested)
                AppDialog.Show(owner, "连接或下载超时，未完成文件已清理。请检查网络后重试。", "组件下载超时");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { if (!owner.IsDisposed) AppDialog.Show(owner, "已取消 Windows 管理员确认，未启动安装。", "安装已取消"); }
        catch (Exception ex)
        { if (!owner.IsDisposed && !closing.IsCancellationRequested) AppDialog.Show(owner, ex.Message, "组件安装未完成"); }
        finally
        {
            progress.Close();
            // A launched installer may spawn a child process that still needs its source.
            if (directory != null && !launched) { try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        return false;
    }
}
