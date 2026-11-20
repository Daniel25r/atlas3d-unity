// ATLAS 3D (Unity) - textures of imported models made right for the device:
//   · normal maps: on PC the shader reads them as «DXT5nm» (X from the
//     alpha). A normal map with transparency, height or a specular mask in
//     its alpha came out with the relief twisted (stripes, dark sides,
//     light from the wrong side). They are copied with a solid alpha.
//   · the «flat» relief used where a material has none: Unity's own one is
//     half transparent and on PC tilted every model's lighting a little.
//   · phones: textures bigger than the phone can hold comfortably (4K and
//     8K from Sketchfab) are made smaller once, on the graphics card. A model
//     with ten 4K textures used more than a gigabyte and closed the app.
//   · meshes with relief get their tangents if the file brought none.
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public static class TextureTune
    {
        static Texture2D flat;

        /// A flat normal map (no relief) that reads right everywhere.
        public static Texture2D Flat
        {
            get
            {
                if (flat == null)
                {
                    flat = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "ReliévePlano" };
                    var px = new Color32[16];
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32(128, 128, 255, 255);
                    flat.SetPixels32(px);
                    flat.Apply(false, true);
                    flat.hideFlags = HideFlags.DontSave;
                }
                return flat;
            }
        }

        /// Largest side a texture keeps on this device.
        public static int MaxSide
        {
            get
            {
                if (!Application.isMobilePlatform) return 8192;
                int mem = SystemInfo.systemMemorySize;   // MB
                if (mem > 0 && mem < 3500) return 1024;
                if (mem > 0 && mem < 6500) return 2048;
                return 4096;
            }
        }

        /// The shader reads normal maps as DXT5nm (PC, editor).
        static bool Dxt5nm { get { return !Application.isMobilePlatform; } }

        static readonly string[] ColorProps = { "_BaseMap", "_EmissionMap" };

        /// Fixes the textures and tangents of a freshly loaded model.
        public static void Model(GameObject root)
        {
            if (root == null) return;
            int max = MaxSide;
            var done = new Dictionary<Texture, Texture2D>();
            var needTangents = new HashSet<Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    foreach (var p in ColorProps)
                    {
                        if (!m.HasProperty(p)) continue;
                        var t = m.GetTexture(p) as Texture2D;
                        if (t == null || Mathf.Max(t.width, t.height) <= max) continue;
                        var n = Get(done, t, max, false);
                        if (n != null) m.SetTexture(p, n);
                    }
                    if (!m.HasProperty("_BumpMap")) continue;
                    var bt = m.GetTexture("_BumpMap") as Texture2D;
                    if (bt == null || bt == Texture2D.normalTexture)
                    {
                        // No relief: the flat one (Unity's tilted the light on PC).
                        m.SetTexture("_BumpMap", Flat);
                        continue;
                    }
                    if (bt == flat) continue;
                    if (Dxt5nm || Mathf.Max(bt.width, bt.height) > max)
                    {
                        var n = Get(done, bt, max, true);
                        if (n != null) m.SetTexture("_BumpMap", n);
                    }
                    if (m.IsKeywordEnabled("_NORMALMAP")) needTangents.Add(m);
                }
            }
            if (needTangents.Count > 0) Tangents(root, needTangents);
            // The big originals are not used any more: memory back.
            foreach (var kv in done)
                if (kv.Value != null && kv.Key != null && kv.Key != kv.Value) Object.Destroy(kv.Key);
        }

        static Texture2D Get(Dictionary<Texture, Texture2D> done, Texture2D t, int max, bool normal)
        {
            Texture2D n;
            if (done.TryGetValue(t, out n)) return n;
            n = Copy(t, max, normal);
            done[t] = n;
            return n;
        }

        /// A copy of `src` no bigger than `max`; `normal`: linear, alpha solid.
        public static Texture2D Copy(Texture2D src, int max, bool normal)
        {
            if (src == null) return null;
            int w = src.width, h = src.height;
            float k = Mathf.Min(1f, max / (float)Mathf.Max(w, h));
            int nw = Mathf.Max(1, Mathf.RoundToInt(w * k)), nh = Mathf.Max(1, Mathf.RoundToInt(h * k));
            bool linear = normal || !src.isDataSRGB;
            RenderTexture rt = null;
            var prev = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32,
                    linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
                rt.filterMode = FilterMode.Bilinear;
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var dst = new Texture2D(nw, nh, TextureFormat.RGBA32, true, linear);
                dst.ReadPixels(new Rect(0, 0, nw, nh), 0, 0, false);
                if (normal)
                {
                    var px = dst.GetPixels32();
                    for (int i = 0; i < px.Length; i++) px[i].a = 255;
                    dst.SetPixels32(px);
                }
                dst.Apply(true, true);
                dst.name = src.name;
                dst.wrapModeU = src.wrapModeU;
                dst.wrapModeV = src.wrapModeV;
                dst.filterMode = src.filterMode;
                dst.anisoLevel = Mathf.Max(src.anisoLevel, 2);
                return dst;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("ATLAS: no se pudo ajustar la textura " + src.name + ": " + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// Tangents for meshes that use a material with relief and have none.
        public static void Tangents(GameObject root, HashSet<Material> mats)
        {
            var seen = new HashSet<Mesh>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                bool uses = false;
                foreach (var m in r.sharedMaterials) if (m != null && mats.Contains(m)) { uses = true; break; }
                if (!uses) continue;
                var smr = r as SkinnedMeshRenderer;
                var mf = smr == null ? r.GetComponent<MeshFilter>() : null;
                var mesh = smr != null ? smr.sharedMesh : mf != null ? mf.sharedMesh : null;
                if (mesh == null || !seen.Add(mesh) || !mesh.isReadable) continue;
                try
                {
                    if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) continue;
                    if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)) continue;
                    if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal)) mesh.RecalculateNormals();
                    mesh.RecalculateTangents();
                }
                catch (System.Exception) { }
            }
        }

        /// A picture read as a normal map or as a height map (black and
        /// white relief), always given back as a normal map.
        /// `flipGreen`: DirectX-style (Source, Substance «DirectX»).
        public static Texture2D NormalFromImage(byte[] data, bool flipGreen)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!t.LoadImage(data)) { Object.Destroy(t); return null; }
            int w = t.width, h = t.height;
            var px = t.GetPixels32();
            Object.Destroy(t);
            // Normal maps are bluish with red and green around the middle;
            // height maps are grey.
            long sr = 0, sg = 0, sb = 0, grey = 0;
            int step = Mathf.Max(1, px.Length / 4096);
            int cnt = 0;
            for (int i = 0; i < px.Length; i += step)
            {
                var c = px[i];
                sr += c.r; sg += c.g; sb += c.b; cnt++;
                if (Mathf.Abs(c.r - c.g) < 6 && Mathf.Abs(c.g - c.b) < 6) grey++;
            }
            bool isNormal = cnt > 0 && sb / cnt > 170 && Mathf.Abs(sr / cnt - 128) < 50 && Mathf.Abs(sg / cnt - 128) < 50 && grey < cnt * 0.6f;
            var outPx = new Color32[px.Length];
            if (isNormal)
            {
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    outPx[i] = new Color32(c.r, flipGreen ? (byte)(255 - c.g) : c.g, c.b, 255);
                }
            }
            else
            {
                var hgt = new float[px.Length];
                for (int i = 0; i < px.Length; i++) hgt[i] = (px[i].r * 0.3f + px[i].g * 0.59f + px[i].b * 0.11f) / 255f;
                const float strength = 4f;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int xl = (x + w - 1) % w, xr = (x + 1) % w, yd = (y + h - 1) % h, yu = (y + 1) % h;
                        float dx = (hgt[y * w + xr] - hgt[y * w + xl]) * strength;
                        float dy = (hgt[yu * w + x] - hgt[yd * w + x]) * strength;
                        var n = new Vector3(-dx, -dy, 1f).normalized;
                        outPx[y * w + x] = new Color32((byte)(n.x * 127.5f + 127.5f), (byte)(n.y * 127.5f + 127.5f), (byte)(n.z * 127.5f + 127.5f), 255);
                    }
            }
            var res = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
            res.SetPixels32(outPx);
            res.Apply(true, true);
            res.wrapMode = TextureWrapMode.Repeat;
            res.filterMode = FilterMode.Trilinear;
            res.anisoLevel = 4;
            return res;
        }
    }
}
