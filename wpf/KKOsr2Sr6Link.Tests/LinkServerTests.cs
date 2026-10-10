using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using KKOsr2Sr6Link.Wpf.Engine;

namespace KKOsr2Sr6Link.Tests;

public class LinkServerTests
{
    [Fact]
    public void TryParse_DecodesPathIndexSleep()
    {
        Assert.True(LinkServer.TryParse("C:/x/scene.txt|42|0.1", out var m));
        Assert.Equal("C:/x/scene.txt", m.Path);
        Assert.Equal(42, m.Index);
        Assert.Equal(0.1, m.Sleep, 5);
        Assert.Equal("", m.ProfileKey);

        Assert.True(LinkServer.TryParse("C:/x/scene.txt|42|0.1|shared-v1", out var shared));
        Assert.Equal("shared-v1", shared.ProfileKey);

        Assert.False(LinkServer.TryParse("incomplete", out _));
        Assert.False(LinkServer.TryParse(LinkServer.ClearMessage, out _));
        Assert.False(LinkServer.TryParse("", out _));
    }

    [Fact]
    public void ProfileBinding_IsNotBlockedByTimelineThrottle()
    {
        using var server = new LinkServer();
        server.Start("127.0.0.1", 0);
        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.ListeningPort);
        WaitFor(() => server.IsConnected);

        Assert.True(server.SendPlay());
        Assert.True(server.SendProfileBinding("shared-v1"));
        Assert.True(server.SendProfileBinding(null));
    }

    [Fact]
    public void Throttle_AllowsOnePerWindow_UsingInjectedClock()
    {
        long now = 1000;
        using var server = new LinkServer(() => now);
        server.Start("127.0.0.1", 0);
        int port = server.ListeningPort;

        using var client = new TcpClient();
        client.Connect("127.0.0.1", port);
        WaitFor(() => server.IsConnected);

        Assert.True(server.SendPlay());    // first allowed at t=1000
        Assert.False(server.SendSeek(1));  // blocked within window
        now += LinkServer.ThrottleMs;      // advance past 200ms
        Assert.True(server.SendSeek(2));   // allowed again
    }

    [Fact]
    public void Loopback_ServerReceivesPluginMessage()
    {
        using var server = new LinkServer();
        SceneMessage? got = null;
        using var gotEvent = new ManualResetEventSlim();
        server.MessageReceived += m => { got = m; gotEvent.Set(); };
        server.Start("127.0.0.1", 0);
        int port = server.ListeningPort;

        using var client = new TcpClient();
        client.Connect("127.0.0.1", port);
        var bytes = Encoding.UTF8.GetBytes("scene.txt|7|0.1\n");
        client.GetStream().Write(bytes, 0, bytes.Length);

        Assert.True(gotEvent.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal("scene.txt", got!.Value.Path);
        Assert.Equal(7, got.Value.Index);
    }

    [Fact]
    public void Loopback_SplitsMessagesOnNewline()
    {
        using var server = new LinkServer();
        var got = new System.Collections.Concurrent.ConcurrentQueue<string>();
        int clears = 0;
        using var done = new ManualResetEventSlim();
        server.MessageReceived += m => { got.Enqueue(m.Path + "#" + m.Index); if (got.Count == 3) done.Set(); };
        server.SceneCleared += () => Interlocked.Increment(ref clears);
        server.Start("127.0.0.1", 0);

        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.ListeningPort);
        var stream = client.GetStream();
        // 前三則黏在同一次寫入，最後一則的中文字從 UTF-8 位元組中間切開
        var bytes = Encoding.UTF8.GetBytes("a.txt|1|0.1\nclear\nb.txt|2|0.1\n場景.txt|3|0.1\n");
        int cut = Encoding.UTF8.GetByteCount("a.txt|1|0.1\nclear\nb.txt|2|0.1\n場") - 1;
        stream.Write(bytes, 0, cut);
        stream.Flush();
        Thread.Sleep(100);
        stream.Write(bytes, cut, bytes.Length - cut);

        Assert.True(done.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(new[] { "a.txt#1", "b.txt#2", "場景.txt#3" }, got.ToArray());
        Assert.Equal(1, clears);
    }

    [Fact]
    public void Commands_EndWithNewline()
    {
        using var server = new LinkServer();
        server.Start("127.0.0.1", 0);
        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.ListeningPort);
        WaitFor(() => server.IsConnected);

        Assert.True(server.SendPlay());
        Assert.True(server.SendProfileBinding(""));
        var expected = "0:\n5:\n";
        var buffer = new byte[64];
        var text = "";
        client.ReceiveTimeout = 2000;
        while (text.Length < expected.Length)
            text += Encoding.UTF8.GetString(buffer, 0, client.GetStream().Read(buffer, 0, buffer.Length));
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Loopback_ClearMessage_RaisesSceneClearedOnly()
    {
        using var server = new LinkServer();
        using var cleared = new ManualResetEventSlim();
        bool gotScene = false;
        server.SceneCleared += () => cleared.Set();
        server.MessageReceived += _ => gotScene = true;
        server.Start("127.0.0.1", 0);

        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.ListeningPort);
        var bytes = Encoding.UTF8.GetBytes(LinkServer.ClearMessage + "\n");
        client.GetStream().Write(bytes, 0, bytes.Length);

        Assert.True(cleared.Wait(TimeSpan.FromSeconds(2)));
        Assert.False(gotScene);
    }

    private static void WaitFor(Func<bool> cond)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!cond() && DateTime.UtcNow < deadline) Thread.Sleep(10);
        Assert.True(cond());
    }
}
