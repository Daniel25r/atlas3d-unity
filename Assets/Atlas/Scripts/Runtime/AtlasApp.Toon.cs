// ATLAS 3D (Unity) - «Estilo dibujo»: anime / cartoon look for any object
// (cel shading in bands + outline), or for the whole scene at once.
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        Material toonTemplate;

        bool CanToon(AtlasItem it)
        {
            return it != null && !it.IsHelper && it.kind != "fx" && it.kind != "water" && it.kind != "video" && it.kind != "text";
        }

        /// After the materials are made: each one swapped for its toon copy.
        void ApplyToon(AtlasItem it)
        {
            if (it == null || !it.toon || !CanToon(it)) return;
            if (toonTemplate == null) toonTemplate = Resources.Load<Material>("AtlasToon");
            if (toonTemplate == null) return;
            foreach (var r in it.GetComponentsInChildren<Renderer>(true))
            {
                if (r is LineRenderer || r is ParticleSystemRenderer || r.gameObject.name == "Volumen" || r.gameObject.name == "Marca") continue;
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = ToonOf(it, src[i]);
                r.sharedMaterials = dst;
            }
        }

        Material ToonOf(AtlasItem it, Material src)
        {
            if (src == null) return null;
            if (src.shader == toonTemplate.shader) return src;
            Material m;
            if (!it.toonMats.TryGetValue(src, out m) || m == null)
            {
                m = new Material(toonTemplate);
                it.toonMats[src] = m;
            }
            if (src.HasProperty("_BaseMap"))
            {
                m.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
                m.SetTextureScale("_BaseMap", src.GetTextureScale("_BaseMap"));
                m.SetTextureOffset("_BaseMap", src.GetTextureOffset("_BaseMap"));
            }
            Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white;
            m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 1f));
            // The relief goes along too (it was lost with the anime style on).
            var bump = src.HasProperty("_BumpMap") && src.IsKeywordEnabled("_NORMALMAP") ? src.GetTexture("_BumpMap") : null;
            bool hasBump = bump != null && bump != TextureTune.Flat;
            m.SetTexture("_BumpMap", hasBump ? bump : null);
            m.SetFloat("_BumpScale", hasBump ? (src.HasProperty("_BumpScale") ? src.GetFloat("_BumpScale") : 1f) : 0f);
            if (hasBump) EnsureTangentsFor(it, src);
            bool cut = src.IsKeywordEnabled("_ALPHATEST_ON") || src.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
            m.SetFloat("_Cutoff", cut ? 0.4f : 0f);
            m.SetColor("_EmissionColor", src.IsKeywordEnabled("_EMISSION") && src.HasProperty("_EmissionColor") ? src.GetColor("_EmissionColor") : Color.black);
            m.SetFloat("_Steps", Mathf.Clamp(it.toonSteps, 1, 5));
            m.SetFloat("_OutlineWidth", it.outline);
            m.SetColor("_OutlineColor", it.outlineColor);
            m.SetColor("_ShadeColor", Color.Lerp(new Color(0.55f, 0.5f, 0.72f), new Color(0.75f, 0.72f, 0.8f), Mathf.Clamp01(it.toonSoft)));
            return m;
        }

        static readonly Color[] OutlineColors = { new Color(0.05f, 0.04f, 0.06f), new Color(0.22f, 0.12f, 0.07f), new Color(0.07f, 0.1f, 0.3f), new Color(1f, 1f, 1f), new Color(0.9f, 0.1f, 0.15f) };
        static readonly string[] OutlineNames = { "Negro", "Marrón", "Azul oscuro", "Blanco", "Rojo" };

        void BuildToonSection(AtlasItem it)
        {
            if (!CanToon(it)) return;
            var box = Sec("Estilo dibujo (anime)", "edit", it.toon);
            AtlasTheme.Switch(box, "Sombreado de dibujo y contorno", it.toon, on => { it.toon = on; ApplyLook(it); Commit(true); RefreshProps(); });
            if (!it.toon) { Hint(box, "Luz y sombra en bandas planas con un contorno negro, como en el anime o los dibujos animados."); return; }
            AtlasTheme.SliderRow(box, "Contorno", 0f, 5f, it.outline, "0.0", x => { it.outline = x; ApplyLook(it); }, () => Commit(true));
            var g = AtlasTheme.Grid(box, 3, InnerWidth, 34f, 4f);
            for (int i = 0; i < OutlineColors.Length; i++)
            {
                var c = OutlineColors[i];
                bool on = (it.outlineColor - c).maxColorComponent < 0.02f && (c - it.outlineColor).maxColorComponent < 0.02f;
                AtlasTheme.Chip(g, OutlineNames[i], () => { it.outlineColor = c; ApplyLook(it); Commit(true); RefreshProps(); }, on);
            }
            var g2 = AtlasTheme.Grid(box, 3, InnerWidth, 34f, 4f);
            foreach (int n0 in new[] { 1, 2, 3 })
            {
                int n = n0;
                AtlasTheme.Chip(g2, n == 1 ? "1 sombra" : n + " sombras", () => { it.toonSteps = n; ApplyLook(it); Commit(true); RefreshProps(); }, it.toonSteps == n);
            }
            AtlasTheme.SliderRow(box, "Sombra suave", 0f, 1f, it.toonSoft, "0.00", x => { it.toonSoft = x; ApplyLook(it); }, () => Commit(true));
        }

        /// Scene inspector: the whole scene drawn like a cartoon (or not).
        void ToonAll(bool on)
        {
            int n = 0;
            foreach (var it in items)
            {
                if (!CanToon(it)) continue;
                it.toon = on;
                ApplyLook(it);
                n++;
            }
            Commit(true);
            RefreshProps();
            Toast(on ? "Estilo dibujo en " + n + " objetos" : "Estilo dibujo quitado");
        }
    }
}
