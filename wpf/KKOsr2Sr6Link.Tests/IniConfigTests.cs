using System.IO;
using KKOsr2Sr6Link.Wpf.Engine;

namespace KKOsr2Sr6Link.Tests;

public class IniConfigTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), "kkcfg_" + Path.GetRandomFileName() + ".ini");

    [Fact]
    public void RoundTrips_Sections_Keys_With_Spaces()
    {
        var path = TempPath();
        try
        {
            var ini = new IniConfig(path);
            ini.Set("Intiface Central", "webserverip", "ws://localhost:12345");
            ini.Set("Game", "game root", "C:/some path/with spaces");
            ini.Save();

            var reload = new IniConfig(path);
            Assert.True(reload.FileExisted);
            Assert.Equal("ws://localhost:12345", reload.Get("Intiface Central", "webserverip"));
            Assert.Equal("C:/some path/with spaces", reload.Get("Game", "game root"));
            Assert.Equal("fallback", reload.Get("Missing", "nope", "fallback"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AppConfig_SeedsDefaults_OnFirstRun()
    {
        var path = TempPath();
        try
        {
            var cfg = new AppConfig(path);
            Assert.Equal("115200", cfg.BaudRate);
            Assert.Equal("127.0.0.1", cfg.ServerIp);
            Assert.Equal("8000", cfg.ServerPort);
            Assert.Equal("ws://localhost:12345", cfg.WebServerIp);
            Assert.False(cfg.RebuildAllAxes);
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ServerPort_ReproducesOriginalQuirk_WriteDoesNotPersistToReadKey()
    {
        // Original Qt bug: reads [Server]/Serverport, writes [SerialPort]/Serverport.
        var path = TempPath();
        try
        {
            var cfg = new AppConfig(path);
            cfg.ServerPort = "9001";

            // Same process: getter still reads the (unchanged) [Server] key.
            Assert.Equal("8000", cfg.ServerPort);

            // And it landed under [SerialPort] instead.
            var ini = new IniConfig(path);
            Assert.Equal("9001", ini.Get("SerialPort", "Serverport"));
            Assert.Equal("8000", ini.Get("Server", "Serverport"));
        }
        finally { File.Delete(path); }
    }
}
