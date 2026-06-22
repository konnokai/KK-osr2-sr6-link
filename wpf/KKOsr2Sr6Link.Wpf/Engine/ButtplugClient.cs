using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace KKOsr2Sr6Link.Wpf.Engine;

/// <summary>A device reported by Intiface, mirroring the Qt Device struct (mainwindow.h:225-232).</summary>
public sealed class ButtplugDevice
{
    public string Name { get; set; } = "";
    public int Index { get; set; }
    public string WorkWay { get; set; } = "unknown";
    /// <summary>Per-feature axis mapping (0..5), one slot per FeatureCount. Defaults all 0 (L0).</summary>
    public List<int> Feature { get; } = new();
    /// <summary>Per-feature enable flag (0/1).</summary>
    public List<int> FeatureEnable { get; } = new();

    public bool IsLinear => string.Equals(WorkWay, "linearCmd", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Raw Buttplug-over-websocket client (no SDK), matching the JSON the Qt app hand-builds. All messages
/// are single-element arrays. On connect: RequestServerInfo, then RequestDeviceList. Parses ServerInfo,
/// DeviceList, DeviceAdded. Mirrors mainwindow.cpp:1986-2139.
/// </summary>
public sealed class ButtplugClient : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;

    public List<ButtplugDevice> Devices { get; } = new();

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action? DevicesChanged;
    public event Action<string>? Error;

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public async Task ConnectAsync(string url, CancellationToken ct = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
        _ws = new ClientWebSocket();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _ws.ConnectAsync(new Uri(url), _cts.Token).ConfigureAwait(false);
        Connected?.Invoke();
        _ = ReceiveLoopAsync(_ws, _cts.Token);
        await SendRawAsync(BuildHandshake()).ConfigureAwait(false);
        await SendRawAsync(BuildRequestDeviceList()).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16384];
        var sb = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false);
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                HandleMessage(sb.ToString());
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex) { Error?.Invoke(ex.Message); }
        finally { Disconnected?.Invoke(); }
    }

    public void HandleMessage(string message)
    {
        if (JsonNode.Parse(message) is not JsonArray arr) return;
        bool changed = false;
        foreach (var node in arr)
        {
            if (node is not JsonObject obj) continue;
            if (obj.ContainsKey("DeviceList"))
            {
                Devices.Clear();
                if (obj["DeviceList"]?["Devices"] is JsonArray devs)
                    foreach (var d in devs)
                        if (d is JsonObject dObj) AddDevice(dObj);
                changed = true;
            }
            else if (obj.ContainsKey("DeviceAdded"))
            {
                if (obj["DeviceAdded"] is JsonObject dObj) AddDevice(dObj);
                changed = true;
            }
            // ServerInfo: nothing actionable to store (matches Qt's debug-only handling).
        }
        if (changed) DevicesChanged?.Invoke();
    }

    private void AddDevice(JsonObject info)
    {
        var device = new ButtplugDevice
        {
            Index = info["DeviceIndex"]?.GetValue<int>() ?? 0,
            Name = info["DeviceName"]?.GetValue<string>() ?? "",
        };
        var messages = info["DeviceMessages"] as JsonObject;
        (string way, int count) = FeatureOf(messages);
        device.WorkWay = way;
        for (int i = 0; i < count; i++) { device.Feature.Add(0); device.FeatureEnable.Add(0); }

        if (Devices.Exists(d => d.Index == device.Index)) return; // dedup by index, like devices_index
        Devices.Add(device);
    }

    private static (string, int) FeatureOf(JsonObject? messages)
    {
        if (messages == null) return ("unknown", 0);
        if (messages["VibrateCmd"] is JsonObject v) return ("VibrateCmd", v["FeatureCount"]?.GetValue<int>() ?? 0);
        if (messages["LinearCmd"] is JsonObject l) return ("linearCmd", l["FeatureCount"]?.GetValue<int>() ?? 0);
        if (messages["ScalarCmd"] is JsonObject s) return ("ScalarCmd", s["FeatureCount"]?.GetValue<int>() ?? 0);
        if (messages["RotateCmd"] is JsonObject r) return ("RotateCmd", r["FeatureCount"]?.GetValue<int>() ?? 0);
        return ("unknown", 0);
    }

    // ---- outbound messages ----

    public Task SendLinearCmdAsync(int featureIndex, int deviceIndex, int durationMs, int move)
        => SendRawAsync(BuildLinearCmd(featureIndex, deviceIndex, durationMs, move));

    public Task StartScanningAsync() => SendRawAsync(BuildSimple("StartScanning"));
    public Task StopScanningAsync() => SendRawAsync(BuildSimple("StopScanning"));
    public Task RequestDeviceListAsync() => SendRawAsync(BuildRequestDeviceList());

    private async Task SendRawAsync(string json)
    {
        var ws = _ws;
        if (ws == null || ws.State != WebSocketState.Open) return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }
        catch (WebSocketException ex) { Error?.Invoke(ex.Message); }
        catch (ObjectDisposedException) { }
    }

    // ---- message builders (static, tested directly) ----

    public static string BuildHandshake()
        => new JsonArray(new JsonObject
        {
            ["RequestServerInfo"] = new JsonObject
            {
                ["Id"] = 1,
                ["ClientName"] = "Link_osr2_sr6_to_kk_studio",
                ["MessageVersion"] = 1,
            }
        }).ToJsonString();

    public static string BuildRequestDeviceList()
        => new JsonArray(new JsonObject { ["RequestDeviceList"] = new JsonObject { ["Id"] = 1 } }).ToJsonString();

    public static string BuildSimple(string name)
        => new JsonArray(new JsonObject { [name] = new JsonObject { ["Id"] = 1 } }).ToJsonString();

    public static string BuildLinearCmd(int featureIndex, int deviceIndex, int durationMs, int move)
    {
        double position = Math.Round(move / 1000.0, 3);
        return new JsonArray(new JsonObject
        {
            ["LinearCmd"] = new JsonObject
            {
                ["Id"] = 1,
                ["DeviceIndex"] = deviceIndex,
                ["Vectors"] = new JsonArray(new JsonObject
                {
                    ["Index"] = featureIndex,
                    ["Duration"] = durationMs,
                    ["Position"] = position,
                }),
            }
        }).ToJsonString();
    }

    public async Task DisconnectAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try
        {
            if (_ws?.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
        _ws?.Dispose(); _ws = null;
        _cts?.Dispose(); _cts = null;
        Devices.Clear();
    }

    public void Dispose() => _ = DisconnectAsync();
}
