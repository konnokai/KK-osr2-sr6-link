using System.Collections.Generic;

namespace KKOsr2Sr6Link.Wpf.Engine;

/// <summary>One axis output decision for a given playback index.</summary>
public readonly record struct AxisCommand(Axis Axis, int ScaledValue, int SleepMs);

/// <summary>
/// Drives outputs from the timeline. The game is the clock: the plugin streams the current index and
/// the engine emits the NEXT keyframe per axis (so the device's "I&lt;ms&gt;" interpolation lands on time).
/// Pure decision logic lives in <see cref="ComputeCommands"/> (mainwindow.cpp:1239-1437); Dispatch just
/// pushes the result to the serial port and each mapped Buttplug device feature.
/// </summary>
public sealed class PlaybackEngine
{
    public const int IntervalMs = 100; // frame cadence

    // Per-axis state, indexed 0..5 (L0..R2).
    public List<int>[] AxisValues { get; } = { new(), new(), new(), new(), new(), new() };
    public int[] MinValue { get; } = { 0, 0, 0, 0, 0, 0 };
    public int[] MaxValue { get; } = { 999, 999, 999, 999, 999, 999 };
    public bool[] Enabled { get; } = { true, true, true, true, true, true };

    private readonly int[] _last = { int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue, int.MinValue };

    public SerialOutput? Serial { get; set; }
    public bool SerialEnabled { get; set; }
    public ButtplugClient? Buttplug { get; set; }

    /// <summary>Most recent scaled value per axis (for UI slider reflection). -1 = none yet.</summary>
    public int[] CurrentScaled { get; } = { -1, -1, -1, -1, -1, -1 };

    public void ResetDedup()
    {
        for (int i = 0; i < 6; i++) _last[i] = int.MinValue;
    }

    /// <summary>
    /// Send every axis to its mid position (500) on all connected outputs — serial (if open) and each
    /// mapped+enabled Buttplug linear feature. Clears the dedup state so the next live frame re-sends.
    /// </summary>
    public void ResetAll(int sleepMs = IntervalMs)
    {
        for (int a = 0; a < 6; a++)
        {
            if (Serial?.IsOpen == true) Serial.WriteAxis((Axis)a, 500, sleepMs);

            var bp = Buttplug;
            if (bp == null) continue;
            foreach (var device in bp.Devices)
            {
                if (!device.IsLinear) continue;
                for (int f = 0; f < device.Feature.Count; f++)
                    if (device.Feature[f] == a && device.FeatureEnable[f] == 1)
                        _ = bp.SendLinearCmdAsync(f, device.Index, sleepMs, 500);
            }
        }
        ResetDedup();
    }

    /// <summary>
    /// For each enabled axis, find the first non-(-1) value after <paramref name="index"/>, scale it to
    /// the axis range, and emit a command only if it changed since the last send (dedup). Updates
    /// CurrentScaled and the dedup state. Pure aside from that state — no I/O.
    /// </summary>
    public List<AxisCommand> ComputeCommands(int index)
    {
        var commands = new List<AxisCommand>(6);
        for (int a = 0; a < 6; a++)
        {
            var values = AxisValues[a];
            for (int i = index + 1; i < values.Count && i >= 0; i++)
            {
                if (values[i] == -1) continue;
                int sleep = IntervalMs * (i - index);
                int scaled = SerialOutput.Scale(values[i], MinValue[a], MaxValue[a]);
                CurrentScaled[a] = scaled;
                // dedup matches the original last_Lx guard (which had a copy/paste typo on L2 using
                // last_L1 — corrected here to proper per-axis dedup; only affects redundant sends).
                if (Enabled[a] && scaled != _last[a])
                {
                    _last[a] = scaled;
                    commands.Add(new AxisCommand((Axis)a, scaled, sleep));
                }
                break;
            }
        }
        return commands;
    }

    /// <summary>Compute and push commands to the serial port and mapped Buttplug device features.</summary>
    public void Dispatch(int index)
    {
        foreach (var cmd in ComputeCommands(index))
        {
            if (SerialEnabled)
                Serial?.WriteAxis(cmd.Axis, cmd.ScaledValue, cmd.SleepMs);

            var bp = Buttplug;
            if (bp == null) continue;
            int axisCode = (int)cmd.Axis;
            foreach (var device in bp.Devices)
            {
                if (!device.IsLinear) continue;
                for (int f = 0; f < device.Feature.Count; f++)
                {
                    if (device.Feature[f] == axisCode && device.FeatureEnable[f] == 1)
                        _ = bp.SendLinearCmdAsync(f, device.Index, cmd.SleepMs, cmd.ScaledValue);
                }
            }
        }
    }
}
