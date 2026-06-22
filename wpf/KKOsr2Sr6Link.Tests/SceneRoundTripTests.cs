using System.IO;
using System.Linq;
using KKOsr2Sr6Link.Wpf.Engine;

namespace KKOsr2Sr6Link.Tests;

/// <summary>
/// End-to-end file pipeline: parse a fresh scene .txt -> derive axes -> save the six .sr6script files
/// and the .sr6cfg -> reload. Proves the on-disk formats round-trip (the data-compat requirement).
/// </summary>
public class SceneRoundTripTests
{
    [Fact]
    public void FreshScene_SavesAndReloads_AllAxesAndParts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kkrt_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var scenePath = Path.Combine(dir, "scene.txt");
        try
        {
            // 3 frames, one character pair.
            string line = string.Join("/", Enumerable.Range(0, 33).Select(i => (i % 7).ToString()));
            File.WriteAllText(scenePath, "New\nchaF_001-chaM_001\n" + line + "\n" + line + "\n" + line);

            var scene = SceneTxtParser.Parse(File.ReadAllText(scenePath));
            Assert.True(scene.IsNewVersion);
            var axes = SceneTxtParser.ComputeInitialAxes(scene.Data[0]);

            var parts = new[] { new ScenePart { Part = 0, LovemakingMode = "normal", Charas = "chaF_001-chaM_001" } };
            for (int a = 0; a < 6; a++)
                SceneFiles.SaveSr6Script(AxisInfo.Sr6ScriptPath(scenePath, (Axis)a),
                    new AxisScript { Values = axes[a].Values, MaxValue = 999, MinValue = 0 });
            SceneFiles.SaveSr6Cfg(AxisInfo.Sr6CfgPath(scenePath), parts);

            // reload each axis + parts
            for (int a = 0; a < 6; a++)
            {
                var reloaded = SceneFiles.LoadSr6Script(AxisInfo.Sr6ScriptPath(scenePath, (Axis)a));
                Assert.NotNull(reloaded);
                Assert.Equal(axes[a].Values, reloaded!.Values);
                Assert.Equal(999, reloaded.MaxValue);
            }
            var reloadedParts = SceneFiles.LoadSr6Cfg(AxisInfo.Sr6CfgPath(scenePath));
            Assert.Single(reloadedParts);
            Assert.Equal("chaF_001-chaM_001", reloadedParts[0].Charas);

            // expected sidecar filenames exist (matches the Qt naming)
            Assert.True(File.Exists(Path.Combine(dir, "scene.sr6script")));
            Assert.True(File.Exists(Path.Combine(dir, "scene.surge.sr6script")));
            Assert.True(File.Exists(Path.Combine(dir, "scene.pitch.sr6script")));
            Assert.True(File.Exists(Path.Combine(dir, "scene.sr6cfg")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
