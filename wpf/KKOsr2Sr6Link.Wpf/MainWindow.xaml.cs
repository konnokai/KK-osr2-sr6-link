using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using KKOsr2Sr6Link.Wpf.Controls;
using KKOsr2Sr6Link.Wpf.Engine;

namespace KKOsr2Sr6Link.Wpf;

public partial class MainWindow : Window
{
    private readonly AppConfig _cfg = new();
    private readonly LinkServer _server = new();
    private readonly SerialOutput _serial = new();
    private readonly ButtplugClient _buttplug = new();
    private readonly PlaybackEngine _engine = new();

    private readonly ScripterEdit[] _editors = new ScripterEdit[6];
    private readonly RangeSlider[] _sliders = new RangeSlider[6];
    private OverviewEdit _overview = null!;
    private CheckBox[] _enables = Array.Empty<CheckBox>();

    private string _filePath = "";   // resolved scene .txt path currently loaded
    private List<ScenePart> _sceneParts = new();
    private List<LovemakingData> _lovemakingDatas = new(); // raw captured streams, for "generates"
    private bool _serverRunning;

    public MainWindow()
    {
        InitializeComponent();
        BuildScripterSurface();
        WireEvents();
        LoadConfigIntoUi();
        ReloadPorts_Click(this, new RoutedEventArgs());
        Closing += (_, _) => Cleanup();
    }

    // ---------- setup ----------

    private void BuildScripterSurface()
    {
        _overview = new OverviewEdit();
        _overview.CurrentLine += OnScrub;
        _overview.SetPlay += OnSetPlay;
        SurfaceHost.Content = _overview; // Overview tab is selected by default

        _enables = new[] { EnL0, EnL1, EnL2, EnR0, EnR1, EnR2 };
        for (int a = 0; a < 6; a++)
        {
            int axis = a;
            var slider = new RangeSlider { HorizontalAlignment = HorizontalAlignment.Stretch };
            slider.ValueChanged += () => { _engine.MinValue[axis] = slider.MinValue; _engine.MaxValue[axis] = slider.MaxValue; };
            slider.RangeChanged += () => _cfg.SetOutputRange(axis, slider.MinValue, slider.MaxValue);
            _sliders[a] = slider;

            var editor = new ScripterEdit();
            editor.CurrentLine += OnScrub;
            editor.SetPlay += OnSetPlay;
            editor.GetCopyValues += DistributeClipboard;
            editor.RebuildTimes += times => Status($"rebuild requested for {times.Count} frames (regeneration not yet wired)");
            _editors[a] = editor;

            // label + slider row in the output-range card (L* left column, R* right)
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock { Text = ((Axis)a).ToString(), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(slider, 1);
            row.Children.Add(label);
            row.Children.Add(slider);
            (a < 3 ? SliderColLeft : SliderColRight).Children.Add(row);

            _enables[a].Checked += (_, _) => _engine.Enabled[axis] = true;
            _enables[a].Unchecked += (_, _) => _engine.Enabled[axis] = false;
            // Persist only on user click (not the programmatic load in LoadConfigIntoUi).
            _enables[axis].Click += (_, _) => _cfg.SetAxisEnabled(axis, _enables[axis].IsChecked == true);
        }
    }

    // Overview / L0..R2 tab selection: swap the surface shown in the host.
    private void ScripterTab_Checked(object sender, RoutedEventArgs e)
    {
        if (_overview == null) return; // fires once during InitializeComponent, before build
        int tag = int.Parse((string)((RadioButton)sender).Tag);
        FrameworkElement surface = tag < 0 ? _overview : _editors[tag];
        SurfaceHost.Content = surface;
        surface.InvalidateVisual();
    }

    private void Generate_Click(object s, RoutedEventArgs e)
    {
        if (_lovemakingDatas.Count == 0 || _editors[0].Values.Count == 0) { Status("generate: no scene loaded"); return; }
        try { RegenerateScripter(); Status($"regenerated axes for {_sceneParts.Count} parts"); }
        catch (Exception ex) { Status("generate failed: " + ex.Message); }
    }

    // Re-derive all six axes for every scene part from the captured raw streams,
    // honouring each part's chara + lovemaking mode. Mirrors mainwindow.cpp regenerate_scripter().
    private void RegenerateScripter()
    {
        int len = _editors[0].Values.Count;
        for (int p = 0; p < _sceneParts.Count; p++)
        {
            int partBegin = _sceneParts[p].Part;
            int partEnd = (p == _sceneParts.Count - 1) ? len : _sceneParts[p + 1].Part;

            var d = _lovemakingDatas.FirstOrDefault(x => x.CharasName == _sceneParts[p].Charas);
            if (d == null) continue;
            var (ins, su, sw, tw, ro, pi) = SourceStreams(d, _sceneParts[p].LovemakingMode);

            partEnd = Math.Min(partEnd, Math.Min(ins.Count, len));
            int n = partEnd - partBegin;
            if (n <= 0) continue;

            float segMax = float.MinValue, segMin = float.MaxValue, surgeSum = 0, swaySum = 0;
            for (int i = partBegin; i < partEnd; i++)
            {
                if (ins[i] > segMax) segMax = ins[i];
                if (ins[i] < segMin) segMin = ins[i];
                surgeSum += su[i]; swaySum += sw[i];
            }
            float crange = segMin - segMax;
            float surgeOffset = surgeSum / n, swayOffset = swaySum / n, bw = d.BodyWidth;

            for (int i = partBegin; i < partEnd; i++)
            {
                _editors[0].Values[i] = Clamp(crange == 0 ? 0 : (int)((999f / crange) * ins[i] - (999f / crange) * segMax));
                _editors[1].Values[i] = Clamp(bw == 0 ? 500 : 999 / 2 - (int)((su[i] - surgeOffset) * 999f / bw / 2f));
                _editors[2].Values[i] = Clamp(bw == 0 ? 500 : 999 / 2 - (int)((sw[i] - swayOffset) * 999f / bw / 2f));
                _editors[3].Values[i] = Clamp(999 / 2 + (int)(tw[i] * 11.1f));
                _editors[4].Values[i] = Clamp(999 / 2 - (int)(ro[i] * 11.1f));
                _editors[5].Values[i] = Clamp(999 / 2 + (int)(pi[i] * 11.1f / 2f));
            }
        }
        _overview.Values = _editors[0].Values;
        for (int a = 0; a < 6; a++) _editors[a].Refresh();
        _overview.Refresh();
    }

    private static int Clamp(int v) => v < 0 ? 0 : v > 999 ? 999 : v;

    // The raw stream set a part uses depends on its lovemaking mode (mainwindow.cpp:1800-1812).
    private static (List<float>, List<float>, List<float>, List<float>, List<float>, List<float>)
        SourceStreams(LovemakingData d, string mode) => mode switch
        {
            "blowjob"   => (d.BlowjobInserts,   d.BlowjobSurges,   d.BlowjobSways,   d.BlowjobTwists,   d.BlowjobRolls,   d.BlowjobPitchs),
            "breastsex" => (d.BreastsexInserts, d.BreastsexSurges, d.BreastsexSways, d.BreastsexTwists, d.BreastsexRolls, d.BreastsexPitchs),
            "handjobL"  => (d.HandjobLInserts,  d.HandjobLSurges,  d.HandjobLSways,  d.HandjobLTwists,  d.HandjobLRolls,  d.HandjobLPitchs),
            "handjobR"  => (d.HandjobRInserts,  d.HandjobRSurges,  d.HandjobRSways,  d.HandjobRTwists,  d.HandjobRRolls,  d.HandjobRPitchs),
            _           => (d.Inserts,          d.Surges,          d.Sways,          d.Twists,          d.Rolls,          d.Pitchs),
        };

    // Serial device / Buttplug device tab toggle.
    private void DeviceTab_Checked(object sender, RoutedEventArgs e)
    {
        if (SerialPanel == null) return; // fires during InitializeComponent, before panels exist
        bool serial = (string)((RadioButton)sender).Tag == "serial";
        SerialPanel.Visibility = serial ? Visibility.Visible : Visibility.Collapsed;
        ButtplugPanel.Visibility = serial ? Visibility.Collapsed : Visibility.Visible;
    }

    // Show the axis currently mapped to the selected device feature.
    private void FeatureList_Changed(object s, SelectionChangedEventArgs e)
    {
        int d = DeviceList.SelectedIndex, f = FeatureList.SelectedIndex;
        if (d < 0 || d >= _buttplug.Devices.Count || f < 0) return;
        AxisMapList.SelectedIndex = _buttplug.Devices[d].Feature[f];
    }

    // Map the selected feature to an axis and enable it.
    private void AxisMap_Changed(object s, SelectionChangedEventArgs e)
    {
        int d = DeviceList.SelectedIndex, f = FeatureList.SelectedIndex, a = AxisMapList.SelectedIndex;
        if (d < 0 || d >= _buttplug.Devices.Count || f < 0 || a < 0) return;
        var dev = _buttplug.Devices[d];
        dev.Feature[f] = a;
        dev.FeatureEnable[f] = 1;
    }

    private void WireEvents()
    {
        _server.MessageReceived += msg => Dispatcher.Invoke(() => OnSceneMessage(msg));
        _server.ClientConnected += () => Dispatcher.Invoke(() => ClientLabel.Text = "link from: " + _server.ClientAddress);
        _server.ClientDisconnected += () => Dispatcher.Invoke(() => ClientLabel.Text = "client disconnected");

        _buttplug.DevicesChanged += () => Dispatcher.Invoke(RefreshDeviceList);
        _buttplug.Connected += () => Dispatcher.Invoke(() => Status("Intiface connected"));
        _buttplug.Disconnected += () => Dispatcher.Invoke(() => Status("Intiface disconnected"));
        _buttplug.Error += e => Dispatcher.Invoke(() => Status("Intiface: " + e));
        _serial.Error += e => Dispatcher.Invoke(() => Status("Serial: " + e));

        _engine.Serial = _serial;
        _engine.Buttplug = _buttplug;

        // only push the selected chara to the game when the user opted in
        GirlList.SelectionChanged += (_, _) => { if (SelectOnSwitchCheck.IsChecked == true && GirlList.SelectedItem is string g) _server.SendSelectChara(g); };
        BoyList.SelectionChanged += (_, _) => { if (SelectOnSwitchCheck.IsChecked == true && BoyList.SelectedItem is string b) _server.SendSelectChara(b); };
        ModeList.SelectionChanged += (_, _) => Status("mode: " + (ModeList.SelectedItem as ComboBoxItem)?.Content);
    }

    private void LoadConfigIntoUi()
    {
        BaudBox.Text = _cfg.BaudRate;
        ServerIpBox.Text = _cfg.ServerIp;
        ServerPortBox.Text = _cfg.ServerPort;
        WebIpBox.Text = _cfg.WebServerIp;
        GameRootBox.Text = _cfg.GameRoot;
        RebuildAllCheck.IsChecked = _cfg.RebuildAllAxes;

        // Per-axis output range + enable come from config.ini, not the scene scripts.
        for (int a = 0; a < 6; a++)
        {
            var (min, max) = _cfg.GetOutputRange(a);
            _sliders[a].MinValue = min;
            _sliders[a].MaxValue = max;
            _enables[a].IsChecked = _cfg.GetAxisEnabled(a);
        }

        BaudBox.LostFocus += (_, _) => _cfg.BaudRate = BaudBox.Text;
        ServerIpBox.LostFocus += (_, _) => _cfg.ServerIp = ServerIpBox.Text;
        ServerPortBox.LostFocus += (_, _) => _cfg.ServerPort = ServerPortBox.Text;
        WebIpBox.LostFocus += (_, _) => _cfg.WebServerIp = WebIpBox.Text;
        GameRootBox.LostFocus += (_, _) => _cfg.GameRoot = GameRootBox.Text;
        RebuildAllCheck.Click += (_, _) => _cfg.RebuildAllAxes = RebuildAllCheck.IsChecked == true;
    }

    // ---------- navigation / title bar ----------

    private void NavHome_Click(object s, RoutedEventArgs e) => ShowPage(PageHome);
    private void NavScripter_Click(object s, RoutedEventArgs e) => ShowPage(PageScripter);
    private void NavSettings_Click(object s, RoutedEventArgs e) => ShowPage(PageSettings);

    private void ShowPage(UIElement page)
    {
        PageHome.Visibility = PageScripter.Visibility = PageSettings.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
    }

    private void Minimize_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object s, RoutedEventArgs e) => Close();

    // ---------- server / playback ----------

    private void RunServer_Click(object s, RoutedEventArgs e)
    {
        if (_serverRunning) { _server.Stop(); _serverRunning = false; RunServerBtn.Content = "Run server"; ClientLabel.Text = "server stopped"; return; }
        try
        {
            int port = int.TryParse(ServerPortBox.Text, out var p) ? p : 8000;
            _server.Start(ServerIpBox.Text, port);
            _serverRunning = true;
            RunServerBtn.Content = "Stop server";
            ClientLabel.Text = $"listening {ServerIpBox.Text}:{port}";
        }
        catch (Exception ex) { Status("server start failed: " + ex.Message); }
    }

    private void OnSceneMessage(SceneMessage msg)
    {
        try
        {
            if (_filePath != msg.Path)
                LoadScene(msg.Path);

            int index = msg.Index;
            if (_editors[0].Values.Count == 0 || index + 1 >= _editors[0].Values.Count) return;

            // keep engine pointed at the live editor data + ranges
            for (int a = 0; a < 6; a++)
            {
                _engine.AxisValues[a] = _editors[a].Values;
                _engine.MinValue[a] = _sliders[a].MinValue;
                _engine.MaxValue[a] = _sliders[a].MaxValue;
                _engine.Enabled[a] = _enables[a].IsChecked == true;
            }
            _engine.SerialEnabled = _serial.IsOpen;
            _engine.Dispatch(index);

            // move playheads + reflect scaled values on sliders
            _overview.SelectedLine = index; _overview.InvalidateVisual();
            for (int a = 0; a < 6; a++)
            {
                _editors[a].SelectedLine = index; _editors[a].InvalidateVisual();
                if (_engine.CurrentScaled[a] >= 0) { _sliders[a].Value = _engine.CurrentScaled[a]; }
            }
        }
        catch (Exception ex) { Status("scene message error: " + ex.Message); }
    }

    private void OnScrub(int index)
    {
        _overview.SelectedLine = index; _overview.InvalidateVisual();
        for (int a = 0; a < 6; a++) { _editors[a].SelectedLine = index; _editors[a].InvalidateVisual(); }
        _server.SendSeek(index);
    }

    private void OnSetPlay() => _server.SendPlay();

    private void DistributeClipboard(List<int> values, List<int> indexs)
    {
        foreach (var ed in _editors) ed.SetClipboard(values, indexs);
    }

    // ---------- scene load / save ----------

    private void LoadScene(string rawPath)
    {
        string path = rawPath;

        // preview image: KK_osr_sr6_link -> Studio/scene, .txt -> .png
        try
        {
            string previewPath = path.Replace("KK_osr_sr6_link", "Studio/scene").Replace(".txt", ".png");
            if (File.Exists(previewPath))
            {
                // OnLoad reads the whole image now and releases the file handle,
                // instead of streaming lazily and keeping the .png locked.
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(previewPath);
                bmp.EndInit();
                ScenePreview.Source = bmp;
            }
        }
        catch { /* preview is best-effort */ }

        // resolve path against game root (mirrors server_read:864-873)
        const string search = "/UserData/KK_osr_sr6_link/";
        int idx = path.IndexOf(search, StringComparison.Ordinal);
        if (idx != -1 && !string.IsNullOrEmpty(GameRootBox.Text))
            path = GameRootBox.Text + path.Substring(idx);
        else if (idx == -1)
        {
            Status("scene path not under UserData/KK_osr_sr6_link/: " + rawPath);
            return;
        }

        _filePath = rawPath; // keep raw as the identity used by the plugin
        ScenePathLabel.Text = path;

        var girls = new List<string>();
        var boys = new List<string>();

        string baseScript = AxisInfo.Sr6ScriptPath(path, Axis.L0);
        if (File.Exists(baseScript))
        {
            // load saved per-axis scripts
            for (int a = 0; a < 6; a++)
            {
                var sp = SceneFiles.LoadSr6Script(AxisInfo.Sr6ScriptPath(path, (Axis)a));
                _editors[a].Values = sp?.Values ?? new List<int>();
                // range comes from config.ini [Output Range], not the scene script
            }
            _sceneParts = SceneFiles.LoadSr6Cfg(AxisInfo.Sr6CfgPath(path));
        }
        else if (File.Exists(path))
        {
            // fresh scene: parse txt, derive axes, save
            var scene = SceneTxtParser.Parse(File.ReadAllText(path));
            if (!scene.IsNewVersion || scene.Data.Count == 0)
            {
                Status("old-version or empty scene; re-collect with the latest plugin.");
                return;
            }
            var axes = SceneTxtParser.ComputeInitialAxes(scene.Data[0]);
            for (int a = 0; a < 6; a++) { _editors[a].Values = axes[a].Values; } // range stays as loaded from config.ini
            foreach (var d in scene.Data)
            {
                var parts = d.CharasName.Split('-');
                if (parts.Length == 2) { girls.Add(parts[0]); boys.Add(parts[1]); }
            }
            _sceneParts = new List<ScenePart>
            {
                new() { Part = 0, LovemakingMode = "normal",
                        Charas = (girls.FirstOrDefault() ?? "") + "-" + (boys.FirstOrDefault() ?? "") }
            };
            SaveScripter(path);
        }
        else
        {
            Status("scene file not found: " + path);
            return;
        }

        // combos + parts from scene parts
        foreach (var sp in _sceneParts)
        {
            var pr = sp.Charas.Split('-');
            if (pr.Length == 2) { girls.Add(pr[0]); boys.Add(pr[1]); }
        }
        FillCombo(GirlList, girls);
        FillCombo(BoyList, boys);

        // overview tracks L0; split lines from parts (skip first)
        _overview.Values = _editors[0].Values;
        _overview.SplitLines = _sceneParts.Skip(1).Select(p => p.Part).ToList();
        PartList.Items.Clear();
        for (int i = 0; i < _sceneParts.Count; i++) PartList.Items.Add("part" + (i + 1));

        for (int a = 0; a < 6; a++) _editors[a].Refresh();
        _overview.Refresh();
        Status("loaded " + Path.GetFileName(path));
    }

    private void SaveScripter(string path)
    {
        for (int a = 0; a < 6; a++)
            SceneFiles.SaveSr6Script(AxisInfo.Sr6ScriptPath(path, (Axis)a),
                new AxisScript { Values = _editors[a].Values, MaxValue = _sliders[a].MaxValue, MinValue = _sliders[a].MinValue });
        SceneFiles.SaveSr6Cfg(AxisInfo.Sr6CfgPath(path), _sceneParts);
    }

    private string ResolvedPath()
    {
        string path = _filePath;
        const string search = "/UserData/KK_osr_sr6_link/";
        int idx = path.IndexOf(search, StringComparison.Ordinal);
        if (idx != -1 && !string.IsNullOrEmpty(GameRootBox.Text))
            path = GameRootBox.Text + path.Substring(idx);
        return path;
    }

    private void Save_Click(object s, RoutedEventArgs e)
    {
        if (_filePath == "") { Status("no scene loaded"); return; }
        try { SaveScripter(ResolvedPath()); Status("saved scripts"); }
        catch (Exception ex) { Status("save failed: " + ex.Message); }
    }

    private void Convert_Click(object s, RoutedEventArgs e)
    {
        if (_filePath == "") { Status("no scene loaded"); return; }
        try
        {
            string path = ResolvedPath();
            SaveScripter(path);
            int refCount = _editors[0].Values.Count;
            for (int a = 0; a < 6; a++)
                SceneFiles.ExportFunscript(AxisInfo.FunscriptPath(path, (Axis)a), _editors[a].Values, refCount);
            Status("exported 6 funscripts");
        }
        catch (Exception ex) { Status("export failed: " + ex.Message); }
    }

    private void OpenFolder_Click(object s, RoutedEventArgs e)
    {
        if (_filePath == "") return;
        try
        {
            var dir = Path.GetDirectoryName(ResolvedPath());
            if (dir != null && Directory.Exists(dir))
                System.Diagnostics.Process.Start("explorer.exe", dir);
        }
        catch (Exception ex) { Status("open folder failed: " + ex.Message); }
    }

    private void ShowChars_Click(object s, RoutedEventArgs e) => _server.SendShow(GirlList.Text, BoyList.Text);
    private void HideChars_Click(object s, RoutedEventArgs e) => _server.SendHide(GirlList.Text, BoyList.Text);

    // ---------- serial ----------

    private void ReloadPorts_Click(object s, RoutedEventArgs e)
    {
        PortList.Items.Clear();
        foreach (var p in SerialOutput.AvailablePorts()) PortList.Items.Add(p);
        if (PortList.Items.Count > 0) PortList.SelectedIndex = 0;
    }

    private void LinkSerial_Click(object s, RoutedEventArgs e)
    {
        if (_serial.IsOpen) { _serial.Close(); LinkSerialBtn.Content = "Link serial"; Status("serial closed"); return; }
        if (PortList.SelectedItem is not string port) { Status("no serial port selected"); return; }
        try
        {
            int baud = int.TryParse(BaudBox.Text, out var b) ? b : 115200;
            _serial.Open(port, baud);
            LinkSerialBtn.Content = "Unlink serial";
            Status("serial linked " + port);
        }
        catch (Exception ex) { Status("serial open failed: " + ex.Message); }
    }

    // ---------- intiface ----------

    private async void LinkIntiface_Click(object s, RoutedEventArgs e)
    {
        if (_buttplug.IsConnected) { await _buttplug.DisconnectAsync(); LinkIntifaceBtn.Content = "Connect"; return; }
        try { await _buttplug.ConnectAsync(WebIpBox.Text); LinkIntifaceBtn.Content = "Disconnect"; }
        catch (Exception ex) { Status("intiface connect failed: " + ex.Message); }
    }

    private async void Rescan_Click(object s, RoutedEventArgs e)
    {
        try { await _buttplug.StartScanningAsync(); Status("scanning…"); }
        catch (Exception ex) { Status("scan failed: " + ex.Message); }
    }

    private void RefreshDeviceList()
    {
        DeviceList.Items.Clear();
        foreach (var d in _buttplug.Devices)
            DeviceList.Items.Add($"{d.Name}({d.Index}){(d.IsLinear ? "" : " [unsupported]")}");
        if (DeviceList.Items.Count > 0 && DeviceList.SelectedIndex < 0) DeviceList.SelectedIndex = 0;
    }

    private void DeviceList_Changed(object s, SelectionChangedEventArgs e)
    {
        FeatureList.Items.Clear();
        int i = DeviceList.SelectedIndex;
        if (i < 0 || i >= _buttplug.Devices.Count) return;
        var dev = _buttplug.Devices[i];
        for (int f = 0; f < dev.Feature.Count; f++) FeatureList.Items.Add($"feature {f}");
    }

    // ---------- misc ----------

    private void BrowseGameRoot_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog();
        if (dlg.ShowDialog() == true) { GameRootBox.Text = dlg.FolderName; _cfg.GameRoot = dlg.FolderName; }
    }

    private static void FillCombo(ComboBox combo, IEnumerable<string> items)
    {
        combo.Items.Clear(); // drop the previous scene's chars before refilling
        foreach (var it in items.Distinct()) combo.Items.Add(it);
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void Status(string text) => StatusBar.Text = text;

    private void Cleanup()
    {
        _server.Dispose();
        _serial.Dispose();
        _ = _buttplug.DisconnectAsync();
    }
}
