// ATLAS 3D (Unity) - «Texto 3D» for titles and credits: a text in the
// scene that moves, turns, scales and animates like any object and comes
// out in renders. Styles: flat, raised 3D (layers behind it) and neon
// (glow around it).
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        public static readonly string[] TextStyles = { "flat", "raised", "neon" };
        public static readonly string[] TextStyleNames = { "Plano", "Relieve 3D", "Neón" };

        GameObject MakeText3D()
        {
            var go = new GameObject();
            // Invisible plate: what a tap finds.
            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "Placa";
            Destroy(plate.GetComponent<Collider>());
            plate.transform.SetParent(go.transform, false);
            plate.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var cgo = new GameObject("Lienzo");
            cgo.transform.SetParent(go.transform, false);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)cgo.transform;
            rt.sizeDelta = new Vector2(2000f, 400f);
            rt.localScale = Vector3.one * 0.005f;
            return go;
        }

        void AddText3D()
        {
            CloseMenus();
            if (store == null) return;
            count++;
            var d = new ItemData
            {
                v = 3,
                kind = "text",
                name = "Texto",
                position = new Vector3(target.position.x, 1.2f, target.position.z),
                scale = Vector3.one,
                color = new Color(1f, 0.85f, 0.3f),
                preset = "raised",
                text = "ATLAS 3D",
            };
            Select(Create(d));
            Commit(true);
            Toast("Texto 3D: cambia lo que dice en Propiedades");
        }

        void ApplyText3D(AtlasItem it)
        {
            var lienzo = it.transform.Find("Lienzo");
            if (lienzo == null) return;
            AtlasTheme.Clear((RectTransform)lienzo);
            string txt = string.IsNullOrEmpty(it.text) ? " " : it.text;
            Color c = new Color(it.color.r, it.color.g, it.color.b, Mathf.Clamp01(it.opacity));
            int layers = it.preset == "raised" ? 10 : it.preset == "neon" ? 6 : 0;
            for (int i = layers; i >= 0; i--)
            {
                var t = new GameObject(i == 0 ? "Cara" : "Capa").AddComponent<Text>();
                t.transform.SetParent(lienzo, false);
                var r = t.rectTransform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = r.offsetMax = Vector2.zero;
                t.font = Lang.Arabic && Lang.ArabicFont != null ? Lang.ArabicFont : AtlasTheme.FontBold;
                t.fontSize = 200;
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.raycastTarget = false;
                t.text = HasArabic(txt) ? ArabicShaper.Visual(ArabicShaper.Shape(txt)) : txt;
                if (i == 0) { t.color = c; continue; }
                if (it.preset == "raised")
                {
                    // Darker copies behind, one step back each: depth.
                    float k = 0.55f - i * 0.025f;
                    t.color = new Color(c.r * k, c.g * k, c.b * k, c.a);
                    r.localPosition = new Vector3(i * 1.6f, -i * 1.6f, i * 6f);
                }
                else
                {
                    // Neon: soft halo copies around.
                    t.color = new Color(c.r, c.g, c.b, 0.16f * c.a);
                    float a = i * Mathf.PI * 2f / layers;
                    r.localPosition = new Vector3(Mathf.Cos(a) * 9f, Mathf.Sin(a) * 9f, 0f);
                }
            }
            // The plate covers the text (for taps), never seen.
            var face = lienzo.GetComponentInChildren<Text>();
            float w = Mathf.Max(200f, face != null ? face.preferredWidth : 800f) * 0.005f;
            var plate = it.transform.Find("Placa");
            if (plate != null)
            {
                plate.localScale = new Vector3(w + 0.1f, 1.1f, 1f);
                var pr = plate.GetComponent<Renderer>();
                if (it.material == null) { it.material = new Material(unlitTemplate != null ? unlitTemplate : litTemplate); MatUtil.SetTransparent(it.material, true); }
                it.material.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
                pr.sharedMaterial = it.material;
                pr.enabled = it.visible;
            }
            lienzo.gameObject.SetActive(it.visible);
        }

        static bool HasArabic(string s)
        {
            foreach (char ch in s) if (ch >= 0x0600 && ch <= 0x06FF) return true;
            return false;
        }

        void BuildText3DSection(AtlasItem it)
        {
            var box = Sec("Texto 3D", "edit");
            var field = AtlasTheme.TextField(box, it.text, v => { it.text = v; ApplyLook(it); Commit(true); }, 46f);
            AtlasTheme.Le(field.gameObject, 46f);
            var g = AtlasTheme.Grid(box, 3, InnerWidth, 38f);
            for (int i = 0; i < TextStyles.Length; i++)
            {
                string s = TextStyles[i];
                AtlasTheme.Chip(g, TextStyleNames[i], () => { it.preset = s; ApplyLook(it); Commit(true); RefreshProps(); }, it.preset == s);
            }
            ColorControls(box, it, Swatches);
            OpacityControls(box, it);
            Hint(box, "Muévelo, gíralo y escálalo como cualquier objeto; anima la opacidad o la escala con claves para títulos que aparecen.");
        }
    }
}
