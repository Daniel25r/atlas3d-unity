// ATLAS 3D (Unity) - materials of imported models. glTFast normally uses
// its own shaders, which a phone build may not contain: the material then
// failed and the model came in without its textures. ATLAS builds every
// material itself from the URP Lit materials that are always in the app:
// colour, texture, metal, roughness, glow, normal map, transparency and
// two-sided faces.
using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using GLTFast.Schema;
using UnityEngine;

namespace Atlas
{
    public class AtlasMaterialGenerator : IMaterialGenerator
    {
        readonly UnityEngine.Material lit, transparent, litNormal, cutout, cutoutNormal, transparentNormal;

        public AtlasMaterialGenerator()
        {
            lit = Resources.Load<UnityEngine.Material>("AtlasLit");
            transparent = Resources.Load<UnityEngine.Material>("AtlasLitTransparent");
            litNormal = Resources.Load<UnityEngine.Material>("AtlasLitNormal");
            cutout = Resources.Load<UnityEngine.Material>("AtlasLitCutout");
            cutoutNormal = Resources.Load<UnityEngine.Material>("AtlasLitCutoutNormal");
            transparentNormal = Resources.Load<UnityEngine.Material>("AtlasLitTransparentNormal");
            if (transparent == null) transparent = lit;
            if (litNormal == null) litNormal = lit;
        }

        public UnityEngine.Material GetDefaultMaterial(bool pointsSupport = false)
        {
            var m = new UnityEngine.Material(lit);
            m.name = "Material";
            m.SetColor("_BaseColor", new Color(0.8f, 0.8f, 0.8f, 1f));
            m.SetFloat("_Smoothness", 0.4f);
            return m;
        }

        static Texture2D Tex(IGltfReadable gltf, TextureInfoBase info)
        {
            if (info == null || info.index < 0) return null;
            try
            {
                // The texture as glTFast made it: with its sampler (sharp
                // pixel art stays sharp, repeat / clamp as the file says).
                var made = gltf.GetTexture(info.index);
                if (made != null) return made;
                var t = gltf.GetSourceTexture(info.index);
                if (t == null) return null;
                int img = t.GetImageIndex();
                return img >= 0 ? gltf.GetImage(img) : null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        public UnityEngine.Material GenerateMaterial(MaterialBase g, IGltfReadable gltf, bool pointsSupport = false)
        {
            if (g == null) return GetDefaultMaterial();
            var mode = g.GetAlphaMode();
            // MASK (cut-out hair, lace, leaves) is solid with holes, never
            // blended: blended parts let the inside of the head show through.
            bool mask = mode == MaterialBase.AlphaMode.Mask;
            bool see = mode == MaterialBase.AlphaMode.Blend;
            Texture2D normal = Tex(gltf, g.NormalTexture);
            // Opaque: the normal-map variant always, so relief can be added later.
            // Cut-out and see-through parts keep their relief too (their own
            // templates, so the shader variant is in the build).
            var tmpl = see ? (normal != null && transparentNormal != null ? transparentNormal : transparent)
                     : mask && cutout != null ? (normal != null && cutoutNormal != null ? cutoutNormal : cutout)
                     : litNormal;
            var m = new UnityEngine.Material(tmpl);
            m.name = string.IsNullOrEmpty(g.name) ? "Material" : g.name;

            var pbr = g.PbrMetallicRoughness;
            Color baseColor = pbr != null ? pbr.BaseColor : Color.white;
            var ext = g.Extensions;
            // Older files (many from Sketchfab) keep their colour and texture
            // in «specular-glossiness»: without reading it they came in grey
            // or dark, with no texture.
            var sg = ext != null ? ext.KHR_materials_pbrSpecularGlossiness : null;
            TextureInfoBase baseInfo = pbr != null ? pbr.BaseColorTexture : null;
            bool fromSg = sg != null && (baseInfo == null || baseInfo.index < 0);
            if (fromSg)
            {
                baseColor = sg.DiffuseColor;
                baseInfo = sg.diffuseTexture;
            }
            bool unlit = ext != null && ext.KHR_materials_unlit != null;
            // glTF colours are linear; SetColor expects sRGB.
            Color c = baseColor.gamma;
            c.a = baseColor.a;
            m.SetColor("_BaseColor", c);
            Texture2D baseTex = Tex(gltf, baseInfo);
            // Pixel art (Minecraft, Blockbench, retro): small textures stay
            // sharp instead of turning into a blur.
            if (baseTex != null && baseTex.width <= 128 && baseTex.height <= 128) baseTex.filterMode = FilterMode.Point;
            m.SetTexture("_BaseMap", baseTex);
            // Repeated / moved textures (KHR_texture_transform): without it
            // a tiled texture showed only a stretched corner of itself.
            SetTransform(m, baseInfo != null && baseInfo.index >= 0 ? baseInfo : g.EmissiveTexture);
            float metal = pbr != null ? pbr.metallicFactor : 0f;
            float rough = pbr != null ? pbr.roughnessFactor : 1f;
            // With a metal/roughness texture the factors are multipliers
            // (usually 1, «all metal, all rough»): the texture is not read
            // by the phone material, so a soft, non-metal look is used
            // instead of a mirror-like shine.
            if (pbr != null && pbr.MetallicRoughnessTexture != null && pbr.MetallicRoughnessTexture.index >= 0)
            {
                metal = 0f;
                rough = Mathf.Max(rough, 0.72f);
            }
            // Specular-glossiness has no metal: the empty metal/rough block
            // some exporters add (metal 1 by default) made it look black.
            if (fromSg) { metal = 0f; rough = Mathf.Max(0.5f, 1f - sg.glossinessFactor); }
            // Painted surfaces never look like polished chrome by accident.
            if (baseTex != null) metal = Mathf.Min(metal, 0.3f);
            // Imported characters looked «oiled»: most exporters write a
            // medium roughness (Blender: 0.5) that on a phone's lighting
            // shines like plastic. A soft, matte finish by default; real
            // metals keep some shine. «Aspecto del modelo» can raise it.
            float smooth = Mathf.Clamp01(1f - rough);
            smooth = Mathf.Min(smooth, metal >= 0.5f ? 0.5f : 0.18f);
            m.SetFloat("_Metallic", Mathf.Clamp01(metal));
            m.SetFloat("_Smoothness", smooth);

            Color e = g.Emissive;
            Texture2D emisTex = Tex(gltf, g.EmissiveTexture);
            if (emisTex != null && e.maxColorComponent <= 0f) e = Color.white;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", e);
            if (emisTex != null && m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emisTex);

            if (normal != null && m.HasProperty("_BumpMap") && (!see || transparentNormal != null))
            {
                m.EnableKeyword("_NORMALMAP");
                m.SetTexture("_BumpMap", normal);
                var nt = g.NormalTexture as NormalTextureInfoBase;
                m.SetFloat("_BumpScale", nt != null ? nt.scale : 1f);
            }
            if (g.doubleSided && m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            // «Unlit» (flat colour, no light): its colour as it is.
            if (unlit)
            {
                float a = c.a;
                m.SetColor("_BaseColor", new Color(0f, 0f, 0f, a));
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0f);
                m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
                if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
                m.SetColor("_EmissionColor", new Color(c.r, c.g, c.b));
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", baseTex);
            }
            if (mask) MatUtil.SetCutout(m, Mathf.Clamp(g.alphaCutoff, 0.05f, 0.95f));
            else if (see)
            {
                MatUtil.SetTransparent(m, true);
                // Many characters are exported «blended» although they are
                // solid (alpha 1): they keep writing depth, so faces behind
                // (the back of the head, the inside of gloves) stay hidden.
                if (baseColor.a >= 0.99f) m.SetFloat("_ZWrite", 1f);
            }
            return m;
        }

        /// glTF's texture transform as Unity's tiling / offset (V flipped,
        /// as glTFast does; rotation is rare and left out).
        static void SetTransform(UnityEngine.Material m, TextureInfoBase info)
        {
            if (info == null) return;
            TextureTransform tt = null;
            try { tt = info.Extensions != null ? info.Extensions.KHR_texture_transform : null; }
            catch (System.Exception) { tt = null; }
            if (tt == null) return;
            float sx = tt.scale != null && tt.scale.Length > 1 ? tt.scale[0] : 1f;
            float sy = tt.scale != null && tt.scale.Length > 1 ? tt.scale[1] : 1f;
            float ox = tt.offset != null && tt.offset.Length > 1 ? tt.offset[0] : 0f;
            float oy = tt.offset != null && tt.offset.Length > 1 ? tt.offset[1] : 0f;
            var st = new Vector2(sx, sy);
            var off = new Vector2(ox, 1f - oy - sy);
            if (m.HasProperty("_BaseMap")) { m.SetTextureScale("_BaseMap", st); m.SetTextureOffset("_BaseMap", off); }
        }

        public void SetLogger(ICodeLogger logger)
        {
        }
    }
}
