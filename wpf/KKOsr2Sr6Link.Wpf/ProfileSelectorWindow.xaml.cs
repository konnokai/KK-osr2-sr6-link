using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KKOsr2Sr6Link.Wpf.Engine;
using KKOsr2Sr6Link.Wpf.Localization;

namespace KKOsr2Sr6Link.Wpf;

/// <summary>
/// Selects a shared profile from a paged preview gallery. Selection only returns the key;
/// MainWindow keeps the explicit load action that changes the scene binding.
/// </summary>
public partial class ProfileSelectorWindow : Window
{
    private const int ProfilesPerPage = 12;
    private const uint MonitorDefaultToNearest = 2;
    private static readonly Size[] ResolutionSteps =
    {
        new(3840, 2160), new(2560, 1440), new(1920, 1080), new(1600, 900),
        new(1366, 768), new(1280, 720), new(1024, 576),
    };

    private readonly string _gameRoot;
    private readonly IReadOnlyList<string> _profiles;
    private readonly string _selectedKey;
    private int _page;

    public string? SelectedProfileKey { get; private set; }

    public ProfileSelectorWindow(Window owner, string gameRoot, IReadOnlyList<string> profiles, string selectedKey)
    {
        InitializeComponent();
        Owner = owner;
        _gameRoot = gameRoot;
        _profiles = profiles;
        _selectedKey = selectedKey;

        int selectedIndex = profiles.ToList().IndexOf(selectedKey);
        _page = selectedIndex < 0 ? 0 : selectedIndex / ProfilesPerPage;
        ApplyDefaultSize(owner);
        ShowPage();
    }

    /// <summary>Returns the largest common resolution below the current monitor resolution.</summary>
    public static Size SelectDefaultPixelSize(double screenWidth, double screenHeight)
    {
        foreach (var step in ResolutionSteps)
            if (step.Width <= screenWidth && step.Height <= screenHeight &&
                (step.Width < screenWidth || step.Height < screenHeight))
                return step;

        return new Size(Math.Max(1, screenWidth), Math.Max(1, screenHeight));
    }

    private void ApplyDefaultSize(Window owner)
    {
        var handle = new WindowInteropHelper(owner).Handle;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            var desired = SelectDefaultPixelSize(info.Monitor.Width, info.Monitor.Height);
            double pixelWidth = Math.Min(desired.Width, info.Work.Width);
            double pixelHeight = Math.Min(desired.Height, info.Work.Height);
            var dpi = VisualTreeHelper.GetDpi(owner);
            Width = pixelWidth / dpi.DpiScaleX;
            Height = pixelHeight / dpi.DpiScaleY;
            return;
        }

        Width = SystemParameters.WorkArea.Width;
        Height = SystemParameters.WorkArea.Height;
    }

    private void ShowPage()
    {
        int pageCount = Math.Max(1, (_profiles.Count + ProfilesPerPage - 1) / ProfilesPerPage);
        _page = Math.Clamp(_page, 0, pageCount - 1);
        ProfileGrid.ItemsSource = _profiles.Skip(_page * ProfilesPerPage).Take(ProfilesPerPage)
            .Select(key => new ProfileTile(
                key,
                LoadBitmap(AxisInfo.ProfilePreviewPath(_gameRoot, key)),
                string.Equals(key, _selectedKey, StringComparison.Ordinal)))
            .ToList();
        PageLabel.Text = Loc.T("L.ProfilePage", _page + 1, pageCount);
        PreviousPageButton.IsEnabled = _page > 0;
        NextPageButton.IsEnabled = _page + 1 < pageCount;
    }

    private void ProfileTile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not ProfileTile tile) return;
        SelectedProfileKey = tile.Key;
        DialogResult = true;
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        _page--;
        ShowPage();
    }

    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        ShowPage();
    }

    private static BitmapImage? LoadBitmap(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    private sealed record ProfileTile(string Key, BitmapImage? Preview, bool IsSelected);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
