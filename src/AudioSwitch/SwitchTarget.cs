namespace AudioSwitch;

internal static class SwitchTarget
{
    public static List<OutputDevice> Order(Settings settings) => (settings.Devices ?? [new(settings.Headphones, "已保存的设备 1"), new(settings.Speakers, "已保存的设备 2")])
        .Where(d => !string.IsNullOrWhiteSpace(d.Id)).DistinctBy(d => d.Id).ToList();
    public static string? Choose(Settings settings, string? current, IEnumerable<string> connected)
    {
        var order = Order(settings);
        var online = connected.ToHashSet(StringComparer.Ordinal);
        int start = order.FindIndex(d => d.Id == current);
        for (int offset = 1; offset <= order.Count; offset++)
        {
            var candidate = order[(start + offset) % order.Count];
            if (online.Contains(candidate.Id)) return candidate.Id;
        }
        return null;
    }
}
