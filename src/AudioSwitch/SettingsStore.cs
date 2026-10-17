using System.Text.Json;

namespace AudioSwitch;
internal static class SettingsStore
{
    public static Settings? Read(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) : null;

    public static void Write(string path, Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Verify(string resultPath)
    {
        string path = Path.Combine(Path.GetDirectoryName(resultPath)!, "settings-roundtrip.json");
        var first = new Settings("test-headphones", "test-speakers");
        var second = new Settings("second-headphones", "test-speakers", 7, Keys.F10, false, "DISPLAY2");
        Write(path, first);
        if (Read(path) != first) throw new InvalidOperationException("Initial settings roundtrip failed.");
        Write(path, second);
        if (Read(path) != second) throw new InvalidOperationException("Replacing settings failed.");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bool rejected = false;
            try { Write(path, first); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Locked settings should reject replacement.");
        }
        if (Read(path) != second) throw new InvalidOperationException("Failed save changed the old settings.");
        bool reopened = false;
        using var hotkey = new HotkeyWindow(() => { }, () => reopened = true);
        HotkeyWindow.OpenExistingSettings();
        Application.DoEvents();
        if (!reopened) throw new InvalidOperationException("Opening existing settings failed.");
        Settings? saved = null;
        using (var form = new SettingsForm([], second, (candidate, _) => { saved = candidate; return null; }))
        {
            form.Show();
            form.AcceptButton!.PerformClick();
            if (saved != second || form.Selection != second)
                throw new InvalidOperationException("Saving preferences lost disconnected device selections.");
            form.Close();
        }
        if (!hotkey.TryChange(3, Keys.F11) || !hotkey.TryChange(7, Keys.F10))
            throw new InvalidOperationException("Hotkey registration or replacement failed.");
        bool original = StartupService.Enabled;
        try
        {
            StartupService.SetEnabled(false);
            if (StartupService.Enabled) throw new InvalidOperationException("Disabling startup failed.");
            StartupService.SetEnabled(true);
            if (!StartupService.Enabled) throw new InvalidOperationException("Enabling startup failed.");
        }
        finally { StartupService.SetEnabled(original); }
        if (!ReleaseVersion.TryParse("0.2.0-preview.1", out var oldPreview)
            || !ReleaseVersion.TryParse("0.2.1-preview.1", out var newPreview)
            || !ReleaseVersion.TryParse("0.2.1", out var stable)
            || newPreview.CompareTo(oldPreview) <= 0 || stable.CompareTo(newPreview) <= 0
            || ReleaseVersion.TryParse("not-a-version", out _))
            throw new InvalidOperationException("Update version ordering failed.");
        using var releases = JsonDocument.Parse("""
            [{"draft":false,"prerelease":true,"tag_name":"v0.2.1-preview.1"}]
            """);
        if (UpdateService.SelectRelease(releases.RootElement, oldPreview) == null)
            throw new InvalidOperationException("Preview channel ignored an available preview update.");
        if (UpdateService.SelectRelease(releases.RootElement, stable) != null)
            throw new InvalidOperationException("Stable channel selected a preview update.");
        using (var checking = new UpdateProgressForm("检查更新", "正在连接…"))
        {
            checking.Show(); checking.Close();
            if (!checking.Token.IsCancellationRequested) throw new InvalidOperationException("Closing update progress did not cancel the request.");
        }
        var screens = new[] {
            new MonitorInfo("primary", true, new Rectangle(0, 0, 1920, 1040)),
            new MonitorInfo("left", false, new Rectangle(-2560, -200, 2560, 1400))
        };
        if (ToastPlacement.Select("", screens).Id != "primary" || ToastPlacement.Select("left", screens).Id != "left"
            || ToastPlacement.Select("unplugged", screens).Id != "primary")
            throw new InvalidOperationException("Toast monitor preference or fallback failed.");
        foreach (var monitor in screens)
        {
            var size = new Size(570, 144);
            var point = ToastPlacement.Position(monitor.WorkArea, size, 36);
            if (!monitor.WorkArea.Contains(new Rectangle(point, size)))
                throw new InvalidOperationException("Toast placement escaped the selected screen.");
        }
        File.Delete(path);
    }
}
