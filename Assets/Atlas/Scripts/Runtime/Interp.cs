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

        public static string Name(string mode)
        {
            if (string.IsNullOrEmpty(mode)) mode = EaseInOut;
            int i = System.Array.IndexOf(Modes, mode);
            return i >= 0 ? Names[i] : "Suave (ease in-out)";
        }

        /// Shapes t in [0, 1] for the mode (cubic is handled by the caller).
        public static float Shape(string mode, float t)
        {
            if (string.IsNullOrEmpty(mode)) mode = EaseInOut;
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
