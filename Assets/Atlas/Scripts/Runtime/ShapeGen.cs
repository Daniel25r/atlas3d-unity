// ATLAS 5D (Unity) - shapes with settings («Geometría» in the inspector):
// segments, rings and radius of a sphere, cylinder, cone, capsule,
// pyramid, torus, plane or cube; a radial deformation (ribs or grooves
// around an axis), a twist, holes, and the kind of faces (quads,
// triangles, or a sphere made from a cube: «R-Cube»).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas
{
    [Serializable]
    public class GeoParams
    {
        public bool on;
        public int seg, rings;          // 0 = the shape's usual amount
        public float radius = 1f;       // size of the shape
        public float deform;            // 0..1 radial ribs / grooves
        public int waves = 6;
        public bool sink;               // grooves instead of ribs
        public int axis = 1;            // 0 X, 1 Y, 2 Z
        public bool mirror = true;      // the whole height (else growing upwards)
        public float twist;             // degrees from bottom to top
        public bool holes;
        public int topo;                // 0 quads, 1 triangles, 2 R-Cube
        public float round = 0.15f;     // R-Cube on a cube: rounded edges
        public float height;            // cylinder, cone, pyramid, capsule: metres (0 = usual)
        public float radius2 = -1f;     // radius at the top (×0.5 m like `radius`; -1 = usual)

        public GeoParams Clone() { return (GeoParams)MemberwiseClone(); }
    }

    public static class ShapeGen
    {
        public static bool Supports(string kind)
        {
            switch (kind)
            {
                case "sphere": case "cylinder": case "cone": case "capsule": case "pyramid": case "torus": case "plane": case "cube": return true;
            }
            return false;
        }

        public static int DefaultSeg(string kind)
        {
            switch (kind)
            {
                case "cone": return 40;
                case "torus": return 48;
                case "pyramid": return 4;
                case "plane": return 10;
                case "cube": return 1;
            }
            return 24;
        }

        public static int DefaultRings(string kind)
        {
            switch (kind)
            {
                case "sphere": return 16;
                case "capsule": return 8;
                case "torus": return 20;
                case "plane": return 10;
                case "cube": return 1;
            }
            return 1;
        }

        public static bool HasRings(string kind) { return kind != "cube"; }
        public static bool HasHeight(string kind) { return kind == "cylinder" || kind == "cone" || kind == "pyramid" || kind == "capsule"; }
        public static bool HasTop(string kind) { return kind == "cylinder" || kind == "cone" || kind == "pyramid"; }

        public static float DefaultHeight(string kind)
        {
            return kind == "cylinder" || kind == "capsule" ? 2f : kind == "cone" || kind == "pyramid" ? 1f : 0f;
        }

        /// Metres: radius at the bottom, at the top, and the height.
        public static float Radius(GeoParams g) { return 0.5f * Mathf.Clamp(g.radius, 0.02f, 40f); }
        public static float Top(string kind, GeoParams g) { return g.radius2 >= 0f ? 0.5f * Mathf.Min(g.radius2, 40f) : (kind == "cylinder" ? Radius(g) : 0f); }
        public static float Height(string kind, GeoParams g) { return g.height > 0f ? Mathf.Min(g.height, 100f) : DefaultHeight(kind); }
        public static bool HasRCube(string kind) { return kind == "sphere" || kind == "cube"; }

        // ------------------------------------------------------------------

        class Poly
        {
            public Vector3[] p;
            public Vector2[] uv;
            public int cell;
        }

        public static Mesh Build(string kind, GeoParams g)
        {
            List<Vector3> verts, norms;
            List<Vector2> uvs;
            List<int> tris;
            if (!Arrays(kind, g, out verts, out norms, out uvs, out tris)) return null;
            var m = new Mesh { name = "Forma" };
            if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            if (tris.Count > 0) m.RecalculateTangents();
            return m;
        }

        public static bool Arrays(string kind, GeoParams g, out List<Vector3> verts, out List<Vector3> norms, out List<Vector2> uvs, out List<int> tris)
        {
            verts = norms = null; uvs = null; tris = null;
            var polys = Polys(kind, g);
            if (polys == null) return false;
            ToArrays(polys, g.holes, out verts, out norms, out uvs, out tris);
            return true;
        }

        /// The shape as faces (quads, n-gon lids) for Modelar: clean like
        /// Blender's, instead of rebuilding faces from loose triangles.
        public static EditMesh Editable(string kind, GeoParams g)
        {
            var polys = Polys(kind, g);
            if (polys == null) return null;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var faces = new List<int[]>();
            foreach (var f in polys)
            {
                var idx = new int[f.p.Length];
                for (int k = 0; k < idx.Length; k++) { idx[k] = v.Count; v.Add(f.p[k]); uv.Add(f.uv[k]); }
                faces.Add(idx);
            }
            return EditMesh.FromPolys(v.ToArray(), uv.ToArray(), faces);
        }

        static List<Poly> Polys(string kind, GeoParams g)
        {
            int seg = g.seg > 0 ? g.seg : DefaultSeg(kind);
            int rings = g.rings > 0 ? g.rings : DefaultRings(kind);
            var polys = new List<Poly>();
            bool tri = g.topo == 1;
            float R = Radius(g), R2 = Top(kind, g), H = Height(kind, g);
            switch (kind)
            {
                case "sphere":
                    if (g.topo == 1) IcoSphere(polys, Mathf.Clamp(rings / 6, 0, 5));
                    else if (g.topo == 2) CubeShape(polys, Mathf.Max(1, seg / 4), 0f, true);
                    else Lathe(polys, SphereProfile(Mathf.Max(2, rings)), Mathf.Max(3, seg), 0f, false);
                    break;
                case "cylinder":
                    Lathe(polys, Tube(-H / 2f, H / 2f, R, R2, Mathf.Max(1, rings)), Mathf.Max(3, seg), 0f, tri);
                    break;
                case "cone":
                    Lathe(polys, Tube(-H / 2f, H / 2f, R, R2, Mathf.Max(1, rings)), Mathf.Max(3, seg), 0f, tri);
                    break;
                case "pyramid":
                    Lathe(polys, Tube(-H / 2f, H / 2f, R * 1.41421356f, R2 * 1.41421356f, Mathf.Max(1, rings)), Mathf.Max(3, seg), 45f, tri);
                    break;
                case "capsule":
                    Lathe(polys, CapsuleProfile(Mathf.Max(1, rings), R, H), Mathf.Max(3, seg), 0f, tri);
                    break;
                case "torus":
                    Torus(polys, Mathf.Max(3, seg), Mathf.Max(3, rings), tri);
                    break;
                case "plane":
                    PlaneGrid(polys, Mathf.Max(1, seg), Mathf.Max(1, rings), tri);
                    break;
                case "cube":
                    CubeShape(polys, Mathf.Max(1, seg), g.topo == 2 ? Mathf.Clamp(g.round, 0.01f, 0.5f) : 0f, false);
                    if (tri) polys = Split(polys, true);
                    break;
                default: return null;
            }
            if (g.holes)
            {
                var kept = new List<Poly>();
                for (int i = 0; i < polys.Count; i++) if (!Hole(polys[i])) kept.Add(polys[i]);
                polys = kept;
            }
            Deform(polys, g, !HasHeight(kind));
            return polys;
        }

        // A checker of holes, by the face's place in its grid.
        static bool Hole(Poly f) { return (f.cell & 1) == 1; }

        // ------------------------------------------------------------------
        // Builders

        static List<Vector3> SphereProfile(int rings)
        {
            var l = new List<Vector3>();   // (radius, y, v)
            for (int j = 0; j <= rings; j++)
            {
                float t = j / (float)rings, a = Mathf.PI * t;
                l.Add(new Vector3(Mathf.Sin(a) * 0.5f, -Mathf.Cos(a) * 0.5f, t));
            }
            return l;
        }

        static List<Vector3> Tube(float y0, float y1, float r0, float r1, int rings)
        {
            var l = new List<Vector3>();
            l.Add(new Vector3(0f, y0, 0f));
            for (int j = 0; j <= rings; j++)
            {
                float t = j / (float)rings;
                l.Add(new Vector3(Mathf.Lerp(r0, r1, t), Mathf.Lerp(y0, y1, t), 0.1f + 0.8f * t));
            }
            if (r1 > 0f) l.Add(new Vector3(0f, y1, 1f));
            return l;
        }

        static List<Vector3> CapsuleProfile(int rings, float r, float h)
        {
            var l = new List<Vector3>();
            int hr = Mathf.Max(2, rings);
            float c = Mathf.Max(0f, h / 2f - r);   // half of the straight part
            for (int j = 0; j <= hr; j++)
            {
                float a = Mathf.PI * 0.5f * j / hr;
                l.Add(new Vector3(Mathf.Sin(a) * r, -c - Mathf.Cos(a) * r, 0.25f * j / hr));
            }
            for (int j = c > 1e-5f ? 0 : 1; j <= hr; j++)
            {
                float a = Mathf.PI * 0.5f * j / hr;
                l.Add(new Vector3(Mathf.Cos(a) * r, c + Mathf.Sin(a) * r, 0.75f + 0.25f * j / hr));
            }
            return l;
        }

        /// Turns a profile (x = radius, y = height, z = v) around Y.
        static void Lathe(List<Poly> polys, List<Vector3> prof, int seg, float startDeg, bool tri)
        {
            float a0 = startDeg * Mathf.Deg2Rad;
            for (int j = 0; j < prof.Count - 1; j++)
            {
                Vector3 p0 = prof[j], p1 = prof[j + 1];
                // Triangles: every other ring is turned by half a step.
                float o0 = tri && (j & 1) == 1 ? 0.5f : 0f, o1 = tri && ((j + 1) & 1) == 1 ? 0.5f : 0f;
                for (int i = 0; i < seg; i++)
                {
                    Vector3 A = Ring(p0, (i + o0) / seg, a0), B = Ring(p0, (i + 1 + o0) / seg, a0);
                    Vector3 C = Ring(p1, (i + 1 + o1) / seg, a0), D = Ring(p1, (i + o1) / seg, a0);
                    Vector2 uA = new Vector2((i + o0) / seg, p0.z), uB = new Vector2((i + 1 + o0) / seg, p0.z);
                    Vector2 uC = new Vector2((i + 1 + o1) / seg, p1.z), uD = new Vector2((i + o1) / seg, p1.z);
                    int cell = i + j;
                    bool bottom = p0.x < 1e-6f, top = p1.x < 1e-6f;
                    if (bottom && top) continue;
                    // A flat lid (cylinder, cone base): ONE face with all the
                    // rim, like Blender, not a star of thin triangles.
                    bool flat = Mathf.Abs(p0.y - p1.y) < 1e-6f;
                    if (flat && (bottom || top))
                    {
                        if (i > 0) continue;
                        var ring = new Vector3[seg]; var ru = new Vector2[seg];
                        Vector3 rp = bottom ? p1 : p0; float ro = bottom ? o1 : o0;
                        float rr = Mathf.Max(1e-5f, rp.x);
                        for (int k = 0; k < seg; k++)
                        {
                            int q = bottom ? seg - 1 - k : k;
                            ring[k] = Ring(rp, (q + ro) / seg, a0);
                            ru[k] = new Vector2(0.5f + ring[k].x / (2f * rr), 0.5f + ring[k].z / (2f * rr));
                        }
                        Add(polys, 0, ring, ru);
                        continue;
                    }
                    if (bottom) Add(polys, cell, new[] { A, C, D }, new[] { uA, uC, uD });
                    else if (top) Add(polys, cell, new[] { A, B, C }, new[] { uA, uB, uC });
                    else if (tri)
                    {
                        // Two triangles, shaped by the half-step offset.
                        if (o0 > 0f) { Add(polys, cell, new[] { A, B, C }, new[] { uA, uB, uC }); Add(polys, cell + 1, new[] { A, C, D }, new[] { uA, uC, uD }); }
                        else { Add(polys, cell, new[] { A, B, D }, new[] { uA, uB, uD }); Add(polys, cell + 1, new[] { B, C, D }, new[] { uB, uC, uD }); }
                    }
                    else Add(polys, cell, new[] { A, B, C, D }, new[] { uA, uB, uC, uD });
                }
            }
        }

        static Vector3 Ring(Vector3 prof, float t, float a0)
        {
            float a = a0 - t * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(a) * prof.x, prof.y, Mathf.Sin(a) * prof.x);
        }

        static void Torus(List<Poly> polys, int seg, int rings, bool tri)
        {
            const float R = 0.5f, r = 0.17f;
            Func<float, float, Vector3> P = (u, v) =>
            {
                float a = -u * Mathf.PI * 2f, b = v * Mathf.PI * 2f;
                float d = R + r * Mathf.Cos(b);
                return new Vector3(Mathf.Cos(a) * d, r * Mathf.Sin(b), Mathf.Sin(a) * d);
            };
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    float u0 = i / (float)seg, u1 = (i + 1) / (float)seg, v0 = j / (float)rings, v1 = (j + 1) / (float)rings;
                    Grid4(polys, i + j, P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1),
                        new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), tri, ((i + j) & 1) == 1);
                }
        }

        static void PlaneGrid(List<Poly> polys, int nx, int nz, bool tri)
        {
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x0 = 5f - 10f * i / nx, x1 = 5f - 10f * (i + 1) / nx, z0 = 5f - 10f * j / nz, z1 = 5f - 10f * (j + 1) / nz;
                    Grid4(polys, i + j, new Vector3(x0, 0f, z0), new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1), new Vector3(x1, 0f, z0),
                        new Vector2(i / (float)nx, j / (float)nz), new Vector2(i / (float)nx, (j + 1) / (float)nz),
                        new Vector2((i + 1) / (float)nx, (j + 1) / (float)nz), new Vector2((i + 1) / (float)nx, j / (float)nz), tri, ((i + j) & 1) == 1);
                }
        }

        /// A cube of n×n faces per side. `round` > 0 rounds its edges;
        /// `sphere` pushes every point onto a ball (a sphere made of a cube).
        static void CubeShape(List<Poly> polys, int n, float round, bool sphere)
        {
            Vector3[] normal = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3[] uAxis = { Vector3.back, Vector3.forward, Vector3.right, Vector3.right, Vector3.right, Vector3.left };
            for (int f = 0; f < 6; f++)
            {
                Vector3 N = normal[f], U = uAxis[f], V = Vector3.Cross(N, U);
                Func<float, float, Vector3> P = (s, t) =>
                {
                    Vector3 p = N * 0.5f + U * (s - 0.5f) + V * (t - 0.5f);
                    if (sphere) return p.normalized * 0.5f;
                    if (round > 0f)
                    {
                        float k = 0.5f - round;
                        var inner = new Vector3(Mathf.Clamp(p.x, -k, k), Mathf.Clamp(p.y, -k, k), Mathf.Clamp(p.z, -k, k));
                        var d = p - inner;
                        if (d.sqrMagnitude > 1e-12f) p = inner + d.normalized * round;
                    }
                    return p;
                };
                // Rounded edges need more faces to look round.
                int m = round > 0f ? Mathf.Max(n, 8) : n;
                for (int j = 0; j < m; j++)
                    for (int i = 0; i < m; i++)
                    {
                        float s0 = i / (float)m, s1 = (i + 1) / (float)m, t0 = j / (float)m, t1 = (j + 1) / (float)m;
                        Add(polys, i + j + f, new[] { P(s0, t0), P(s1, t0), P(s1, t1), P(s0, t1) },
                            new[] { new Vector2(s0, t0), new Vector2(s1, t0), new Vector2(s1, t1), new Vector2(s0, t1) });
                    }
            }
        }

        static void IcoSphere(List<Poly> polys, int level)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] idx = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                          3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var tris = new List<Vector3[]>();
            for (int i = 0; i < idx.Length; i += 3)
                tris.Add(new[] { v[idx[i]].normalized * 0.5f, v[idx[i + 1]].normalized * 0.5f, v[idx[i + 2]].normalized * 0.5f });
            for (int l = 0; l < level; l++)
            {
                var next = new List<Vector3[]>();
                foreach (var tr in tris)
                {
                    Vector3 a = ((tr[0] + tr[1]) * 0.5f).normalized * 0.5f, b = ((tr[1] + tr[2]) * 0.5f).normalized * 0.5f, c = ((tr[2] + tr[0]) * 0.5f).normalized * 0.5f;
                    next.Add(new[] { tr[0], a, c }); next.Add(new[] { a, tr[1], b }); next.Add(new[] { c, b, tr[2] }); next.Add(new[] { a, b, c });
                }
                tris = next;
            }
            for (int i = 0; i < tris.Count; i++)
            {
                var uv = new Vector2[3];
                for (int k = 0; k < 3; k++)
                {
                    var p = tris[i][k];
                    uv[k] = new Vector2(0.5f + Mathf.Atan2(p.z, p.x) / (2f * Mathf.PI), 0.5f + Mathf.Asin(Mathf.Clamp(p.y * 2f, -1f, 1f)) / Mathf.PI);
                }
                // Keep a triangle across the back seam on one side of the picture.
                float mx = Mathf.Max(uv[0].x, Mathf.Max(uv[1].x, uv[2].x));
                for (int k = 0; k < 3; k++) if (mx - uv[k].x > 0.5f) uv[k].x += 1f;
                Add(polys, i, tris[i], uv);
            }
        }

        static void Grid4(List<Poly> polys, int cell, Vector3 A, Vector3 B, Vector3 C, Vector3 D, Vector2 uA, Vector2 uB, Vector2 uC, Vector2 uD, bool tri, bool flip)
        {
            if (!tri) { Add(polys, cell, new[] { A, B, C, D }, new[] { uA, uB, uC, uD }); return; }
            if (flip) { Add(polys, cell, new[] { A, B, C }, new[] { uA, uB, uC }); Add(polys, cell + 1, new[] { A, C, D }, new[] { uA, uC, uD }); }
            else { Add(polys, cell, new[] { A, B, D }, new[] { uA, uB, uD }); Add(polys, cell + 1, new[] { B, C, D }, new[] { uB, uC, uD }); }
        }

        static List<Poly> Split(List<Poly> polys, bool alternate)
        {
            var o = new List<Poly>();
            foreach (var f in polys)
            {
                if (f.p.Length != 4) { o.Add(f); continue; }
                bool flip = alternate && (f.cell & 1) == 1;
                Grid4(o, f.cell, f.p[0], f.p[1], f.p[2], f.p[3], f.uv[0], f.uv[1], f.uv[2], f.uv[3], true, flip);
            }
            return o;
        }

        static void Add(List<Poly> polys, int cell, Vector3[] p, Vector2[] uv)
        {
            polys.Add(new Poly { p = p, uv = uv, cell = cell });
        }

        // ------------------------------------------------------------------
        // Radial deformation, twist and size

        static void Deform(List<Poly> polys, GeoParams g, bool uniform)
        {
            int ax = Mathf.Clamp(g.axis, 0, 2);
            int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var f in polys) foreach (var p in f.p) { lo = Mathf.Min(lo, p[ax]); hi = Mathf.Max(hi, p[ax]); }
            float span = Mathf.Max(1e-5f, hi - lo);
            float amount = Mathf.Clamp01(g.deform) * 0.6f * (g.sink ? -1f : 1f);
            float twist = g.twist * Mathf.Deg2Rad;
            float size = uniform ? Mathf.Clamp(g.radius, 0.02f, 40f) : 1f;
            int waves = Mathf.Clamp(g.waves, 1, 64);
            foreach (var f in polys)
                for (int k = 0; k < f.p.Length; k++)
                {
                    Vector3 p = f.p[k];
                    float h = (p[ax] - lo) / span;   // 0 bottom .. 1 top
                    float x = p[a1], y = p[a2];
                    if (amount != 0f)
                    {
                        float ang = Mathf.Atan2(y, x);
                        float w = g.mirror ? 1f : h;
                        float s = 1f + amount * w * (0.5f + 0.5f * Mathf.Cos(waves * ang));
                        x *= s; y *= s;
                    }
                    if (twist != 0f)
                    {
                        float t = twist * (h - 0.5f);
                        float c = Mathf.Cos(t), sn = Mathf.Sin(t);
                        float nx = x * c - y * sn, ny = x * sn + y * c;
                        x = nx; y = ny;
                    }
                    p[a1] = x; p[a2] = y;
                    f.p[k] = p * size;
                }
        }

        // ------------------------------------------------------------------
        // To a Unity mesh: smooth normals across faces that meet at less than
        // 60°, hard edges elsewhere (the rim of a cylinder, a cube's corners).

        static void ToArrays(List<Poly> polys, bool twoSided, out List<Vector3> verts, out List<Vector3> norms, out List<Vector2> uvs, out List<int> tris)
        {
            var faceN = new Vector3[polys.Count];
            var byPos = new Dictionary<Vector3, List<int>>();
            for (int i = 0; i < polys.Count; i++)
            {
                var f = polys[i];
                Vector3 n = Vector3.zero;
                for (int k = 0; k < f.p.Length; k++)
                {
                    Vector3 a = f.p[k], b = f.p[(k + 1) % f.p.Length];
                    n.x += (a.y - b.y) * (a.z + b.z);
                    n.y += (a.z - b.z) * (a.x + b.x);
                    n.z += (a.x - b.x) * (a.y + b.y);
                }
                faceN[i] = n;
                foreach (var p in f.p)
                {
                    var key = Snap(p);
                    List<int> l;
                    if (!byPos.TryGetValue(key, out l)) { l = new List<int>(); byPos[key] = l; }
                    if (l.Count == 0 || l[l.Count - 1] != i) l.Add(i);
                }
            }
            verts = new List<Vector3>();
            norms = new List<Vector3>();
            uvs = new List<Vector2>();
            tris = new List<int>();
            var weld = new Dictionary<VKey, int>();
            const float Cos = 0.5f;
            for (int pass = 0; pass < (twoSided ? 2 : 1); pass++)
                for (int i = 0; i < polys.Count; i++)
                {
                    var f = polys[i];
                    Vector3 fn = faceN[i].normalized;
                    if (fn == Vector3.zero) continue;
                    var ids = new int[f.p.Length];
                    for (int k = 0; k < f.p.Length; k++)
                    {
                        Vector3 n = Vector3.zero;
                        foreach (int o in byPos[Snap(f.p[k])])
                        {
                            Vector3 on = faceN[o].normalized;
                            if (Vector3.Dot(on, fn) > Cos) n += faceN[o];
                        }
                        n = n.sqrMagnitude > 1e-20f ? n.normalized : fn;
                        if (pass == 1) n = -n;
                        var vk = new VKey { p = Snap(f.p[k]), n = Snap(n * 100f), uv = Snap2(f.uv[k]) };
                        int id;
                        if (!weld.TryGetValue(vk, out id))
                        {
                            id = verts.Count;
                            weld[vk] = id;
                            verts.Add(f.p[k]); norms.Add(n); uvs.Add(f.uv[k]);
                        }
                        ids[k] = id;
                    }
                    for (int k = 1; k + 1 < ids.Length; k++)
                    {
                        if (pass == 0) { tris.Add(ids[0]); tris.Add(ids[k]); tris.Add(ids[k + 1]); }
                        else { tris.Add(ids[0]); tris.Add(ids[k + 1]); tris.Add(ids[k]); }
                    }
                }
        }

        struct VKey : IEquatable<VKey>
        {
            public Vector3 p, n;
            public Vector2 uv;
            public bool Equals(VKey o) { return p == o.p && n == o.n && uv == o.uv; }
            public override int GetHashCode() { return p.GetHashCode() ^ (n.GetHashCode() * 31) ^ (uv.GetHashCode() * 17); }
        }

        static Vector3 Snap(Vector3 p)
        {
            return new Vector3(Mathf.Round(p.x * 1e4f) / 1e4f, Mathf.Round(p.y * 1e4f) / 1e4f, Mathf.Round(p.z * 1e4f) / 1e4f);
        }

        static Vector2 Snap2(Vector2 p)
        {
            return new Vector2(Mathf.Round(p.x * 1e4f) / 1e4f, Mathf.Round(p.y * 1e4f) / 1e4f);
        }
    }
}
