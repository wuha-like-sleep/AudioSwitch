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

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    public static void Verify(string resultPath)
    {
        void Trace(string stage) => File.AppendAllText(resultPath + ".trace", stage + Environment.NewLine);
        Trace("Settings verification started");
        string path = Path.Combine(Path.GetDirectoryName(resultPath)!, "settings-roundtrip.json");
        var first = new Settings("test-headphones", "test-speakers");
        var second = new Settings("second-headphones", "test-speakers", 7, Keys.F10, false, "DISPLAY2", false, "top-left", 3000);
        File.WriteAllText(path, """{"Headphones":"legacy-h","Speakers":"legacy-s"}""");
        if (Read(path)?.ShowSwitchNotifications != true)
            throw new InvalidOperationException("Older settings did not keep switch notifications enabled.");
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
        int switchRequests = 0;
        using (var form = new SettingsForm([], second, (candidate, _) => { saved = candidate; return null; }, switchOutput: () => switchRequests++))
        {
            form.Show();
            Descendants(form).OfType<ThemeButton>().First(c => c.Text == "立即切换").PerformClick();
            if (switchRequests != 1) throw new InvalidOperationException("Settings switch action was not invoked.");
            var notificationToggle = Descendants(form).OfType<Toggle>().First(c => c.Text == "显示切换提示");
            if (notificationToggle.Checked) throw new InvalidOperationException("Disabled switch notifications were not restored.");
            notificationToggle.Checked = true;
            if (!form.HasUnsavedChanges) throw new InvalidOperationException("Notification preference change was not detected.");
            notificationToggle.Checked = false;
            form.UpdateDevices([new OutputDevice(second.Headphones, "Reconnected headphones")], second.Headphones);
            if (form.HasUnsavedChanges) throw new InvalidOperationException("Connection refresh incorrectly marked settings as modified.");
            var toggle = Descendants(form).OfType<Toggle>().First(c => c.Text == "启动时检查新版本");
            toggle.Checked = !toggle.Checked;
            if (!form.HasUnsavedChanges) throw new InvalidOperationException("Changed preferences were not detected.");
            using (var dismiss = new System.Windows.Forms.Timer { Interval = 100 })
            {
                dismiss.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.OfType<AppDialog>().FirstOrDefault();
                    if (dialog != null) { dismiss.Stop(); dialog.AcceptButton!.PerformClick(); }
                };
                dismiss.Start();
                Trace("Opening discard confirmation");
                form.Close();
                Trace("Discard confirmation returned");
            }
            if (form.IsDisposed || !form.Visible)
                throw new InvalidOperationException("Default discard confirmation did not preserve the editing window.");
            toggle.Checked = !toggle.Checked;
            if (form.HasUnsavedChanges) throw new InvalidOperationException("Restoring preferences still marked settings as modified.");
            form.AcceptButton!.PerformClick();
            if (saved == null || saved with { Devices = null } != second || form.Selection! with { Devices = null } != second)
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
            foreach (var corner in ToastPlacement.Corners)
            {
                var point = ToastPlacement.Position(monitor.WorkArea, size, 36, corner);
                if (!monitor.WorkArea.Contains(new Rectangle(point, size))) throw new InvalidOperationException("Toast placement escaped the selected screen.");
                if (corner.EndsWith("left") && point.X != monitor.WorkArea.Left + 36
                    || corner.StartsWith("top") && point.Y != monitor.WorkArea.Top + 36)
                    throw new InvalidOperationException("Toast corner selection failed.");
            }
        }
        if (SwitchTarget.Choose(first, first.Headphones, [first.Headphones, first.Speakers]) != first.Speakers
            || SwitchTarget.Choose(first, first.Speakers, [first.Headphones, first.Speakers]) != first.Headphones
            || SwitchTarget.Choose(first, "other-output", [first.Speakers]) != first.Speakers
            || SwitchTarget.Choose(first, first.Headphones, [first.Headphones]) != first.Headphones
            || SwitchTarget.Choose(first, null, []) != null)
            throw new InvalidOperationException("Connected device switch selection failed.");
        Trace("Opening toast style verification");
        using (var style = new ToastStyleForm("", "top-left", 3000))
        {
            style.Show();
            if (style.Corner != "top-left" || style.Duration != 3000) throw new InvalidOperationException("Toast style preferences were not restored.");
            Descendants(style).OfType<ThemeButton>().First(c => c.Text == "右下角").PerformClick();
            if (style.Corner != "bottom-right") throw new InvalidOperationException("Visual corner selection failed.");
            style.Close();
        }
        if (UpdateService.CancellationMessage(false, false, false, true) == null
            || UpdateService.CancellationMessage(false, false, false, false) == null
            || UpdateService.CancellationMessage(false, true, false, true) != null
            || UpdateService.CancellationMessage(false, false, true, true) != null
            || UpdateService.CancellationMessage(true, false, false, false) != null)
            throw new InvalidOperationException("Update timeout and cancellation feedback failed.");
        using (var component = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            draft = false, tag_name = "v0.0.1-preview.1",
            assets = new[] { new { name = "AudioSwitch-LoginLab-Setup-win-arm64.exe", digest = "sha256:" + new string('a', 64), browser_download_url = "https://github.com/test/project/releases/download/v0.0.1-preview.1/AudioSwitch-LoginLab-Setup-win-arm64.exe" } }
        })))
        {
            var asset = LoginComponentInstaller.SelectAsset(component.RootElement, "test/project", "0.0.1-preview.1", "win-arm64");
            if (asset.Digest.Length != 64) throw new InvalidOperationException("Component digest selection failed.");
            foreach (var (repository, tag, runtime) in new[] { ("other/project", "0.0.1-preview.1", "win-arm64"), ("test/project", "0.0.2", "win-arm64"), ("test/project", "0.0.1-preview.1", "win-x64") })
            {
                bool rejected = false;
                try { LoginComponentInstaller.SelectAsset(component.RootElement, repository, tag, runtime); } catch (InvalidOperationException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Mismatched component source/version/architecture was accepted.");
            }
        }
        if (!LoginComponentForm.PhoneFailure(10060, 8).Contains("超时") || !LoginComponentForm.PhoneFailure(10013, 4).Contains("认证"))
            throw new InvalidOperationException("Native service phone error explanation failed.");
        if (!LoginComponentInstaller.VersionMatches("0.3.10-preview.1", "0.3.10-preview.1")
            || LoginComponentInstaller.VersionMatches("0.3.8-preview.1", "0.3.10-preview.1")
            || LoginComponentInstaller.VersionMatches("未知", "0.3.10-preview.1"))
            throw new InvalidOperationException("Login component version mismatch was accepted.");
        Trace("Opening owned window verification");
        using (var parentWindow = new Form())
        using (var childWindow = new Form())
        {
            parentWindow.Show(); childWindow.Show(parentWindow);
            if (AppDialog.ResolveOwner(parentWindow) != childWindow) throw new InvalidOperationException("New dialog did not select the latest owned window.");
            childWindow.Close();
            if (AppDialog.ResolveOwner(parentWindow) != parentWindow) throw new InvalidOperationException("Closed child remained the dialog owner.");
            parentWindow.Close();
        }
        string diagnostic = Diagnostics.Build(second, true);
        if (!diagnostic.Contains(UpdateService.CurrentTag) || diagnostic.Contains(second.Headphones) || diagnostic.Contains(second.Speakers))
            throw new InvalidOperationException("Diagnostics version or device identifier privacy failed.");
        var many = first with { Devices = [new("a", "USB"), new("b", "Bluetooth"), new("c", "HDMI")], Proximity = new(true, "phone", "signal", 60, -85) };
        Write(path, many);
        var restored = Read(path)!;
        if (!SwitchTarget.Order(restored).SequenceEqual(many.Devices!) || restored.Proximity != many.Proximity)
            throw new InvalidOperationException("Multiple devices or proximity settings failed to roundtrip.");
        if (SwitchTarget.Choose(many, "a", ["a", "b", "c"]) != "b"
            || SwitchTarget.Choose(many, "b", ["a", "c"]) != "c"
            || SwitchTarget.Choose(many, "c", ["a", "b", "c"]) != "a"
            || SwitchTarget.Choose(many, "external", ["c"]) != "c")
            throw new InvalidOperationException("Multi-device cycle order or offline skipping failed.");
        using (var chooser = new DeviceSelectionForm(many.Devices!, many.Devices!))
        {
            chooser.Show(); VerifyButtonBounds(chooser); chooser.MoveDevice("c", -1);
            if (!chooser.Selection.Select(d => d.Id).SequenceEqual(["a", "c", "b"])) throw new InvalidOperationException("Visual device reordering failed.");
            chooser.SearchQuery = "HDMI";
            if (!chooser.VisibleIds.SequenceEqual(["c"])) throw new InvalidOperationException("Device search failed.");
            chooser.RemoveVisible();
            if (!chooser.Selection.Select(d => d.Id).SequenceEqual(["a", "b"])) throw new InvalidOperationException("Filtered removal changed other selections.");
            chooser.AddVisibleOnline();
            if (!chooser.Selection.Select(d => d.Id).SequenceEqual(["a", "b", "c"])) throw new InvalidOperationException("Adding filtered online devices lost existing order.");
            chooser.SearchQuery = "not-a-device";
            chooser.RemoveVisible(); chooser.AddVisibleOnline();
            if (chooser.Selection.Count != 3 || chooser.VisibleIds.Count != 0) throw new InvalidOperationException("Empty search results changed device selection.");
            chooser.Close();
        }
        if (SignalCalibration.Recommend([-60, -61, -59, -110, -60]) != -75)
            throw new InvalidOperationException("Median signal calibration did not reject an outlier.");
        bool rejectedCalibration = false;
        try { SignalCalibration.Recommend([-60, -61]); } catch (InvalidOperationException) { rejectedCalibration = true; }
        if (!rejectedCalibration) throw new InvalidOperationException("Calibration accepted too few samples.");
        var legacyProximity = JsonSerializer.Deserialize<ProximitySettings>("""{"Enabled":false,"PhoneId":"phone","SignalThreshold":-75}""");
        if (legacyProximity?.SignalHysteresis != 5) throw new InvalidOperationException("Older signal settings lost their default recovery buffer.");
        VerifyProximity();
        Trace("Opening proximity test window");
        using (var testForm = new ProximityForm(new(false, "test-phone"), () => [new("test-phone", "Phone", true, -60, DateTimeOffset.UtcNow)], () => null))
        {
            testForm.Show(); VerifyButtonBounds(testForm);
            var testButton = Descendants(testForm).OfType<ThemeButton>().First(c => c.Text == "测试判定");
            testButton.PerformClick();
            if (!testForm.IsTesting || !testForm.StatusText.Contains("不会锁屏")) throw new InvalidOperationException("Non-locking proximity test mode failed.");
            testButton.PerformClick();
            if (testForm.IsTesting) throw new InvalidOperationException("Proximity test could not be stopped.");
            testForm.Close();
        }
        using (var presence = new BluetoothPresence())
        {
            presence.Start(); Application.DoEvents(); _ = presence.Snapshot();
            // A runner may have no Bluetooth adapter. An API loading failure must still fail verification.
            if (presence.Failure is ArgumentException or TypeLoadException or FileNotFoundException) throw new InvalidOperationException("Bluetooth API initialization failed.", presence.Failure);
        }
        if (UnlockLinkProbe.IsAuthenticated(Windows.Networking.Sockets.SocketProtectionLevel.PlainSocket)
            || UnlockLinkProbe.IsAuthenticated(Windows.Networking.Sockets.SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication)
            || !UnlockLinkProbe.IsAuthenticated(Windows.Networking.Sockets.SocketProtectionLevel.BluetoothEncryptionWithAuthentication))
            throw new InvalidOperationException("Bluetooth link probe accepted an insufficient protection level.");
        var emptyProbe = UnlockLinkProbe.RunAsync("", CancellationToken.None).GetAwaiter().GetResult();
        if (emptyProbe.AuthenticatedChannel) throw new InvalidOperationException("Missing phone was marked verified.");
        bool probeInvoked = false;
        using (var probeForm = new UnlockProbeForm("test-phone", (id, _) =>
        {
            probeInvoked = id == "test-phone";
            return Task.FromResult(new UnlockProbeResult("测试结果", "模拟结果，仅检查界面，不建立蓝牙连接。"));
        }))
        {
            probeForm.Show(); VerifyButtonBounds(probeForm);
            Descendants(probeForm).OfType<ThemeButton>().First(b => b.Text == "开始检测").PerformClick();
            if (!probeInvoked || !probeForm.Report.Contains("模拟结果")) throw new InvalidOperationException("Unlock link probe UI did not show its result.");
            probeForm.Close();
        }
        bool probeCancelled = false;
        using (var probeForm = new UnlockProbeForm("test-phone", async (_, cancellation) =>
        {
            using var registration = cancellation.Register(() => probeCancelled = true);
            await Task.Delay(Timeout.Infinite, cancellation);
            return new("", "");
        }))
        {
            probeForm.Show();
            Descendants(probeForm).OfType<ThemeButton>().First(b => b.Text == "开始检测").PerformClick();
            probeForm.Close(); Application.DoEvents();
            if (!probeCancelled) throw new InvalidOperationException("Closing link probe did not cancel pending work.");
        }
        File.Delete(path);
    }
    private static void VerifyButtonBounds(Control root)
    {
        Application.DoEvents();
        foreach (var button in Descendants(root).OfType<ThemeButton>())
            if (button.Visible && button.Parent is { } parent && !parent.ClientRectangle.Contains(button.Bounds))
                throw new InvalidOperationException($"Button layout exceeds its container: {button.Text} ({button.Bounds}, {parent.ClientRectangle}).");
    }

    private static void VerifyProximity()
    {
        var guard = new ProximityGuard(); var config = new ProximitySettings(true, "phone", "disconnect", 15);
        var now = DateTimeOffset.UtcNow;
        var connected = new PhoneSnapshot("phone", "Phone", true, -60, now);
        var disconnected = connected with { Connected = false };
        if (guard.Evaluate(config, disconnected, now).ShouldLock) throw new InvalidOperationException("Locked without ever seeing phone connected.");
        guard.Evaluate(config, connected, now);
        if (guard.Evaluate(config, disconnected, now.AddSeconds(1)).Remaining != 15) throw new InvalidOperationException("Disconnect countdown did not start.");
        if (guard.Evaluate(config, connected, now.AddSeconds(5)).Remaining != null) throw new InvalidOperationException("Reconnection did not cancel countdown.");
        guard.Evaluate(config, disconnected, now.AddSeconds(6));
        if (!guard.Evaluate(config, disconnected, now.AddSeconds(21)).ShouldLock || guard.Evaluate(config, disconnected, now.AddSeconds(22)).ShouldLock)
            throw new InvalidOperationException("Disconnect lock or one-shot behavior failed.");
        guard.Reset(); config = config with { Mode = "signal", SignalHysteresis = 10 };
        var buffered = connected with { Signal = -66 };
        if (guard.Evaluate(config, buffered, now).Healthy) throw new InvalidOperationException("Custom signal recovery buffer was ignored.");
        if (!guard.Evaluate(config, connected, now).Healthy) throw new InvalidOperationException("Strong signal failed custom recovery buffer.");
        guard.Evaluate(config, connected with { Signal = -85, SignalAt = now.AddSeconds(1) }, now.AddSeconds(1));
        var bufferState = guard.Evaluate(config, connected with { Signal = -70, SignalAt = now.AddSeconds(5) }, now.AddSeconds(5));
        if (bufferState.Remaining != 11) throw new InvalidOperationException("Recovery buffer cancelled a pending weak-signal countdown too early.");
        if (guard.Evaluate(config, connected with { SignalAt = now.AddSeconds(6) }, now.AddSeconds(6)).Remaining != null)
            throw new InvalidOperationException("Signal recovery failed to cancel countdown.");
        guard.Reset(); config = config with { SignalHysteresis = 5 };
        guard.Evaluate(config, connected, now);
        var weak = connected with { Signal = -85, SignalAt = now.AddSeconds(1) };
        guard.Evaluate(config, weak, now.AddSeconds(1));
        if (guard.Evaluate(config, weak, now.AddSeconds(22)).ShouldLock) throw new InvalidOperationException("Stale signal triggered lock.");
        weak = weak with { SignalAt = now.AddSeconds(23) }; guard.Evaluate(config, weak, now.AddSeconds(23));
        weak = weak with { SignalAt = now.AddSeconds(38) };
        if (!guard.Evaluate(config, weak, now.AddSeconds(38)).ShouldLock) throw new InvalidOperationException("Fresh sustained weak signal did not trigger lock.");
        guard.Reset();
        guard.Evaluate(config, connected with { Connected = false }, now);
        guard.Evaluate(config, weak with { Connected = false, SignalAt = now.AddSeconds(1) }, now.AddSeconds(1));
        if (!guard.Evaluate(config, weak with { Connected = false, SignalAt = now.AddSeconds(16) }, now.AddSeconds(16)).ShouldLock)
            throw new InvalidOperationException("Fresh signal mode incorrectly required an active audio connection.");
        foreach (var mode in new[] { "disconnect", "either" })
        {
            var removedGuard = new ProximityGuard(); var removedConfig = config with { Mode = mode, DelaySeconds = 15 };
            removedGuard.Evaluate(removedConfig, connected, now);
            var removed = BluetoothPresence.Disconnected(connected);
            if (removed.Signal != null || removed.SignalAt != null || removedGuard.Evaluate(removedConfig, removed, now.AddSeconds(1)).Remaining != 15
                || !removedGuard.Evaluate(removedConfig, removed, now.AddSeconds(16)).ShouldLock)
                throw new InvalidOperationException("Removed Bluetooth endpoint did not preserve an armed departure countdown.");
            var neverConnected = new ProximityGuard();
            if (neverConnected.Evaluate(removedConfig, removed, now).Remaining != null || neverConnected.Evaluate(removedConfig, removed, now.AddMinutes(2)).ShouldLock)
                throw new InvalidOperationException("An unarmed removed endpoint triggered locking.");
        }
        guard.Reset(); guard.Evaluate(config, connected, now);
        if (guard.Evaluate(config, null, now.AddSeconds(1)).ShouldLock) throw new InvalidOperationException("Missing device status triggered lock.");
        if (guard.Evaluate(config with { Enabled = false }, disconnected, now.AddSeconds(90)).ShouldLock) throw new InvalidOperationException("Disabled proximity triggered lock.");
    }

}
