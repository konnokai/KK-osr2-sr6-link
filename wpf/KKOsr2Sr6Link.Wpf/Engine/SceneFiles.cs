using System;
using System.Collections.Generic;
using System.IO;
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
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return null;
        var root = JsonNode.Parse(text) as JsonObject;
        if (root == null) return null;

        var script = new AxisScript
        {
            MaxValue = root.TryGetPropertyValue("maxvalue", out var mx) ? (mx?.GetValue<int>() ?? 999) : 999,
            MinValue = root.TryGetPropertyValue("minvalue", out var mn) ? (mn?.GetValue<int>() ?? 0) : 0,
        };
        if (root.TryGetPropertyValue("actions", out var actions) && actions is JsonArray arr)
            foreach (var v in arr)
                script.Values.Add(v?.GetValue<int>() ?? 0);
        return script;
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
        var result = new List<ScenePart>();
        if (!File.Exists(path)) return result;
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return result;
        if (JsonNode.Parse(text) is not JsonArray arr) return result;
        foreach (var node in arr)
        {
            if (node is not JsonObject obj) continue;
            result.Add(new ScenePart
            {
                Part = obj.TryGetPropertyValue("part", out var p) ? (p?.GetValue<int>() ?? 0) : 0,
                LovemakingMode = obj.TryGetPropertyValue("lovemaking mode", out var m) ? (m?.GetValue<string>() ?? "") : "",
                Charas = obj.TryGetPropertyValue("charas", out var c) ? (c?.GetValue<string>() ?? "") : "",
            });
        }
        return result;
    }

    public static void SaveSr6Cfg(string path, IEnumerable<ScenePart> parts)
    {
        using var stream = File.Create(path);
        using var w = new Utf8JsonWriter(stream, Indented);
        w.WriteStartArray();
        foreach (var part in parts)
        {
            w.WriteStartObject();
            w.WriteString("charas", part.Charas);
            w.WriteString("lovemaking mode", part.LovemakingMode);
            w.WriteNumber("part", part.Part);
            w.WriteEndObject();
        }
        w.WriteEndArray();
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
