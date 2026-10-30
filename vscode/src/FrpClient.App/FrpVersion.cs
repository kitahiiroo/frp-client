using System.Text.RegularExpressions;

namespace FrpClient.App;

/// <summary>
/// 表示一个 frp 版本号，例如 0.62.1。
/// frpc --version 可能输出 "0.62.1"，也可能带前缀 v 或后缀 -rc.1，
/// 这里只解析用于比较的主版本号部分，后缀按“同一版本”处理。
/// </summary>
public readonly struct FrpVersion : IComparable<FrpVersion>, IEquatable<FrpVersion>
{
    private static readonly Regex VersionPattern = new(
        @"(?<major>\d+)(?:\.(?<minor>\d+))?(?:\.(?<patch>\d+))?",
        RegexOptions.Compiled);

    public FrpVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>
    /// 从任意包含版本号的文本里解析版本，例如 "0.62.1"、"v0.44.0"、"0.62.0-rc.1"。
    /// </summary>
    public static bool TryParse(string? text, out FrpVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = VersionPattern.Match(text);

        if (!match.Success)
        {
            return false;
        }

        version = new FrpVersion(
            ParseGroup(match, "major"),
            ParseGroup(match, "minor"),
            ParseGroup(match, "patch"));

        return true;
    }

    public bool IsAtLeast(FrpVersion minimum)
    {
        return CompareTo(minimum) >= 0;
    }

    public int CompareTo(FrpVersion other)
    {
        var majorCompare = Major.CompareTo(other.Major);

        if (majorCompare != 0)
        {
            return majorCompare;
        }

        var minorCompare = Minor.CompareTo(other.Minor);

        return minorCompare != 0 ? minorCompare : Patch.CompareTo(other.Patch);
    }

    public bool Equals(FrpVersion other)
    {
        return CompareTo(other) == 0;
    }

    public override bool Equals(object? obj)
    {
        return obj is FrpVersion other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Major, Minor, Patch);
    }

    public override string ToString()
    {
        return $"{Major}.{Minor}.{Patch}";
    }

    public static bool operator >=(FrpVersion left, FrpVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(FrpVersion left, FrpVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >(FrpVersion left, FrpVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(FrpVersion left, FrpVersion right) => left.CompareTo(right) < 0;

    private static int ParseGroup(Match match, string groupName)
    {
        var group = match.Groups[groupName];

        return group.Success && int.TryParse(group.Value, out var value) ? value : 0;
    }
}
