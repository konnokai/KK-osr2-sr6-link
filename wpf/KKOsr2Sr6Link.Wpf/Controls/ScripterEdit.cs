using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KKOsr2Sr6Link.Wpf.Controls;

/// <summary>
/// Per-axis keyframe curve editor — port of scripter_edit3.cpp. Renders points/segments, supports
/// rubber-band select (plain / Ctrl toggle), Shift+drag value change, middle-drag scrub, Ctrl+right
/// add/delete point, Alt rebuild-rectangle, plus the full keyboard + context-menu op set (all routed
/// through <see cref="ScripterOps"/>). Height 120; width grows with the data.
/// </summary>
public sealed class ScripterEdit : FrameworkElement
{
    private const int Pad = 20;

    private static readonly Brush Bg = Freeze(Color.FromRgb(47, 54, 60));
    private static readonly Pen LinePen = FreezePen(Color.FromRgb(255, 110, 144), 1);
    private static readonly Pen SelSegPen = FreezePen(Color.FromRgb(136, 0, 255), 4);
    private static readonly Pen GridFull = FreezePen(Color.FromRgb(255, 255, 255), 1);
    private static readonly Pen GridDim = FreezePen(Color.FromArgb(150, 255, 255, 255), 1);
    private static readonly Pen GridRebuild = FreezePen(Color.FromRgb(105, 105, 105), 1);
    private static readonly Pen PlayPen = FreezePen(Color.FromRgb(255, 19, 121), 2);
    private static readonly Brush PlayBrush = Freeze(Color.FromRgb(255, 19, 121));
    private static readonly Brush SelDot = Freeze(Color.FromRgb(255, 121, 198));
    private static readonly Brush Dot = Freeze(Color.FromRgb(189, 147, 249));
    private static readonly Brush RubberFill = Freeze(Color.FromArgb(40, 255, 121, 198));
    private static readonly Pen RubberPen = FreezePen(Color.FromRgb(255, 121, 198), 1);
    private static readonly Brush White = Freeze(Colors.White);

    public List<int> Values { get; set; } = new() { 0, 500, 999 };
    public int SelectedLine { get; set; }
    public int Intervals { get; private set; } = 100;

    private readonly List<int> _selected = new();
    private readonly List<int> _selectedTimes = new();
    private readonly List<int> _rebuildTimes = new();
    private readonly List<List<int>> _undo = new();
    private List<int> _oldValues = new();
    private List<int> _oldSelected = new();
    private List<int> _copyValues = new();
    private List<int> _copyIndexs = new();

    private int _valueEdge = 5;
    private bool _mouse1, _mouse2, _mouse3, _rebuild, _moveFirst = true;
    private int _moveIndex, _lastMoveIndex;
    private Point _press, _move, _rebuildStart, _rebuildEnd;

    private readonly ContextMenu _menu;
    private readonly TextBox _valueBox;

    public event Action<int>? CurrentLine;
    public event Action? SetPlay;
    public event Action<List<int>, List<int>>? GetCopyValues;
    public event Action<List<int>>? RebuildTimes;

    public ScripterEdit()
    {
        Height = 120;
        Focusable = true;
        FocusVisualStyle = null;
        _oldValues = new List<int>(Values);
        (_menu, _valueBox) = BuildMenu();
    }

    public void Refresh()
    {
        if (Values.Count >= 1)
            Width = (Values.Count - 1) * Intervals + _valueEdge * 2 + Pad * 2;
        InvalidateVisual();
    }

    /// <summary>Receive a clipboard from another axis (mirrors the cross-axis get_copy_values wiring).</summary>
    public void SetClipboard(List<int> copyValues, List<int> copyIndexs)
    {
        _copyValues = new List<int>(copyValues);
        _copyIndexs = new List<int>(copyIndexs);
    }

    private void PushUndo(List<int> snapshot) => _undo.Add(new List<int>(snapshot));

    private double Y(int value) => (ActualHeight - _valueEdge * 2 - Pad * 2) / -999.0 * value + ActualHeight - _valueEdge - Pad;
    private int X(int i) => _valueEdge + Intervals * i + Pad;

    // ---------- rendering ----------

    protected override void OnRender(DrawingContext dc)
    {
        if (Values.Count < 1) return;
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Bg, null, new Rect(0, 0, w, h));

        // value polyline + selected segments
        for (int i = 1; i < Values.Count; i++)
        {
            if (Values[i] == -1) continue;
            int index = i;
            while (index - 1 >= 0 && Values[index - 1] == -1) index--;
            var a = new Point(X(index - 1), Y(Values[index - 1]));
            var b = new Point(X(i), Y(Values[i]));
            dc.DrawLine(LinePen, a, b);
            if (_selected.Contains(i) && _selected.Contains(index - 1))
                dc.DrawLine(SelSegPen, a, b);
        }

        // horizontal reference lines
        dc.DrawLine(GridDim, new Point(_valueEdge + Pad, _valueEdge + Pad), new Point(w - Pad - _valueEdge, _valueEdge + Pad));
        dc.DrawLine(GridDim, new Point(_valueEdge + Pad, h - _valueEdge - Pad), new Point(w - Pad - _valueEdge, h - _valueEdge - Pad));
        dc.DrawLine(GridDim, new Point(_valueEdge + Pad, h / 2), new Point(w - Pad - _valueEdge, h / 2));

        // playhead
        int px = X(SelectedLine);
        dc.DrawLine(PlayPen, new Point(px, Pad + _valueEdge - 5), new Point(px, h - Pad - _valueEdge + 5));
        Triangle(dc, new Point(px, Pad + _valueEdge - 5), new Point(px + 5, Pad + _valueEdge - 10), new Point(px - 5, Pad + _valueEdge - 10));
        Triangle(dc, new Point(px, h - Pad - _valueEdge + 5), new Point(px - 5, h - Pad - _valueEdge + 10), new Point(px + 5, h - Pad - _valueEdge + 10));

        // vertical grid + time labels + points
        for (int i = 0; i < Values.Count; i++)
        {
            int x = X(i);
            Pen grid = _rebuildTimes.Contains(i) ? GridRebuild : _selectedTimes.Contains(i) ? GridFull : GridDim;
            dc.DrawLine(grid, new Point(x, _valueEdge + Pad), new Point(x, h - Pad - _valueEdge));

            if ((i == 0 || i == Values.Count - 1 || i % 5 == 0) && Intervals >= 30)
            {
                long ms = i * 100L;
                string text = $"{(ms % 3600000) / 60000:00}:{(ms % 60000) / 1000:00}.{ms % 1000 / 10:00}";
                var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei"), 11, White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(ft, new Point(x - ft.Width / 2, h - Pad / 2.0 - ft.Height / 2));
            }

            if (Values[i] == -1) continue;
            var brush = _selected.Contains(i) ? SelDot : Dot;
            dc.DrawEllipse(brush, null, new Point(x, Y(Values[i])), _valueEdge, _valueEdge);
        }

        // rubber band
        if (_mouse1 || _mouse2)
        {
            double rx = Math.Min(_press.X, _move.X), ry = Math.Min(_press.Y, _move.Y);
            dc.DrawRectangle(RubberFill, RubberPen, new Rect(rx, ry, Math.Abs(_move.X - _press.X), Math.Abs(_move.Y - _press.Y)));
        }
    }

    private static void Triangle(DrawingContext dc, Point a, Point b, Point c)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open()) { g.BeginFigure(a, true, true); g.LineTo(b, true, false); g.LineTo(c, true, false); }
        geo.Freeze();
        dc.DrawGeometry(PlayBrush, null, geo);
    }

    // ---------- mouse ----------

    private bool HitPoint(int i, Point p)
    {
        int x = X(i);
        if (Values[i] == -1) return false;
        double y = Y(Values[i]);
        return x - _valueEdge < p.X && p.X < x + _valueEdge && y - _valueEdge < p.Y && p.Y < y + _valueEdge;
    }

    private bool HitColumn(int i, double mx)
    {
        int x = X(i);
        return x - _valueEdge < mx && mx < x + _valueEdge;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        if (_rebuild && e.ChangedButton != MouseButton.Right && e.ChangedButton != MouseButton.Middle) return;

        if (e.ChangedButton == MouseButton.Left && !ctrl && !shift && !alt)
        {
            _mouse1 = true; CaptureMouse();
            _press = _move = e.GetPosition(this);
            _selected.Clear(); _selectedTimes.Clear();
            for (int i = 0; i < Values.Count; i++)
            {
                if (HitPoint(i, _press) && !_selected.Contains(i)) _selected.Add(i);
                if (HitColumn(i, _press.X) && !_selectedTimes.Contains(i)) _selectedTimes.Add(i);
            }
            _oldValues = new List<int>(Values);
        }
        else if (e.ChangedButton == MouseButton.Left && ctrl && !shift && !alt)
        {
            _mouse1 = true; CaptureMouse();
            _press = _move = e.GetPosition(this);
            for (int i = 0; i < Values.Count; i++)
            {
                if (HitPoint(i, _press)) { if (!_selected.Remove(i)) _selected.Add(i); }
                if (HitColumn(i, _press.X)) { if (!_selectedTimes.Remove(i)) _selectedTimes.Add(i); }
            }
        }
        else if (e.ChangedButton == MouseButton.Left && shift && !ctrl && !alt)
        {
            _mouse1 = true; CaptureMouse();
            _press = _move = e.GetPosition(this);
            _oldValues = new List<int>(Values);
        }
        else if (e.ChangedButton == MouseButton.Middle)
        {
            _mouse3 = true; CaptureMouse();
            ScrubTo(e.GetPosition(this).X);
        }
        else if (e.ChangedButton == MouseButton.Right && !ctrl && !shift && !alt)
        {
            _valueBox.Text = "";
            _menu.IsOpen = true;
        }
        else if (e.ChangedButton == MouseButton.Right && shift && !ctrl && !alt)
        {
            _mouse2 = true; CaptureMouse();
            _press = _move = e.GetPosition(this);
            _oldValues = new List<int>(Values);
            _oldSelected = new List<int>(_selected);
        }
        else if (e.ChangedButton == MouseButton.Right && ctrl && !shift && !alt)
        {
            _mouse2 = true; CaptureMouse();
            _press = _move = e.GetPosition(this);
            var p = e.GetPosition(this);
            for (int i = 0; i < Values.Count; i++)
            {
                if (!HitColumn(i, p.X)) continue;
                if (Values[i] == -1)
                {
                    int value = (int)((p.Y - ActualHeight + _valueEdge + Pad) / ((ActualHeight - _valueEdge * 2 - Pad * 2) / -999.0));
                    if (value >= 0 && value <= 999) Values[i] = value;
                }
                else
                {
                    double y = Y(Values[i]);
                    if (y - _valueEdge < p.Y && p.Y < y + _valueEdge) Values[i] = -1;
                }
            }
            InvalidateVisual();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (_rebuild)
        {
            _rebuildEnd = p;
            _rebuildTimes.Clear();
            double lo = Math.Min(_rebuildStart.X, _rebuildEnd.X), hi = Math.Max(_rebuildStart.X, _rebuildEnd.X);
            for (int i = 0; i < Values.Count; i++) { int x = X(i); if (lo < x && x < hi) _rebuildTimes.Add(i); }
            InvalidateVisual(); return;
        }
        if (_mouse1 && !shift && !ctrl)
        {
            _move = p;
            _selected.Clear(); _selectedTimes.Clear();
            RectSelect(add: true);
            InvalidateVisual();
        }
        else if (_mouse1 && shift && !ctrl)
        {
            int movePos = (int)(p.Y - _press.Y);
            foreach (var s in _selected)
            {
                if (Values[s] == -1) continue;
                int value = _oldValues[s];
                int add = (int)(movePos / ((ActualHeight - _valueEdge * 2 - Pad * 2) / -999.0));
                if (value + add > 999 || value + add < 0) continue;
                Values[s] = value + add;
            }
            InvalidateVisual();
        }
        else if (_mouse1 && ctrl)
        {
            _move = p; RectSelect(add: true); InvalidateVisual();
        }
        else if (_mouse2 && shift && !ctrl)
        {
            for (int i = 0; i < Values.Count; i++)
            {
                if (!HitColumn(i, p.X)) continue;
                _moveIndex = (int)((p.X - _press.X) / Intervals);
                if (_moveIndex == 0 || _lastMoveIndex == _moveIndex) return;
                _lastMoveIndex = _moveIndex;
                var res = ScripterOps.MoveHorizontal(Values, _oldValues, _selected, _oldSelected, _moveIndex);
                if (res != null) { _selected.Clear(); _selected.AddRange(res); }
                InvalidateVisual(); return;
            }
        }
        else if (_mouse2 && ctrl && !shift)
        {
            _move = p; RectSelect(add: false); InvalidateVisual();
        }
        else if (_mouse3)
        {
            ScrubTo(p.X);
        }
    }

    private void RectSelect(bool add)
    {
        double loX = Math.Min(_press.X, _move.X), hiX = Math.Max(_press.X, _move.X);
        double loY = Math.Min(_press.Y, _move.Y), hiY = Math.Max(_press.Y, _move.Y);
        for (int i = 0; i < Values.Count; i++)
        {
            int x = X(i);
            bool inX = loX < x && x < hiX;
            if (!inX) continue;
            if (Values[i] != -1)
            {
                double y = Y(Values[i]);
                bool inY = loY < y && y < hiY;
                if (inY)
                {
                    if (add) { if (!_selected.Contains(i)) _selected.Add(i); }
                    else _selected.Remove(i);
                }
            }
            if (add) { if (!_selectedTimes.Contains(i)) _selectedTimes.Add(i); }
            else _selectedTimes.Remove(i);
        }
    }

    private void ScrubTo(double mx)
    {
        for (int i = 0; i < Values.Count; i++)
            if (HitColumn(i, mx)) { SelectedLine = i; CurrentLine?.Invoke(i); InvalidateVisual(); return; }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        _press = _move = e.GetPosition(this);
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (_mouse1 && shift && !_oldValues.SequenceEqual(Values)) PushUndo(_oldValues);
        _mouse1 = _mouse2 = _mouse3 = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
    }

    // ---------- keyboard ----------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (shift && e.Key == Key.Up) { BeginMove(); ScripterOps.ShiftValue(Values, _selected, 10); InvalidateVisual(); }
        else if (shift && e.Key == Key.Down) { BeginMove(); ScripterOps.ShiftValue(Values, _selected, -10); InvalidateVisual(); }
        else if (shift && e.Key == Key.Left) { BeginMove(); MoveSelected(-1); }
        else if (shift && e.Key == Key.Right) { BeginMove(); MoveSelected(1); }
        else if (ctrl && e.Key == Key.Left) ZoomOut();
        else if (ctrl && e.Key == Key.Right) ZoomIn();
        else if (ctrl && e.Key == Key.A) DoSelectAll();
        else if (ctrl && e.Key == Key.F) DoRemoveDuplicateStacks();
        else if (ctrl && e.Key == Key.D) DoDelete();
        else if (ctrl && e.Key == Key.E) DoAddLines();
        else if (ctrl && e.Key == Key.D1) DoSelectPeaks();
        else if (ctrl && e.Key == Key.D2) DoSelectMidpoints();
        else if (ctrl && e.Key == Key.D3) DoSelectValleys();
        else if (ctrl && e.Key == Key.C) DoCopy();
        else if (ctrl && e.Key == Key.X) DoCut();
        else if (ctrl && e.Key == Key.V) DoPaste();
        else if (ctrl && e.Key == Key.Z) DoUndo();
        else if (ctrl && e.Key == Key.PageUp) { BeginMove(); DoAmplify(1.1); }
        else if (ctrl && e.Key == Key.PageDown) { BeginMove(); DoAmplify(0.9); }
        else if (e.Key == Key.Space) SetPlay?.Invoke();
        else if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt) ToggleRebuild();
        else return;
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.PageUp or Key.PageDown)
            _moveFirst = true;
        if (!_oldValues.SequenceEqual(Values)) PushUndo(_oldValues);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Delta > 0) ZoomIn(); else ZoomOut();
    }

    private void BeginMove() { if (_moveFirst) { _moveFirst = false; _oldValues = new List<int>(Values); } }

    private void MoveSelected(int delta)
    {
        _moveIndex = delta; _lastMoveIndex = delta;
        var now = new List<int>();
        foreach (var s in _selected)
        {
            int ni = s + delta;
            if (ni <= 0 || ni >= Values.Count - 1) return;
            now.Add(ni);
            Values[ni] = Values[s];
            Values[s] = -1;
        }
        _selected.Clear(); _selected.AddRange(now.Distinct());
        InvalidateVisual();
    }

    private void ToggleRebuild()
    {
        _mouse1 = _mouse2 = _mouse3 = false;
        _selected.Clear(); _selectedTimes.Clear();
        if (!_rebuild) { _rebuild = true; _rebuildStart = Mouse.GetPosition(this); _rebuildTimes.Clear(); }
        else { _rebuildTimes.Clear(); _rebuild = false; }
        InvalidateVisual();
    }

    private void ZoomIn() { if (Intervals + 5 > 200) return; Intervals += 5; UpdateEdge(); Refresh(); }
    private void ZoomOut() { if (Intervals - 5 > 5) Intervals -= 5; else if (Intervals - 1 > 1) Intervals -= 1; UpdateEdge(); Refresh(); }
    private void UpdateEdge() { if (Intervals <= 100) _valueEdge = (int)((5 - 3) / (100.0 - 2) * Intervals + 3 - (5 - 3) / (100.0 - 2) * 2); }

    // ---------- operations (shared by menu + keyboard) ----------

    private void DoSelectAll() { var (sv, st) = ScripterOps.SelectAll(Values); _selected.Clear(); _selected.AddRange(sv); _selectedTimes.Clear(); _selectedTimes.AddRange(st); InvalidateVisual(); }
    private void DoSelectPeaks() { Clean(); var r = ScripterOps.SelectPeaks(Values, _selected); _selected.Clear(); _selected.AddRange(r); InvalidateVisual(); }
    private void DoSelectValleys() { Clean(); var r = ScripterOps.SelectValleys(Values, _selected); _selected.Clear(); _selected.AddRange(r); InvalidateVisual(); }
    private void DoSelectMidpoints() { Clean(); var r = ScripterOps.SelectMidpoints(Values, _selected); _selected.Clear(); _selected.AddRange(r); InvalidateVisual(); }
    private void DoSelectInterval() { Clean(); var r = ScripterOps.SelectInterval(_selected); _selected.Clear(); _selected.AddRange(r); InvalidateVisual(); }

    private void DoDelete() { var snap = new List<int>(Values); ScripterOps.DeleteSelected(Values, _selected); if (!snap.SequenceEqual(Values)) PushUndo(snap); _selected.Clear(); InvalidateVisual(); }
    private void DoAddLines() { var snap = new List<int>(Values); ScripterOps.AddSelectedLines(Values, _selectedTimes); if (!snap.SequenceEqual(Values)) PushUndo(snap); InvalidateVisual(); }
    private void DoChangeValues() { if (!int.TryParse(_valueBox.Text, out var v)) return; var snap = new List<int>(Values); ScripterOps.ChangeValues(Values, _selected, v); if (!snap.SequenceEqual(Values)) PushUndo(snap); InvalidateVisual(); }
    private void DoAmplify(double f) { Clean(); var snap = new List<int>(Values); ScripterOps.Amplify(Values, _selected, f); if (!snap.SequenceEqual(Values)) PushUndo(snap); InvalidateVisual(); }
    private void DoReverse() { Clean(); var snap = new List<int>(Values); ScripterOps.Reverse(Values, _selected); if (!snap.SequenceEqual(Values)) PushUndo(snap); InvalidateVisual(); }

    private void DoRemoveDuplicateStacks()
    {
        if (_selected.Count < 2) return;
        Clean();
        PushUndo(Values);
        var r = ScripterOps.RemoveDuplicateStacks(Values, _selected);
        _selected.Clear(); _selected.AddRange(r); InvalidateVisual();
    }

    private void DoCopy() { Clean(); (_copyValues, _copyIndexs) = ScripterOps.Copy(Values, _selected); GetCopyValues?.Invoke(_copyValues, _copyIndexs); }
    private void DoCut() { Clean(); (_copyValues, _copyIndexs) = ScripterOps.Copy(Values, _selected); GetCopyValues?.Invoke(_copyValues, _copyIndexs); var snap = new List<int>(Values); ScripterOps.DeleteSelected(Values, _selected); PushUndo(snap); InvalidateVisual(); }
    private void DoPaste() { var snap = new List<int>(Values); ScripterOps.Paste(Values, SelectedLine, _copyValues, _copyIndexs); if (!snap.SequenceEqual(Values)) PushUndo(snap); InvalidateVisual(); }
    private void DoUndo() { if (_undo.Count < 1) return; Values = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); Refresh(); }
    private void DoRebuild() { RebuildTimes?.Invoke(new List<int>(_rebuildTimes)); _rebuild = false; _rebuildTimes.Clear(); InvalidateVisual(); }

    private void Clean() => ScripterOps.CleanAndSort(Values, _selected);

    // ---------- context menu ----------

    private (ContextMenu, TextBox) BuildMenu()
    {
        var menu = new ContextMenu();
        var box = new TextBox { MinWidth = 160, Margin = new Thickness(6, 4, 6, 4) };
        menu.Items.Add(new MenuItem { Header = box, StaysOpenOnClick = true, IsHitTestVisible = true });
        menu.Items.Add(new Separator());

        void Add(string header, Action act) { var mi = new MenuItem { Header = header }; mi.Click += (_, _) => act(); menu.Items.Add(mi); }

        Add("add selected lines point (Ctrl+E)", DoAddLines);
        Add("select all point (Ctrl+A)", DoSelectAll);
        Add("select top points (Ctrl+1)", DoSelectPeaks);
        Add("select midpoints (Ctrl+2)", DoSelectMidpoints);
        Add("select down points (Ctrl+3)", DoSelectValleys);
        Add("change selected point values", DoChangeValues);
        Add("select intervals points", DoSelectInterval);
        Add("remove duplicate stacks (Ctrl+F)", DoRemoveDuplicateStacks);
        Add("delete selected point (Ctrl+D)", DoDelete);
        Add("rebuild selected times", DoRebuild);
        Add("reverse selected point values", DoReverse);
        Add("enlarge selected point values (Ctrl+PgUp)", () => DoAmplify(1.1));
        Add("decrease selected point values (Ctrl+PgDn)", () => DoAmplify(0.9));
        Add("copy selected point values (Ctrl+C)", DoCopy);
        Add("cut selected point values (Ctrl+X)", DoCut);
        Add("paste selected point values (Ctrl+V)", DoPaste);
        Add("withdraw changes of points (Ctrl+Z)", DoUndo);
        return (menu, box);
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(double.IsNaN(Width) ? (Values.Count - 1) * Intervals + _valueEdge * 2 + Pad * 2 : Width, Height);

    private static Brush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Pen FreezePen(Color c, double w) { var p = new Pen(new SolidColorBrush(c), w); p.Freeze(); return p; }
}
