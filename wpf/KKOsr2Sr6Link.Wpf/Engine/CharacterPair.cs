using System.Text.RegularExpressions;

namespace KKOsr2Sr6Link.Wpf.Engine;

/// <summary>Extracts stable female/male slot roots without splitting character display names.</summary>
public static class CharacterPair
{
    private static readonly Regex FemaleRoot = new(@"chaF_[A-Za-z0-9_]+", RegexOptions.Compiled);
    private static readonly Regex MaleRoot = new(@"chaM_[A-Za-z0-9_]+", RegexOptions.Compiled);

    public static bool TryNormalize(string? value, out string key)
    {
        key = "";
        if (value == null) return false;
        var female = FemaleRoot.Match(value);
        var male = MaleRoot.Match(value);
        if (!female.Success || !male.Success) return false;
        key = female.Value + "-" + male.Value;
        return true;
    }

    public static string Normalize(string? value)
        => TryNormalize(value, out var key) ? key : "";

    public static bool TrySplitLabels(string? value, out string female, out string male)
    {
        female = "";
        male = "";
        if (value == null) return false;
        var femaleRoot = FemaleRoot.Match(value);
        var maleRoot = MaleRoot.Match(value);
        if (!femaleRoot.Success || !maleRoot.Success || maleRoot.Index <= femaleRoot.Index)
            return false;

        int separator = value.IndexOf('-', femaleRoot.Index + femaleRoot.Length);
        if (separator < 0 || separator >= maleRoot.Index) return false;
        female = value[..separator].Trim();
        male = value[(separator + 1)..].Trim();
        return female.Length > 0 && male.Length > 0;
    }
}
