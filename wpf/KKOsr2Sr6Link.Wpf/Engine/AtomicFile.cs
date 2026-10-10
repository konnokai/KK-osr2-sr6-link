using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KKOsr2Sr6Link.Wpf.Engine;

/// <summary>
/// Writes files through "&lt;path&gt;.tmp" and then moves the temp file over the target, so a failed
/// write never leaves a half-written target behind.
/// </summary>
public static class AtomicFile
{
    public static void Write(string path, Action<Stream> write) => WriteAll(new[] { (path, write) });

    public static void WriteAllText(string path, string text, Encoding encoding)
        => Write(path, s => { var bytes = encoding.GetPreamble(); s.Write(bytes); s.Write(encoding.GetBytes(text)); });

    public static void Copy(string source, string destination)
        => Write(destination, s => { using var from = File.OpenRead(source); from.CopyTo(s); });

    /// <summary>Write every temp file first and replace targets only after all of them succeeded, so a
    /// failed write keeps the whole old set. A failure during the final moves can still leave the set
    /// mixed; that window is only a few renames in one folder.</summary>
    public static void WriteAll(IReadOnlyList<(string Path, Action<Stream> Write)> files)
    {
        var temps = new List<string>();
        try
        {
            foreach (var (path, write) in files)
            {
                string temp = path + ".tmp";
                temps.Add(temp);
                using var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                write(stream);
                stream.Flush(true);
            }
        }
        catch
        {
            foreach (var temp in temps)
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
        for (int i = 0; i < files.Count; i++)
            File.Move(temps[i], files[i].Path, overwrite: true);
    }
}
