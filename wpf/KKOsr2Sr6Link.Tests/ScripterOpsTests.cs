using System.Collections.Generic;
using System.Linq;
using KKOsr2Sr6Link.Wpf.Controls;

namespace KKOsr2Sr6Link.Tests;

public class ScripterOpsTests
{
    [Fact]
    public void CleanAndSort_DropsMinusOne_AndSorts()
    {
        var values = new List<int> { 0, -1, 200, 300 };
        var sel = new List<int> { 3, 1, 2 };
        ScripterOps.CleanAndSort(values, sel);
        Assert.Equal(new List<int> { 2, 3 }, sel); // index 1 was -1
    }

    [Fact]
    public void SelectAll_PicksValuePointsAndAllTimes()
    {
        var values = new List<int> { 0, -1, 500 };
        var (sv, st) = ScripterOps.SelectAll(values);
        Assert.Equal(new List<int> { 0, 2 }, sv);
        Assert.Equal(new List<int> { 0, 1, 2 }, st);
    }

    [Fact]
    public void DeleteSelected_KeepsEndpoints()
    {
        var values = new List<int> { 10, 20, 30, 40 };
        ScripterOps.DeleteSelected(values, new[] { 0, 1, 3 });
        Assert.Equal(new List<int> { 10, -1, 30, 40 }, values); // 0 and last kept
    }

    [Fact]
    public void SelectPeaks_FindsLocalMaxima()
    {
        var values = new List<int> { 0, 800, 100, 900, 50 };
        var sel = new List<int> { 0, 1, 2, 3, 4 };
        var peaks = ScripterOps.SelectPeaks(values, sel);
        Assert.Contains(1, peaks);
        Assert.Contains(3, peaks);
        Assert.DoesNotContain(2, peaks);
    }

    [Fact]
    public void Amplify_ExpandsAroundMidpoint_Clamped()
    {
        var values = new List<int> { 400, 600 };
        ScripterOps.Amplify(values, new List<int> { 0, 1 }, 1.1);
        // mid=500; 400-> 500 + (-100*1.1)=390 ; 600-> 610
        Assert.Equal(390, values[0]);
        Assert.Equal(610, values[1]);
    }

    [Fact]
    public void Reverse_MirrorsAround500()
    {
        var values = new List<int> { 300, 700 };
        ScripterOps.Reverse(values, new List<int> { 0, 1 });
        Assert.Equal(700, values[0]);
        Assert.Equal(300, values[1]);
    }

    [Fact]
    public void CopyPaste_RoundTripsContiguousRange()
    {
        var src = new List<int> { 10, 20, 30, 40, 50 };
        var sel = new List<int> { 1, 3 };
        var (cv, ci) = ScripterOps.Copy(src, sel);
        Assert.Equal(new List<int> { 20, 30, 40 }, cv); // fills the gap (index 2)
        Assert.Equal(new List<int> { 1, 2, 3 }, ci);

        var dst = new List<int> { 0, 0, 0, 0, 0, 0 };
        ScripterOps.Paste(dst, selectedLine: 2, cv, ci);
        Assert.Equal(new List<int> { 0, 0, 20, 30, 40, 0 }, dst);
    }
}
