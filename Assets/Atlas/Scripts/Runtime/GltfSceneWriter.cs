// ATLAS 3D (Unity) - full glTF 2.0 export (GLB or .gltf): the objects with
// their hierarchy, meshes, PBR materials with embedded textures, rigged
// models with their skeleton and skin weights, and the animation (object
// keys, bone keys, poses and the models' own animations), sampled frame by
// frame. Written from the public specification; no package needed.
// Unity is left-handed, glTF right-handed: everything is mirrored on X.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Atlas
{
    public static class GltfSceneWriter
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        class Bin
        {
            public readonly MemoryStream data = new MemoryStream();
            public readonly List<string> views = new List<string>();
            public readonly List<string> accessors = new List<string>();

            public int View(byte[] bytes, int target)
            {
                while (data.Length % 4 != 0) data.WriteByte(0);
                long offset = data.Length;
                data.Write(bytes, 0, bytes.Length);
                string t = target > 0 ? ",\"target\":" + target : "";
                views.Add("{\"buffer\":0,\"byteOffset\":" + offset + ",\"byteLength\":" + bytes.Length + t + "}");
                return views.Count - 1;
            }

            public int Accessor(int view, int componentType, int count, string type, string extra)
            {
                accessors.Add("{\"bufferView\":" + view + ",\"componentType\":" + componentType + ",\"count\":" + count + ",\"type\":\"" + type + "\"" + extra + "}");
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
                        int c = i % comps;
                        if (f[i] < mn[c]) mn[c] = f[i];
                        if (f[i] > mx[c]) mx[c] = f[i];
                    }
                    extra = ",\"min\":[" + Join(mn) + "],\"max\":[" + Join(mx) + "]";
                }
                return Accessor(View(bytes, target), 5126, f.Length / comps, type, extra);
            }
        }

        static string N(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
            return v.ToString("0.#######", Inv);
        }

        static string Join(float[] f)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < f.Length; i++) { if (i > 0) sb.Append(','); sb.Append(N(f[i])); }
            return sb.ToString();
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "nodo";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // Unity (left-handed) -> glTF (right-handed): mirror X.
        static Vector3 P(Vector3 v) { return new Vector3(-v.x, v.y, v.z); }
        static Quaternion Q(Quaternion q) { return new Quaternion(q.x, -q.y, -q.z, q.w); }
        static Matrix4x4 M(Matrix4x4 m)
        {
            var s = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
            return s * m * s;
        }

        class NodeInfo
        {
            public Transform t;
            public int mesh = -1;
            public int skin = -1;
            public List<int> children = new List<int>();
            public SkinnedMeshRenderer smr;
        }

        public class Options
        {
            public bool binary = true;
            public Action<float> setFrame;   // poses the scene at a frame
            public int endFrame;
            public int fps = 24;
            public float currentFrame;
        }

        public static void Write(List<AtlasItem> items, string path, Options opt)
        {
            var bin = new Bin();
            var nodes = new List<NodeInfo>();
            var nodeOf = new Dictionary<Transform, int>();
            var roots = new List<int>();
            foreach (var it in items)
            {
                if (it == null) continue;
                roots.Add(AddNode(it.transform, nodes, nodeOf));
            }
            if (roots.Count == 0) throw new Exception("no hay objetos que exportar");

            // Meshes and materials
            var meshes = new List<string>();
            var materials = new List<string>();
            var images = new List<string>();
            var textures = new List<string>();
            var texIndex = new Dictionary<Texture, int>();
            var matIndex = new Dictionary<Material, int>();
            var skins = new List<string>();
            int written = 0;
            foreach (var n in nodes)
            {
                var mr = n.t.GetComponent<MeshRenderer>();
                var mf = n.t.GetComponent<MeshFilter>();
                var smr = n.t.GetComponent<SkinnedMeshRenderer>();
                Mesh mesh = null;
                Material[] mats = null;
                if (smr != null && smr.sharedMesh != null) { mesh = smr.sharedMesh; mats = smr.sharedMaterials; n.smr = smr; }
                else if (mr != null && mf != null && mf.sharedMesh != null) { mesh = mf.sharedMesh; mats = mr.sharedMaterials; }
                if (mesh == null || !mesh.isReadable) continue;
                int mi = WriteMesh(bin, mesh, mats, n.smr != null, materials, images, textures, texIndex, matIndex, meshes);
                if (mi < 0) continue;
                n.mesh = mi;
                written++;
            }
            if (written == 0) throw new Exception("las mallas no se pueden leer");

            // Skins: joints are the bone nodes; inverse bind = the mesh's bindposes.
            foreach (var n in nodes)
            {
                if (n.smr == null || n.mesh < 0) continue;
                var bones = n.smr.bones;
                var bind = n.smr.sharedMesh.bindposes;
                if (bones == null || bones.Length == 0 || bind.Length != bones.Length) { continue; }
                var joints = new List<int>();
                var ibm = new float[bones.Length * 16];
                bool ok = true;
                for (int i = 0; i < bones.Length; i++)
                {
                    int ni;
                    if (bones[i] == null || !nodeOf.TryGetValue(bones[i], out ni)) { ok = false; break; }
                    joints.Add(ni);
                    var m = M(bind[i]);
                    for (int c = 0; c < 4; c++)
                        for (int r = 0; r < 4; r++)
                            ibm[i * 16 + c * 4 + r] = m[r, c];   // column-major
                }
                if (!ok) continue;
                int acc = bin.Floats(ibm, 16, "MAT4", 0, false);
                var js = new StringBuilder();
                js.Append("{\"inverseBindMatrices\":").Append(acc).Append(",\"joints\":[");
                for (int i = 0; i < joints.Count; i++) js.Append(i > 0 ? "," : "").Append(joints[i]);
                js.Append("]");
                int rootNode;
                if (n.smr.rootBone != null && nodeOf.TryGetValue(n.smr.rootBone, out rootNode)) js.Append(",\"skeleton\":").Append(rootNode);
                js.Append("}");
                skins.Add(js.ToString());
                n.skin = skins.Count - 1;
            }

            // Animation: sample every frame and keep what moves.
            string animJson = "";
            if (opt.setFrame != null && opt.endFrame > 0)
            {
                try { animJson = SampleAnimation(bin, nodes, opt); }
                finally { opt.setFrame(opt.currentFrame); }
            }

            // Rest transforms (at the frame on screen)
            var js2 = new StringBuilder();
            js2.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"ATLAS 3D\"},\"scene\":0,\"scenes\":[{\"name\":\"ATLAS 3D\",\"nodes\":[");
            for (int i = 0; i < roots.Count; i++) js2.Append(i > 0 ? "," : "").Append(roots[i]);
            js2.Append("]}],\"nodes\":[");
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (i > 0) js2.Append(',');
                Vector3 tp = P(n.t.localPosition);
                Quaternion tq = Q(n.t.localRotation);
                Vector3 ts = n.t.localScale;
                js2.Append("{\"name\":\"").Append(Esc(n.t.name)).Append("\"");
                js2.Append(",\"translation\":[").Append(N(tp.x)).Append(',').Append(N(tp.y)).Append(',').Append(N(tp.z)).Append(']');
                js2.Append(",\"rotation\":[").Append(N(tq.x)).Append(',').Append(N(tq.y)).Append(',').Append(N(tq.z)).Append(',').Append(N(tq.w)).Append(']');
                js2.Append(",\"scale\":[").Append(N(ts.x)).Append(',').Append(N(ts.y)).Append(',').Append(N(ts.z)).Append(']');
                if (n.mesh >= 0) js2.Append(",\"mesh\":").Append(n.mesh);
                if (n.skin >= 0) js2.Append(",\"skin\":").Append(n.skin);
                if (n.children.Count > 0)
                {
                    js2.Append(",\"children\":[");
                    for (int c = 0; c < n.children.Count; c++) js2.Append(c > 0 ? "," : "").Append(n.children[c]);
                    js2.Append(']');
                }
                js2.Append('}');
            }
            js2.Append(']');
            js2.Append(",\"meshes\":[").Append(string.Join(",", meshes)).Append(']');
            js2.Append(",\"materials\":[").Append(string.Join(",", materials)).Append(']');
            if (skins.Count > 0) js2.Append(",\"skins\":[").Append(string.Join(",", skins)).Append(']');
            foreach (var mj in materials)
                if (mj.Contains("KHR_materials_emissive_strength"))
                {
                    js2.Append(",\"extensionsUsed\":[\"KHR_materials_emissive_strength\"]");
                    break;
                }
            if (animJson != "") js2.Append(",\"animations\":[").Append(animJson).Append(']');
            if (textures.Count > 0)
            {
                js2.Append(",\"samplers\":[{\"magFilter\":9729,\"minFilter\":9987,\"wrapS\":10497,\"wrapT\":10497}]");
                js2.Append(",\"images\":[").Append(string.Join(",", images)).Append(']');
                js2.Append(",\"textures\":[").Append(string.Join(",", textures)).Append(']');
            }
            while (bin.data.Length % 4 != 0) bin.data.WriteByte(0);
            byte[] buffer = bin.data.ToArray();
            js2.Append(",\"accessors\":[").Append(string.Join(",", bin.accessors)).Append(']');
            js2.Append(",\"bufferViews\":[").Append(string.Join(",", bin.views)).Append(']');
            if (opt.binary)
                js2.Append(",\"buffers\":[{\"byteLength\":").Append(buffer.Length).Append("}]}");
            else
                js2.Append(",\"buffers\":[{\"byteLength\":").Append(buffer.Length).Append(",\"uri\":\"data:application/octet-stream;base64,").Append(Convert.ToBase64String(buffer)).Append("\"}]}");

            if (!opt.binary)
            {
                File.WriteAllText(path, js2.ToString(), new UTF8Encoding(false));
                return;
            }
            byte[] json = Encoding.UTF8.GetBytes(js2.ToString());
            int jsonLen = (json.Length + 3) & ~3;
            int total = 12 + 8 + jsonLen + 8 + buffer.Length;
            using (var f = File.Create(path))
            using (var w = new BinaryWriter(f))
            {
                w.Write(0x46546C67u);   // "glTF"
                w.Write(2u);
                w.Write((uint)total);
                w.Write((uint)jsonLen);
                w.Write(0x4E4F534Au);   // "JSON"
                w.Write(json);
                for (int i = json.Length; i < jsonLen; i++) w.Write((byte)0x20);
                w.Write((uint)buffer.Length);
                w.Write(0x004E4942u);   // "BIN"
                w.Write(buffer);
            }
        }

        static int AddNode(Transform t, List<NodeInfo> nodes, Dictionary<Transform, int> nodeOf)
        {
            var n = new NodeInfo { t = t };
            nodes.Add(n);
            int idx = nodes.Count - 1;
            nodeOf[t] = idx;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.GetComponent<Light>() != null && c.childCount == 0 && c.GetComponent<Renderer>() == null) continue;
                n.children.Add(AddNode(c, nodes, nodeOf));
            }
            return idx;
        }

        static int WriteMesh(Bin bin, Mesh mesh, Material[] mats, bool skinned, List<string> materials, List<string> images,
            List<string> textures, Dictionary<Texture, int> texIndex, Dictionary<Material, int> matIndex, List<string> meshes)
        {
            var v = mesh.vertices;
            int n = v.Length;
            if (n == 0) return -1;
            var nrm = mesh.normals;
            var uv = mesh.uv;
            var pos = new float[n * 3];
            var nor = new float[n * 3];
            for (int i = 0; i < n; i++)
            {
                var p = P(v[i]);
                pos[i * 3] = p.x; pos[i * 3 + 1] = p.y; pos[i * 3 + 2] = p.z;
                var q = i < nrm.Length && nrm[i].sqrMagnitude > 0f ? P(nrm[i].normalized) : Vector3.up;
                nor[i * 3] = q.x; nor[i * 3 + 1] = q.y; nor[i * 3 + 2] = q.z;
            }
            string attrs = "\"POSITION\":" + bin.Floats(pos, 3, "VEC3", 34962, true) + ",\"NORMAL\":" + bin.Floats(nor, 3, "VEC3", 34962, false);
            if (uv.Length == n)
            {
                var uvf = new float[n * 2];
                for (int i = 0; i < n; i++) { uvf[i * 2] = uv[i].x; uvf[i * 2 + 1] = 1f - uv[i].y; }
                attrs += ",\"TEXCOORD_0\":" + bin.Floats(uvf, 2, "VEC2", 34962, false);
            }
            if (skinned)
            {
                var bw = mesh.boneWeights;
                if (bw.Length == n)
                {
                    var joints = new byte[n * 8];
                    var weights = new float[n * 4];
                    for (int i = 0; i < n; i++)
                    {
                        var w = bw[i];
                        float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                        if (sum <= 0f) sum = 1f;
                        int[] ji = { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
                        float[] wi = { w.weight0 / sum, w.weight1 / sum, w.weight2 / sum, w.weight3 / sum };
                        for (int k = 0; k < 4; k++)
                        {
                            ushort j = (ushort)Mathf.Max(0, wi[k] > 0f ? ji[k] : 0);
                            joints[i * 8 + k * 2] = (byte)(j & 0xff);
                            joints[i * 8 + k * 2 + 1] = (byte)(j >> 8);
                            weights[i * 4 + k] = wi[k];
                        }
                    }
                    int ja = bin.Accessor(bin.View(joints, 34962), 5123, n, "VEC4", "");
                    attrs += ",\"JOINTS_0\":" + ja + ",\"WEIGHTS_0\":" + bin.Floats(weights, 4, "VEC4", 34962, false);
                }
            }
            var prims = new List<string>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var t = mesh.GetTriangles(s);
                if (t.Length < 3) continue;
                // Mirroring flips the winding: swap two corners.
                for (int k = 0; k + 2 < t.Length; k += 3) { int tmp = t[k + 1]; t[k + 1] = t[k + 2]; t[k + 2] = tmp; }
                var idx = new byte[t.Length * 4];
                Buffer.BlockCopy(t, 0, idx, 0, idx.Length);
                int ia = bin.Accessor(bin.View(idx, 34963), 5125, t.Length, "SCALAR", "");
                var mat = mats != null && mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null;
                int mi = MaterialIndex(bin, mat, materials, images, textures, texIndex, matIndex);
                prims.Add("{\"attributes\":{" + attrs + "},\"indices\":" + ia + ",\"material\":" + mi + "}");
            }
            if (prims.Count == 0) return -1;
            meshes.Add("{\"name\":\"" + Esc(mesh.name) + "\",\"primitives\":[" + string.Join(",", prims) + "]}");
            return meshes.Count - 1;
        }

        static Color GetColor(Material m, params string[] props)
        {
            foreach (var p in props) if (m.HasProperty(p)) return m.GetColor(p);
            return Color.white;
        }

        static float GetFloat(Material m, float def, params string[] props)
        {
            foreach (var p in props) if (m.HasProperty(p)) return m.GetFloat(p);
            return def;
        }

        static Texture GetTex(Material m, params string[] props)
        {
            foreach (var p in props) if (m.HasProperty(p) && m.GetTexture(p) != null) return m.GetTexture(p);
            return null;
        }

        static int MaterialIndex(Bin bin, Material m, List<string> materials, List<string> images, List<string> textures,
            Dictionary<Texture, int> texIndex, Dictionary<Material, int> matIndex)
        {
            if (m != null && matIndex.ContainsKey(m)) return matIndex[m];
            string json;
            if (m == null)
                json = "{\"name\":\"material\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.8,0.8,0.8,1],\"metallicFactor\":0,\"roughnessFactor\":0.6}}";
            else
            {
                Color c = GetColor(m, "_BaseColor", "baseColorFactor", "_Color");
                float metal = GetFloat(m, 0f, "_Metallic", "metallicFactor");
                float rough = m.HasProperty("_Smoothness") ? 1f - m.GetFloat("_Smoothness") : GetFloat(m, 0.6f, "roughnessFactor");
                Texture tex = GetTex(m, "_BaseMap", "baseColorTexture", "_MainTex");
                string texJson = "";
                if (tex != null)
                {
                    int ti;
                    if (!texIndex.TryGetValue(tex, out ti))
                    {
                        ti = -1;
                        byte[] png = null;
                        try { png = MeshFormats.TexturePng(tex); } catch (Exception) { png = null; }
                        if (png != null)
                        {
                            int view = bin.View(png, 0);
                            images.Add("{\"bufferView\":" + view + ",\"mimeType\":\"image/png\"}");
                            textures.Add("{\"source\":" + (images.Count - 1) + ",\"sampler\":0}");
                            ti = textures.Count - 1;
                        }
                        texIndex[tex] = ti;
                    }
                    if (ti >= 0) texJson = ",\"baseColorTexture\":{\"index\":" + ti + "}";
                }
                Color lc = c.linear;
                bool transparent = c.a < 0.999f || m.renderQueue >= 3000;
                string emis = "";
                if (m.IsKeywordEnabled("_EMISSION") || m.HasProperty("emissiveFactor"))
                {
                    Color e = GetColor(m, "_EmissionColor", "emissiveFactor");
                    float k = Mathf.Max(e.r, Mathf.Max(e.g, e.b));
                    if (k > 0.001f)
                    {
                        Color el = e.linear;
                        float mx = Mathf.Max(1f, Mathf.Max(el.r, Mathf.Max(el.g, el.b)));
                        emis = ",\"emissiveFactor\":[" + N(el.r / mx) + "," + N(el.g / mx) + "," + N(el.b / mx) + "]";
                        if (mx > 1f) emis += ",\"extensions\":{\"KHR_materials_emissive_strength\":{\"emissiveStrength\":" + N(mx) + "}}";
                    }
                }
                json = "{\"name\":\"" + Esc(m.name.Replace(" (Instance)", "")) + "\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[" + N(lc.r) + "," + N(lc.g) + "," + N(lc.b) + "," + N(c.a) +
                    "],\"metallicFactor\":" + N(metal) + ",\"roughnessFactor\":" + N(Mathf.Clamp01(rough)) + texJson + "}" + emis +
                    (transparent ? ",\"alphaMode\":\"BLEND\"" : "") + ",\"doubleSided\":false}";
            }
            materials.Add(json);
            int idx = materials.Count - 1;
            if (m != null) matIndex[m] = idx;
            return idx;
        }

        static string SampleAnimation(Bin bin, List<NodeInfo> nodes, Options opt)
        {
            int frames = opt.endFrame + 1;
            int count = nodes.Count;
            var tr = new Vector3[count, frames];
            var ro = new Quaternion[count, frames];
            var sc = new Vector3[count, frames];
            for (int f = 0; f < frames; f++)
            {
                opt.setFrame(f);
                for (int i = 0; i < count; i++)
                {
                    var t = nodes[i].t;
                    tr[i, f] = t.localPosition;
                    ro[i, f] = t.localRotation;
                    sc[i, f] = t.localScale;
                }
            }
            var times = new float[frames];
            for (int f = 0; f < frames; f++) times[f] = f / (float)Mathf.Max(1, opt.fps);
            int timeAcc = -1;
            var samplers = new List<string>();
            var channels = new List<string>();
            for (int i = 0; i < count; i++)
            {
                bool mt = false, mr = false, ms = false;
                for (int f = 1; f < frames && !(mt && mr && ms); f++)
                {
                    if (!mt && (tr[i, f] - tr[i, 0]).sqrMagnitude > 1e-10f) mt = true;
                    if (!mr && Quaternion.Angle(ro[i, f], ro[i, 0]) > 0.01f) mr = true;
                    if (!ms && (sc[i, f] - sc[i, 0]).sqrMagnitude > 1e-10f) ms = true;
                }
                if (!mt && !mr && !ms) continue;
                if (timeAcc < 0) timeAcc = bin.Floats(times, 1, "SCALAR", 0, true);
                if (mt)
                {
                    var o = new float[frames * 3];
                    for (int f = 0; f < frames; f++) { var p = P(tr[i, f]); o[f * 3] = p.x; o[f * 3 + 1] = p.y; o[f * 3 + 2] = p.z; }
                    samplers.Add("{\"input\":" + timeAcc + ",\"output\":" + bin.Floats(o, 3, "VEC3", 0, false) + ",\"interpolation\":\"LINEAR\"}");
                    channels.Add("{\"sampler\":" + (samplers.Count - 1) + ",\"target\":{\"node\":" + i + ",\"path\":\"translation\"}}");
                }
                if (mr)
                {
                    var o = new float[frames * 4];
                    Quaternion prev = Q(ro[i, 0]);
                    for (int f = 0; f < frames; f++)
                    {
                        var q = Q(ro[i, f]);
                        // Same hemisphere as the previous sample (no flips).
                        if (Quaternion.Dot(q, prev) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                        prev = q;
                        o[f * 4] = q.x; o[f * 4 + 1] = q.y; o[f * 4 + 2] = q.z; o[f * 4 + 3] = q.w;
                    }
                    samplers.Add("{\"input\":" + timeAcc + ",\"output\":" + bin.Floats(o, 4, "VEC4", 0, false) + ",\"interpolation\":\"LINEAR\"}");
                    channels.Add("{\"sampler\":" + (samplers.Count - 1) + ",\"target\":{\"node\":" + i + ",\"path\":\"rotation\"}}");
                }
                if (ms)
                {
                    var o = new float[frames * 3];
                    for (int f = 0; f < frames; f++) { var s = sc[i, f]; o[f * 3] = s.x; o[f * 3 + 1] = s.y; o[f * 3 + 2] = s.z; }
                    samplers.Add("{\"input\":" + timeAcc + ",\"output\":" + bin.Floats(o, 3, "VEC3", 0, false) + ",\"interpolation\":\"LINEAR\"}");
                    channels.Add("{\"sampler\":" + (samplers.Count - 1) + ",\"target\":{\"node\":" + i + ",\"path\":\"scale\"}}");
                }
            }
            if (channels.Count == 0) return "";
            return "{\"name\":\"Animación ATLAS\",\"samplers\":[" + string.Join(",", samplers) + "],\"channels\":[" + string.Join(",", channels) + "]}";
        }
    }
}
