namespace AudioSwitch;
internal static class BrandIcon
{
    public static Icon Load()
    {
        using var stream = typeof(BrandIcon).Assembly.GetManifestResourceStream("AudioSwitch.Assets.app.ico");
        if (stream == null) return (Icon)SystemIcons.Application.Clone();
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
