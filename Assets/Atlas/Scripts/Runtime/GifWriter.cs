// ATLAS 3D (Unity) - animated GIF writer (GIF89a, looping): a fixed 256
// colour palette with ordered dithering (fast and the same for every
// frame, so the animation does not flicker) and LZW compression.
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Atlas
{
    public class GifWriter : System.IDisposable
    {
        readonly Stream s;
        readonly int w, h, delay;
        static byte[] palette;
        static readonly int[,] Bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };

        public GifWriter(Stream stream, int width, int height, int fps)
        {
            s = stream; w = width; h = height;
            delay = Mathf.Max(2, Mathf.RoundToInt(100f / Mathf.Max(1, fps)));
            BuildPalette();
            Write("GIF89a");
            U16(w); U16(h);
            s.WriteByte(0xF7);   // global colour table, 256 colours
            s.WriteByte(0); s.WriteByte(0);
            s.Write(palette, 0, palette.Length);
            // Loop forever (NETSCAPE2.0)
            s.Write(new byte[] { 0x21, 0xFF, 0x0B }, 0, 3);
            Write("NETSCAPE2.0");
            s.Write(new byte[] { 3, 1, 0, 0, 0 }, 0, 5);
        }

        /// 6×7×6 colour cube (252) + 4 greys.
        static void BuildPalette()
        {
            if (palette != null) return;
            palette = new byte[256 * 3];
            int i = 0;
            for (int r = 0; r < 6; r++)
                for (int g = 0; g < 7; g++)
                    for (int b = 0; b < 6; b++)
                    {
                        palette[i * 3] = (byte)(r * 255 / 5);
                        palette[i * 3 + 1] = (byte)(g * 255 / 6);
                        palette[i * 3 + 2] = (byte)(b * 255 / 5);
                        i++;
                    }
            foreach (int gv in new[] { 64, 128, 192, 224 }) { palette[i * 3] = palette[i * 3 + 1] = palette[i * 3 + 2] = (byte)gv; i++; }
        }

        void Write(string t) { foreach (char c in t) s.WriteByte((byte)c); }
        void U16(int v) { s.WriteByte((byte)(v & 255)); s.WriteByte((byte)(v >> 8)); }

        /// One frame (rows from the bottom, as Unity gives them).
        public void AddFrame(Color32[] px)
        {
            var idx = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int src = (h - 1 - y) * w;
                for (int x = 0; x < w; x++)
                {
                    var c = px[src + x];
                    float d = (Bayer[y & 3, x & 3] + 0.5f) / 16f - 0.5f;
                    int r = Mathf.Clamp(Mathf.RoundToInt(c.r / 255f * 5f + d), 0, 5);
                    int g = Mathf.Clamp(Mathf.RoundToInt(c.g / 255f * 6f + d), 0, 6);
                    int b = Mathf.Clamp(Mathf.RoundToInt(c.b / 255f * 5f + d), 0, 5);
                    idx[y * w + x] = (byte)(r * 42 + g * 6 + b);
                }
            }
            // Graphic control: delay, no transparency.
            s.Write(new byte[] { 0x21, 0xF9, 4, 0 }, 0, 4);
            U16(delay);
            s.WriteByte(0); s.WriteByte(0);
            // Image descriptor
            s.WriteByte(0x2C);
            U16(0); U16(0); U16(w); U16(h);
            s.WriteByte(0);
            Lzw(idx);
        }

        void Lzw(byte[] data)
        {
            const int minCode = 8;
            s.WriteByte(minCode);
            var block = new List<byte>(255);
            int bitBuf = 0, bitCount = 0;
            System.Action<int, int> emit = (code, size) =>
            {
                bitBuf |= code << bitCount;
                bitCount += size;
                while (bitCount >= 8)
                {
                    block.Add((byte)(bitBuf & 255));
                    bitBuf >>= 8;
                    bitCount -= 8;
                    if (block.Count == 255) { s.WriteByte(255); s.Write(block.ToArray(), 0, 255); block.Clear(); }
                }
            };
            int clear = 1 << minCode, eoi = clear + 1;
            var dict = new Dictionary<int, int>();
            int next = eoi + 1, size = minCode + 1;
            emit(clear, size);
            int prefix = data[0];
            for (int i = 1; i < data.Length; i++)
            {
                int k = data[i];
                int key = (prefix << 8) | k;
                int code;
                if (dict.TryGetValue(key, out code)) { prefix = code; continue; }
                emit(prefix, size);
                if (next < 4096)
                {
                    dict[key] = next++;
                    if (next > (1 << size) && size < 12) size++;
                }
                else
                {
                    emit(clear, size);
                    dict.Clear();
                    next = eoi + 1;
                    size = minCode + 1;
                }
                prefix = k;
            }
            emit(prefix, size);
            emit(eoi, size);
            if (bitCount > 0) { block.Add((byte)(bitBuf & 255)); }
            if (block.Count > 0) { s.WriteByte((byte)block.Count); s.Write(block.ToArray(), 0, block.Count); }
            s.WriteByte(0);
        }

        public void Dispose()
        {
            s.WriteByte(0x3B);
            s.Flush();
        }
    }
}
