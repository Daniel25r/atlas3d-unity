// ATLAS 3D (Unity) - phase 1: the editor.
// Simple ATLAS layout: top bar (logo, undo, redo, save), left tool rail
// (move, rotate, scale, add), the 3D view, a panel for the selected object
// (colour, duplicate, delete) and a status line. Touch: one finger orbits
// (or drags a gizmo handle), two fingers zoom and pan, a tap selects.
// The scene is saved in the project folder and reopened on start.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Atlas
{
    public class AtlasApp : MonoBehaviour
    {
        Camera cam;
        Material litTemplate, unlitTemplate;
        Text status;
        Transform target;   // orbit centre
        float yaw = 30f, pitch = 20f, distance = 7f;
        readonly List<AtlasItem> items = new List<AtlasItem>();
        AtlasItem selected;
        Gizmo gizmo;
        Tool tool = Tool.Move;
        SceneStore store;
        int count;

        // UI
        RectTransform safeRoot;
        Button moveBtn, rotateBtn, scaleBtn, undoBtn, redoBtn;
        GameObject addMenu, selPanel;
        Text selName;
        Rect lastSafe;

        static readonly Color[] Swatches =
        {
            AtlasTheme.Hex("e8eaf0"), AtlasTheme.Hex("e5484d"), AtlasTheme.Hex("f5a524"), AtlasTheme.Hex("46c46e"),
            AtlasTheme.Hex("3d8bfd"), AtlasTheme.Hex("b25cff"), AtlasTheme.Hex("8a6a4f"), AtlasTheme.Hex("2a2d36"),
        };

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Input.simulateMouseWithTouches = false;   // a touch is not also a mouse click
            litTemplate = Resources.Load<Material>("AtlasLit");
            unlitTemplate = Resources.Load<Material>("AtlasUnlit");
            store = new SceneStore("Mi proyecto");
            BuildWorld();
            BuildUi();
            var saved = store.Load();
            if (saved != null && saved.items.Count > 0)
            {
                Restore(saved);
                SetStatus("Proyecto abierto · toca un objeto para seleccionarlo");
            }
            else
            {
                AddShape("cube");
                SetStatus("Toca un objeto para seleccionarlo · un dedo: girar la vista · dos dedos: zoom y desplazar");
            }
            Commit(false);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) SaveNow(false);
        }

        // ------------------------------------------------------------------
        // 3D world

        void BuildWorld()
        {
            target = new GameObject("Centro").transform;
            target.position = new Vector3(0f, 0.5f, 0f);

            var camGo = new GameObject("Cámara");
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = AtlasTheme.Hex("2a2d36");
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1000f;
            cam.allowMSAA = true;
            UpdateCamera();

            var sun = new GameObject("Sol").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.58f, 0.66f);
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.39f, 0.43f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.16f, 0.18f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Suelo";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            var gm = new Material(litTemplate);
            gm.SetTexture("_BaseMap", GridTexture());
            gm.SetTextureScale("_BaseMap", new Vector2(200f, 200f));
            gm.SetColor("_BaseColor", Color.white);
            gm.SetFloat("_Smoothness", 0.1f);
            ground.GetComponent<Renderer>().sharedMaterial = gm;

            var g = new GameObject("Gizmo");
            gizmo = g.AddComponent<Gizmo>();
            gizmo.cam = cam;
            gizmo.unlitTemplate = unlitTemplate;
            gizmo.Build();
        }

        static Texture2D GridTexture()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var px = new Color[n * n];
            Color bg = new Color(0.23f, 0.24f, 0.28f);
            Color line = new Color(0.36f, 0.37f, 0.42f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = (x < 2 || y < 2) ? line : bg;
            t.SetPixels(px);
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 8;
            t.Apply(true);
            return t;
        }

        // ------------------------------------------------------------------
        // Objects

        static string DisplayName(string kind)
        {
            switch (kind)
            {
                case "cube": return "Cubo";
                case "sphere": return "Esfera";
                case "cylinder": return "Cilindro";
                case "capsule": return "Cápsula";
                case "plane": return "Plano";
                case "light": return "Luz";
                default: return "Modelo";
            }
        }

        AtlasItem Create(ItemData d)
        {
            GameObject go;
            switch (d.kind)
            {
                case "cube": go = GameObject.CreatePrimitive(PrimitiveType.Cube); break;
                case "sphere": go = GameObject.CreatePrimitive(PrimitiveType.Sphere); break;
                case "cylinder": go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); break;
                case "capsule": go = GameObject.CreatePrimitive(PrimitiveType.Capsule); break;
                case "plane": go = GameObject.CreatePrimitive(PrimitiveType.Plane); break;
                case "light":
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    var l = new GameObject("Bombilla").AddComponent<Light>();
                    l.transform.SetParent(go.transform, false);
                    l.type = LightType.Point;
                    l.range = 10f;
                    l.intensity = 3f;
                    l.shadows = LightShadows.Soft;
                    go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    break;
                default: go = new GameObject(); break;
            }
            go.name = string.IsNullOrEmpty(d.name) ? DisplayName(d.kind) : d.name;
            var it = go.AddComponent<AtlasItem>();
            it.id = string.IsNullOrEmpty(d.id) ? SceneStore.NewId() : d.id;
            it.kind = d.kind;
            it.modelFile = d.modelFile;
            go.transform.position = d.position;
            go.transform.eulerAngles = d.rotation;
            go.transform.localScale = d.scale;
            items.Add(it);
            if (d.kind != "model")
            {
                var m = new Material(litTemplate);
                go.GetComponent<Renderer>().sharedMaterial = m;
                SetColor(it, d.color);
            }
            else
            {
                LoadModel(it);
            }
            return it;
        }

        async void LoadModel(AtlasItem it)
        {
            string path = store.ModelPath(it.modelFile);
            string err = await ModelLoader.Load(path, it, litTemplate, true);
            if (err != "")
                SetStatus(err);
            else if (selected == it)
                gizmo.Show(it.transform);
        }

        void SetColor(AtlasItem it, Color c)
        {
            it.color = c;
            var r = it.GetComponent<Renderer>();
            if (r == null) return;
            var m = r.sharedMaterial;
            if (it.kind == "light")
            {
                m.SetColor("_BaseColor", c);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * 2f);
                var l = it.GetComponentInChildren<Light>();
                if (l != null) l.color = c;
            }
            else
            {
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", 0.45f);
            }
        }

        void AddShape(string kind)
        {
            count++;
            float a = count * 1.3f;
            float rad = items.Count > 0 ? 1.8f : 0f;
            var d = new ItemData
            {
                kind = kind,
                name = DisplayName(kind) + " " + count,
                position = new Vector3(Mathf.Cos(a) * rad, kind == "plane" ? 0.01f : (kind == "light" ? 2.5f : 0.5f), Mathf.Sin(a) * rad),
                scale = kind == "light" ? Vector3.one * 0.25f : Vector3.one,
                color = kind == "light" ? new Color(1f, 0.92f, 0.75f) : Color.HSVToRGB((count * 0.17f) % 1f, 0.45f, 0.9f),
            };
            if (kind == "cylinder" || kind == "capsule") d.position.y = 1f;
            Select(Create(d));
            HideAddMenu();
            Commit(true);
        }

        void ImportModel()
        {
            HideAddMenu();
            if (NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                string file;
                try
                {
                    file = store.AddModelFile(path);
                }
                catch (System.Exception e)
                {
                    SetStatus("No se pudo copiar el modelo: " + e.Message);
                    return;
                }
                count++;
                var d = new ItemData
                {
                    kind = "model",
                    name = System.IO.Path.GetFileNameWithoutExtension(path),
                    position = new Vector3(0f, 0f, 0f),
                    modelFile = file,
                };
                Select(Create(d));
                SetStatus("Importando «" + d.name + "»…");
                Commit(true);
            }, new string[] { "*/*" });
        }

        void Select(AtlasItem it)
        {
            selected = it;
            gizmo.Show(it != null ? it.transform : null);
            selPanel.SetActive(it != null);
            if (it != null)
            {
                selName.text = it.gameObject.name;
                SetStatus("Seleccionado: " + it.gameObject.name + " · arrastra las flechas de colores");
            }
        }

        void DeleteSelected()
        {
            if (selected == null) return;
            items.Remove(selected);
            Destroy(selected.gameObject);
            Select(null);
            Commit(true);
            SetStatus("Borrado");
        }

        void DuplicateSelected()
        {
            if (selected == null) return;
            var data = SceneStore.Capture(new[] { selected }).items[0];
            data.id = SceneStore.NewId();
            data.name = selected.gameObject.name + " (copia)";
            data.position += new Vector3(0.6f, 0f, 0.6f);
            Select(Create(data));
            Commit(true);
        }

        // ------------------------------------------------------------------
        // Undo / redo / save

        SceneData Snapshot()
        {
            var d = SceneStore.Capture(items);
            d.cameraTarget = target.position;
            d.cameraYaw = yaw;
            d.cameraPitch = pitch;
            d.cameraDistance = distance;
            return d;
        }

        /// After every finished change: one undo step (and autosave).
        void Commit(bool save)
        {
            store.Push(Snapshot());
            RefreshUndo();
            if (save) SaveNow(false);
        }

        void SaveNow(bool tell)
        {
            try
            {
                store.Save(Snapshot());
                if (tell) SetStatus("Proyecto guardado");
            }
            catch (System.Exception e)
            {
                SetStatus("No se pudo guardar: " + e.Message);
            }
        }

        void Restore(SceneData d)
        {
            string keep = selected != null ? selected.id : null;
            foreach (var it in items)
                if (it != null) Destroy(it.gameObject);
            items.Clear();
            AtlasItem sel = null;
            foreach (var data in d.items)
            {
                var it = Create(data);
                if (data.id == keep) sel = it;
            }
            count = items.Count;
            target.position = d.cameraTarget;
            yaw = d.cameraYaw;
            pitch = d.cameraPitch;
            distance = d.cameraDistance;
            UpdateCamera();
            Select(sel);
        }

        void Undo()
        {
            var d = store.Undo();
            if (d == null) return;
            Restore(d);
            RefreshUndo();
            SaveNow(false);
        }

        void Redo()
        {
            var d = store.Redo();
            if (d == null) return;
            Restore(d);
            RefreshUndo();
            SaveNow(false);
        }

        void RefreshUndo()
        {
            if (undoBtn != null) undoBtn.interactable = store.CanUndo;
            if (redoBtn != null) redoBtn.interactable = store.CanRedo;
        }

        // ------------------------------------------------------------------
        // Touch / mouse input

        Vector2 downPos;
        float moved = -1f;   // < 0: no finger down on the 3D view
        bool draggingGizmo, orbiting;

        bool OverUi(int pointerId)
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId);
        }

        void Update()
        {
            if (Screen.safeArea != lastSafe) ApplySafeArea();
            if (Input.touchCount == 1)
            {
                Touch t = Input.GetTouch(0);
                if (t.phase == TouchPhase.Began)
                {
                    if (OverUi(t.fingerId)) { orbiting = false; draggingGizmo = false; moved = -1f; return; }
                    BeginPointer(t.position);
                }
                else if (moved >= 0f && (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary))
                    MovePointer(t.position, t.deltaPosition);
                else if (moved >= 0f && (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled))
                    EndPointer(t.position);
            }
            else if (Input.touchCount >= 2)
            {
                if (draggingGizmo) { gizmo.End(); draggingGizmo = false; Commit(true); }
                moved = 99f;   // not a tap any more
                Touch a = Input.GetTouch(0), b = Input.GetTouch(1);
                Vector2 pa = a.position - a.deltaPosition, pb = b.position - b.deltaPosition;
                float before = (pa - pb).magnitude, now = (a.position - b.position).magnitude;
                if (before > 1f && now > 1f)
                    distance = Mathf.Clamp(distance * before / now, 0.3f, 400f);
                Vector2 pan = (a.deltaPosition + b.deltaPosition) * 0.5f;
                float k = distance * 0.0016f;
                target.position -= cam.transform.right * pan.x * k + cam.transform.up * pan.y * k;
                UpdateCamera();
            }
            else
            {
                if (Input.GetMouseButtonDown(0))
                {
                    if (OverUi(-1)) { moved = -1f; return; }
                    BeginPointer(Input.mousePosition);
                }
                else if (Input.GetMouseButton(0) && moved >= 0f)
                    MovePointer(Input.mousePosition, new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 12f);
                else if (Input.GetMouseButtonUp(0) && moved >= 0f)
                    EndPointer(Input.mousePosition);
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f && !OverUi(-1))
                {
                    distance = Mathf.Clamp(distance * (1f - wheel * 0.1f), 0.3f, 400f);
                    UpdateCamera();
                }
            }
        }

        void BeginPointer(Vector2 pos)
        {
            downPos = pos;
            moved = 0f;
            draggingGizmo = gizmo.TryBegin(pos);
            orbiting = !draggingGizmo;
        }

        void MovePointer(Vector2 pos, Vector2 delta)
        {
            moved += delta.magnitude;
            if (draggingGizmo)
            {
                gizmo.Drag(pos);
                return;
            }
            if (orbiting && moved > 6f)
            {
                yaw += delta.x * 0.25f;
                pitch = Mathf.Clamp(pitch - delta.y * 0.25f, -85f, 85f);
                UpdateCamera();
            }
        }

        void EndPointer(Vector2 pos)
        {
            if (draggingGizmo)
            {
                gizmo.End();
                draggingGizmo = false;
                Commit(true);
                return;
            }
            bool tap = (pos - downPos).magnitude < 14f && moved < 24f;
            orbiting = false;
            moved = -1f;
            if (!tap) return;
            RaycastHit hit;
            AtlasItem it = null;
            if (Physics.Raycast(cam.ScreenPointToRay(pos), out hit, 2000f))
                it = hit.collider.GetComponentInParent<AtlasItem>();
            Select(it);
            if (it == null) SetStatus("Nada seleccionado");
        }

        void UpdateCamera()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            cam.transform.position = target.position + rot * new Vector3(0f, 0f, -distance);
            cam.transform.rotation = rot;
        }

        void FocusSelected()
        {
            if (selected == null) return;
            var rs = selected.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) { target.position = selected.transform.position; UpdateCamera(); return; }
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            target.position = b.center;
            distance = Mathf.Clamp(b.size.magnitude * 1.6f, 1f, 300f);
            UpdateCamera();
        }

        // ------------------------------------------------------------------
        // Interface

        void SetTool(Tool t)
        {
            tool = t;
            gizmo.SetTool(t);
            AtlasTheme.SetActive(moveBtn, t == Tool.Move);
            AtlasTheme.SetActive(rotateBtn, t == Tool.Rotate);
            AtlasTheme.SetActive(scaleBtn, t == Tool.Scale);
            SetStatus(t == Tool.Move ? "Mover: arrastra una flecha (o la bola blanca, por el suelo)"
                : t == Tool.Rotate ? "Girar: arrastra una bola de color hacia los lados"
                : "Escalar: arrastra un cubo de color (o la bola blanca, todo a la vez)");
        }

        void BuildUi()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            var canvasGo = new GameObject("Interfaz");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.6f;
            canvasGo.AddComponent<GraphicRaycaster>();

            safeRoot = AtlasTheme.Rect(canvasGo.transform, "AreaSegura");
            ApplySafeArea();

            // Top bar
            var bar = AtlasTheme.Box(safeRoot, "BarraSuperior", AtlasTheme.Panel);
            var br = bar.rectTransform;
            br.anchorMin = new Vector2(0f, 1f);
            br.anchorMax = new Vector2(1f, 1f);
            br.pivot = new Vector2(0.5f, 1f);
            br.sizeDelta = new Vector2(0f, 68f);
            var row = AtlasTheme.Row(bar.gameObject, 10f, 10);
            row.padding = new RectOffset(14, 14, 10, 10);

            var logoTex = Resources.Load<Texture2D>("AtlasLogo");
            if (logoTex != null)
            {
                var logo = AtlasTheme.Rect(bar.transform, "Logo").gameObject.AddComponent<RawImage>();
                logo.texture = logoTex;
                logo.raycastTarget = false;
                var le = logo.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 46f;
                le.preferredHeight = 46f;
            }
            var title = AtlasTheme.Label(bar.transform, "ATLAS 3D", 26, AtlasTheme.TextColor, FontStyle.Bold);
            var tle = title.gameObject.AddComponent<LayoutElement>();
            tle.preferredWidth = 150f;
            var spacer = AtlasTheme.Rect(bar.transform, "Espacio").gameObject;
            AtlasTheme.Flexible(spacer);
            undoBtn = AtlasTheme.Button(bar.transform, "Deshacer", Undo);
            redoBtn = AtlasTheme.Button(bar.transform, "Rehacer", Redo);
            AtlasTheme.Button(bar.transform, "Guardar", () => SaveNow(true), true);

            // Left tool rail
            var rail = AtlasTheme.Box(safeRoot, "Herramientas", AtlasTheme.Panel);
            var rr = rail.rectTransform;
            rr.anchorMin = new Vector2(0f, 0f);
            rr.anchorMax = new Vector2(0f, 1f);
            rr.pivot = new Vector2(0f, 0.5f);
            rr.offsetMin = new Vector2(0f, 44f);
            rr.offsetMax = new Vector2(110f, -68f);
            AtlasTheme.Column(rail.gameObject, 8f, 8);
            var add = AtlasTheme.Button(rail.transform, "+ Añadir", ToggleAddMenu, true, 0f, 56f);
            moveBtn = AtlasTheme.Button(rail.transform, "Mover", () => SetTool(Tool.Move), false, 0f, 56f);
            rotateBtn = AtlasTheme.Button(rail.transform, "Girar", () => SetTool(Tool.Rotate), false, 0f, 56f);
            scaleBtn = AtlasTheme.Button(rail.transform, "Escalar", () => SetTool(Tool.Scale), false, 0f, 56f);
            AtlasTheme.Button(rail.transform, "Centrar", FocusSelected, false, 0f, 56f);

            // Add menu (opens next to the rail)
            var menu = AtlasTheme.Box(safeRoot, "MenuAnadir", AtlasTheme.Panel2);
            var mr = menu.rectTransform;
            mr.anchorMin = new Vector2(0f, 1f);
            mr.anchorMax = new Vector2(0f, 1f);
            mr.pivot = new Vector2(0f, 1f);
            mr.anchoredPosition = new Vector2(118f, -76f);
            mr.sizeDelta = new Vector2(260f, 0f);
            AtlasTheme.Column(menu.gameObject, 6f, 10);
            var fit = menu.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            AtlasTheme.Label(menu.transform, "AÑADIR", 15, AtlasTheme.TextDim, FontStyle.Bold).gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            AtlasTheme.Button(menu.transform, "Cubo", () => AddShape("cube"), false, 0f, 50f);
            AtlasTheme.Button(menu.transform, "Esfera", () => AddShape("sphere"), false, 0f, 50f);
            AtlasTheme.Button(menu.transform, "Cilindro", () => AddShape("cylinder"), false, 0f, 50f);
            AtlasTheme.Button(menu.transform, "Cápsula", () => AddShape("capsule"), false, 0f, 50f);
            AtlasTheme.Button(menu.transform, "Plano (suelo)", () => AddShape("plane"), false, 0f, 50f);
            AtlasTheme.Button(menu.transform, "Luz", () => AddShape("light"), false, 0f, 50f);
            AtlasTheme.Button(menu.transform, "Importar modelo (GLB / glTF)…", ImportModel, true, 0f, 54f);
            addMenu = menu.gameObject;
            addMenu.SetActive(false);

            // Selected object panel (bottom right)
            var sel = AtlasTheme.Box(safeRoot, "Seleccion", AtlasTheme.Panel);
            var sr = sel.rectTransform;
            sr.anchorMin = new Vector2(1f, 0f);
            sr.anchorMax = new Vector2(1f, 0f);
            sr.pivot = new Vector2(1f, 0f);
            sr.anchoredPosition = new Vector2(-10f, 52f);
            sr.sizeDelta = new Vector2(330f, 0f);
            AtlasTheme.Column(sel.gameObject, 8f, 12);
            var sfit = sel.gameObject.AddComponent<ContentSizeFitter>();
            sfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            selName = AtlasTheme.Label(sel.transform, "", 20, AtlasTheme.TextColor, FontStyle.Bold);
            selName.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            var colors = AtlasTheme.Rect(sel.transform, "Colores").gameObject;
            var grid = colors.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(34f, 34f);
            grid.spacing = new Vector2(4f, 4f);
            colors.AddComponent<LayoutElement>().preferredHeight = 34f;
            foreach (var c in Swatches)
            {
                Color cc = c;
                var sw = AtlasTheme.Box(colors.transform, "Color", cc);
                var sb = sw.gameObject.AddComponent<Button>();
                sb.onClick.AddListener(() =>
                {
                    if (selected == null || selected.kind == "model") return;
                    SetColor(selected, cc);
                    Commit(true);
                });
            }
            var actions = AtlasTheme.Rect(sel.transform, "Acciones").gameObject;
            AtlasTheme.Row(actions, 8f, 0);
            actions.AddComponent<LayoutElement>().preferredHeight = 48f;
            AtlasTheme.Button(actions.transform, "Duplicar", DuplicateSelected);
            var del = AtlasTheme.Button(actions.transform, "Borrar", DeleteSelected);
            del.GetComponent<Image>().color = AtlasTheme.Danger * 0.8f;
            selPanel = sel.gameObject;
            selPanel.SetActive(false);

            // Status line
            var foot = AtlasTheme.Box(safeRoot, "Estado", new Color(AtlasTheme.Panel.r, AtlasTheme.Panel.g, AtlasTheme.Panel.b, 0.9f));
            var fr = foot.rectTransform;
            fr.anchorMin = new Vector2(0f, 0f);
            fr.anchorMax = new Vector2(1f, 0f);
            fr.pivot = new Vector2(0.5f, 0f);
            fr.sizeDelta = new Vector2(0f, 40f);
            status = AtlasTheme.Label(foot.transform, "", 17, AtlasTheme.TextDim);
            AtlasTheme.Fill(status.rectTransform, 16f, 200f);
            var ver = AtlasTheme.Label(foot.transform, "ATLAS 3D · Unity · fase 1", 15, AtlasTheme.AccentLight, FontStyle.Normal, TextAnchor.MiddleRight);
            var vr = ver.rectTransform;
            vr.anchorMin = new Vector2(1f, 0f);
            vr.anchorMax = new Vector2(1f, 1f);
            vr.pivot = new Vector2(1f, 0.5f);
            vr.sizeDelta = new Vector2(220f, 0f);
            vr.anchoredPosition = new Vector2(-14f, 0f);

            SetTool(Tool.Move);
            RefreshUndo();
        }

        void ApplySafeArea()
        {
            if (safeRoot == null) return;
            lastSafe = Screen.safeArea;
            Vector2 min = lastSafe.position, max = lastSafe.position + lastSafe.size;
            min.x /= Mathf.Max(1, Screen.width);
            min.y /= Mathf.Max(1, Screen.height);
            max.x /= Mathf.Max(1, Screen.width);
            max.y /= Mathf.Max(1, Screen.height);
            safeRoot.anchorMin = min;
            safeRoot.anchorMax = max;
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;
        }

        void ToggleAddMenu()
        {
            addMenu.SetActive(!addMenu.activeSelf);
        }

        void HideAddMenu()
        {
            if (addMenu != null) addMenu.SetActive(false);
        }

        void SetStatus(string s)
        {
            if (status != null) status.text = s;
        }
    }
}
