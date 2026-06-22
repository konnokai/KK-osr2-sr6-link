using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using KKOsr2Sr6Link.Wpf.Engine;

namespace KKOsr2Sr6Link.Tests;

public class SceneFilesTests
{
    private static string Temp(string ext) => Path.Combine(Path.GetTempPath(), "kk_" + Path.GetRandomFileName() + ext);

    [Fact]
    public void Sr6Script_RoundTrips()
    {
        var path = Temp(".sr6script");
        try
        {
            var script = new AxisScript { Values = { 0, 500, 999, -1, 250 }, MaxValue = 999, MinValue = 0 };
            SceneFiles.SaveSr6Script(path, script);
            var loaded = SceneFiles.LoadSr6Script(path)!;
            Assert.Equal(new List<int> { 0, 500, 999, -1, 250 }, loaded.Values);
            Assert.Equal(999, loaded.MaxValue);
            Assert.Equal(0, loaded.MinValue);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Sr6Cfg_RoundTrips()
    {
        var path = Temp(".sr6cfg");
        try
        {
            var parts = new List<ScenePart>
            {
                new() { Part = 0, LovemakingMode = "normal", Charas = "chaF_001-chaM_001" },
                new() { Part = 150, LovemakingMode = "blowjob", Charas = "chaF_001-chaM_001" },
            };
            SceneFiles.SaveSr6Cfg(path, parts);
            var loaded = SceneFiles.LoadSr6Cfg(path);
            Assert.Equal(2, loaded.Count);
            Assert.Equal(150, loaded[1].Part);
            Assert.Equal("blowjob", loaded[1].LovemakingMode);
            Assert.Equal("chaF_001-chaM_001", loaded[0].Charas);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Funscript_Export_MapsTimeAndPosition_SkipsMinusOne()
    {
        var path = Temp(".funscript");
        try
        {
            // index:        0     1    2(skip)  3
            var values = new[] { 0, 999, -1, 500 };
            SceneFiles.ExportFunscript(path, values, referenceCount: 4);

            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var actions = root["actions"]!.AsArray();
            Assert.Equal(3, actions.Count); // -1 skipped

            Assert.Equal(0, (int)actions[0]!["at"]!);
            Assert.Equal(0, (int)actions[0]!["pos"]!);
            Assert.Equal(100, (int)actions[1]!["at"]!);
            Assert.Equal(100, (int)actions[1]!["pos"]!);     // 999/999*100 -> 100
            Assert.Equal(300, (int)actions[2]!["at"]!);      // index 3 -> 300ms
            Assert.Equal(50, (int)actions[2]!["pos"]!);      // 500/999*100 -> 50

            Assert.Equal("1.0", (string)root["version"]!);
            Assert.Equal(100, (int)root["range"]!);
            Assert.False((bool)root["inverted"]!);
            Assert.Equal(1, (int)root["metadata"]!["duration"]!); // ceil((4-1)*0.1)=1
            Assert.Equal("basic", (string)root["metadata"]!["type"]!);
        }
        finally { File.Delete(path); }
    }
}
