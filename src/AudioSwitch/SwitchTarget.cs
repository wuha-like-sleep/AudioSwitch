namespace AudioSwitch;

internal static class SwitchTarget
{
    public static string? Choose(Settings settings, string? current, IEnumerable<string> connected)
    {
        var online = connected.ToHashSet(StringComparer.Ordinal);
        string preferred = current == settings.Headphones ? settings.Speakers : settings.Headphones;
        string other = preferred == settings.Headphones ? settings.Speakers : settings.Headphones;
        return online.Contains(preferred) ? preferred : online.Contains(other) ? other : null;
    }
}
