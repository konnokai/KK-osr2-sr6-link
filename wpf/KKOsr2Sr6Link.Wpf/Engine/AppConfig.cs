namespace KKOsr2Sr6Link.Wpf.Engine;

/// <summary>
/// Typed view over <see cref="IniConfig"/>, mirroring the original Qt config_init()
/// (mainwindow.cpp:243-327). Seeds the same defaults on first run and exposes the
/// same section/key layout so existing config.ini files keep working.
/// </summary>
public sealed class AppConfig
{
    private readonly IniConfig _ini;

    public const string DefaultPath = "config.ini";

    public AppConfig(string path = DefaultPath)
    {
        _ini = new IniConfig(path);
        if (!_ini.FileExisted)
        {
            // Same defaults the Qt app writes when no config exists.
            _ini.Set("SerialPort", "baudrate", "115200");
            _ini.Set("Server", "Serverip", "127.0.0.1");
            _ini.Set("Server", "Serverport", "8000");
            _ini.Set("Game", "game root", "");
            _ini.Set("Intiface Central", "webserverip", "ws://localhost:12345");
            _ini.Set("Scripter edit", "rebuild all axes", "0");
            _ini.Save();
        }
    }

    public string BaudRate
    {
        get => _ini.Get("SerialPort", "baudrate", "115200");
        set { _ini.Set("SerialPort", "baudrate", value); _ini.Save(); }
    }

    public string ServerIp
    {
        get => _ini.Get("Server", "Serverip", "127.0.0.1");
        set { _ini.Set("Server", "Serverip", value); _ini.Save(); }
    }

    /// <summary>
    /// Server port. The original Qt app had a bug (mainwindow.cpp:288-296,318): it READ from
    /// [Server]/Serverport but WROTE to [SerialPort]/Serverport, so a port change never
    /// persisted. That has since been fixed in the Qt app (write to [Server] too); we mirror
    /// the fix here.
    /// </summary>
    public string ServerPort
    {
        get => _ini.Get("Server", "Serverport", "8000");
        set { _ini.Set("Server", "Serverport", value); _ini.Save(); }
    }

    public string GameRoot
    {
        get => _ini.Get("Game", "game root", "");
        set { _ini.Set("Game", "game root", value); _ini.Save(); }
    }

    public string WebServerIp
    {
        get => _ini.Get("Intiface Central", "webserverip", "ws://localhost:12345");
        set { _ini.Set("Intiface Central", "webserverip", value); _ini.Save(); }
    }

    public bool RebuildAllAxes
    {
        get => _ini.Get("Scripter edit", "rebuild all axes", "0") == "1";
        set { _ini.Set("Scripter edit", "rebuild all axes", value ? "1" : "0"); _ini.Save(); }
    }

    private static string AxisKey(int axis) => ((Axis)axis).ToString(); // 0..5 -> "L0".."R2"

    /// <summary>
    /// Per-axis output range (min/max, 0..999) under [Output Range], keys L0min/L0max … R2max.
    /// Persisted here — NOT in the per-scene .sr6script — so the range is a device/user
    /// preference independent of any scene. Mirrors the Qt save_output_range()/load_output_range().
    /// </summary>
    public (int Min, int Max) GetOutputRange(int axis)
    {
        string a = AxisKey(axis);
        int min = int.TryParse(_ini.Get("Output Range", a + "min", "0"), out var lo) ? lo : 0;
        int max = int.TryParse(_ini.Get("Output Range", a + "max", "999"), out var hi) ? hi : 999;
        return (min, max);
    }

    public void SetOutputRange(int axis, int min, int max)
    {
        string a = AxisKey(axis);
        _ini.Set("Output Range", a + "min", min.ToString());
        _ini.Set("Output Range", a + "max", max.ToString());
        _ini.Save();
    }

    /// <summary>
    /// Per-axis output enable under [Axis Enable], keys L0..R2 (true/false), default true.
    /// Mirrors the Qt save_axis_enable()/load_axis_enable().
    /// </summary>
    public bool GetAxisEnabled(int axis) => _ini.Get("Axis Enable", AxisKey(axis), "true") != "false";

    public void SetAxisEnabled(int axis, bool enabled)
    {
        _ini.Set("Axis Enable", AxisKey(axis), enabled ? "true" : "false");
        _ini.Save();
    }
}
