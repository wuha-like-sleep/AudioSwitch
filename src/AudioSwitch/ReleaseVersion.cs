using System.Text.RegularExpressions;

namespace AudioSwitch;
internal readonly record struct ReleaseVersion(Version Version, int Preview) : IComparable<ReleaseVersion>
{
    public bool IsPreview => Preview != int.MaxValue;
    public int CompareTo(ReleaseVersion other)
    {
        int result = Version.CompareTo(other.Version);
        return result != 0 ? result : Preview.CompareTo(other.Preview);
    }
    public static bool TryParse(string value, out ReleaseVersion version)
    {
        version = default;
        var match = Regex.Match(value.Split('+')[0], @"^v?(\d+\.\d+\.\d+)(?:-preview\.(\d+))?$", RegexOptions.IgnoreCase);
        if (!match.Success || !System.Version.TryParse(match.Groups[1].Value, out var number)) return false;
        int preview = int.MaxValue;
        if (match.Groups[2].Success && !int.TryParse(match.Groups[2].Value, out preview)) return false;
        version = new ReleaseVersion(number, preview); return true;
    }
}
