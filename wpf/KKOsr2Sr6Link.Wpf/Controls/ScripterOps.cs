using System;
using System.Collections.Generic;
using System.Linq;

namespace KKOsr2Sr6Link.Wpf.Controls;

/// <summary>
/// Pure value-curve operations for <see cref="ScripterEdit"/>, factored out of scripter_edit3.cpp's
/// context-menu/keyboard handlers so they can be unit-tested. -1 means "no point" at that frame.
/// </summary>
public static class ScripterOps
{
    /// <summary>Sort the selection and drop indices that point at deleted (-1) frames.</summary>
    public static void CleanAndSort(IReadOnlyList<int> values, List<int> selected)
    {
        selected.RemoveAll(i => i < 0 || i >= values.Count || values[i] == -1);
        selected.Sort();
    }

    public static (List<int> selectedValues, List<int> selectedTimes) SelectAll(IReadOnlyList<int> values)
    {
        var sv = new List<int>();
        var st = new List<int>();
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] != -1) sv.Add(i);
            st.Add(i);
        }
        return (sv, st);
    }

    /// <summary>Set selected (non-endpoint) frames to -1 (delete). Mutates values.</summary>
    public static void DeleteSelected(List<int> values, IEnumerable<int> selected)
    {
        foreach (var v in selected)
            if (v != 0 && v != values.Count - 1) values[v] = -1;
    }

    /// <summary>Materialise a point (value 500) at every selected-time frame that is currently empty.</summary>
    public static void AddSelectedLines(List<int> values, IEnumerable<int> selectedTimes)
    {
        foreach (var t in selectedTimes)
            if (t >= 0 && t < values.Count && values[t] == -1) values[t] = 500;
    }

    public static void ChangeValues(List<int> values, IEnumerable<int> selected, int value)
    {
        if (value < 0 || value > 999) return;
        foreach (var i in selected) values[i] = value;
    }

    /// <summary>Amplify (factor &gt; 1) or shrink (factor &lt; 1) selected values around their midpoint.</summary>
    public static void Amplify(List<int> values, List<int> selected, double factor)
    {
        var cur = selected.Select(i => values[i]).ToList();
        if (cur.Count == 0) return;
        int max = cur.Max(), min = cur.Min();
        int average = (max + min) / 2;
        for (int i = 0; i < cur.Count; i++)
        {
            int diff = (int)((cur[i] - average) * factor);
            int nv = average + diff;
            values[selected[i]] = Math.Clamp(nv, 0, 999);
        }
    }

    /// <summary>Mirror selected values around 500.</summary>
    public static void Reverse(List<int> values, List<int> selected)
    {
        foreach (var i in selected)
            values[i] = Math.Clamp(500 + (500 - values[i]), 0, 999);
    }

    public static void ShiftValue(List<int> values, IEnumerable<int> selected, int delta)
    {
        foreach (var i in selected)
        {
            int nv = values[i] + delta;
            if (nv > 999 || nv < 0) continue;
            values[i] = nv;
        }
    }

    public static List<int> SelectPeaks(IReadOnlyList<int> values, List<int> sortedSel)
    {
        var r = new List<int>();
        for (int i = 0; i < sortedSel.Count; i++)
        {
            int cur = values[sortedSel[i]];
            if (i == 0)
            {
                if (sortedSel.Count > 1 && cur > values[sortedSel[1]]) r.Add(sortedSel[0]);
            }
            else if (i == sortedSel.Count - 1)
            {
                if (cur > values[sortedSel[i - 1]]) r.Add(sortedSel[i]);
            }
            else
            {
                int prev = values[sortedSel[i - 1]], next = values[sortedSel[i + 1]];
                if ((cur >= prev && cur > next) || (cur > prev && cur >= next)) r.Add(sortedSel[i]);
            }
        }
        return r;
    }

    public static List<int> SelectValleys(IReadOnlyList<int> values, List<int> sortedSel)
    {
        var r = new List<int>();
        for (int i = 0; i < sortedSel.Count; i++)
        {
            int cur = values[sortedSel[i]];
            if (i == 0)
            {
                if (sortedSel.Count > 1 && cur < values[sortedSel[1]]) r.Add(sortedSel[0]);
            }
            else if (i == sortedSel.Count - 1)
            {
                if (cur < values[sortedSel[i - 1]]) r.Add(sortedSel[i]);
            }
            else
            {
                int prev = values[sortedSel[i - 1]], next = values[sortedSel[i + 1]];
                if ((cur <= prev && cur < next) || (cur < prev && cur <= next)) r.Add(sortedSel[i]);
            }
        }
        return r;
    }

    /// <summary>Midpoints = selected points that are neither endpoints, peaks, nor valleys.</summary>
    public static List<int> SelectMidpoints(IReadOnlyList<int> values, List<int> sortedSel)
    {
        var notMid = new HashSet<int>();
        for (int i = 0; i < sortedSel.Count; i++)
        {
            if (i == 0 || i == sortedSel.Count - 1) { notMid.Add(sortedSel[i]); continue; }
            int cur = values[sortedSel[i]], prev = values[sortedSel[i - 1]], next = values[sortedSel[i + 1]];
            if ((cur >= prev && cur > next) || (cur > prev && cur >= next) ||
                (cur <= prev && cur < next) || (cur < prev && cur <= next))
                notMid.Add(sortedSel[i]);
        }
        return sortedSel.Where(v => !notMid.Contains(v)).ToList();
    }

    /// <summary>action12: keep first, then every other interior point (quirky stride from the original).</summary>
    public static List<int> SelectInterval(List<int> sortedSel)
    {
        if (sortedSel.Count < 1) return new List<int>();
        var r = new List<int> { sortedSel[0] };
        for (int i = 0; i < sortedSel.Count - 1; i += 2)
        {
            if (i == 0 || i == sortedSel.Count - 1) continue;
            r.Add(sortedSel[i]);
        }
        return r;
    }

    /// <summary>Remove a run of duplicate/similar points, keeping the left-middle one selected.</summary>
    public static List<int> RemoveDuplicateStacks(List<int> values, List<int> sortedSel)
    {
        if (sortedSel.Count < 2) return sortedSel;
        int leftIndex = (int)((float)sortedSel.Count / 2);
        int leftPoint = sortedSel[leftIndex - 1];
        sortedSel.RemoveAt(leftIndex - 1);
        foreach (var idx in sortedSel) values[idx] = -1;
        return new List<int> { leftPoint };
    }

    public static (List<int> copyValues, List<int> copyIndexs) Copy(IReadOnlyList<int> values, List<int> sortedSel)
    {
        var indexs = new List<int>(sortedSel);
        var vals = new List<int>();
        if (indexs.Count == 0) return (vals, indexs);
        for (int i = indexs[0]; i <= indexs[^1]; i++)
        {
            if (!indexs.Contains(i)) { indexs.Add(i); indexs.Sort(); }
            vals.Add(values[i]);
        }
        return (vals, indexs);
    }

    public static void Paste(List<int> values, int selectedLine, List<int> copyValues, List<int> copyIndexs)
    {
        if (copyValues.Count == 0 || copyIndexs.Count == 0) return;
        for (int i = 0; i < copyValues.Count; i++)
        {
            int target = selectedLine + copyIndexs[i] - copyIndexs[0];
            if (target < values.Count - 1 && target >= 0)
                values[target] = copyValues[i];
        }
    }

    /// <summary>Frame indices [start..end] of the part that <paramref name="selectedPart"/> opens, given the
    /// sorted <paramref name="splitLines"/> (part boundaries) and curve length. start is the part's left
    /// boundary (0 for the first part), end its right boundary (last index for the final part). Matches the
    /// part the OverviewEdit highlight box covers. Empty when there are no frames.</summary>
    public static List<int> SelectSinglePart(int count, IList<int> splitLines, int selectedPart)
    {
        var r = new List<int>();
        if (count <= 0) return r;
        int last = count - 1;
        int idx = splitLines.IndexOf(selectedPart);
        int start, end;
        if (selectedPart <= 0 || idx < 0) { start = 0; end = splitLines.Count > 0 ? splitLines[0] : last; }
        else { start = selectedPart; end = idx + 1 < splitLines.Count ? splitLines[idx + 1] : last; }
        for (int i = start; i <= end && i <= last; i++) r.Add(i);
        return r;
    }

    /// <summary>Move selected points horizontally by delta, blanking the originals. Returns new selection.</summary>
    public static List<int>? MoveHorizontal(List<int> values, List<int> oldValues, List<int> selected, List<int> oldSelected, int delta)
    {
        var now = new List<int>();
        for (int i = 0; i < oldSelected.Count; i++)
        {
            int ni = oldSelected[i] + delta;
            if (ni <= 0 || ni >= values.Count - 1) return null;
            now.Add(ni);
            values[ni] = oldValues[oldSelected[i]];
            if (i < selected.Count) values[selected[i]] = -1;
        }
        return now.Distinct().ToList();
    }
}
