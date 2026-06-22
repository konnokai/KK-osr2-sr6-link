using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace KKOsr2Sr6Link.Wpf.Controls;

/// <summary>
/// Per-axis min/value/max slider (0..999), a direct port of range_silder.cpp. Drag the left circle
/// for min, the center bar for value, the right circle for max. Fixed height 30.
/// </summary>
public sealed class RangeSlider : FrameworkElement
{
    private const int ValueEdge = 5;
    private const int Pad = 10;

    private static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(34, 37, 46));
    private static readonly Brush Track = new SolidColorBrush(Color.FromRgb(52, 59, 72));
    private static readonly Brush Pink = new SolidColorBrush(Color.FromRgb(255, 121, 198));

    private int _maxValue = 999;
    private int _minValue = 0;
    private int _value = 500;
    private int _select = -1; // 0 min, 1 value, 2 max

    public event Action? ValueChanged;

    public int MaxValue { get => _maxValue; set { _maxValue = value; InvalidateVisual(); } }
    public int MinValue { get => _minValue; set { _minValue = value; InvalidateVisual(); } }
    public int Value { get => _value; set { _value = value; InvalidateVisual(); } }

    public RangeSlider()
    {
        Height = 30;
        MinWidth = 50;
        Bg.Freeze(); Track.Freeze(); Pink.Freeze();
    }

    private double GetX(int value)
        => (ActualWidth - ValueEdge * 2 - Pad * 2) / 999.0 * value + ValueEdge + Pad;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Bg, null, new Rect(0, 0, w, h));

        // base track
        dc.DrawRoundedRectangle(Track, null,
            new Rect(Pad, h / 2 - ValueEdge, Math.Max(0, w - Pad * 2), 10), 5, 5);

        // active range bar
        double minX = GetX(_minValue), maxX = GetX(_maxValue);
        dc.DrawRoundedRectangle(Pink, null,
            new Rect(minX, h / 2 - 2, Math.Max(0, maxX - minX), 4), 2, 2);

        // min/max thumbs
        dc.DrawEllipse(Pink, null, new Point(maxX, h / 2), ValueEdge, ValueEdge);
        dc.DrawEllipse(Pink, null, new Point(minX, h / 2), ValueEdge, ValueEdge);

        // value indicator
        dc.DrawRectangle(Pink, null, new Rect(GetX(_value) - 3, h / 2 - 9, 6, 18));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        double h = ActualHeight;
        if (p.Y < h / 2 + 9 && p.Y > h / 2 - ValueEdge
            && GetX(_value) - ValueEdge < p.X && p.X < GetX(_value) + ValueEdge)
        {
            _select = 1; CaptureMouse(); return;
        }
        if (p.Y < h / 2 + ValueEdge && p.Y > h / 2 - ValueEdge)
        {
            if (GetX(_minValue) - ValueEdge < p.X && p.X < GetX(_minValue) + ValueEdge) { _select = 0; CaptureMouse(); }
            else if (GetX(_maxValue) - ValueEdge < p.X && p.X < GetX(_maxValue) + ValueEdge) { _select = 2; CaptureMouse(); }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_select < 0) return;
        var p = e.GetPosition(this);
        int got = (int)((p.X - ValueEdge - Pad) * 999 / (ActualWidth - ValueEdge * 2 - Pad * 2));
        switch (_select)
        {
            case 0 when got < _maxValue && got >= 0:
                if (got > _value) _value = got;
                _minValue = got; break;
            case 1 when got < _maxValue && got > _minValue:
                _value = got; break;
            case 2 when got > _minValue && got <= 999:
                if (got < _value) _value = got;
                _maxValue = got; break;
        }
        InvalidateVisual();
        ValueChanged?.Invoke();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        _select = -1;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
}
