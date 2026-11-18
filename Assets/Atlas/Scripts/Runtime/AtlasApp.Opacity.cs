// ATLAS 3D (Unity) - opacity of any object (shapes, pictures, texts, video
// and imported models): at 0 the object disappears completely (no shadow
// either), and it can be animated with keys to fade things in and out.
// Also «Emisión pura»: the surface shows its colour as it is, like Blender's
// Emission, without light or shadow on it.
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        static bool HasOpacity(AtlasItem it)
        {
            return it != null && !it.IsLight && it.kind != "camera" && it.kind != "fx" && it.kind != "water" && it.kind != "terrain";
        }

        /// Shows the item's opacity: materials, and nothing drawn at 0.
        /// `materials` false: only which parts are drawn (texts paint their
        /// own letters).
        void ApplyOpacity(AtlasItem it, bool materials = true)
        {
            if (!HasOpacity(it)) return;
            float op = Mathf.Clamp01(it.opacity);
            bool gone = op < 0.004f;
            foreach (var r in it.GetComponentsInChildren<Renderer>(true))
            {
                if (r is LineRenderer || r is ParticleSystemRenderer || r.gameObject.name == "Volumen") continue;
                r.enabled = it.visible && !gone;
                // A see-through object hardly casts a shadow.
                r.shadowCastingMode = it.noShadow || env.shadowsOff || op < 0.5f
                    ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            if (!materials) return;
            if (it.kind == "model") { ModelOpacity(it, op); return; }
            var m = it.material;
            if (m != null && m.HasProperty("_BaseColor"))
            {
                var c = m.GetColor("_BaseColor");
                c.a = op;
                m.SetColor("_BaseColor", c);
                if (it.shader != "aero") MatUtil.SetTransparent(m, op < 0.995f);
            }
        }

        /// Imported models: every material fades; back at 1 each one is
        /// exactly as the file made it (solid, cut-out or see-through).
        void ModelOpacity(AtlasItem it, float op)
        {
            foreach (var m in it.modelMats)
            {
                if (m == null) continue;
                MatOriginal o;
                if (!it.matOriginals.TryGetValue(m, out o)) continue;
                if (op >= 0.995f)
                {
                    if (o.faded) { o.RestoreState(m); o.faded = false; }
                }
                else
                {
                    MatUtil.SetTransparent(m, true);
                    o.faded = true;
                }
                if (m.HasProperty("_BaseColor"))
                {
                    var c = m.GetColor("_BaseColor");
                    c.a = o.color.a * op;
                    m.SetColor("_BaseColor", c);
                }
            }
        }

        /// The opacity slider with its key button (any object that has it).
        void OpacityControls(Transform box, AtlasItem it)
        {
            AtlasTheme.SliderRow(box, "Opacidad", 0f, 1f, it.opacity, "0.00", x => { it.opacity = x; ApplyOpacityNow(it); }, () =>
            {
                // Already animated: the change becomes a key by itself.
                if (autoKey || HasTrack(it, "O")) { it.SetKey(Mathf.RoundToInt(frame), AtlasItem.ChOpacity, new List<string>()); RefreshKeys(); }
                Commit(true);
            });
            AtlasTheme.TextButton(box, "key", "Clave de opacidad (fotograma " + Mathf.RoundToInt(frame) + ")", () =>
            {
                it.SetKey(Mathf.RoundToInt(frame), AtlasItem.ChOpacity, new List<string>());
                Commit(true);
                RefreshKeys();
                RefreshProps();
                Toast("Opacidad " + Mathf.RoundToInt(it.opacity * 100f) + "% guardada en el fotograma " + Mathf.RoundToInt(frame) + ". Pon otra clave en otro fotograma para que aparezca o desaparezca poco a poco.");
            }, false, 40f, 14);
        }

        static bool HasTrack(AtlasItem it, string track)
        {
            foreach (var k in it.keys) if (k.Has(track)) return true;
            return false;
        }

        void ApplyOpacityNow(AtlasItem it)
        {
            if (it.kind == "text") { ApplyText3D(it); ApplyOpacity(it, false); }
            else ApplyOpacity(it);
        }

        // ------------------------------------------------------------------
        // «Emisión pura»

        /// The surface is its colour (or texture) as it is: no light, no
        /// shadow, no reflections on it.
        static void MakePureEmission(Material m, Color col, float strength, Texture map)
        {
            if (m == null) return;
            float a = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").a : 1f;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0f, 0f, 0f, a));
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", map);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", new Color(col.r, col.g, col.b) * Mathf.Max(1f, strength));
        }

        static void UndoPureEmission(Material m)
        {
            if (m == null) return;
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 1f);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 1f);
            m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }
    }
}
