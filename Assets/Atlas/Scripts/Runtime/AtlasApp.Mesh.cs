// ATLAS 3D (Unity) - «Modelar» and «Esculpir», like the Godot version.
//   Modelar   vertices or faces are chosen with a tap and moved, turned or
//             scaled with the manipulator; extrude, subdivide, smooth,
//             flip, delete.
//   Esculpir  brushes over the surface with one finger (two fingers still
//             move the camera): draw, clay, inflate, smooth, flatten, grab,
//             pinch, crease; invert and X symmetry.
// The shape becomes an editable mesh saved in the project (each change is
// one undo step).
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        string meshMode = "";         // "" | "model" | "sculpt"
        string selMode = "vert";      // vert | edge | face
        readonly HashSet<int> selVerts = new HashSet<int>();
        readonly HashSet<int> selFaces = new HashSet<int>();
        Transform meshPivot;
        Matrix4x4 pivotStartInv;
        readonly Dictionary<int, Vector3> dragStart = new Dictionary<int, Vector3>();
        bool meshDragging;
        Button modelBtn, sculptBtn;
        GameObject meshBar;
        RectTransform meshBarContent, meshLayer;
        readonly List<Image> meshDots = new List<Image>();
        // Sculpt
        string brush = "draw";
        float brushSize = 0.12f, brushStrength = 0.5f;
        bool brushInvert, symmetryX = true;
        bool sculpting;
        List<int>[] strokeNb;
        Vector3[] strokeVn;
        Vector3 lastDab;
        // Grab
        readonly List<int> grabIdx = new List<int>();
        readonly List<float> grabW = new List<float>();
        readonly List<Vector3> grabStart = new List<Vector3>();
        Plane grabPlane;
        Vector3 grabOrigin;
        static readonly Dictionary<string, Mesh> originalMeshes = new Dictionary<string, Mesh>();
        LineRenderer brushRing;

        /// Circle on the surface showing the brush's size while sculpting.
        void ShowBrushRing(Vector3 localPoint, Vector3 localNormal)
        {
            if (brushRing == null)
            {
                var go = new GameObject("Pincel");
                go.layer = Gizmo.Layer;
                brushRing = go.AddComponent<LineRenderer>();
                brushRing.useWorldSpace = true;
                brushRing.loop = true;
                brushRing.positionCount = 40;
                brushRing.widthMultiplier = 0.01f;
                brushRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                brushRing.receiveShadows = false;
                var m = new Material(unlitTemplate);
                m.SetColor("_BaseColor", new Color(1f, 0.85f, 0.4f));
                brushRing.sharedMaterial = m;
            }
            var t = selected.transform;
            Vector3 p = t.TransformPoint(localPoint);
            Vector3 n = t.TransformDirection(localNormal).normalized;
            float r = BrushRadius() * t.lossyScale.magnitude / 1.732f;
            Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(n, u);
            for (int i = 0; i < 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f;
                brushRing.SetPosition(i, p + n * r * 0.02f + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r);
            }
            brushRing.widthMultiplier = Mathf.Max(0.004f, r * 0.04f);
            brushRing.enabled = true;
        }

        void HideBrushRing()
        {
            if (brushRing != null) brushRing.enabled = false;
        }

        bool MeshEditable(AtlasItem it)
        {
            return it != null && it.IsShape && !it.IsGenerated && it.GetComponent<MeshFilter>() != null;
        }

        // ------------------------------------------------------------------
        // Editable mesh of an item

        static Mesh OriginalMesh(string kind)
        {
            Mesh m;
            if (originalMeshes.TryGetValue(kind, out m) && m != null) return m;
            PrimitiveType? pt = null;
            switch (kind)
            {
                case "cube": pt = PrimitiveType.Cube; break;
                case "sphere": pt = PrimitiveType.Sphere; break;
                case "cylinder": pt = PrimitiveType.Cylinder; break;
                case "capsule": pt = PrimitiveType.Capsule; break;
                case "plane": pt = PrimitiveType.Plane; break;
                case "image": pt = PrimitiveType.Quad; break;
            }
            if (pt != null)
            {
                var tmp = GameObject.CreatePrimitive(pt.Value);
                m = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
            }
            else m = Primitives.Get(kind);
            originalMeshes[kind] = m;
            return m;
        }

        void EnsureEditable(AtlasItem it)
        {
            if (it.edit != null) return;
            var mf = it.GetComponent<MeshFilter>();
            it.edit = EditMesh.FromMesh(mf.sharedMesh);
            if (it.editMesh == null) it.editMesh = new Mesh { name = "Malla editable" };
            it.edit.ToMesh(it.editMesh);
            mf.sharedMesh = it.editMesh;
            highlighter.Show(selected);
        }

        /// The item's mesh from its saved file ("" = the original shape).
        void LoadItemMesh(AtlasItem it)
        {
            var mf = it.GetComponent<MeshFilter>();
            if (mf == null) return;
            if (string.IsNullOrEmpty(it.meshFile) || store == null)
            {
                it.edit = null;
                mf.sharedMesh = OriginalMesh(it.kind);
                return;
            }
            try
            {
                it.edit = EditMesh.Load(Path.Combine(store.MeshesDir, it.meshFile));
                if (it.editMesh == null) it.editMesh = new Mesh { name = "Malla editable" };
                it.edit.ToMesh(it.editMesh);
                mf.sharedMesh = it.editMesh;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("ATLAS: malla no cargada: " + e.Message);
                it.edit = null;
                mf.sharedMesh = OriginalMesh(it.kind);
            }
        }

        /// One undo step: the mesh is written to a new file.
        void SaveEdit(AtlasItem it)
        {
            if (it == null || it.edit == null || store == null) return;
            Directory.CreateDirectory(store.MeshesDir);
            string name = it.id + "_" + System.DateTime.Now.Ticks.ToString("x") + ".amsh";
            try
            {
                it.edit.Save(Path.Combine(store.MeshesDir, name));
                it.meshFile = name;
            }
            catch (System.Exception e)
            {
                Toast("No se pudo guardar la malla: " + e.Message, true);
            }
            Commit(true);
            RefreshProps();
        }

        // ------------------------------------------------------------------
        // Modes

        void SetMeshMode(string mode)
        {
            if (mode != "" && !MeshEditable(selected))
            {
                Toast("Selecciona una forma (cubo, esfera…) para modelarla o esculpirla");
                return;
            }
            if (mode != "") ExitPose();
            if (sculpting) EndSculpt();
            meshMode = mode;
            selVerts.Clear();
            selFaces.Clear();
            selEdges.Clear();
            vertOrder.Clear();
            boxSelect = knifeOn = false;
            if (mode != "")
            {
                EnsureEditable(selected);
                if (mode == "model") Toast("Modelar: toca vértices o caras y muévelos con el manipulador");
                else Toast(selected.edit.verts.Count < 1500
                    ? "Esculpir: arrastra un dedo sobre la forma (dos dedos mueven la cámara). Tiene pocos puntos: pulsa «Más detalle» para esculpir con detalle."
                    : "Esculpir: arrastra un dedo sobre la forma (dos dedos mueven la cámara). Cada trazo se deshace por separado.");
            }
            BuildMeshBar();
            ShowGizmo();
            RefreshMode();
            RefreshProps();
        }

        void ExitMeshMode()
        {
            if (meshMode != "") SetMeshMode("");
        }

        void BuildMeshUi()
        {
            meshPivot = new GameObject("PivoteMalla").transform;
            meshLayer = AtlasTheme.Rect(viewArea, "Puntos");
            AtlasTheme.Fill(meshLayer);
            meshLayer.pivot = new Vector2(0.5f, 0.5f);
            meshLayer.SetAsFirstSibling();
            var bar = AtlasTheme.Box(viewArea, "BarraModelado", AtlasTheme.Panel, 14);
            var r = bar.rectTransform;
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.sizeDelta = new Vector2(-24f, 58f);
            meshBar = bar.gameObject;
            var view = AtlasTheme.Rect(bar.transform, "Vista");
            AtlasTheme.Fill(view, 6f, 6f, 4f, 4f);
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            view.gameObject.AddComponent<RectMask2D>();
            meshBarContent = AtlasTheme.Rect(view, "Herramientas");
            meshBarContent.anchorMin = new Vector2(0f, 0f);
            meshBarContent.anchorMax = new Vector2(0f, 1f);
            meshBarContent.pivot = new Vector2(0f, 0.5f);
            AtlasTheme.Row(meshBarContent.gameObject, 4f, new RectOffset(0, 6, 0, 0));
            meshBarContent.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = bar.gameObject.AddComponent<ScrollRect>();
            sr.viewport = view;
            sr.content = meshBarContent;
            sr.horizontal = true;
            sr.vertical = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            meshBar.SetActive(false);
        }

        Button BarButton(string icon, string text, UnityEngine.Events.UnityAction a, bool active = false)
        {
            var b = AtlasTheme.TextButton(meshBarContent, icon, text, a, false, 46f, 15);
            b.GetComponent<Image>().color = active ? AtlasTheme.AccentDim : AtlasTheme.Panel3;
            return b;
        }

        void BuildMeshBar()
        {
            AtlasTheme.Clear(meshBarContent);
            meshBar.SetActive(meshMode != "");
            if (meshMode == "") { LayoutTimelineNeighbours(); return; }
            var r = (RectTransform)meshBar.transform;
            r.anchoredPosition = new Vector2(0f, (timeline.activeSelf ? TimelineHeight : 0f) + 10f);
            LayoutTimelineNeighbours();
            if (meshMode == "model")
            {
                BarButton("mode_vert", "Vértices", () => { selMode = "vert"; selFaces.Clear(); selEdges.Clear(); BuildMeshBar(); ShowGizmo(); }, selMode == "vert");
                BarButton("mode_vert", "Aristas", () => { selMode = "edge"; selFaces.Clear(); selVerts.Clear(); vertOrder.Clear(); BuildMeshBar(); ShowGizmo(); }, selMode == "edge");
                BarButton("mode_face", "Caras", () => { selMode = "face"; selVerts.Clear(); selEdges.Clear(); vertOrder.Clear(); BuildMeshBar(); ShowGizmo(); }, selMode == "face");
                BarButton("select", "Recuadro", () => { boxSelect = !boxSelect; if (boxSelect) knifeOn = false; BuildMeshBar(); Toast(boxSelect ? "Recuadro: arrastra un dedo para elegir todo lo que quede dentro" : "Recuadro desactivado"); }, boxSelect);
                BarButton("multiselect", "Todo", SelectAllElements);
                BarButton("deselect", "Nada", () => { selVerts.Clear(); selFaces.Clear(); selEdges.Clear(); vertOrder.Clear(); ShowGizmo(); });
                AtlasTheme.Separator(meshBarContent, true);
                BarButton("extrude", "Extruir", ExtrudeSelected);
                BarButton("inset", "Insertar", InsetSelected);
                BarButton("subdivide", "Bisel", () => BevelMenu(BarPoint()));
                BarButton("merge", "Conectar", ConnectSelected);
                BarButton("add", "Crear cara", FillSelected);
                BarButton("add", "Rellenar agujero", FillHolesSelected);
                BarButton("merge", "Puente", BridgeSelected);
                BarButton("edit", "Cuchillo", () => { knifeOn = !knifeOn; if (knifeOn) boxSelect = false; BuildMeshBar(); Toast(knifeOn ? "Cuchillo: dibuja una línea de lado a lado de la forma (o elige vértices en orden y pulsa Conectar)" : "Cuchillo desactivado"); }, knifeOn);
                BarButton("loop", "Corte en bucle", LoopCutSelected);
                BarButton("loop", "Bucle", () => LoopOrRing(false));
                BarButton("loop", "Anillo", () => LoopOrRing(true));
                BarButton("delete", "Disolver", DissolveSelected);
                BarButton("merge", "Unir", () =>
                {
                    var vs = SelectedVertsOrNull();
                    if (vs == null || vs.Count < 2) { Toast("Elige dos o más vértices para unirlos"); return; }
                    MeshOp("Unir vértices", m => m.Merge(vs), true);
                });
                BarButton("extrude", "Dar grosor", () =>
                {
                    float sz = Mathf.Max(0.0001f, EditBoundsSize());
                    var l = new List<AtlasTheme.MenuItem> { AtlasTheme.MenuItem.Header("Dar grosor (toda la forma)") };
                    l.Add(new AtlasTheme.MenuItem(null, "Fino", () => MeshOp("Grosor fino", m => m.Solidify(sz * 0.02f), true)));
                    l.Add(new AtlasTheme.MenuItem(null, "Medio", () => MeshOp("Grosor medio", m => m.Solidify(sz * 0.05f), true)));
                    l.Add(new AtlasTheme.MenuItem(null, "Grueso", () => MeshOp("Grosor grueso", m => m.Solidify(sz * 0.12f), true)));
                    AtlasTheme.Popup(canvasRoot, BarPoint(), l);
                });
                BarButton("mirror", "Espejo X", () => MeshOp("Espejo X (lado +X copiado)", m => m.MirrorX(), true));
                BarButton("subdivide", "Subdividir", () => MeshOp("Subdividir", m => m.Subdivide(), true));
                BarButton("subdivide", "Subdivisión suave", () => MeshOp("Subdivisión suave", m => m.SubdivideSmooth(), true));
                BarButton("smooth", "Suavizar", () => MeshOp("Suavizar", m => m.Smooth(0.5f, SelectedVertsOrNull()), false));
                BarButton("sphere", "Sombreado y liso", () => ShadingMenu(BarPoint()));
                BarButton("image", "Editor UV", ToggleUvEditor, UvOpen);
                BarButton("image", "UV automáticas", () => UvMenu(BarPoint()));
                BarButton("flip", "Invertir", () => MeshOp("Invertir caras", m => m.Flip(selMode == "face" ? selFaces : null), false));
                BarButton("delete", "Borrar caras", DeleteSelectedFaces);
            }
            else
            {
                for (int i = 0; i < SculptBrush.Order.Length; i++)
                {
                    string k = SculptBrush.Order[i];
                    string tip = SculptBrush.Tips[i];
                    BarButton(null, SculptBrush.Names[i], () => { brush = k; BuildMeshBar(); Toast(tip); }, brush == k);
                }
                AtlasTheme.Separator(meshBarContent, true);
                BarButton(null, brushInvert ? "Invertir: SÍ" : "Invertir", () => { brushInvert = !brushInvert; BuildMeshBar(); }, brushInvert);
                BarButton("mirror", "Simetría X", () => { symmetryX = !symmetryX; BuildMeshBar(); }, symmetryX);
                BarButton("subdivide", "Más detalle", () => MeshOp("Más detalle", m => m.Subdivide(), true));
            }
            AtlasTheme.Separator(meshBarContent, true);
            var done = BarButton("close", "Terminar", () => SetMeshMode(""));
            done.GetComponent<Image>().color = AtlasTheme.AccentButton;
        }

        HashSet<int> SelectedVertsOrNull()
        {
            if (selMode == "face" && selFaces.Count > 0) return selected.edit.VertsOfFaces(selFaces);
            if (selMode == "edge" && selEdges.Count > 0) return VertsOfEdges();
            return selVerts.Count > 0 ? new HashSet<int>(selVerts) : null;
        }

        void MeshOp(string name, System.Action<EditMesh> op, bool topology)
        {
            if (selected == null || selected.edit == null) return;
            if (topology && selected.edit.TriangleCount > 60000) { Toast("La malla ya es muy densa para el móvil", true); return; }
            op(selected.edit);
            if (topology) { selVerts.Clear(); selFaces.Clear(); selEdges.Clear(); vertOrder.Clear(); }
            selected.edit.ToMesh(selected.editMesh);
            SaveEdit(selected);
            ShowGizmo();
            Toast(name + " · " + selected.edit.verts.Count + " vértices");
        }

        void ExtrudeSelected()
        {
            if (selected == null || selected.edit == null) return;
            Vector3 s = selected.transform.lossyScale;
            float dist = 0.25f / Mathf.Max(0.0001f, (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f);
            if (selMode == "edge" && selEdges.Count > 0)
            {
                var ne = selected.edit.ExtrudeEdges(selEdges, dist);
                selEdges.Clear();
                foreach (long k in ne) selEdges.Add(k);
                selected.edit.ToMesh(selected.editMesh);
                SaveEdit(selected);
                SetTool(Tool.Move);
                ShowGizmo();
                Toast("Aristas extruidas: muévelas con el manipulador");
                return;
            }
            if (selMode != "face" || selFaces.Count == 0) { Toast("Elige caras (en «Caras») o aristas (en «Aristas») y pulsa Extruir"); return; }
            var nf = selected.edit.Extrude(selFaces, dist);
            selFaces.Clear();
            foreach (int f in nf) selFaces.Add(f);
            selected.edit.ToMesh(selected.editMesh);
            SaveEdit(selected);
            SetTool(Tool.Move);
            ShowGizmo();
            Toast("Extruido: muévelo con el manipulador");
        }

        void InsetSelected()
        {
            if (selected == null || selected.edit == null) return;
            if (selMode != "face" || selFaces.Count == 0) { Toast("Pasa a «Caras», elige las que quieras y pulsa Insertar"); return; }
            var nf = selected.edit.Inset(selFaces, 0.25f);
            selFaces.Clear();
            foreach (int f in nf) selFaces.Add(f);
            selected.edit.ToMesh(selected.editMesh);
            SaveEdit(selected);
            ShowGizmo();
            Toast("Caras insertadas: ahora puedes extruirlas o moverlas");
        }

        void DeleteSelectedFaces()
        {
            if (selected == null || selected.edit == null) return;
            if (selMode != "face" || selFaces.Count == 0) { Toast("Pasa a «Caras» y elige las que quieras borrar"); return; }
            if (selFaces.Count >= selected.edit.faces.Count) { Toast("No se pueden borrar todas las caras (borra el objeto)", true); return; }
            selected.edit.DeleteFaces(selFaces);
            selFaces.Clear();
            selected.edit.ToMesh(selected.editMesh);
            SaveEdit(selected);
            ShowGizmo();
        }

        void SelectAllElements()
        {
            if (selected == null || selected.edit == null) return;
            if (selMode == "face") for (int i = 0; i < selected.edit.faces.Count; i++) selFaces.Add(i);
            else if (selMode == "edge") foreach (long k in selected.edit.EdgeFaces().Keys) selEdges.Add(k);
            else for (int i = 0; i < selected.edit.verts.Count; i++) selVerts.Add(i);
            ShowGizmo();
        }

        void RevertMesh(AtlasItem it)
        {
            ExitMeshMode();
            it.meshFile = "";
            LoadItemMesh(it);
            highlighter.Show(it);
            Commit(true);
            RefreshProps();
            Toast("Forma original recuperada (Deshacer la vuelve a dejar como estaba)");
        }

        // ------------------------------------------------------------------
        // Selection and the manipulator

        HashSet<int> CurrentSelVerts()
        {
            if (selected == null || selected.edit == null) return new HashSet<int>();
            if (selMode == "edge") return VertsOfEdges();
            return selMode == "face" ? selected.edit.VertsOfFaces(selFaces) : selVerts;
        }

        /// The manipulator sits at the middle of what is selected.
        bool PlaceMeshPivot()
        {
            var vs = CurrentSelVerts();
            if (vs.Count == 0 || selected == null) return false;
            Vector3 c = Vector3.zero;
            foreach (int i in vs) c += selected.edit.verts[i];
            c /= vs.Count;
            meshPivot.SetPositionAndRotation(selected.transform.TransformPoint(c), selected.transform.rotation);
            meshPivot.localScale = Vector3.one;
            return true;
        }

        bool MeshTap(Vector2 screen)
        {
            if (meshMode != "model" || selected == null || selected.edit == null) return false;
            var e = selected.edit;
            if (selMode == "vert")
            {
                float scale = canvasRoot.GetComponent<Canvas>().scaleFactor;
                float best = 28f * Mathf.Max(1f, scale);
                int found = -1;
                for (int i = 0; i < e.verts.Count; i++)
                {
                    Vector3 sp = cam.WorldToScreenPoint(selected.transform.TransformPoint(e.verts[i]));
                    if (sp.z <= 0f) continue;
                    float d = (new Vector2(sp.x, sp.y) - screen).magnitude;
                    if (d < best) { best = d; found = i; }
                }
                if (found < 0) { selVerts.Clear(); vertOrder.Clear(); }
                else if (!selVerts.Remove(found)) { selVerts.Add(found); vertOrder.Add(found); }
                else vertOrder.Remove(found);
            }
            else if (selMode == "edge")
            {
                long ek;
                if (PickEdge(screen, out ek) == 0) selEdges.Clear();
                else if (!selEdges.Remove(ek)) selEdges.Add(ek);
            }
            else
            {
                Ray ray = cam.ScreenPointToRay(screen);
                var inv = selected.transform.worldToLocalMatrix;
                int face;
                Vector3 p;
                if (e.Raycast(inv.MultiplyPoint3x4(ray.origin), inv.MultiplyVector(ray.direction), out face, out p))
                {
                    if (!selFaces.Remove(face)) selFaces.Add(face);
                }
                else selFaces.Clear();
            }
            ShowGizmo();
            return true;
        }

        void MeshDragBegin()
        {
            meshDragging = true;
            dragStart.Clear();
            pivotStartInv = meshPivot.worldToLocalMatrix;
            foreach (int i in CurrentSelVerts()) dragStart[i] = selected.transform.TransformPoint(selected.edit.verts[i]);
        }

        void MeshDragMove()
        {
            if (!meshDragging || selected == null || selected.edit == null) return;
            Matrix4x4 m = meshPivot.localToWorldMatrix * pivotStartInv;
            foreach (var kv in dragStart)
                selected.edit.verts[kv.Key] = selected.transform.InverseTransformPoint(m.MultiplyPoint3x4(kv.Value));
            selected.edit.ToMesh(selected.editMesh);
        }

        void MeshDragEnd()
        {
            if (!meshDragging) return;
            meshDragging = false;
            SaveEdit(selected);
            ShowGizmo();
        }

        // ------------------------------------------------------------------
        // Points drawn over the view

        GameObject wireGo, selGo, fillGo;
        Mesh wireMesh, selMesh, fillMesh;
        readonly List<Vector3> selPts = new List<Vector3>();
        readonly List<int> selIdx = new List<int>(), fillIdx = new List<int>();
        int wireSig;

        static GameObject OverlayMesh(string name, Transform parent, Material m, out Mesh mesh)
        {
            var go = new GameObject(name);
            go.layer = Gizmo.Layer;
            go.transform.SetParent(parent, false);
            mesh = new Mesh { name = name };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }
        readonly List<Vector3> wirePts = new List<Vector3>();
        readonly List<int> wireIdx = new List<int>();

        /// The mesh's edges as dark lines (like Blender), while modelling.
        void UpdateWire(bool show)
        {
            if (!show)
            {
                highlighter.extraWanted = false;
                if (wireGo != null && wireGo.activeSelf) wireGo.SetActive(false);
                return;
            }
            if (wireGo == null)
            {
                wireGo = new GameObject("Aristas");
                wireGo.layer = Gizmo.Layer;
                wireGo.transform.SetParent(highlighter.transform, false);
                wireMesh = new Mesh { name = "Aristas" };
                wireMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                wireMesh.MarkDynamic();
                wireGo.AddComponent<MeshFilter>().sharedMesh = wireMesh;
                var mr = wireGo.AddComponent<MeshRenderer>();
                var m = new Material(unlitTemplate);
                m.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.04f, 1f));
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                highlighter.extra = wireGo;
                // What is selected: bright lines (and a tint on chosen faces).
                var sm = new Material(unlitTemplate);
                sm.SetColor("_BaseColor", AtlasTheme.Key);
                sm.renderQueue = 3100;
                selGo = OverlayMesh("Seleccion", wireGo.transform, sm, out selMesh);
                var fm = new Material(unlitTemplate);
                MatUtil.SetTransparent(fm, true);
                fm.SetColor("_BaseColor", new Color(AtlasTheme.Key.r, AtlasTheme.Key.g, AtlasTheme.Key.b, 0.28f));
                if (fm.HasProperty("_Cull")) fm.SetFloat("_Cull", 0f);
                fillGo = OverlayMesh("CarasElegidas", wireGo.transform, fm, out fillMesh);
            }
            highlighter.extraWanted = true;
            if (!wireGo.activeSelf) wireGo.SetActive(true);
            wireGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            wireGo.transform.localScale = Vector3.one;
            var e = selected.edit;
            var t = selected.transform;
            // Rebuilt only when something changed (not every frame).
            int sig = Sig(e, t);
            if (sig == wireSig) return;
            wireSig = sig;
            if (UvOpen) DrawUv();   // the 3D choice and shape show in the UV editor too
            wirePts.Clear();
            wireIdx.Clear();
            if (e.verts.Count > 60000) { wireMesh.Clear(); selMesh.Clear(); fillMesh.Clear(); return; }
            // A little towards the camera so the lines are not hidden by the faces.
            Vector3 cp = cam.transform.position;
            for (int i = 0; i < e.verts.Count; i++)
            {
                Vector3 w = t.TransformPoint(e.verts[i]);
                wirePts.Add(Vector3.Lerp(w, cp, 0.004f));
            }
            var seen = new HashSet<long>();
            foreach (var f in e.faces)
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    if (!seen.Add(EditMesh.EdgeKey(a, b))) continue;
                    wireIdx.Add(a);
                    wireIdx.Add(b);
                }
            wireMesh.Clear();
            wireMesh.SetVertices(wirePts);
            wireMesh.SetIndices(wireIdx, MeshTopology.Lines, 0, false);
            wireMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);

            // Selected edges, edges of selected faces, or edges between
            // selected vertices: lines in the selection colour.
            selPts.Clear(); selIdx.Clear(); fillIdx.Clear();
            for (int i = 0; i < e.verts.Count; i++) selPts.Add(Vector3.Lerp(t.TransformPoint(e.verts[i]), cp, 0.007f));
            var done = new HashSet<long>();
            System.Action<int, int> addEdge = (a, b) =>
            {
                if (!done.Add(EditMesh.EdgeKey(a, b))) return;
                selIdx.Add(a); selIdx.Add(b);
            };
            if (selMode == "edge")
                foreach (long k in selEdges) { int a = EditMesh.EdgeA(k), b = EditMesh.EdgeB(k); if (a < e.verts.Count && b < e.verts.Count) addEdge(a, b); }
            else if (selMode == "face")
            {
                foreach (int fi in selFaces)
                {
                    if (fi < 0 || fi >= e.faces.Count) continue;
                    var f = e.faces[fi];
                    for (int k = 0; k < f.Length; k++) addEdge(f[k], f[(k + 1) % f.Length]);
                    for (int k = 1; k + 1 < f.Length; k++) { fillIdx.Add(f[0]); fillIdx.Add(f[k]); fillIdx.Add(f[k + 1]); }
                }
            }
            else
                foreach (var f in e.faces)
                    for (int k = 0; k < f.Length; k++)
                    {
                        int a = f[k], b = f[(k + 1) % f.Length];
                        if (selVerts.Contains(a) && selVerts.Contains(b)) addEdge(a, b);
                    }
            selMesh.Clear();
            selMesh.SetVertices(selPts);
            selMesh.SetIndices(selIdx, MeshTopology.Lines, 0, false);
            selMesh.bounds = wireMesh.bounds;
            fillMesh.Clear();
            fillMesh.SetVertices(selPts);
            fillMesh.SetTriangles(fillIdx, 0, false);
            fillMesh.bounds = wireMesh.bounds;
        }

        /// Cheap fingerprint of what the lines show: shape, selection,
        /// object pose and camera.
        int Sig(EditMesh e, Transform t)
        {
            unchecked
            {
                int h = e.verts.Count * 31 + e.faces.Count;
                for (int i = 0; i < e.verts.Count; i++) h = h * 31 + e.verts[i].GetHashCode();
                h = h * 31 + selVerts.Count * 7 + selFaces.Count * 13 + selEdges.Count * 17 + selMode.GetHashCode();
                foreach (int v in selVerts) h += v * 92821;
                foreach (int f in selFaces) h += f * 68917;
                foreach (long k in selEdges) h += k.GetHashCode() * 3613;
                h = h * 31 + t.localToWorldMatrix.GetHashCode();
                h = h * 31 + cam.transform.position.GetHashCode();
                return h;
            }
        }

        // The edges of the mesh being modelled: worked out again only when
        // the faces change (it was every frame: a big list and thousands of
        // small ones each time, the phone slowed down in edge mode).
        readonly List<long> edgeCache = new List<long>();
        EditMesh edgeCacheMesh;
        long edgeCacheSig = -1;

        List<long> CachedEdges(EditMesh e)
        {
            long sig = e.faces.Count * 1000003L + e.verts.Count;
            for (int i = 0; i < e.faces.Count; i++)
            {
                var f = e.faces[i];
                for (int k = 0; k < f.Length; k++) sig = sig * 31 + f[k] + 1;
            }
            if (e != edgeCacheMesh || sig != edgeCacheSig)
            {
                edgeCacheMesh = e;
                edgeCacheSig = sig;
                edgeCache.Clear();
                edgeCache.AddRange(e.EdgeFaces().Keys);
            }
            return edgeCache;
        }

        void UpdateMeshOverlay()
        {
            if (meshLayer == null) return;
            bool show = meshMode == "model" && selected != null && selected.edit != null;
            if (!show && UvOpen) CloseUvEditor();
            UpdateWire(show);
            int n = 0;
            if (show)
            {
                var e = selected.edit;
                var t = selected.transform;
                bool faces = selMode == "face";
                bool edges = selMode == "edge";
                List<long> edgeList = edges ? CachedEdges(e) : null;
                int count = edges ? edgeList.Count : faces ? e.faces.Count : e.verts.Count;
                if (count <= 4000)
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 w = t.TransformPoint(edges ? (e.verts[EditMesh.EdgeA(edgeList[i])] + e.verts[EditMesh.EdgeB(edgeList[i])]) * 0.5f : faces ? e.FaceCenter(i) : e.verts[i]);
                        Vector3 sp = cam.WorldToScreenPoint(w);
                        if (sp.z <= 0f) continue;
                        Vector2 local;
                        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(meshLayer, sp, null, out local)) continue;
                        if (n >= meshDots.Count)
                        {
                            var dot = AtlasTheme.Box(meshLayer, "Punto", Color.white, 3);
                            dot.raycastTarget = false;
                            dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                            meshDots.Add(dot);
                        }
                        var d = meshDots[n++];
                        d.enabled = true;
                        bool sel = edges ? selEdges.Contains(edgeList[i]) : faces ? selFaces.Contains(i) : selVerts.Contains(i);
                        d.color = sel ? AtlasTheme.Key : (faces ? new Color(0.85f, 0.7f, 1f, 0.8f) : new Color(1f, 1f, 1f, 0.85f));
                        d.rectTransform.anchoredPosition = local;
                        // Edges and faces show as coloured lines: their dots are
                        // just small handles to tap.
                        d.rectTransform.sizeDelta = edges || faces ? (sel ? new Vector2(8f, 8f) : new Vector2(6f, 6f)) : (sel ? new Vector2(14f, 14f) : new Vector2(8f, 8f));
                        if (edges || faces) d.color = sel ? AtlasTheme.Key : new Color(1f, 1f, 1f, 0.45f);
                    }
            }
            for (int i = n; i < meshDots.Count; i++) meshDots[i].enabled = false;
        }

        // ------------------------------------------------------------------
        // Sculpting

        bool SculptRay(Vector2 screen, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            if (selected == null || selected.edit == null) return false;
            Ray ray = cam.ScreenPointToRay(screen);
            var inv = selected.transform.worldToLocalMatrix;
            int face;
            if (!selected.edit.Raycast(inv.MultiplyPoint3x4(ray.origin), inv.MultiplyVector(ray.direction), out face, out point)) return false;
            normal = selected.edit.FaceNormal(face);
            return true;
        }

        float BrushRadius()
        {
            var b = selected.editMesh.bounds;
            return Mathf.Max(0.001f, b.size.magnitude * brushSize);
        }

        bool BeginSculpt(Vector2 screen)
        {
            if (meshMode != "sculpt") return false;
            Vector3 p, n;
            if (!SculptRay(screen, out p, out n)) return false;
            sculpting = true;
            ShowBrushRing(p, n);
            strokeNb = selected.edit.Neighbors();
            strokeVn = selected.edit.VertexNormals();
            lastDab = p + Vector3.one * 1e6f;
            if (brush == "grab")
            {
                grabIdx.Clear(); grabW.Clear(); grabStart.Clear();
                float r = BrushRadius();
                var vs = selected.edit.verts;
                for (int i = 0; i < vs.Count; i++)
                {
                    float d = (vs[i] - p).magnitude;
                    if (d < r) { grabIdx.Add(i); grabW.Add(SculptBrush.Falloff(d, r)); grabStart.Add(vs[i]); }
                    else if (symmetryX)
                    {
                        Vector3 mp = new Vector3(-p.x, p.y, p.z);
                        float dm = (vs[i] - mp).magnitude;
                        if (dm < r) { grabIdx.Add(-1 - i); grabW.Add(SculptBrush.Falloff(dm, r)); grabStart.Add(vs[i]); }
                    }
                }
                grabOrigin = p;
                Vector3 camLocal = selected.transform.InverseTransformDirection(cam.transform.forward);
                grabPlane = new Plane(camLocal, p);
            }
            else SculptAt(p, n);
            return true;
        }

        void SculptMove(Vector2 screen)
        {
            if (!sculpting) return;
            if (brush == "grab")
            {
                Ray ray = cam.ScreenPointToRay(screen);
                var inv = selected.transform.worldToLocalMatrix;
                var lr = new Ray(inv.MultiplyPoint3x4(ray.origin), inv.MultiplyVector(ray.direction));
                float e;
                if (!grabPlane.Raycast(lr, out e)) return;
                Vector3 delta = lr.GetPoint(e) - grabOrigin;
                Vector3 mdelta = new Vector3(-delta.x, delta.y, delta.z);
                var vs = selected.edit.verts;
                for (int k = 0; k < grabIdx.Count; k++)
                {
                    int i = grabIdx[k];
                    if (i >= 0) vs[i] = grabStart[k] + delta * grabW[k];
                    else vs[-1 - i] = grabStart[k] + mdelta * grabW[k];
                }
                selected.edit.ToMesh(selected.editMesh);
                return;
            }
            Vector3 p, n;
            if (!SculptRay(screen, out p, out n)) return;
            ShowBrushRing(p, n);
            // Dabs every bit of the brush radius along the stroke.
            if ((p - lastDab).magnitude < BrushRadius() * 0.18f) return;
            SculptAt(p, n);
        }

        void SculptAt(Vector3 p, Vector3 n)
        {
            lastDab = p;
            float r = BrushRadius();
            float s = brushStrength * (brushInvert ? -1f : 1f);
            if (brush == "inflate") strokeVn = selected.edit.VertexNormals();
            SculptBrush.Dab(selected.edit, brush, p, n, r, s, strokeNb, strokeVn);
            if (symmetryX && Mathf.Abs(p.x) > r * 0.05f)
                SculptBrush.Dab(selected.edit, brush, new Vector3(-p.x, p.y, p.z), new Vector3(-n.x, n.y, n.z), r, s, strokeNb, strokeVn);
            selected.edit.ToMesh(selected.editMesh);
        }

        void EndSculpt()
        {
            if (!sculpting) return;
            sculpting = false;
            HideBrushRing();
            SaveEdit(selected);
        }

        // ------------------------------------------------------------------
        // Inspector

        void BuildModellingSection(AtlasItem it)
        {
            var box = Sec("Modelado y escultura", "sculpt");
            bool here = selected == it && meshMode != "";
            if (it.edit != null)
                Hint(box, "Malla editable: " + it.edit.verts.Count + " vértices · " + it.edit.faces.Count + " caras · " + it.edit.TriangleCount + " triángulos");
            var g = AtlasTheme.Grid(box, 2, InnerWidth, 44f);
            var mb = AtlasTheme.TextButton(g, "edit", meshMode == "model" && here ? "Terminar" : "Modelar", () => SetMeshMode(meshMode == "model" ? "" : "model"), meshMode != "model", 44f, 16);
            var sb = AtlasTheme.TextButton(g, "sculpt", meshMode == "sculpt" && here ? "Terminar" : "Esculpir", () => SetMeshMode(meshMode == "sculpt" ? "" : "sculpt"), meshMode != "sculpt", 44f, 16);
            if (meshMode == "sculpt" && here)
            {
                AtlasTheme.SliderRow(box, "Tamaño", 0.02f, 0.5f, brushSize, "0.00", x => brushSize = x, null);
                AtlasTheme.SliderRow(box, "Fuerza", 0.05f, 1f, brushStrength, "0.00", x => brushStrength = x, null);
            }
            if (!string.IsNullOrEmpty(it.meshFile))
                AtlasTheme.TextButton(box, "undo", "Volver a la forma original", () => RevertMesh(it), false, 42f, 16);
            if (it.edit == null)
                Hint(box, "Al modelar o esculpir, la forma se convierte en una malla editable: puedes mover vértices, extruir caras o esculpirla como arcilla. Todo se puede deshacer.");
        }
    }
}
