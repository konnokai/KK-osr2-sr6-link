using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KKOsr2Sr6Link.Wpf.Engine;

public sealed class ParsedScene
{
    public bool IsNewVersion { get; set; }
    public List<LovemakingData> Data { get; } = new();
}

/// <summary>
/// Parses the scene .txt streamed/saved by the plugin into per-pair sample sets, and derives the
/// initial 0..999 axis values for a brand-new scene. Mirrors mainwindow.cpp server_read (851-997).
///
/// Field layout per "/"-delimited data line (indices as used by the Qt code):
///   0 insert, 1 surge, 2 sway, 3 twist, 4 roll, 5 pitch, (6,7 unused), 8 bodywidth,
///   9..14  blowjob   (insert, sway, surge, twist, roll, pitch  -- note surge/sway swapped vs normal),
///   15..20 breastsex (insert, surge, sway, twist, roll, pitch),
///   21..26 handjobL  (insert, surge, sway, twist, roll, pitch),
///   27..32 handjobR  (insert, surge, sway, twist, roll, pitch).
/// The blowjob surge/sway swap is reproduced verbatim from the original.
/// </summary>
public static class SceneTxtParser
{
    public static ParsedScene Parse(string text)
    {
        var scene = new ParsedScene();
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        bool firstLine = true;
        foreach (var line in lines)
        {
            if (firstLine)
            {
                firstLine = false;
                if (line == "New") { scene.IsNewVersion = true; continue; }
                // Old-version file: the Qt app shows a warning and does not parse motion data.
                return scene;
            }
            if (!scene.IsNewVersion) continue;
            if (line.Length == 0) continue;

            if (line.Contains("chaF_"))
            {
                scene.Data.Add(new LovemakingData { CharasName = line });
                continue;
            }
            if (scene.Data.Count == 0) continue;

            var f = line.Split('/');
            if (f.Length < 33) continue; // malformed line; skip rather than crash
            var d = scene.Data[^1];

            d.Inserts.Add(F(f[0]));
            d.Surges.Add(F(f[1]));
            d.Sways.Add(F(f[2]));
            d.Twists.Add(F(f[3]));
            d.Rolls.Add(F(f[4]));
            d.Pitchs.Add(F(f[5]));
            d.BodyWidth = F(f[8]);
            d.BlowjobInserts.Add(F(f[9]));
            d.BlowjobSways.Add(F(f[10]));
            d.BlowjobSurges.Add(F(f[11]));
            d.BlowjobTwists.Add(F(f[12]));
            d.BlowjobRolls.Add(F(f[13]));
            d.BlowjobPitchs.Add(F(f[14]));
            d.BreastsexInserts.Add(F(f[15]));
            d.BreastsexSurges.Add(F(f[16]));
            d.BreastsexSways.Add(F(f[17]));
            d.BreastsexTwists.Add(F(f[18]));
            d.BreastsexRolls.Add(F(f[19]));
            d.BreastsexPitchs.Add(F(f[20]));
            d.HandjobLInserts.Add(F(f[21]));
            d.HandjobLSurges.Add(F(f[22]));
            d.HandjobLSways.Add(F(f[23]));
            d.HandjobLTwists.Add(F(f[24]));
            d.HandjobLRolls.Add(F(f[25]));
            d.HandjobLPitchs.Add(F(f[26]));
            d.HandjobRInserts.Add(F(f[27]));
            d.HandjobRSurges.Add(F(f[28]));
            d.HandjobRSways.Add(F(f[29]));
            d.HandjobRTwists.Add(F(f[30]));
            d.HandjobRRolls.Add(F(f[31]));
            d.HandjobRPitchs.Add(F(f[32]));
        }
        return scene;
    }

    /// <summary>
    /// Derive the initial 0..999 keyframe values for each axis from a pair's "normal"-mode samples,
    /// used only when no .sr6script exists yet (the result is saved immediately, then edited).
    ///
    /// NOTE (deliberate deviation): the original (mainwindow.cpp:977-996) divided by an
    /// *uninitialized* `bodywidth` member and appended R0 with a one-frame offset (both bugs). Here we
    /// use the parsed BodyWidth and compute each axis per-frame to the evident intent. Flagged to the
    /// user. ponytail: matches intent, not the original UB; revisit if a captured scene needs the quirk.
    /// </summary>
    public static AxisScript[] ComputeInitialAxes(LovemakingData d)
    {
        var result = new AxisScript[6];
        for (int i = 0; i < 6; i++) result[i] = new AxisScript();

        int n = d.Inserts.Count;
        if (n == 0) return result;

        float insertMax = d.Inserts.Max();
        float insertMin = d.Inserts.Min();
        float surgeOffset = d.Surges.Sum() / d.Surges.Count;
        float swayOffset = d.Sways.Sum() / d.Sways.Count;
        float bodyWidth = d.BodyWidth != 0 ? d.BodyWidth : 1f; // avoid div-by-zero (original read garbage)

        for (int i = 0; i < n; i++)
        {
            int l0;
            if (insertMin == insertMax)
                l0 = 0;
            else
                l0 = (int)((999f / (insertMin - insertMax)) * d.Inserts[i] - (999f / (insertMin - insertMax)) * insertMax);
            result[0].Values.Add(Clamp(l0));

            int l1 = 999 / 2 - (int)((d.Surges[i] - surgeOffset) * 999f / bodyWidth / 2f);
            result[1].Values.Add(Clamp(l1));

            int l2 = 999 / 2 - (int)((d.Sways[i] - swayOffset) * 999f / bodyWidth / 2f);
            result[2].Values.Add(Clamp(l2));

            int r0 = 999 / 2 + (int)(d.Twists[i] * 11.1f);
            result[3].Values.Add(Clamp(r0));

            int r1 = 999 / 2 - (int)(d.Rolls[i] * 11.1f);
            result[4].Values.Add(Clamp(r1));

            int r2 = 999 / 2 + (int)(d.Pitchs[i] * 11.1f / 2f);
            result[5].Values.Add(Clamp(r2));
        }
        return result;
    }

    private static int Clamp(int v) => v < 0 ? 0 : v > 999 ? 999 : v;

    private static float F(string s)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
}
