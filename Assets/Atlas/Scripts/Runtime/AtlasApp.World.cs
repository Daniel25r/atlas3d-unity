// ATLAS 3D (Unity) - world pieces made by ATLAS:
//  · Agua: sea, lake or pool with waves that move with the timeline.
//  · Terreno: hills, snowy mountains, desert or island, with its own
//    painted ground and low-poly trees.
//  · Tela: flag, curtain or cape that waves with the timeline (also
//    frame by frame in renders); its look is set like any material.
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        public static readonly string[] WaterPresets = { "sea", "lake", "pool", "lava" };
        public static readonly string[] WaterNames = { "Mar", "Lago", "Piscina", "Lava" };
        public static readonly string[] TerrainPresets = { "hills", "mountains", "desert", "island" };
        public static readonly string[] TerrainNames = { "Colinas", "Montañas nevadas", "Desierto", "Isla" };
        public static readonly string[] ClothPresets = { "flag", "curtain", "cape" };
        public static readonly string[] ClothNames = { "Bandera", "Cortina", "Capa" };

        Material waterTemplate;

        GameObject MakeWorldPiece(string kind)
        {
            var go = new GameObject();
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            return go;
        }

        void AddWorldPiece(string kind, string preset)
        {
            CloseMenus();
            if (store == null) return;
            count++;
            string name = kind == "water" ? "Agua" : kind == "terrain" ? "Terreno" : "Tela";
            var d = new ItemData
            {
                v = 3,
                kind = kind,
                name = name,
                position = kind == "flag" ? new Vector3(target.position.x, 1.5f, target.position.z) : new Vector3(target.position.x, kind == "water" ? 0.05f : 0f, target.position.z),
                scale = Vector3.one,
                color = kind == "flag" ? new Color(0.85f, 0.15f, 0.2f) : Color.white,
                preset = preset,
                amount = 1f,
                seed = Random.Range(1, 99999),
                smoothness = 0.2f,
            };
            Select(Create(d));
            Commit(true);
            Toast(kind == "water" ? "Agua: sus olas se mueven con la línea de tiempo" : kind == "terrain" ? "Terreno creado: cámbialo en Propiedades (otra forma, más árboles…)" : "Tela: ondea con la línea de tiempo; ponle una imagen desde el Editor de materiales");
        }

        // ------------------------------------------------------------------
        // Water

        void BuildWaterMesh(AtlasItem it)
        {
            var mf = it.GetComponent<MeshFilter>();
            if (mf.sharedMesh != null && mf.sharedMesh.name == "Agua") return;
            mf.sharedMesh = Grid("Agua", 96, 96, 30f, 30f, false);
        }

        static Mesh Grid(string name, int nx, int nz, float sx, float sz, bool vertical)
        {
            var v = new Vector3[(nx + 1) * (nz + 1)];
            var uv = new Vector2[v.Length];
            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                {
                    float u = x / (float)nx, w = z / (float)nz;
                    v[z * (nx + 1) + x] = vertical ? new Vector3(u * sx, -w * sz, 0f) : new Vector3((u - 0.5f) * sx, 0f, (w - 0.5f) * sz);
                    uv[z * (nx + 1) + x] = new Vector2(u, vertical ? 1f - w : w);
                }
            var t = new int[nx * nz * 6];
            int k = 0;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int a = z * (nx + 1) + x, b = a + 1, c = a + nx + 1, d = c + 1;
                    if (vertical) { t[k++] = a; t[k++] = b; t[k++] = c; t[k++] = b; t[k++] = d; t[k++] = c; }
                    else { t[k++] = a; t[k++] = c; t[k++] = b; t[k++] = b; t[k++] = c; t[k++] = d; }
                }
            var m = new Mesh { name = name };
            m.indexFormat = v.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.vertices = v;
            m.uv = uv;
            m.triangles = t;
            m.RecalculateNormals();
            m.RecalculateBounds();
            if (!vertical) m.bounds = new Bounds(Vector3.zero, new Vector3(sx, 4f, sz));
            return m;
        }

        void ApplyWater(AtlasItem it)
        {
            BuildWaterMesh(it);
            if (waterTemplate == null) waterTemplate = Resources.Load<Material>("AtlasWater");
            if (it.material == null || (waterTemplate != null && it.material.shader != waterTemplate.shader))
                it.material = new Material(waterTemplate != null ? waterTemplate : litTemplate);
            var m = it.material;
            Color deep, shallow;
            float h, len, clarity;
            switch (it.preset)
            {
                case "lake": deep = new Color(0.04f, 0.16f, 0.2f); shallow = new Color(0.15f, 0.45f, 0.4f); h = 0.08f; len = 3f; clarity = 0.6f; break;
                case "pool": deep = new Color(0.05f, 0.35f, 0.6f); shallow = new Color(0.3f, 0.8f, 0.95f); h = 0.03f; len = 1.6f; clarity = 0.9f; break;
                case "lava": deep = new Color(0.6f, 0.08f, 0.02f); shallow = new Color(1f, 0.55f, 0.1f); h = 0.06f; len = 2.5f; clarity = 0f; break;
                default: deep = new Color(0.02f, 0.14f, 0.28f); shallow = new Color(0.08f, 0.5f, 0.6f); h = 0.3f; len = 5f; clarity = 0.7f; break;
            }
            m.SetColor("_DeepColor", deep);
            m.SetColor("_ShallowColor", shallow);
            m.SetFloat("_WaveHeight", h * it.amount);
            m.SetFloat("_WaveLength", len);
            m.SetFloat("_WaveSpeed", it.preset == "lava" ? 0.25f : 1f);
            m.SetFloat("_Clarity", clarity);
            var r = it.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.enabled = it.visible;
        }

        // ------------------------------------------------------------------
        // Terrain

        void BuildTerrain(AtlasItem it)
        {
            const int N = 110;
            const float S = 40f;
            var rnd = new System.Random(it.seed);
            float ox = (float)rnd.NextDouble() * 1000f, oz = (float)rnd.NextDouble() * 1000f;
            var heights = new float[(N + 1) * (N + 1)];
            float amp, freq;
            switch (it.preset)
            {
                case "mountains": amp = 11f; freq = 0.045f; break;
                case "desert": amp = 2.2f; freq = 0.05f; break;
                case "island": amp = 4f; freq = 0.06f; break;
                default: amp = 3f; freq = 0.04f; break;
            }
            amp *= it.amount;
            for (int z = 0; z <= N; z++)
                for (int x = 0; x <= N; x++)
                {
                    float px = (x / (float)N - 0.5f) * S, pz = (z / (float)N - 0.5f) * S;
                    float h = 0f, a = 1f, f = freq, norm = 0f;
                    for (int o = 0; o < 5; o++)
                    {
                        float n = Mathf.PerlinNoise(ox + px * f, oz + pz * f);
                        if (it.preset == "mountains") n = 1f - Mathf.Abs(n * 2f - 1f);   // ridges
                        if (it.preset == "desert") n = Mathf.Sin((px * 0.25f + n * 4f)) * 0.5f + 0.5f;   // dunes
                        h += n * a; norm += a; a *= 0.5f; f *= 2.05f;
                    }
                    h = h / norm;
                    float edge = Mathf.Clamp01(1f - Mathf.Max(Mathf.Abs(px), Mathf.Abs(pz)) / (S * 0.5f));
                    if (it.preset == "island")
                    {
                        float rr = Mathf.Sqrt(px * px + pz * pz) / (S * 0.45f);
                        h = h * Mathf.Clamp01(1.2f - rr * rr) - 0.35f;
                    }
                    else h = h - 0.35f;
                    // The middle stays fairly flat to put things on.
                    float centre = Mathf.Clamp01(Mathf.Sqrt(px * px + pz * pz) / 6f);
                    heights[z * (N + 1) + x] = h * amp * Mathf.Lerp(0.25f, 1f, centre) * Mathf.Lerp(0.6f, 1f, Mathf.Sqrt(edge));
                }
            var v = new Vector3[heights.Length];
            var uv = new Vector2[heights.Length];
            for (int z = 0; z <= N; z++)
                for (int x = 0; x <= N; x++)
                {
                    int i = z * (N + 1) + x;
                    v[i] = new Vector3((x / (float)N - 0.5f) * S, heights[i], (z / (float)N - 0.5f) * S);
                    uv[i] = new Vector2(x / (float)N, z / (float)N);
                }
            var t = new int[N * N * 6];
            int k = 0;
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    int a = z * (N + 1) + x, b = a + 1, c = a + N + 1, d = c + 1;
                    t[k++] = a; t[k++] = c; t[k++] = b; t[k++] = b; t[k++] = c; t[k++] = d;
                }
            var mesh = new Mesh { name = "Terreno" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = v;
            mesh.uv = uv;
            mesh.triangles = t;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            it.GetComponent<MeshFilter>().sharedMesh = mesh;
            var nrm = mesh.normals;
            // Ground painted by height and slope.
            const int T = 256;
            var tex = new Texture2D(T, T, TextureFormat.RGB24, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px2 = new Color32[T * T];
            for (int y = 0; y < T; y++)
                for (int x = 0; x < T; x++)
                {
                    int gx = Mathf.Min(N, Mathf.RoundToInt(x / (T - 1f) * N)), gz = Mathf.Min(N, Mathf.RoundToInt(y / (T - 1f) * N));
                    int i = gz * (N + 1) + gx;
                    float h = heights[i], slope = 1f - Mathf.Clamp01(nrm[i].y);
                    float n = Mathf.PerlinNoise(x * 0.09f + ox, y * 0.09f + oz) * 0.25f - 0.12f;
                    Color c = GroundColor(it.preset, h / Mathf.Max(0.5f, amp), slope) * (1f + n);
                    px2[y * T + x] = c;
                }
            tex.SetPixels32(px2);
            tex.Apply(true, false);
            if (it.material == null || it.material.shader != litTemplate.shader) it.material = new Material(litTemplate);
            it.material.SetTexture("_BaseMap", tex);
            it.material.SetColor("_BaseColor", Color.white);
            it.material.SetFloat("_Smoothness", it.preset == "desert" ? 0.1f : 0.05f);
            it.material.SetFloat("_Metallic", 0f);
            it.GetComponent<MeshRenderer>().sharedMaterial = it.material;
            BuildTrees(it, v, nrm, N, rnd);
        }

        static Color GroundColor(string preset, float h, float slope)
        {
            Color sand = new Color(0.86f, 0.76f, 0.52f), grass = new Color(0.3f, 0.55f, 0.2f), dark = new Color(0.2f, 0.4f, 0.15f);
            Color rock = new Color(0.45f, 0.42f, 0.4f), snow = new Color(0.95f, 0.97f, 1f);
            switch (preset)
            {
                case "desert": return Color.Lerp(sand, new Color(0.75f, 0.58f, 0.36f), slope * 2f + h * 0.3f);
                case "mountains":
                    if (h > 0.32f && slope < 0.55f) return snow;
                    return Color.Lerp(Color.Lerp(dark, grass, h + 0.5f), rock, Mathf.Clamp01(slope * 2.4f + h * 0.8f));
                case "island":
                    if (h < -0.02f) return Color.Lerp(new Color(0.75f, 0.68f, 0.45f), sand, 0.5f);
                    if (h < 0.06f) return sand;
                    return Color.Lerp(grass, rock, Mathf.Clamp01(slope * 2.5f));
                default: return Color.Lerp(Color.Lerp(dark, grass, h + 0.6f), rock, Mathf.Clamp01(slope * 3f - 0.4f));
            }
        }

        /// Low-poly trees (trunk + two cones) where the ground is gentle.
        void BuildTrees(AtlasItem it, Vector3[] ground, Vector3[] normals, int N, System.Random rnd)
        {
            var old = it.transform.Find("Arboles");
            if (old != null) Destroy(old.gameObject);
            if (it.preset == "desert") return;
            int want = Mathf.RoundToInt(70 * Mathf.Clamp(it.amount, 0.2f, 3f));
            var tv = new List<Vector3>(); var tt = new List<int>();
            var lv = new List<Vector3>(); var lt = new List<int>();
            int placed = 0;
            for (int tries = 0; tries < want * 20 && placed < want; tries++)
            {
                int i = rnd.Next(ground.Length);
                Vector3 p = ground[i];
                if (normals[i].y < 0.85f || p.y < 0.08f) continue;
                if (it.preset == "mountains" && p.y > 6f) continue;
                if (new Vector2(p.x, p.z).magnitude < 3.5f) continue;   // the middle stays free
                float s = 0.7f + (float)rnd.NextDouble() * 0.8f;
                Cylinder(tv, tt, p, 0.09f * s, 0.7f * s, 6);
                Cone(lv, lt, p + Vector3.up * 0.5f * s, 0.6f * s, 1.2f * s, 8);
                Cone(lv, lt, p + Vector3.up * 1.05f * s, 0.42f * s, 0.9f * s, 8);
                placed++;
            }
            if (placed == 0) return;
            var go = new GameObject("Arboles");
            go.transform.SetParent(it.transform, false);
            var mesh = new Mesh { name = "Arboles" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var all = new List<Vector3>(tv); all.AddRange(lv);
            mesh.SetVertices(all);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(tt, 0);
            var lt2 = new List<int>();
            foreach (int x in lt) lt2.Add(x + tv.Count);
            mesh.SetTriangles(lt2, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var trunk = new Material(litTemplate); trunk.SetColor("_BaseColor", new Color(0.36f, 0.24f, 0.14f)); trunk.SetFloat("_Smoothness", 0.05f);
            var leaves = new Material(litTemplate); leaves.SetColor("_BaseColor", it.preset == "mountains" ? new Color(0.12f, 0.3f, 0.18f) : new Color(0.2f, 0.45f, 0.16f)); leaves.SetFloat("_Smoothness", 0.08f);
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { trunk, leaves };
        }

        static void Cylinder(List<Vector3> v, List<int> t, Vector3 at, float r, float h, int seg)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r), p1 = new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
                int b = v.Count;
                v.Add(at + p0); v.Add(at + p1); v.Add(at + p1 + Vector3.up * h); v.Add(at + p0 + Vector3.up * h);
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2);
            }
        }

        static void Cone(List<Vector3> v, List<int> t, Vector3 at, float r, float h, int seg)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                int b = v.Count;
                v.Add(at + new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r));
                v.Add(at + new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r));
                v.Add(at + Vector3.up * h);
                t.Add(b); t.Add(b + 2); t.Add(b + 1);
                // underside
                int c = v.Count;
                v.Add(at + new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r));
                v.Add(at + new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r));
                v.Add(at);
                t.Add(c); t.Add(c + 1); t.Add(c + 2);
            }
        }

        // ------------------------------------------------------------------
        // Cloth

        readonly Dictionary<AtlasItem, Vector3[]> clothBase = new Dictionary<AtlasItem, Vector3[]>();

        void BuildCloth(AtlasItem it)
        {
            var mf = it.GetComponent<MeshFilter>();
            string want = "Tela_" + it.preset;
            if (mf.sharedMesh != null && mf.sharedMesh.name == want && clothBase.ContainsKey(it)) return;
            float w = it.preset == "curtain" ? 2.4f : it.preset == "cape" ? 1.2f : 2f;
            float h = it.preset == "curtain" ? 2.6f : it.preset == "cape" ? 1.6f : 1.33f;
            var m = Grid(want, 28, 20, w, h, true);
            m.MarkDynamic();
            mf.sharedMesh = m;
            clothBase[it] = m.vertices;
        }

        /// The cloth waves for the timeline's time: a flag is held on its
        /// left edge, a curtain and a cape on their top edge.
        void WaveCloth(AtlasItem it, float t)
        {
            Vector3[] b;
            var mf = it.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !clothBase.TryGetValue(it, out b)) return;
            var v = new Vector3[b.Length];
            float w = 0f, h = 0f;
            foreach (var p in b) { w = Mathf.Max(w, p.x); h = Mathf.Max(h, -p.y); }
            float k = Mathf.Clamp(it.amount, 0.1f, 3f);
            float sx = it.seed * 0.137f;
            for (int i = 0; i < b.Length; i++)
            {
                Vector3 p = b[i];
                float u = w > 0f ? p.x / w : 0f, d = h > 0f ? -p.y / h : 0f;
                float hold, phase;
                if (it.preset == "flag") { hold = u; phase = u * 7f - t * 6f; }
                else if (it.preset == "curtain") { hold = d * d; phase = u * 9f - t * 1.6f + d * 2f; }
                else { hold = d; phase = u * 4f - t * 2.4f + d * 3f; }
                float wave = Mathf.Sin(phase + sx) * 0.6f + Mathf.Sin(phase * 1.7f + 1.3f + sx) * 0.3f;
                float flutter = (Mathf.PerlinNoise(u * 3f + t * 1.5f + sx, d * 3f) - 0.5f) * 0.5f;
                float amp = (it.preset == "flag" ? 0.22f : it.preset == "curtain" ? 0.12f : 0.18f) * k;
                p.z += (wave + flutter) * amp * hold;
                if (it.preset == "flag") p.y -= hold * hold * 0.06f * k;   // a little droop
                if (it.preset == "cape") p.z -= d * d * 0.25f;            // hangs backwards
                v[i] = p;
            }
            var m = mf.sharedMesh;
            m.vertices = v;
            m.RecalculateNormals();
            m.RecalculateBounds();
        }

        // ------------------------------------------------------------------

        void ApplyWorldLook(AtlasItem it)
        {
            if (it.kind == "water") ApplyWater(it);
            else if (it.kind == "terrain")
            {
                string sig = it.preset + "|" + it.seed + "|" + it.amount;
                if (it.GetComponent<MeshFilter>().sharedMesh == null || worldSig.ContainsKey(it) == false || worldSig[it] != sig)
                {
                    BuildTerrain(it);
                    worldSig[it] = sig;
                }
                foreach (var r in it.GetComponentsInChildren<Renderer>(true)) r.enabled = it.visible;
            }
            UpdateWorld();
        }

        readonly Dictionary<AtlasItem, string> worldSig = new Dictionary<AtlasItem, string>();

        void UpdateWorld()
        {
            float t = frame / Mathf.Max(1f, fps);
            foreach (var it in items)
            {
                if (it == null) continue;
                if (it.kind == "water" && it.material != null && it.material.HasProperty("_WaveTime")) it.material.SetFloat("_WaveTime", t);
                else if (it.kind == "flag") { BuildCloth(it); WaveCloth(it, t); }
            }
        }

        void BuildWorldSection(AtlasItem it)
        {
            string[] presets = it.kind == "water" ? WaterPresets : it.kind == "terrain" ? TerrainPresets : ClothPresets;
            string[] names = it.kind == "water" ? WaterNames : it.kind == "terrain" ? TerrainNames : ClothNames;
            var box = Sec(it.kind == "water" ? "Agua" : it.kind == "terrain" ? "Terreno" : "Tela", "sparkle");
            var g = AtlasTheme.Grid(box, 2, InnerWidth, 38f);
            for (int i = 0; i < presets.Length; i++)
            {
                string p = presets[i];
                AtlasTheme.Chip(g, names[i], () => { it.preset = p; if (it.kind == "flag") clothBase.Remove(it); ApplyLook(it); Commit(true); RefreshProps(); }, it.preset == p);
            }
            string what = it.kind == "water" ? "Olas" : it.kind == "terrain" ? "Altura" : "Viento";
            AtlasTheme.SliderRow(box, what, 0.1f, 3f, it.amount, "0.0", x => { it.amount = x; if (it.kind != "terrain") ApplyLook(it); }, () => { ApplyLook(it); Commit(true); });
            if (it.kind != "water")
                AtlasTheme.TextButton(box, "undo", it.kind == "terrain" ? "Otro terreno" : "Otra forma de ondear", () => { it.seed = Random.Range(1, 99999); ApplyLook(it); Commit(true); }, false, 42f, 15);
            Hint(box, it.kind == "water" ? "Las olas se mueven con la línea de tiempo y reflejan el cielo; escálala para cubrir más."
                : it.kind == "terrain" ? "Escálalo para hacerlo más grande. El centro queda casi llano para poner personajes."
                : "Ponle una imagen (bandera, estampado) en el Editor de materiales de abajo.");
        }
    }
}
