// ATLAS 5D (Unity) - «Curva»: a tube that passes smoothly through points
// (cables, tails, hoses, pipes, ropes, lettering, frames…). Each point is
// moved with the manipulator like an object; the curve's thickness, how
// round it is, whether it closes into a ring and whether its ends taper are
// settings of the object. It takes materials like any shape.
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        int curvePoint = -1;          // point being moved (-1: the whole object)
        Transform curvePtHandle;        // what the manipulator holds for a point
        LineRenderer curveGuide;      // the line through the points while editing

        static List<Vector3> DefaultCurvePoints()
        {
            return new List<Vector3> { new Vector3(-1f, 0f, 0f), new Vector3(-0.35f, 0.6f, 0.2f), new Vector3(0.35f, -0.2f, -0.2f), new Vector3(1f, 0.4f, 0f) };
        }

        /// Point on the smooth curve through `p` (Catmull-Rom), segment i, t 0..1.
        static Vector3 CurveAt(List<Vector3> p, bool closed, int i, float t)
        {
            int n = p.Count;
            System.Func<int, Vector3> P = k => closed ? p[((k % n) + n) % n] : p[Mathf.Clamp(k, 0, n - 1)];
            Vector3 p0 = P(i - 1), p1 = P(i), p2 = P(i + 1), p3 = P(i + 2);
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// The tube along the curve.
        static Mesh BuildCurveMesh(List<Vector3> pts, float radius, int sides, bool closed, bool taper, Mesh mesh)
        {
            if (mesh == null) mesh = new Mesh { name = "Curva" };
            mesh.Clear();
            if (pts == null || pts.Count < 2) return mesh;
            sides = Mathf.Clamp(sides, 3, 32);
            radius = Mathf.Max(0.0005f, radius);
            int segs = closed ? pts.Count : pts.Count - 1;
            const int per = 16;
            var path = new List<Vector3>();
            for (int s = 0; s < segs; s++)
                for (int k = 0; k < per; k++) path.Add(CurveAt(pts, closed, s, k / (float)per));
            path.Add(closed ? path[0] : pts[pts.Count - 1]);
            int rings = path.Count;
            // Frames carried along the curve (no sudden twists).
            var tang = new Vector3[rings];
            for (int i = 0; i < rings; i++)
            {
                Vector3 a = path[Mathf.Max(0, i - 1)], b = path[Mathf.Min(rings - 1, i + 1)];
                if (closed && (i == 0 || i == rings - 1)) { a = path[rings - 2]; b = path[1]; }
                Vector3 d = b - a;
                tang[i] = d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.forward;
            }
            var nrm = new Vector3[rings];
            Vector3 n0 = Vector3.Cross(tang[0], Mathf.Abs(tang[0].y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            nrm[0] = n0;
            for (int i = 1; i < rings; i++)
            {
                Vector3 n = nrm[i - 1] - tang[i] * Vector3.Dot(nrm[i - 1], tang[i]);
                nrm[i] = n.sqrMagnitude > 1e-10f ? n.normalized : nrm[i - 1];
            }
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float len = 0f;
            for (int i = 0; i < rings; i++)
            {
                if (i > 0) len += (path[i] - path[i - 1]).magnitude;
                Vector3 bi = Vector3.Cross(tang[i], nrm[i]);
                float r = radius;
                if (taper && !closed) r *= Mathf.Lerp(0.15f, 1f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(i / (float)(rings - 1))) * 0.85f + 0.15f);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    Vector3 dir = nrm[i] * Mathf.Cos(a) + bi * Mathf.Sin(a);
                    verts.Add(path[i] + dir * r);
                    norms.Add(dir);
                    uvs.Add(new Vector2(s / (float)sides, len / (2f * Mathf.PI * radius)));
                }
            }
            int row = sides + 1;
            for (int i = 0; i < rings - 1; i++)
                for (int s = 0; s < sides; s++)
                {
                    int a = i * row + s, b = a + 1, c = a + row, d = c + 1;
                    // Clockwise seen from outside (Unity's front face).
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            // Ends closed with a lid (open curves).
            if (!closed)
                for (int end = 0; end < 2; end++)
                {
                    int ring = end == 0 ? 0 : rings - 1;
                    int center = verts.Count;
                    verts.Add(path[ring]);
                    Vector3 out0 = end == 0 ? -tang[ring] : tang[ring];
                    norms.Add(out0);
                    uvs.Add(new Vector2(0.5f, 0.5f));
                    int start = verts.Count;
                    for (int s = 0; s <= sides; s++)
                    {
                        verts.Add(verts[ring * row + s]);
                        norms.Add(out0);
                        float a = s / (float)sides * Mathf.PI * 2f;
                        uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
                    }
                    for (int s = 0; s < sides; s++)
                    {
                        if (end == 0) { tris.Add(center); tris.Add(start + s + 1); tris.Add(start + s); }
                        else { tris.Add(center); tris.Add(start + s); tris.Add(start + s + 1); }
                    }
                }
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        GameObject MakeCurveObject()
        {
            var go = new GameObject();
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            go.AddComponent<MeshCollider>();
            return go;
        }

        void RebuildCurve(AtlasItem it)
        {
            if (it == null || it.kind != "curve") return;
            if (it.curvePts == null || it.curvePts.Count < 2) it.curvePts = DefaultCurvePoints();
            var mf = it.GetComponent<MeshFilter>();
            if (mf == null) return;
            var mesh = BuildCurveMesh(it.curvePts, it.curveRadius, it.curveSides, it.curveClosed, it.curveTaper, mf.sharedMesh);
            mf.sharedMesh = mesh;
            var mc = it.GetComponent<MeshCollider>();
            if (mc != null) { mc.sharedMesh = null; mc.sharedMesh = mesh; }
            if (curvePoint >= 0 && selected == it) DrawCurveGuide(it);
        }

        // ------------------------------------------------------------------
        // Editing the points

        void EditCurvePoint(AtlasItem it, int i)
        {
            if (it == null || it.curvePts == null) return;
            curvePoint = i >= 0 && i < it.curvePts.Count ? i : -1;
            if (curvePtHandle == null) curvePtHandle = new GameObject("PuntoCurva").transform;
            if (curvePoint >= 0)
            {
                curvePtHandle.SetParent(it.transform, false);
                curvePtHandle.localPosition = it.curvePts[curvePoint];
                curvePtHandle.localRotation = Quaternion.identity;
                curvePtHandle.localScale = Vector3.one;
                if (tool != Tool.Move) SetTool(Tool.Move);
                DrawCurveGuide(it);
            }
            else if (curveGuide != null) curveGuide.gameObject.SetActive(false);
            ShowGizmo();
            RefreshProps();
        }

        /// Called every frame: a point dragged with the manipulator reshapes the curve.
        void UpdateCurveEdit()
        {
            if (curvePoint < 0) return;
            if (selected == null || selected.kind != "curve" || curvePtHandle == null || curvePtHandle.parent != selected.transform || curvePoint >= selected.curvePts.Count)
            {
                curvePoint = -1;
                if (curveGuide != null) curveGuide.gameObject.SetActive(false);
                return;
            }
            SyncCurvePoint();
        }

        void SyncCurvePoint()
        {
            if (curvePoint < 0 || selected == null || selected.kind != "curve" || curvePtHandle == null) return;
            if (curvePoint >= selected.curvePts.Count) return;
            Vector3 p = curvePtHandle.localPosition;
            if ((p - selected.curvePts[curvePoint]).sqrMagnitude < 1e-10f) return;
            selected.curvePts[curvePoint] = p;
            RebuildCurve(selected);
        }

        void DrawCurveGuide(AtlasItem it)
        {
            if (curveGuide == null)
            {
                var go = new GameObject("GuiaCurva");
                go.layer = Gizmo.Layer;
                curveGuide = go.AddComponent<LineRenderer>();
                curveGuide.sharedMaterial = new Material(unlitTemplate);
                curveGuide.sharedMaterial.SetColor("_BaseColor", AtlasTheme.Key);
                curveGuide.widthMultiplier = 0.01f;
                curveGuide.useWorldSpace = true;
                curveGuide.numCapVertices = 2;
            }
            curveGuide.gameObject.SetActive(true);
            curveGuide.loop = it.curveClosed;
            curveGuide.positionCount = it.curvePts.Count;
            for (int i = 0; i < it.curvePts.Count; i++) curveGuide.SetPosition(i, it.transform.TransformPoint(it.curvePts[i]));
            float s = Mathf.Max(0.001f, it.transform.lossyScale.x);
            curveGuide.widthMultiplier = Mathf.Max(0.004f, it.curveRadius * s * 0.4f);
        }

        void BuildCurveSection(AtlasItem it)
        {
            if (it.kind != "curve") return;
            var box = Sec("Curva", "edit");
            Hint(box, "Elige un punto y muévelo con el manipulador: la curva pasa suave por todos. Sirve para cables, colas, tubos, cuerdas, letras…");
            AtlasTheme.SliderRow(box, "Grosor", 0.002f, 0.5f, it.curveRadius, "0.000", v => { it.curveRadius = v; RebuildCurve(it); }, () => Commit(true));
            var sg = AtlasTheme.Grid(box, 6, InnerWidth, 34f, 4f);
            foreach (int n0 in new[] { 3, 4, 6, 8, 12, 16 })
            {
                int n = n0;
                AtlasTheme.Chip(sg, n + (n == 3 ? " lados" : ""), () => { it.curveSides = n; RebuildCurve(it); Commit(true); RefreshProps(); }, it.curveSides == n);
            }
            AtlasTheme.Switch(box, "Cerrada (vuelve al primer punto: aro)", it.curveClosed, on => { it.curveClosed = on; RebuildCurve(it); Commit(true); });
            AtlasTheme.Switch(box, "Puntas finas (como una cola o un pelo)", it.curveTaper, on => { it.curveTaper = on; RebuildCurve(it); Commit(true); });
            var t = AtlasTheme.Label(box, "Puntos (" + it.curvePts.Count + ")", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t.gameObject, 26f);
            var pg = AtlasTheme.Grid(box, 6, InnerWidth, 36f, 4f);
            for (int i = 0; i < it.curvePts.Count; i++)
            {
                int k = i;
                AtlasTheme.Chip(pg, (i + 1).ToString(), () => EditCurvePoint(it, curvePoint == k ? -1 : k), curvePoint == i);
            }
            var bg = AtlasTheme.Grid(box, 2, InnerWidth, 40f, 4f);
            AtlasTheme.TextButton(bg, "add", "Añadir punto", () => AddCurvePoint(it), false, 40f, 14);
            AtlasTheme.TextButton(bg, "delete", "Quitar punto", () => RemoveCurvePoint(it), false, 40f, 14);
            if (curvePoint >= 0)
                AtlasTheme.TextButton(box, "edit", "Terminar (mover la curva entera)", () => EditCurvePoint(it, -1), true, 40f, 14);
        }

        void AddCurvePoint(AtlasItem it)
        {
            var p = it.curvePts;
            int at;
            Vector3 np;
            if (curvePoint >= 0 && curvePoint < p.Count - 1)
            {
                at = curvePoint + 1;
                np = CurveAt(p, it.curveClosed, curvePoint, 0.5f);
            }
            else if (curvePoint == p.Count - 1 && it.curveClosed)
            {
                at = p.Count;
                np = CurveAt(p, true, curvePoint, 0.5f);
            }
            else
            {
                at = p.Count;
                np = p[p.Count - 1] + (p[p.Count - 1] - p[p.Count - 2]);
            }
            p.Insert(at, np);
            RebuildCurve(it);
            Commit(true);
            EditCurvePoint(it, at);
            Toast("Punto " + (at + 1) + " añadido: muévelo con el manipulador");
        }

        void RemoveCurvePoint(AtlasItem it)
        {
            if (it.curvePts.Count <= 2) { Toast("Una curva necesita al menos dos puntos"); return; }
            int at = curvePoint >= 0 ? curvePoint : it.curvePts.Count - 1;
            it.curvePts.RemoveAt(at);
            RebuildCurve(it);
            Commit(true);
            EditCurvePoint(it, Mathf.Min(at, it.curvePts.Count - 1));
        }
    }
}
