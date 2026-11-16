// ATLAS 3D (Unity) - the shapes Unity does not have (cone, pyramid,
// torus), made from code with normals and UVs, plus the material helpers
// (opaque / transparent) shared by the app and the build script.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas
{
    public static class Primitives
    {
        static Mesh cone, pyramid, torus, ring;

        /// Thin ring on the ground plane (the rotate manipulator).
        public static Mesh Ring(float radius, float tube)
        {
            if (ring == null) ring = Torus(72, 6, radius, tube);
            return ring;
        }

        public static Mesh Get(string kind)
        {
            switch (kind)
            {
                case "cone": if (cone == null) cone = Cone(40); return cone;
                case "pyramid": if (pyramid == null) pyramid = Pyramid(); return pyramid;
                case "torus": if (torus == null) torus = Torus(48, 20, 0.5f, 0.17f); return torus;
            }
            return null;
        }

        /// Base radius 0.5 at y = -0.5, tip at y = 0.5 (like Unity's cube).
        static Mesh Cone(int seg)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            float slope = 0.5f;   // radius / height
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 normal = (dir + Vector3.up * slope).normalized;
                v.Add(dir * 0.5f + Vector3.down * 0.5f); n.Add(normal); uv.Add(new Vector2(i / (float)seg, 0f));
                v.Add(Vector3.up * 0.5f); n.Add(normal); uv.Add(new Vector2(i / (float)seg, 1f));
            }
            for (int i = 0; i < seg; i++)
            {
                int b = i * 2;
                t.Add(b); t.Add(b + 1); t.Add(b + 2);
            }
            int c = v.Count;
            v.Add(Vector3.down * 0.5f); n.Add(Vector3.down); uv.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * 0.5f, -0.5f, Mathf.Sin(a) * 0.5f));
                n.Add(Vector3.down);
                uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            }
            for (int i = 0; i < seg; i++) { t.Add(c); t.Add(c + 1 + i); t.Add(c + 2 + i); }
            return Build("Cono", v, n, uv, t);
        }

        /// Square base 1 x 1 at y = -0.5, tip at y = 0.5. Flat faces.
        static Mesh Pyramid()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            Vector3 top = Vector3.up * 0.5f;
            Vector3[] b =
            {
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f),
            };
            for (int i = 0; i < 4; i++)
            {
                Vector3 p0 = b[i], p1 = b[(i + 1) % 4];
                Vector3 mid = (p0 + p1) * 0.5f;
                Vector3 normal = Vector3.Cross(p1 - p0, top - p0).normalized;
                if (Vector3.Dot(normal, mid) < 0f) normal = -normal;
                int s = v.Count;
                v.Add(p0); v.Add(p1); v.Add(top);
                n.Add(normal); n.Add(normal); n.Add(normal);
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(0.5f, 1f));
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
            }
            int c = v.Count;
            for (int i = 0; i < 4; i++)
            {
                v.Add(b[i]); n.Add(Vector3.down); uv.Add(new Vector2(b[i].x + 0.5f, b[i].z + 0.5f));
            }
            t.Add(c); t.Add(c + 1); t.Add(c + 2);
            t.Add(c); t.Add(c + 2); t.Add(c + 3);
            return Build("Piramide", v, n, uv, t);
        }

        /// Ring lying on the ground plane, centred on the origin.
        static Mesh Torus(int seg, int side, float r, float tube)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 ring = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int j = 0; j <= side; j++)
                {
                    float b = j / (float)side * Mathf.PI * 2f;
                    Vector3 normal = ring * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    v.Add(ring * r + normal * tube);
                    n.Add(normal);
                    uv.Add(new Vector2(i / (float)seg, j / (float)side));
                }
            }
            int row = side + 1;
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int a0 = i * row + j, a1 = (i + 1) * row + j;
                    t.Add(a0); t.Add(a1); t.Add(a0 + 1);
                    t.Add(a1); t.Add(a1 + 1); t.Add(a0 + 1);
                }
            return Build("Toroide", v, n, uv, t);
        }

        /// Puts every triangle's front face outside (Unity: the side where
        /// Cross(b - a, c - a) points), whatever order it was written in.
        static Mesh Build(string name, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)
        {
            for (int i = 0; i + 2 < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);
                Vector3 want = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
                if (Vector3.Dot(face, want) < 0f)
                {
                    int k = t[i + 1];
                    t[i + 1] = t[i + 2];
                    t[i + 2] = k;
                }
            }
            var m = new Mesh();
            m.name = name;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }

    public static class MatUtil
    {
        /// URP Lit: opaque, or see-through with the alpha of its colour.
        public static void SetTransparent(Material m, bool on)
        {
            if (m == null) return;
            bool now = m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT");
            if (now == on && m.renderQueue == (on ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry)) return;
            m.SetFloat("_Surface", on ? 1f : 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", on ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            m.SetFloat("_DstBlend", on ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", on ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", on ? 0f : 1f);
            m.SetOverrideTag("RenderType", on ? "Transparent" : "Opaque");
            if (on) m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = on ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
        }

        /// URP Lit cut-out: solid, with holes where the texture's alpha is
        /// below `cutoff` (hair, leaves, lace). Writes depth like an opaque
        /// surface, so a character never looks see-through.
        public static void SetCutout(Material m, float cutoff)
        {
            if (m == null) return;
            SetTransparent(m, false);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", cutoff);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
        }
    }
}
