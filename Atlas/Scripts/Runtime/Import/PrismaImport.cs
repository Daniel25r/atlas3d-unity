// ATLAS 3D (Unity) - Prisma3D projects (.prisma, .proj, .pobject), ported
// from the Godot version: meshes, materials, textures, skeleton and skin,
// turned into a model and then a GLB.
//
// The format, worked out from real project files:
// - a zip whose first byte is altered (0x60 instead of 0x50 'P');
// - <project>/project.proj: 4 header bytes + MessagePack [settings, root
//   objects, materials, …];
// - <project>/content/<id>.pobject: 4 bytes + MessagePack [id, objects,
//   meshes]. Object: [id, name, components, parent id, active, …].
//   Components: [type, [[property, value (JSON text), type name], …]]
//   (PTransform, PBone, PMeshRenderer, Primitives.PrimitivePlane…).
//   Mesh: [id, face submesh, face corner count, face normal, corner vertex,
//   corner uv, corner edge id, corner index, vertex position, vertex skin
//   [b0..b3, w0..w3], bind poses (4×4, column-major)];
// - <project>/res/*.png: textures.
// Prisma3D works like Unity (left-handed, Y up): mirrored on X for glTF.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Atlas.Import
{
    /// A .proj picked without the .pobject files of its «content» folder.
    public class MissingContentException : Exception
    {
        public readonly int count;
        public string names = "";
        public string folder = "", file = "";   // where the project was copied (set by SceneStore)
        public MissingContentException(int n) : base("a este proyecto de Prisma3D le faltan sus mallas (archivos .pobject de la carpeta «content»)") { count = n; }
    }

    public static class PrismaImport
    {
        static readonly byte[] Magic = { 0x14, 0xd3, 0x82, 0x1b };

        public static bool IsAlteredZip(byte[] h)
        {
            return h != null && h.Length >= 4 && h[0] != 0x50 && h[1] == 0x4B && h[2] == 0x03 && h[3] == 0x04;
        }

        static bool IsArr(byte b) { return (b >= 0x90 && b <= 0x9f) || b == 0xdc || b == 0xdd; }

        public static bool IsProjectFile(byte[] h)
        {
            if (h == null || h.Length < 6) return false;
            bool magic = true;
            // The 4th byte changes between Prisma3D versions (0x1b, 0x18…).
            for (int k = 0; k < 3; k++) if (h[k] != Magic[k]) magic = false;
            return (magic && IsArr(h[4])) || (h[0] == 0x00 && h[1] == 0x20 && IsArr(h[4]));
        }

        // ------------------------------------------------------------------
        // Entry point

        /// .prisma (zip), .proj or .pobject -> model.
        /// Pictures the last project asked for but did not bring (a .proj
        /// picked without its «res» folder).
        public static readonly List<string> MissingTextures = new List<string>();

        public static MModel Load(string path)
        {
            MissingTextures.Clear();
            byte[] head = new byte[8];
            using (var f = File.OpenRead(path)) f.Read(head, 0, 8);
            string projPath = path;
            string rootDir = Path.GetDirectoryName(path);
            fileDir = rootDir;
            if (IsAlteredZip(head) || (head[0] == 0x50 && head[1] == 0x4B))
            {
                // Repair the signature and unpack next to the file.
                var data = File.ReadAllBytes(path);
                data[0] = 0x50;
                string dest = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_prisma");
                Directory.CreateDirectory(dest);
                using (var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read))
                {
                    foreach (var e in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;
                        string rel = e.FullName.Replace('\\', '/');
                        if (rel.Contains("..")) continue;
                        string outPath = Path.Combine(dest, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                        using (var src = e.Open())
                        using (var dst = File.Create(outPath)) src.CopyTo(dst);
                    }
                }
                var projs = Directory.GetFiles(dest, "*.proj", SearchOption.AllDirectories);
                if (projs.Length == 0) throw new Exception("el .prisma no contiene un proyecto (.proj)");
                projPath = projs[0];
                rootDir = Path.GetDirectoryName(projPath);
            }
            return Build(rootDir, projPath);
        }

        // ------------------------------------------------------------------
        // MessagePack (big-endian)

        class MsgPack
        {
            readonly byte[] b;
            public int i;
            public MsgPack(byte[] d, int start) { b = d; i = start; }

            ulong U(int n)
            {
                if (i + n > b.Length) { i = b.Length; return 0; }
                ulong v = 0;
                for (int k = 0; k < n; k++) v = (v << 8) | b[i + k];
                i += n;
                return v;
            }

            string Str(int n)
            {
                if (n < 0 || i + n > b.Length) { i = b.Length; return ""; }
                string s = Encoding.UTF8.GetString(b, i, n);
                i += n;
                return s;
            }

            public object Read()
            {
                if (i >= b.Length) return null;
                byte t = b[i++];
                if (t <= 0x7f) return (long)t;
                if (t >= 0xe0) return (long)(t - 256);
                if (t >= 0x80 && t <= 0x8f) return Map(t & 15);
                if (t >= 0x90 && t <= 0x9f) return Arr(t & 15);
                if (t >= 0xa0 && t <= 0xbf) return Str(t & 31);
                switch (t)
                {
                    case 0xc0: return null;
                    case 0xc2: return false;
                    case 0xc3: return true;
                    case 0xc4: return Bin((int)U(1));
                    case 0xc5: return Bin((int)U(2));
                    case 0xc6: return Bin((int)U(4));
                    case 0xca: { uint v = (uint)U(4); return (double)BitConverter.ToSingle(BitConverter.GetBytes(v), 0); }
                    case 0xcb: { ulong v = U(8); return BitConverter.Int64BitsToDouble((long)v); }
                    case 0xcc: return (long)U(1);
                    case 0xcd: return (long)U(2);
                    case 0xce: return (long)U(4);
                    case 0xcf: return (long)U(8);
                    case 0xd0: return (long)(sbyte)U(1);
                    case 0xd1: return (long)(short)U(2);
                    case 0xd2: return (long)(int)U(4);
                    case 0xd3: return (long)U(8);
                    case 0xd9: return Str((int)U(1));
                    case 0xda: return Str((int)U(2));
                    case 0xdb: return Str((int)U(4));
                    case 0xdc: return Arr((int)U(2));
                    case 0xdd: return Arr((int)U(4));
                    case 0xde: return Map((int)U(2));
                    case 0xdf: return Map((int)U(4));
                    case 0xd4: i += 2; return null;
                    case 0xd5: i += 3; return null;
                    case 0xd6: i += 5; return null;
                    case 0xd7: i += 9; return null;
                    case 0xd8: i += 17; return null;
                    case 0xc7: { int n = (int)U(1); i += 1 + n; return null; }
                    case 0xc8: { int n = (int)U(2); i += 1 + n; return null; }
                    case 0xc9: { int n = (int)U(4); i += 1 + n; return null; }
                }
                i = b.Length;
                return null;
            }

            byte[] Bin(int n)
            {
                if (n < 0 || i + n > b.Length) { i = b.Length; return new byte[0]; }
                var s = new byte[n];
                Buffer.BlockCopy(b, i, s, 0, n);
                i += n;
                return s;
            }

            List<object> Arr(int n)
            {
                var a = new List<object>();
                if (n < 0 || n > b.Length - i) { i = b.Length; return a; }
                for (int k = 0; k < n; k++) a.Add(Read());
                return a;
            }

            Dictionary<object, object> Map(int n)
            {
                var d = new Dictionary<object, object>();
                if (n < 0 || n * 2 > b.Length - i) { i = b.Length; return d; }
                for (int k = 0; k < n; k++)
                {
                    var key = Read();
                    var val = Read();
                    if (key != null) d[key] = val;
                }
                return d;
            }
        }

        static object Decode(byte[] data)
        {
            if (data == null || data.Length <= 4) return null;
            // MessagePack-CSharp LZ4 (newer Prisma3D files): unpacked first.
            for (int start = 0; start <= 4; start += 4)
            {
                byte[] plain = Lz4Unpack(data, start);
                if (plain != null)
                {
                    var z = new MsgPack(plain, 0).Read() as List<object>;
                    if (z != null && z.Count > 0) return z;
                }
            }
            var v = new MsgPack(data, 4).Read() as List<object>;
            if (v != null && v.Count > 0) return v;
            var w = new MsgPack(data, 0).Read() as List<object>;
            return w != null && w.Count > 0 ? w : (object)v;
        }

        /// LZ4 as MessagePack-CSharp writes it: one block (ext 99: int length +
        /// LZ4 data) or a block array ([ext 98: lengths…, bin, bin…]); null
        /// when the data is not compressed.
        static byte[] Lz4Unpack(byte[] d, int at)
        {
            try
            {
                if (at >= d.Length) return null;
                int i = at;
                int extType, extLen;
                if (ReadExt(d, ref i, out extType, out extLen) && extType == 99)
                {
                    int end = i + extLen;
                    int j = i;
                    long size = ReadInt(d, ref j);
                    if (size <= 0 || size > 512L * 1024 * 1024) return null;
                    var outb = new byte[size];
                    Lz4Block(d, j, end - j, outb, 0, (int)size);
                    return outb;
                }
                i = at;
                byte t = d[i];
                int count;
                if (t >= 0x90 && t <= 0x9f) { count = t & 15; i++; }
                else if (t == 0xdc) { count = (d[i + 1] << 8) | d[i + 2]; i += 3; }
                else return null;
                if (count < 2 || !ReadExt(d, ref i, out extType, out extLen) || extType != 98) return null;
                int lend = i + extLen;
                var sizes = new List<long>();
                while (i < lend) sizes.Add(ReadInt(d, ref i));
                if (sizes.Count != count - 1) return null;
                long total = 0;
                foreach (var s in sizes) { if (s < 0) return null; total += s; }
                if (total <= 0 || total > 512L * 1024 * 1024) return null;
                var all = new byte[total];
                int o = 0;
                foreach (var s in sizes)
                {
                    int bl = ReadBinLen(d, ref i);
                    if (bl < 0) return null;
                    Lz4Block(d, i, bl, all, o, (int)s);
                    i += bl;
                    o += (int)s;
                }
                return all;
            }
            catch (Exception) { return null; }
        }

        static bool ReadExt(byte[] d, ref int i, out int type, out int len)
        {
            type = -1; len = 0;
            byte t = d[i];
            if (t == 0xc7) { len = d[i + 1]; type = (sbyte)d[i + 2]; i += 3; }
            else if (t == 0xc8) { len = (d[i + 1] << 8) | d[i + 2]; type = (sbyte)d[i + 3]; i += 4; }
            else if (t == 0xc9) { len = (d[i + 1] << 24) | (d[i + 2] << 16) | (d[i + 3] << 8) | d[i + 4]; type = (sbyte)d[i + 5]; i += 6; }
            else if (t >= 0xd4 && t <= 0xd8) { len = 1 << (t - 0xd4); type = (sbyte)d[i + 1]; i += 2; }
            else return false;
            return len >= 0 && i + len <= d.Length;
        }

        static long ReadInt(byte[] d, ref int i)
        {
            byte t = d[i++];
            if (t <= 0x7f) return t;
            if (t >= 0xe0) return t - 256;
            long v = 0;
            int n;
            switch (t)
            {
                case 0xcc: case 0xd0: n = 1; break;
                case 0xcd: case 0xd1: n = 2; break;
                case 0xce: case 0xd2: n = 4; break;
                case 0xcf: case 0xd3: n = 8; break;
                default: throw new Exception("lz4: longitud no válida");
            }
            for (int k = 0; k < n; k++) v = (v << 8) | d[i + k];
            i += n;
            if (t == 0xd0) v = (sbyte)v; else if (t == 0xd1) v = (short)v; else if (t == 0xd2) v = (int)v;
            return v;
        }

        static int ReadBinLen(byte[] d, ref int i)
        {
            byte t = d[i];
            if (t == 0xc4) { i += 2; return d[i - 1]; }
            if (t == 0xc5) { i += 3; return (d[i - 2] << 8) | d[i - 1]; }
            if (t == 0xc6) { i += 5; return (d[i - 4] << 24) | (d[i - 3] << 16) | (d[i - 2] << 8) | d[i - 1]; }
            return -1;
        }

        /// Plain LZ4 block decoder.
        static void Lz4Block(byte[] src, int si, int slen, byte[] dst, int di, int dlen)
        {
            int send = si + slen, dend = di + dlen, dstart = di;
            while (si < send)
            {
                int token = src[si++];
                int lit = token >> 4;
                if (lit == 15) { int b; do { b = src[si++]; lit += b; } while (b == 255); }
                if (di + lit > dend || si + lit > send) throw new Exception("lz4: datos dañados");
                Buffer.BlockCopy(src, si, dst, di, lit);
                si += lit; di += lit;
                if (si >= send || di >= dend) break;
                int off = src[si] | (src[si + 1] << 8);
                si += 2;
                if (off == 0 || di - off < dstart) throw new Exception("lz4: desplazamiento no válido");
                int ml = token & 15;
                if (ml == 15) { int b; do { b = src[si++]; ml += b; } while (b == 255); }
                ml += 4;
                if (di + ml > dend) throw new Exception("lz4: datos dañados");
                for (int k = 0; k < ml; k++) { dst[di] = dst[di - off]; di++; }
            }
            if (di != dend) throw new Exception("lz4: tamaño distinto");
        }

        // ------------------------------------------------------------------
        // Small JSON reader (property values are JSON text)

        static object Json(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int i = 0;
            try { return JValue(s, ref i); }
            catch (Exception) { return null; }
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object JValue(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (i < s.Length)
                {
                    Ws(s, ref i);
                    string k = (string)JValue(s, ref i);
                    Ws(s, ref i);
                    i++;   // :
                    d[k ?? ""] = JValue(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    i++;   // }
                    break;
                }
                return d;
            }
            if (c == '[')
            {
                var a = new List<object>();
                i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (i < s.Length)
                {
                    a.Add(JValue(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    i++;
                    break;
                }
                return a;
            }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        char e = s[i];
                        if (e == 'u' && i + 4 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                        else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                i++;
                return sb.ToString();
            }
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double v;
            double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }

        static Dictionary<string, object> JObj(object raw)
        {
            return Json(raw as string) as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        static double Num(object o, double def)
        {
            if (o is double) return (double)o;
            if (o is long) return (long)o;
            if (o is string)
            {
                double v;
                if (double.TryParse((string)o, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            }
            return def;
        }

        static double[] Vec3(object raw, double dx, double dy, double dz)
        {
            var d = Json(raw as string) as Dictionary<string, object>;
            if (d == null) return new[] { dx, dy, dz };
            object x, y, z;
            d.TryGetValue("x", out x); d.TryGetValue("y", out y); d.TryGetValue("z", out z);
            return new[] { Num(x, dx), Num(y, dy), Num(z, dz) };
        }

        static string Sid(object v)
        {
            if (v == null) return "";
            if (v is double && Math.Abs((double)v - Math.Round((double)v)) < 1e-9) return ((long)Math.Round((double)v)).ToString(CultureInfo.InvariantCulture);
            if (v is long) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        static List<object> L(object o) { return o as List<object>; }

        // ------------------------------------------------------------------
        // Objects

        static Dictionary<string, object> Props(List<object> obj, string comp)
        {
            var comps = L(obj[2]);
            if (comps == null) return null;
            foreach (var c0 in comps)
            {
                var c = L(c0);
                if (c == null || c.Count < 2 || !(c[0] is string) || (string)c[0] != comp) continue;
                var d = new Dictionary<string, object>();
                var ps = L(c[1]);
                if (ps != null)
                    foreach (var p0 in ps)
                    {
                        var p = L(p0);
                        if (p != null && p.Count >= 2 && p[0] is string) d[(string)p[0]] = p[1];
                    }
                return d;
            }
            return null;
        }

        static string PrimitiveKind(List<object> obj)
        {
            var comps = L(obj[2]);
            if (comps == null) return "";
            foreach (var c0 in comps)
            {
                var c = L(c0);
                if (c != null && c.Count >= 1 && c[0] is string && ((string)c[0]).StartsWith("Primitives.")) return ((string)c[0]).Substring(11);
            }
            return "";
        }

        /// Unity local transform (left-handed) -> glTF node (mirrored on X).
        static void LocalTrs(List<object> obj, MNode n)
        {
            var t = Props(obj, "PTransform");
            if (t == null) return;
            object raw;
            t.TryGetValue("localPosition", out raw);
            var pos = Vec3(raw, 0, 0, 0);
            t.TryGetValue("localEulerAnglesHint", out raw);
            var e = Vec3(raw, 0, 0, 0);
            t.TryGetValue("localScale", out raw);
            var s = Vec3(raw, 1, 1, 1);
            // Unity: Z, then X, then Y  -> q = qY * qX * qZ
            var q = QMul(QMul(QAxis(0, 1, 0, e[1]), QAxis(1, 0, 0, e[0])), QAxis(0, 0, 1, e[2]));
            n.t = new[] { (float)-pos[0], (float)pos[1], (float)pos[2] };
            n.r = new[] { (float)q[0], (float)-q[1], (float)-q[2], (float)q[3] };
            n.s = new[] { (float)s[0], (float)s[1], (float)s[2] };
        }

        static double[] QAxis(double x, double y, double z, double deg)
        {
            double h = deg * Math.PI / 360.0, s = Math.Sin(h);
            return new[] { x * s, y * s, z * s, Math.Cos(h) };
        }

        static double[] QMul(double[] a, double[] b)
        {
            return new[]
            {
                a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
                a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
                a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
                a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2],
            };
        }

        // 4x4 column-major helpers (for global transforms of rigid pieces)
        static float[] Trs(MNode n)
        {
            float x = n.r[0], y = n.r[1], z = n.r[2], w = n.r[3];
            float[] m =
            {
                (1 - 2 * (y * y + z * z)) * n.s[0], (2 * (x * y + z * w)) * n.s[0], (2 * (x * z - y * w)) * n.s[0], 0,
                (2 * (x * y - z * w)) * n.s[1], (1 - 2 * (x * x + z * z)) * n.s[1], (2 * (y * z + x * w)) * n.s[1], 0,
                (2 * (x * z + y * w)) * n.s[2], (2 * (y * z - x * w)) * n.s[2], (1 - 2 * (x * x + y * y)) * n.s[2], 0,
                n.t[0], n.t[1], n.t[2], 1,
            };
            return m;
        }

        static float[] Mul(float[] a, float[] c)
        {
            var r = new float[16];
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                {
                    float s = 0f;
                    for (int k = 0; k < 4; k++) s += a[k * 4 + row] * c[col * 4 + k];
                    r[col * 4 + row] = s;
                }
            return r;
        }

        static float[] Inverse(float[] m)
        {
            var inv = new float[16];
            inv[0] = m[5] * m[10] * m[15] - m[5] * m[11] * m[14] - m[9] * m[6] * m[15] + m[9] * m[7] * m[14] + m[13] * m[6] * m[11] - m[13] * m[7] * m[10];
            inv[4] = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14] + m[8] * m[6] * m[15] - m[8] * m[7] * m[14] - m[12] * m[6] * m[11] + m[12] * m[7] * m[10];
            inv[8] = m[4] * m[9] * m[15] - m[4] * m[11] * m[13] - m[8] * m[5] * m[15] + m[8] * m[7] * m[13] + m[12] * m[5] * m[11] - m[12] * m[7] * m[9];
            inv[12] = -m[4] * m[9] * m[14] + m[4] * m[10] * m[13] + m[8] * m[5] * m[14] - m[8] * m[6] * m[13] - m[12] * m[5] * m[10] + m[12] * m[6] * m[9];
            inv[1] = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14] + m[9] * m[2] * m[15] - m[9] * m[3] * m[14] - m[13] * m[2] * m[11] + m[13] * m[3] * m[10];
            inv[5] = m[0] * m[10] * m[15] - m[0] * m[11] * m[14] - m[8] * m[2] * m[15] + m[8] * m[3] * m[14] + m[12] * m[2] * m[11] - m[12] * m[3] * m[10];
            inv[9] = -m[0] * m[9] * m[15] + m[0] * m[11] * m[13] + m[8] * m[1] * m[15] - m[8] * m[3] * m[13] - m[12] * m[1] * m[11] + m[12] * m[3] * m[9];
            inv[13] = m[0] * m[9] * m[14] - m[0] * m[10] * m[13] - m[8] * m[1] * m[14] + m[8] * m[2] * m[13] + m[12] * m[1] * m[10] - m[12] * m[2] * m[9];
            inv[2] = m[1] * m[6] * m[15] - m[1] * m[7] * m[14] - m[5] * m[2] * m[15] + m[5] * m[3] * m[14] + m[13] * m[2] * m[7] - m[13] * m[3] * m[6];
            inv[6] = -m[0] * m[6] * m[15] + m[0] * m[7] * m[14] + m[4] * m[2] * m[15] - m[4] * m[3] * m[14] - m[12] * m[2] * m[7] + m[12] * m[3] * m[6];
            inv[10] = m[0] * m[5] * m[15] - m[0] * m[7] * m[13] - m[4] * m[1] * m[15] + m[4] * m[3] * m[13] + m[12] * m[1] * m[7] - m[12] * m[3] * m[5];
            inv[14] = -m[0] * m[5] * m[14] + m[0] * m[6] * m[13] + m[4] * m[1] * m[14] - m[4] * m[2] * m[13] - m[12] * m[1] * m[6] + m[12] * m[2] * m[5];
            inv[3] = -m[1] * m[6] * m[11] + m[1] * m[7] * m[10] + m[5] * m[2] * m[11] - m[5] * m[3] * m[10] - m[9] * m[2] * m[7] + m[9] * m[3] * m[6];
            inv[7] = m[0] * m[6] * m[11] - m[0] * m[7] * m[10] - m[4] * m[2] * m[11] + m[4] * m[3] * m[10] + m[8] * m[2] * m[7] - m[8] * m[3] * m[6];
            inv[11] = -m[0] * m[5] * m[11] + m[0] * m[7] * m[9] + m[4] * m[1] * m[11] - m[4] * m[3] * m[9] - m[8] * m[1] * m[7] + m[8] * m[3] * m[5];
            inv[15] = m[0] * m[5] * m[10] - m[0] * m[6] * m[9] - m[4] * m[1] * m[10] + m[4] * m[2] * m[9] + m[8] * m[1] * m[6] - m[8] * m[2] * m[5];
            float det = m[0] * inv[0] + m[1] * inv[4] + m[2] * inv[8] + m[3] * inv[12];
            if (Math.Abs(det) < 1e-20f) return new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
            for (int i = 0; i < 16; i++) inv[i] /= det;
            return inv;
        }

        // ------------------------------------------------------------------
        // Materials

        static float Lin(double c)
        {
            float v = (float)c;
            return v <= 0.04045f ? v / 12.92f : (float)Math.Pow((v + 0.055f) / 1.055f, 2.4f);
        }

        static int Material(List<object> md, string rootDir, MModel m, Dictionary<string, int> texCache)
        {
            var mm = new MMaterial { name = md.Count > 1 && md[1] is string ? (string)md[1] : "material", roughness = 0.6f };
            var props = new Dictionary<string, object>();
            var ps = md.Count > 2 ? L(md[2]) : null;
            if (ps != null)
                foreach (var p0 in ps)
                {
                    var p = L(p0);
                    if (p != null && p.Count >= 2 && p[0] is string) props[(string)p[0]] = p[1];
                }
            object raw;
            float a = 1f;
            if (props.TryGetValue("color", out raw))
            {
                var c = JObj(raw);
                object r, g, b, al;
                c.TryGetValue("r", out r); c.TryGetValue("g", out g); c.TryGetValue("b", out b); c.TryGetValue("a", out al);
                mm.color = new[] { Lin(Num(r, 1)), Lin(Num(g, 1)), Lin(Num(b, 1)), 1f };
                a = (float)Num(al, 1);
            }
            if (props.TryGetValue("alpha", out raw)) a *= (float)Num(raw, 1);
            mm.color[3] = Math.Max(0f, Math.Min(1f, a));
            if (props.TryGetValue("glossiness", out raw)) mm.roughness = Math.Max(0f, Math.Min(1f, 1f - (float)Num(raw, 0.4)));
            if (props.TryGetValue("cullMode", out raw) && raw is string && ((string)raw == "Off" || (string)raw == "Front")) mm.doubleSided = true;
            if (props.TryGetValue("texture", out raw) && raw is string && (string)raw != "")
            {
                string tex = ((string)raw).Replace('\\', '/');
                if (tex.StartsWith("./")) tex = tex.Substring(2);
                int ti;
                if (!texCache.TryGetValue(tex, out ti))
                {
                    ti = -1;
                    var img = FbxImport.FindImage(Path.Combine(rootDir, tex), Path.Combine("res", Path.GetFileName(tex)), rootDir);
                    if (img == null && !MissingTextures.Contains(Path.GetFileName(tex))) MissingTextures.Add(Path.GetFileName(tex));
                    if (img != null)
                    {
                        m.textures.Add(new MTexture { data = img, mime = MModel.ImageMime(img) });
                        ti = m.textures.Count - 1;
                    }
                    texCache[tex] = ti;
                }
                if (ti >= 0)
                {
                    mm.texture = ti;
                    // «Pixel blending» off in Prisma3D: pixel art, keep it sharp.
                    object pb;
                    if (props.TryGetValue("pixelBlending", out pb) && pb is string && (string)pb == "False") m.textures[ti].nearest = true;
                    // PNG with an alpha channel: cut-out (hair, eyelashes…)
                    var d = m.textures[ti].data;
                    if (d.Length > 25 && d[0] == 0x89 && (d[25] == 6 || d[25] == 4)) mm.alphaMask = true;
                }
            }
            m.materials.Add(mm);
            return m.materials.Count - 1;
        }

        // ------------------------------------------------------------------
        // Meshes

        static bool MeshOk(List<object> md)
        {
            if (md.Count < 10) return false;
            foreach (int k in new[] { 1, 2, 3, 4, 8 }) if (L(md[k]) == null) return false;
            if (L(md[5]) == null) md[5] = new List<object>();
            long corners = 0;
            foreach (var n in L(md[2])) corners += (long)Num(n, 0);
            if (L(md[4]).Count < corners || L(md[3]).Count < L(md[2]).Count) return false;
            int nv = L(md[8]).Count;
            foreach (var v in L(md[4])) { long x = (long)Num(v, -1); if (x < 0 || x >= nv) return false; }
            foreach (var p in L(md[8])) { var pl = L(p); if (pl == null || pl.Count < 3) return false; }
            if (L(md[9]) == null) md[9] = new List<object>();
            return true;
        }

        /// One primitive per submesh; corners unshared (normals smoothed up
        /// to the break angle, like Prisma3D).
        static MMesh BuildMesh(List<object> md, List<int> mats, bool skinned, double breakAngle)
        {
            var faceSub = L(md[1]);
            var faceN = L(md[2]);
            var faceNrm = L(md[3]);
            var cornerV = L(md[4]);
            var cornerUv = L(md[5]);
            var verts = L(md[8]);
            var skin = L(md[9]);
            int nf = faceN.Count;
            var starts = new int[nf];
            var vfaces = new Dictionary<int, List<int>>();
            int c = 0;
            for (int f = 0; f < nf; f++)
            {
                starts[f] = c;
                int n = (int)Num(faceN[f], 0);
                for (int k = 0; k < n; k++)
                {
                    int vi = (int)Num(cornerV[c + k], 0);
                    List<int> l;
                    if (!vfaces.TryGetValue(vi, out l)) { l = new List<int>(); vfaces[vi] = l; }
                    l.Add(f);
                }
                c += n;
            }
            var fn = new float[nf * 3];
            for (int f = 0; f < nf; f++)
            {
                var n = f < faceNrm.Count ? L(faceNrm[f]) : null;
                float x = 0, y = 1, z = 0;
                if (n != null && n.Count >= 3) { x = -(float)Num(n[0], 0); y = (float)Num(n[1], 0); z = (float)Num(n[2], 0); }
                float l = (float)Math.Sqrt(x * x + y * y + z * z);
                if (l > 1e-12f) { x /= l; y /= l; z /= l; } else { x = 0; y = 1; z = 0; }
                fn[f * 3] = x; fn[f * 3 + 1] = y; fn[f * 3 + 2] = z;
            }
            float cosBreak = (float)Math.Cos(breakAngle * Math.PI / 180.0);
            var pos = new List<float>();
            var nor = new List<float>();
            var uv = new List<float>();
            var joints = new List<ushort>();
            var weights = new List<float>();
            var bySub = new Dictionary<int, List<uint>>();
            var order = new List<int>();
            bool hasUv = cornerUv.Count > 0;
            for (int f = 0; f < nf; f++)
            {
                int sub = f < faceSub.Count ? (int)Num(faceSub[f], 0) : 0;
                List<uint> idx;
                if (!bySub.TryGetValue(sub, out idx)) { idx = new List<uint>(); bySub[sub] = idx; order.Add(sub); }
                int n = (int)Num(faceN[f], 0);
                for (int k = 1; k < n - 1; k++)
                {
                    // Mirrored on X: corners reversed so the front faces out.
                    int[] tri = { starts[f], starts[f] + k + 1, starts[f] + k };
                    foreach (int ci in tri)
                    {
                        int vi = (int)Num(cornerV[ci], 0);
                        float nx = 0, ny = 0, nz = 0;
                        List<int> faces;
                        if (vfaces.TryGetValue(vi, out faces))
                            foreach (int of in faces)
                                if (fn[of * 3] * fn[f * 3] + fn[of * 3 + 1] * fn[f * 3 + 1] + fn[of * 3 + 2] * fn[f * 3 + 2] >= cosBreak)
                                { nx += fn[of * 3]; ny += fn[of * 3 + 1]; nz += fn[of * 3 + 2]; }
                        float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (nl > 1e-4f) { nx /= nl; ny /= nl; nz /= nl; }
                        else { nx = fn[f * 3]; ny = fn[f * 3 + 1]; nz = fn[f * 3 + 2]; }
                        idx.Add((uint)(pos.Count / 3));
                        var p = L(verts[vi]);
                        pos.Add(-(float)Num(p[0], 0)); pos.Add((float)Num(p[1], 0)); pos.Add((float)Num(p[2], 0));
                        nor.Add(nx); nor.Add(ny); nor.Add(nz);
                        if (hasUv)
                        {
                            var u = ci < cornerUv.Count ? L(cornerUv[ci]) : null;
                            uv.Add(u != null && u.Count >= 2 ? (float)Num(u[0], 0) : 0f);
                            uv.Add(u != null && u.Count >= 2 ? 1f - (float)Num(u[1], 0) : 0f);
                        }
                        if (skinned)
                        {
                            var sw = vi < skin.Count ? L(skin[vi]) : null;
                            float sum = 0f;
                            var wv = new float[4];
                            var jv = new ushort[4];
                            if (sw != null && sw.Count >= 8)
                                for (int j = 0; j < 4; j++)
                                {
                                    int bi = (int)Num(sw[j], 0);
                                    jv[j] = (ushort)Math.Max(0, bi);
                                    wv[j] = Math.Max(0f, (float)Num(sw[4 + j], 0));
                                    sum += wv[j];
                                }
                            if (sum <= 0f) { wv[0] = 1f; sum = 1f; jv[0] = 0; }
                            for (int j = 0; j < 4; j++) { joints.Add(wv[j] > 0f ? jv[j] : (ushort)0); weights.Add(wv[j] / sum); }
                        }
                    }
                }
            }
            if (pos.Count == 0) return null;
            var mesh = new MMesh { pos = pos.ToArray(), nor = nor.ToArray(), uv = hasUv ? uv.ToArray() : null };
            if (skinned) { mesh.joints = joints.ToArray(); mesh.weights = weights.ToArray(); }
            foreach (int sub in order)
            {
                int mat = sub < mats.Count ? mats[sub] : (mats.Count > 0 ? mats[0] : -1);
                mesh.parts.Add(new MPart { material = mat, indices = bySub[sub].ToArray() });
            }
            return mesh;
        }

        // ------------------------------------------------------------------
        // Scene

        /// Objects written in the project itself ([id, name, components,
        /// parent, …] with number ids) and a mesh list after the materials.
        static bool Embedded(List<object> e)
        {
            var objs = L(e[1]);
            var first = L(objs[0]);
            if (first == null || first.Count < 4 || first[0] is string || L(first[2]) == null) return false;
            var m0 = L(L(e[3])[0]);
            return m0 != null && (m0.Count == 9 || m0.Count == 11) && L(m0[1]) != null;
        }

        static bool IsContent(object v)
        {
            var a = L(v);
            return a != null && a.Count >= 2 && a[0] is string && L(a[1]) != null;
        }

        static bool IsProject(object v)
        {
            var a = L(v);
            return a != null && a.Count >= 3 && L(a[1]) != null && L(a[2]) != null && !(a[0] is string);
        }

        static string FindFile(string rootDir, string sub, string file)
        {
            foreach (var c in new[] { Path.Combine(rootDir, sub, file), Path.Combine(rootDir, file) })
                if (File.Exists(c)) return c;
            try
            {
                foreach (var f in Directory.GetFiles(rootDir, "*", SearchOption.AllDirectories))
                    if (string.Equals(Path.GetFileName(f), file, StringComparison.OrdinalIgnoreCase)) return f;
            }
            catch (Exception) { }
            return "";
        }

        static List<KeyValuePair<string, object>> contentIndex;
        static string fileDir;   // the picked file's folder (extra .pobject files go there)

        static object ContentById(string rootDir, string cid, List<object> used)
        {
            if (contentIndex == null)
            {
                contentIndex = new List<KeyValuePair<string, object>>();
                var files = new List<string>();
                try { files.AddRange(Directory.GetFiles(rootDir, "*", SearchOption.AllDirectories)); } catch (Exception) { }
                if (!string.IsNullOrEmpty(fileDir) && fileDir != rootDir)
                    try { files.AddRange(Directory.GetFiles(fileDir, "*.pobject", SearchOption.AllDirectories)); } catch (Exception) { }
                try
                {
                    foreach (var f in files)
                    {
                        string e = Path.GetExtension(f).ToLowerInvariant();
                        if (e != ".pobject" && e != "") continue;
                        if (new FileInfo(f).Length > 300L * 1024 * 1024) continue;
                        object c;
                        try { c = Decode(File.ReadAllBytes(f)); } catch (Exception) { continue; }
                        if (IsContent(c)) contentIndex.Add(new KeyValuePair<string, object>(Sid(L(c)[0]), c));
                    }
                }
                catch (Exception) { }
            }
            foreach (var kv in contentIndex) if (kv.Key == cid) return kv.Value;
            foreach (var kv in contentIndex) if (!used.Contains(kv.Value)) return kv.Value;
            return null;
        }

        static MModel Build(string rootDir, string projPath)
        {
            var proj = Decode(File.ReadAllBytes(projPath));
            object loneContent = null;
            // Single-file projects (other Prisma3D versions): no «content»
            // folder; [settings, objects, materials, meshes] all in the .proj,
            // meshes without the edge lists.
            var E = L(proj);
            if (E != null && E.Count >= 4 && L(E[1]) != null && L(E[3]) != null && L(E[3]).Count > 0 && Embedded(E))
            {
                var meshes = new List<object>();
                foreach (var md0 in L(E[3]))
                {
                    var md = L(md0);
                    if (md == null) continue;
                    if (md.Count == 9)
                        meshes.Add(new List<object> { md[0], md[1], md[2], md[3], md[4], md[5], new List<object>(), new List<object>(), md[6], md[7], md[8] });
                    else meshes.Add(md);
                }
                object rootId = null;
                string rootName = "Prisma3D";
                foreach (var o0 in L(E[1]))
                {
                    var o = L(o0);
                    if (o == null || o.Count < 4) continue;
                    if (Num(o[3], -1) < 0) { rootId = o[0]; rootName = o[1] as string ?? rootName; break; }
                }
                if (rootId == null) rootId = L(L(E[1])[0])[0];
                loneContent = new List<object> { Sid(rootId), E[1], meshes };
                proj = new List<object> { E[0], new List<object> { new List<object> { Sid(rootId), rootName, new List<object>() } }, E[2] };
            }
            if (IsContent(proj))
            {
                // A content file on its own: one root object, no materials.
                loneContent = proj;
                var pl = L(proj);
                string rootName = "Prisma3D";
                foreach (var o0 in L(pl[1]))
                {
                    var o = L(o0);
                    if (o != null && o.Count > 1 && Sid(o[0]) == Sid(pl[0])) rootName = o[1] as string ?? rootName;
                }
                proj = new List<object> { new List<object>(), new List<object> { new List<object> { Sid(pl[0]), rootName, new List<object>() } }, new List<object>() };
            }
            if (!IsProject(proj)) throw new Exception("el proyecto de Prisma3D está dañado o es de una versión que ATLAS no conoce todavía");
            var P = L(proj);
            var m = new MModel();
            var texCache = new Dictionary<string, int>();
            var materials = new Dictionary<string, int>();
            foreach (var md0 in L(P[2]))
            {
                var md = L(md0);
                if (md != null && md.Count >= 3) materials[Sid(md[0])] = Material(md, rootDir, m, texCache);
            }
            // Top node: Prisma3D characters face -Z; ATLAS models face +Z.
            m.nodes.Add(new MNode { name = "Prisma3D", r = new[] { 0f, 1f, 0f, 0f } });
            int meshesMade = 0;
            var missing = new List<string>();
            var usedContent = new List<object>();
            contentIndex = null;
            foreach (var r0 in L(P[1]))
            {
                var rootObj = L(r0);
                if (rootObj == null || rootObj.Count < 2) continue;
                string cid = Sid(rootObj[0]);
                object content = loneContent;
                if (content == null)
                {
                    string cpath = FindFile(rootDir, "content", cid + ".pobject");
                    content = cpath != "" ? Decode(File.ReadAllBytes(cpath)) : null;
                    // Not by its file name: by the id written inside each
                    // .pobject (renamed or copied files), then any left over.
                    if (!IsContent(content)) content = ContentById(rootDir, cid, usedContent);
                }
                if (!IsContent(content)) { missing.Add(rootObj[1] as string ?? cid); continue; }
                usedContent.Add(content);
                var C = L(content);
                var objs = new List<List<object>>();
                foreach (var o0 in L(C[1]))
                {
                    var o = L(o0);
                    if (o != null && o.Count >= 3 && L(o[2]) != null) objs.Add(o);
                }
                var meshData = new Dictionary<string, List<object>>();
                if (C.Count > 2 && L(C[2]) != null)
                    foreach (var md0 in L(C[2]))
                    {
                        var md = L(md0);
                        if (md != null && md.Count >= 10 && MeshOk(md)) meshData[Sid(md[0])] = md;
                    }
                // One node per object, parented like in Prisma3D.
                var nodeOf = new Dictionary<string, int>();
                var byId = new Dictionary<string, List<object>>();
                foreach (var o in objs)
                {
                    string id = Sid(o[0]);
                    if (nodeOf.ContainsKey(id)) continue;
                    byId[id] = o;
                    var n = new MNode { name = o[1] as string ?? "objeto" };
                    LocalTrs(id == cid && rootObj.Count >= 3 && L(rootObj[2]) != null && L(rootObj[2]).Count > 0 ? rootObj : o, n);
                    m.nodes.Add(n);
                    nodeOf[id] = m.nodes.Count - 1;
                }
                int contentRoot = -1;
                foreach (var o in objs)
                {
                    string id = Sid(o[0]);
                    int ni = nodeOf[id];
                    string pid = o.Count > 3 ? Sid(o[3]) : "";
                    int pn;
                    if (pid != "" && pid != id && nodeOf.TryGetValue(pid, out pn)) m.nodes[ni].parent = pn;
                    else { m.nodes[ni].parent = 0; if (contentRoot < 0) contentRoot = ni; }
                }
                // Global transforms (for rigid pieces that follow a bone).
                var glob = new Dictionary<int, float[]>();
                Func<int, float[]> G = null;
                G = ni =>
                {
                    float[] g;
                    if (glob.TryGetValue(ni, out g)) return g;
                    var n = m.nodes[ni];
                    g = Trs(n);
                    if (n.parent > 0) g = Mul(G(n.parent), g);
                    glob[ni] = g;
                    return g;
                };
                var boneIds = new HashSet<string>();
                foreach (var o in objs) if (Props(o, "PBone") != null) boneIds.Add(Sid(o[0]));
                foreach (var o in objs)
                {
                    var mr = Props(o, "PMeshRenderer");
                    if (mr == null) continue;
                    string id = Sid(o[0]);
                    int ni = nodeOf[id];
                    object raw;
                    var mats = new List<int>();
                    mr.TryGetValue("materials", out raw);
                    var ids = JObj(raw);
                    object idl;
                    if (ids.TryGetValue("ids", out idl) && idl is List<object>)
                        foreach (var mid in (List<object>)idl)
                        {
                            int mi;
                            mats.Add(materials.TryGetValue(Sid(mid), out mi) ? mi : -1);
                        }
                    mr.TryGetValue("pMesh", out raw);
                    object meshRefO;
                    JObj(raw).TryGetValue("id", out meshRefO);
                    string meshRef = Sid(meshRefO);
                    mr.TryGetValue("isSkinned", out raw);
                    bool skinned = raw is string && (string)raw == "True" && boneIds.Count > 0;
                    mr.TryGetValue("normalBreakAngle", out raw);
                    double brk = Num(raw, 30);
                    MMesh mesh = null;
                    List<object> mdat;
                    if (meshRef != "" && meshData.TryGetValue(meshRef, out mdat))
                    {
                        var skinIds = new List<string>();
                        if (skinned)
                        {
                            mr.TryGetValue("skinBones", out raw);
                            object sl;
                            if (JObj(raw).TryGetValue("ids", out sl) && sl is List<object>)
                                foreach (var s in (List<object>)sl) skinIds.Add(Sid(s));
                        }
                        mesh = BuildMesh(mdat, mats, skinned && skinIds.Count > 0, brk);
                        if (mesh != null && skinned && skinIds.Count > 0)
                        {
                            var binds = mdat.Count > 10 ? L(mdat[10]) : null;
                            mesh.skinJoints = new int[skinIds.Count];
                            mesh.skinIbm = new float[skinIds.Count * 16];
                            for (int j = 0; j < skinIds.Count; j++)
                            {
                                int bn;
                                mesh.skinJoints[j] = nodeOf.TryGetValue(skinIds[j], out bn) ? bn : ni;
                                var bm = binds != null && j < binds.Count ? L(binds[j]) : null;
                                float[] ibm;
                                if (bm != null && bm.Count >= 16)
                                {
                                    // Unity bind pose (column-major) mirrored on X: S * M * S
                                    ibm = new float[16];
                                    for (int k = 0; k < 16; k++) ibm[k] = (float)Num(bm[k], 0);
                                    int[] flip = { 1, 2, 4, 8, 12 };
                                    foreach (int k in flip) ibm[k] = -ibm[k];
                                }
                                else ibm = Mul(Inverse(G(mesh.skinJoints[j])), G(ni));
                                Array.Copy(ibm, 0, mesh.skinIbm, j * 16, 16);
                            }
                        }
                    }
                    else if (PrimitiveKind(o) != "")
                        mesh = PrimitiveShapes.Make(PrimitiveKind(o), mats.Count > 0 ? mats[0] : -1);
                    if (mesh == null) continue;
                    mesh.name = o[1] as string ?? "malla";
                    // A rigid piece inside a bone follows it as a one-bone skin,
                    // so the pose mode moves it like the rest of the rig.
                    if (mesh.joints == null && boneIds.Count > 0)
                    {
                        string p = o.Count > 3 ? Sid(o[3]) : "";
                        for (int guard = 0; p != "" && byId.ContainsKey(p) && guard < 256; guard++)
                        {
                            if (boneIds.Contains(p))
                            {
                                int bn = nodeOf[p];
                                int nv = mesh.VertexCount;
                                mesh.joints = new ushort[nv * 4];
                                mesh.weights = new float[nv * 4];
                                for (int v = 0; v < nv; v++) mesh.weights[v * 4] = 1f;
                                mesh.skinJoints = new[] { bn };
                                mesh.skinIbm = Mul(Inverse(G(bn)), G(ni));
                                break;
                            }
                            var po = byId[p];
                            p = po.Count > 3 ? Sid(po[3]) : "";
                        }
                    }
                    m.meshes.Add(mesh);
                    m.nodes[ni].mesh = m.meshes.Count - 1;
                    meshesMade++;
                }
            }
            if (meshesMade == 0)
            {
                if (missing.Count > 0) throw new MissingContentException(missing.Count) { names = string.Join(", ", missing.ToArray()) };
                throw new Exception("el proyecto de Prisma3D no tiene mallas que ATLAS pueda leer");
            }
            return m;
        }
    }

    /// Prisma3D's built-in shapes («Primitives.PrimitiveCube»…), unit size,
    /// already in glTF space (right-handed, faces pointing out).
    public static class PrimitiveShapes
    {
        public static MMesh Make(string kind, int material)
        {
            string k = kind.ToLowerInvariant();
            var pos = new List<float>();
            var nor = new List<float>();
            var uv = new List<float>();
            var idx = new List<uint>();
            Action<float, float, float, float, float, float, float, float> V = (px, py, pz, nx, ny, nz, u, v) =>
            {
                pos.Add(px); pos.Add(py); pos.Add(pz); nor.Add(nx); nor.Add(ny); nor.Add(nz); uv.Add(u); uv.Add(v);
            };
            if (k.Contains("plane") || k.Contains("quad"))
            {
                V(-0.5f, 0, -0.5f, 0, 1, 0, 0, 0); V(0.5f, 0, -0.5f, 0, 1, 0, 1, 0); V(0.5f, 0, 0.5f, 0, 1, 0, 1, 1); V(-0.5f, 0, 0.5f, 0, 1, 0, 0, 1);
                idx.AddRange(new uint[] { 0, 1, 2, 0, 2, 3 });
            }
            else if (k.Contains("sphere") || k.Contains("capsule") || k.Contains("cylinder") || k.Contains("cone") || k.Contains("torus"))
            {
                int seg = 32, rings = 16;
                bool torus = k.Contains("torus"), cone = k.Contains("cone"), cyl = k.Contains("cylinder"), caps = k.Contains("capsule");
                for (int i = 0; i <= rings; i++)
                    for (int j = 0; j <= seg; j++)
                    {
                        float a = j / (float)seg * (float)Math.PI * 2f, t = i / (float)rings;
                        float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                        float px, py, pz, nx, ny, nz;
                        if (torus)
                        {
                            float b = t * (float)Math.PI * 2f, R = 0.75f, rr = 0.25f;
                            nx = ca * (float)Math.Cos(b); ny = (float)Math.Sin(b); nz = sa * (float)Math.Cos(b);
                            px = ca * R + nx * rr; py = ny * rr; pz = sa * R + nz * rr;
                        }
                        else if (cyl || cone)
                        {
                            float r = cone ? 0.5f * (1f - t) : 0.5f;
                            px = ca * r; py = t - 0.5f; pz = sa * r;
                            nx = ca; ny = cone ? 0.5f : 0f; nz = sa;
                        }
                        else
                        {
                            float ph = t * (float)Math.PI;
                            nx = ca * (float)Math.Sin(ph); ny = -(float)Math.Cos(ph); nz = sa * (float)Math.Sin(ph);
                            px = nx * 0.5f; py = ny * 0.5f; pz = nz * 0.5f;
                            if (caps) py += ny >= 0 ? 0.5f : -0.5f;
                        }
                        V(px, py, pz, nx, ny, nz, j / (float)seg, t);
                    }
                for (int i = 0; i < rings; i++)
                    for (int j = 0; j < seg; j++)
                    {
                        uint a0 = (uint)(i * (seg + 1) + j), a1 = (uint)((i + 1) * (seg + 1) + j);
                        idx.AddRange(new[] { a0, a1, a0 + 1, a1, a1 + 1, a0 + 1 });
                    }
                if (cyl || cone)
                {
                    // Caps
                    foreach (float y in cone ? new[] { -0.5f } : new[] { -0.5f, 0.5f })
                    {
                        uint c0 = (uint)(pos.Count / 3);
                        V(0, y, 0, 0, y > 0 ? 1 : -1, 0, 0.5f, 0.5f);
                        for (int j = 0; j <= seg; j++)
                        {
                            float a = j / (float)seg * (float)Math.PI * 2f;
                            V((float)Math.Cos(a) * 0.5f, y, (float)Math.Sin(a) * 0.5f, 0, y > 0 ? 1 : -1, 0, 0.5f + (float)Math.Cos(a) * 0.5f, 0.5f + (float)Math.Sin(a) * 0.5f);
                        }
                        for (int j = 0; j < seg; j++) idx.AddRange(new[] { c0, c0 + 1 + (uint)j, c0 + 2 + (uint)j });
                    }
                }
            }
            else
            {
                // Box
                int[][] faces = { new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
                foreach (var f in faces)
                {
                    float nx = f[0], ny = f[1], nz = f[2];
                    // Two axes across the face
                    float ux = ny != 0 ? 1 : (nz != 0 ? 1 : 0), uy = 0, uz = nx != 0 ? 1 : 0;
                    float vx = uy * nz - uz * ny, vy = uz * nx - ux * nz, vz = ux * ny - uy * nx;
                    uint b = (uint)(pos.Count / 3);
                    for (int c = 0; c < 4; c++)
                    {
                        float su = (c == 1 || c == 2) ? 0.5f : -0.5f, sv = c >= 2 ? 0.5f : -0.5f;
                        V(nx * 0.5f + ux * su + vx * sv, ny * 0.5f + uy * su + vy * sv, nz * 0.5f + uz * su + vz * sv, nx, ny, nz, su + 0.5f, sv + 0.5f);
                    }
                    idx.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                }
            }
            var mesh = new MMesh { pos = pos.ToArray(), nor = nor.ToArray(), uv = uv.ToArray() };
            var ia = idx.ToArray();
            MMesh.FixWinding(mesh.pos, mesh.nor, ia);
            mesh.parts.Add(new MPart { material = material, indices = ia });
            return mesh;
        }
    }
}
