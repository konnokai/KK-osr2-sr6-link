using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace KKOsr2Sr6Link.Wpf.Engine;

/// <summary>OSR2/SR6 / TCode axes. Order matches the Qt app's L0..R2 indexing (0..5).</summary>
public enum Axis
{
    L0 = 0, // insert (stroke)
    L1 = 1, // surge
    L2 = 2, // sway
    R0 = 3, // twist
    R1 = 4, // roll
    R2 = 5, // pitch
}

public static class AxisInfo
{
    public static readonly Axis[] All = { Axis.L0, Axis.L1, Axis.L2, Axis.R0, Axis.R1, Axis.R2 };

    /// <summary>TCode prefix written to the serial device, e.g. "L0", "R2".</summary>
    public static string Code(this Axis a) => a.ToString();

    /// <summary>
    /// Per-axis filename infix used for the saved .sr6script / exported .funscript files.
    /// L0 has no infix (base file); the rest mirror mainwindow.cpp save_scripter().
    /// </summary>
    public static string Infix(this Axis a) => a switch
    {
        Axis.L0 => "",
        Axis.L1 => ".surge",
        Axis.L2 => ".sway",
        Axis.R0 => ".twist",
        Axis.R1 => ".roll",
        Axis.R2 => ".pitch",
        _ => throw new ArgumentOutOfRangeException(nameof(a)),
    };

    /// <summary>Strip a trailing ".txt" (case-insensitive) to get the scene file stem.</summary>
    public static string SceneStem(string scenePath)
        => scenePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? scenePath[..^4]
            : scenePath;

    public static string Sr6ScriptPath(string scenePath, Axis a) => SceneStem(scenePath) + a.Infix() + ".sr6script";
    public static string FunscriptPath(string scenePath, Axis a) => SceneStem(scenePath) + a.Infix() + ".funscript";
    public static string Sr6CfgPath(string scenePath) => SceneStem(scenePath) + ".sr6cfg";

    public static string Sr6RefPath(string scenePath) => SceneStem(scenePath) + ".sr6ref";

    public static string ProfilesDirectory(string gameRoot)
        => Path.Combine(gameRoot, "UserData", "KK_osr_sr6_link", "_profiles");

    public static bool TryValidateProfileKey(string? profileKey, out string error)
    {
        error = "";
        if (string.IsNullOrEmpty(profileKey)) { error = "Profile key is empty."; return false; }
        if (profileKey == "." || profileKey == "..") { error = "Profile key is reserved."; return false; }
        if (profileKey.IndexOfAny(new[] { '/', '\\', '|', ':' }) >= 0)
        { error = "Profile key contains a forbidden separator."; return false; }
        if (profileKey.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        { error = "Profile key contains an invalid filename character."; return false; }
        return true;
    }

    public static bool IsValidProfileKey(string? profileKey)
        => TryValidateProfileKey(profileKey, out _);

    public static string TimestampProfileKey(DateTime timestamp)
        => timestamp.ToString("yyyy_MMdd_HHmm_ss_fff", CultureInfo.InvariantCulture);

    public static string ProfileStem(string gameRoot, string profileKey)
    {
        if (!TryValidateProfileKey(profileKey, out var error))
            throw new ArgumentException(error, nameof(profileKey));
        return Path.Combine(ProfilesDirectory(gameRoot), profileKey);
    }

    public static string ProfileRawPath(string gameRoot, string profileKey)
        => ProfileStem(gameRoot, profileKey) + ".txt";

    public static string ProfilePreviewPath(string gameRoot, string profileKey)
        => ProfileStem(gameRoot, profileKey) + ".png";
}
