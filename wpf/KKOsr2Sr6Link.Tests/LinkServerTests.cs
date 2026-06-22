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

        Assert.False(LinkServer.TryParse("incomplete", out _));
        Assert.False(LinkServer.TryParse("", out _));
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
        var bytes = Encoding.UTF8.GetBytes("scene.txt|7|0.1");
        client.GetStream().Write(bytes, 0, bytes.Length);

        Assert.True(gotEvent.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal("scene.txt", got!.Value.Path);
        Assert.Equal(7, got.Value.Index);
    }

    private static void WaitFor(Func<bool> cond)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!cond() && DateTime.UtcNow < deadline) Thread.Sleep(10);
        Assert.True(cond());
    }
}
