// ATLAS 5D (Unity) - two modelling tools for open edges (the border of a
// hole):
//   · «Rellenar agujero»: the whole border around a chosen vertex or edge
//     becomes one face (or every hole when nothing is chosen).
//   · «Puente»: two chosen borders (two rings, or two rows of edges) are
//     joined with a band of faces, like Blender's «Bridge Edge Loops».
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public partial class EditMesh
    {
        /// Open edges in the direction their face goes (a → b).
        Dictionary<int, List<int>> BorderNext()
        {
            var ef = EdgeFaces();
            var next = new Dictionary<int, List<int>>();
            foreach (var kv in ef)
            {
                if (kv.Value.Count != 1) continue;
                var f = faces[kv.Value[0]];
                int a = EdgeA(kv.Key), b = EdgeB(kv.Key);
                // Which way does the face run along this edge?
                for (int k = 0; k < f.Length; k++)
                {
                    int p = f[k], q = f[(k + 1) % f.Length];
                    if ((p == a && q == b) || (p == b && q == a))
                    {
                        List<int> l;
                        if (!next.TryGetValue(p, out l)) { l = new List<int>(); next[p] = l; }
                        l.Add(q);
                        break;
                    }
                }
            }
            return next;
        }

        /// Every hole's border as a closed list of vertices, in the order its
        /// faces run.
        public List<List<int>> BorderLoops()
        {
            var next = BorderNext();
            var used = new HashSet<long>();
            var loops = new List<List<int>>();
            foreach (var kv in next)
                foreach (int first in kv.Value)
                {
                    if (used.Contains(EdgeKey(kv.Key, first))) continue;
                    var loop = new List<int> { kv.Key };
                    int cur = kv.Key, nx = first;
                    bool closed = false;
                    for (int guard = 0; guard < 100000; guard++)
                    {
                        used.Add(EdgeKey(cur, nx));
                        if (nx == loop[0]) { closed = true; break; }
                        loop.Add(nx);
                        List<int> l;
                        if (!next.TryGetValue(nx, out l)) break;
                        int pick = -1;
                        foreach (int c in l) if (!used.Contains(EdgeKey(nx, c))) { pick = c; break; }
                        if (pick < 0) break;
                        cur = nx; nx = pick;
                    }
                    if (closed && loop.Count >= 3) loops.Add(loop);
                }
            return loops;
        }

        /// Fills the holes that touch `touch` (all holes if null). Returns
        /// how many faces were made.
        public int FillHoles(HashSet<int> touch)
        {
            int n = 0;
            foreach (var loop in BorderLoops())
            {
                if (touch != null)
                {
                    bool hit = false;
                    foreach (int v in loop) if (touch.Contains(v)) { hit = true; break; }
                    if (!hit) continue;
                }
                // The new face runs against the border (so it faces the same way as its neighbours).
                var f = new int[loop.Count];
                for (int i = 0; i < loop.Count; i++) f[i] = loop[loop.Count - 1 - i];
                faces.Add(f);
                uvs.Add(PlanarUv(f));
                n++;
            }
            return n;
        }

        Vector2[] PlanarUv(int[] f)
        {
            Vector3 c = Vector3.zero, nrm = Vector3.zero;
            foreach (int v in f) c += verts[v];
            c /= f.Length;
            for (int k = 0; k < f.Length; k++) nrm += Vector3.Cross(verts[f[k]] - c, verts[f[(k + 1) % f.Length]] - c);
            nrm = nrm.sqrMagnitude > 1e-12f ? nrm.normalized : Vector3.up;
            Vector3 u = Vector3.Cross(nrm, Mathf.Abs(nrm.y) < 0.9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(nrm, u);
            var r = new Vector2[f.Length];
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int k = 0; k < f.Length; k++)
            {
                var p = verts[f[k]] - c;
                r[k] = new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, w));
                x0 = Mathf.Min(x0, r[k].x); y0 = Mathf.Min(y0, r[k].y); x1 = Mathf.Max(x1, r[k].x); y1 = Mathf.Max(y1, r[k].y);
            }
            float s = 1f / Mathf.Max(1e-5f, Mathf.Max(x1 - x0, y1 - y0));
            for (int k = 0; k < f.Length; k++) r[k] = new Vector2((r[k].x - x0) * s, (r[k].y - y0) * s);
            return r;
        }

        /// Joins two borders made of the chosen open edges. "" when done,
        /// otherwise why it could not.
        public string Bridge(HashSet<long> chosen)
        {
            var next = BorderNext();
            // The chosen open edges, in their face's direction.
            var dir = new Dictionary<int, int>();
            var into = new Dictionary<int, int>();
            foreach (var kv in next)
                foreach (int b in kv.Value)
                    if (chosen.Contains(EdgeKey(kv.Key, b)) && !dir.ContainsKey(kv.Key) && !into.ContainsKey(b)) { dir[kv.Key] = b; into[b] = kv.Key; }
            if (dir.Count < 2) return "Elige aristas del borde de dos agujeros (aristas abiertas)";
            // Chains: start where nothing comes in (open rows) or anywhere (rings).
            var chains = new List<List<int>>();
            var closedFlags = new List<bool>();
            var seen = new HashSet<int>();
            var starts = new List<int>();
            foreach (int v in dir.Keys) if (!into.ContainsKey(v)) starts.Add(v);
            foreach (int v in dir.Keys) if (into.ContainsKey(v)) starts.Add(v);
            foreach (int s in starts)
            {
                if (seen.Contains(s)) continue;
                var c = new List<int> { s };
                seen.Add(s);
                int cur = s;
                bool closed = false;
                for (int g = 0; g < 100000; g++)
                {
                    int nx;
                    if (!dir.TryGetValue(cur, out nx)) break;
                    if (nx == s) { closed = true; break; }
                    if (seen.Contains(nx)) break;
                    c.Add(nx); seen.Add(nx); cur = nx;
                }
                chains.Add(c);
                closedFlags.Add(closed);
            }
            if (chains.Count != 2) return "Elige exactamente dos bordes (dos anillos o dos filas de aristas); ahora hay " + chains.Count;
            var A = chains[0];
            var B = chains[1];
            bool ring = closedFlags[0] && closedFlags[1];
            if (closedFlags[0] != closedFlags[1]) return "Los dos bordes deben ser del mismo tipo: dos anillos cerrados o dos filas abiertas";
            if (A.Count != B.Count) return "Los dos bordes deben tener el mismo número de vértices (" + A.Count + " y " + B.Count + ")";
            int n = A.Count;
            // Rings: the turn of B that lines up best with A.
            int best = 0;
            if (ring)
            {
                float bestD = float.MaxValue;
                for (int off = 0; off < n; off++)
                {
                    float d = 0f;
                    for (int i = 0; i < n; i++) d += (verts[A[i]] - verts[B[((off - i) % n + n) % n]]).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = off; }
                }
            }
            // A[i] goes with B[best - i]: the band runs against both borders.
            int m = ring ? n : n - 1;
            for (int i = 0; i < m; i++)
            {
                int a0 = A[i], a1 = A[(i + 1) % n];
                int b0 = ring ? B[((best - i) % n + n) % n] : B[n - 1 - i];
                int b1 = ring ? B[((best - i - 1) % n + n) % n] : B[n - 2 - i];
                faces.Add(new[] { a1, a0, b0, b1 });
                uvs.Add(new[] { new Vector2((i + 1f) / m, 0f), new Vector2(i / (float)m, 0f), new Vector2(i / (float)m, 1f), new Vector2((i + 1f) / m, 1f) });
            }
            return "";
        }
    }
}
