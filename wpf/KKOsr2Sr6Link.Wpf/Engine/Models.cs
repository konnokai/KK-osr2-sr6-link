using System.Collections.Generic;

namespace KKOsr2Sr6Link.Wpf.Engine;

/// <summary>One timeline segment: start frame, mode, and "girl-boy" character pair.</summary>
public sealed class ScenePart
{
    public int Part { get; set; }
    public string LovemakingMode { get; set; } = "normal";
    public string Charas { get; set; } = "";
}

/// <summary>
/// Raw per-character-pair sample sets captured by the plugin, one entry per "chaF…-chaM…"
/// block in the scene .txt. Six modes (default + blowjob/breastsex/handjobL/handjobR), each
/// with six float axis streams. Mirrors Lovemaking_data in mainwindow.h.
/// </summary>
public sealed class LovemakingData
{
    public string CharasName { get; set; } = "";
    public float BodyWidth { get; set; }

    public List<float> Inserts { get; } = new();
    public List<float> Surges { get; } = new();
    public List<float> Sways { get; } = new();
    public List<float> Twists { get; } = new();
    public List<float> Rolls { get; } = new();
    public List<float> Pitchs { get; } = new();

    public List<float> BlowjobInserts { get; } = new();
    public List<float> BlowjobSurges { get; } = new();
    public List<float> BlowjobSways { get; } = new();
    public List<float> BlowjobTwists { get; } = new();
    public List<float> BlowjobRolls { get; } = new();
    public List<float> BlowjobPitchs { get; } = new();

    public List<float> BreastsexInserts { get; } = new();
    public List<float> BreastsexSurges { get; } = new();
    public List<float> BreastsexSways { get; } = new();
    public List<float> BreastsexTwists { get; } = new();
    public List<float> BreastsexRolls { get; } = new();
    public List<float> BreastsexPitchs { get; } = new();

    public List<float> HandjobLInserts { get; } = new();
    public List<float> HandjobLSurges { get; } = new();
    public List<float> HandjobLSways { get; } = new();
    public List<float> HandjobLTwists { get; } = new();
    public List<float> HandjobLRolls { get; } = new();
    public List<float> HandjobLPitchs { get; } = new();

    public List<float> HandjobRInserts { get; } = new();
    public List<float> HandjobRSurges { get; } = new();
    public List<float> HandjobRSways { get; } = new();
    public List<float> HandjobRTwists { get; } = new();
    public List<float> HandjobRRolls { get; } = new();
    public List<float> HandjobRPitchs { get; } = new();
}

/// <summary>
/// A saved per-axis script: 0..999 keyframe values (-1 = no sample) plus the axis range.
/// Mirrors one .sr6script file (config_Lx in the Qt app).
/// </summary>
public sealed class AxisScript
{
    public List<int> Values { get; set; } = new();
    public int MaxValue { get; set; } = 999;
    public int MinValue { get; set; } = 0;
}
