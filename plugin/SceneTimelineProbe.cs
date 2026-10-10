using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KK_osr2_sr6_link
{
    /// <summary>
    /// 不載入場景，直接讀場景卡判斷 Timeline 有沒有關鍵影格。選場景畫面要在載入前就顯示 TL 標籤。
    /// </summary>
    public static class SceneTimelineProbe
    {
        private const int ChunkSize = 1 << 20;
        private const int Overlap = 64;
        // ExtendedSave 把場景區塊寫在 PNG 最尾端：BinaryWriter 字串 "KKEx" + int 版本 3 + int 長度 + MessagePack。
        private static readonly byte[] BlockHeader = { 0x04, (byte)'K', (byte)'K', (byte)'E', (byte)'x', 0x03, 0x00, 0x00, 0x00 };
        // MessagePack 的 key "timeline"，後面接 PluginData 的兩元素陣列 [version, data]。
        private static readonly byte[] TimelineKey = { 0xa8, (byte)'t', (byte)'i', (byte)'m', (byte)'e', (byte)'l', (byte)'i', (byte)'n', (byte)'e', 0x92 };
        private static readonly byte[] SceneInfoKey = { 0xa9, (byte)'s', (byte)'c', (byte)'e', (byte)'n', (byte)'e', (byte)'I', (byte)'n', (byte)'f', (byte)'o' };
        private static readonly byte[] Keyframe = Encoding.ASCII.GetBytes("<keyframe");

        private struct CacheEntry
        {
            public long Length;
            public DateTime WriteTime;
            public bool HasKeyframes;
        }

        private static readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        public static bool HasKeyframes(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists) return false;
            CacheEntry entry;
            if (cache.TryGetValue(path, out entry) && entry.Length == info.Length && entry.WriteTime == info.LastWriteTimeUtc)
                return entry.HasKeyframes;

            bool result;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                result = ReadHasKeyframes(stream);
            cache[path] = new CacheEntry { Length = info.Length, WriteTime = info.LastWriteTimeUtc, HasKeyframes = result };
            return result;
        }

        public static bool ReadHasKeyframes(Stream stream)
        {
            long size = stream.Length;
            var buffer = new byte[ChunkSize + Overlap];
            long end = size;
            while (end > 0)
            {
                long start = Math.Max(0, end - ChunkSize);
                // 多讀上一塊開頭的幾個位元組，避免 key 剛好跨在兩塊之間。
                int count = (int)(Math.Min(size, end + Overlap) - start);
                stream.Position = start;
                ReadExactly(stream, buffer, count);

                int key = LastIndexOf(buffer, count, TimelineKey, count);
                if (key >= 0) return SceneInfoHasKeyframes(stream, start + key + TimelineKey.Length);

                for (int header = LastIndexOf(buffer, count, BlockHeader, count); header >= 0;
                     header = LastIndexOf(buffer, count, BlockHeader, header))
                {
                    if (header + BlockHeader.Length + 4 > count) continue;
                    int length = BitConverter.ToInt32(buffer, header + BlockHeader.Length);
                    if (start + header + BlockHeader.Length + 4 + length == size) return false;
                }
                end = start;
            }
            return false;
        }

        private static bool SceneInfoHasKeyframes(Stream stream, long position)
        {
            var head = new byte[2 + SceneInfoKey.Length + 5];
            stream.Position = position;
            if (stream.Read(head, 0, head.Length) != head.Length) return false;
            // PluginData.version 是正 fixint，data 是只有 sceneInfo 一個 key 的 map。
            if (head[0] >= 0x80 || head[1] != 0x81) return false;
            for (int i = 0; i < SceneInfoKey.Length; i++)
                if (head[2 + i] != SceneInfoKey[i]) return false;

            int p = 2 + SceneInfoKey.Length;
            byte type = head[p];
            long length;
            int headerSize;
            if (type >= 0xa0 && type <= 0xbf) { length = type & 0x1f; headerSize = 1; }
            else if (type == 0xd9) { length = head[p + 1]; headerSize = 2; }
            else if (type == 0xda) { length = (head[p + 1] << 8) | head[p + 2]; headerSize = 3; }
            else if (type == 0xdb) { length = ((long)head[p + 1] << 24) | ((long)head[p + 2] << 16) | ((long)head[p + 3] << 8) | head[p + 4]; headerSize = 5; }
            else return false;
            if (length <= 0 || position + p + headerSize + length > stream.Length) return false;

            var text = new byte[length];
            stream.Position = position + p + headerSize;
            ReadExactly(stream, text, text.Length);
            return LastIndexOf(text, text.Length, Keyframe, text.Length) >= 0;
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
        }

        private static int LastIndexOf(byte[] buffer, int count, byte[] pattern, int before)
        {
            for (int i = Math.Min(before - 1, count - pattern.Length); i >= 0; i--)
            {
                if (buffer[i] != pattern[0]) continue;
                int j = 1;
                while (j < pattern.Length && buffer[i + j] == pattern[j]) j++;
                if (j == pattern.Length) return i;
            }
            return -1;
        }
    }
}
