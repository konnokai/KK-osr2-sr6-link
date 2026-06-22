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
    /// Server port. Faithfully reproduces the original quirk (mainwindow.cpp:288-296,318):
    /// the value is READ from [Server]/Serverport but the change handler WROTE it to
    /// [SerialPort]/Serverport. We replicate this byte-for-byte rather than "fixing" it,
    /// so a config.ini produced by the Qt app behaves identically.
    /// NOTE: latent bug in the original — a port change does not persist to the key that is
    /// read back on next launch. Surface to the user before deciding whether to correct it.
    /// </summary>
    public string ServerPort
    {
        get => _ini.Get("Server", "Serverport", "8000");
        set { _ini.Set("SerialPort", "Serverport", value); _ini.Save(); }
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
}
