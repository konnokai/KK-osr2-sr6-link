using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KKOsr2Sr6Link.Wpf.Engine;

/// <summary>
/// Minimal QSettings-style INI store: [Section] headers, key=value lines.
/// Section and key names are case-sensitive and may contain spaces
/// (e.g. [Intiface Central], "game root"), matching the original Qt config.ini.
/// </summary>
public sealed class IniConfig
{
    private readonly string _path;
    // Section -> (key -> value). Insertion order preserved for stable output.
    private readonly Dictionary<string, Dictionary<string, string>> _data = new();

    public bool FileExisted { get; }

    public IniConfig(string path)
    {
        _path = path;
        FileExisted = File.Exists(path);
        if (FileExisted)
            Load();
    }

    private void Load()
    {
        string? section = null;
        foreach (var raw in File.ReadAllLines(_path, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line.Substring(1, line.Length - 2);
                if (!_data.ContainsKey(section))
                    _data[section] = new Dictionary<string, string>();
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq < 0 || section == null)
                continue;
            var key = line.Substring(0, eq).Trim();
            var value = line.Substring(eq + 1).Trim();
            _data[section][key] = value;
        }
    }

    public string Get(string section, string key, string def = "")
        => _data.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) ? v : def;

    public bool Has(string section, string key)
        => _data.TryGetValue(section, out var s) && s.ContainsKey(key);

    public void Set(string section, string key, string value)
    {
        if (!_data.TryGetValue(section, out var s))
            _data[section] = s = new Dictionary<string, string>();
        s[key] = value;
    }

    public void Save()
    {
        var sb = new StringBuilder();
        foreach (var (section, keys) in _data)
        {
            sb.Append('[').Append(section).Append("]\n");
            foreach (var (key, value) in keys)
                sb.Append(key).Append('=').Append(value).Append('\n');
            sb.Append('\n');
        }
        File.WriteAllText(_path, sb.ToString(), Encoding.UTF8);
    }
}
