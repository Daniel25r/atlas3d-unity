// ATLAS 3D (Unity) - texture images that phones (and glTF) cannot read
// directly: TGA (raw or RLE, 8/16/24/32 bit) and BMP (24/32 bit) are turned
// into PNG. Plain C#, so it works while a model converts in the background.
using System;
using System.IO;
using System.IO.Compression;

namespace Atlas.Import
{
    public static class ImageConv
    {
        /// PNG / JPEG as they are; TGA / BMP converted to PNG; else null.
        public static byte[] ToWebImage(byte[] d, string nameHint)
        {
            if (d == null || d.Length < 18) return null;
            if (MModel.ImageMime(d) != null) return d;
            try
            {
                int w, h;
                byte[] rgba = null;
                if (d[0] == 'B' && d[1] == 'M') rgba = Bmp(d, out w, out h);
                else if ((nameHint ?? "").ToLowerInvariant().EndsWith(".tga") || LooksTga(d)) rgba = Tga(d, out w, out h);
                else { w = h = 0; }
                return rgba != null ? Png(rgba, w, h) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static bool LooksTga(byte[] d)
        {
            int type = d[2];
            int bpp = d[16];
            return (type == 2 || type == 3 || type == 10 || type == 11) && (bpp == 8 || bpp == 16 || bpp == 24 || bpp == 32) && d[1] == 0;
        }

        static byte[] Tga(byte[] d, out int w, out int h)
        {
            int idLen = d[0], cmapType = d[1], type = d[2];
            int cmapLen = d[5] | (d[6] << 8), cmapBits = d[7];
            w = d[12] | (d[13] << 8);
            h = d[14] | (d[15] << 8);
            int bpp = d[16], desc = d[17];
            if (w <= 0 || h <= 0 || cmapType != 0) return null;
            int bytes = bpp / 8;
            int p = 18 + idLen + (cmapType == 1 ? cmapLen * ((cmapBits + 7) / 8) : 0);
            var outp = new byte[w * h * 4];
            bool rle = type == 10 || type == 11;
            int count = w * h, i = 0;
            while (i < count && p < d.Length)
            {
                int run = 1;
                bool repeat = false;
                if (rle)
                {
                    int hdr = d[p++];
                    run = (hdr & 0x7F) + 1;
                    repeat = (hdr & 0x80) != 0;
                }
                for (int k = 0; k < run && i < count; k++)
                {
                    if (p + bytes > d.Length) break;
                    byte r, g, b, a = 255;
                    if (bytes == 1) { r = g = b = d[p]; }
                    else if (bytes == 2)
                    {
                        int v = d[p] | (d[p + 1] << 8);
                        r = (byte)(((v >> 10) & 31) * 255 / 31); g = (byte)(((v >> 5) & 31) * 255 / 31); b = (byte)((v & 31) * 255 / 31);
                    }
                    else { b = d[p]; g = d[p + 1]; r = d[p + 2]; if (bytes == 4) a = d[p + 3]; }
                    int o = i * 4;
                    outp[o] = r; outp[o + 1] = g; outp[o + 2] = b; outp[o + 3] = a;
                    i++;
                    if (!rle || !repeat) p += bytes;
                }
                if (rle && repeat) p += bytes;
            }
            // TGA rows go up from the bottom unless bit 5 says otherwise.
            bool topDown = (desc & 0x20) != 0;
            return topDown ? outp : FlipRows(outp, w, h);
        }

        static byte[] Bmp(byte[] d, out int w, out int h)
        {
            int off = BitConverter.ToInt32(d, 10);
            w = BitConverter.ToInt32(d, 18);
            int hh = BitConverter.ToInt32(d, 22);
            int bpp = BitConverter.ToUInt16(d, 28);
            int comp = BitConverter.ToInt32(d, 30);
            h = Math.Abs(hh);
            if (w <= 0 || h <= 0 || (bpp != 24 && bpp != 32) || (comp != 0 && comp != 3)) return null;
            int stride = ((w * bpp / 8) + 3) & ~3;
            var outp = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int row = hh > 0 ? h - 1 - y : y;   // positive height: bottom-up
                int p = off + row * stride;
                for (int x = 0; x < w; x++)
                {
                    int q = p + x * (bpp / 8);
                    if (q + 3 > d.Length) break;
                    int o = (y * w + x) * 4;
                    outp[o] = d[q + 2]; outp[o + 1] = d[q + 1]; outp[o + 2] = d[q];
                    outp[o + 3] = bpp == 32 && comp == 0 ? (byte)255 : (bpp == 32 ? d[q + 3] : (byte)255);
                }
            }
            return outp;
        }

        static byte[] FlipRows(byte[] px, int w, int h)
        {
            var o = new byte[px.Length];
            int s = w * 4;
            for (int y = 0; y < h; y++) Buffer.BlockCopy(px, y * s, o, (h - 1 - y) * s, s);
            return o;
        }

        // ------------------------------------------------------------------
        // Minimal PNG encoder (RGBA 8 bit, no filter, zlib by DeflateStream)

        static readonly uint[] CrcTable = MakeCrc();

        static uint[] MakeCrc()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        static uint Crc(byte[] b, int off, int len)
        {
            uint c = 0xFFFFFFFFu;
            for (int i = off; i < off + len; i++) c = CrcTable[(c ^ b[i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }

        static void BE(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
        }

        static void Chunk(Stream s, string type, byte[] data)
        {
            BE(s, (uint)data.Length);
            var td = new byte[4 + data.Length];
            td[0] = (byte)type[0]; td[1] = (byte)type[1]; td[2] = (byte)type[2]; td[3] = (byte)type[3];
            Buffer.BlockCopy(data, 0, td, 4, data.Length);
            s.Write(td, 0, td.Length);
            BE(s, Crc(td, 0, td.Length));
        }

        public static byte[] Png(byte[] rgba, int w, int h)
        {
            var raw = new byte[(w * 4 + 1) * h];
            for (int y = 0; y < h; y++)
            {
                raw[y * (w * 4 + 1)] = 0;
                Buffer.BlockCopy(rgba, y * w * 4, raw, y * (w * 4 + 1) + 1, w * 4);
            }
            byte[] deflated;
            using (var ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, true)) ds.Write(raw, 0, raw.Length);
                deflated = ms.ToArray();
            }
            uint a = 1, b = 0;
            foreach (byte x in raw) { a = (a + x) % 65521; b = (b + a) % 65521; }
            var z = new byte[2 + deflated.Length + 4];
            z[0] = 0x78; z[1] = 0x01;
            Buffer.BlockCopy(deflated, 0, z, 2, deflated.Length);
            uint adler = (b << 16) | a;
            z[z.Length - 4] = (byte)(adler >> 24); z[z.Length - 3] = (byte)(adler >> 16); z[z.Length - 2] = (byte)(adler >> 8); z[z.Length - 1] = (byte)adler;
            using (var s = new MemoryStream())
            {
                s.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                var ihdr = new byte[13];
                ihdr[0] = (byte)(w >> 24); ihdr[1] = (byte)(w >> 16); ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w;
                ihdr[4] = (byte)(h >> 24); ihdr[5] = (byte)(h >> 16); ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h;
                ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
                Chunk(s, "IHDR", ihdr);
                Chunk(s, "IDAT", z);
                Chunk(s, "IEND", new byte[0]);
                return s.ToArray();
            }
        }
    }
}
