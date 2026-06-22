using System.Linq;
using KKOsr2Sr6Link.Wpf.Engine;

namespace KKOsr2Sr6Link.Tests;

public class PlaybackEngineTests
{
    private static PlaybackEngine WithL0(params int[] values)
    {
        var e = new PlaybackEngine();
        e.AxisValues[0].AddRange(values);
        return e;
    }

    [Fact]
    public void FindsNextKeyframe_AndComputesSleepFromGap()
    {
        var e = WithL0(0, 100, 200, 300);
        var cmds = e.ComputeCommands(index: 1);
        var l0 = cmds.Single(c => c.Axis == Axis.L0);
        Assert.Equal(200, l0.ScaledValue);   // next value after index 1
        Assert.Equal(100, l0.SleepMs);       // gap of 1 frame * 100ms
    }

    [Fact]
    public void SkipsMinusOne_Sentinels()
    {
        var e = WithL0(0, -1, -1, 500);
        var cmds = e.ComputeCommands(index: 0);
        var l0 = cmds.Single(c => c.Axis == Axis.L0);
        Assert.Equal(500, l0.ScaledValue);
        Assert.Equal(300, l0.SleepMs);       // 3 frames ahead
    }

    [Fact]
    public void Dedup_SuppressesRepeatValue_UntilItChanges()
    {
        var e = WithL0(0, 200, 200, 400);
        Assert.Equal(200, e.ComputeCommands(0).Single(c => c.Axis == Axis.L0).ScaledValue);
        Assert.DoesNotContain(e.ComputeCommands(1), c => c.Axis == Axis.L0); // still 200 -> suppressed
        Assert.Equal(400, e.ComputeCommands(2).Single(c => c.Axis == Axis.L0).ScaledValue);
    }

    [Fact]
    public void DisabledAxis_EmitsNoCommand()
    {
        var e = WithL0(0, 100, 200);
        e.Enabled[0] = false;
        Assert.DoesNotContain(e.ComputeCommands(0), c => c.Axis == Axis.L0);
    }

    [Fact]
    public void AppliesAxisRangeScaling()
    {
        var e = WithL0(0, 999);
        e.MinValue[0] = 100;
        e.MaxValue[0] = 900;
        var l0 = e.ComputeCommands(0).Single(c => c.Axis == Axis.L0);
        Assert.Equal(900, l0.ScaledValue); // 999 raw -> max
    }
}
