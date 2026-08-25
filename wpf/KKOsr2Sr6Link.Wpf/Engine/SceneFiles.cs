using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KKOsr2Sr6Link.Wpf.Engine;

/// <summary>
/// Reads/writes the scene sidecar files in the exact shapes the Qt app uses, so existing
/// user data keeps working:
///  - .sr6script : { "actions":[int...], "maxvalue":int, "minvalue":int }   (one per axis)
///  - .sr6cfg    : [ { "part":int, "lovemaking mode":str, "charas":str } ]  (scene parts)
///  - .funscript : Funscript v1.0                                            (export only)
/// JSON is emitted with keys in the same alphabetical order Qt's QJsonObject produces.
/// Whitespace/indentation is not guaranteed byte-identical to Qt (it is irrelevant to every
/// consumer, which parses the JSON), but structure and key order match.
/// </summary>
public static class SceneFiles
{
    private static readonly JsonWriterOptions Indented = new() { Indented = true };

    // ---- .sr6script (per axis) ----

    public static AxisScript? LoadSr6Script(string path)
    {
        return TryLoadSr6Script(path, out var script) ? script : null;
    }

    public static bool TryLoadSr6Script(string path, out AxisScript script)
    {
        script = new AxisScript();
        if (!File.Exists(path)) return false;
        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (JsonNode.Parse(text) is not JsonObject root) return false;

            script.MaxValue = root.TryGetPropertyValue("maxvalue", out var mx) ? (mx?.GetValue<int>() ?? 999) : 999;
            script.MinValue = root.TryGetPropertyValue("minvalue", out var mn) ? (mn?.GetValue<int>() ?? 0) : 0;
            if (root.TryGetPropertyValue("actions", out var actions) && actions is JsonArray arr)
            {
                foreach (var v in arr)
                    if (v == null) return false;
                    else script.Values.Add(v.GetValue<int>());
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    public static void SaveSr6Script(string path, AxisScript script)
    {
        using var stream = File.Create(path);
        using var w = new Utf8JsonWriter(stream, Indented);
        w.WriteStartObject();
        w.WriteStartArray("actions");
        foreach (var v in script.Values) w.WriteNumberValue(v);
        w.WriteEndArray();
        w.WriteNumber("maxvalue", script.MaxValue);
        w.WriteNumber("minvalue", script.MinValue);
        w.WriteEndObject();
    }

    // ---- .sr6cfg (scene parts) ----

    public static List<ScenePart> LoadSr6Cfg(string path)
    {
        return TryLoadSr6Cfg(path, out var result) ? result : new List<ScenePart>();
    }

    public static bool TryLoadSr6Cfg(string path, out List<ScenePart> result)
    {
        result = new List<ScenePart>();
        if (!File.Exists(path)) return false;
        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (JsonNode.Parse(text) is not JsonArray arr) return false;
            foreach (var node in arr)
            {
                if (node is not JsonObject obj) return false;
                result.Add(new ScenePart
                {
                    Part = obj.TryGetPropertyValue("part", out var p) ? (p?.GetValue<int>() ?? 0) : 0,
                    LovemakingMode = ModeFromQt(obj.TryGetPropertyValue("lovemaking mode", out var m) ? (m?.GetValue<string>() ?? "") : ""),
                    Charas = obj.TryGetPropertyValue("charas", out var c) ? (c?.GetValue<string>() ?? "") : "",
                });
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    // The Qt app stores handjob modes as "handjob(Detecting girl left/right hand)"; WPF uses the
    // short "handjobL"/"handjobR" internally. Translate at the file boundary so cfgs round-trip both
    // ways. Other modes (normal/blowjob/breastsex) match verbatim and pass through.
    private static string ModeFromQt(string mode) => mode switch
    {
        "handjob(Detecting girl left hand)" => "handjobL",
        "handjob(Detecting girl right hand)" => "handjobR",
        _ => mode,
    };

    private static string ModeToQt(string mode) => mode switch
    {
        "handjobL" => "handjob(Detecting girl left hand)",
        "handjobR" => "handjob(Detecting girl right hand)",
        _ => mode,
    };

    public static void SaveSr6Cfg(string path, IEnumerable<ScenePart> parts)
    {
        using var stream = File.Create(path);
        using var w = new Utf8JsonWriter(stream, Indented);
        w.WriteStartArray();
        foreach (var part in parts)
        {
            w.WriteStartObject();
            w.WriteString("charas", part.Charas);
            w.WriteString("lovemaking mode", ModeToQt(part.LovemakingMode));
            w.WriteNumber("part", part.Part);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    // ---- complete action sets and shared-profile references ----

    public static bool TryLoadActionSet(string stem, out SceneActionSet actionSet, out string error)
    {
        actionSet = null!;
        error = "";
        var axes = new AxisScript[6];
        for (int i = 0; i < 6; i++)
        {
            string path = AxisInfo.Sr6ScriptPath(stem, (Axis)i);
            if (!File.Exists(path)) { error = $"Missing {Path.GetFileName(path)}."; return false; }
            if (!TryLoadSr6Script(path, out axes[i])) { error = $"Malformed {Path.GetFileName(path)}."; return false; }
            if (axes[i].Values.Count == 0) { error = $"Empty {Path.GetFileName(path)}."; return false; }
        }

        string cfgPath = AxisInfo.Sr6CfgPath(stem);
        if (!File.Exists(cfgPath)) { error = $"Missing {Path.GetFileName(cfgPath)}."; return false; }
        if (!TryLoadSr6Cfg(cfgPath, out var parts)) { error = $"Malformed {Path.GetFileName(cfgPath)}."; return false; }

        int length = axes[0].Values.Count;
        if (axes.Any(a => a.Values.Count != length)) { error = "Axis lengths do not match."; return false; }
        actionSet = new SceneActionSet(axes, parts);
        return true;
    }

    public static void SaveActionSet(string stem, IReadOnlyList<AxisScript> axes, IEnumerable<ScenePart> parts)
    {
        if (axes.Count != 6) throw new ArgumentException("A complete action set needs six axes.", nameof(axes));
        string? dir = Path.GetDirectoryName(stem);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        for (int i = 0; i < 6; i++) SaveSr6Script(AxisInfo.Sr6ScriptPath(stem, (Axis)i), axes[i]);
        SaveSr6Cfg(AxisInfo.Sr6CfgPath(stem), parts);
    }

    /// <summary>Copies an optional profile asset without deleting an existing asset when no source exists.</summary>
    public static void CopyFileIfExists(string source, string destination)
    {
        if (!File.Exists(source)) return;
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) return;
        string? dir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(source, destination, true);
    }

    public static bool HasAnyActionSetFiles(string stem)
        => AxisInfo.All.Select(a => AxisInfo.Sr6ScriptPath(stem, a)).Append(AxisInfo.Sr6CfgPath(stem)).Any(File.Exists);

    public static bool HasLegacySceneData(string stem)
        => File.Exists(stem) || HasAnyActionSetFiles(stem);

    public static List<string> ListCompleteProfiles(string gameRoot)
    {
        string dir = AxisInfo.ProfilesDirectory(gameRoot);
        if (!Directory.Exists(dir)) return new List<string>();
        return Directory.EnumerateFiles(dir, "*.sr6cfg", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(k => AxisInfo.IsValidProfileKey(k))
            .Where(k => SceneFiles.TryLoadActionSet(AxisInfo.ProfileStem(gameRoot, k!), out _, out _))
            .OrderByDescending(k => ProfileLastWriteTimeUtc(gameRoot, k!))
            .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
    }

    /// <summary>Uses the newest related asset timestamp as the profile's last edit time.</summary>
    private static DateTime ProfileLastWriteTimeUtc(string gameRoot, string profileKey)
    {
        string stem = AxisInfo.ProfileStem(gameRoot, profileKey);
        return AxisInfo.All.Select(axis => AxisInfo.Sr6ScriptPath(stem, axis))
            .Append(AxisInfo.Sr6CfgPath(stem))
            .Append(AxisInfo.ProfileRawPath(gameRoot, profileKey))
            .Append(AxisInfo.ProfilePreviewPath(gameRoot, profileKey))
            .Where(File.Exists)
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
    }

    public static bool TryLoadSr6Ref(string path, out string profileKey, out bool exists, out string error)
    {
        profileKey = "";
        exists = File.Exists(path);
        error = "";
        if (!exists) return false;
        try
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length == 0 || string.IsNullOrEmpty(lines[0]) || lines.Skip(1).Any(l => l.Length > 0))
            { error = "Reference must contain one profile key."; return false; }
            if (!AxisInfo.TryValidateProfileKey(lines[0], out error)) return false;
            profileKey = lines[0];
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public static void SaveSr6Ref(string path, string profileKey)
    {
        if (!AxisInfo.TryValidateProfileKey(profileKey, out var error))
            throw new ArgumentException(error, nameof(profileKey));
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, profileKey + Environment.NewLine, new UTF8Encoding(false));
    }

    // ---- .funscript export ----

    /// <summary>
    /// Export one axis to Funscript v1.0. at = i*100ms, pos = round(v/999*100) clamped 0..100,
    /// skipping v == -1. duration = ceil((referenceCount-1)*0.1) seconds (Qt uses L0s.size()).
    /// Mirrors convertsr6sToFunscript (mainwindow.cpp:785-841).
    /// </summary>
    public static void ExportFunscript(string path, IReadOnlyList<int> values, int referenceCount)
    {
        int durationSec = referenceCount <= 0 ? 0 : (int)Math.Ceiling((referenceCount - 1) * 0.1);

        using var stream = File.Create(path);
        using var w = new Utf8JsonWriter(stream, Indented);
        w.WriteStartObject();

        w.WriteStartArray("actions");
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] == -1) continue;
            int atMs = i * 100;
            int pos = (int)Math.Round(values[i] / 999.0 * 100.0, MidpointRounding.AwayFromZero);
            pos = Math.Clamp(pos, 0, 100);
            w.WriteStartObject();
            w.WriteNumber("at", atMs);
            w.WriteNumber("pos", pos);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteBoolean("inverted", false);

        w.WriteStartObject("metadata");
        w.WriteStartArray("bookmarks"); w.WriteEndArray();
        w.WriteStartArray("chapters"); w.WriteEndArray();
        w.WriteString("creator", "");
        w.WriteString("description", "");
        w.WriteNumber("duration", durationSec);
        w.WriteString("license", "");
        w.WriteString("notes", "");
        w.WriteStartArray("performers"); w.WriteEndArray();
        w.WriteString("script_url", "");
        w.WriteStartArray("tags"); w.WriteEndArray();
        w.WriteString("title", "");
        w.WriteString("type", "basic");
        w.WriteString("video_url", "");
        w.WriteEndObject();

        w.WriteNumber("range", 100);
        w.WriteString("version", "1.0");

        w.WriteEndObject();
    }
}
