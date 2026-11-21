// ATLAS 3D (Unity) - keyframe interpolation, the same curves as the Godot
// version. The mode stored on a key applies to the segment from that key
// to the next one.
using UnityEngine;

namespace Atlas
{
    public static class Interp
    {
        public const string Constant = "constant", Linear = "linear", EaseIn = "ease_in", EaseOut = "ease_out",
            EaseInOut = "ease_in_out", Cubic = "cubic", Heavy = "heavy", Bounce = "bounce", Elastic = "elastic", Back = "back";

        public static readonly string[] Modes = { Constant, Linear, EaseIn, EaseOut, EaseInOut, Cubic, Heavy, Bounce, Elastic, Back };
        public static readonly string[] Names =
        {
            "Constante", "Lineal", "Aceleración (ease in)", "Desaceleración (ease out)", "Suave (ease in-out)",
            "Cúbica (curva suave)", "Pesada (arranque y frenado lentos)", "Rebote", "Elástica (muelle)", "Pasarse y volver",
        };

        // ------------------------------------------------------------------
        // Custom curve (editor de curvas): "bez:x1,y1,x2,y2", a cubic Bézier
        // from (0,0) to (1,1) with two handles, like CSS / After Effects.
        public const string BezPrefix = "bez:";

        public static bool IsBezier(string mode) { return mode != null && mode.StartsWith(BezPrefix); }

        public static string Bezier(Vector4 h)
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return BezPrefix + Mathf.Clamp01(h.x).ToString("0.###", c) + "," + h.y.ToString("0.###", c) + "," + Mathf.Clamp01(h.z).ToString("0.###", c) + "," + h.w.ToString("0.###", c);
        }

        static string lastBez;
        static Vector4 lastHandles;

        /// The two handles of a custom curve (x1, y1, x2, y2).
        public static Vector4 Handles(string mode)
        {
            if (mode == lastBez) return lastHandles;
            var h = new Vector4(0.42f, 0f, 0.58f, 1f);
            if (IsBezier(mode))
            {
                var p = mode.Substring(BezPrefix.Length).Split(',');
                float v;
                var c = System.Globalization.CultureInfo.InvariantCulture;
                if (p.Length == 4)
                    for (int i = 0; i < 4; i++)
                        if (float.TryParse(p[i], System.Globalization.NumberStyles.Float, c, out v)) h[i] = v;
                h.x = Mathf.Clamp01(h.x);
                h.z = Mathf.Clamp01(h.z);
            }
            lastBez = mode;
            lastHandles = h;
            return h;
        }

        static float Bez1(float a, float b, float t)
        {
            float u = 1f - t;
            return 3f * u * u * t * a + 3f * u * t * t * b + t * t * t;
        }

        /// y for x on the curve (x found by bisection: always steady).
        public static float BezierAt(Vector4 h, float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            float lo = 0f, hi = 1f, t = x;
            for (int i = 0; i < 24; i++)
            {
                t = (lo + hi) * 0.5f;
                if (Bez1(h.x, h.z, t) < x) lo = t; else hi = t;
            }
            return Bez1(h.y, h.w, t);
        }

        /// Handles that match a ready-made mode (to start editing from it).
        public static Vector4 HandlesFor(string mode)
        {
            if (IsBezier(mode)) return Handles(mode);
            switch (string.IsNullOrEmpty(mode) ? EaseInOut : mode)
            {
                case Linear: return new Vector4(0.33f, 0.33f, 0.67f, 0.67f);
                case EaseIn: return new Vector4(0.55f, 0f, 1f, 0.45f);
                case EaseOut: return new Vector4(0f, 0.55f, 0.45f, 1f);
                case Heavy: return new Vector4(0.8f, 0f, 0.2f, 1f);
                case Back: return new Vector4(0.34f, 1.56f, 0.64f, 1f);
            }
            return new Vector4(0.42f, 0f, 0.58f, 1f);
        }

        public static string Name(string mode)
        {
            if (IsBezier(mode)) return "Curva personalizada";
            if (string.IsNullOrEmpty(mode)) mode = EaseInOut;
            int i = System.Array.IndexOf(Modes, mode);
            return i >= 0 ? Names[i] : "Suave (ease in-out)";
        }

        /// Shapes t in [0, 1] for the mode (cubic is handled by the caller).
        public static float Shape(string mode, float t)
        {
            if (string.IsNullOrEmpty(mode)) mode = EaseInOut;
            if (IsBezier(mode)) return BezierAt(Handles(mode), t);
            switch (mode)
            {
                case Constant: return 0f;
                case Linear: return t;
                case EaseIn: return t * t * t;
                case EaseOut: return 1f - Mathf.Pow(1f - t, 3f);
                case Cubic:
                case EaseInOut: return t * t * (3f - 2f * t);
                case Heavy: return t < 0.5f ? 16f * Mathf.Pow(t, 5f) : 1f - Mathf.Pow(-2f * t + 2f, 5f) / 2f;
                case Bounce:
                {
                    const float n1 = 7.5625f, d1 = 2.75f;
                    if (t < 1f / d1) return n1 * t * t;
                    if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
                    if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
                    t -= 2.625f / d1;
                    return n1 * t * t + 0.984375f;
                }
                case Elastic:
                    if (t <= 0f || t >= 1f) return t;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * Mathf.PI * 2f / 3f) + 1f;
                case Back:
                {
                    const float c1 = 1.70158f;
                    return 1f + (c1 + 1f) * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
                }
            }
            return t;
        }

        /// p0..p3: previous, start, end, next (Catmull-Rom for «cubic»).
        public static Vector3 Blend(string mode, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            if (mode == Constant) return p1;
            if (mode == Cubic)
            {
                float t2 = t * t, t3 = t2 * t;
                return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
            }
            return Vector3.LerpUnclamped(p1, p2, Shape(mode, t));
        }

        public static Quaternion Blend(string mode, Quaternion a, Quaternion b, float t)
        {
            if (mode == Constant) return a;
            if (Quaternion.Dot(a, b) < 0f) b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            return Quaternion.SlerpUnclamped(a, b, Shape(mode, t));
        }
    }
}
