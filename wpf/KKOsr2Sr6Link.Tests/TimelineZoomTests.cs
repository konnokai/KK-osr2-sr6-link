using KKOsr2Sr6Link.Wpf.Controls;

namespace KKOsr2Sr6Link.Tests;

public class TimelineZoomTests
{
    private static List<int> Frames(int count) => Enumerable.Repeat(500, count).ToList();

    // 600 格塞進 1000 px，每格不到 2 px；取整數會變成 1 px，只填到一半寬
    [Fact]
    public void OverviewEdit_FitToWidth_FillsViewport()
        => Sta.Run(() =>
        {
            var ov = new OverviewEdit { Values = Frames(600) };
            ov.FitToWidth(1000);
            Assert.InRange(ov.Width, 999, 1000);
        });

    [Fact]
    public void ScripterEdit_FitToWidth_FillsViewport()
        => Sta.Run(() =>
        {
            var se = new ScripterEdit { Values = Frames(600) };
            se.FitToWidth(1000);
            Assert.InRange(se.Width, 999, 1000);
        });

    [Fact]
    public void ZoomIn_FromFit_GrowsByOneStep()
        => Sta.Run(() =>
        {
            var ov = new OverviewEdit { Values = Frames(600) };
            ov.FitToWidth(1000);
            double fit = ov.Intervals;
            ov.ZoomIn();
            Assert.Equal(fit * 1.25, ov.Intervals, 6);

            var se = new ScripterEdit { Values = Frames(600) };
            se.FitToWidth(1000);
            fit = se.Intervals;
            se.ZoomIn();
            Assert.Equal(fit * 1.25, se.Intervals, 6);
        });

    // 只有幾格時填滿寬度會超過上限 200 px，這時放大不能反而縮小
    [Fact]
    public void ZoomIn_AboveMax_DoesNotShrink()
        => Sta.Run(() =>
        {
            var ov = new OverviewEdit { Values = Frames(3) };
            ov.FitToWidth(1000);
            double fit = ov.Intervals;
            ov.ZoomIn();
            Assert.True(ov.Intervals >= fit);
        });

    [Fact]
    public void ZoomOut_StopsAtFitWidth()
        => Sta.Run(() =>
        {
            var ov = new OverviewEdit { Values = Frames(600) };
            ov.FitToWidth(1000);
            double fit = ov.Intervals;
            ov.ZoomIn();
            ov.ZoomOut();
            ov.ZoomOut();
            Assert.Equal(fit, ov.Intervals, 6);
        });
}
