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
    public void ServerPort_PersistsToServerKey()
    {
        // Original Qt bug (read [Server], write [SerialPort]) is now fixed in both apps:
        // a port change persists to [Server]/Serverport and reads back.
        var path = TempPath();
        try
        {
            var cfg = new AppConfig(path);
            cfg.ServerPort = "9001";
            Assert.Equal("9001", cfg.ServerPort);

            var ini = new IniConfig(path);
            Assert.Equal("9001", ini.Get("Server", "Serverport"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OutputRange_RoundTrips_AndDefaults()
    {
        var path = TempPath();
        try
        {
            var cfg = new AppConfig(path);
            // defaults when absent
            Assert.Equal((0, 999), cfg.GetOutputRange(0));

            cfg.SetOutputRange(3, 100, 800);
            Assert.Equal((100, 800), cfg.GetOutputRange(3));

            var reload = new AppConfig(path);
            Assert.Equal((100, 800), reload.GetOutputRange(3));
            var ini = new IniConfig(path);
            Assert.Equal("100", ini.Get("Output Range", "R0min"));
            Assert.Equal("800", ini.Get("Output Range", "R0max"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Language_DefaultsEn_AndRoundTrips()
    {
        var path = TempPath();
        try
        {
            var cfg = new AppConfig(path);
            Assert.Equal("en", cfg.Language); // default

            cfg.Language = "zh-Hant";
            Assert.Equal("zh-Hant", cfg.Language);

            var reload = new AppConfig(path);
            Assert.Equal("zh-Hant", reload.Language);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AxisEnable_RoundTrips_AndDefaultsTrue()
    {
        var path = TempPath();
        try
        {
            var cfg = new AppConfig(path);
            for (int a = 0; a < 6; a++) Assert.True(cfg.GetAxisEnabled(a)); // default true

            cfg.SetAxisEnabled(2, false);
            Assert.False(cfg.GetAxisEnabled(2));

            var reload = new AppConfig(path);
            Assert.False(reload.GetAxisEnabled(2));
            Assert.True(reload.GetAxisEnabled(0));
        }
        finally { File.Delete(path); }
    }
}
