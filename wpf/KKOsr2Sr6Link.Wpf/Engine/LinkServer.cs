using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KKOsr2Sr6Link.Wpf.Engine;

/// <summary>A decoded "path|index|sleep" message from the plugin.</summary>
public readonly record struct SceneMessage(string Path, int Index, double Sleep);

/// <summary>
/// TCP server side of the plugin link (the plugin is the client). Listens on ip:port, decodes
/// "&lt;path&gt;|&lt;index&gt;|&lt;sleep&gt;" messages, and sends colon-prefixed commands back, throttled to
/// one write per 200 ms (the original allowriter/writerTimer behaviour). Mirrors mainwindow.cpp
/// new_connected/server_read and the set_play/setplaytime writes.
/// </summary>
public sealed class LinkServer : IDisposable
{
    public const int ThrottleMs = 200;

    private readonly Func<long> _clock;
    private readonly object _sendLock = new();
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private long _nextAllowed;

    public bool IsConnected => _client?.Connected == true && _stream != null;
    public string ClientAddress { get; private set; } = "";

    public event Action<SceneMessage>? MessageReceived;
    public event Action? ClientConnected;
    public event Action? ClientDisconnected;

    public LinkServer(Func<long>? clock = null) => _clock = clock ?? (() => Environment.TickCount64);

    public void Start(string ip, int port)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Parse(ip), port);
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public int ListeningPort => ((IPEndPoint?)_listener?.LocalEndpoint)?.Port ?? 0;

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                // Only one client at a time, matching the single-socket Qt design.
                _client = client;
                _stream = client.GetStream();
                ClientAddress = ((IPEndPoint?)client.Client.RemoteEndPoint)?.Address.ToString() ?? "";
                ClientConnected?.Invoke();
                _ = ReadLoopAsync(client, _stream, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task ReadLoopAsync(TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read <= 0) break;
                var data = Encoding.UTF8.GetString(buffer, 0, read);
                if (TryParse(data, out var msg))
                    MessageReceived?.Invoke(msg);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            if (ReferenceEquals(_client, client)) { _client = null; _stream = null; }
            ClientDisconnected?.Invoke();
        }
    }

    /// <summary>Decode "path|index|sleep" (first three '|' fields), like server_read's split.</summary>
    public static bool TryParse(string data, out SceneMessage msg)
    {
        msg = default;
        if (string.IsNullOrEmpty(data)) return false;
        var parts = data.Split('|');
        if (parts.Length < 3) return false;
        int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index);
        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var sleep);
        msg = new SceneMessage(parts[0], index, sleep);
        return true;
    }

    // ---- outbound commands (200ms throttled) ----

    public bool SendPlay() => TrySend("0:");
    public bool SendSeek(int index) => TrySend("1:" + index.ToString(CultureInfo.InvariantCulture));
    public bool SendSelectChara(string chara) => TrySend("2:" + chara);
    public bool SendShow(string girl, string boy) => TrySend("3:" + girl + "-" + boy);
    public bool SendHide(string girl, string boy) => TrySend("4:" + girl + "-" + boy);

    private bool TrySend(string message)
    {
        lock (_sendLock)
        {
            var stream = _stream;
            if (stream == null || _client?.Connected != true) return false;
            long now = _clock();
            if (now < _nextAllowed) return false;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
            catch (IOException) { return false; }
            catch (ObjectDisposedException) { return false; }
            _nextAllowed = now + ThrottleMs;
            return true;
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        try { _listener?.Stop(); } catch { }
        _stream = null; _client = null; _listener = null;
        _cts?.Dispose(); _cts = null;
        _nextAllowed = 0;
    }

    public void Dispose() => Stop();
}
