using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KKOsr2Sr6Link.Wpf.Controls;

namespace KKOsr2Sr6Link.Tests;

public class ScripterEditStateTests
{
    [Fact]
    public void ScripterEdit_LeadingEmptyFrame_RendersWithoutThrowing()
        => Sta.Run(() => Render(new ScripterEdit { Values = new List<int> { -1, 200, 300 } }));

    [Fact]
    public void OverviewEdit_LeadingEmptyFrame_RendersWithoutThrowing()
        => Sta.Run(() => Render(new OverviewEdit { Values = new List<int> { -1, 200, 300 } }));

    // 舊場景的選取 index 留到短場景，刪除時會超出範圍
    [Fact]
    public void SceneSwitch_DropsStaleSelection()
        => Sta.Run(() =>
        {
            var se = new ScripterEdit { Values = Enumerable.Repeat(500, 20).ToList() };
            se.DoSelectAll();
            se.Values = new List<int> { 0, 500, 999 };
            se.DoDelete();
            Assert.Equal(new List<int> { 0, 500, 999 }, se.Values);
        });

    // 上一個場景的 undo 不能還原到新場景
    [Fact]
    public void SceneSwitch_ClearsUndo()
        => Sta.Run(() =>
        {
            var se = new ScripterEdit { Values = new List<int> { 0, 100, 200, 300, 999 } };
            se.RecordUndo();
            var next = new List<int> { 0, 500, 999 };
            se.Values = next;
            se.DoUndo();
            Assert.Same(next, se.Values);
            Assert.Equal(new List<int> { 0, 500, 999 }, next);
        });

    // OverviewEdit 和第一軸共用同一個 List，undo 換掉實例就會讓總覽停在舊曲線
    [Fact]
    public void Undo_RestoresIntoSameList()
        => Sta.Run(() =>
        {
            var values = new List<int> { 0, 100, 999 };
            var se = new ScripterEdit { Values = values };
            se.RecordUndo();
            values[1] = 700;
            se.DoUndo();
            Assert.Same(values, se.Values);
            Assert.Equal(100, values[1]);
        });

    private static void Render(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        var bmp = new RenderTargetBitmap(
            Math.Max(1, (int)element.ActualWidth), Math.Max(1, (int)element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(element);
    }
}
