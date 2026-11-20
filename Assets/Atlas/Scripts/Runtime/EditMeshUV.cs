// ATLAS 3D (Unity) - «Desplegar por islas» (like Blender's Smart UV
// Project): the faces are grouped into islands of similar direction, each
// island is laid flat, and the islands are packed side by side inside the
// picture, all at the same scale (the texture is not stretched).
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public partial class EditMesh
    {
        /// `only`: just those faces (null = all). `angle`: how far two
        /// touching faces may turn and still be in the same island.
        public void SmartUV(HashSet<int> only, float angle = 66f, float margin = 0.02f)
        {
            var use = new List<int>();
            for (int i = 0; i < faces.Count; i++) if (only == null || only.Contains(i)) use.Add(i);
            if (use.Count == 0) return;
            var normals = new Vector3[faces.Count];
            foreach (int i in use) normals[i] = FaceNormal(i);
            var ef = EdgeFaces();
            float cosA = Mathf.Cos(angle * Mathf.Deg2Rad);
            var island = new int[faces.Count];
            for (int i = 0; i < island.Length; i++) island[i] = -1;
            var inUse = new HashSet<int>(use);
            var islands = new List<List<int>>();
            foreach (int seed in use)
            {
                if (island[seed] >= 0) continue;
                var list = new List<int> { seed };
                island[seed] = islands.Count;
                Vector3 sn = normals[seed];
                for (int q = 0; q < list.Count; q++)
                {
                    var f = faces[list[q]];
                    for (int k = 0; k < f.Length; k++)
                    {
                        List<int> nb;
                        if (!ef.TryGetValue(EdgeKey(f[k], f[(k + 1) % f.Length]), out nb)) continue;
                        foreach (int o in nb)
                        {
                            if (island[o] >= 0 || !inUse.Contains(o)) continue;
                            if (Vector3.Dot(normals[o], normals[list[q]]) < cosA || Vector3.Dot(normals[o], sn) < cosA) continue;
                            island[o] = islands.Count;
                            list.Add(o);
                        }
                    }
                }
                islands.Add(list);
            }

            // Each island flat on its own plane.
            var flat = new List<Vector2[][]>();
            var rects = new List<Rect>();
            float area = 0f;
            foreach (var isl in islands)
            {
                Vector3 n = Vector3.zero;
                foreach (int fi in isl) n += normals[fi] * Mathf.Max(1e-6f, FaceArea(fi));
                n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
                Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 v = Vector3.Cross(n, u);
                // Upright when it can be: «up» of the island is world up.
                if (Mathf.Abs(n.y) < 0.9f) { v = Vector3.ProjectOnPlane(Vector3.up, n).normalized; u = Vector3.Cross(v, n); }
                var pts = new Vector2[isl.Count][];
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int a = 0; a < isl.Count; a++)
                {
                    var f = faces[isl[a]];
                    pts[a] = new Vector2[f.Length];
                    for (int k = 0; k < f.Length; k++)
                    {
                        Vector3 p = verts[f[k]];
                        var q = new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v));
                        pts[a][k] = q;
                        x0 = Mathf.Min(x0, q.x); y0 = Mathf.Min(y0, q.y); x1 = Mathf.Max(x1, q.x); y1 = Mathf.Max(y1, q.y);
                    }
                }
                var r = new Rect(x0, y0, Mathf.Max(1e-5f, x1 - x0), Mathf.Max(1e-5f, y1 - y0));
                flat.Add(pts);
                rects.Add(r);
                area += r.width * r.height;
            }

            // Shelves: tallest islands first, rows as wide as a square.
            var order = new List<int>();
            for (int i = 0; i < islands.Count; i++) order.Add(i);
            order.Sort((a, b) => rects[b].height.CompareTo(rects[a].height));
            float gap = 0f;
            foreach (var r in rects) gap = Mathf.Max(gap, Mathf.Max(r.width, r.height));
            gap *= margin;
            float side = Mathf.Sqrt(Mathf.Max(1e-8f, area)) * 1.05f;
            foreach (var r in rects) side = Mathf.Max(side, r.width + gap * 2f);
            var place = new Vector2[islands.Count];
            for (int tries = 0; tries < 60; tries++)
            {
                float x = gap, y = gap, rowH = 0f;
                bool fits = true;
                foreach (int i in order)
                {
                    var r = rects[i];
                    if (x + r.width + gap > side) { x = gap; y += rowH + gap; rowH = 0f; }
                    place[i] = new Vector2(x, y);
                    x += r.width + gap;
                    rowH = Mathf.Max(rowH, r.height);
                }
                if (y + rowH + gap > side) fits = false;
                if (fits) break;
                side *= 1.06f;
            }
            float s = 1f / side;
            for (int i = 0; i < islands.Count; i++)
            {
                var isl = islands[i];
                var r = rects[i];
                for (int a = 0; a < isl.Count; a++)
                {
                    var src = flat[i][a];
                    var dst = new Vector2[src.Length];
                    for (int k = 0; k < src.Length; k++)
                        dst[k] = new Vector2((src[k].x - r.xMin + place[i].x) * s, (src[k].y - r.yMin + place[i].y) * s);
                    uvs[isl[a]] = dst;
                }
            }
        }

        public float FaceArea(int fi)
        {
            var f = faces[fi];
            float a = 0f;
            for (int k = 1; k + 1 < f.Length; k++)
                a += Vector3.Cross(verts[f[k]] - verts[f[0]], verts[f[k + 1]] - verts[f[0]]).magnitude * 0.5f;
            return a;
        }

        /// The faces touching `seed` through edges, all of them (one piece).
        public HashSet<int> Linked(HashSet<int> seed)
        {
            var ef = EdgeFaces();
            var res = new HashSet<int>(seed);
            var q = new Queue<int>(seed);
            while (q.Count > 0)
            {
                var f = faces[q.Dequeue()];
                for (int k = 0; k < f.Length; k++)
                {
                    List<int> nb;
                    if (!ef.TryGetValue(EdgeKey(f[k], f[(k + 1) % f.Length]), out nb)) continue;
                    foreach (int o in nb) if (res.Add(o)) q.Enqueue(o);
                }
            }
            return res;
        }
    }
}

namespace Atlas
{
    public partial class EditMesh
    {
        /// «Dar grosor» (Blender's Solidify): a second skin inside, facing
        /// in, joined to the first one along the open borders: a plane or
        /// an extruded sheet becomes a solid with thickness.
        public void Solidify(float thickness)
        {
            if (faces.Count == 0 || thickness <= 0f) return;
            var vn = VertexNormals();
            int n0 = verts.Count, f0 = faces.Count;
            var ef = EdgeFaces();
            for (int i = 0; i < n0; i++) verts.Add(verts[i] - vn[i] * thickness);
            for (int fi = 0; fi < f0; fi++)
            {
                var f = faces[fi];
                var u = uvs[fi];
                var nf = new int[f.Length];
                var nu = new Vector2[f.Length];
                for (int k = 0; k < f.Length; k++) { nf[k] = f[f.Length - 1 - k] + n0; nu[k] = u[f.Length - 1 - k]; }
                faces.Add(nf);
                uvs.Add(nu);
            }
            // The rim: open edges (used by one face) get a band.
            for (int fi = 0; fi < f0; fi++)
            {
                var f = faces[fi];
                var u = uvs[fi];
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    List<int> l;
                    if (!ef.TryGetValue(EdgeKey(a, b), out l) || l.Count != 1) continue;
                    faces.Add(new[] { b, a, a + n0, b + n0 });
                    Vector2 ua = u[k], ub = u[(k + 1) % f.Length];
                    uvs.Add(new[] { ub, ua, ua, ub });
                }
            }
        }
    }
}
