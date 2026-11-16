// ATLAS 3D (Unity) - «Editor de materiales»: a window of its own with a
// live preview (sphere, cube or plane that turns), a colour picker (square
// + hue bar + HEX code), metal, roughness, glow, opacity, the textures and
// maps, texture placement (repeat / shift, which can be keyed on the
// timeline: faces that change expression, scrolling water…) and «Mis
// materiales», a library kept on the phone to reuse in any project.
// It edits a shape or one material of an imported model.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Atlas
{
    /// A square or bar that reports where the finger is (0..1).
    public class ColorPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public Action<Vector2> onPick;
        public Action onRelease;

        void Pick(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out local)) return;
            var n = Rect.PointToNormalized(rt.rect, local);
            if (onPick != null) onPick(new Vector2(Mathf.Clamp01(n.x), Mathf.Clamp01(n.y)));
        }

        public void OnPointerDown(PointerEventData e) { Pick(e); }
        public void OnDrag(PointerEventData e) { Pick(e); }
        public void OnPointerUp(PointerEventData e) { if (onRelease != null) onRelease(); }
    }

    [Serializable]
    public class SavedMat
    {
        public string name = "";
        public Color color = Color.white;
        public float metallic, smooth = 0.5f, emission, opacity = 1f, bump = 1f;
        public string tex = "", normal = "", emis = "";
    }

    [Serializable]
    public class SavedMatList
    {
        public List<SavedMat> mats = new List<SavedMat>();
    }

    public partial class AtlasApp
    {
        const int PreviewLayer = 31;

        GameObject matEditor;
        AtlasItem meItem;
        int meIndex = -1;          // material of a model, -1 = the shape itself
        Camera previewCam;
        RenderTexture previewRT;
        GameObject previewObj;
        string previewShape = "sphere";
        float previewSpin;
        Texture2D svTex, hueTex;
        RectTransform svMark, hueMark;
        InputField hexField;
        Image colorChip;
        float meH, meS, meV;

        class MatState
        {
            public Color color = Color.white;
            public float metallic, smooth = 0.5f, emission, opacity = 1f, bump = 1f;
            public string tex = "", normal = "", emis = "";
        }

        bool EditingModel { get { return meItem != null && meItem.kind == "model" && meIndex >= 0 && meIndex < meItem.modelMats.Count; } }

        Material TargetMaterial()
        {
            if (meItem == null) return null;
            if (EditingModel) return meItem.modelMats[meIndex];
            return meItem.material;
        }

        MatState ReadMat()
        {
            var s = new MatState();
            var it = meItem;
            if (EditingModel)
            {
                var m = it.modelMats[meIndex];
                MatOriginal o;
                it.matOriginals.TryGetValue(m, out o);
                var ov = OverrideFor(it, meIndex, false);
                s.color = ov != null && ov.tint ? ov.color : (o != null ? o.color : Color.white);
                s.metallic = ov != null && ov.metallic >= 0f ? ov.metallic : GetF(m, "_Metallic", 0f);
                s.smooth = ov != null && ov.smoothness >= 0f ? ov.smoothness : GetF(m, "_Smoothness", 0.4f);
                s.emission = ov != null && ov.emission >= 0f ? ov.emission : 0f;
                s.tex = ov != null ? ov.texture : "";
                s.normal = ov != null ? ov.normalMap : "";
                s.emis = ov != null ? ov.emissionMap : "";
                s.bump = ov != null ? ov.bump : 1f;
                return s;
            }
            s.color = it.color;
            s.metallic = it.metallic;
            s.smooth = it.smoothness;
            s.emission = it.emission;
            s.opacity = it.opacity;
            s.tex = it.texture;
            s.normal = it.normalMap;
            s.emis = it.emissionMap;
            s.bump = it.bumpStrength;
            return s;
        }

        void WriteMat(MatState s)
        {
            var it = meItem;
            if (it == null) return;
            if (EditingModel)
            {
                var ov = OverrideFor(it, meIndex, true);
                ov.tint = true;
                ov.color = new Color(s.color.r, s.color.g, s.color.b, 1f);
                ov.metallic = s.metallic;
                ov.smoothness = s.smooth;
                ov.emission = s.emission;
                ov.texture = s.tex ?? "";
                ov.normalMap = s.normal ?? "";
                ov.emissionMap = s.emis ?? "";
                ov.bump = s.bump;
            }
            else
            {
                it.color = new Color(s.color.r, s.color.g, s.color.b, it.color.a);
                it.metallic = s.metallic;
                it.smoothness = s.smooth;
                it.emission = s.emission;
                it.opacity = s.opacity;
                it.texture = s.tex ?? "";
                it.normalMap = s.normal ?? "";
                it.emissionMap = s.emis ?? "";
                it.bumpStrength = s.bump;
            }
            ApplyLook(it);
            UpdatePreviewMaterial();
        }

        void ChangeMat(Action<MatState> change)
        {
            var s = ReadMat();
            change(s);
            WriteMat(s);
        }

        // ------------------------------------------------------------------
        // Window

        void OpenMatEditor(AtlasItem it, int index)
        {
            if (it == null) return;
            if (it.kind == "model" && (!it.loaded || index < 0 || index >= it.modelMats.Count)) { Toast("Elige primero un material del modelo (tócalo otra vez)"); return; }
            if (it.kind != "model" && !(it.IsShape || it.kind == "image")) { Toast("Este objeto no tiene material"); return; }
            meItem = it;
            meIndex = it.kind == "model" ? index : -1;
            BuildMatEditor();
        }

        void CloseMatEditor()
        {
            if (matEditor != null) Destroy(matEditor);
            matEditor = null;
            if (previewCam != null) previewCam.gameObject.SetActive(false);
            if (previewObj != null) previewObj.SetActive(false);
            meItem = null;
            RefreshProps();
        }

        void BuildMatEditor()
        {
            if (matEditor != null) Destroy(matEditor);
            CloseMenus();
            EnsurePreview();
            var dim = AtlasTheme.Box(canvasRoot, "EditorMateriales", new Color(0f, 0f, 0f, 0.6f));
            AtlasTheme.Fill(dim.rectTransform);
            dim.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            matEditor = dim.gameObject;
            var card = AtlasTheme.Box(dim.transform, "Ventana", AtlasTheme.Panel2, 18);
            AtlasTheme.Fill(card.rectTransform, 14f, 14f, 14f, 14f);
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            // Title
            string name = EditingModel ? meItem.modelMats[meIndex].name.Replace(" (Instance)", "") : meItem.gameObject.name;
            var title = AtlasTheme.Label(card.transform, "Editor de materiales · " + name, 19, AtlasTheme.TextColor, true);
            AtlasTheme.Pin(title.rectTransform, 0f, 1f, new Vector2(18f, -8f), new Vector2(700f, 40f));
            var x = AtlasTheme.IconButton(card.transform, "close", CloseMatEditor, 44f, 22f);
            AtlasTheme.Pin((RectTransform)x.transform, 1f, 1f, new Vector2(-8f, -4f), new Vector2(44f, 44f));
            ((RectTransform)x.transform).pivot = new Vector2(1f, 1f);
            // Left: preview and «Mis materiales»
            var left = AtlasTheme.Rect(card.transform, "Izquierda");
            left.anchorMin = new Vector2(0f, 0f);
            left.anchorMax = new Vector2(0f, 1f);
            left.pivot = new Vector2(0f, 1f);
            left.offsetMin = new Vector2(12f, 12f);
            // Narrow screens: a smaller preview column.
            float lw = canvasRoot.rect.width < 900f ? 250f : 310f;
            left.offsetMax = new Vector2(12f + lw, -52f);
            ScrollRect lsr;
            var lc = AtlasTheme.ScrollArea(left, out lsr, 8f, new RectOffset(4, 4, 0, 8));
            var pv = AtlasTheme.Box(lc, "Vista", new Color(0.08f, 0.09f, 0.11f), 14);
            AtlasTheme.Le(pv.gameObject, lw - 20f);
            var raw = AtlasTheme.Rect(pv.transform, "Imagen").gameObject.AddComponent<RawImage>();
            AtlasTheme.Fill(raw.rectTransform, 6f, 6f, 6f, 6f);
            raw.texture = previewRT;
            raw.raycastTarget = false;
            var sg = AtlasTheme.Grid(lc, 3, lw - 14f, 36f);
            foreach (var sh in new[] { "sphere", "cube", "plane" })
            {
                string shape = sh;
                AtlasTheme.Chip(sg, sh == "sphere" ? "Esfera" : sh == "cube" ? "Cubo" : "Plano", () => { previewShape = shape; EnsurePreview(); BuildMatEditor(); }, previewShape == sh);
            }
            var ml = AtlasTheme.Label(lc, "MIS MATERIALES", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Le(ml.gameObject, 22f);
            var lib = LoadMatLibrary();
            if (lib.mats.Count == 0) PrefHint(lc, "Guarda aquí los materiales que te gusten para usarlos en cualquier proyecto.");
            else
            {
                var lg = AtlasTheme.Grid(lc, 2, lw - 14f, 38f);
                for (int i = 0; i < lib.mats.Count; i++)
                {
                    var sm = lib.mats[i];
                    AtlasTheme.Chip(lg, "★ " + sm.name, () => ApplySavedMat(sm));
                }
            }
            AtlasTheme.TextButton(lc, "save", "Guardar en Mis materiales…", SaveCurrentMat, false, 42f, 15);
            if (lib.mats.Count > 0)
                AtlasTheme.TextButton(lc, "delete", "Borrar uno de Mis materiales…", DeleteSavedMatMenu, false, 40f, 14);
            // Right: the controls
            var right = AtlasTheme.Rect(card.transform, "Derecha");
            right.anchorMin = new Vector2(0f, 0f);
            right.anchorMax = new Vector2(1f, 1f);
            right.offsetMin = new Vector2(24f + lw, 12f);
            right.offsetMax = new Vector2(-12f, -52f);
            ScrollRect rsr;
            var rc = AtlasTheme.ScrollArea(right, out rsr, 8f, new RectOffset(6, 10, 0, 12));
            BuildMatControls(rc);
            if (toastHolder != null) toastHolder.SetAsLastSibling();
        }

        float RightWidth { get { float lw = canvasRoot.rect.width < 900f ? 250f : 310f; return Mathf.Max(300f, canvasRoot.rect.width - 28f - lw - 64f); } }

        void BuildMatControls(Transform c)
        {
            var s = ReadMat();
            float w = RightWidth;
            // Colour: square (saturation / brightness), hue bar, HEX.
            var cl = AtlasTheme.Label(c, "COLOR BASE", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Le(cl.gameObject, 22f);
            Color.RGBToHSV(s.color, out meH, out meS, out meV);
            var row = AtlasTheme.Rect(c, "FilaColor").gameObject;
            AtlasTheme.Row(row, 12f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);
            AtlasTheme.Le(row, 170f);
            var sv = AtlasTheme.Rect(row.transform, "Cuadro").gameObject.AddComponent<RawImage>();
            AtlasTheme.Le(sv.gameObject, 170f, 220f);
            if (svTex == null) { svTex = new Texture2D(48, 48, TextureFormat.RGB24, false); svTex.wrapMode = TextureWrapMode.Clamp; }
            FillSv();
            sv.texture = svTex;
            svMark = AtlasTheme.Box(sv.transform, "Marca", Color.white, 9).rectTransform;
            svMark.sizeDelta = new Vector2(18f, 18f);
            svMark.anchorMin = svMark.anchorMax = new Vector2(meS, meV);
            svMark.GetComponent<Image>().raycastTarget = false;
            var pad = sv.gameObject.AddComponent<ColorPad>();
            pad.onPick = p => { meS = p.x; meV = p.y; svMark.anchorMin = svMark.anchorMax = p; PickedColor(); };
            pad.onRelease = () => Commit(true);
            var side = AtlasTheme.Rect(row.transform, "Lado").gameObject;
            AtlasTheme.Column(side, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);
            AtlasTheme.Le(side, 170f, 200f);
            colorChip = AtlasTheme.Box(side.transform, "Muestra", s.color, 10);
            AtlasTheme.Le(colorChip.gameObject, 52f, 200f);
            hexField = AtlasTheme.TextField(side.transform, "#" + ColorUtility.ToHtmlStringRGB(s.color), v =>
            {
                Color cc;
                string t = v.Trim();
                if (!t.StartsWith("#")) t = "#" + t;
                if (!ColorUtility.TryParseHtmlString(t, out cc)) { Toast("Código no válido: usa #RRGGBB (por ejemplo #FF8800)", true); return; }
                Color.RGBToHSV(cc, out meH, out meS, out meV);
                ChangeMat(st => st.color = cc);
                Commit(true);
                BuildMatEditor();
            }, 44f);
            AtlasTheme.Le(hexField.gameObject, 44f, 200f);
            var hl = AtlasTheme.Label(side.transform, "Código HEX: escríbelo y pulsa Intro", 13, AtlasTheme.TextDim);
            AtlasTheme.Le(hl.gameObject, 34f, 200f);
            // Hue bar
            var hue = AtlasTheme.Rect(c, "Tono").gameObject.AddComponent<RawImage>();
            AtlasTheme.Le(hue.gameObject, 30f);
            if (hueTex == null)
            {
                hueTex = new Texture2D(128, 1, TextureFormat.RGB24, false);
                hueTex.wrapMode = TextureWrapMode.Clamp;
                for (int i = 0; i < 128; i++) hueTex.SetPixel(i, 0, Color.HSVToRGB(i / 127f, 1f, 1f));
                hueTex.Apply();
            }
            hue.texture = hueTex;
            hueMark = AtlasTheme.Box(hue.transform, "Marca", Color.white, 3).rectTransform;
            hueMark.sizeDelta = new Vector2(6f, 34f);
            hueMark.anchorMin = hueMark.anchorMax = new Vector2(meH, 0.5f);
            hueMark.GetComponent<Image>().raycastTarget = false;
            var hpad = hue.gameObject.AddComponent<ColorPad>();
            hpad.onPick = p => { meH = p.x; hueMark.anchorMin = hueMark.anchorMax = new Vector2(meH, 0.5f); FillSv(); PickedColor(); };
            hpad.onRelease = () => Commit(true);
            var g = AtlasTheme.Grid(c, 12, w, 30f, 5f);
            foreach (var c0 in Swatches)
            {
                Color cc = c0;
                var sw = AtlasTheme.Box(g, "Color", cc, 8);
                sw.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    ChangeMat(st => st.color = cc);
                    Commit(true);
                    BuildMatEditor();
                });
            }
            // Surface
            var sl = AtlasTheme.Label(c, "SUPERFICIE", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Le(sl.gameObject, 22f);
            AtlasTheme.SliderRow(c, "Metálico", 0f, 1f, s.metallic, "0.00", v => ChangeMat(st => st.metallic = v), () => Commit(true));
            AtlasTheme.SliderRow(c, "Rugosidad", 0f, 1f, 1f - s.smooth, "0.00", v => ChangeMat(st => st.smooth = 1f - v), () => Commit(true));
            AtlasTheme.SliderRow(c, "Emisión (brillo propio)", 0f, 8f, s.emission, "0.0", v => ChangeMat(st => st.emission = v), () => Commit(true));
            if (!EditingModel)
                AtlasTheme.SliderRow(c, "Opacidad", 0.05f, 1f, s.opacity, "0.00", v => ChangeMat(st => st.opacity = v), () => Commit(true));
            // Textures
            var tl = AtlasTheme.Label(c, "TEXTURAS Y MAPAS", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Le(tl.gameObject, 22f);
            var tg = AtlasTheme.Grid(c, 2, w, 42f);
            MapButtons(tg, "Textura base", s.tex, () => PickEditorImage("base"), () => { ChangeMat(st => st.tex = ""); Commit(true); BuildMatEditor(); });
            MapButtons(tg, "Mapa normal", s.normal, () => PickEditorImage("normal"), () => { ChangeMat(st => st.normal = ""); Commit(true); BuildMatEditor(); });
            AtlasTheme.TextButton(tg, "image", "Relieve desde imagen B/N…", () => PickEditorImage("bump"), false, 42f, 14);
            MapButtons(tg, "Mapa de emisión", s.emis, () => PickEditorImage("emission"), () => { ChangeMat(st => st.emis = ""); Commit(true); BuildMatEditor(); });
            if (!string.IsNullOrEmpty(s.normal))
                AtlasTheme.SliderRow(c, "Fuerza del relieve", 0f, 3f, s.bump, "0.0", v => ChangeMat(st => st.bump = v), () => Commit(true));
            // Texture placement (animatable)
            var ul = AtlasTheme.Label(c, "COLOCACIÓN DE LA TEXTURA (ANIMABLE)", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Le(ul.gameObject, 22f);
            var it = meItem;
            if (EditingModel && it.uvMat != meIndex)
                PrefHint(c, "Al moverla se aplica a este material del modelo (por ejemplo, la cara para cambiar de expresión).");
            AtlasTheme.SliderRow(c, "Repetir X", 0.1f, 20f, it.uv.x, "0.00", v => SetUv(0, v), () => Commit(true));
            AtlasTheme.SliderRow(c, "Repetir Y", 0.1f, 20f, it.uv.y, "0.00", v => SetUv(1, v), () => Commit(true));
            AtlasTheme.SliderRow(c, "Desplazar X", -1f, 1f, it.uv.z, "0.000", v => SetUv(2, v), () => Commit(true));
            AtlasTheme.SliderRow(c, "Desplazar Y", -1f, 1f, it.uv.w, "0.000", v => SetUv(3, v), () => Commit(true));
            var ug = AtlasTheme.Grid(c, 2, w, 42f);
            AtlasTheme.TextButton(ug, "key", "Clave de textura aquí (◆)", () =>
            {
                it.SetKey(Mathf.RoundToInt(frame), AtlasItem.ChUv, new List<string>());
                Commit(true);
                RefreshKeys();
                Toast("Clave de textura en el fotograma " + Mathf.RoundToInt(frame));
            }, true, 42f, 14);
            AtlasTheme.TextButton(ug, "undo", "Colocación original", () => { it.uv = new Vector4(1f, 1f, 0f, 0f); it.ApplyUv(); Commit(true); BuildMatEditor(); }, false, 42f, 14);
            PrefHint(c, "Para cambiar de expresión con una textura de varias caras: pon «Repetir» al tamaño de una cara (por ejemplo 0,5), mueve «Desplazar» a cada cara y guarda claves; en la línea de tiempo usa la interpolación «Constante» para que salte de una a otra.");
            // Glow (animatable): parts that light up when you want.
            BuildGlowEditor(c, w);
            // Presets: shapes, and a model's parts (not the glass ones).
            if (EditingModel)
            {
                var pl = AtlasTheme.Label(c, "PREAJUSTES", 14, AtlasTheme.TextDim, true);
                AtlasTheme.Le(pl.gameObject, 22f);
                var pg = AtlasTheme.Grid(c, 3, w, 38f);
                foreach (var p0 in MatPresets)
                {
                    var p = p0;
                    if (p.shader != "" || p.opacity < 0.99f) continue;
                    AtlasTheme.Chip(pg, p.name, () =>
                    {
                        ChangeMat(st =>
                        {
                            if (!p.keepColor) st.color = p.color;
                            st.metallic = p.metallic;
                            st.smooth = 1f - p.roughness;
                            st.emission = p.emission;
                        });
                        Commit(true);
                        BuildMatEditor();
                    });
                }
            }
            else
            {
                var pl = AtlasTheme.Label(c, "PREAJUSTES", 14, AtlasTheme.TextDim, true);
                AtlasTheme.Le(pl.gameObject, 22f);
                var pg = AtlasTheme.Grid(c, 3, w, 38f);
                foreach (var p0 in MatPresets)
                {
                    var p = p0;
                    AtlasTheme.Chip(pg, p.name, () =>
                    {
                        if (!p.keepColor) it.color = p.color;
                        it.shader = p.shader;
                        it.metallic = p.metallic;
                        it.smoothness = 1f - p.roughness;
                        it.emission = p.emission;
                        it.opacity = p.opacity;
                        ApplyLook(it);
                        Commit(true);
                        BuildMatEditor();
                    });
                }
            }
            var bg = AtlasTheme.Grid(c, 2, w, 46f);
            AtlasTheme.TextButton(bg, "undo", "Restablecer material", () =>
            {
                if (EditingModel) it.matOverrides.RemoveAll(o => o.index == meIndex);
                else { it.metallic = 0f; it.smoothness = 0.5f; it.emission = 0f; it.opacity = 1f; it.texture = ""; it.normalMap = ""; it.emissionMap = ""; it.shader = ""; }
                ApplyLook(it);
                Commit(true);
                BuildMatEditor();
            }, false, 46f, 15);
            AtlasTheme.TextButton(bg, null, "Listo", CloseMatEditor, true, 46f, 16);
        }

        void MapButtons(Transform g, string label, string current, UnityEngine.Events.UnityAction pick, UnityEngine.Events.UnityAction remove)
        {
            if (string.IsNullOrEmpty(current)) AtlasTheme.TextButton(g, "image", label + "…", pick, false, 42f, 14);
            else AtlasTheme.TextButton(g, "close", "Quitar " + label.ToLowerInvariant(), remove, false, 42f, 14);
        }

        /// «Brillo animable»: the part (or the whole object) lights up; keys
        /// turn it on and off on the timeline.
        void BuildGlowEditor(Transform c, float w)
        {
            var it = meItem;
            if (it == null) return;
            int target = EditingModel ? meIndex : -1;
            var gl = AtlasTheme.Label(c, "BRILLO (ANIMABLE CON CLAVES)", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Le(gl.gameObject, 22f);
            if (it.glowMat != target && it.glow > 0f)
                PrefHint(c, "Ahora brilla otra pieza; al mover el brillo pasa a esta.");
            AtlasTheme.SliderRow(c, "Brillo", 0f, 12f, it.glowMat == target ? it.glow : 0f, "0.0", v =>
            {
                if (it.glowMat != target) { it.glow = 0f; it.ApplyGlow(); it.glowMat = target; it.LookChanged(); }
                it.glow = v;
                it.ApplyGlow();
            }, () => Commit(true));
            var cg = AtlasTheme.Grid(c, 4, w, 36f);
            var cols = new[] { new Color(1f, 1f, 1f, 0f), new Color(1f, 0.85f, 0.2f, 1f), new Color(0.2f, 0.85f, 1f, 1f), new Color(1f, 0.15f, 0.1f, 1f), new Color(0.3f, 1f, 0.3f, 1f), new Color(1f, 0.3f, 0.9f, 1f), new Color(0.55f, 0.35f, 1f, 1f), new Color(1f, 1f, 1f, 1f) };
            var names = new[] { "Su color", "Amarillo", "Cian", "Rojo", "Verde", "Rosa", "Violeta", "Blanco" };
            for (int i = 0; i < cols.Length; i++)
            {
                var col = cols[i];
                bool on = Mathf.Abs(it.glowColor.r - col.r) < 0.01f && Mathf.Abs(it.glowColor.g - col.g) < 0.01f && Mathf.Abs(it.glowColor.b - col.b) < 0.01f && (it.glowColor.a >= 0.5f) == (col.a >= 0.5f);
                AtlasTheme.Chip(cg, names[i], () =>
                {
                    it.glowColor = col;
                    it.ApplyGlow();
                    Commit(true);
                    BuildMatEditor();
                }, on);
            }
            var kg = AtlasTheme.Grid(c, 2, w, 42f);
            AtlasTheme.TextButton(kg, "key", "Clave de brillo aquí (◆)", () =>
            {
                if (it.glowMat != target) { it.glow = 0f; it.ApplyGlow(); it.glowMat = target; it.LookChanged(); }
                it.SetKey(Mathf.RoundToInt(frame), AtlasItem.ChGlow, new List<string>());
                Commit(true);
                RefreshKeys();
                Toast("Clave de brillo en el fotograma " + Mathf.RoundToInt(frame));
            }, true, 42f, 14);
            AtlasTheme.TextButton(kg, "sparkle", "Apagado / encendido", () =>
            {
                if (it.glowMat != target) { it.glow = 0f; it.ApplyGlow(); it.glowMat = target; it.LookChanged(); }
                it.glow = it.glow > 0f ? 0f : 4f;
                it.ApplyGlow();
                it.SetKey(Mathf.RoundToInt(frame), AtlasItem.ChGlow, new List<string>());
                Commit(true);
                RefreshKeys();
                BuildMatEditor();
            }, false, 42f, 14);
            PrefHint(c, "Para que se encienda de golpe: clave apagada en un fotograma y encendida en el siguiente, o interpolación «Constante».");
        }

        void SetUv(int k, float v)
        {
            var it = meItem;
            if (it == null) return;
            var u = it.uv;
            u[k] = v;
            it.uv = u;
            it.uvMat = EditingModel ? meIndex : -1;
            it.ApplyUv();
        }

        void FillSv()
        {
            if (svTex == null) return;
            for (int y = 0; y < svTex.height; y++)
                for (int x = 0; x < svTex.width; x++)
                    svTex.SetPixel(x, y, Color.HSVToRGB(meH, x / (svTex.width - 1f), y / (svTex.height - 1f)));
            svTex.Apply();
        }

        void PickedColor()
        {
            Color c = Color.HSVToRGB(meH, meS, meV);
            ChangeMat(st => st.color = c);
            if (colorChip != null) colorChip.color = c;
            if (hexField != null) hexField.text = "#" + ColorUtility.ToHtmlStringRGB(c);
        }

        void PickEditorImage(string slot)
        {
            if (store == null || NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path) || store == null || meItem == null) return;
                string file;
                try { file = store.AddTextureFile(path); }
                catch (Exception e) { Toast("No se pudo usar la imagen: " + e.Message, true); return; }
                if (slot == "bump")
                {
                    string made = BumpToNormal(file);
                    if (made == null) { Toast("Esa imagen no se puede leer (usa PNG o JPG)", true); return; }
                    ChangeMat(st => st.normal = made);
                }
                else
                {
                    if (LoadTexture(file, slot == "normal") == null) { Toast("Esa imagen no se puede leer (usa PNG o JPG)", true); return; }
                    if (slot == "base") ChangeMat(st => st.tex = file);
                    else if (slot == "normal") ChangeMat(st => st.normal = file);
                    else ChangeMat(st => { st.emis = file; if (st.emission < 1f) st.emission = 1f; });
                }
                Commit(true);
                BuildMatEditor();
            }, new string[] { "image/*" });
        }

        // ------------------------------------------------------------------
        // Preview

        void EnsurePreview()
        {
            cam.cullingMask &= ~(1 << PreviewLayer);
            if (probe != null) probe.cullingMask &= ~(1 << PreviewLayer);
            var root = new Vector3(0f, -4000f, 0f);
            if (previewRT == null)
            {
                previewRT = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
                previewRT.antiAliasing = 4;
                previewRT.Create();
            }
            if (previewCam == null)
            {
                var go = new GameObject("VistaMaterial");
                previewCam = go.AddComponent<Camera>();
                previewCam.cullingMask = 1 << PreviewLayer;
                previewCam.clearFlags = CameraClearFlags.SolidColor;
                previewCam.backgroundColor = new Color(0.1f, 0.11f, 0.13f);
                previewCam.fieldOfView = 30f;
                previewCam.nearClipPlane = 0.1f;
                previewCam.farClipPlane = 20f;
                previewCam.targetTexture = previewRT;
                go.transform.position = root + new Vector3(0f, 0.6f, -4.2f);
                go.transform.LookAt(root);
            }
            previewCam.gameObject.SetActive(true);
            if (previewObj != null && previewObj.name != previewShape) { Destroy(previewObj); previewObj = null; }
            if (previewObj == null)
            {
                var type = previewShape == "cube" ? PrimitiveType.Cube : previewShape == "plane" ? PrimitiveType.Quad : PrimitiveType.Sphere;
                previewObj = GameObject.CreatePrimitive(type);
                previewObj.name = previewShape;
                Destroy(previewObj.GetComponent<Collider>());
                previewObj.layer = PreviewLayer;
                previewObj.transform.position = root;
                previewObj.transform.localScale = Vector3.one * (previewShape == "cube" ? 1.25f : previewShape == "plane" ? 1.8f : 1.6f);
            }
            previewObj.SetActive(true);
            UpdatePreviewMaterial();
        }

        void UpdatePreviewMaterial()
        {
            if (previewObj == null) return;
            var m = TargetMaterial();
            if (m != null) previewObj.GetComponent<Renderer>().sharedMaterial = m;
        }

        /// Called every frame: the preview turns slowly.
        void UpdateMatPreview()
        {
            if (matEditor == null || previewObj == null) return;
            if (meItem == null) { CloseMatEditor(); return; }
            previewSpin += Time.unscaledDeltaTime * 25f;
            previewObj.transform.rotation = previewShape == "plane" ? Quaternion.Euler(0f, Mathf.Sin(previewSpin * 0.03f) * 25f, 0f) : Quaternion.Euler(-15f, previewSpin, 0f);
        }

        // ------------------------------------------------------------------
        // «Mis materiales»

        static string MatLibDir { get { string d = Path.Combine(Application.persistentDataPath, "MisMateriales"); Directory.CreateDirectory(d); return d; } }

        static SavedMatList LoadMatLibrary()
        {
            try
            {
                string f = Path.Combine(MatLibDir, "lista.json");
                if (File.Exists(f)) return JsonUtility.FromJson<SavedMatList>(File.ReadAllText(f)) ?? new SavedMatList();
            }
            catch (Exception) { }
            return new SavedMatList();
        }

        static void SaveMatLibrary(SavedMatList l)
        {
            try { File.WriteAllText(Path.Combine(MatLibDir, "lista.json"), JsonUtility.ToJson(l, true)); }
            catch (Exception) { }
        }

        string CopyToLibrary(string projectFile)
        {
            if (string.IsNullOrEmpty(projectFile) || store == null) return "";
            try
            {
                string src = store.TexturePath(projectFile);
                if (!File.Exists(src)) return "";
                string dst = Path.Combine(MatLibDir, projectFile);
                File.Copy(src, dst, true);
                return projectFile;
            }
            catch (Exception) { return ""; }
        }

        string CopyFromLibrary(string libFile)
        {
            if (string.IsNullOrEmpty(libFile) || store == null) return "";
            try
            {
                string src = Path.Combine(MatLibDir, libFile);
                return File.Exists(src) ? store.AddTextureFile(src) : "";
            }
            catch (Exception) { return ""; }
        }

        void SaveCurrentMat()
        {
            AtlasTheme.Dialog(canvasRoot, "Guardar en Mis materiales", "Ponle un nombre: lo tendrás en cualquier proyecto.", "Mi material", "Guardar", false, n =>
            {
                var s = ReadMat();
                var l = LoadMatLibrary();
                l.mats.Add(new SavedMat
                {
                    name = string.IsNullOrEmpty(n.Trim()) ? "Mi material" : n.Trim(),
                    color = s.color, metallic = s.metallic, smooth = s.smooth, emission = s.emission, opacity = s.opacity, bump = s.bump,
                    tex = CopyToLibrary(s.tex), normal = CopyToLibrary(s.normal), emis = CopyToLibrary(s.emis),
                });
                SaveMatLibrary(l);
                Toast("Guardado en Mis materiales");
                BuildMatEditor();
            });
            toastHolder.SetAsLastSibling();
        }

        void ApplySavedMat(SavedMat sm)
        {
            ChangeMat(st =>
            {
                st.color = sm.color; st.metallic = sm.metallic; st.smooth = sm.smooth; st.emission = sm.emission;
                st.opacity = sm.opacity; st.bump = sm.bump;
                st.tex = CopyFromLibrary(sm.tex); st.normal = CopyFromLibrary(sm.normal); st.emis = CopyFromLibrary(sm.emis);
            });
            Commit(true);
            Toast("Material «" + sm.name + "» puesto");
            BuildMatEditor();
        }

        void DeleteSavedMatMenu()
        {
            var l = LoadMatLibrary();
            var entries = new List<AtlasTheme.MenuItem> { AtlasTheme.MenuItem.Header("Borrar de Mis materiales") };
            for (int i = 0; i < l.mats.Count; i++)
            {
                int idx = i;
                entries.Add(new AtlasTheme.MenuItem("delete", l.mats[i].name, () =>
                {
                    var l2 = LoadMatLibrary();
                    if (idx < l2.mats.Count) l2.mats.RemoveAt(idx);
                    SaveMatLibrary(l2);
                    BuildMatEditor();
                }));
            }
            AtlasTheme.Popup(canvasRoot, new Vector2(Screen.width * 0.2f, Screen.height * 0.3f), entries);
        }
    }
}
