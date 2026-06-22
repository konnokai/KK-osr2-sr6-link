using System;
using System.Globalization;
using System.IO.Ports;
using System.Text;

namespace KKOsr2Sr6Link.Wpf.Engine;

/// <summary>
/// TCode serial output to an OSR2/SR6. One line per axis: "&lt;AXIS&gt;&lt;val:000..999&gt;I&lt;sleepMs&gt;\r\n".
/// The "I&lt;ms&gt;" suffix is the TCode interval extension: the device interpolates to the target over
/// that time, so smoothing happens device-side. Mirrors mainwindow.cpp:1252 etc. Defaults 8/N/1, no
/// flow control. (ayvajs is the reference for TCode semantics; it is JS-only, not a dependency.)
/// </summary>
public sealed class SerialOutput : IDisposable
{
    private SerialPort? _port;

    public bool IsOpen => _port?.IsOpen == true;
    public event Action<string>? Error;

    public void Open(string portName, int baudRate,
        int dataBits = 8, Parity parity = Parity.None, StopBits stopBits = StopBits.One,
        Handshake handshake = Handshake.None)
    {
        Close();
        _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
        {
            Handshake = handshake,
            NewLine = "\r\n",
            WriteTimeout = 500,
        };
        _port.ErrorReceived += (_, e) => Error?.Invoke(e.EventType.ToString());
        _port.Open();
    }

    /// <summary>Build the TCode line for one axis (also used by tests). value padded to 3 digits.</summary>
    public static string FormatLine(Axis axis, int value, int sleepMs)
    {
        string v = value >= 0
            ? value.ToString(CultureInfo.InvariantCulture).PadLeft(3, '0')
            : value.ToString(CultureInfo.InvariantCulture);
        return $"{axis.Code()}{v}I{sleepMs.ToString(CultureInfo.InvariantCulture)}\r\n";
    }

    /// <summary>Scale a raw 0..999 keyframe to the axis range, matching mainwindow.cpp:1242 (truncated to int).</summary>
    public static int Scale(int raw, int minValue, int maxValue)
        => (int)((double)(maxValue - minValue) / 999.0 * raw + minValue);

    public bool WriteAxis(Axis axis, int scaledValue, int sleepMs)
    {
        var port = _port;
        if (port == null || !port.IsOpen) return false;
        try
        {
            var bytes = Encoding.ASCII.GetBytes(FormatLine(axis, scaledValue, sleepMs));
            port.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex.Message);
            Close();
            return false;
        }
    }

    public void Close()
    {
        try { if (_port?.IsOpen == true) _port.Close(); } catch { }
        _port?.Dispose();
        _port = null;
    }

    public void Dispose() => Close();

    public static string[] AvailablePorts() => SerialPort.GetPortNames();
}
