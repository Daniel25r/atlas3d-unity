// ATLAS 3D (Unity) - more modelling tools for EditMesh, the ones the
// community asked for: smooth (Catmull-Clark) subdivision, bevel of every
// edge (corners included), automatic UVs, connect, fill a face, dissolve,
// edge loops and rings, loop cut and knife cut.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public partial class EditMesh
    {
        /// Normals: smooth across edges flatter than this (0 flat, 180 all
        /// smooth); «subdivView»: smooth levels shown without changing the
        /// mesh (like Blender's Subdivision modifier).
        public float smoothAngle = 40f;
        public int subdivView;

        public EditMesh Copy()
        {
            var e = new EditMesh { smoothAngle = smoothAngle, subdivView = subdivView };
            e.verts = new List<Vector3>(verts);
            foreach (var f in faces) e.faces.Add((int[])f.Clone());
            foreach (var u in uvs) e.uvs.Add((Vector2[])u.Clone());
            return e;
        }

        // ------------------------------------------------------------------
        // Edges

        /// Extrudes edges (Blender's E on edges): each one makes a new face
        /// out of it, pushed a little outwards; the new edges are returned
        /// so they can be moved right away.
        public List<long> ExtrudeEdges(HashSet<long> sel, float dist)
        {
            var result = new List<long>();
            if (sel == null || sel.Count == 0) return result;
            var ef = EdgeFaces();
            // Where each new vertex goes: away from the faces of its edges.
            var push = new Dictionary<int, Vector3>();
            foreach (long k in sel)
            {
                int a = EdgeA(k), b = EdgeB(k);
                Vector3 dir = Vector3.zero;
                List<int> fl;
                if (ef.TryGetValue(k, out fl) && fl.Count == 1)
                {
                    // A border: outwards, in the plane of its face.
                    Vector3 mid = (verts[a] + verts[b]) * 0.5f;
                    Vector3 edge = verts[b] - verts[a];
                    Vector3 n = FaceNormal(fl[0]);
                    dir = Vector3.Cross(edge, n);
                    if (Vector3.Dot(dir, mid - FaceCenter(fl[0])) < 0f) dir = -dir;
                }
                else if (fl != null) foreach (int f in fl) dir += FaceNormal(f);
                dir = dir.sqrMagnitude > 1e-12f ? dir.normalized : Vector3.up;
                Vector3 p;
                push.TryGetValue(a, out p); push[a] = p + dir;
                push.TryGetValue(b, out p); push[b] = p + dir;
            }
            var copy = new Dictionary<int, int>();
            foreach (var kv in push)
            {
                copy[kv.Key] = verts.Count;
                verts.Add(verts[kv.Key] + kv.Value.normalized * dist);
            }
            foreach (long k in sel)
            {
                int a = EdgeA(k), b = EdgeB(k);
                // Same winding as the face it comes out of (edge a→b there).
                Vector2 ua = Vector2.zero, ub = Vector2.zero;
                bool forward = true;
                List<int> fl;
                if (ef.TryGetValue(k, out fl) && fl.Count > 0)
                {
                    var f = faces[fl[0]];
                    var u = uvs[fl[0]];
                    for (int i = 0; i < f.Length; i++)
                    {
                        int x = f[i], y = f[(i + 1) % f.Length];
                        if (x == a && y == b) { forward = true; ua = u[i]; ub = u[(i + 1) % f.Length]; }
                        else if (x == b && y == a) { forward = false; ub = u[i]; ua = u[(i + 1) % f.Length]; }
                    }
                }
                if (forward)
                {
                    faces.Add(new[] { b, a, copy[a], copy[b] });
                    uvs.Add(new[] { ub, ua, ua, ub });
                }
                else
                {
                    faces.Add(new[] { a, b, copy[b], copy[a] });
                    uvs.Add(new[] { ua, ub, ub, ua });
                }
                result.Add(EdgeKey(copy[a], copy[b]));
            }
            return result;
        }

        public static int EdgeA(long k) { return (int)(k >> 32); }
        public static int EdgeB(long k) { return (int)(k & 0xffffffffL); }

        /// Every edge with the faces that use it.
        public Dictionary<long, List<int>> EdgeFaces()
        {
            var d = new Dictionary<long, List<int>>();
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                for (int k = 0; k < f.Length; k++)
                {
                    long e = EdgeKey(f[k], f[(k + 1) % f.Length]);
                    List<int> l;
                    if (!d.TryGetValue(e, out l)) { l = new List<int>(2); d[e] = l; }
                    l.Add(i);
                }
            }
            return d;
        }

        int CornerOf(int face, int v)
        {
            var f = faces[face];
            for (int k = 0; k < f.Length; k++) if (f[k] == v) return k;
            return -1;
        }

        // ------------------------------------------------------------------
        // Smooth subdivision

        /// One level of Catmull-Clark: rounded and smooth, quads only.
        public void SubdivideSmooth()
        {
            int nv = verts.Count, nf = faces.Count;
            var ef = EdgeFaces();
            var edgeIndex = new Dictionary<long, int>();
            var edgeList = new List<long>(ef.Keys);
            for (int i = 0; i < edgeList.Count; i++) edgeIndex[edgeList[i]] = i;
            var fp = new Vector3[nf];
            for (int i = 0; i < nf; i++) fp[i] = FaceCenter(i);
            var ep = new Vector3[edgeList.Count];
            var boundaryV = new bool[nv];
            for (int i = 0; i < edgeList.Count; i++)
            {
                long e = edgeList[i];
                int a = EdgeA(e), b = EdgeB(e);
                var fl = ef[e];
                if (fl.Count == 2) ep[i] = (verts[a] + verts[b] + fp[fl[0]] + fp[fl[1]]) * 0.25f;
                else { ep[i] = (verts[a] + verts[b]) * 0.5f; boundaryV[a] = true; boundaryV[b] = true; }
            }
            var fSum = new Vector3[nv]; var fCnt = new int[nv];
            var rSum = new Vector3[nv]; var rCnt = new int[nv];
            var bSum = new Vector3[nv]; var bCnt = new int[nv];
            for (int i = 0; i < nf; i++) foreach (int v in faces[i]) { fSum[v] += fp[i]; fCnt[v]++; }
            for (int i = 0; i < edgeList.Count; i++)
            {
                long e = edgeList[i];
                int a = EdgeA(e), b = EdgeB(e);
                Vector3 mid = (verts[a] + verts[b]) * 0.5f;
                rSum[a] += mid; rCnt[a]++; rSum[b] += mid; rCnt[b]++;
                if (ef[e].Count != 2) { bSum[a] += mid; bCnt[a]++; bSum[b] += mid; bCnt[b]++; }
            }
            var vp = new Vector3[nv];
            for (int v = 0; v < nv; v++)
            {
                Vector3 P = verts[v];
                if (fCnt[v] == 0) { vp[v] = P; continue; }
                if (boundaryV[v]) vp[v] = bCnt[v] >= 2 ? (P + bSum[v] / bCnt[v]) * 0.5f : P;
                else
                {
                    float n = fCnt[v];
                    vp[v] = n >= 3 ? (fSum[v] / n + 2f * rSum[v] / rCnt[v] + (n - 3f) * P) / n : P;
                }
            }
            var nverts = new List<Vector3>(nv + edgeList.Count + nf);
            nverts.AddRange(vp);
            nverts.AddRange(ep);
            nverts.AddRange(fp);
            var nfaces = new List<int[]>();
            var nuvs = new List<Vector2[]>();
            for (int i = 0; i < nf; i++)
            {
                var f = faces[i];
                var u = uvs[i];
                int n = f.Length;
                Vector2 cu = Vector2.zero;
                for (int k = 0; k < n; k++) cu += u[k];
                cu /= n;
                for (int k = 0; k < n; k++)
                {
                    int prev = (k + n - 1) % n, next = (k + 1) % n;
                    int en = nv + edgeIndex[EdgeKey(f[k], f[next])];
                    int epv = nv + edgeIndex[EdgeKey(f[prev], f[k])];
                    nfaces.Add(new[] { f[k], en, nv + edgeList.Count + i, epv });
                    nuvs.Add(new[] { u[k], (u[k] + u[next]) * 0.5f, cu, (u[k] + u[prev]) * 0.5f });
                }
            }
            verts = nverts;
            faces = nfaces;
            uvs = nuvs;
        }

        // ------------------------------------------------------------------
        // Bevel

        /// Bevels every edge of the shape by `width` (its corners too): each
        /// face shrinks, each edge becomes a strip and each corner a small
        /// face. A cube comes out with clean, closed corners.
        public void BevelAll(float width)
        {
            int nf = faces.Count;
            var ef = EdgeFaces();
            var nverts = new List<Vector3>();
            var nfaces = new List<int[]>();
            var nuvs = new List<Vector2[]>();
            // Face corners pulled inwards along their two edges.
            var cornerIdx = new int[nf][];
            for (int i = 0; i < nf; i++)
            {
                var f = faces[i];
                int n = f.Length;
                cornerIdx[i] = new int[n];
                var nu = new Vector2[n];
                for (int k = 0; k < n; k++)
                {
                    Vector3 v = verts[f[k]], a = verts[f[(k + n - 1) % n]], b = verts[f[(k + 1) % n]];
                    Vector3 da = a - v, db = b - v;
                    float la = da.magnitude, lb = db.magnitude;
                    float w = Mathf.Min(width, la * 0.45f, lb * 0.45f);
                    Vector3 p = v + (la > 1e-7f ? da / la * w : Vector3.zero) + (lb > 1e-7f ? db / lb * w : Vector3.zero);
                    cornerIdx[i][k] = nverts.Count;
                    nverts.Add(p);
                    var ua = uvs[i][(k + n - 1) % n]; var ub = uvs[i][(k + 1) % n]; var uv0 = uvs[i][k];
                    nu[k] = uv0 + (la > 1e-7f ? (ua - uv0) * (w / la) : Vector2.zero) + (lb > 1e-7f ? (ub - uv0) * (w / lb) : Vector2.zero);
                }
                nfaces.Add((int[])cornerIdx[i].Clone());
                nuvs.Add(nu);
            }
            // Edge strips between the two faces of each edge.
            foreach (var kv in ef)
            {
                if (kv.Value.Count != 2) continue;
                int f1 = kv.Value[0], f2 = kv.Value[1];
                int a = EdgeA(kv.Key), b = EdgeB(kv.Key);
                int a1 = CornerOf(f1, a), b1 = CornerOf(f1, b), a2 = CornerOf(f2, a), b2 = CornerOf(f2, b);
                if (a1 < 0 || b1 < 0 || a2 < 0 || b2 < 0) continue;
                // In f1 the edge goes a→b or b→a; keep the strip facing out.
                bool f1ab = faces[f1][(a1 + 1) % faces[f1].Length] == b;
                int[] q = f1ab
                    ? new[] { cornerIdx[f1][b1], cornerIdx[f1][a1], cornerIdx[f2][a2], cornerIdx[f2][b2] }
                    : new[] { cornerIdx[f1][a1], cornerIdx[f1][b1], cornerIdx[f2][b2], cornerIdx[f2][a2] };
                nfaces.Add(q);
                nuvs.Add(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            }
            // Corner faces: the face corners around each vertex, in order.
            var vfaces = new List<int>[verts.Count];
            for (int i = 0; i < nf; i++)
                foreach (int v in faces[i]) { if (vfaces[v] == null) vfaces[v] = new List<int>(); vfaces[v].Add(i); }
            for (int v = 0; v < verts.Count; v++)
            {
                var fl = vfaces[v];
                if (fl == null || fl.Count < 3) continue;
                // Walk around the vertex: from a face, the next is the one
                // sharing the edge that leaves v backwards.
                var ring = new List<int>();
                int cur = fl[0];
                bool closed = false;
                for (int guard = 0; guard < fl.Count + 1; guard++)
                {
                    ring.Add(cornerIdx[cur][CornerOf(cur, v)]);
                    var f = faces[cur];
                    int k = CornerOf(cur, v);
                    int prevV = f[(k + f.Length - 1) % f.Length];
                    List<int> sh;
                    if (!ef.TryGetValue(EdgeKey(v, prevV), out sh) || sh.Count != 2) break;
                    int nxt = sh[0] == cur ? sh[1] : sh[0];
                    if (nxt == fl[0]) { closed = true; break; }
                    cur = nxt;
                }
                if (!closed || ring.Count < 3) continue;
                var rf = ring.ToArray();
                var ru = new Vector2[ring.Count];
                for (int k = 0; k < ru.Length; k++) ru[k] = new Vector2(0.5f + 0.5f * Mathf.Cos(k * Mathf.PI * 2f / ru.Length), 0.5f + 0.5f * Mathf.Sin(k * Mathf.PI * 2f / ru.Length));
                // Facing the same way as the faces around the corner.
                Vector3 want = Vector3.zero;
                foreach (int fi in fl) want += FaceNormal(fi);
                if (Vector3.Dot(Newell(nverts, rf), want) < 0f) { Array.Reverse(rf); Array.Reverse(ru); }
                nfaces.Add(rf);
                nuvs.Add(ru);
            }
            verts = nverts;
            faces = nfaces;
            uvs = nuvs;
        }

        static Vector3 Newell(List<Vector3> vs, int[] f)
        {
            Vector3 n = Vector3.zero;
            for (int k = 0; k < f.Length; k++)
            {
                Vector3 a = vs[f[k]], b = vs[f[(k + 1) % f.Length]];
                n.x += (a.y - b.y) * (a.z + b.z);
                n.y += (a.z - b.z) * (a.x + b.x);
                n.z += (a.x - b.x) * (a.y + b.y);
            }
            return n;
        }

        // ------------------------------------------------------------------
        // UVs

        /// Box projection: each face takes the texture from the side it
        /// faces most (like Blender's «Cube Projection»).
        public void AutoUV(float scale)
        {
            for (int i = 0; i < faces.Count; i++)
            {
                Vector3 n = FaceNormal(i);
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                var f = faces[i];
                var u = new Vector2[f.Length];
                for (int k = 0; k < f.Length; k++)
                {
                    Vector3 p = verts[f[k]] * scale;
                    if (ax >= ay && ax >= az) u[k] = new Vector2(n.x > 0 ? -p.z : p.z, p.y);
                    else if (ay >= az) u[k] = new Vector2(p.x, n.y > 0 ? -p.z : p.z);
                    else u[k] = new Vector2(n.z > 0 ? p.x : -p.x, p.y);
                    u[k] += new Vector2(0.5f, 0.5f);
                }
                uvs[i] = u;
            }
        }

        // ------------------------------------------------------------------
        // Connect, fill, dissolve

        /// Splits the face that has both vertices (not neighbours) in two.
        public bool Connect(int a, int b)
        {
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                int ka = CornerOf(i, a), kb = CornerOf(i, b);
                if (ka < 0 || kb < 0) continue;
                int n = f.Length;
                if ((ka + 1) % n == kb || (kb + 1) % n == ka) continue;
                var p1 = new List<int>(); var u1 = new List<Vector2>();
                for (int k = ka; ; k = (k + 1) % n) { p1.Add(f[k]); u1.Add(uvs[i][k]); if (k == kb) break; }
                var p2 = new List<int>(); var u2 = new List<Vector2>();
                for (int k = kb; ; k = (k + 1) % n) { p2.Add(f[k]); u2.Add(uvs[i][k]); if (k == ka) break; }
                faces[i] = p1.ToArray(); uvs[i] = u1.ToArray();
                faces.Add(p2.ToArray()); uvs.Add(u2.ToArray());
                return true;
            }
            return false;
        }

        /// Connects a chain of vertices (knife by vertices): each pair that
        /// shares a face is joined by a new edge.
        public int ConnectChain(List<int> chain)
        {
            int made = 0;
            for (int i = 0; i + 1 < chain.Count; i++) if (Connect(chain[i], chain[i + 1])) made++;
            return made;
        }

        /// A new face through the chosen vertices (in order around them),
        /// facing away from the middle of the shape.
        public bool Fill(ICollection<int> vs)
        {
            if (vs.Count < 3) return false;
            var list = new List<int>(vs);
            Vector3 c = Vector3.zero;
            foreach (int v in list) c += verts[v];
            c /= list.Count;
            // Plane normal (Newell over the points sorted roughly by angle).
            Vector3 n = Vector3.zero;
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 a = verts[list[i]] - c, b = verts[list[(i + 1) % list.Count]] - c;
                n += Vector3.Cross(a, b);
            }
            if (n.sqrMagnitude < 1e-12f)
            {
                Vector3 a = verts[list[1]] - verts[list[0]], b = verts[list[2]] - verts[list[0]];
                n = Vector3.Cross(a, b);
            }
            n.Normalize();
            Vector3 ax = (verts[list[0]] - c).normalized;
            Vector3 ay = Vector3.Cross(n, ax);
            list.Sort((p, q) =>
            {
                Vector3 dp = verts[p] - c, dq = verts[q] - c;
                return Mathf.Atan2(Vector3.Dot(dp, ay), Vector3.Dot(dp, ax)).CompareTo(Mathf.Atan2(Vector3.Dot(dq, ay), Vector3.Dot(dq, ax)));
            });
            Vector3 mc = Vector3.zero;
            foreach (var v in verts) mc += v;
            mc /= Mathf.Max(1, verts.Count);
            var face = list.ToArray();
            faces.Add(face);
            var u = new Vector2[face.Length];
            for (int k = 0; k < u.Length; k++) { Vector3 d = verts[face[k]] - c; u[k] = new Vector2(Vector3.Dot(d, ax), Vector3.Dot(d, ay)) + new Vector2(0.5f, 0.5f); }
            uvs.Add(u);
            if (Vector3.Dot(FaceNormal(faces.Count - 1), c - mc) < 0f) { Array.Reverse(face); Array.Reverse(u); }
            return true;
        }

        /// Removes the edges between pairs of faces: each pair becomes one face.
        public int DissolveEdges(ICollection<long> edges)
        {
            int done = 0;
            foreach (long e in edges)
            {
                var ef = EdgeFaces();
                List<int> fl;
                if (!ef.TryGetValue(e, out fl) || fl.Count != 2 || fl[0] == fl[1]) continue;
                int f1 = fl[0], f2 = fl[1];
                int a = EdgeA(e), b = EdgeB(e);
                var A = faces[f1]; var B = faces[f2];
                int ka = CornerOf(f1, a), kb = CornerOf(f1, b);
                // Start f1 just after the shared edge and walk to its other end.
                int n1 = A.Length, n2 = B.Length;
                bool ab = A[(ka + 1) % n1] == b;
                int start1 = ab ? kb : ka, end1 = ab ? ka : kb;
                var poly = new List<int>(); var pu = new List<Vector2>();
                for (int k = start1; ; k = (k + 1) % n1) { poly.Add(A[k]); pu.Add(uvs[f1][k]); if (k == end1) break; }
                int endV = A[end1], startV = A[start1];
                int kb2 = CornerOf(f2, endV);
                for (int k = (kb2 + 1) % n2; B[k] != startV; k = (k + 1) % n2) { poly.Add(B[k]); pu.Add(uvs[f2][k]); }
                if (poly.Count < 3) continue;
                faces[f1] = poly.ToArray(); uvs[f1] = pu.ToArray();
                faces.RemoveAt(f2); uvs.RemoveAt(f2);
                done++;
            }
            return done;
        }

        /// Removes vertices: one in the middle of an edge is taken out of
        /// its faces; one surrounded by faces turns them into one face.
        public int DissolveVerts(ICollection<int> vs)
        {
            int done = 0;
            foreach (int v in vs)
            {
                var around = new List<int>();
                for (int i = 0; i < faces.Count; i++) if (CornerOf(i, v) >= 0) around.Add(i);
                if (around.Count == 0) continue;
                var ef = EdgeFaces();
                int edgesAt = 0;
                foreach (var kv in ef) if (EdgeA(kv.Key) == v || EdgeB(kv.Key) == v) edgesAt++;
                if (edgesAt == 2)
                {
                    foreach (int fi in around)
                    {
                        if (faces[fi].Length <= 3) continue;
                        int k = CornerOf(fi, v);
                        var lf = new List<int>(faces[fi]); var lu = new List<Vector2>(uvs[fi]);
                        lf.RemoveAt(k); lu.RemoveAt(k);
                        faces[fi] = lf.ToArray(); uvs[fi] = lu.ToArray();
                    }
                    done++;
                    continue;
                }
                // Merge the fan: dissolve the edges from v, then drop v.
                var spokes = new List<long>();
                foreach (var kv in ef) if ((EdgeA(kv.Key) == v || EdgeB(kv.Key) == v) && kv.Value.Count == 2) spokes.Add(kv.Key);
                if (spokes.Count < 2) continue;
                spokes.RemoveAt(spokes.Count - 1);
                DissolveEdges(spokes);
                for (int i = 0; i < faces.Count; i++)
                {
                    if (CornerOf(i, v) < 0) continue;
                    var lf = new List<int>(faces[i]); var lu = new List<Vector2>(uvs[i]);
                    for (int k = lf.Count - 1; k >= 0; k--) if (lf[k] == v) { lf.RemoveAt(k); lu.RemoveAt(k); }
                    // The spoke left behind comes back as «w, w»: one of them goes.
                    for (int k = lf.Count - 1; k >= 0 && lf.Count > 3; k--)
                        if (lf[k] == lf[(k + 1) % lf.Count]) { lf.RemoveAt(k); lu.RemoveAt(k); }
                    if (lf.Count < 3) continue;
                    faces[i] = lf.ToArray(); uvs[i] = lu.ToArray();
                }
                done++;
            }
            RemoveUnused();
            return done;
        }

        // ------------------------------------------------------------------
        // Loops and rings

        /// The quad that has edge e, other than `not`; -1 if none.
        static int OtherFace(Dictionary<long, List<int>> ef, long e, int not)
        {
            List<int> l;
            if (!ef.TryGetValue(e, out l)) return -1;
            foreach (int f in l) if (f != not) return f;
            return -1;
        }

        /// The edge across a quad from edge (a, b).
        long Opposite(int face, int a, int b)
        {
            var f = faces[face];
            if (f.Length != 4) return -1;
            int ka = CornerOf(face, a);
            if (ka < 0) return -1;
            int kb = CornerOf(face, b);
            // a and b are neighbours; the opposite edge uses the other two.
            var rest = new List<int>();
            for (int k = 0; k < 4; k++) if (k != ka && k != kb) rest.Add(f[k]);
            return rest.Count == 2 ? EdgeKey(rest[0], rest[1]) : -1;
        }

        /// Ring: the edges across a strip of quads (both ways).
        public List<long> Ring(long start)
        {
            var ef = EdgeFaces();
            var res = new List<long> { start };
            var seen = new HashSet<long> { start };
            List<int> fl;
            if (!ef.TryGetValue(start, out fl)) return res;
            foreach (int first in fl)
            {
                long e = start;
                int face = first;
                for (int guard = 0; guard < 10000 && face >= 0; guard++)
                {
                    long o = Opposite(face, EdgeA(e), EdgeB(e));
                    if (o < 0 || !seen.Add(o)) break;
                    res.Add(o);
                    int nf = OtherFace(ef, o, face);
                    e = o;
                    face = nf;
                }
            }
            return res;
        }

        /// Loop: edges going straight on through vertices with 4 edges.
        public List<long> Loop(long start)
        {
            var ef = EdgeFaces();
            var nb = Neighbors();
            var res = new List<long> { start };
            var seen = new HashSet<long> { start };
            foreach (int dirV in new[] { EdgeA(start), EdgeB(start) })
            {
                int from = dirV == EdgeA(start) ? EdgeB(start) : EdgeA(start);
                int at = dirV;
                for (int guard = 0; guard < 10000; guard++)
                {
                    if (nb[at].Count != 4) break;
                    // The next edge: shares no face with the one we came by.
                    long came = EdgeKey(from, at);
                    var cameFaces = ef[came];
                    int next = -1;
                    foreach (int cand in nb[at])
                    {
                        if (cand == from) continue;
                        var cf = ef[EdgeKey(at, cand)];
                        bool shares = false;
                        foreach (int f in cf) if (cameFaces.Contains(f)) shares = true;
                        if (!shares) { next = cand; break; }
                    }
                    if (next < 0) break;
                    long e = EdgeKey(at, next);
                    if (!seen.Add(e)) break;
                    res.Add(e);
                    from = at;
                    at = next;
                }
            }
            return res;
        }

        /// Loop cut: a new loop of edges through the middle of the strip of
        /// quads that crosses edge `start`.
        public int LoopCut(long start, float t = 0.5f)
        {
            var ring = Ring(start);
            var ef = EdgeFaces();
            var ringSet = new HashSet<long>(ring);
            var mid = new Dictionary<long, int>();
            foreach (long e in ring)
            {
                int a = EdgeA(e), b = EdgeB(e);
                mid[e] = verts.Count;
                verts.Add(Vector3.Lerp(verts[a], verts[b], t));
            }
            int cut = 0;
            int nf = faces.Count;
            for (int i = 0; i < nf; i++)
            {
                var f = faces[i];
                if (f.Length != 4) continue;
                // A quad of the strip has two ring edges, opposite each other.
                int e0 = -1;
                for (int k = 0; k < 4; k++) if (ringSet.Contains(EdgeKey(f[k], f[(k + 1) % 4])) && ringSet.Contains(EdgeKey(f[(k + 2) % 4], f[(k + 3) % 4]))) { e0 = k; break; }
                if (e0 < 0) continue;
                int k0 = e0, k1 = (e0 + 1) % 4, k2 = (e0 + 2) % 4, k3 = (e0 + 3) % 4;
                int m01 = mid[EdgeKey(f[k0], f[k1])], m23 = mid[EdgeKey(f[k2], f[k3])];
                var u = uvs[i];
                Vector2 u01 = Vector2.Lerp(u[k0], u[k1], Ratio(f[k0], f[k1], m01)), u23 = Vector2.Lerp(u[k2], u[k3], Ratio(f[k2], f[k3], m23));
                faces[i] = new[] { f[k0], m01, m23, f[k3] };
                uvs[i] = new[] { u[k0], u01, u23, u[k3] };
                faces.Add(new[] { m01, f[k1], f[k2], m23 });
                uvs.Add(new[] { u01, u[k1], u[k2], u23 });
                cut++;
            }
            // Faces around that only touch one ring edge (ends of an open
            // strip, triangles…) take the new point so the mesh stays closed.
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                for (int k = 0; k < f.Length; k++)
                {
                    long e = EdgeKey(f[k], f[(k + 1) % f.Length]);
                    int m;
                    if (!mid.TryGetValue(e, out m)) continue;
                    if (Array.IndexOf(f, m) >= 0) continue;
                    var lf = new List<int>(f); var lu = new List<Vector2>(uvs[i]);
                    lf.Insert(k + 1, m);
                    lu.Insert(k + 1, Vector2.Lerp(uvs[i][k], uvs[i][(k + 1) % f.Length], Ratio(f[k], f[(k + 1) % f.Length], m)));
                    faces[i] = lf.ToArray(); uvs[i] = lu.ToArray();
                    f = faces[i];
                    k++;
                }
            }
            return cut;
        }

        float Ratio(int a, int b, int m)
        {
            float l = (verts[b] - verts[a]).magnitude;
            return l > 1e-9f ? (verts[m] - verts[a]).magnitude / l : 0.5f;
        }

        // ------------------------------------------------------------------
        // Knife (a plane through the camera and the line drawn)

        /// Cuts the faces that the plane crosses where `inside` says yes:
        /// new points on the crossed edges, faces split along them.
        public int KnifePlane(Vector3 p0, Vector3 n, Func<Vector3, bool> inside)
        {
            var dist = new float[verts.Count];
            for (int i = 0; i < verts.Count; i++) dist[i] = Vector3.Dot(verts[i] - p0, n);
            var cutPt = new Dictionary<long, int>();
            int nf = faces.Count;
            for (int i = 0; i < nf; i++)
            {
                var f = faces[i];
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    float da = dist[a], db = dist[b];
                    if ((da > 0f) == (db > 0f) || Mathf.Abs(da - db) < 1e-9f) continue;
                    long e = EdgeKey(a, b);
                    if (cutPt.ContainsKey(e)) continue;
                    float t = da / (da - db);
                    if (t < 1e-4f || t > 1f - 1e-4f) continue;
                    Vector3 p = Vector3.Lerp(verts[a], verts[b], t);
                    if (!inside(p)) continue;
                    cutPt[e] = verts.Count;
                    verts.Add(p);
                }
            }
            if (cutPt.Count == 0) return 0;
            int split = 0;
            for (int i = 0; i < nf; i++)
            {
                var f = faces[i];
                var lf = new List<int>(); var lu = new List<Vector2>();
                var added = new List<int>();
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    lf.Add(a); lu.Add(uvs[i][k]);
                    int m;
                    if (cutPt.TryGetValue(EdgeKey(a, b), out m))
                    {
                        added.Add(lf.Count);
                        lf.Add(m);
                        lu.Add(Vector2.Lerp(uvs[i][k], uvs[i][(k + 1) % f.Length], Ratio(a, b, m)));
                    }
                }
                if (added.Count == 0) continue;
                faces[i] = lf.ToArray(); uvs[i] = lu.ToArray();
                if (added.Count == 2)
                {
                    int ka = added[0], kb = added[1], n2 = lf.Count;
                    var p1 = new List<int>(); var u1 = new List<Vector2>();
                    for (int k = ka; ; k = (k + 1) % n2) { p1.Add(lf[k]); u1.Add(lu[k]); if (k == kb) break; }
                    var p2 = new List<int>(); var u2 = new List<Vector2>();
                    for (int k = kb; ; k = (k + 1) % n2) { p2.Add(lf[k]); u2.Add(lu[k]); if (k == ka) break; }
                    if (p1.Count >= 3 && p2.Count >= 3)
                    {
                        faces[i] = p1.ToArray(); uvs[i] = u1.ToArray();
                        faces.Add(p2.ToArray()); uvs.Add(u2.ToArray());
                        split++;
                    }
                }
            }
            return split;
        }
    }
}
