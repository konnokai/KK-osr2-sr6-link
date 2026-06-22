using System;
using System.Collections.Generic;

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
}
