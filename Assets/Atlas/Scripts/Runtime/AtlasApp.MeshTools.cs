// ATLAS 3D (Unity) - the «Modelar» tools added for the community's list:
// edge mode, box selection, bevel, connect, fill, knife (a line drawn on
// the screen, or through the chosen vertices in order), loop cut, edge
// loop / ring selection, dissolve, smooth subdivision, the non-destructive
// «Liso» view, flat / smooth shading and automatic UVs.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        readonly HashSet<long> selEdges = new HashSet<long>();
        readonly List<int> vertOrder = new List<int>();   // vertices in the order they were tapped
        bool boxSelect, knifeOn;
        bool boxing, knifing;
        Vector2 dragA, dragB;
        Image boxImg, knifeImg;

        // ------------------------------------------------------------------
        // Box selection and knife line: a one-finger drag in the view

        /// True when the drag belongs to the box or the knife (no orbit).
        bool MeshPointerBegin(Vector2 pos)
        {
            if (meshMode != "model" || selected == null || selected.edit == null) return false;
            if (!boxSelect && !knifeOn) return false;
            dragA = dragB = pos;
            boxing = boxSelect && !knifeOn;
            knifing = knifeOn;
            DrawDragShape();
            return true;
        }

        bool MeshPointerMove(Vector2 pos)
        {
            if (!boxing && !knifing) return false;
            dragB = pos;
            DrawDragShape();
            return true;
        }

        bool MeshPointerEnd(Vector2 pos)
        {
            if (!boxing && !knifing) return false;
            dragB = pos;
            bool wasBox = boxing;
            boxing = knifing = false;
            DrawDragShape();
            // A tap (no drag): it picks like always.
            if ((dragB - dragA).magnitude < 10f) return false;
            if (wasBox) BoxSelectNow();
            else KnifeNow();
            return true;
        }

        void DrawDragShape()
        {
            if (boxImg == null)
            {
                boxImg = AtlasTheme.Box(viewArea, "Recuadro", new Color(0.7f, 0.5f, 1f, 0.18f), 4);
                boxImg.raycastTarget = false;
                boxImg.rectTransform.anchorMin = boxImg.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                knifeImg = AtlasTheme.Box(viewArea, "Cuchillo", AtlasTheme.Key, 2);
                knifeImg.raycastTarget = false;
                knifeImg.rectTransform.anchorMin = knifeImg.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                knifeImg.rectTransform.pivot = new Vector2(0f, 0.5f);
            }
            Vector2 a, b;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewArea, dragA, null, out a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewArea, dragB, null, out b);
            boxImg.enabled = boxing;
            knifeImg.enabled = knifing;
            if (boxing)
            {
                boxImg.rectTransform.anchoredPosition = (a + b) * 0.5f - viewArea.rect.center;
                boxImg.rectTransform.sizeDelta = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            }
            if (knifing)
            {
                Vector2 d = b - a;
                knifeImg.rectTransform.anchoredPosition = a - viewArea.rect.center;
                knifeImg.rectTransform.sizeDelta = new Vector2(d.magnitude, 4f);
                knifeImg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }
        }

        void BoxSelectNow()
        {
            var e = selected.edit;
            var t = selected.transform;
            var r = new Rect(Mathf.Min(dragA.x, dragB.x), Mathf.Min(dragA.y, dragB.y), Mathf.Abs(dragB.x - dragA.x), Mathf.Abs(dragB.y - dragA.y));
            if (r.width < 6f && r.height < 6f) return;
            System.Func<Vector3, bool> inside = p =>
            {
                Vector3 sp = cam.WorldToScreenPoint(t.TransformPoint(p));
                return sp.z > 0f && r.Contains(new Vector2(sp.x, sp.y));
            };
            int n = 0;
            if (selMode == "face")
            {
                for (int i = 0; i < e.faces.Count; i++) if (inside(e.FaceCenter(i))) { selFaces.Add(i); n++; }
            }
            else if (selMode == "edge")
            {
                foreach (long k in e.EdgeFaces().Keys)
                    if (inside(e.verts[EditMesh.EdgeA(k)]) && inside(e.verts[EditMesh.EdgeB(k)])) { selEdges.Add(k); n++; }
            }
            else
            {
                for (int i = 0; i < e.verts.Count; i++) if (inside(e.verts[i]) && selVerts.Add(i)) { vertOrder.Add(i); n++; }
            }
            ShowGizmo();
            Toast(n + " elegidos con el recuadro");
        }

        /// The knife: the plane through the eye and the line drawn cuts
        /// every face it crosses between the line's two ends.
        void KnifeNow()
        {
            if ((dragB - dragA).magnitude < 12f) return;
            var t = selected.transform;
            Ray ra = cam.ScreenPointToRay(dragA), rb = cam.ScreenPointToRay(dragB);
            var inv = t.worldToLocalMatrix;
            Vector3 eye = inv.MultiplyPoint3x4(cam.transform.position);
            Vector3 da = inv.MultiplyVector(ra.direction), db = inv.MultiplyVector(rb.direction);
            Vector3 n = Vector3.Cross(da, db);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();
            Vector2 a = dragA, b = dragB, ab = b - a;
            float len2 = ab.sqrMagnitude;
            System.Func<Vector3, bool> within = p =>
            {
                Vector3 sp = cam.WorldToScreenPoint(t.TransformPoint(p));
                if (sp.z <= 0f) return false;
                float u = Vector2.Dot(new Vector2(sp.x, sp.y) - a, ab) / len2;
                return u >= -0.02f && u <= 1.02f;
            };
            int cut = 0;
            MeshOp("Cuchillo", m => cut = m.KnifePlane(eye, n, within), true);
            if (cut == 0) Toast("El cuchillo no cruzó ninguna cara: dibuja la línea de lado a lado de la forma");
        }

        // ------------------------------------------------------------------
        // Edge picking

        int PickEdge(Vector2 screen, out long edge)
        {
            edge = -1;
            var e = selected.edit;
            var t = selected.transform;
            float scale = canvasRoot.GetComponent<Canvas>().scaleFactor;
            float best = 24f * Mathf.Max(1f, scale);
            foreach (long k in e.EdgeFaces().Keys)
            {
                Vector3 pa = cam.WorldToScreenPoint(t.TransformPoint(e.verts[EditMesh.EdgeA(k)]));
                Vector3 pb = cam.WorldToScreenPoint(t.TransformPoint(e.verts[EditMesh.EdgeB(k)]));
                if (pa.z <= 0f || pb.z <= 0f) continue;
                Vector2 a = pa, b = pb, ab = b - a;
                float u = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(screen - a, ab) / ab.sqrMagnitude) : 0f;
                float d = (a + ab * u - screen).magnitude;
                if (d < best) { best = d; edge = k; }
            }
            return edge >= 0 ? 1 : 0;
        }

        HashSet<int> VertsOfEdges()
        {
            var s = new HashSet<int>();
            foreach (long k in selEdges) { s.Add(EditMesh.EdgeA(k)); s.Add(EditMesh.EdgeB(k)); }
            return s;
        }

        // ------------------------------------------------------------------
        // Tools

        void BevelMenu(Vector2 at)
        {
            var b = selected.editMesh != null ? selected.edit.verts.Count > 0 ? EditBoundsSize() : 1f : 1f;
            var l = new List<AtlasTheme.MenuItem> { AtlasTheme.MenuItem.Header("Bisel (todas las aristas)") };
            foreach (var p in new[] { 0.02f, 0.05f, 0.1f })
            {
                float w = p * b;
                l.Add(new AtlasTheme.MenuItem(null, p == 0.02f ? "Fino" : p == 0.05f ? "Medio" : "Grueso", () => MeshOp("Bisel", m => m.BevelAll(w), true)));
            }
            AtlasTheme.Popup(canvasRoot, at, l);
        }

        float EditBoundsSize()
        {
            var e = selected.edit;
            Vector3 mn = e.verts[0], mx = e.verts[0];
            foreach (var v in e.verts) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
            return Mathf.Max(0.001f, (mx - mn).magnitude);
        }

        void ConnectSelected()
        {
            if (vertOrder.Count < 2) { Toast("Elige (en orden) dos o más vértices de una misma cara"); return; }
            var chain = new List<int>(vertOrder);
            int made = 0;
            MeshOp("Conectar", m => made = m.ConnectChain(chain), true);
            if (made == 0) Toast("Esos vértices no comparten una cara (o ya están unidos por una arista)");
        }

        void FillSelected()
        {
            var vs = selMode == "edge" ? VertsOfEdges() : new HashSet<int>(selVerts);
            if (vs.Count < 3) { Toast("Elige tres o más vértices (o aristas) para crear una cara"); return; }
            MeshOp("Crear cara", m => m.Fill(vs), true);
        }

        void LoopOrRing(bool ring)
        {
            if (selMode != "edge" || selEdges.Count == 0) { Toast("Pasa a «Aristas» y elige una arista"); return; }
            long first = 0;
            foreach (long k in selEdges) { first = k; break; }
            var l = ring ? selected.edit.Ring(first) : selected.edit.Loop(first);
            foreach (long k in l) selEdges.Add(k);
            ShowGizmo();
            Toast((ring ? "Anillo: " : "Bucle: ") + l.Count + " aristas");
        }

        void LoopCutSelected()
        {
            if (selMode != "edge" || selEdges.Count == 0) { Toast("Pasa a «Aristas», elige una arista y pulsa Corte en bucle"); return; }
            long first = 0;
            foreach (long k in selEdges) { first = k; break; }
            int cut = 0;
            MeshOp("Corte en bucle", m => cut = m.LoopCut(first), true);
            if (cut == 0) Toast("Esa arista no está entre cuadriláteros: el corte en bucle necesita caras de 4 lados");
        }

        void DissolveSelected()
        {
            if (selMode == "edge" && selEdges.Count > 0)
            {
                var es = new List<long>(selEdges);
                MeshOp("Disolver aristas", m => m.DissolveEdges(es), true);
            }
            else if (selMode == "vert" && selVerts.Count > 0)
            {
                var vs = new List<int>(selVerts);
                MeshOp("Disolver vértices", m => m.DissolveVerts(vs), true);
            }
            else Toast("Elige vértices o aristas para disolverlos");
        }

        void ShadingMenu(Vector2 at)
        {
            var e = selected.edit;
            var l = new List<AtlasTheme.MenuItem> { AtlasTheme.MenuItem.Header("Sombreado") };
            l.Add(AtlasTheme.MenuItem.Check(null, "Suave (curvo)", e.smoothAngle >= 179f, () => MeshOp("Sombreado suave", m => m.smoothAngle = 180f, false)));
            l.Add(AtlasTheme.MenuItem.Check(null, "Automático (aristas marcadas)", e.smoothAngle > 1f && e.smoothAngle < 179f, () => MeshOp("Sombreado automático", m => m.smoothAngle = 40f, false)));
            l.Add(AtlasTheme.MenuItem.Check(null, "Plano (facetado)", e.smoothAngle <= 1f, () => MeshOp("Sombreado plano", m => m.smoothAngle = 0f, false)));
            l.Add(AtlasTheme.MenuItem.Header("Liso (subdivisión sin tocar la malla)"));
            for (int i = 0; i <= 3; i++)
            {
                int lv = i;
                l.Add(AtlasTheme.MenuItem.Check(null, i == 0 ? "Sin liso" : "Nivel " + i, e.subdivView == i, () => MeshOp(lv == 0 ? "Sin liso" : "Liso nivel " + lv, m => m.subdivView = lv, false)));
            }
            AtlasTheme.Popup(canvasRoot, at, l);
        }

        void UvMenu(Vector2 at)
        {
            var l = new List<AtlasTheme.MenuItem> { AtlasTheme.MenuItem.Header("UV automáticas (proyección de caja)") };
            float size = EditBoundsSize();
            foreach (var r in new[] { 1f, 2f, 4f })
            {
                float rep = r;
                l.Add(new AtlasTheme.MenuItem(null, r == 1f ? "La textura cubre la forma" : "Repetir ×" + r, () => MeshOp("UV automáticas", m => m.AutoUV(rep / size * 1.7f), false)));
            }
            AtlasTheme.Popup(canvasRoot, at, l);
        }

        Vector2 BarPoint()
        {
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.3f);
        }
    }
}
