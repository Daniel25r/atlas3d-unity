// ATLAS 3D (Unity) - editable mesh for «Modelar» and «Esculpir», like the
// Godot version's EditMesh: shared vertices and polygon faces (quads stay
// quads), with UVs per face corner. Turned into a Unity mesh with
// auto-smooth normals (curved surfaces smooth, sharp edges kept), and
// saved in the project as a small binary file.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas
{
    public class EditMesh
    {
        public List<Vector3> verts = new List<Vector3>();
        public List<int[]> faces = new List<int[]>();
        public List<Vector2[]> uvs = new List<Vector2[]>();   // per face corner

        public int TriangleCount
        {
            get { int n = 0; foreach (var f in faces) n += Mathf.Max(0, f.Length - 2); return n; }
        }

        // ------------------------------------------------------------------
        // From / to Unity meshes

        static long Key(Vector3 p)
        {
            long x = (long)Mathf.Round(p.x * 100000f), y = (long)Mathf.Round(p.y * 100000f), z = (long)Mathf.Round(p.z * 100000f);
            return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
        }

        /// Welds coincident points; flat neighbouring triangles become quads.
        public static EditMesh FromMesh(Mesh m)
        {
            var tris = new List<int>();
            for (int s = 0; s < m.subMeshCount; s++)
                if (m.GetTopology(s) == MeshTopology.Triangles) tris.AddRange(m.GetTriangles(s));
            return FromArrays(m.vertices, m.uv, tris);
        }

        public static EditMesh FromArrays(Vector3[] v, Vector2[] uv, List<int> tris)
        {
            var e = new EditMesh();
            var map = new int[v.Length];
            var weld = new Dictionary<long, List<int>>();
            for (int i = 0; i < v.Length; i++)
            {
                long k = Key(v[i]);
                List<int> cands;
                int found = -1;
                if (weld.TryGetValue(k, out cands))
                    foreach (int c in cands) if ((e.verts[c] - v[i]).sqrMagnitude < 1e-10f) { found = c; break; }
                if (found < 0)
                {
                    found = e.verts.Count;
                    e.verts.Add(v[i]);
                    if (cands == null) { cands = new List<int>(); weld[k] = cands; }
                    cands.Add(found);
                }
                map[i] = found;
            }
            int nt = tris.Count / 3;
            var used = new bool[nt];
            // Edge -> triangles, to pair flat triangles into quads.
            var edgeTris = new Dictionary<long, List<int>>();
            for (int t = 0; t < nt; t++)
                for (int k = 0; k < 3; k++)
                {
                    long ek = EdgeKey(map[tris[t * 3 + k]], map[tris[t * 3 + (k + 1) % 3]]);
                    List<int> l;
                    if (!edgeTris.TryGetValue(ek, out l)) { l = new List<int>(); edgeTris[ek] = l; }
                    l.Add(t);
                }
            Func<int, Vector3> triN = t => Vector3.Cross(v[tris[t * 3 + 1]] - v[tris[t * 3]], v[tris[t * 3 + 2]] - v[tris[t * 3]]).normalized;
            for (int t = 0; t < nt; t++)
            {
                if (used[t]) continue;
                used[t] = true;
                int[] c = { tris[t * 3], tris[t * 3 + 1], tris[t * 3 + 2] };
                bool merged = false;
                for (int k = 0; k < 3 && !merged; k++)
                {
                    int a = c[k], b = c[(k + 1) % 3], opp = c[(k + 2) % 3];
                    List<int> l;
                    if (!edgeTris.TryGetValue(EdgeKey(map[a], map[b]), out l) || l.Count != 2) continue;
                    int o = l[0] == t ? l[1] : l[0];
                    if (used[o] || Vector3.Dot(triN(t), triN(o)) < 0.9999f) continue;
                    // The other triangle's corner that is not on the edge.
                    int other = -1;
                    for (int q = 0; q < 3; q++)
                    {
                        int vi = tris[o * 3 + q];
                        if (map[vi] != map[a] && map[vi] != map[b]) other = vi;
                    }
                    if (other < 0) continue;
                    // Quad a, other, b, opp (keeps the winding).
                    int[] quad = { a, other, b, opp };
                    if (!Convex(v, quad, triN(t))) continue;
                    used[o] = true;
                    e.AddFace(quad, map, uv);
                    merged = true;
                }
                if (!merged) e.AddFace(c, map, uv);
            }
            return e;
        }

        static bool Convex(Vector3[] v, int[] q, Vector3 n)
        {
            for (int i = 0; i < q.Length; i++)
            {
                Vector3 a = v[q[i]], b = v[q[(i + 1) % q.Length]], c = v[q[(i + 2) % q.Length]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - b), n) <= 1e-9f) return false;
            }
            return true;
        }

        void AddFace(int[] meshIdx, int[] map, Vector2[] uv)
        {
            var f = new int[meshIdx.Length];
            var u = new Vector2[meshIdx.Length];
            for (int i = 0; i < f.Length; i++)
            {
                f[i] = map[meshIdx[i]];
                u[i] = uv != null && meshIdx[i] < uv.Length ? uv[meshIdx[i]] : Vector2.zero;
            }
            faces.Add(f);
            uvs.Add(u);
        }

        public static long EdgeKey(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        public Vector3 FaceNormal(int fi)
        {
            var f = faces[fi];
            Vector3 n = Vector3.zero;
            for (int k = 0; k < f.Length; k++)
            {
                Vector3 a = verts[f[k]], b = verts[f[(k + 1) % f.Length]];
                n += Vector3.Cross(a, b);
            }
            // Same side as Unity's Cross(b - a, c - a) for its triangles.
            return n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up;
        }

        public Vector3 FaceCenter(int fi)
        {
            var f = faces[fi];
            Vector3 c = Vector3.zero;
            foreach (int i in f) c += verts[i];
            return c / Mathf.Max(1, f.Length);
        }

        /// Writes the mesh into `m` (one corner per face corner; normals
        /// smooth across edges flatter than 40°).
        public void ToMesh(Mesh m)
        {
            var fn = new Vector3[faces.Count];
            for (int i = 0; i < faces.Count; i++) fn[i] = FaceNormal(i);
            var vf = new List<int>[verts.Count];
            for (int i = 0; i < faces.Count; i++)
                foreach (int vi in faces[i])
                {
                    if (vf[vi] == null) vf[vi] = new List<int>(6);
                    vf[vi].Add(i);
                }
            var pos = new List<Vector3>();
            var nor = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            const float cosSmooth = 0.766f;   // 40°
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                int b = pos.Count;
                for (int k = 0; k < f.Length; k++)
                {
                    int vi = f[k];
                    Vector3 n = Vector3.zero;
                    foreach (int o in vf[vi]) if (Vector3.Dot(fn[o], fn[i]) >= cosSmooth) n += fn[o];
                    pos.Add(verts[vi]);
                    nor.Add(n.sqrMagnitude > 1e-12f ? n.normalized : fn[i]);
                    uv.Add(uvs[i][k]);
                }
                for (int k = 1; k < f.Length - 1; k++) { tri.Add(b); tri.Add(b + k); tri.Add(b + k + 1); }
            }
            m.Clear();
            m.indexFormat = pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(pos);
            m.SetNormals(nor);
            m.SetUVs(0, uv);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
        }

        // ------------------------------------------------------------------
        // Topology helpers

        public List<int>[] Neighbors()
        {
            var nb = new HashSet<int>[verts.Count];
            for (int i = 0; i < nb.Length; i++) nb[i] = new HashSet<int>();
            foreach (var f in faces)
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    nb[a].Add(b);
                    nb[b].Add(a);
                }
            var o = new List<int>[verts.Count];
            for (int i = 0; i < o.Length; i++) o[i] = new List<int>(nb[i]);
            return o;
        }

        public Vector3[] VertexNormals()
        {
            var vn = new Vector3[verts.Count];
            for (int i = 0; i < faces.Count; i++)
            {
                var n = FaceNormal(i);
                foreach (int vi in faces[i]) vn[vi] += n;
            }
            for (int i = 0; i < vn.Length; i++) vn[i] = vn[i].sqrMagnitude > 1e-16f ? vn[i].normalized : Vector3.up;
            return vn;
        }

        public HashSet<int> VertsOfFaces(IEnumerable<int> fs)
        {
            var s = new HashSet<int>();
            foreach (int f in fs) foreach (int v in faces[f]) s.Add(v);
            return s;
        }

        // ------------------------------------------------------------------
        // Operations

        /// Every face split in four (n-gons: one quad per corner).
        public void Subdivide()
        {
            var edgeMid = new Dictionary<long, int>();
            var nf = new List<int[]>();
            var nu = new List<Vector2[]>();
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                var u = uvs[i];
                int n = f.Length;
                var mids = new int[n];
                var midUv = new Vector2[n];
                Vector2 cu = Vector2.zero;
                for (int k = 0; k < n; k++)
                {
                    int a = f[k], b = f[(k + 1) % n];
                    long ek = EdgeKey(a, b);
                    int mi;
                    if (!edgeMid.TryGetValue(ek, out mi))
                    {
                        mi = verts.Count;
                        verts.Add((verts[a] + verts[b]) * 0.5f);
                        edgeMid[ek] = mi;
                    }
                    mids[k] = mi;
                    midUv[k] = (u[k] + u[(k + 1) % n]) * 0.5f;
                    cu += u[k];
                }
                cu /= n;
                if (n == 3)
                {
                    nf.Add(new[] { f[0], mids[0], mids[2] }); nu.Add(new[] { u[0], midUv[0], midUv[2] });
                    nf.Add(new[] { mids[0], f[1], mids[1] }); nu.Add(new[] { midUv[0], u[1], midUv[1] });
                    nf.Add(new[] { mids[2], mids[1], f[2] }); nu.Add(new[] { midUv[2], midUv[1], u[2] });
                    nf.Add(new[] { mids[0], mids[1], mids[2] }); nu.Add(new[] { midUv[0], midUv[1], midUv[2] });
                    continue;
                }
                int c = verts.Count;
                verts.Add(FaceCenter(i));
                for (int k = 0; k < n; k++)
                {
                    int prev = (k + n - 1) % n;
                    nf.Add(new[] { f[k], mids[k], c, mids[prev] });
                    nu.Add(new[] { u[k], midUv[k], cu, midUv[prev] });
                }
            }
            faces = nf;
            uvs = nu;
        }

        /// Laplacian smoothing (only `only` when given).
        public void Smooth(float amount, HashSet<int> only)
        {
            var nb = Neighbors();
            var o = new List<Vector3>(verts);
            for (int i = 0; i < verts.Count; i++)
            {
                if (only != null && !only.Contains(i)) continue;
                if (nb[i].Count == 0) continue;
                Vector3 a = Vector3.zero;
                foreach (int j in nb[i]) a += verts[j];
                a /= nb[i].Count;
                o[i] = Vector3.Lerp(verts[i], a, amount);
            }
            verts = o;
        }

        /// Extrudes the faces as one region: new copies of their vertices,
        /// side walls on the region's border, moved `dist` along the normal.
        /// Returns the new faces (to keep them selected).
        public List<int> Extrude(HashSet<int> sel, float dist)
        {
            var result = new List<int>();
            if (sel.Count == 0) return result;
            Vector3 normal = Vector3.zero;
            foreach (int f in sel) normal += FaceNormal(f);
            normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.up;
            // Border edges: used once inside the selection.
            var edgeCount = new Dictionary<long, int>();
            foreach (int fi in sel)
            {
                var f = faces[fi];
                for (int k = 0; k < f.Length; k++)
                {
                    long ek = EdgeKey(f[k], f[(k + 1) % f.Length]);
                    int c;
                    edgeCount.TryGetValue(ek, out c);
                    edgeCount[ek] = c + 1;
                }
            }
            var copy = new Dictionary<int, int>();
            foreach (int v in VertsOfFaces(sel))
            {
                copy[v] = verts.Count;
                verts.Add(verts[v] + normal * dist);
            }
            foreach (int fi in sel)
            {
                var f = faces[fi];
                var u = uvs[fi];
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    if (edgeCount[EdgeKey(a, b)] != 1) continue;
                    // Wall: a, b on the old surface, b', a' on the new one.
                    faces.Add(new[] { a, b, copy[b], copy[a] });
                    uvs.Add(new[] { u[k], u[(k + 1) % f.Length], u[(k + 1) % f.Length], u[k] });
                }
                var nfc = new int[f.Length];
                for (int k = 0; k < f.Length; k++) nfc[k] = copy[f[k]];
                faces[fi] = nfc;
                result.Add(fi);
            }
            return result;
        }

        /// Reverses the faces (inside out).
        public void Flip(HashSet<int> sel)
        {
            for (int i = 0; i < faces.Count; i++)
            {
                if (sel != null && sel.Count > 0 && !sel.Contains(i)) continue;
                Array.Reverse(faces[i]);
                Array.Reverse(uvs[i]);
            }
        }

        public void DeleteFaces(HashSet<int> sel)
        {
            var nf = new List<int[]>();
            var nu = new List<Vector2[]>();
            for (int i = 0; i < faces.Count; i++)
                if (!sel.Contains(i)) { nf.Add(faces[i]); nu.Add(uvs[i]); }
            faces = nf;
            uvs = nu;
            RemoveUnused();
        }

        public void RemoveUnused()
        {
            var used = new bool[verts.Count];
            foreach (var f in faces) foreach (int v in f) used[v] = true;
            var remap = new int[verts.Count];
            var nv = new List<Vector3>();
            for (int i = 0; i < verts.Count; i++)
            {
                remap[i] = used[i] ? nv.Count : -1;
                if (used[i]) nv.Add(verts[i]);
            }
            foreach (var f in faces) for (int k = 0; k < f.Length; k++) f[k] = remap[f[k]];
            verts = nv;
        }

        /// Ray (mesh space) against the faces: nearest hit, its face and point.
        public bool Raycast(Vector3 o, Vector3 d, out int face, out Vector3 point)
        {
            face = -1;
            point = Vector3.zero;
            float best = float.MaxValue;
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                for (int k = 1; k < f.Length - 1; k++)
                {
                    float t;
                    if (Tri(o, d, verts[f[0]], verts[f[k]], verts[f[k + 1]], out t) && t < best)
                    {
                        best = t;
                        face = i;
                    }
                }
            }
            if (face < 0) return false;
            point = o + d * best;
            return true;
        }

        static bool Tri(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1f / det;
            Vector3 s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 0f;
        }

        // ------------------------------------------------------------------
        // File

        public void Save(string path)
        {
            using (var f = File.Create(path))
            using (var w = new BinaryWriter(f))
            {
                w.Write(0x48534D41);   // "AMSH"
                w.Write(1);
                w.Write(verts.Count);
                foreach (var v in verts) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
                w.Write(faces.Count);
                for (int i = 0; i < faces.Count; i++)
                {
                    w.Write(faces[i].Length);
                    for (int k = 0; k < faces[i].Length; k++)
                    {
                        w.Write(faces[i][k]);
                        w.Write(uvs[i][k].x);
                        w.Write(uvs[i][k].y);
                    }
                }
            }
        }

        public static EditMesh Load(string path)
        {
            using (var f = File.OpenRead(path))
            using (var r = new BinaryReader(f))
            {
                if (r.ReadInt32() != 0x48534D41) throw new Exception("malla dañada");
                r.ReadInt32();
                var e = new EditMesh();
                int nv = r.ReadInt32();
                for (int i = 0; i < nv; i++) e.verts.Add(new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                int nf = r.ReadInt32();
                for (int i = 0; i < nf; i++)
                {
                    int n = r.ReadInt32();
                    var fa = new int[n];
                    var ua = new Vector2[n];
                    for (int k = 0; k < n; k++) { fa[k] = r.ReadInt32(); ua[k] = new Vector2(r.ReadSingle(), r.ReadSingle()); }
                    e.faces.Add(fa);
                    e.uvs.Add(ua);
                }
                return e;
            }
        }
    }

    /// «Esculpir» brushes, the same as the Godot version: they push, pull,
    /// smooth and shape the surface like clay.
    public static class SculptBrush
    {
        public static readonly string[] Order = { "draw", "clay", "inflate", "smooth", "flatten", "grab", "pinch", "crease" };
        public static readonly string[] Names = { "Dibujar", "Arcilla", "Inflar", "Suavizar", "Aplanar", "Agarrar", "Pellizcar", "Pliegue" };
        public static readonly string[] Tips =
        {
            "Levanta la superficie bajo el dedo (con «Invertir», la hunde).",
            "Añade capas de arcilla que rellenan los huecos y aplanan un poco.",
            "Hincha la zona hacia fuera en todas direcciones.",
            "Alisa bultos y arrugas.",
            "Aplana la zona como una espátula.",
            "Arrastra la zona entera siguiendo el dedo.",
            "Junta los puntos hacia el centro (bordes más marcados).",
            "Marca un surco fino (con «Invertir», una cresta).",
        };

        public static float Falloff(float d, float r)
        {
            if (d >= r) return 0f;
            float x = 1f - (d * d) / (r * r);
            return x * x;
        }

        /// One dab at `center` (mesh space) with the surface `normal`.
        public static void Dab(EditMesh m, string kind, Vector3 center, Vector3 normal, float radius, float strength, List<int>[] nb, Vector3[] vn)
        {
            if (radius <= 1e-6f) return;
            var vs = m.verts;
            float step = radius * 0.3f * strength;
            var hit = new List<int>();
            var w = new List<float>();
            float r2 = radius * radius;
            for (int i = 0; i < vs.Count; i++)
            {
                float d2 = (vs[i] - center).sqrMagnitude;
                if (d2 < r2) { hit.Add(i); w.Add(Falloff(Mathf.Sqrt(d2), radius)); }
            }
            if (hit.Count == 0) return;
            Vector3 planeC = center;
            if (kind == "clay" || kind == "flatten")
            {
                Vector3 acc = Vector3.zero;
                float tw = 0f;
                for (int k = 0; k < hit.Count; k++) { acc += vs[hit[k]] * w[k]; tw += w[k]; }
                if (tw > 0f) planeC = acc / tw;
            }
            var outp = new Vector3[hit.Count];
            for (int k = 0; k < hit.Count; k++)
            {
                int i = hit[k];
                Vector3 p = vs[i];
                float wk = w[k];
                Vector3 r = p;
                switch (kind)
                {
                    case "draw": r = p + normal * step * wk; break;
                    case "inflate": r = p + vn[i] * step * wk; break;
                    case "clay":
                    {
                        Vector3 target = planeC + normal * radius * 0.08f * Mathf.Sign(strength);
                        float dist = Vector3.Dot(p - target, normal);
                        if (dist * Mathf.Sign(strength) < 0f) r = p - normal * dist * Mathf.Abs(strength) * 0.5f * wk;
                        break;
                    }
                    case "flatten":
                    {
                        float dist = Vector3.Dot(p - planeC, normal);
                        r = p - normal * dist * Mathf.Clamp01(Mathf.Abs(strength)) * 0.5f * wk;
                        break;
                    }
                    case "smooth":
                        if (nb[i].Count > 0)
                        {
                            Vector3 avg = Vector3.zero;
                            foreach (int j in nb[i]) avg += vs[j];
                            avg /= nb[i].Count;
                            r = Vector3.Lerp(p, avg, Mathf.Clamp01(Mathf.Abs(strength)) * wk);
                        }
                        break;
                    case "pinch":
                    {
                        Vector3 toC = center - p;
                        toC -= normal * Vector3.Dot(toC, normal);
                        r = p + toC * Mathf.Clamp(strength, -1f, 1f) * 0.25f * wk;
                        break;
                    }
                    case "crease":
                    {
                        Vector3 toC = center - p;
                        toC -= normal * Vector3.Dot(toC, normal);
                        r = p + toC * Mathf.Abs(strength) * 0.2f * wk - normal * step * 0.8f * wk * wk;
                        break;
                    }
                }
                outp[k] = r;
            }
            for (int k = 0; k < hit.Count; k++) vs[hit[k]] = outp[k];
        }
    }
}
