// ATLAS 3D (Unity) - look of the interface: the same dark panels and
// purple accent as the Godot version, built with simple touch-sized
// uGUI pieces (one place to change the style of every button and panel).
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
        public static readonly Color Accent = Hex("b25cff");
        public static readonly Color AccentDim = Hex("6b2fc0");
        public static readonly Color AccentLight = Hex("d8b4ff");
        public static readonly Color TextColor = Hex("e8eaf0");
        public static readonly Color TextDim = Hex("959ba7");
        public static readonly Color Danger = Hex("e5484d");
        public static readonly Color AxisX = Hex("e5484d");
        public static readonly Color AxisY = Hex("46c46e");
        public static readonly Color AxisZ = Hex("3d8bfd");

        static Font font;

        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Color Hex(string h)
        {
            Color c;
            ColorUtility.TryParseHtmlString("#" + h, out c);
            return c;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Box(Transform parent, string name, Color c)
        {
            var r = Rect(parent, name);
            var img = r.gameObject.AddComponent<Image>();
            img.color = c;
            return img;
        }

        public static void Fill(RectTransform r, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(left, bottom);
            r.offsetMax = new Vector2(-right, -top);
        }

        public static Text Label(Transform parent, string text, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var r = Rect(parent, "Texto");
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// A rounded-looking touch button (44 px tall at least).
        public static Button Button(Transform parent, string text, UnityAction onClick, bool accent = false, float width = 0f, float height = 48f, int fontSize = 19)
        {
            var img = Box(parent, "Boton", accent ? AccentDim : Panel3);
            var r = img.rectTransform;
            r.sizeDelta = new Vector2(width > 0f ? width : text.Length * 10.5f + 32f, height);
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = r.sizeDelta.x;
            le.preferredHeight = height;
            le.minHeight = height;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.8f, 0.7f, 1f, 1f);
            colors.selectedColor = Color.white;
            b.colors = colors;
            if (onClick != null) b.onClick.AddListener(onClick);
            var t = Label(r, text, fontSize, TextColor, FontStyle.Bold, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Fill(t.rectTransform, 6f, 6f);
            return b;
        }

        public static void SetButtonText(Button b, string text)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = text;
        }

        /// Highlighted (active tool) or normal look.
        public static void SetActive(Button b, bool on)
        {
            var img = b.GetComponent<Image>();
            if (img != null) img.color = on ? AccentDim : Panel3;
        }

        public static HorizontalLayoutGroup Row(GameObject go, float spacing, int padding)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(padding, padding, padding, padding);
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            return h;
        }

        public static VerticalLayoutGroup Column(GameObject go, float spacing, int padding)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static void Flexible(GameObject go)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
        }
    }
}
