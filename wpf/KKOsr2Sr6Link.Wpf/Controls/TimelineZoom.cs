namespace KKOsr2Sr6Link.Wpf.Controls;

/// <summary>Per-frame pixel width (<c>Intervals</c>) math shared by OverviewEdit and ScripterEdit.</summary>
internal static class TimelineZoom
{
    public const double Max = 200;
    // 以前每次固定加 5 px，從 1 px 一格會直接跳成 6 倍；改成按比例縮放
    private const double Step = 1.25;

    /// <summary>Width per frame so <paramref name="frames"/> frames plus <paramref name="margins"/> fill
    /// <paramref name="viewport"/>. Kept fractional: rounding down leaves long scenes half as wide.</summary>
    public static double Fit(double viewport, double margins, int frames)
        => Math.Max(0.1, (viewport - margins) / (frames - 1));

    // 格數很少時填滿寬度本來就超過 Max，放大不能把它縮回 Max
    public static double In(double interval) => Math.Max(interval, Math.Min(Max, interval * Step));

    /// <summary>Zoom out, but not below <paramref name="min"/> (the last fit width, or 1 px if none).</summary>
    public static double Out(double interval, double min) => Math.Max(min, interval / Step);
}
