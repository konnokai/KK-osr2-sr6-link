namespace KKOsr2Sr6Link.Tests;

/// <summary>WPF 控制項只能在 STA 執行緒建立，xUnit 預設是 MTA。</summary>
internal static class Sta
{
    public static void Run(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw new Exception("STA body failed", error);
    }
}
