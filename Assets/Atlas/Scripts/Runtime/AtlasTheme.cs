// ATLAS 3D (Unity) - look of the interface, the same as the Godot version:
// dark rounded panels, purple accent, ATLAS's own stroke icons (white PNGs
// tinted per state) and the Inter font. Every button and panel is made here.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
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
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
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
            float w = width > 0f ? width : (icon != null ? 52f : 28f) + text.Length * 10.4f;
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
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
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
    }
}
