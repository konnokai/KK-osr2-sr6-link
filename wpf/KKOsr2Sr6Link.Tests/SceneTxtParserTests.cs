using System.Linq;
using KKOsr2Sr6Link.Wpf.Engine;

namespace KKOsr2Sr6Link.Tests;

public class SceneTxtParserTests
{
    // 33 "/"-delimited fields; value = field index so we can assert routing.
    private static string DataLine() => string.Join("/", Enumerable.Range(0, 33));

    [Fact]
    public void OldVersion_FirstLineNotNew_ReturnsNoData()
    {
        var scene = SceneTxtParser.Parse("old\nchaF_001-chaM_001\n" + DataLine());
        Assert.False(scene.IsNewVersion);
        Assert.Empty(scene.Data);
    }

    [Fact]
    public void NewVersion_ParsesPairAndFieldsByIndex()
    {
        var text = "New\nchaF_001-chaM_001\n" + DataLine();
        var scene = SceneTxtParser.Parse(text);

        Assert.True(scene.IsNewVersion);
        var d = Assert.Single(scene.Data);
        Assert.Equal("chaF_001-chaM_001", d.CharasName);

        // normal axes route from fields 0..5
        Assert.Equal(0f, d.Inserts[0]);
        Assert.Equal(1f, d.Surges[0]);
        Assert.Equal(2f, d.Sways[0]);
        Assert.Equal(3f, d.Twists[0]);
        Assert.Equal(4f, d.Rolls[0]);
        Assert.Equal(5f, d.Pitchs[0]);
        Assert.Equal(8f, d.BodyWidth);

        // blowjob surge/sway are intentionally swapped (10 -> sway, 11 -> surge)
        Assert.Equal(10f, d.BlowjobSways[0]);
        Assert.Equal(11f, d.BlowjobSurges[0]);

        // handjobR tail
        Assert.Equal(27f, d.HandjobRInserts[0]);
        Assert.Equal(32f, d.HandjobRPitchs[0]);
    }

    [Fact]
    public void ComputeInitialAxes_ProducesClamped0To999_ForEachAxis()
    {
        var text = "New\nchaF_001-chaM_001\n" + DataLine() + "\n" + DataLine();
        var scene = SceneTxtParser.Parse(text);
        var axes = SceneTxtParser.ComputeInitialAxes(scene.Data[0]);

        Assert.Equal(6, axes.Length);
        foreach (var ax in axes)
        {
            Assert.Equal(2, ax.Values.Count); // one per data line
            Assert.All(ax.Values, v => Assert.InRange(v, 0, 999));
        }
    }
}
