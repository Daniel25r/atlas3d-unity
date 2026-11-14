// ATLAS 3D (Unity) - a 3D model in memory (nodes, meshes, materials,
// textures, skins, animations) and its GLB writer. Every format ATLAS
// imports that glTFast cannot read (FBX, OBJ, 3DS, …) is turned into this
// and saved as one self-contained GLB, like the Godot version's
// ModelConverter: one model format downstream, bones, materials and
// textures included. Plain C#: no Unity types, so it can run off the main
// thread.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Atlas.Import
{
    public class MNode
    {
        public string name = "nodo";
        public int parent = -1;
        public float[] t = { 0, 0, 0 };
        public float[] r = { 0, 0, 0, 1 };
        public float[] s = { 1, 1, 1 };
        public int mesh = -1;
    }

    public class MPart
    {
        public int material = -1;
        public uint[] indices;
    }

    public class MMesh
    {
        public string name = "malla";
        public float[] pos, nor, uv;           // 3, 3, 2 per vertex (uv optional)
        public ushort[] joints;                // 4 per vertex (skinned)
        public float[] weights;                // 4 per vertex (skinned)
        public int[] skinJoints;               // node index per joint
        public float[] skinIbm;                // 16 per joint, column-major
        public readonly List<MPart> parts = new List<MPart>();
        public int VertexCount { get { return pos != null ? pos.Length / 3 : 0; } }
    }

    public class MMaterial
    {
        public string name = "material";
        public float[] color = { 0.8f, 0.8f, 0.8f, 1f };   // linear
        public float metallic, roughness = 0.6f;
        public float[] emission = { 0, 0, 0 };
        public int texture = -1, normalTexture = -1;
        public bool doubleSided;
    }

    public class MTexture
    {
        public byte[] data;   // PNG or JPEG
        public string mime;
    }

    public class MAnimation
    {
        public string name = "Animación";
        public float fps = 30f;
        public int frames;
        public float[] trs;   // frames * nodes * 10 (t3 r4 s3)
    }

    public class MModel
    {
        public readonly List<MNode> nodes = new List<MNode>();
        public readonly List<MMesh> meshes = new List<MMesh>();
        public readonly List<MMaterial> materials = new List<MMaterial>();
        public readonly List<MTexture> textures = new List<MTexture>();
        public readonly List<MAnimation> animations = new List<MAnimation>();

        /// PNG / JPEG only (what glTF and phones read); other formats null.
        public static string ImageMime(byte[] d)
        {
            if (d == null || d.Length < 8) return null;
            if (d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return "image/png";
            if (d[0] == 0xFF && d[1] == 0xD8) return "image/jpeg";
            return null;
        }

        // ------------------------------------------------------------------
        // GLB

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string N(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
            return v.ToString("R", Inv);
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        class Bin
        {
            public readonly MemoryStream data = new MemoryStream();
            public readonly List<string> views = new List<string>();
            public readonly List<string> accessors = new List<string>();

            public int View(byte[] bytes, int target)
            {
                while (data.Length % 4 != 0) data.WriteByte(0);
                long off = data.Length;
                data.Write(bytes, 0, bytes.Length);
                views.Add("{\"buffer\":0,\"byteOffset\":" + off + ",\"byteLength\":" + bytes.Length + (target > 0 ? ",\"target\":" + target : "") + "}");
                return views.Count - 1;
            }

            public int Acc(int view, int comp, int count, string type, string extra)
            {
                accessors.Add("{\"bufferView\":" + view + ",\"componentType\":" + comp + ",\"count\":" + count + ",\"type\":\"" + type + "\"" + extra + "}");
                return accessors.Count - 1;
            }

            public int Floats(float[] f, int comps, string type, int target, bool minMax)
            {
                var bytes = new byte[f.Length * 4];
                Buffer.BlockCopy(f, 0, bytes, 0, bytes.Length);
                string extra = "";
                if (minMax && f.Length >= comps)
                {
                    var mn = new float[comps];
                    var mx = new float[comps];
                    for (int c = 0; c < comps; c++) { mn[c] = float.MaxValue; mx[c] = float.MinValue; }
                    for (int i = 0; i < f.Length; i++)
                    {
                        float v = float.IsNaN(f[i]) ? 0f : f[i];
                        int c = i % comps;
                        if (v < mn[c]) mn[c] = v;
                        if (v > mx[c]) mx[c] = v;
                    }
                    var a = new string[comps];
                    var b = new string[comps];
                    for (int c = 0; c < comps; c++) { a[c] = N(mn[c]); b[c] = N(mx[c]); }
                    extra = ",\"min\":[" + string.Join(",", a) + "],\"max\":[" + string.Join(",", b) + "]";
                }
                return Acc(View(bytes, target), 5126, f.Length / comps, type, extra);
            }
        }

        public void WriteGlb(string path)
        {
            if (meshes.Count == 0) throw new Exception("el archivo no tiene mallas");
            var bin = new Bin();
            var meshJson = new List<string>();
            var skinJson = new List<string>();
            var meshSkin = new int[meshes.Count];
            for (int mi = 0; mi < meshes.Count; mi++)
            {
                meshSkin[mi] = -1;
                var m = meshes[mi];
                int n = m.VertexCount;
                if (n == 0 || m.parts.Count == 0) { meshJson.Add(null); continue; }
                string attrs = "\"POSITION\":" + bin.Floats(m.pos, 3, "VEC3", 34962, true);
                if (m.nor != null && m.nor.Length == n * 3) attrs += ",\"NORMAL\":" + bin.Floats(m.nor, 3, "VEC3", 34962, false);
                if (m.uv != null && m.uv.Length == n * 2) attrs += ",\"TEXCOORD_0\":" + bin.Floats(m.uv, 2, "VEC2", 34962, false);
                if (m.joints != null && m.weights != null && m.joints.Length == n * 4 && m.skinJoints != null && m.skinJoints.Length > 0)
                {
                    var jb = new byte[m.joints.Length * 2];
                    Buffer.BlockCopy(m.joints, 0, jb, 0, jb.Length);
                    attrs += ",\"JOINTS_0\":" + bin.Acc(bin.View(jb, 34962), 5123, n, "VEC4", "");
                    attrs += ",\"WEIGHTS_0\":" + bin.Floats(m.weights, 4, "VEC4", 34962, false);
                    var sk = new StringBuilder();
                    sk.Append("{\"inverseBindMatrices\":").Append(bin.Floats(m.skinIbm, 16, "MAT4", 0, false)).Append(",\"joints\":[");
                    for (int j = 0; j < m.skinJoints.Length; j++) sk.Append(j > 0 ? "," : "").Append(Math.Max(0, m.skinJoints[j]));
                    sk.Append("]}");
                    skinJson.Add(sk.ToString());
                    meshSkin[mi] = skinJson.Count - 1;
                }
                var prims = new List<string>();
                foreach (var p in m.parts)
                {
                    if (p.indices == null || p.indices.Length < 3) continue;
                    var ib = new byte[p.indices.Length * 4];
                    Buffer.BlockCopy(p.indices, 0, ib, 0, ib.Length);
                    int ia = bin.Acc(bin.View(ib, 34963), 5125, p.indices.Length, "SCALAR", "");
                    prims.Add("{\"attributes\":{" + attrs + "},\"indices\":" + ia + (p.material >= 0 && p.material < materials.Count ? ",\"material\":" + p.material : "") + "}");
                }
                meshJson.Add(prims.Count > 0 ? "{\"name\":\"" + Esc(m.name) + "\",\"primitives\":[" + string.Join(",", prims) + "]}" : null);
            }
            // Meshes without triangles are dropped: renumber.
            var meshIndex = new int[meshes.Count];
            var meshOut = new List<string>();
            for (int i = 0; i < meshes.Count; i++)
            {
                meshIndex[i] = meshJson[i] != null ? meshOut.Count : -1;
                if (meshJson[i] != null) meshOut.Add(meshJson[i]);
            }
            if (meshOut.Count == 0) throw new Exception("el archivo no tiene mallas con caras");

            // Textures / images
            var imageJson = new List<string>();
            var texJson = new List<string>();
            var texIndex = new int[textures.Count];
            for (int i = 0; i < textures.Count; i++)
            {
                texIndex[i] = -1;
                var t = textures[i];
                if (t == null || t.data == null || t.mime == null) continue;
                imageJson.Add("{\"bufferView\":" + bin.View(t.data, 0) + ",\"mimeType\":\"" + t.mime + "\"}");
                texJson.Add("{\"source\":" + (imageJson.Count - 1) + ",\"sampler\":0}");
                texIndex[i] = texJson.Count - 1;
            }
            var matJson = new List<string>();
            foreach (var m in materials)
            {
                var sb = new StringBuilder();
                sb.Append("{\"name\":\"").Append(Esc(m.name)).Append("\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[")
                  .Append(N(m.color[0])).Append(',').Append(N(m.color[1])).Append(',').Append(N(m.color[2])).Append(',').Append(N(m.color[3])).Append(']')
                  .Append(",\"metallicFactor\":").Append(N(Clamp01(m.metallic))).Append(",\"roughnessFactor\":").Append(N(Clamp01(m.roughness)));
                if (m.texture >= 0 && m.texture < texIndex.Length && texIndex[m.texture] >= 0)
                    sb.Append(",\"baseColorTexture\":{\"index\":").Append(texIndex[m.texture]).Append('}');
                sb.Append('}');
                if (m.normalTexture >= 0 && m.normalTexture < texIndex.Length && texIndex[m.normalTexture] >= 0)
                    sb.Append(",\"normalTexture\":{\"index\":").Append(texIndex[m.normalTexture]).Append('}');
                if (m.emission[0] + m.emission[1] + m.emission[2] > 0.001f)
                    sb.Append(",\"emissiveFactor\":[").Append(N(Clamp01(m.emission[0]))).Append(',').Append(N(Clamp01(m.emission[1]))).Append(',').Append(N(Clamp01(m.emission[2]))).Append(']');
                if (m.color[3] < 0.999f) sb.Append(",\"alphaMode\":\"BLEND\"");
                if (m.doubleSided) sb.Append(",\"doubleSided\":true");
                sb.Append('}');
                matJson.Add(sb.ToString());
            }

            // Nodes
            var children = new List<int>[nodes.Count];
            var roots = new List<int>();
            for (int i = 0; i < nodes.Count; i++) children[i] = new List<int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                int p = nodes[i].parent;
                if (p >= 0 && p < nodes.Count && p != i) children[p].Add(i);
                else roots.Add(i);
            }
            var nodeJson = new List<string>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                var sb = new StringBuilder();
                sb.Append("{\"name\":\"").Append(Esc(string.IsNullOrEmpty(n.name) ? "nodo" + i : n.name)).Append('"');
                sb.Append(",\"translation\":[").Append(N(n.t[0])).Append(',').Append(N(n.t[1])).Append(',').Append(N(n.t[2])).Append(']');
                sb.Append(",\"rotation\":[").Append(N(n.r[0])).Append(',').Append(N(n.r[1])).Append(',').Append(N(n.r[2])).Append(',').Append(N(n.r[3])).Append(']');
                sb.Append(",\"scale\":[").Append(N(n.s[0])).Append(',').Append(N(n.s[1])).Append(',').Append(N(n.s[2])).Append(']');
                if (n.mesh >= 0 && n.mesh < meshes.Count && meshIndex[n.mesh] >= 0)
                {
                    sb.Append(",\"mesh\":").Append(meshIndex[n.mesh]);
                    if (meshSkin[n.mesh] >= 0) sb.Append(",\"skin\":").Append(meshSkin[n.mesh]);
                }
                if (children[i].Count > 0) sb.Append(",\"children\":[").Append(string.Join(",", children[i])).Append(']');
                sb.Append('}');
                nodeJson.Add(sb.ToString());
            }

            // Animations: only what moves.
            var animJson = new List<string>();
            foreach (var a in animations)
            {
                if (a.frames < 2 || a.trs == null || a.trs.Length < a.frames * nodes.Count * 10) continue;
                var times = new float[a.frames];
                for (int f = 0; f < a.frames; f++) times[f] = f / a.fps;
                int timeAcc = -1;
                var samplers = new List<string>();
                var channels = new List<string>();
                int nc = nodes.Count;
                for (int i = 0; i < nc; i++)
                {
                    bool mt = false, mr = false, ms = false;
                    int b0 = i * 10;
                    for (int f = 1; f < a.frames && !(mt && mr && ms); f++)
                    {
                        int b = (f * nc + i) * 10;
                        for (int k = 0; k < 3; k++) if (Math.Abs(a.trs[b + k] - a.trs[b0 + k]) > 1e-5f) mt = true;
                        for (int k = 3; k < 7; k++) if (Math.Abs(a.trs[b + k] - a.trs[b0 + k]) > 1e-5f) mr = true;
                        for (int k = 7; k < 10; k++) if (Math.Abs(a.trs[b + k] - a.trs[b0 + k]) > 1e-5f) ms = true;
                    }
                    if (!mt && !mr && !ms) continue;
                    if (timeAcc < 0) timeAcc = bin.Floats(times, 1, "SCALAR", 0, true);
                    if (mt) Channel(bin, a, i, 0, 3, "VEC3", "translation", timeAcc, samplers, channels);
                    if (mr) Channel(bin, a, i, 3, 4, "VEC4", "rotation", timeAcc, samplers, channels);
                    if (ms) Channel(bin, a, i, 7, 3, "VEC3", "scale", timeAcc, samplers, channels);
                }
                if (channels.Count > 0)
                    animJson.Add("{\"name\":\"" + Esc(string.IsNullOrEmpty(a.name) ? "Animación " + (animJson.Count + 1) : a.name) + "\",\"samplers\":[" + string.Join(",", samplers) + "],\"channels\":[" + string.Join(",", channels) + "]}");
            }

            var js = new StringBuilder();
            js.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"ATLAS 3D\"},\"scene\":0,\"scenes\":[{\"nodes\":[").Append(string.Join(",", roots)).Append("]}]");
            js.Append(",\"nodes\":[").Append(string.Join(",", nodeJson)).Append(']');
            js.Append(",\"meshes\":[").Append(string.Join(",", meshOut)).Append(']');
            if (matJson.Count > 0) js.Append(",\"materials\":[").Append(string.Join(",", matJson)).Append(']');
            if (skinJson.Count > 0) js.Append(",\"skins\":[").Append(string.Join(",", skinJson)).Append(']');
            if (animJson.Count > 0) js.Append(",\"animations\":[").Append(string.Join(",", animJson)).Append(']');
            if (texJson.Count > 0)
            {
                js.Append(",\"samplers\":[{\"magFilter\":9729,\"minFilter\":9987,\"wrapS\":10497,\"wrapT\":10497}]");
                js.Append(",\"images\":[").Append(string.Join(",", imageJson)).Append(']');
                js.Append(",\"textures\":[").Append(string.Join(",", texJson)).Append(']');
            }
            while (bin.data.Length % 4 != 0) bin.data.WriteByte(0);
            byte[] buffer = bin.data.ToArray();
            js.Append(",\"accessors\":[").Append(string.Join(",", bin.accessors)).Append(']');
            js.Append(",\"bufferViews\":[").Append(string.Join(",", bin.views)).Append(']');
            js.Append(",\"buffers\":[{\"byteLength\":").Append(buffer.Length).Append("}]}");
            byte[] json = Encoding.UTF8.GetBytes(js.ToString());
            int jsonLen = (json.Length + 3) & ~3;
            using (var f = File.Create(path))
            using (var w = new BinaryWriter(f))
            {
                w.Write(0x46546C67u);
                w.Write(2u);
                w.Write((uint)(12 + 8 + jsonLen + 8 + buffer.Length));
                w.Write((uint)jsonLen);
                w.Write(0x4E4F534Au);
                w.Write(json);
                for (int i = json.Length; i < jsonLen; i++) w.Write((byte)0x20);
                w.Write((uint)buffer.Length);
                w.Write(0x004E4942u);
                w.Write(buffer);
            }
        }

        static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        static void Channel(Bin bin, MAnimation a, int node, int off, int comps, string type, string path, int timeAcc, List<string> samplers, List<string> channels)
        {
            int nc = a.trs.Length / (a.frames * 10);
            var o = new float[a.frames * comps];
            for (int f = 0; f < a.frames; f++)
            {
                int b = (f * nc + node) * 10 + off;
                for (int k = 0; k < comps; k++) o[f * comps + k] = a.trs[b + k];
            }
            if (comps == 4)
            {
                // Keep each quaternion on the same side as the previous one.
                for (int f = 1; f < a.frames; f++)
                {
                    float dot = 0f;
                    for (int k = 0; k < 4; k++) dot += o[f * 4 + k] * o[(f - 1) * 4 + k];
                    if (dot < 0f) for (int k = 0; k < 4; k++) o[f * 4 + k] = -o[f * 4 + k];
                }
            }
            samplers.Add("{\"input\":" + timeAcc + ",\"output\":" + bin.Floats(o, comps, type, 0, false) + ",\"interpolation\":\"LINEAR\"}");
            channels.Add("{\"sampler\":" + (samplers.Count - 1) + ",\"target\":{\"node\":" + node + ",\"path\":\"" + path + "\"}}");
        }
    }
}
