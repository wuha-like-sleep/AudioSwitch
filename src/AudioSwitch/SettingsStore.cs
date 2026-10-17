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
        var second = new Settings("second-headphones", "test-speakers", 7, Keys.F10, false);
        Write(path, first);
        if (Read(path) != first) throw new InvalidOperationException("Initial settings roundtrip failed.");
        Write(path, second);
        if (Read(path) != second) throw new InvalidOperationException("Replacing settings failed.");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bool rejected = false;
            try { Write(path, first); } catch (IOException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Locked settings should reject replacement.");
        }
        if (Read(path) != second) throw new InvalidOperationException("Failed save changed the old settings.");
        using var hotkey = new HotkeyWindow(() => { });
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
        File.Delete(path);
    }
}
