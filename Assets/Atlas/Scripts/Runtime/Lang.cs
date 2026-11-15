// ATLAS 3D (Unity) - languages: Spanish (as written in the code), English
// and Arabic. Texts are translated when they are put on screen, from the
// table in Resources/Lang.txt (Spanish § English § Arabic). Arabic is
// drawn with joined letters and right to left (Unity's text does neither):
// letters are turned into their joined forms and each line is reversed,
// with numbers and Latin words kept readable.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public static class Lang
    {
        public static string Current = "es";
        static Dictionary<string, string> table;
        static List<KeyValuePair<string, string>> fragments;
        static Font arabicFont;

        public static bool Arabic { get { return Current == "ar"; } }

        public static readonly string[] Codes = { "es", "en", "ar" };
        public static readonly string[] Names = { "Español", "English", "العربية" };

        public static void Load(string code)
        {
            Current = System.Array.IndexOf(Codes, code) >= 0 ? code : "es";
            table = null;
            fragments = null;
            if (Current == "es") return;
            table = new Dictionary<string, string>();
            fragments = new List<KeyValuePair<string, string>>();
            var ta = Resources.Load<TextAsset>("Lang");
            if (ta == null) return;
            int col = Current == "en" ? 1 : 2;
            foreach (var raw in ta.text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var p = line.Split('§');
                if (p.Length < 3) continue;
                string es = p[0].Replace("\\n", "\n"), tr = p[col].Replace("\\n", "\n");
                if (es.Length == 0 || tr.Length == 0) continue;
                table[es] = tr;
                // Pieces joined to names or numbers in the code («Proyecto «» + name…).
                if (es.Length >= 4 && (IsEdge(es[0]) || IsEdge(es[es.Length - 1]) || es.Contains("\n")))
                    fragments.Add(new KeyValuePair<string, string>(es, tr));
            }
            fragments.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
        }

        static bool IsEdge(char c)
        {
            return c == ' ' || c == '«' || c == '»' || c == ':' || c == '(' || c == ')' || c == '·' || c == '\n' || c == ',' || c == '…';
        }

        /// The text in the current language (Spanish when not translated).
        public static string T(string s)
        {
            if (table == null || string.IsNullOrEmpty(s)) return s;
            string v;
            if (table.TryGetValue(s, out v)) return v;
            string o = s;
            foreach (var kv in fragments)
                if (o.Contains(kv.Key)) o = o.Replace(kv.Key, kv.Value);
            return o;
        }

        public static Font ArabicFont
        {
            get
            {
                if (arabicFont == null) arabicFont = Resources.Load<Font>("Fonts/AtlasArabic");
                return arabicFont;
            }
        }

        /// Puts `s` on a Text in the current language (Arabic: shaped, right
        /// to left, wrapped by lines).
        public static void Set(Text t, string s)
        {
            s = T(s);
            if (!Arabic || t == null) { if (t != null) t.text = s; return; }
            if (ArabicFont != null) t.font = ArabicFont;
            switch (t.alignment)
            {
                case TextAnchor.UpperLeft: t.alignment = TextAnchor.UpperRight; break;
                case TextAnchor.MiddleLeft: t.alignment = TextAnchor.MiddleRight; break;
                case TextAnchor.LowerLeft: t.alignment = TextAnchor.LowerRight; break;
            }
            var rtl = t.GetComponent<RtlText>();
            if (rtl == null) rtl = t.gameObject.AddComponent<RtlText>();
            rtl.SetLogical(ArabicShaper.Shape(s));
        }
    }

    /// Keeps an Arabic text in visual order, broken into lines that fit.
    public class RtlText : MonoBehaviour
    {
        string logical = "";
        float lastWidth = -1f;
        Text text;

        public void SetLogical(string s)
        {
            logical = s ?? "";
            if (text == null) text = GetComponent<Text>();
            lastWidth = -1f;
            Refresh();
        }

        void OnRectTransformDimensionsChange() { if (isActiveAndEnabled) Refresh(); }

        void LateUpdate()
        {
            if (text == null) return;
            float w = ((RectTransform)transform).rect.width;
            if (Mathf.Abs(w - lastWidth) > 0.5f) Refresh();
        }

        void Refresh()
        {
            if (text == null) text = GetComponent<Text>();
            if (text == null) return;
            var rt = (RectTransform)transform;
            float w = rt.rect.width;
            lastWidth = w;
            var sb = new StringBuilder();
            foreach (var para in logical.Split('\n'))
            {
                var lines = new List<string>();
                if (w > 1f && text.horizontalOverflow == HorizontalWrapMode.Wrap)
                {
                    var gen = new TextGenerator();
                    var settings = text.GetGenerationSettings(new Vector2(w, 10000f));
                    gen.Populate(para, settings);
                    var ls = gen.lines;
                    for (int i = 0; i < ls.Count; i++)
                    {
                        int a = Mathf.Clamp(ls[i].startCharIdx, 0, para.Length);
                        int b = i + 1 < ls.Count ? Mathf.Clamp(ls[i + 1].startCharIdx, 0, para.Length) : para.Length;
                        if (b > a) lines.Add(para.Substring(a, b - a).Trim());
                    }
                }
                if (lines.Count == 0) lines.Add(para);
                foreach (var l in lines)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(ArabicShaper.Visual(l));
                }
            }
            text.text = sb.ToString();
        }
    }

    /// Arabic letters in their joined forms (Unicode presentation forms) and
    /// lines in visual order for a left-to-right text engine.
    public static class ArabicShaper
    {
        // isolated, final, initial, medial (0 = none); 0x0621..0x064A
        static readonly int[,] Forms =
        {
            { 0xFE80, 0, 0, 0 }, { 0xFE81, 0xFE82, 0, 0 }, { 0xFE83, 0xFE84, 0, 0 }, { 0xFE85, 0xFE86, 0, 0 },
            { 0xFE87, 0xFE88, 0, 0 }, { 0xFE89, 0xFE8A, 0xFE8B, 0xFE8C }, { 0xFE8D, 0xFE8E, 0, 0 }, { 0xFE8F, 0xFE90, 0xFE91, 0xFE92 },
            { 0xFE93, 0xFE94, 0, 0 }, { 0xFE95, 0xFE96, 0xFE97, 0xFE98 }, { 0xFE99, 0xFE9A, 0xFE9B, 0xFE9C }, { 0xFE9D, 0xFE9E, 0xFE9F, 0xFEA0 },
            { 0xFEA1, 0xFEA2, 0xFEA3, 0xFEA4 }, { 0xFEA5, 0xFEA6, 0xFEA7, 0xFEA8 }, { 0xFEA9, 0xFEAA, 0, 0 }, { 0xFEAB, 0xFEAC, 0, 0 },
            { 0xFEAD, 0xFEAE, 0, 0 }, { 0xFEAF, 0xFEB0, 0, 0 }, { 0xFEB1, 0xFEB2, 0xFEB3, 0xFEB4 }, { 0xFEB5, 0xFEB6, 0xFEB7, 0xFEB8 },
            { 0xFEB9, 0xFEBA, 0xFEBB, 0xFEBC }, { 0xFEBD, 0xFEBE, 0xFEBF, 0xFEC0 }, { 0xFEC1, 0xFEC2, 0xFEC3, 0xFEC4 }, { 0xFEC5, 0xFEC6, 0xFEC7, 0xFEC8 },
            { 0xFEC9, 0xFECA, 0xFECB, 0xFECC }, { 0xFECD, 0xFECE, 0xFECF, 0xFED0 },
            { 0, 0, 0, 0 }, { 0, 0, 0, 0 }, { 0, 0, 0, 0 }, { 0, 0, 0, 0 }, { 0, 0, 0, 0 },   // 063B..063F
            { 0x0640, 0x0640, 0x0640, 0x0640 },                                                 // tatweel
            { 0xFED1, 0xFED2, 0xFED3, 0xFED4 }, { 0xFED5, 0xFED6, 0xFED7, 0xFED8 }, { 0xFED9, 0xFEDA, 0xFEDB, 0xFEDC }, { 0xFEDD, 0xFEDE, 0xFEDF, 0xFEE0 },
            { 0xFEE1, 0xFEE2, 0xFEE3, 0xFEE4 }, { 0xFEE5, 0xFEE6, 0xFEE7, 0xFEE8 }, { 0xFEE9, 0xFEEA, 0xFEEB, 0xFEEC }, { 0xFEED, 0xFEEE, 0, 0 },
            { 0xFEEF, 0xFEF0, 0, 0 }, { 0xFEF1, 0xFEF2, 0xFEF3, 0xFEF4 },
        };

        static bool IsLetter(char c) { return c >= 0x0621 && c <= 0x064A && Forms[c - 0x0621, 0] != 0; }
        static bool IsMark(char c) { return (c >= 0x064B && c <= 0x065F) || c == 0x0670; }
        static bool Dual(char c) { return IsLetter(c) && Forms[c - 0x0621, 2] != 0; }

        static int LamAlef(char alef, bool final)
        {
            switch ((int)alef)
            {
                case 0x0622: return final ? 0xFEF6 : 0xFEF5;
                case 0x0623: return final ? 0xFEF8 : 0xFEF7;
                case 0x0625: return final ? 0xFEFA : 0xFEF9;
                case 0x0627: return final ? 0xFEFC : 0xFEFB;
            }
            return 0;
        }

        /// Joined forms, still in reading (logical) order.
        public static string Shape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            int n = s.Length;
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                if (!IsLetter(c)) { sb.Append(c); continue; }
                int p = i - 1;
                while (p >= 0 && IsMark(s[p])) p--;
                int q = i + 1;
                while (q < n && IsMark(s[q])) q++;
                bool joinPrev = p >= 0 && Dual(s[p]);
                bool joinNext = Dual(c) && q < n && IsLetter(s[q]);
                if (c == 0x0644 && q < n)
                {
                    int lig = LamAlef(s[q], joinPrev);
                    if (lig != 0)
                    {
                        sb.Append((char)lig);
                        for (int k = i + 1; k < q; k++) sb.Append(s[k]);   // marks
                        i = q;
                        continue;
                    }
                }
                int form = joinPrev && joinNext ? 3 : joinPrev ? 1 : joinNext ? 2 : 0;
                int g = Forms[c - 0x0621, form];
                if (g == 0) g = Forms[c - 0x0621, joinPrev ? 1 : 0];
                sb.Append((char)(g != 0 ? g : c));
            }
            return sb.ToString();
        }

        static bool IsRtl(char c)
        {
            return (c >= 0x0590 && c <= 0x08FF) || (c >= 0xFB1D && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF);
        }

        static bool IsLtr(char c)
        {
            return char.IsLetterOrDigit(c) && !IsRtl(c);
        }

        static char Mirror(char c)
        {
            switch (c)
            {
                case '(': return ')';
                case ')': return '(';
                case '[': return ']';
                case ']': return '[';
                case '«': return '»';
                case '»': return '«';
                case '<': return '>';
                case '>': return '<';
                case '←': return '→';
                case '→': return '←';
            }
            return c;
        }

        /// One line in the order a left-to-right engine must draw it: the
        /// line reversed, but Latin words and numbers left as they are.
        public static string Visual(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            int n = line.Length;
            // Each char: true = part of a left-to-right run.
            var ltr = new bool[n];
            for (int i = 0; i < n; i++) ltr[i] = IsLtr(line[i]);
            // Neutral characters between two left-to-right ones join them
            // («GLB, glTF», «1.5», «#FF8800»).
            for (int i = 0; i < n; i++)
            {
                if (ltr[i] || IsRtl(line[i])) continue;
                int a = i - 1, b = i;
                while (b < n && !ltr[b] && !IsRtl(line[b])) b++;
                if (a >= 0 && ltr[a] && b < n && ltr[b])
                    for (int k = i; k < b; k++) ltr[k] = true;
                i = b - 1;
            }
            var outp = new StringBuilder(n);
            int j = n - 1;
            while (j >= 0)
            {
                if (ltr[j])
                {
                    int e = j;
                    while (j >= 0 && ltr[j]) j--;
                    outp.Append(line, j + 1, e - j);
                }
                else
                {
                    outp.Append(Mirror(line[j]));
                    j--;
                }
            }
            return outp.ToString();
        }
    }
}
