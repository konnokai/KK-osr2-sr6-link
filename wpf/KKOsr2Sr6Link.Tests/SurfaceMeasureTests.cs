using System.Windows;
using KKOsr2Sr6Link.Wpf.Controls;

namespace KKOsr2Sr6Link.Tests;

public class SurfaceMeasureTests
{
    // 換場景時 ClearActions 會把 Values 清空，這時切到腳本編輯頁不能因為負寬度崩潰
    [Fact]
    public void OverviewEdit_EmptyValues_MeasuresWithoutThrowing()
        => RunSta(() =>
        {
            var ov = new OverviewEdit { Values = new List<int>() };
            ov.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(ov.DesiredSize.Width >= 0);
        });

    [Fact]
    public void ScripterEdit_EmptyValues_MeasuresWithoutThrowing()
        => RunSta(() =>
        {
            var se = new ScripterEdit { Values = new List<int>() };
            se.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(se.DesiredSize.Width >= 0);
        });

    private static void RunSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw new Exception("STA body failed", error);
    }
}
