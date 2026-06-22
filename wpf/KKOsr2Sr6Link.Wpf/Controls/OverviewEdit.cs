using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace KKOsr2Sr6Link.Wpf.Controls;

/// <summary>
/// Whole-scene timeline overview: a polyline of L0 values with split lines and a scrub playhead.
/// Port of overview_edit.cpp. Left-click selects a part, middle-drag scrubs (CurrentLine),
/// right-click adds/removes a split, Ctrl+Left/Right or Ctrl+Wheel zoom, Space plays.
/// </summary>
public sealed class OverviewEdit : FrameworkElement
{
    private const int Pad = 20;

    private static readonly Brush Bg = Freeze(Color.FromRgb(47, 54, 60));
    private static readonly Brush Box = Freeze(Color.FromRgb(58, 65, 82));
    private static readonly Pen LinePen = FreezePen(Color.FromRgb(255, 110, 144), 1);
    private static readonly Pen GridPen = FreezePen(Color.FromArgb(150, 255, 255, 255), 1);
    private static readonly Pen SplitPen = FreezePen(Color.FromArgb(255, 220, 220, 200), 3);
    private static readonly Pen PlayPen = FreezePen(Color.FromRgb(255, 19, 121), 2);
    private static readonly Brush PlayBrush = Freeze(Color.FromRgb(255, 19, 121));
    private static readonly Brush White = Freeze(Colors.White);

    public List<int> Values { get; set; } = new() { 0, 500, 999 };
    public List<int> SplitLines { get; set; } = new();
    public int SelectedLine { get; set; }
    public int SelectedPart { get; set; }
    public int Intervals { get; private set; } = 100;
    private int _valueEdge = 5;
    private bool _mouseScrub, _mouseSplit;

    public event Action<int>? CurrentLine;
    public event Action? SetPlay;
    public event Action<int>? AddPart;
    public event Action<int>? DelPart;
    public event Action<int>? SelectPart;

    public OverviewEdit()
    {
        Height = 100;
        Focusable = true;
        FocusVisualStyle = null;
    }

    /// <summary>Recompute layout width from the data, like Qt's setFixedWidth in paintEvent.</summary>
    public void Refresh()
    {
        if (Values.Count >= 1)
            Width = (Values.Count - 1) * Intervals + _valueEdge * 2 + Pad * 2;
        InvalidateVisual();
    }

    private double Y(int value, double h)
        => (h - _valueEdge * 2 - Pad * 2) / -999.0 * value + h - _valueEdge - Pad;

    private int X(int i) => _valueEdge + Intervals * i + Pad;

    protected override void OnRender(DrawingContext dc)
    {
        if (Values.Count < 1) return;
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Bg, null, new Rect(0, 0, w, h));

        // selected-part highlight box
        if (SplitLines.Count == 0)
            dc.DrawRectangle(Box, null, new Rect(Pad + _valueEdge, Pad, Math.Max(0, w - Pad * 2), h - Pad * 2));
        else if (SelectedPart == SplitLines[^1])
            dc.DrawRectangle(Box, null, new Rect(_valueEdge + Intervals * SelectedPart + Pad, Pad,
                Math.Max(0, w - 2 * Pad - _valueEdge - Intervals * SelectedPart), h - 2 * Pad));
        else if (SelectedPart == 0)
            dc.DrawRectangle(Box, null, new Rect(Pad + _valueEdge, Pad, Intervals * SplitLines[0], h - 2 * Pad));
        else
        {
            int idx = SplitLines.IndexOf(SelectedPart);
            if (idx >= 0 && idx + 1 < SplitLines.Count)
                dc.DrawRectangle(Box, null, new Rect(_valueEdge + Intervals * SelectedPart + Pad, Pad,
                    Intervals * (SplitLines[idx + 1] - SelectedPart), h - 2 * Pad));
        }

        // value polyline (skip -1, connect to last valid)
        for (int i = 1; i < Values.Count; i++)
        {
            if (Values[i] == -1) continue;
            int index = i;
            while (index - 1 >= 0 && Values[index - 1] == -1) index--;
            dc.DrawLine(LinePen, new Point(X(index - 1), Y(Values[index - 1], h)), new Point(X(i), Y(Values[i], h)));
        }

        // horizontal reference lines
        dc.DrawLine(GridPen, new Point(_valueEdge + Pad, _valueEdge + Pad), new Point(w - Pad - _valueEdge, _valueEdge + Pad));
        dc.DrawLine(GridPen, new Point(_valueEdge + Pad, h - _valueEdge - Pad), new Point(w - Pad - _valueEdge, h - _valueEdge - Pad));
        dc.DrawLine(GridPen, new Point(_valueEdge + Pad, h / 2), new Point(w - Pad - _valueEdge, h / 2));

        // vertical grid + split lines + time labels
        for (int i = 0; i < Values.Count; i++)
        {
            int x = X(i);
            dc.DrawLine(GridPen, new Point(x, _valueEdge + Pad), new Point(x, h - Pad - _valueEdge));
            if (SplitLines.Contains(i))
                dc.DrawLine(SplitPen, new Point(x, 0), new Point(x, h));

            if ((i == 0 || i == Values.Count - 1 || i % 5 == 0) && Intervals >= 30)
            {
                long ms = i * 100L;
                string text = $"{(ms % 3600000) / 60000:00}:{(ms % 60000) / 1000:00}.{ms % 1000 / 10:00}";
                var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei"), 11, White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(ft, new Point(x - ft.Width / 2, h - Pad / 2.0 - ft.Height / 2));
            }
        }

        // playhead
        int px = X(SelectedLine);
        dc.DrawLine(PlayPen, new Point(px, Pad + _valueEdge - 5), new Point(px, h - Pad - _valueEdge + 5));
        DrawTriangle(dc, new Point(px, Pad + _valueEdge - 5), new Point(px + 5, Pad + _valueEdge - 10), new Point(px - 5, Pad + _valueEdge - 10));
        DrawTriangle(dc, new Point(px, h - Pad - _valueEdge + 5), new Point(px - 5, h - Pad - _valueEdge + 10), new Point(px + 5, h - Pad - _valueEdge + 10));
    }

    private static void DrawTriangle(DrawingContext dc, Point a, Point b, Point c)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open()) { g.BeginFigure(a, true, true); g.LineTo(b, true, false); g.LineTo(c, true, false); }
        geo.Freeze();
        dc.DrawGeometry(PlayBrush, null, geo);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (Values.Count == 0) return;
        double mx = e.GetPosition(this).X;

        if (e.ChangedButton == MouseButton.Left)
        {
            if (SplitLines.Count == 0) { SelectedPart = 0; SelectPart?.Invoke(0); InvalidateVisual(); return; }
            if (Pad + SplitLines[^1] * Intervals < mx && mx < ActualWidth - Pad)
            {
                SelectedPart = SplitLines[^1]; SelectPart?.Invoke(SplitLines.IndexOf(SelectedPart) + 1); InvalidateVisual(); return;
            }
            for (int i = 0; i < SplitLines.Count; i++)
            {
                if (i == 0 && Pad < mx && mx < Pad + SplitLines[i] * Intervals)
                { SelectedPart = 0; SelectPart?.Invoke(0); InvalidateVisual(); return; }
                if (i + 1 < SplitLines.Count && Pad + SplitLines[i] * Intervals < mx && mx < Pad + SplitLines[i + 1] * Intervals)
                { SelectedPart = SplitLines[i]; SelectPart?.Invoke(SplitLines.IndexOf(SelectedPart) + 1); InvalidateVisual(); return; }
            }
        }
        else if (e.ChangedButton == MouseButton.Middle)
        {
            _mouseScrub = true; CaptureMouse(); ScrubTo(mx);
        }
        else if (e.ChangedButton == MouseButton.Right && !_mouseSplit)
        {
            _mouseSplit = true;
            for (int i = 0; i < Values.Count; i++)
            {
                int x = X(i);
                if (x - _valueEdge < mx && mx < x + _valueEdge)
                {
                    if (i == 0 || i == Values.Count - 1) return;
                    if (!SplitLines.Contains(i))
                    {
                        SplitLines.Add(i); SplitLines.Sort(); SelectedPart = i; AddPart?.Invoke(SplitLines.IndexOf(i) + 1);
                    }
                    else if (i == SelectedPart)
                    {
                        int idx = SplitLines.IndexOf(i);
                        if (idx == 0) { SelectedPart = 0; DelPart?.Invoke(0); SelectPart?.Invoke(0); }
                        else { SelectedPart = SplitLines[idx - 1]; DelPart?.Invoke(idx); SelectPart?.Invoke(SplitLines.IndexOf(SelectedPart) + 1); }
                    }
                    else DelPart?.Invoke(SplitLines.IndexOf(i));
                    InvalidateVisual(); return;
                }
            }
        }
    }

    private void ScrubTo(double mx)
    {
        for (int i = 0; i < Values.Count; i++)
        {
            int x = X(i);
            if (x - _valueEdge < mx && mx < x + _valueEdge) { SelectedLine = i; CurrentLine?.Invoke(i); InvalidateVisual(); return; }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_mouseScrub) ScrubTo(e.GetPosition(this).X);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        _mouseScrub = false; _mouseSplit = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl && e.Key == Key.Left) ZoomOut();
        else if (ctrl && e.Key == Key.Right) ZoomIn();
        else if (e.Key == Key.Space) SetPlay?.Invoke();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Delta > 0) ZoomIn(); else ZoomOut();
    }

    private void ZoomIn()
    {
        if (Intervals + 5 > 200) return;
        Intervals += 5;
        UpdateEdge(); Refresh();
    }

    private void ZoomOut()
    {
        if (Intervals - 5 > 5) Intervals -= 5;
        else if (Intervals - 1 > 1) Intervals -= 1;
        UpdateEdge(); Refresh();
    }

    private void UpdateEdge()
    {
        if (Intervals <= 100)
            _valueEdge = (int)((5 - 3) / (100.0 - 2) * Intervals + 3 - (5 - 3) / (100.0 - 2) * 2);
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(double.IsNaN(Width) ? (Values.Count - 1) * Intervals + _valueEdge * 2 + Pad * 2 : Width, Height);

    private static Brush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Pen FreezePen(Color c, double w) { var p = new Pen(new SolidColorBrush(c), w); p.Freeze(); return p; }
}
