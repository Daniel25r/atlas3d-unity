// ATLAS 3D (Unity) - Autodesk 3D Studio (.3ds), ported from the Godot
// version: objects with their meshes, texture coordinates, materials
// (colour, transparency, two-sided) and texture images next to the file.
// 3DS is Z-up: turned to Y-up (glTF), triangles keep facing out.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Atlas.Import
{
    public static class ThreeDsImport
    {
        public static bool Is3ds(byte[] h)
        {
            if (h == null || h.Length < 10 || BitConverter.ToUInt16(h, 0) != 0x4D4D) return false;
            int nxt = BitConverter.ToUInt16(h, 6);
            return nxt == 0x0002 || nxt == 0x3D3D || nxt == 0xB000 || nxt == 0x0001;
        }

        class Obj
        {
            public string name = "";
            public float[] verts = new float[0];
            public int[] faces = new int[0];
            public float[] uv = new float[0];
            public readonly List<KeyValuePair<string, List<int>>> faceMat = new List<KeyValuePair<string, List<int>>>();
        }

        class Mat
        {
            public string name = "";
            public float[] color = { 0.8f, 0.8f, 0.8f };
            public float alpha = 1f;
            public bool twoSided;
            public string texture = "";
        }

        class Ctx
        {
            public readonly List<Obj> objects = new List<Obj>();
            public readonly Dictionary<string, Mat> materials = new Dictionary<string, Mat>();
        }

        public static MModel Load(string path)
        {
            var b = File.ReadAllBytes(path);
            if (!Is3ds(b)) throw new Exception("este archivo no es un modelo de 3D Studio (.3ds) válido");
            var ctx = new Ctx();
            Chunks(b, 6, (int)Math.Min(BitConverter.ToUInt32(b, 2), (uint)b.Length), ctx, null);
            if (ctx.objects.Count == 0) throw new Exception("el .3ds no contiene mallas");
            string dir = Path.GetDirectoryName(path);
            var m = new MModel();
            var matIndex = new Dictionary<string, int>();
            foreach (var kv in ctx.materials)
            {
                var mt = kv.Value;
                var mm = new MMaterial { name = mt.name, roughness = 0.8f, doubleSided = mt.twoSided };
                mm.color = new[] { Lin(mt.color[0]), Lin(mt.color[1]), Lin(mt.color[2]), mt.alpha };
                if (mt.texture != "")
                {
                    var img = FbxImport.FindImage(mt.texture, "", dir);
                    if (img != null)
                    {
                        m.textures.Add(new MTexture { data = img, mime = MModel.ImageMime(img) });
                        mm.texture = m.textures.Count - 1;
                        mm.color = new[] { 1f, 1f, 1f, mt.alpha };
                    }
                }
                m.materials.Add(mm);
                matIndex[kv.Key] = m.materials.Count - 1;
            }
            m.nodes.Add(new MNode { name = Path.GetFileNameWithoutExtension(path) });
            foreach (var o in ctx.objects)
            {
                var mesh = BuildMesh(o, matIndex);
                if (mesh == null) continue;
                m.meshes.Add(mesh);
                m.nodes.Add(new MNode { name = o.name == "" ? "Pieza" : o.name, parent = 0, mesh = m.meshes.Count - 1 });
            }
            if (m.meshes.Count == 0) throw new Exception("el .3ds no tiene caras que mostrar");
            return m;
        }

        static float Lin(float c)
        {
            return c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        static string CStr(byte[] b, int at, int end, out int next)
        {
            int i = at;
            while (i < end && b[i] != 0) i++;
            next = i + 1;
            return i > at ? Encoding.UTF8.GetString(b, at, i - at) : "";
        }

        static void Chunks(byte[] b, int from, int to, Ctx ctx, object cur)
        {
            int o = from;
            while (o + 6 <= to)
            {
                int id = BitConverter.ToUInt16(b, o);
                long ln = BitConverter.ToUInt32(b, o + 2);
                if (ln < 6 || o + ln > to) return;
                int body = o + 6, end = (int)(o + ln);
                var ob = cur as Obj;
                var mt = cur as Mat;
                int next;
                switch (id)
                {
                    case 0x3D3D:
                    case 0x4100:
                        Chunks(b, body, end, ctx, cur);
                        break;
                    case 0x4000:
                    {
                        var no = new Obj { name = CStr(b, body, end, out next) };
                        ctx.objects.Add(no);
                        Chunks(b, next, end, ctx, no);
                        break;
                    }
                    case 0x4110:
                        if (ob != null && body + 2 <= end)
                        {
                            int n = BitConverter.ToUInt16(b, body);
                            var v = new List<float>(n * 3);
                            for (int i = 0; i < n; i++)
                            {
                                int p = body + 2 + i * 12;
                                if (p + 12 > end) break;
                                // Z-up -> Y-up
                                v.Add(BitConverter.ToSingle(b, p));
                                v.Add(BitConverter.ToSingle(b, p + 8));
                                v.Add(-BitConverter.ToSingle(b, p + 4));
                            }
                            ob.verts = v.ToArray();
                        }
                        break;
                    case 0x4120:
                        if (ob != null && body + 2 <= end)
                        {
                            int n = BitConverter.ToUInt16(b, body);
                            var f = new List<int>(n * 3);
                            for (int i = 0; i < n; i++)
                            {
                                int p = body + 2 + i * 8;
                                if (p + 8 > end) break;
                                f.Add(BitConverter.ToUInt16(b, p));
                                f.Add(BitConverter.ToUInt16(b, p + 2));
                                f.Add(BitConverter.ToUInt16(b, p + 4));
                            }
                            ob.faces = f.ToArray();
                            Chunks(b, body + 2 + n * 8, end, ctx, cur);
                        }
                        break;
                    case 0x4130:
                        if (ob != null)
                        {
                            string name = CStr(b, body, end, out next);
                            if (next + 2 <= end)
                            {
                                int n = BitConverter.ToUInt16(b, next);
                                var list = new List<int>();
                                for (int i = 0; i < n; i++)
                                {
                                    if (next + 4 + i * 2 > end) break;
                                    list.Add(BitConverter.ToUInt16(b, next + 2 + i * 2));
                                }
                                ob.faceMat.Add(new KeyValuePair<string, List<int>>(name, list));
                            }
                        }
                        break;
                    case 0x4140:
                        if (ob != null && body + 2 <= end)
                        {
                            int n = BitConverter.ToUInt16(b, body);
                            var u = new List<float>(n * 2);
                            for (int i = 0; i < n; i++)
                            {
                                int p = body + 2 + i * 8;
                                if (p + 8 > end) break;
                                u.Add(BitConverter.ToSingle(b, p));
                                u.Add(1f - BitConverter.ToSingle(b, p + 4));
                            }
                            ob.uv = u.ToArray();
                        }
                        break;
                    case 0xAFFF:
                    {
                        var nm = new Mat();
                        Chunks(b, body, end, ctx, nm);
                        ctx.materials[nm.name] = nm;
                        break;
                    }
                    case 0xA000:
                        if (mt != null) mt.name = CStr(b, body, end, out next);
                        break;
                    case 0xA020:
                        if (mt != null) mt.color = Color(b, body, end, mt.color);
                        break;
                    case 0xA050:
                        if (mt != null && body + 6 <= end)
                        {
                            int pid = BitConverter.ToUInt16(b, body);
                            float t = 0f;
                            if (pid == 0x0030 && body + 8 <= end) t = BitConverter.ToUInt16(b, body + 6) / 100f;
                            else if (pid == 0x0031 && body + 10 <= end) t = BitConverter.ToSingle(b, body + 6);
                            mt.alpha = Math.Max(0f, Math.Min(1f, 1f - t));
                        }
                        break;
                    case 0xA081:
                        if (mt != null) mt.twoSided = true;
                        break;
                    case 0xA200:
                        Chunks(b, body, end, ctx, cur);
                        break;
                    case 0xA300:
                        if (mt != null) mt.texture = CStr(b, body, end, out next);
                        break;
                }
                o = end;
            }
        }

        static float[] Color(byte[] b, int from, int to, float[] fallback)
        {
            if (from + 6 > to) return fallback;
            int id = BitConverter.ToUInt16(b, from);
            int p = from + 6;
            if ((id == 0x0011 || id == 0x0012) && p + 3 <= to) return new[] { b[p] / 255f, b[p + 1] / 255f, b[p + 2] / 255f };
            if ((id == 0x0010 || id == 0x0013) && p + 12 <= to)
                return new[] { BitConverter.ToSingle(b, p), BitConverter.ToSingle(b, p + 4), BitConverter.ToSingle(b, p + 8) };
            return fallback;
        }

        static MMesh BuildMesh(Obj o, Dictionary<string, int> matIndex)
        {
            int nv = o.verts.Length / 3;
            int nf = o.faces.Length / 3;
            if (nv == 0 || nf == 0) return null;
            var mesh = new MMesh { name = o.name, pos = o.verts };
            if (o.uv.Length == nv * 2) mesh.uv = o.uv;
            // Smooth normals over the shared corners.
            var nor = new float[nv * 3];
            for (int f = 0; f < nf; f++)
            {
                int a = o.faces[f * 3], b = o.faces[f * 3 + 1], c = o.faces[f * 3 + 2];
                if (a >= nv || b >= nv || c >= nv) continue;
                float ux = o.verts[b * 3] - o.verts[a * 3], uy = o.verts[b * 3 + 1] - o.verts[a * 3 + 1], uz = o.verts[b * 3 + 2] - o.verts[a * 3 + 2];
                float vx = o.verts[c * 3] - o.verts[a * 3], vy = o.verts[c * 3 + 1] - o.verts[a * 3 + 1], vz = o.verts[c * 3 + 2] - o.verts[a * 3 + 2];
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                foreach (int k in new[] { a, b, c }) { nor[k * 3] += nx; nor[k * 3 + 1] += ny; nor[k * 3 + 2] += nz; }
            }
            for (int i = 0; i < nv; i++)
            {
                float l = (float)Math.Sqrt(nor[i * 3] * nor[i * 3] + nor[i * 3 + 1] * nor[i * 3 + 1] + nor[i * 3 + 2] * nor[i * 3 + 2]);
                if (l > 1e-12f) { nor[i * 3] /= l; nor[i * 3 + 1] /= l; nor[i * 3 + 2] /= l; }
                else nor[i * 3 + 1] = 1f;
            }
            mesh.nor = nor;
            // Faces per material; the rest in a default group.
            var assigned = new bool[nf];
            var groups = new List<KeyValuePair<int, List<int>>>();
            foreach (var kv in o.faceMat)
            {
                int mi;
                if (!matIndex.TryGetValue(kv.Key, out mi)) mi = -1;
                foreach (int fi in kv.Value) if (fi < nf) assigned[fi] = true;
                groups.Add(new KeyValuePair<int, List<int>>(mi, kv.Value));
            }
            var rest = new List<int>();
            for (int fi = 0; fi < nf; fi++) if (!assigned[fi]) rest.Add(fi);
            if (rest.Count > 0) groups.Add(new KeyValuePair<int, List<int>>(-1, rest));
            foreach (var g in groups)
            {
                var idx = new List<uint>();
                foreach (int fi in g.Value)
                {
                    if (fi >= nf) continue;
                    int a = o.faces[fi * 3], b = o.faces[fi * 3 + 1], c = o.faces[fi * 3 + 2];
                    if (a >= nv || b >= nv || c >= nv) continue;
                    idx.Add((uint)a); idx.Add((uint)b); idx.Add((uint)c);
                }
                if (idx.Count > 0) mesh.parts.Add(new MPart { material = g.Key, indices = idx.ToArray() });
            }
            return mesh.parts.Count > 0 ? mesh : null;
        }
    }
}
