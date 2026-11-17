// ATLAS 3D (Unity) - Minecraft-style models: Blockbench projects (.bbmodel,
// textures inside) and Minecraft Bedrock geometry (.geo.json + its PNG).
// Cubes and meshes become one skinned model: every group / bone of the
// model is a bone of ATLAS (pose mode moves arms, legs, head), textures
// keep their sharp pixels. 16 units = 1 block = 1 metre / 16.
using System;
using System.Collections.Generic;
using System.IO;

namespace Atlas.Import
{
    public static class BlockbenchImport
    {
        const float U = 1f / 16f;

        class Group
        {
            public string name, uuid;
            public double[] origin = new double[3], rotation = new double[3];
            public Group parent;
            public int node;
            public float[] world;
        }

        class Face
        {
            public string dir;
            public double[] uv;        // u1 v1 u2 v2 (pixels)
            public int texture = -1;
            public int rot;
        }

        class Element
        {
            public string uuid;
            public double[] from = new double[3], to = new double[3], origin = new double[3], rotation = new double[3];
            public double inflate;
            public readonly List<Face> faces = new List<Face>();
            public Group group;
            // Mesh elements
            public Dictionary<string, double[]> verts;
            public List<KeyValuePair<List<string>, KeyValuePair<Dictionary<string, double[]>, int>>> mfaces;
        }

        // ------------------------------------------------------------------

        public static bool LooksMinecraft(string path)
        {
            string n = path.ToLowerInvariant();
            if (n.EndsWith(".bbmodel")) return true;
            if (!n.EndsWith(".json")) return false;
            try
            {
                var head = File.ReadAllText(path);
                return head.Contains("minecraft:geometry") || head.Contains("\"geometry.") || head.Contains("\"bones\"");
            }
            catch (Exception) { return false; }
        }

        public static MModel Load(string path)
        {
            var root = MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
            if (root == null) throw new Exception("el archivo no es un modelo de Blockbench / Minecraft válido");
            var groups = new List<Group>();
            var elements = new List<Element>();
            var texData = new List<byte[]>();
            double uvW = 16, uvH = 16;
            var texUv = new List<double[]>();
            if (root.ContainsKey("elements")) ReadBbmodel(root, groups, elements, texData, texUv, ref uvW, ref uvH);
            else ReadBedrock(root, path, groups, elements, texData, ref uvW, ref uvH);
            if (elements.Count == 0) throw new Exception("el modelo de Minecraft no tiene cubos");
            return Build(Path.GetFileNameWithoutExtension(path).Replace(".geo", ""), groups, elements, texData, texUv, uvW, uvH);
        }

        // ------------------------------------------------------------------
        // Blockbench project

        static double D(object o, double d = 0) { return o is double ? (double)o : o is bool ? ((bool)o ? 1 : 0) : d; }

        static double[] V3(object o, double d = 0)
        {
            var l = o as List<object>;
            var r = new[] { d, d, d };
            if (l != null) for (int i = 0; i < 3 && i < l.Count; i++) r[i] = D(l[i], d);
            return r;
        }

        static double[] V4(object o)
        {
            var l = o as List<object>;
            if (l == null || l.Count < 4) return null;
            return new[] { D(l[0]), D(l[1]), D(l[2]), D(l[3]) };
        }

        static string S(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) ? o as string ?? "" : ""; }

        static void ReadBbmodel(Dictionary<string, object> root, List<Group> groups, List<Element> elements, List<byte[]> texData, List<double[]> texUv, ref double uvW, ref double uvH)
        {
            object o;
            if (root.TryGetValue("resolution", out o) && o is Dictionary<string, object>)
            {
                var r = (Dictionary<string, object>)o;
                uvW = D(r.ContainsKey("width") ? r["width"] : null, 16);
                uvH = D(r.ContainsKey("height") ? r["height"] : null, 16);
            }
            var byUuid = new Dictionary<string, Element>();
            foreach (var e0 in root["elements"] as List<object> ?? new List<object>())
            {
                var e = e0 as Dictionary<string, object>;
                if (e == null) continue;
                if (e.ContainsKey("visibility") && e["visibility"] is bool && !(bool)e["visibility"]) continue;
                string type = S(e, "type");
                var el = new Element { uuid = S(e, "uuid") };
                el.origin = V3(e.ContainsKey("origin") ? e["origin"] : null);
                el.rotation = V3(e.ContainsKey("rotation") ? e["rotation"] : null);
                if (type == "mesh")
                {
                    el.verts = new Dictionary<string, double[]>();
                    if (e.TryGetValue("vertices", out o) && o is Dictionary<string, object>)
                        foreach (var kv in (Dictionary<string, object>)o) el.verts[kv.Key] = V3(kv.Value);
                    el.mfaces = new List<KeyValuePair<List<string>, KeyValuePair<Dictionary<string, double[]>, int>>>();
                    if (e.TryGetValue("faces", out o) && o is Dictionary<string, object>)
                        foreach (var kv in (Dictionary<string, object>)o)
                        {
                            var f = kv.Value as Dictionary<string, object>;
                            if (f == null) continue;
                            var vs = new List<string>();
                            if (f.TryGetValue("vertices", out o) && o is List<object>) foreach (var v in (List<object>)o) vs.Add(v as string);
                            var uv = new Dictionary<string, double[]>();
                            if (f.TryGetValue("uv", out o) && o is Dictionary<string, object>)
                                foreach (var u in (Dictionary<string, object>)o) { var l = u.Value as List<object>; if (l != null && l.Count >= 2) uv[u.Key] = new[] { D(l[0]), D(l[1]) }; }
                            int tex = f.ContainsKey("texture") && f["texture"] is double ? (int)(double)f["texture"] : -1;
                            el.mfaces.Add(new KeyValuePair<List<string>, KeyValuePair<Dictionary<string, double[]>, int>>(vs, new KeyValuePair<Dictionary<string, double[]>, int>(uv, tex)));
                        }
                }
                else if (type == "" || type == "cube")
                {
                    el.from = V3(e.ContainsKey("from") ? e["from"] : null);
                    el.to = V3(e.ContainsKey("to") ? e["to"] : null);
                    el.inflate = D(e.ContainsKey("inflate") ? e["inflate"] : null);
                    bool boxUv = e.ContainsKey("box_uv") && e["box_uv"] is bool && (bool)e["box_uv"];
                    if (e.TryGetValue("faces", out o) && o is Dictionary<string, object>)
                        foreach (var kv in (Dictionary<string, object>)o)
                        {
                            var f = kv.Value as Dictionary<string, object>;
                            if (f == null) continue;
                            if (!f.ContainsKey("texture") || f["texture"] == null || f["texture"] is bool) continue;   // no texture: the face is not drawn
                            el.faces.Add(new Face
                            {
                                dir = kv.Key,
                                uv = V4(f.ContainsKey("uv") ? f["uv"] : null),
                                texture = f["texture"] is double ? (int)(double)f["texture"] : -1,
                                rot = f.ContainsKey("rotation") ? (int)D(f["rotation"]) : 0,
                            });
                        }
                    if (boxUv && e.TryGetValue("uv_offset", out o))
                    {
                        var off = o as List<object>;
                        if (off != null && off.Count >= 2) BoxUv(el, D(off[0]), D(off[1]), false);
                    }
                }
                else continue;   // locators, null objects…
                elements.Add(el);
                if (el.uuid != "") byUuid[el.uuid] = el;
            }
            // Groups (bones) from the outliner.
            if (root.TryGetValue("outliner", out o) && o is List<object>)
                foreach (var n in (List<object>)o) Outline(n, null, groups, byUuid);
            // Textures: inside the project as data: URLs.
            if (root.TryGetValue("textures", out o) && o is List<object>)
                foreach (var t0 in (List<object>)o)
                {
                    var t = t0 as Dictionary<string, object>;
                    byte[] png = null;
                    string src = t != null ? S(t, "source") : "";
                    int comma = src.IndexOf("base64,");
                    if (comma >= 0) { try { png = Convert.FromBase64String(src.Substring(comma + 7)); } catch (Exception) { png = null; } }
                    texData.Add(png);
                    double tw = t != null && t.ContainsKey("uv_width") ? D(t["uv_width"], uvW) : uvW;
                    double th = t != null && t.ContainsKey("uv_height") ? D(t["uv_height"], uvH) : uvH;
                    texUv.Add(new[] { tw, th });
                }
        }

        static void Outline(object n, Group parent, List<Group> groups, Dictionary<string, Element> byUuid)
        {
            var s = n as string;
            if (s != null)
            {
                Element e;
                if (byUuid.TryGetValue(s, out e)) e.group = parent;
                return;
            }
            var d = n as Dictionary<string, object>;
            if (d == null) return;
            var g = new Group { name = S(d, "name"), uuid = S(d, "uuid"), parent = parent };
            g.origin = V3(d.ContainsKey("origin") ? d["origin"] : null);
            g.rotation = V3(d.ContainsKey("rotation") ? d["rotation"] : null);
            if (string.IsNullOrEmpty(g.name)) g.name = "hueso";
            groups.Add(g);
            object c;
            if (d.TryGetValue("children", out c) && c is List<object>)
                foreach (var ch in (List<object>)c) Outline(ch, g, groups, byUuid);
        }

        /// Minecraft's box UV: the cube's faces unfolded from one corner.
        static void BoxUv(Element el, double u, double v, bool mirror)
        {
            double w = Math.Abs(el.to[0] - el.from[0]), h = Math.Abs(el.to[1] - el.from[1]), d = Math.Abs(el.to[2] - el.from[2]);
            w = Math.Floor(w + 1e-4); h = Math.Floor(h + 1e-4); d = Math.Floor(d + 1e-4);
            var map = new Dictionary<string, double[]>
            {
                { "east", new[] { u, v + d, u + d, v + d + h } },
                { "north", new[] { u + d, v + d, u + d + w, v + d + h } },
                { "west", new[] { u + d + w, v + d, u + 2 * d + w, v + d + h } },
                { "south", new[] { u + 2 * d + w, v + d, u + 2 * d + 2 * w, v + d + h } },
                { "up", new[] { u + d + w, v + d, u + d, v } },
                { "down", new[] { u + d + 2 * w, v, u + d + w, v + d } },
            };
            if (mirror)
            {
                foreach (var k in new List<string>(map.Keys)) { var a = map[k]; map[k] = new[] { a[2], a[1], a[0], a[3] }; }
                var e = map["east"]; map["east"] = map["west"]; map["west"] = e;
            }
            var have = new HashSet<string>();
            foreach (var f in el.faces) { have.Add(f.dir); if (f.uv == null) f.uv = map[f.dir]; }
            foreach (var kv in map) if (!have.Contains(kv.Key)) el.faces.Add(new Face { dir = kv.Key, uv = kv.Value, texture = 0 });
        }

        // ------------------------------------------------------------------
        // Bedrock geometry (.geo.json)

        static void ReadBedrock(Dictionary<string, object> root, string path, List<Group> groups, List<Element> elements, List<byte[]> texData, ref double uvW, ref double uvH)
        {
            Dictionary<string, object> geo = null;
            object o;
            if (root.TryGetValue("minecraft:geometry", out o) && o is List<object> && ((List<object>)o).Count > 0)
                geo = ((List<object>)o)[0] as Dictionary<string, object>;
            else
                foreach (var kv in root) if (kv.Key.StartsWith("geometry.") && kv.Value is Dictionary<string, object>) { geo = (Dictionary<string, object>)kv.Value; break; }
            if (geo == null) throw new Exception("el .json no es una geometría de Minecraft");
            Dictionary<string, object> desc = geo.ContainsKey("description") ? geo["description"] as Dictionary<string, object> : geo;
            if (desc != null)
            {
                if (desc.ContainsKey("texture_width")) uvW = D(desc["texture_width"], 64);
                else if (desc.ContainsKey("texturewidth")) uvW = D(desc["texturewidth"], 64);
                if (desc.ContainsKey("texture_height")) uvH = D(desc["texture_height"], 64);
                else if (desc.ContainsKey("textureheight")) uvH = D(desc["textureheight"], 64);
            }
            var byName = new Dictionary<string, Group>();
            var bones = geo.ContainsKey("bones") ? geo["bones"] as List<object> : null;
            if (bones == null) throw new Exception("la geometría de Minecraft no tiene huesos");
            var parentOf = new Dictionary<Group, string>();
            foreach (var b0 in bones)
            {
                var b = b0 as Dictionary<string, object>;
                if (b == null) continue;
                var g = new Group { name = S(b, "name") };
                var piv = V3(b.ContainsKey("pivot") ? b["pivot"] : null);
                var rot = V3(b.ContainsKey("rotation") ? b["rotation"] : null);
                // Bedrock X points the other way.
                g.origin = new[] { -piv[0], piv[1], piv[2] };
                g.rotation = new[] { -rot[0], -rot[1], rot[2] };
                groups.Add(g);
                byName[g.name] = g;
                parentOf[g] = S(b, "parent");
                bool mirror = b.ContainsKey("mirror") && b["mirror"] is bool && (bool)b["mirror"];
                if (b.TryGetValue("cubes", out o) && o is List<object>)
                    foreach (var c0 in (List<object>)o)
                    {
                        var c = c0 as Dictionary<string, object>;
                        if (c == null) continue;
                        var org = V3(c.ContainsKey("origin") ? c["origin"] : null);
                        var size = V3(c.ContainsKey("size") ? c["size"] : null);
                        var el = new Element { group = g };
                        el.from = new[] { -(org[0] + size[0]), org[1], org[2] };
                        el.to = new[] { -org[0], org[1] + size[1], org[2] + size[2] };
                        el.inflate = D(c.ContainsKey("inflate") ? c["inflate"] : null);
                        var cp = c.ContainsKey("pivot") ? V3(c["pivot"]) : null;
                        el.origin = cp != null ? new[] { -cp[0], cp[1], cp[2] } : new[] { 0.0, 0.0, 0.0 };
                        var cr = V3(c.ContainsKey("rotation") ? c["rotation"] : null);
                        el.rotation = new[] { -cr[0], -cr[1], cr[2] };
                        bool cm = c.ContainsKey("mirror") ? (c["mirror"] is bool && (bool)c["mirror"]) : mirror;
                        object uvo;
                        c.TryGetValue("uv", out uvo);
                        if (uvo is List<object> && ((List<object>)uvo).Count >= 2)
                            BoxUv(el, D(((List<object>)uvo)[0]), D(((List<object>)uvo)[1]), cm);
                        else if (uvo is Dictionary<string, object>)
                        {
                            foreach (var fk in (Dictionary<string, object>)uvo)
                            {
                                var f = fk.Value as Dictionary<string, object>;
                                if (f == null) continue;
                                var uv = f.ContainsKey("uv") ? f["uv"] as List<object> : null;
                                var us = f.ContainsKey("uv_size") ? f["uv_size"] as List<object> : null;
                                if (uv == null || us == null || uv.Count < 2 || us.Count < 2) continue;
                                double u0 = D(uv[0]), v0 = D(uv[1]);
                                string dir = fk.Key == "east" ? "west" : fk.Key == "west" ? "east" : fk.Key;
                                el.faces.Add(new Face { dir = dir, uv = new[] { u0, v0, u0 + D(us[0]), v0 + D(us[1]) }, texture = 0 });
                            }
                        }
                        elements.Add(el);
                    }
            }
            foreach (var kv in parentOf)
            {
                Group p;
                if (kv.Value != "" && byName.TryGetValue(kv.Value, out p) && p != kv.Key) kv.Key.parent = p;
            }
            // Parents before children.
            groups.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
            // The texture: a PNG next to the model (same name first).
            string dir0 = Path.GetDirectoryName(path);
            string bn = Path.GetFileNameWithoutExtension(path).Replace(".geo", "");
            string tex = null;
            try
            {
                foreach (var f in Directory.GetFiles(dir0, "*.png", SearchOption.AllDirectories))
                    if (string.Equals(Path.GetFileNameWithoutExtension(f), bn, StringComparison.OrdinalIgnoreCase)) { tex = f; break; }
                if (tex == null) { var all = Directory.GetFiles(dir0, "*.png", SearchOption.AllDirectories); if (all.Length > 0) tex = all[0]; }
            }
            catch (Exception) { }
            texData.Add(tex != null ? File.ReadAllBytes(tex) : null);
        }

        static int Depth(Group g) { int d = 0; for (var p = g.parent; p != null && d < 64; p = p.parent) d++; return d; }

        // ------------------------------------------------------------------
        // Building the model

        static float[] Rot(double[] deg)
        {
            // Blockbench order «ZYX»: R = Rz · Ry · Rx.
            double x = deg[0] * Math.PI / 180, y = deg[1] * Math.PI / 180, z = deg[2] * Math.PI / 180;
            double cx = Math.Cos(x), sx = Math.Sin(x), cy = Math.Cos(y), sy = Math.Sin(y), cz = Math.Cos(z), sz = Math.Sin(z);
            // Row-major 3×3 of Rz*Ry*Rx
            return new float[]
            {
                (float)(cz * cy), (float)(cz * sy * sx - sz * cx), (float)(cz * sy * cx + sz * sx),
                (float)(sz * cy), (float)(sz * sy * sx + cz * cx), (float)(sz * sy * cx - cz * sx),
                (float)(-sy), (float)(cy * sx), (float)(cy * cx),
            };
        }

        static double[] Apply(float[] r, double[] v)
        {
            return new[] { r[0] * v[0] + r[1] * v[1] + r[2] * v[2], r[3] * v[0] + r[4] * v[1] + r[5] * v[2], r[6] * v[0] + r[7] * v[1] + r[8] * v[2] };
        }

        static float[] Quat(float[] m)
        {
            // Row-major rotation → quaternion (x, y, z, w)
            float tr = m[0] + m[4] + m[8];
            float qw, qx, qy, qz;
            if (tr > 0) { float s = (float)Math.Sqrt(tr + 1) * 2; qw = 0.25f * s; qx = (m[7] - m[5]) / s; qy = (m[2] - m[6]) / s; qz = (m[3] - m[1]) / s; }
            else if (m[0] > m[4] && m[0] > m[8]) { float s = (float)Math.Sqrt(1 + m[0] - m[4] - m[8]) * 2; qw = (m[7] - m[5]) / s; qx = 0.25f * s; qy = (m[1] + m[3]) / s; qz = (m[2] + m[6]) / s; }
            else if (m[4] > m[8]) { float s = (float)Math.Sqrt(1 + m[4] - m[0] - m[8]) * 2; qw = (m[2] - m[6]) / s; qx = (m[1] + m[3]) / s; qy = 0.25f * s; qz = (m[5] + m[7]) / s; }
            else { float s = (float)Math.Sqrt(1 + m[8] - m[0] - m[4]) * 2; qw = (m[3] - m[1]) / s; qx = (m[2] + m[6]) / s; qy = (m[5] + m[7]) / s; qz = 0.25f * s; }
            return new[] { qx, qy, qz, qw };
        }

        // Column-major 4×4
        static float[] Mat(float[] r3, double[] t)
        {
            return new float[]
            {
                r3[0], r3[3], r3[6], 0,
                r3[1], r3[4], r3[7], 0,
                r3[2], r3[5], r3[8], 0,
                (float)t[0], (float)t[1], (float)t[2], 1,
            };
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

        static double[] Xf(float[] m, double[] p)
        {
            return new[] { m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12], m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13], m[2] * p[0] + m[6] * p[1] + m[10] * p[2] + m[14] };
        }

        static float[] InverseRigid(float[] m)
        {
            var r = new float[16];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++) r[j * 4 + i] = m[i * 4 + j];
            for (int i = 0; i < 3; i++) r[12 + i] = -(r[i] * m[12] + r[4 + i] * m[13] + r[8 + i] * m[14]);
            r[15] = 1f;
            return r;
        }

        static MModel Build(string name, List<Group> groups, List<Element> elements, List<byte[]> texData, List<double[]> texUv, double uvW, double uvH)
        {
            var m = new MModel();
            m.nodes.Add(new MNode { name = name });
            // A root bone holds the cubes outside any group.
            var rootG = new Group { name = "raiz" };
            bool needRoot = false;
            foreach (var e in elements) if (e.group == null) { e.group = rootG; needRoot = true; }
            if (needRoot) groups.Insert(0, rootG);
            foreach (var g in groups)
            {
                double[] po = g.parent != null ? g.parent.origin : new double[3];
                var r3 = Rot(g.rotation);
                var t = new[] { (g.origin[0] - po[0]) * U, (g.origin[1] - po[1]) * U, (g.origin[2] - po[2]) * U };
                var local = Mat(r3, t);
                g.world = g.parent != null && g.parent.world != null ? Mul(g.parent.world, local) : local;
                var n = new MNode { name = g.name, parent = g.parent != null ? g.parent.node : 0 };
                n.t = new[] { (float)t[0], (float)t[1], (float)t[2] };
                n.r = Quat(r3);
                m.nodes.Add(n);
                g.node = m.nodes.Count - 1;
            }
            // Materials: one per texture, sharp pixels.
            var matOf = new Dictionary<int, int>();
            Func<int, int> Mat0 = ti =>
            {
                int k = ti >= 0 && ti < texData.Count ? ti : (texData.Count > 0 ? 0 : -1);
                int mi;
                if (matOf.TryGetValue(k, out mi)) return mi;
                var mat = new MMaterial { name = "minecraft_" + (k < 0 ? 0 : k), color = new[] { 1f, 1f, 1f, 1f }, roughness = 0.9f, alphaMask = true, doubleSided = true };
                if (k >= 0 && texData[k] != null)
                {
                    m.textures.Add(new MTexture { data = texData[k], mime = MModel.ImageMime(texData[k]) ?? "image/png", nearest = true });
                    mat.texture = m.textures.Count - 1;
                }
                else mat.alphaMask = false;
                m.materials.Add(mat);
                matOf[k] = m.materials.Count - 1;
                return matOf[k];
            };
            var pos = new List<float>();
            var nor = new List<float>();
            var uvs = new List<float>();
            var joints = new List<ushort>();
            var weights = new List<float>();
            var parts = new Dictionary<int, List<uint>>();
            int boneBase = 0;
            var jointIndex = new Dictionary<Group, int>();
            for (int i = 0; i < groups.Count; i++) jointIndex[groups[i]] = i;
            foreach (var e in elements)
            {
                var g = e.group;
                var er = Rot(e.rotation);
                Func<double[], double[]> place = p =>
                {
                    // Element rotation around its origin, then the bone.
                    var q = Apply(er, new[] { p[0] - e.origin[0], p[1] - e.origin[1], p[2] - e.origin[2] });
                    q = new[] { q[0] + e.origin[0] - g.origin[0], q[1] + e.origin[1] - g.origin[1], q[2] + e.origin[2] - g.origin[2] };
                    return Xf(g.world, new[] { q[0] * U, q[1] * U, q[2] * U });
                };
                Func<double[], double[]> turn = nv =>
                {
                    var q = Apply(er, nv);
                    var w = g.world;
                    var r = new[] { w[0] * q[0] + w[4] * q[1] + w[8] * q[2], w[1] * q[0] + w[5] * q[1] + w[9] * q[2], w[2] * q[0] + w[6] * q[1] + w[10] * q[2] };
                    double l = Math.Sqrt(r[0] * r[0] + r[1] * r[1] + r[2] * r[2]);
                    return l > 1e-9 ? new[] { r[0] / l, r[1] / l, r[2] / l } : new[] { 0.0, 1.0, 0.0 };
                };
                ushort joint = (ushort)(boneBase + jointIndex[g]);
                if (e.verts != null)
                {
                    foreach (var f in e.mfaces)
                    {
                        var ids = f.Key;
                        if (ids.Count < 3) continue;
                        var pts = new List<double[]>();
                        foreach (var id in ids) { double[] v; pts.Add(e.verts.TryGetValue(id ?? "", out v) ? v : new double[3]); }
                        // Quads come in any order: sort around the centre.
                        if (ids.Count == 4) SortQuad(ids, pts);
                        var nrm = Normal(pts);
                        var tw = f.Value.Value >= 0 && f.Value.Value < texUv.Count ? texUv[f.Value.Value] : new[] { uvW, uvH };
                        int mat = Mat0(f.Value.Value);
                        List<uint> idx;
                        if (!parts.TryGetValue(mat, out idx)) { idx = new List<uint>(); parts[mat] = idx; }
                        uint baseV = (uint)(pos.Count / 3);
                        var wn = turn(nrm);
                        for (int k = 0; k < ids.Count; k++)
                        {
                            var p = place(pts[k]);
                            pos.Add((float)p[0]); pos.Add((float)p[1]); pos.Add((float)p[2]);
                            nor.Add((float)wn[0]); nor.Add((float)wn[1]); nor.Add((float)wn[2]);
                            double[] uv;
                            if (!f.Value.Key.TryGetValue(ids[k] ?? "", out uv)) uv = new double[2];
                            uvs.Add((float)(uv[0] / tw[0])); uvs.Add((float)(uv[1] / tw[1]));
                            joints.Add(joint); joints.Add(0); joints.Add(0); joints.Add(0);
                            weights.Add(1f); weights.Add(0f); weights.Add(0f); weights.Add(0f);
                        }
                        for (int k = 1; k + 1 < ids.Count; k++) { idx.Add(baseV); idx.Add(baseV + (uint)k); idx.Add(baseV + (uint)k + 1); }
                    }
                    continue;
                }
                double inf = e.inflate;
                double x0 = Math.Min(e.from[0], e.to[0]) - inf, y0 = Math.Min(e.from[1], e.to[1]) - inf, z0 = Math.Min(e.from[2], e.to[2]) - inf;
                double x1 = Math.Max(e.from[0], e.to[0]) + inf, y1 = Math.Max(e.from[1], e.to[1]) + inf, z1 = Math.Max(e.from[2], e.to[2]) + inf;
                foreach (var f in e.faces)
                {
                    double[][] c;   // top-left, top-right, bottom-right, bottom-left seen from outside
                    double[] n;
                    switch (f.dir)
                    {
                        case "north": c = new[] { new[] { x1, y1, z0 }, new[] { x0, y1, z0 }, new[] { x0, y0, z0 }, new[] { x1, y0, z0 } }; n = new[] { 0.0, 0, -1 }; break;
                        case "south": c = new[] { new[] { x0, y1, z1 }, new[] { x1, y1, z1 }, new[] { x1, y0, z1 }, new[] { x0, y0, z1 } }; n = new[] { 0.0, 0, 1 }; break;
                        case "east": c = new[] { new[] { x1, y1, z1 }, new[] { x1, y1, z0 }, new[] { x1, y0, z0 }, new[] { x1, y0, z1 } }; n = new[] { 1.0, 0, 0 }; break;
                        case "west": c = new[] { new[] { x0, y1, z0 }, new[] { x0, y1, z1 }, new[] { x0, y0, z1 }, new[] { x0, y0, z0 } }; n = new[] { -1.0, 0, 0 }; break;
                        case "up": c = new[] { new[] { x0, y1, z0 }, new[] { x1, y1, z0 }, new[] { x1, y1, z1 }, new[] { x0, y1, z1 } }; n = new[] { 0.0, 1, 0 }; break;
                        case "down": c = new[] { new[] { x0, y0, z1 }, new[] { x1, y0, z1 }, new[] { x1, y0, z0 }, new[] { x0, y0, z0 } }; n = new[] { 0.0, -1, 0 }; break;
                        default: continue;
                    }
                    if (f.uv == null) continue;
                    var tw = f.texture >= 0 && f.texture < texUv.Count ? texUv[f.texture] : new[] { uvW, uvH };
                    double u1 = f.uv[0] / tw[0], v1 = f.uv[1] / tw[1], u2 = f.uv[2] / tw[0], v2 = f.uv[3] / tw[1];
                    var cuv = new[] { new[] { u1, v1 }, new[] { u2, v1 }, new[] { u2, v2 }, new[] { u1, v2 } };
                    int steps = ((f.rot / 90) % 4 + 4) % 4;
                    int mat = Mat0(f.texture);
                    List<uint> idx;
                    if (!parts.TryGetValue(mat, out idx)) { idx = new List<uint>(); parts[mat] = idx; }
                    uint baseV = (uint)(pos.Count / 3);
                    var wn = turn(n);
                    for (int k = 0; k < 4; k++)
                    {
                        var p = place(c[k]);
                        pos.Add((float)p[0]); pos.Add((float)p[1]); pos.Add((float)p[2]);
                        nor.Add((float)wn[0]); nor.Add((float)wn[1]); nor.Add((float)wn[2]);
                        var t = cuv[(k - steps + 4) % 4];
                        uvs.Add((float)t[0]); uvs.Add((float)t[1]);
                        joints.Add(joint); joints.Add(0); joints.Add(0); joints.Add(0);
                        weights.Add(1f); weights.Add(0f); weights.Add(0f); weights.Add(0f);
                    }
                    idx.Add(baseV); idx.Add(baseV + 1); idx.Add(baseV + 2);
                    idx.Add(baseV); idx.Add(baseV + 2); idx.Add(baseV + 3);
                }
            }
            if (pos.Count == 0) throw new Exception("el modelo de Minecraft no tiene caras con textura");
            var mesh = new MMesh { name = name, pos = pos.ToArray(), nor = nor.ToArray(), uv = uvs.ToArray() };
            foreach (var kv in parts)
            {
                var arr = kv.Value.ToArray();
                MMesh.FixWinding(mesh.pos, mesh.nor, arr);
                mesh.parts.Add(new MPart { material = kv.Key, indices = arr });
            }
            mesh.joints = joints.ToArray();
            mesh.weights = weights.ToArray();
            mesh.skinJoints = new int[groups.Count];
            mesh.skinIbm = new float[groups.Count * 16];
            for (int i = 0; i < groups.Count; i++)
            {
                mesh.skinJoints[i] = groups[i].node;
                Array.Copy(InverseRigid(groups[i].world), 0, mesh.skinIbm, i * 16, 16);
            }
            m.meshes.Add(mesh);
            m.nodes.Add(new MNode { name = name + "_malla", parent = 0, mesh = 0 });
            return m;
        }

        static double[] Normal(List<double[]> p)
        {
            double nx = 0, ny = 0, nz = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var a = p[i]; var b = p[(i + 1) % p.Count];
                nx += (a[1] - b[1]) * (a[2] + b[2]);
                ny += (a[2] - b[2]) * (a[0] + b[0]);
                nz += (a[0] - b[0]) * (a[1] + b[1]);
            }
            double l = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            return l > 1e-12 ? new[] { nx / l, ny / l, nz / l } : new[] { 0.0, 1.0, 0.0 };
        }

        static void SortQuad(List<string> ids, List<double[]> pts)
        {
            var c = new double[3];
            foreach (var p in pts) for (int k = 0; k < 3; k++) c[k] += p[k] / 4;
            // Plane axes from the first two edges.
            var a = new[] { pts[1][0] - pts[0][0], pts[1][1] - pts[0][1], pts[1][2] - pts[0][2] };
            var b = new[] { pts[2][0] - pts[0][0], pts[2][1] - pts[0][1], pts[2][2] - pts[0][2] };
            var n = new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
            var y = new[] { n[1] * a[2] - n[2] * a[1], n[2] * a[0] - n[0] * a[2], n[0] * a[1] - n[1] * a[0] };
            var order = new List<int> { 0, 1, 2, 3 };
            Func<int, double> ang = i =>
            {
                var d = new[] { pts[i][0] - c[0], pts[i][1] - c[1], pts[i][2] - c[2] };
                return Math.Atan2(d[0] * y[0] + d[1] * y[1] + d[2] * y[2], d[0] * a[0] + d[1] * a[1] + d[2] * a[2]);
            };
            order.Sort((i, j) => ang(i).CompareTo(ang(j)));
            var ni = new List<string>(); var np = new List<double[]>();
            foreach (int i in order) { ni.Add(ids[i]); np.Add(pts[i]); }
            ids.Clear(); ids.AddRange(ni);
            pts.Clear(); pts.AddRange(np);
        }
    }
}
