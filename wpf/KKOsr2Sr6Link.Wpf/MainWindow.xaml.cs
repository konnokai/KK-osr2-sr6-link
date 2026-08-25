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
    private string _rawDataPath = "";
    private List<ScenePart> _sceneParts = new();
    private List<LovemakingData> _lovemakingDatas = new(); // raw captured streams, for "generates"
    private SceneActionSource _actionSource;
    private string _profileKey = "";
    private string _cardProfileKey = "";
    private string _selectedProfileKey = "";
    private List<string> _profiles = new();
    private bool _bindingNeedsSceneSave;
    private bool _invalidPlaybackIndexReported;
    private bool _syncingPart; // true while pushing a part's values into the combos (suppresses write-back)
    private bool _serverRunning;

    public MainWindow()
    {
        Localization.Loc.SetLanguage(_cfg.Language); // before InitializeComponent so DynamicResource resolves
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
            editor.RebuildTimes += times => OnRebuildTimes(axis, times);
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
        FitCurrentSurface();
    }

    // Zoom the surface currently shown in the host so it just fills the visible width (N1). Deferred to
    // Loaded priority so the ScrollViewer has a real ViewportWidth; no-op while the page is hidden.
    private void FitCurrentSurface()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            double vw = SurfaceHost.ViewportWidth;
            if (vw <= 0) return;
            if (SurfaceHost.Content is OverviewEdit ov) ov.FitToWidth(vw);
            else if (SurfaceHost.Content is ScripterEdit se) se.FitToWidth(vw);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Generate_Click(object s, RoutedEventArgs e)
    {
        if (_lovemakingDatas.Count == 0 || _editors[0].Values.Count == 0) { Status(L("St.RawUnavailable")); return; }
        try { RegenerateScripter(); Status(L("St.RegeneratedParts", _sceneParts.Count)); }
        catch (Exception ex) { Status(L("St.GenerateFailed", ex.Message)); }
    }

    // Re-derive all six axes for every scene part from the captured raw streams,
    // honouring each part's chara + lovemaking mode. Mirrors mainwindow.cpp regenerate_scripter().
    private void RegenerateScripter()
    {
        int len = _editors[0].Values.Count;
        for (int p = 0; p < _sceneParts.Count; p++) RegeneratePart(p, len);
        _overview.Values = _editors[0].Values;
        for (int a = 0; a < 6; a++) _editors[a].Refresh();
        _overview.Refresh();
    }

    // Re-derive all six axes for one scene part from its chara/mode raw streams. Shared by
    // "generates" (all parts) and the single-part "create" button (creaet_btn).
    private void RegeneratePart(int p, int len)
    {
        int partBegin = _sceneParts[p].Part;
        int partEnd = (p == _sceneParts.Count - 1) ? len : _sceneParts[p + 1].Part;

        var d = FindLovemakingData(_sceneParts[p].Charas);
        if (d == null) return;
        var (ins, su, sw, tw, ro, pi) = SourceStreams(d, _sceneParts[p].LovemakingMode);

        partEnd = Math.Min(partEnd, Math.Min(ins.Count, len));
        int n = partEnd - partBegin;
        if (n <= 0) return;

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

    // Single-part "create": recompute only the currently selected part (mirrors creaet_btn).
    private void CreatePart_Click(object s, RoutedEventArgs e)
    {
        int p = PartList.SelectedIndex;
        if (_lovemakingDatas.Count == 0) { Status(L("St.RawUnavailable")); return; }
        if (_editors[0].Values.Count == 0 || p < 0 || p >= _sceneParts.Count)
        { Status(L("St.CreateNoPart")); return; }
        try
        {
            RegeneratePart(p, _editors[0].Values.Count);
            _overview.Values = _editors[0].Values;
            for (int a = 0; a < 6; a++) _editors[a].Refresh();
            _overview.Refresh();
            Status(L("St.RecomputedPart", p + 1));
        }
        catch (Exception ex) { Status(L("St.CreateFailed", ex.Message)); }
    }

    // "rebuild selected times" from an axis editor: re-derive the selected frames from the
    // current chara/mode raw streams. RebuildAllCheck => all six axes, else just the sender axis.
    // Mirrors mainwindow.cpp update_list (1939-2086).
    private void OnRebuildTimes(int axis, List<int> times)
    {
        if (_lovemakingDatas.Count == 0) { Status(L("St.RawUnavailable")); return; }
        if (times.Count == 0 || _editors[0].Values.Count == 0) return;
        string charas = ComposePairLabel();
        var d = FindLovemakingData(charas);
        if (d == null) { Status(L("St.RebuildNoData", charas)); return; }
        string mode = (ModeList.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "normal";
        var (ins, su, sw, tw, ro, pi) = SourceStreams(d, mode);
        if (ins.Count == 0) return;

        bool all = RebuildAllCheck.IsChecked == true;
        int len = _editors[0].Values.Count;
        var idx = times.Where(t => t >= 0 && t < ins.Count && t < len).ToList();
        if (idx.Count == 0) return;

        if (all) foreach (var ed in _editors) ed.RecordUndo();
        else _editors[axis].RecordUndo();

        // offsets/range come from the selected frames only (not the whole part)
        float insMax = float.MinValue, insMin = float.MaxValue, surgeSum = 0, swaySum = 0;
        foreach (var t in idx)
        {
            if (ins[t] > insMax) insMax = ins[t];
            if (ins[t] < insMin) insMin = ins[t];
            surgeSum += su[t]; swaySum += sw[t];
        }
        float crange = insMin - insMax;
        float surgeOffset = surgeSum / idx.Count, swayOffset = swaySum / idx.Count, bw = d.BodyWidth;

        foreach (var t in idx)
        {
            int[] v =
            {
                Clamp(crange == 0 ? 0 : (int)((999f / crange) * ins[t] - (999f / crange) * insMax)),
                Clamp(bw == 0 ? 500 : 999 / 2 - (int)((su[t] - surgeOffset) * 999f / bw / 2f)),
                Clamp(bw == 0 ? 500 : 999 / 2 - (int)((sw[t] - swayOffset) * 999f / bw / 2f)),
                Clamp(999 / 2 + (int)(tw[t] * 11.1f)),
                Clamp(999 / 2 - (int)(ro[t] * 11.1f)),
                Clamp(999 / 2 + (int)(pi[t] * 11.1f / 2f)),
            };
            if (all) for (int a = 0; a < 6; a++) _editors[a].Values[t] = v[a];
            else _editors[axis].Values[t] = v[axis];
        }

        if (all) for (int a = 0; a < 6; a++) _editors[a].Refresh();
        else _editors[axis].Refresh();
        _overview.Values = _editors[0].Values;
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

    private LovemakingData? FindLovemakingData(string charas)
    {
        string key = CharacterPair.Normalize(charas);
        return string.IsNullOrEmpty(key)
            ? null
            : _lovemakingDatas.FirstOrDefault(x => CharacterPair.Normalize(x.CharasName) == key);
    }

    private string ComposePairLabel()
        => (GirlList.SelectedItem as string ?? "") + "-" + (BoyList.SelectedItem as string ?? "");

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
        _server.ClientConnected += () => Dispatcher.Invoke(() => ClientLabel.Text = L("L.LinkFrom", _server.ClientAddress));
        _server.ClientDisconnected += () => Dispatcher.Invoke(() => ClientLabel.Text = L("L.ClientDisconnected"));

        _buttplug.DevicesChanged += () => Dispatcher.Invoke(RefreshDeviceList);
        _buttplug.Connected += () => Dispatcher.Invoke(() => Status(L("St.IntifaceConnected")));
        _buttplug.Disconnected += () => Dispatcher.Invoke(() => Status(L("St.IntifaceDisconnected")));
        _buttplug.Error += e => Dispatcher.Invoke(() => Status(L("St.IntifaceError", e)));
        _serial.Error += e => Dispatcher.Invoke(() => Status(L("St.SerialError", e)));

        _engine.Serial = _serial;
        _engine.Buttplug = _buttplug;

        // part navigation/editing (mirrors mainwindow.cpp overview_edit + combo connects)
        _overview.SelectPart += part => PartList.SelectedIndex = part; // drives OnPartSelected
        _overview.AddPart += OnAddPart;
        _overview.DelPart += OnDelPart;
        PartList.SelectionChanged += (_, _) => OnPartSelected(PartList.SelectedIndex);

        GirlList.SelectionChanged += (_, _) => OnCharaChanged(GirlList);
        BoyList.SelectionChanged += (_, _) => OnCharaChanged(BoyList);
        ModeList.SelectionChanged += (_, _) =>
        {
            if (_syncingPart) return;
            int p = PartList.SelectedIndex;
            if (p >= 0 && p < _sceneParts.Count) _sceneParts[p].LovemakingMode = (ModeList.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "normal";
        };
    }

    // ---------- scene parts ----------

    private void OnCharaChanged(ComboBox source)
    {
        if (_syncingPart) return;
        int p = PartList.SelectedIndex;
        if (p >= 0 && p < _sceneParts.Count)
            _sceneParts[p].Charas = ComposePairLabel();
        // only push to the game when the user opted in
        if (SelectOnSwitchCheck.IsChecked == true && source.SelectedItem is string c) _server.SendSelectChara(c);
    }

    // A part was selected (by the user or the overview): reflect its mode/chara into the combos.
    private void OnPartSelected(int part)
    {
        if (_overview.Values.Count == 0 || part < 0 || part >= _sceneParts.Count) return;
        _overview.SelectedPart = part == 0 ? 0
            : (part - 1 < _overview.SplitLines.Count ? _overview.SplitLines[part - 1] : 0);
        _overview.InvalidateVisual();
        for (int a = 0; a < 6; a++) { _editors[a].SelectedPart = _overview.SelectedPart; _editors[a].InvalidateVisual(); }

        _syncingPart = true;
        SelectComboItem(ModeList, _sceneParts[part].LovemakingMode);
        if (CharacterPair.TrySplitLabels(_sceneParts[part].Charas, out var female, out var male))
        { GirlList.SelectedItem = female; BoyList.SelectedItem = male; }
        _syncingPart = false;
    }

    // A new split was added in the overview: create the matching part and re-list.
    private void OnAddPart(int part)
    {
        if (_overview.Values.Count == 0 || part - 1 >= _overview.SplitLines.Count) return;
        _sceneParts.Add(new ScenePart
        {
            Part = _overview.SplitLines[part - 1],
            LovemakingMode = "normal",
            Charas = (GirlList.Items.Count > 0 ? GirlList.Items[0] : "") + "-" + (BoyList.Items.Count > 0 ? BoyList.Items[0] : ""),
        });
        _sceneParts.Sort((a, b) => a.Part - b.Part);
        RebuildPartList();
        PartList.SelectedIndex = part;
    }

    // A split was removed in the overview: drop the matching part and re-list.
    private void OnDelPart(int part)
    {
        if (_overview.Values.Count == 0 || part < 0 || part >= _overview.SplitLines.Count) return;
        int splitLine = _overview.SplitLines[part];
        _sceneParts.RemoveAll(p => p.Part == splitLine);
        _overview.SplitLines.RemoveAt(part);
        _overview.InvalidateVisual();
        RebuildPartList();
    }

    private void RebuildPartList()
    {
        _syncingPart = true;
        PartList.Items.Clear();
        for (int i = 0; i < _sceneParts.Count; i++) PartList.Items.Add("part" + (i + 1));
        _syncingPart = false;
    }

    private static void SelectComboItem(ComboBox combo, string content)
    {
        foreach (var it in combo.Items)
            if ((it as ComboBoxItem)?.Content?.ToString() == content) { combo.SelectedItem = it; return; }
    }

    private void LoadConfigIntoUi()
    {
        BaudBox.Text = _cfg.BaudRate;
        ServerIpBox.Text = _cfg.ServerIp;
        ServerPortBox.Text = _cfg.ServerPort;
        WebIpBox.Text = _cfg.WebServerIp;
        GameRootBox.Text = _cfg.GameRoot;
        RebuildAllCheck.IsChecked = _cfg.RebuildAllAxes;
        SelectComboByTag(LanguageList, _cfg.Language); // fires Language_Changed (idempotent)

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
        GameRootBox.LostFocus += (_, _) => { _cfg.GameRoot = GameRootBox.Text; RefreshProfileList(); UpdateProfileUi(); };
        RebuildAllCheck.Click += (_, _) => _cfg.RebuildAllAxes = RebuildAllCheck.IsChecked == true;
        UpdateProfileUi();
    }

    // ---------- navigation / title bar ----------

    // Language selector (PageSettings): swap the live string dictionary + persist.
    private void Language_Changed(object s, SelectionChangedEventArgs e)
    {
        string lang = (LanguageList.SelectedItem as ComboBoxItem)?.Tag as string ?? "en";
        Localization.Loc.SetLanguage(lang);
        _cfg.Language = lang;
        UpdateProfileUi();
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (var it in combo.Items)
            if ((it as ComboBoxItem)?.Tag as string == tag) { combo.SelectedItem = it; return; }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void NavHome_Click(object s, RoutedEventArgs e) => ShowPage(PageHome);
    private void NavScripter_Click(object s, RoutedEventArgs e) { ShowPage(PageScripter); FitCurrentSurface(); }
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
        Debounce(RunServerBtn);
        if (_serverRunning) { _server.Stop(); _serverRunning = false; RunServerBtn.Content = L("L.RunServer"); ClientLabel.Text = L("L.ServerStopped"); return; }
        try
        {
            int port = int.TryParse(ServerPortBox.Text, out var p) ? p : 8000;
            _server.Start(ServerIpBox.Text, port);
            _serverRunning = true;
            RunServerBtn.Content = L("L.StopServer");
            ClientLabel.Text = L("L.Listening", ServerIpBox.Text, port);
        }
        catch (Exception ex) { Status(L("St.ServerStartFailed", ex.Message)); }
    }

    private void OnSceneMessage(SceneMessage msg)
    {
        try
        {
            bool sameScene = _filePath == msg.Path && _cardProfileKey == msg.ProfileKey;
            bool reloadForNewSample = false;
            if (sameScene && _actionSource == SceneActionSource.SharedProfile)
            {
                string path = ResolveScenePath(msg.Path);
                reloadForNewSample = !string.IsNullOrEmpty(path) && SceneFiles.HasUnboundRawData(path);
            }
            if (!sameScene || reloadForNewSample)
                LoadScene(msg.Path, msg.ProfileKey);

            int index = msg.Index;
            if (_actionSource == SceneActionSource.None || _editors[0].Values.Count == 0) return;
            if (index < 0 || index >= _editors[0].Values.Count)
            {
                if (!_invalidPlaybackIndexReported)
                {
                    _invalidPlaybackIndexReported = true;
                    Status(L("St.PlaybackIndexOutOfRange", index));
                }
                return;
            }

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

            // follow the playhead into its part, switching the part/mode/chara combos (setplaytime:1890).
            int part = PartIndexForLine(index);
            if (part != PartList.SelectedIndex) PartList.SelectedIndex = part; // drives OnPartSelected (guarded)
        }
        catch (Exception ex) { Status(L("St.SceneMessageError", ex.Message)); }
    }

    // Which scene part contains a given playback line (parts are ordered, each begins at .Part).
    private int PartIndexForLine(int index)
    {
        int part = 0;
        for (int p = 0; p < _sceneParts.Count; p++)
        {
            if (_sceneParts[p].Part <= index) part = p;
            else break;
        }
        return part;
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

    private void LoadScene(string rawPath, string cardProfileKey = "")
    {
        string path = ResolveScenePath(rawPath);
        if (string.IsNullOrEmpty(path)) return;

        _filePath = rawPath; // keep raw as the identity used by the plugin
        _rawDataPath = "";
        _cardProfileKey = cardProfileKey ?? "";
        _profileKey = "";
        _selectedProfileKey = "";
        _actionSource = SceneActionSource.None;
        _lovemakingDatas = new List<LovemakingData>();
        _invalidPlaybackIndexReported = false;
        _engine.ResetDedup();
        ScenePathLabel.Text = path;
        LoadPreview(path);

        string warning = "";
        string refPath = AxisInfo.Sr6RefPath(path);
        bool refValid = SceneFiles.TryLoadSr6Ref(refPath, out var localKey, out var refExists, out var refError);
        bool hasLegacySceneData = SceneFiles.HasLegacySceneData(path);
        string selectedProfile = "";
        if (refValid)
        {
            selectedProfile = localKey;
            _profileKey = localKey;
        }
        else if (refExists)
        {
            warning = L("St.ProfileBroken", refError);
        }
        else if (!hasLegacySceneData && !string.IsNullOrEmpty(cardProfileKey))
        {
            if (!AxisInfo.IsValidProfileKey(cardProfileKey))
                warning = L("St.ProfileKeyInvalid", cardProfileKey);
            else
            {
                selectedProfile = cardProfileKey;
                _profileKey = cardProfileKey;
                try { SceneFiles.SaveSr6Ref(refPath, cardProfileKey); }
                catch (Exception ex) { warning = L("St.ProfileBroken", ex.Message); }
            }
        }
        _bindingNeedsSceneSave = refValid && !string.Equals(localKey, cardProfileKey, StringComparison.Ordinal);

        SceneActionSet? actionSet = null;
        if (!string.IsNullOrEmpty(selectedProfile))
        {
            try
            {
                string profileStem = AxisInfo.ProfileStem(GameRootFor(path), selectedProfile);
                if (SceneFiles.TryLoadActionSet(profileStem, out var shared, out var profileError))
                {
                    actionSet = shared;
                    _actionSource = SceneActionSource.SharedProfile;
                }
                else
                {
                    warning = L("St.ProfileBroken", profileError);
                }
            }
            catch (Exception ex) { warning = L("St.ProfileBroken", ex.Message); }
        }

        SceneActionSet local = null!;
        string localError = "";
        bool localComplete = SceneFiles.TryLoadActionSet(path, out local, out localError);
        if (actionSet == null && localComplete)
        {
            actionSet = local;
            _actionSource = SceneActionSource.SceneLocal;
        }
        else if (actionSet == null && SceneFiles.HasAnyActionSetFiles(path) && string.IsNullOrEmpty(warning))
        {
            warning = L("St.LegacyBroken", localError);
        }

        if (actionSet != null)
        {
            ApplyActionSet(actionSet);
            bool rawLoaded = TryLoadRawData(path);
            if (!rawLoaded && _actionSource == SceneActionSource.SharedProfile && !string.IsNullOrEmpty(selectedProfile))
                rawLoaded = TryLoadRawData(AxisInfo.ProfileRawPath(GameRootFor(path), selectedProfile));
            if (rawLoaded) ResolvePartsAgainstRaw();
            if (!rawLoaded && string.IsNullOrEmpty(warning))
                warning = L("St.RawUnavailable");
        }
        else if (File.Exists(path))
        {
            if (!TryLoadRawData(path))
            {
                ClearActions();
                Status(L("St.OldOrEmptyScene"));
                RefreshSceneUi();
                return;
            }

            var axes = SceneTxtParser.ComputeInitialAxes(_lovemakingDatas[0]);
            for (int a = 0; a < 6; a++) _editors[a].Values = axes[a].Values;
            _sceneParts = new List<ScenePart>
            {
                new() { Part = 0, LovemakingMode = "normal", Charas = _lovemakingDatas[0].CharasName }
            };
            _actionSource = SceneActionSource.SceneLocal;
            ResolvePartsAgainstRaw();
            SaveScripter(path);
        }
        else
        {
            ClearActions();
            warning = string.IsNullOrEmpty(warning) ? L("St.NoSafeActionSource") : warning;
        }

        RefreshSceneUi();
        Status(string.IsNullOrEmpty(warning) ? L("St.Loaded", Path.GetFileName(path)) : warning);
    }

    private string ResolveScenePath(string rawPath)
    {
        const string search = "/UserData/KK_osr_sr6_link/";
        int idx = rawPath.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0 && !string.IsNullOrEmpty(GameRootBox.Text))
        {
            string relative = rawPath[(idx + 1)..].Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(GameRootBox.Text, relative);
        }
        if (File.Exists(rawPath) || Path.IsPathRooted(rawPath)) return rawPath;
        Status(idx < 0 ? L("St.ScenePathNotUnder", rawPath) : L("St.SceneNotFound", rawPath));
        return "";
    }

    private string GameRootFor(string path)
    {
        if (!string.IsNullOrWhiteSpace(GameRootBox.Text)) return GameRootBox.Text;
        string normalized = path.Replace('\\', '/');
        int idx = normalized.IndexOf("/UserData/", StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? normalized[..idx] : "";
    }

    private bool TryLoadRawData(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var parsed = SceneTxtParser.Parse(File.ReadAllText(path));
            if (!parsed.IsNewVersion || parsed.Data.Count == 0) return false;
            _lovemakingDatas = parsed.Data;
            _rawDataPath = path;
            return true;
        }
        catch { return false; }
    }

    private void SaveProfileAssets(string profileKey)
    {
        string path = ResolvedPath();
        string root = GameRootFor(path);
        SceneFiles.CopyFileIfExists(_rawDataPath, AxisInfo.ProfileRawPath(root, profileKey));
        string? previewPath = CardPreviewPath(path);
        if (previewPath != null)
            SceneFiles.CopyFileIfExists(previewPath, AxisInfo.ProfilePreviewPath(root, profileKey));
    }

    private static string? CardPreviewPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        int idx = normalized.IndexOf("/UserData/KK_osr_sr6_link/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        string previewPath = normalized[..idx] + "/UserData/studio/scene/" + normalized[(idx + "/UserData/KK_osr_sr6_link/".Length)..];
        return previewPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? previewPath[..^4] + ".png"
            : previewPath;
    }

    private static BitmapImage? LoadBitmap(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            return bmp;
        }
        catch { return null; }
    }

    private void LoadPreview(string path)
    {
        ScenePreview.Source = null;
        string? previewPath = CardPreviewPath(path);
        if (previewPath != null) ScenePreview.Source = LoadBitmap(previewPath);
    }

    private void UpdateProfilePreview()
    {
        if (ProfilePreview == null) return;
        ProfilePreview.Source = null;
        string key = _selectedProfileKey;
        if (string.IsNullOrEmpty(key)) return;
        string path = _filePath == "" ? "" : ResolvedPath();
        string root = string.IsNullOrEmpty(path) ? GameRootBox.Text : GameRootFor(path);
        if (string.IsNullOrEmpty(root)) return;
        ProfilePreview.Source = LoadBitmap(AxisInfo.ProfilePreviewPath(root, key));
    }

    private void ApplyActionSet(SceneActionSet actionSet)
    {
        for (int a = 0; a < 6; a++) _editors[a].Values = actionSet.Axes[a].Values;
        _sceneParts = actionSet.Parts;
    }

    private void ResolvePartsAgainstRaw()
    {
        foreach (var part in _sceneParts)
        {
            var data = FindLovemakingData(part.Charas);
            if (data == null) { part.RawResolved = false; continue; }
            part.Charas = data.CharasName;
            part.RawResolved = true;
        }
    }

    private void ClearActions()
    {
        for (int a = 0; a < 6; a++) _editors[a].Values = new List<int>();
        _sceneParts = new List<ScenePart>();
        _actionSource = SceneActionSource.None;
    }

    private void RefreshSceneUi()
    {
        var girls = new List<string>();
        var boys = new List<string>();
        foreach (var data in _lovemakingDatas)
            if (CharacterPair.TrySplitLabels(data.CharasName, out var female, out var male))
            { girls.Add(female); boys.Add(male); }
        foreach (var part in _sceneParts)
            if (CharacterPair.TrySplitLabels(part.Charas, out var female, out var male))
            { girls.Add(female); boys.Add(male); }
        FillCombo(GirlList, girls);
        FillCombo(BoyList, boys);

        _overview.Values = _editors[0].Values;
        _overview.SplitLines = _sceneParts.Skip(1).Select(p => p.Part).ToList();
        for (int a = 0; a < 6; a++) _editors[a].SplitLines = _overview.SplitLines;
        RebuildPartList();
        if (PartList.Items.Count > 0) PartList.SelectedIndex = 0;

        for (int a = 0; a < 6; a++) _editors[a].Refresh();
        _overview.Refresh();
        FitCurrentSurface();
        UpdateRawControls();
        RefreshProfileList();
        UpdateProfileUi();
    }

    private void SaveScripter(string path)
    {
        var axes = new AxisScript[6];
        for (int a = 0; a < 6; a++)
            axes[a] = new AxisScript { Values = _editors[a].Values, MaxValue = _sliders[a].MaxValue, MinValue = _sliders[a].MinValue };
        SceneFiles.SaveActionSet(path, axes, _sceneParts);
    }

    private string ResolvedPath() => ResolveScenePath(_filePath);

    private void SaveActiveActionSet()
    {
        if (_actionSource == SceneActionSource.SharedProfile && !string.IsNullOrEmpty(_profileKey))
        {
            SaveScripter(AxisInfo.ProfileStem(GameRootFor(ResolvedPath()), _profileKey));
            SaveProfileAssets(_profileKey);
        }
        else
            SaveScripter(ResolvedPath());
    }

    private void Save_Click(object s, RoutedEventArgs e)
    {
        if (_filePath == "") { Status(L("St.NoScene")); return; }
        try { SaveActiveActionSet(); Status(L("St.SavedScripts")); }
        catch (Exception ex) { Status(L("St.SaveFailed", ex.Message)); }
    }

    private void Convert_Click(object s, RoutedEventArgs e)
    {
        if (_filePath == "") { Status(L("St.NoScene")); return; }
        try
        {
            string path = ResolvedPath();
            SaveActiveActionSet();
            int refCount = _editors[0].Values.Count;
            for (int a = 0; a < 6; a++)
                SceneFiles.ExportFunscript(AxisInfo.FunscriptPath(path, (Axis)a), _editors[a].Values, refCount);
            Status(L("St.ExportedFunscripts"));
        }
        catch (Exception ex) { Status(L("St.ExportFailed", ex.Message)); }
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
        catch (Exception ex) { Status(L("St.OpenFolderFailed", ex.Message)); }
    }

    private void UpdateRawControls()
    {
        bool enabled = _lovemakingDatas.Count > 0 && _editors[0].Values.Count > 0;
        GenerateBtn.IsEnabled = enabled;
        CreatePartBtn.IsEnabled = enabled;
    }

    private void RefreshProfileList()
    {
        if (ProfilePickerButton == null) return;
        try
        {
            string root = _filePath == "" ? GameRootBox.Text : GameRootFor(ResolvedPath());
            _profiles = string.IsNullOrEmpty(root) ? new List<string>() : SceneFiles.ListCompleteProfiles(root);
            if (_actionSource == SceneActionSource.SharedProfile && _profiles.Contains(_profileKey))
                _selectedProfileKey = _profileKey;
            else if (!_profiles.Contains(_selectedProfileKey))
                _selectedProfileKey = "";
        }
        catch (Exception ex)
        {
            _profiles = new List<string>();
            _selectedProfileKey = "";
            Status(L("St.ProfileListFailed", ex.Message));
        }
    }

    private void UpdateProfileUi()
    {
        if (ProfileSourceLabel == null) return;
        ProfileSourceLabel.Text = _actionSource == SceneActionSource.SharedProfile && !string.IsNullOrEmpty(_profileKey)
            ? L("St.SharedProfileSource", _profileKey)
            : _actionSource == SceneActionSource.SceneLocal
                ? L("St.SceneLocalSource")
                : L("St.NoSafeActionSource");
        SaveSharedProfileBtn.Content = string.IsNullOrEmpty(_profileKey)
            ? L("L.SaveSharedProfile")
            : L("L.SaveSharedProfile") + ": " + _profileKey;
        SaveSharedProfileBtn.IsEnabled = _actionSource == SceneActionSource.SharedProfile && _editors[0].Values.Count > 0;
        ProfileSelectionLabel.Text = string.IsNullOrEmpty(_selectedProfileKey) ? L("L.SelectSharedProfile") : _selectedProfileKey;
        ProfilePickerButton.IsEnabled = _filePath != "" && _profiles.Count > 0;
        LoadSharedProfileBtn.IsEnabled = _filePath != "" && !string.IsNullOrEmpty(_selectedProfileKey);
        ProfileStatusLabel.Text = _bindingNeedsSceneSave ? L("St.ProfileNeedsSceneSave") : "";
        UpdateProfilePreview();
    }

    private void ProfilePicker_Click(object s, RoutedEventArgs e)
    {
        if (_filePath == "" || _profiles.Count == 0) return;
        string root = GameRootFor(ResolvedPath());
        var dialog = new ProfileSelectorWindow(this, root, _profiles, _selectedProfileKey);
        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedProfileKey))
        {
            _selectedProfileKey = dialog.SelectedProfileKey;
            UpdateProfileUi();
        }
    }

    private void LoadSharedProfile_Click(object s, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_selectedProfileKey))
            BindProfile(_selectedProfileKey, "St.ProfileLoaded");
    }

    private bool BindProfile(string key, string statusKey)
    {
        if (_filePath == "") { Status(L("St.NoScene")); return false; }
        if (!AxisInfo.IsValidProfileKey(key)) { Status(L("St.ProfileKeyInvalid", key)); return false; }

        string path = ResolvedPath();
        string root = GameRootFor(path);
        try
        {
            string stem = AxisInfo.ProfileStem(root, key);
            if (!SceneFiles.TryLoadActionSet(stem, out var actionSet, out var error))
            { Status(L("St.ProfileBroken", error)); return false; }

            string refPath = AxisInfo.Sr6RefPath(path);
            SceneFiles.SaveSr6Ref(refPath, key);
            ApplyActionSet(actionSet);
            _profileKey = key;
            _cardProfileKey = key;
            _selectedProfileKey = key;
            _actionSource = SceneActionSource.SharedProfile;
            _engine.ResetDedup();

            _lovemakingDatas = new List<LovemakingData>();
            _rawDataPath = "";
            bool rawLoaded = TryLoadRawData(path);
            if (!rawLoaded) rawLoaded = TryLoadRawData(AxisInfo.ProfileRawPath(root, key));
            if (rawLoaded) ResolvePartsAgainstRaw();

            bool sent = _server.SendProfileBinding(key);
            _bindingNeedsSceneSave = !sent;
            RefreshSceneUi();
            Status(sent ? L(statusKey, key) + " - " + L("St.ProfileSaveQueued") : L("St.ProfilePluginDisconnected"));
            return true;
        }
        catch (Exception ex) { Status(L("St.SaveFailed", ex.Message)); return false; }
    }

    private bool TryReadNewProfileKey(out string key)
    {
        key = ProfileKeyBox.Text.Trim();
        if (key.Length == 0) key = AxisInfo.TimestampProfileKey(DateTime.Now);
        if (!AxisInfo.TryValidateProfileKey(key, out var error))
        { Status(L("St.ProfileKeyInvalid", error)); return false; }
        string stem = AxisInfo.ProfileStem(GameRootFor(ResolvedPath()), key);
        if (SceneFiles.HasAnyActionSetFiles(stem))
        { Status(L("St.ProfileAlreadyExists", key)); return false; }
        return true;
    }

    private void CreateSharedProfile_Click(object s, RoutedEventArgs e)
    {
        if (_filePath == "") { Status(L("St.NoScene")); return; }
        if (_editors[0].Values.Count == 0) { Status(L("St.NoSafeActionSource")); return; }
        if (!TryReadNewProfileKey(out var key)) return;
        try
        {
            SceneFiles.SaveActionSet(AxisInfo.ProfileStem(GameRootFor(ResolvedPath()), key), CurrentAxisScripts(), _sceneParts);
            SaveProfileAssets(key);
            BindProfile(key, "St.ProfileCreated");
        }
        catch (Exception ex) { Status(L("St.SaveFailed", ex.Message)); }
    }

    private void ForkProfile_Click(object s, RoutedEventArgs e)
    {
        if (_filePath == "") { Status(L("St.NoScene")); return; }
        if (_editors[0].Values.Count == 0) { Status(L("St.NoSafeActionSource")); return; }
        if (!TryReadNewProfileKey(out var key)) return;
        try
        {
            SceneFiles.SaveActionSet(AxisInfo.ProfileStem(GameRootFor(ResolvedPath()), key), CurrentAxisScripts(), _sceneParts);
            SaveProfileAssets(key);
            BindProfile(key, "St.ProfileForked");
        }
        catch (Exception ex) { Status(L("St.SaveFailed", ex.Message)); }
    }

    private AxisScript[] CurrentAxisScripts()
    {
        var axes = new AxisScript[6];
        for (int a = 0; a < 6; a++)
            axes[a] = new AxisScript { Values = _editors[a].Values, MaxValue = _sliders[a].MaxValue, MinValue = _sliders[a].MinValue };
        return axes;
    }

    private void SaveSharedProfile_Click(object s, RoutedEventArgs e)
    {
        if (_actionSource != SceneActionSource.SharedProfile || string.IsNullOrEmpty(_profileKey))
        { Status(L("St.NoSafeActionSource")); return; }
        try
        {
            SceneFiles.SaveActionSet(AxisInfo.ProfileStem(GameRootFor(ResolvedPath()), _profileKey), CurrentAxisScripts(), _sceneParts);
            SaveProfileAssets(_profileKey);
            RefreshProfileList();
            Status(L("St.SharedProfileSaved", _profileKey));
        }
        catch (Exception ex) { Status(L("St.SaveFailed", ex.Message)); }
    }

    private void UnbindProfile_Click(object s, RoutedEventArgs e)
    {
        if (_filePath == "") { Status(L("St.NoScene")); return; }
        string path = ResolvedPath();
        try
        {
            File.Delete(AxisInfo.Sr6RefPath(path));
            LoadScene(_filePath);
            Status(L("St.ProfileUnbound"));
        }
        catch (Exception ex) { Status(L("St.SaveFailed", ex.Message)); }
    }

    private void ShowChars_Click(object s, RoutedEventArgs e) => _server.SendShow(GirlList.Text, BoyList.Text);
    private void HideChars_Click(object s, RoutedEventArgs e) => _server.SendHide(GirlList.Text, BoyList.Text);

    // ---------- serial ----------

    private void ReloadPorts_Click(object s, RoutedEventArgs e)
    {
        try
        {
            PortList.Items.Clear();
            foreach (var p in SerialOutput.AvailablePorts()) PortList.Items.Add(p);
            if (PortList.Items.Count > 0) PortList.SelectedIndex = 0;
        }
        catch (Exception ex) { Status(L("St.PortScanFailed", ex.Message)); }
    }

    // Send every axis to mid (500) on the connected outputs (N2).
    private void Reset_Click(object s, RoutedEventArgs e)
    {
        try { _engine.ResetAll(); Status(L("St.ResetAxes")); }
        catch (Exception ex) { Status(L("St.ResetFailed", ex.Message)); }
    }

    private void LinkSerial_Click(object s, RoutedEventArgs e)
    {
        Debounce(LinkSerialBtn);
        if (_serial.IsOpen) { _serial.Close(); LinkSerialBtn.Content = L("L.LinkSerial"); Status(L("St.SerialClosed")); return; }
        if (PortList.SelectedItem is not string port) { Status(L("St.NoSerialPort")); return; }
        try
        {
            int baud = int.TryParse(BaudBox.Text, out var b) ? b : 115200;
            _serial.Open(port, baud);
            LinkSerialBtn.Content = L("L.UnlinkSerial");
            Status(L("St.SerialLinked", port));
        }
        catch (Exception ex) { Status(L("St.SerialOpenFailed", ex.Message)); }
    }

    // ---------- intiface ----------

    private async void LinkIntiface_Click(object s, RoutedEventArgs e)
    {
        if (_buttplug.IsConnected) { await _buttplug.DisconnectAsync(); LinkIntifaceBtn.Content = L("L.Connect"); return; }
        try { await _buttplug.ConnectAsync(WebIpBox.Text); LinkIntifaceBtn.Content = L("L.Disconnect"); }
        catch (Exception ex) { Status(L("St.IntifaceConnectFailed", ex.Message)); }
    }

    private async void Rescan_Click(object s, RoutedEventArgs e)
    {
        try { await _buttplug.StartScanningAsync(); Status(L("St.Scanning")); }
        catch (Exception ex) { Status(L("St.ScanFailed", ex.Message)); }
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
        if (dlg.ShowDialog() == true)
        {
            GameRootBox.Text = dlg.FolderName;
            _cfg.GameRoot = dlg.FolderName;
            RefreshProfileList();
            UpdateProfileUi();
        }
    }

    private static void FillCombo(ComboBox combo, IEnumerable<string> items)
    {
        combo.Items.Clear(); // drop the previous scene's chars before refilling
        foreach (var it in items.Distinct()) combo.Items.Add(it);
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void Status(string text) => StatusBar.Text = text;

    // Short aliases for the localized-string lookup (Localization.Loc.T).
    private static string L(string key) => Localization.Loc.T(key);
    private static string L(string key, params object[] args) => Localization.Loc.T(key, args);

    // Briefly disable a button after a click to swallow accidental double-clicks
    // (mirrors Qt delay_change1/2 + timer1/2, interval 3000ms).
    private static async void Debounce(System.Windows.Controls.Control btn, int ms = 3000)
    {
        btn.IsEnabled = false;
        try { await System.Threading.Tasks.Task.Delay(ms); } finally { btn.IsEnabled = true; }
    }

    private void Cleanup()
    {
        _server.Dispose();
        _serial.Dispose();
        _ = _buttplug.DisconnectAsync();
    }
}
