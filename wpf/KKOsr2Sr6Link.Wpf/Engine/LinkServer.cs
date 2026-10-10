using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KKOsr2Sr6Link.Wpf.Engine;

/// <summary>A decoded "path|index|sleep[|profileKey]" message from the plugin.</summary>
public readonly record struct SceneMessage(string Path, int Index, double Sleep, string ProfileKey = "");

/// <summary>
/// TCP server side of the plugin link (the plugin is the client). Listens on ip:port, decodes
/// "&lt;path&gt;|&lt;index&gt;|&lt;sleep&gt;" messages, and sends colon-prefixed commands back (every message in
/// both directions ends with '\n'), throttled to
/// one write per 200 ms (the original allowriter/writerTimer behaviour). Mirrors mainwindow.cpp
/// new_connected/server_read and the set_play/setplaytime writes.
/// </summary>
public sealed class LinkServer : IDisposable
{
    public const int ThrottleMs = 200;

    /// <summary>Sent by the plugin when the user clicks Init in Studio (KKAPI SceneOperationKind.Clear).</summary>
    public const string ClearMessage = "clear";

    private readonly Func<long> _clock;
    private readonly object _sendLock = new();
    private TcpListener? _listener;
    // 別的程式也可能連進這個 port，所以每條連線都要留著，才能在它斷掉後換回插件那條
    private readonly List<(TcpClient Client, NetworkStream Stream)> _clients = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private long _nextAllowed;

    public bool IsConnected => _client?.Connected == true && _stream != null;
    public string ClientAddress { get; private set; } = "";

    public event Action<SceneMessage>? MessageReceived;
    public event Action? SceneCleared;
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
                var stream = client.GetStream();
                lock (_sendLock)
                {
                    _clients.Add((client, stream));
                    SetCurrent(client, stream);
                }
                ClientConnected?.Invoke();
                _ = ReadLoopAsync(client, stream, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task ReadLoopAsync(TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
        // 一次 read 可能有好幾則訊息，也可能只有半則，連中文字的 UTF-8 位元組都可能被切開
        var decoder = Encoding.UTF8.GetDecoder();
        var pending = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read <= 0) break;
                pending.Append(chars, 0, decoder.GetChars(buffer, 0, read, chars, 0));
                foreach (var line in TakeLines(pending))
                {
                    if (line == ClearMessage)
                    {
                        Promote(client, stream);
                        SceneCleared?.Invoke();
                    }
                    else if (TryParse(line, out var msg))
                    {
                        Promote(client, stream);
                        MessageReceived?.Invoke(msg);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            bool currentChanged;
            bool anyLeft;
            lock (_sendLock)
            {
                _clients.Remove((client, stream));
                currentChanged = ReferenceEquals(_client, client);
                if (currentChanged)
                {
                    if (_clients.Count > 0) SetCurrent(_clients[^1].Client, _clients[^1].Stream);
                    else SetCurrent(null, null);
                }
                anyLeft = _client != null;
            }
            // 不關掉的話 socket 會一直開著，對方以為還連著，但已經沒人在讀
            try { client.Dispose(); } catch { }
            if (!anyLeft) ClientDisconnected?.Invoke();
            else if (currentChanged) ClientConnected?.Invoke();
        }
    }

    /// <summary>A valid plugin message marks this connection as the one that receives commands.</summary>
    private void Promote(TcpClient client, NetworkStream stream)
    {
        lock (_sendLock)
        {
            if (ReferenceEquals(_client, client) || !_clients.Contains((client, stream))) return;
            SetCurrent(client, stream);
        }
        ClientConnected?.Invoke();
    }

    // 呼叫端要先拿 _sendLock，TrySend 才不會讀到換到一半的 _client / _stream
    private void SetCurrent(TcpClient? client, NetworkStream? stream)
    {
        _client = client;
        _stream = stream;
        string address = "";
        try { address = ((IPEndPoint?)client?.Client.RemoteEndPoint)?.Address.ToString() ?? ""; }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
        ClientAddress = address;
    }

    /// <summary>Remove every complete '\n'-terminated line from <paramref name="pending"/>; the unfinished tail stays.</summary>
    private static List<string> TakeLines(StringBuilder pending)
    {
        var lines = new List<string>();
        var text = pending.ToString();
        int start = 0, end;
        while ((end = text.IndexOf('\n', start)) >= 0)
        {
            var line = text[start..end].TrimEnd('\r');
            if (line.Length > 0) lines.Add(line);
            start = end + 1;
        }
        pending.Remove(0, start);
        return lines;
    }

    /// <summary>Decode the legacy three fields and the optional fourth profile key.</summary>
    public static bool TryParse(string data, out SceneMessage msg)
    {
        msg = default;
        if (string.IsNullOrEmpty(data)) return false;
        var parts = data.Split('|');
        if (parts.Length < 3) return false;
        int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index);
        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var sleep);
        msg = new SceneMessage(parts[0], index, sleep, parts.Length >= 4 ? parts[3] : "");
        return true;
    }

    // ---- outbound commands (200ms throttled) ----

    public bool SendPlay() => TrySend("0:");
    public bool SendSeek(int index) => TrySend("1:" + index.ToString(CultureInfo.InvariantCulture));
    public bool SendSelectChara(string chara) => TrySend("2:" + chara);
    public bool SendShow(string girl, string boy) => TrySend("3:" + girl + "-" + boy);
    public bool SendHide(string girl, string boy) => TrySend("4:" + girl + "-" + boy);

    /// <summary>Persist the scene binding without the timeline command throttle.</summary>
    public bool SendProfileBinding(string? profileKey)
    {
        profileKey ??= "";
        if (!string.IsNullOrEmpty(profileKey) && !AxisInfo.IsValidProfileKey(profileKey)) return false;
        return TrySendImmediate("5:" + profileKey);
    }

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
                var bytes = Encoding.UTF8.GetBytes(message + "\n");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
            catch (IOException) { return false; }
            catch (ObjectDisposedException) { return false; }
            _nextAllowed = now + ThrottleMs;
            return true;
        }
    }

    private bool TrySendImmediate(string message)
    {
        lock (_sendLock)
        {
            var stream = _stream;
            if (stream == null || _client?.Connected != true) return false;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(message + "\n");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
                return true;
            }
            catch (IOException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        (TcpClient Client, NetworkStream Stream)[] clients;
        lock (_sendLock)
        {
            clients = _clients.ToArray();
            _clients.Clear();
            SetCurrent(null, null);
        }
        foreach (var (client, _) in clients)
            try { client.Dispose(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        _cts?.Dispose(); _cts = null;
        _nextAllowed = 0;
    }

    public void Dispose() => Stop();
}
