// ATLAS 3D (Unity) - look of the interface, the same as the Godot version:
// dark rounded panels, purple accent, ATLAS's own stroke icons (white PNGs
// tinted per state) and the Inter font. Every button and panel is made here.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Atlas
{
    public static class AtlasTheme
    {
        public static readonly Color Bg = Hex("0e0f13");
        public static readonly Color Panel = Hex("16181d");
        public static readonly Color Panel2 = Hex("1d2027");
        public static readonly Color Panel3 = Hex("262a33");
        public static readonly Color Viewport = Hex("3a3d47");
        public static readonly Color Accent = Hex("b25cff");
        public static readonly Color AccentButton = Hex("7b2fd6");
        public static readonly Color AccentDim = Hex("4a2a75");
        public static readonly Color AccentLight = Hex("d8b4ff");
        public static readonly Color TextColor = Hex("e8eaf0");
        public static readonly Color TextDim = Hex("959ba7");
        public static readonly Color Danger = Hex("e5484d");
        public static readonly Color Key = Hex("f2c14e");
        public static readonly Color Badge = Hex("3d8bfd");
        public static readonly Color AxisX = Hex("e5484d");
        public static readonly Color AxisY = Hex("46c46e");
        public static readonly Color AxisZ = Hex("3d8bfd");

        static Font font, fontBold;
        static readonly Dictionary<int, Sprite> rounded = new Dictionary<int, Sprite>();
        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.Load<Font>("Fonts/Inter-Medium");
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Font FontBold
        {
            get
            {
                if (fontBold == null) fontBold = Resources.Load<Font>("Fonts/Inter-SemiBold");
                return fontBold != null ? fontBold : Font;
            }
        }

        public static Color Hex(string h)
        {
            Color c;
            ColorUtility.TryParseHtmlString("#" + h, out c);
            return c;
        }

        public static Texture2D Icon(string name)
        {
            Texture2D t;
            if (!icons.TryGetValue(name, out t))
            {
                t = Resources.Load<Texture2D>("Icons/" + name);
                icons[name] = t;
            }
            return t;
        }

        /// White rounded rectangle (9-sliced), so any panel can be tinted.
        public static Sprite Rounded(int radius)
        {
            Sprite s;
            if (rounded.TryGetValue(radius, out s)) return s;
            int n = radius * 2 + 4;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[n * n];
            float r = radius;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, n - r);
                    float cy = Mathf.Clamp(y + 0.5f, r, n - r);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new UnityEngine.Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius + 1, radius + 1, radius + 1, radius + 1));
            rounded[radius] = s;
            return s;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Box(Transform parent, string name, Color c, int radius = 0)
        {
            var r = Rect(parent, name);
            var img = r.gameObject.AddComponent<Image>();
            img.color = c;
            if (radius > 0)
            {
                img.sprite = Rounded(radius);
                img.type = Image.Type.Sliced;
            }
            return img;
        }

        public static void Fill(RectTransform r, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(left, bottom);
            r.offsetMax = new Vector2(-right, -top);
        }

        /// Places `r` at a corner / edge: anchor and pivot from (ax, ay) in 0..1.
        public static void Pin(RectTransform r, float ax, float ay, Vector2 pos, Vector2 size)
        {
            r.anchorMin = new Vector2(ax, ay);
            r.anchorMax = new Vector2(ax, ay);
            r.pivot = new Vector2(ax, ay);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        public static Text Label(Transform parent, string text, int size, Color color, bool bold = false, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var r = Rect(parent, "Texto");
            var t = r.gameObject.AddComponent<Text>();
            t.font = bold ? FontBold : Font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            Lang.Set(t, text);
            return t;
        }

        /// Text that always fits its box: the letters shrink (down to `min`)
        /// instead of spilling out of buttons and panels (long Spanish,
        /// English or Arabic texts).
        public static void Fit(Text t, int min)
        {
            if (t == null) return;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMaxSize = t.fontSize;
            t.resizeTextMinSize = Mathf.Max(8, Mathf.Min(min, t.fontSize));
        }

        static Button MakeButton(Image img, UnityAction onClick)
        {
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.75f, 0.75f, 0.8f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.fadeDuration = 0.05f;
            b.colors = colors;
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        static void Size(GameObject go, float w, float h)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = h;
            le.minWidth = w;
            le.minHeight = h;
            ((RectTransform)go.transform).sizeDelta = new Vector2(w, h);
        }

        public static RawImage IconImage(Transform parent, string icon, float size, Color tint)
        {
            var ir = Rect(parent, "Icono");
            var ri = ir.gameObject.AddComponent<RawImage>();
            ri.texture = Icon(icon);
            ri.color = tint;
            ri.raycastTarget = false;
            ir.sizeDelta = new Vector2(size, size);
            return ri;
        }

        /// Square icon button (the rail and the floating bar).
        public static Button IconButton(Transform parent, string icon, UnityAction onClick, float size = 50f, float iconSize = 26f)
        {
            var img = Box(parent, "Icono_" + icon, new Color(0, 0, 0, 0), 12);
            Size(img.gameObject, size, size);
            var b = MakeButton(img, onClick);
            var ri = IconImage(img.transform, icon, iconSize, TextColor);
            var r = ri.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            return b;
        }

        /// Icon + text button (Animar, Render, menu entries).
        public static Button TextButton(Transform parent, string icon, string text, UnityAction onClick, bool accent = false, float height = 50f, int fontSize = 19, float width = 0f)
        {
            var img = Box(parent, "Boton", accent ? AccentButton : Panel3, 12);
            string shown = Lang.T(text ?? "");
            float w = width > 0f ? width : (icon != null ? 52f : 28f) + shown.Length * fontSize * 0.56f;
            Size(img.gameObject, w, height);
            var b = MakeButton(img, onClick);
            var row = img.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(14, 14, 0, 0);
            row.spacing = 9f;
            row.childAlignment = width > 0f && icon == null ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            if (icon != null)
            {
                var ri = IconImage(img.transform, icon, 24f, TextColor);
                var le = ri.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 24f;
                le.preferredHeight = 24f;
            }
            var t = Label(img.transform, text, fontSize, TextColor, true);
            Fit(t, fontSize - 7);
            var tle = t.gameObject.AddComponent<LayoutElement>();
            tle.preferredHeight = height;
            tle.flexibleWidth = 1f;
            if (width > 0f && icon == null) t.alignment = TextAnchor.MiddleCenter;
            return b;
        }

        public static void SetButtonIcon(Button b, string icon)
        {
            if (b == null) return;
            var ri = b.GetComponentInChildren<RawImage>();
            if (ri != null) ri.texture = Icon(icon);
        }

        /// Highlighted (active tool / open panel) or normal look.
        public static void SetActive(Button b, bool on, bool solid = false)
        {
            if (b == null) return;
            var img = b.GetComponent<Image>();
            if (img != null) img.color = on ? AccentDim : (solid ? Panel3 : new Color(0, 0, 0, 0));
            foreach (var ri in b.GetComponentsInChildren<RawImage>())
                ri.color = on ? AccentLight : TextColor;
        }

        public static HorizontalLayoutGroup Row(GameObject go, float spacing, RectOffset padding, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = padding;
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            return h;
        }

        public static VerticalLayoutGroup Column(GameObject go, float spacing, RectOffset padding, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding;
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static GameObject Separator(Transform parent, bool vertical)
        {
            var s = Box(parent, "Separador", new Color(1f, 1f, 1f, 0.08f));
            var le = s.gameObject.AddComponent<LayoutElement>();
            if (vertical) { le.preferredWidth = 1f; le.preferredHeight = 30f; }
            else { le.preferredHeight = 1f; le.minHeight = 1f; }
            return s.gameObject;
        }

        public static GameObject Spacer(Transform parent, float flexible = 1f)
        {
            var go = Rect(parent, "Espacio").gameObject;
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = flexible;
            le.flexibleHeight = flexible;
            return go;
        }

        /// Number field (keyboard on phones); calls `changed` when edited.
        public static InputField NumberField(Transform parent, float value, UnityAction<float> changed, float width = 76f)
        {
            var img = Box(parent, "Numero", Panel, 8);
            Size(img.gameObject, width, 40f);
            var t = Label(img.transform, "", 17, TextColor, true, TextAnchor.MiddleCenter);
            Fill(t.rectTransform, 4f, 4f);
            t.raycastTarget = true;
            var f = img.gameObject.AddComponent<InputField>();
            f.textComponent = t;
            f.contentType = InputField.ContentType.DecimalNumber;
            f.text = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            f.onEndEdit.AddListener(s =>
            {
                float v;
                if (float.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                    changed(v);
            });
            return f;
        }

        public static void SetNumber(InputField f, float v)
        {
            if (f != null && !f.isFocused)
                f.text = v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }
            // ------------------------------------------------------------------
        // Inspector widgets (phase 3)

        public static LayoutElement Le(GameObject go, float height = -1f, float width = -1f, float flexWidth = -1f)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            if (height >= 0f)
            {
                le.minHeight = height;
                // A wrapping text grows with its lines instead of being drawn
                // over what is below it; everything else has this height.
                var tx = go.GetComponent<Text>();
                bool grows = tx != null && tx.horizontalOverflow == HorizontalWrapMode.Wrap && !tx.resizeTextForBestFit;
                le.preferredHeight = grows ? -1f : height;
            }
            if (width >= 0f) { le.preferredWidth = width; le.minWidth = width; }
            if (flexWidth >= 0f) le.flexibleWidth = flexWidth;
            return le;
        }

        /// Vertical scrolling list: returns the content to fill (children
        /// laid out top to bottom, any height).
        public static RectTransform ScrollArea(Transform parent, out ScrollRect sr, float spacing = 8f, RectOffset padding = null)
        {
            var view = Rect(parent, "Vista");
            Fill(view);
            var vimg = view.gameObject.AddComponent<Image>();
            vimg.color = new Color(0, 0, 0, 0);
            view.gameObject.AddComponent<RectMask2D>();
            var content = Rect(view, "Contenido");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var col = Column(content.gameObject, spacing, padding ?? new RectOffset(0, 0, 0, 0));
            col.childForceExpandWidth = true;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr = parent.gameObject.AddComponent<ScrollRect>();
            sr.viewport = view;
            sr.content = content;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.elasticity = 0.08f;
            sr.inertia = true;
            sr.decelerationRate = 0.12f;
            sr.scrollSensitivity = 30f;
            return content;
        }

        /// Removes every child now (so the layout never sees the old ones).
        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                c.SetActive(false);
                c.transform.SetParent(null, false);
                Object.Destroy(c);
            }
        }

        /// Collapsible section like the Godot inspector: a header with an
        /// arrow, and the body that is returned.
        public static Transform Section(Transform parent, string title, string icon, bool open, UnityAction<bool> toggled)
        {
            var sec = Rect(parent, "Seccion_" + title);
            var col = Column(sec.gameObject, 6f, new RectOffset(0, 0, 0, 4));
            col.childForceExpandWidth = true;
            var head = Box(sec, "Cabecera", Panel2, 10);
            Le(head.gameObject, 42f);
            var b = MakeButton(head, null);
            Row(head.gameObject, 8f, new RectOffset(10, 10, 0, 0));
            var arrow = IconImage(head.transform, open ? "chevron_down" : "chevron_right", 18f, TextDim);
            Le(arrow.gameObject, 18f, 18f);
            if (icon != null && Icon(icon) != null)
            {
                var ic = IconImage(head.transform, icon, 20f, AccentLight);
                Le(ic.gameObject, 20f, 20f);
            }
            var t = Label(head.transform, title.ToUpperInvariant(), 15, TextColor, true);
            Fit(t, 10);
            Le(t.gameObject, 42f, -1f, 1f);
            t.GetComponent<LayoutElement>().preferredHeight = 42f;
            var body = Rect(sec, "Cuerpo");
            var bc = Column(body.gameObject, 8f, new RectOffset(4, 4, 2, 4));
            bc.childForceExpandWidth = true;
            body.gameObject.SetActive(open);
            b.onClick.AddListener(() =>
            {
                bool on = !body.gameObject.activeSelf;
                body.gameObject.SetActive(on);
                arrow.texture = Icon(on ? "chevron_down" : "chevron_right");
                if (toggled != null) toggled(on);
            });
            return body;
        }

        /// Label on the left, the control(s) added by the caller on the right.
        public static Transform FieldRow(Transform parent, string label, float labelWidth = 96f, float height = 40f)
        {
            var row = Rect(parent, "Fila_" + label).gameObject;
            Row(row, 6f, new RectOffset(0, 0, 0, 0));
            Le(row, height);
            if (label != null)
            {
                var l = Label(row.transform, label, 16, TextDim);
                Fit(l, 10);
                Le(l.gameObject, height, labelWidth);
                l.GetComponent<LayoutElement>().preferredHeight = height;
            }
            return row.transform;
        }

        /// Label, slider and its value. `changed` while dragging; `released`
        /// when the finger leaves (one undo step).
        public static Slider SliderRow(Transform parent, string label, float min, float max, float value, string format, UnityAction<float> changed, System.Action released)
        {
            var row = FieldRow(parent, label, 92f, 40f);
            var area = Rect(row, "Deslizador");
            Le(area.gameObject, 40f, -1f, 1f);
            var hit = area.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var track = Box(area, "Pista", Panel3, 3);
            track.raycastTarget = false;
            var tr = track.rectTransform;
            tr.anchorMin = new Vector2(0f, 0.5f);
            tr.anchorMax = new Vector2(1f, 0.5f);
            tr.sizeDelta = new Vector2(0f, 6f);
            var fillArea = Rect(area, "Zona");
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(0f, 6f);
            var fill = Box(fillArea, "Relleno", Accent, 3);
            fill.raycastTarget = false;
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Rect(area, "ZonaCabezal");
            handleArea.anchorMin = new Vector2(0f, 0.5f);
            handleArea.anchorMax = new Vector2(1f, 0.5f);
            handleArea.sizeDelta = new Vector2(-22f, 22f);
            var handle = Box(handleArea, "Cabezal", Color.white, 11);
            handle.rectTransform.sizeDelta = new Vector2(22f, 0f);
            var s = area.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.direction = Slider.Direction.LeftToRight;
            var nav = s.navigation;
            nav.mode = Navigation.Mode.None;
            s.navigation = nav;
            s.minValue = min;
            s.maxValue = max;
            s.SetValueWithoutNotify(Mathf.Clamp(value, min, max));
            var vt = Label(row, Mathf.Clamp(value, min, max).ToString(format), 15, TextColor, true, TextAnchor.MiddleRight);
            vt.horizontalOverflow = HorizontalWrapMode.Overflow;
            Le(vt.gameObject, 40f, 46f);
            s.onValueChanged.AddListener(v =>
            {
                vt.text = v.ToString(format);
                if (changed != null) changed(v);
            });
            if (released != null)
            {
                var hook = area.gameObject.AddComponent<PointerUpHook>();
                hook.onUp = released;
            }
            return s;
        }

        /// On / off switch with its label.
        public static Button Switch(Transform parent, string label, bool on, UnityAction<bool> changed)
        {
            var row = Box(parent, "Interruptor", new Color(0, 0, 0, 0), 8);
            Le(row.gameObject, 42f);
            Row(row.gameObject, 8f, new RectOffset(0, 0, 0, 0));
            var b = MakeButton(row, null);
            var l = Label(row.transform, label, 16, TextColor);
            Fit(l, 11);
            Le(l.gameObject, 42f, -1f, 1f);
            l.GetComponent<LayoutElement>().preferredHeight = 42f;
            var pill = Box(row.transform, "Pastilla", on ? AccentButton : Panel3, 14);
            pill.raycastTarget = false;
            Le(pill.gameObject, 28f, 50f);
            var knob = Box(pill.transform, "Bolita", Color.white, 11);
            knob.raycastTarget = false;
            var kr = knob.rectTransform;
            kr.sizeDelta = new Vector2(22f, 22f);
            kr.anchorMin = kr.anchorMax = new Vector2(on ? 1f : 0f, 0.5f);
            kr.pivot = new Vector2(on ? 1f : 0f, 0.5f);
            kr.anchoredPosition = new Vector2(on ? -3f : 3f, 0f);
            bool state = on;
            b.onClick.AddListener(() =>
            {
                state = !state;
                pill.color = state ? AccentButton : Panel3;
                kr.anchorMin = kr.anchorMax = new Vector2(state ? 1f : 0f, 0.5f);
                kr.pivot = new Vector2(state ? 1f : 0f, 0.5f);
                kr.anchoredPosition = new Vector2(state ? -3f : 3f, 0f);
                if (changed != null) changed(state);
            });
            return b;
        }

        /// Small rounded button with centred text (presets, choices).
        public static Button Chip(Transform parent, string text, UnityAction onClick, bool active = false)
        {
            var img = Box(parent, "Chip", active ? AccentDim : Panel3, 10);
            var b = MakeButton(img, onClick);
            var t = Label(img.transform, text, 15, active ? AccentLight : TextColor, true, TextAnchor.MiddleCenter);
            Fill(t.rectTransform, 4f, 4f);
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 11;
            t.resizeTextMaxSize = 15;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return b;
        }

        /// Grid of `columns` equal cells filling `width`.
        public static Transform Grid(Transform parent, int columns, float width, float cellHeight, float spacing = 6f)
        {
            var go = Rect(parent, "Rejilla").gameObject;
            var g = go.AddComponent<GridLayoutGroup>();
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = columns;
            g.spacing = new Vector2(spacing, spacing);
            g.cellSize = new Vector2(Mathf.Floor((width - spacing * (columns - 1)) / columns), cellHeight);
            g.childAlignment = TextAnchor.UpperLeft;
            return go.transform;
        }

        /// Plain text field (names).
        public static InputField TextField(Transform parent, string value, UnityAction<string> done, float height = 44f)
        {
            var box = Box(parent, "Campo", Panel, 10);
            Le(box.gameObject, height, -1f, 1f);
            var t = Label(box.transform, "", 18, TextColor, true);
            Fill(t.rectTransform, 12f, 12f);
            t.raycastTarget = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var f = box.gameObject.AddComponent<InputField>();
            f.textComponent = t;
            f.text = value ?? "";
            f.characterLimit = 60;
            if (done != null) f.onEndEdit.AddListener(done);
            return f;
        }

        /// Dims the screen and shows a card in the middle: a message, an
        /// optional text field and the buttons. `ok` gets the field's text.
        public static GameObject Dialog(Transform root, string title, string message, string input, string okText, bool danger, UnityAction<string> ok)
        {
            var dim = Box(root, "Dialogo", new Color(0f, 0f, 0f, 0.6f));
            Fill(dim.rectTransform);
            var close = MakeButton(dim, null);
            var colors = close.colors;
            colors.pressedColor = Color.white;
            close.colors = colors;
            var card = Box(dim.transform, "Tarjeta", Panel2, 18);
            // Never wider than the screen (small phones).
            float rw = root is RectTransform ? ((RectTransform)root).rect.width : 1000f;
            Pin(card.rectTransform, 0.5f, 0.5f, Vector2.zero, new Vector2(Mathf.Min(470f, Mathf.Max(260f, rw - 32f)), 0f));
            card.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var col = Column(card.gameObject, 12f, new RectOffset(22, 22, 20, 20));
            col.childForceExpandWidth = true;
            var fit = card.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var tl = Label(card.transform, title, 21, TextColor, true);
            Le(tl.gameObject, 30f);
            if (!string.IsNullOrEmpty(message))
            {
                var ml = Label(card.transform, message, 16, TextDim);
            }
            InputField field = null;
            if (input != null) field = TextField(card.transform, input, null, 48f);
            var row = Rect(card.transform, "Botones").gameObject;
            Row(row, 8f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleRight);
            Le(row, 54f);
            Spacer(row.transform);
            var go = dim.gameObject;
            TextButton(row.transform, null, "Cancelar", () => Object.Destroy(go), false, 48f, 17, 130f);
            var okb = TextButton(row.transform, null, okText, () =>
            {
                string v = field != null ? field.text : "";
                Object.Destroy(go);
                if (ok != null) ok(v);
            }, !danger, 48f, 17, 150f);
            if (danger) okb.GetComponent<Image>().color = new Color(0.62f, 0.18f, 0.21f, 1f);
            close.onClick.AddListener(() => Object.Destroy(go));
            // The card itself swallows taps (only the dim area closes).
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            return go;
        }

        public class MenuItem
        {
            public string icon, text;
            public System.Action action;
            public bool check, on, header;

            public MenuItem(string icon, string text, System.Action action) { this.icon = icon; this.text = text; this.action = action; }
            public static MenuItem Check(string icon, string text, bool on, System.Action action) { return new MenuItem(icon, text, action) { check = true, on = on }; }
            public static MenuItem Header(string text) { return new MenuItem(null, text, null) { header = true }; }
        }

        /// Pop-up menu next to `screenPos` (like Godot's PopupMenu); a tap
        /// outside closes it.
        public static GameObject Popup(RectTransform root, Vector2 screenPos, List<MenuItem> entries, float width = 330f, bool dropDown = false)
        {
            var dim = Box(root, "Dialogo", new Color(0f, 0f, 0f, 0.15f));
            Fill(dim.rectTransform);
            var go = dim.gameObject;
            var close = dim.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(() => Object.Destroy(go));
            float h = 16f;
            foreach (var e in entries) h += (e.header ? 28f : 46f) + 2f;
            float maxH = root.rect.height * 0.8f;
            float ch = Mathf.Min(h, maxH);
            var card = Box(dim.transform, "Menu", Panel2, 14);
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            var cr = card.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(0f, 0f);
            cr.pivot = new Vector2(0f, 0f);
            // Wide enough for the longest entry (long Spanish / Arabic texts
            // were cut off), never wider than the screen.
            float longest = 0f;
            foreach (var e in entries)
            {
                if (e.header || string.IsNullOrEmpty(e.text)) continue;
                var probe = Label(root, Lang.T(e.text), 16, TextColor);
                longest = Mathf.Max(longest, probe.preferredWidth);
                Object.Destroy(probe.gameObject);
            }
            width = Mathf.Max(width, longest + 96f);
            width = Mathf.Min(width, Mathf.Max(220f, root.rect.width - 20f));
            cr.sizeDelta = new Vector2(width, ch);
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPos, null, out local);
            // root pivot -> bottom-left coordinates
            float lx = local.x - root.rect.xMin, ly = local.y - root.rect.yMin;
            float x = Mathf.Clamp(dropDown ? lx : lx - width * 0.5f, 8f, root.rect.width - width - 8f);
            float y = dropDown ? ly - ch - 2f : ly + 12f;
            if (!dropDown && y + ch > root.rect.height - 8f) y = ly - ch - 12f;
            y = Mathf.Clamp(y, 8f, root.rect.height - ch - 8f);
            cr.anchoredPosition = new Vector2(x, y);
            ScrollRect sr;
            var content = ScrollArea(card.transform, out sr, 2f, new RectOffset(8, 8, 8, 8));
            foreach (var e0 in entries)
            {
                var e = e0;
                if (e.header)
                {
                    var hl = Label(content, Lang.T(e.text).ToUpperInvariant(), 13, TextDim, true);
                    Le(hl.gameObject, 28f);
                    continue;
                }
                string text = e.check ? (e.on ? "✓  " : "     ") + Lang.T(e.text) : e.text;
                var b = TextButton(content, e.icon, text, () =>
                {
                    Object.Destroy(go);
                    if (e.action != null) e.action();
                }, false, 46f, 16);
                b.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            }
            return go;
        }
    }

    /// Calls `onUp` when the finger that pressed this control is lifted.
    public class PointerUpHook : MonoBehaviour, IPointerUpHandler
    {
        public System.Action onUp;

        public void OnPointerUp(PointerEventData e)
        {
            if (onUp != null) onUp();
        }
    }
}
