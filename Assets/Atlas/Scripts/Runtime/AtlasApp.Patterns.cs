// ATLAS 5D (Unity) - shading, first step: textures made by the app
// (gradient, noise, clouds, checks, stripes, dots, wood, marble, bricks,
// tiles) in two colours, repeating seamlessly; the same pattern as relief
// (normal map); mixing the texture with another picture through the pattern
// (a mask); and copying / pasting a whole material between objects.
using System;
using System.IO;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        static readonly string[] PatternNames = { "Degradado", "Ruido", "Nubes", "Cuadros", "Rayas", "Puntos", "Madera", "Mármol", "Ladrillos", "Baldosas" };
        int patKind = 1;
        Color patA = new Color(0.85f, 0.85f, 0.88f), patB = new Color(0.25f, 0.27f, 0.32f);
        float patScale = 4f;
        const int PatRes = 512;

        // Copy / paste of a whole material (this session).
        MatState matClipboard;

        void BuildPatternControls(Transform c)
        {
            float w = RightWidth;
            var pl = AtlasTheme.Label(c, "TEXTURAS GENERADAS", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Le(pl.gameObject, 22f);
            var g = AtlasTheme.Grid(c, 5, w, 34f, 4f);
            for (int i = 0; i < PatternNames.Length; i++)
            {
                int k = i;
                AtlasTheme.Chip(g, PatternNames[i], () => { patKind = k; BuildMatEditor(); }, patKind == i);
            }
            // The two colours: tap a swatch for each.
            ColorRow(c, "Color 1", patA, col => { patA = col; BuildMatEditor(); });
            ColorRow(c, "Color 2", patB, col => { patB = col; BuildMatEditor(); });
            AtlasTheme.SliderRow(c, "Tamaño del dibujo (veces que se repite)", 1f, 16f, patScale, "0", v => patScale = Mathf.Round(v), null);
            var bg = AtlasTheme.Grid(c, 2, w, 42f, 6f);
            AtlasTheme.TextButton(bg, "image", "Usar como textura", () => ApplyPattern("base"), true, 42f, 14);
            AtlasTheme.TextButton(bg, "image", "Usar como relieve", () => ApplyPattern("bump"), false, 42f, 14);
            AtlasTheme.TextButton(bg, "image", "Mezclar con otra imagen…", MixWithPicture, false, 42f, 14);
            AtlasTheme.TextButton(bg, "image", "Usar como brillo propio", () => ApplyPattern("emission"), false, 42f, 14);
            var ml = AtlasTheme.Label(c, "COPIAR MATERIAL", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Le(ml.gameObject, 22f);
            var cg = AtlasTheme.Grid(c, 2, w, 42f, 6f);
            AtlasTheme.TextButton(cg, "copy", "Copiar material", () => { matClipboard = ReadMat(); Toast("Material copiado: elige otro objeto (o pieza) y pulsa «Pegar material»"); }, false, 42f, 14);
            AtlasTheme.TextButton(cg, "paste", "Pegar material", () =>
            {
                if (matClipboard == null) { Toast("Primero copia un material"); return; }
                var src = matClipboard;
                ChangeMat(st =>
                {
                    st.color = src.color; st.metallic = src.metallic; st.smooth = src.smooth; st.emission = src.emission;
                    st.opacity = src.opacity; st.bump = src.bump; st.tex = src.tex; st.normal = src.normal; st.emis = src.emis;
                });
                Commit(true);
                BuildMatEditor();
                Toast("Material pegado");
            }, false, 42f, 14);
        }

        void ColorRow(Transform c, string label, Color current, Action<Color> pick)
        {
            var row = AtlasTheme.Rect(c, label).gameObject;
            AtlasTheme.Row(row, 5f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft);
            AtlasTheme.Le(row, 30f);
            var t = AtlasTheme.Label(row.transform, label, 14, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t.gameObject, 30f, 70f);
            var now = AtlasTheme.Box(row.transform, "Actual", current, 8);
            AtlasTheme.Le(now.gameObject, 30f, 40f);
            foreach (var c0 in Swatches)
            {
                Color cc = c0;
                var sw = AtlasTheme.Box(row.transform, "Color", cc, 6);
                AtlasTheme.Le(sw.gameObject, 26f, 22f);
                sw.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(() => pick(cc));
            }
            // «Usar el del material»: the colour chosen above.
            var use = AtlasTheme.Chip(row.transform, "Este", () => pick(ReadMat().color));
            AtlasTheme.Le(use.gameObject, 30f, 56f);
        }

        /// The pattern (0..1) at a point of the picture; it repeats at the edges.
        float PatternValue(int kind, float u, float v, float sc)
        {
            float x = u * sc, y = v * sc;
            int period = Mathf.Max(1, Mathf.RoundToInt(sc));
            switch (kind)
            {
                case 0: return Mathf.SmoothStep(0f, 1f, v);
                case 1: return Fbm(x, y, period, 5);
                case 2: return Mathf.SmoothStep(0.35f, 0.75f, Fbm(x, y, period, 6));
                case 3: return ((Mathf.FloorToInt(x) + Mathf.FloorToInt(y)) & 1) == 0 ? 0f : 1f;
                case 4: return Mathf.SmoothStep(0.45f, 0.55f, Mathf.Abs(Frac(x + y) - 0.5f) * 2f);
                case 5:
                {
                    float fx = Frac(x) - 0.5f, fy = Frac(y) - 0.5f;
                    return 1f - Mathf.SmoothStep(0.26f, 0.3f, Mathf.Sqrt(fx * fx + fy * fy));
                }
                case 6:
                {
                    // Grain lines across, wobbled by noise (they repeat at the edges).
                    float d = y + Fbm(x, y, period, 3) * 1.5f;
                    float ring = Frac(d * 2f);
                    return Mathf.Pow(ring, 3f) * 0.85f + Fbm(x * 4f, y * 0.5f, period * 4, 2) * 0.15f;
                }
                case 7:
                {
                    float t = Mathf.Sin((x + y) * Mathf.PI + Fbm(x, y, period, 5) * 7f);
                    return Mathf.Pow(Mathf.Abs(t), 0.35f);
                }
                case 8:
                {
                    float by = y * 2f;
                    float bx = x + (Mathf.FloorToInt(by) % 2 == 0 ? 0f : 0.5f);
                    float gx = Frac(bx), gy = Frac(by);
                    bool mortar = gx < 0.04f || gx > 0.96f || gy < 0.08f || gy > 0.92f;
                    return mortar ? 1f : Fbm(x * 3f, y * 3f, period * 3, 2) * 0.35f;
                }
                default:
                {
                    float gx = Frac(x), gy = Frac(y);
                    bool grout = gx < 0.035f || gx > 0.965f || gy < 0.035f || gy > 0.965f;
                    return grout ? 1f : Fbm(x * 2f, y * 2f, period * 2, 2) * 0.2f;
                }
            }
        }

        static float Frac(float v) { return v - Mathf.Floor(v); }

        static float Hash2(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
            }
        }

        /// Value noise that repeats every `period` cells (seamless textures).
        static float Noise(float x, float y, int period)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int p = Mathf.Max(1, period);
            Func<int, int> W = a => ((a % p) + p) % p;
            float a00 = Hash2(W(x0), W(y0)), a10 = Hash2(W(x0 + 1), W(y0)), a01 = Hash2(W(x0), W(y0 + 1)), a11 = Hash2(W(x0 + 1), W(y0 + 1));
            return Mathf.Lerp(Mathf.Lerp(a00, a10, fx), Mathf.Lerp(a01, a11, fx), fy);
        }

        static float Fbm(float x, float y, int period, int octaves)
        {
            float v = 0f, a = 0.5f, sum = 0f;
            for (int o = 0; o < octaves; o++)
            {
                v += a * Noise(x, y, period);
                sum += a;
                x *= 2f; y *= 2f; period *= 2; a *= 0.5f;
            }
            return v / Mathf.Max(1e-5f, sum);
        }

        Color32[] MakePattern(out float[] mask)
        {
            var px = new Color32[PatRes * PatRes];
            mask = new float[PatRes * PatRes];
            for (int y = 0; y < PatRes; y++)
                for (int x = 0; x < PatRes; x++)
                {
                    float t = Mathf.Clamp01(PatternValue(patKind, x / (float)PatRes, y / (float)PatRes, patScale));
                    mask[y * PatRes + x] = t;
                    px[y * PatRes + x] = Color.Lerp(patA, patB, t);
                }
            return px;
        }

        /// Saves pixels as a PNG in the project and returns its file name.
        string SavePatternPng(Color32[] px, string name)
        {
            var t = new Texture2D(PatRes, PatRes, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            t.Apply(false);
            string tmp = Path.Combine(Application.temporaryCachePath, name + ".png");
            File.WriteAllBytes(tmp, t.EncodeToPNG());
            Destroy(t);
            string file = store.AddTextureFile(tmp);
            try { File.Delete(tmp); } catch (Exception) { }
            return file;
        }

        void ApplyPattern(string slot)
        {
            if (store == null || meItem == null) return;
            try
            {
                float[] mask;
                var px = MakePattern(out mask);
                string name = PatternNames[patKind].ToLowerInvariant();
                if (slot == "bump")
                {
                    // Black and white (the pattern itself) → relief.
                    var bw = new Color32[px.Length];
                    for (int i = 0; i < px.Length; i++) { byte g = (byte)(mask[i] * 255f); bw[i] = new Color32(g, g, g, 255); }
                    string file = SavePatternPng(bw, "relieve_" + name);
                    string made = BumpToNormal(file);
                    if (made == null) { Toast("No se pudo crear el relieve", true); return; }
                    ChangeMat(st => { st.normal = made; if (st.bump <= 0.01f) st.bump = 1f; });
                    Toast("Relieve «" + PatternNames[patKind] + "» puesto (Fuerza del relieve para ajustarlo)");
                }
                else
                {
                    string file = SavePatternPng(px, "patron_" + name);
                    if (slot == "base") ChangeMat(st => { st.tex = file; st.color = Color.white; });
                    else ChangeMat(st => { st.emis = file; if (st.emission < 1f) st.emission = 1f; });
                    Toast("Textura «" + PatternNames[patKind] + "» puesta");
                }
                Commit(true);
                BuildMatEditor();
            }
            catch (Exception e) { Toast("No se pudo crear la textura: " + e.Message, true); }
        }

        /// The current texture (or colour) and another picture, mixed with
        /// the pattern as the mask: 0 shows the first, 1 the other.
        void MixWithPicture()
        {
            if (store == null || meItem == null || NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path) || store == null || meItem == null) return;
                try
                {
                    var other = new Texture2D(2, 2);
                    if (!other.LoadImage(File.ReadAllBytes(path))) { Destroy(other); Toast("Esa imagen no se puede leer (usa PNG o JPG)", true); return; }
                    var s = ReadMat();
                    Texture2D first = null;
                    if (!string.IsNullOrEmpty(s.tex) && File.Exists(store.TexturePath(s.tex)))
                    {
                        first = new Texture2D(2, 2);
                        if (!first.LoadImage(File.ReadAllBytes(store.TexturePath(s.tex)))) { Destroy(first); first = null; }
                    }
                    float[] mask;
                    MakePattern(out mask);
                    var px = new Color32[PatRes * PatRes];
                    for (int y = 0; y < PatRes; y++)
                        for (int x = 0; x < PatRes; x++)
                        {
                            float u = (x + 0.5f) / PatRes, v = (y + 0.5f) / PatRes;
                            Color a = first != null ? first.GetPixelBilinear(u, v) * s.color : s.color;
                            Color b = other.GetPixelBilinear(u, v);
                            Color m = Color.Lerp(a, b, mask[y * PatRes + x]);
                            m.a = 1f;
                            px[y * PatRes + x] = m;
                        }
                    Destroy(other);
                    if (first != null) Destroy(first);
                    string file = SavePatternPng(px, "mezcla");
                    ChangeMat(st => { st.tex = file; st.color = Color.white; });
                    Commit(true);
                    BuildMatEditor();
                    Toast("Mezcla hecha con el patrón «" + PatternNames[patKind] + "» como máscara");
                }
                catch (Exception e) { Toast("No se pudo mezclar: " + e.Message, true); }
            }, new string[] { "image/*" });
        }
    }
}
