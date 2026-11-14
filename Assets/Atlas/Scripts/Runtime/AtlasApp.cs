// ATLAS 3D (Unity) - the editor, with the same interface as the Godot
// version: left tool rail (home, select, move, rotate, scale | add,
// duplicate, delete, deselect | centre), floating top bar (undo, redo,
// view, Animar, properties, Render, save, menu), the «OBJETO · Mover» pill,
// the axis gizmo, the properties panel and the animation timeline.
// Touch: one finger orbits (or drags a gizmo handle), two fingers zoom and
// pan, a tap selects. The scene is saved in the project and reopened.
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Atlas
{
    public class AtlasApp : MonoBehaviour
    {
        Camera cam;
        Material litTemplate, unlitTemplate;
        Transform target;   // orbit centre
        float yaw = 30f, pitch = 22f, distance = 7f;
        readonly List<AtlasItem> items = new List<AtlasItem>();
        AtlasItem selected;
        Gizmo gizmo;
        Tool tool = Tool.Move;
        SceneStore store;
        GameObject ground;
        int count;

        // Animation
        int fps = 24;
        int endFrame = 120;
        float frame;
        bool playing;

        // UI
        RectTransform safeRoot, viewArea;
        Button selectBtn, moveBtn, rotateBtn, scaleBtn, undoBtn, redoBtn, animBtn, propsBtn, eyeBtn;
        GameObject addMenu, mainMenu, propsPanel, timeline;
        Text modeText;
        RectTransform toastHolder;
        UnityEngine.Rect lastSafe;
        // Properties
        InputField nameField;
        readonly InputField[] tf = new InputField[9];
        Text propsTitle;
        GameObject propsObject, propsScene;
        Transform sceneList;
        // Timeline
        Slider frameSlider;
        Text timeText, frameText;
        Button playBtn, keyBtn;
        RectTransform keyMarks;
        InputField durationField;
        bool sliderLock;
        // Axis gizmo
        RectTransform nav;
        readonly RectTransform[] navBalls = new RectTransform[6];
        static readonly Vector3[] NavDirs = { Vector3.right, Vector3.up, Vector3.forward, Vector3.left, Vector3.down, Vector3.back };

        const float RailWidth = 72f;

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
                Toast("Proyecto abierto");
            }
            else
            {
                AddShape("cube");
                Toast("Toca un objeto para seleccionarlo · un dedo gira la vista · dos dedos: zoom y desplazar");
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
            cam.backgroundColor = AtlasTheme.Viewport;
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

            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
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
            Color bg = new Color(0.25f, 0.26f, 0.3f);
            Color line = new Color(0.38f, 0.39f, 0.44f);
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
            it.keys = d.keys != null ? new List<KeyData>(d.keys) : new List<KeyData>();
            go.transform.position = d.position;
            go.transform.eulerAngles = d.rotation;
            go.transform.localScale = d.scale;
            items.Add(it);
            if (d.kind != "model")
            {
                go.GetComponent<Renderer>().sharedMaterial = new Material(litTemplate);
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
                Toast(err, true);
            else if (it != null && selected == it)
                ShowGizmo();
        }

        void SetColor(AtlasItem it, Color c)
        {
            it.color = c;
            var r = it.GetComponent<Renderer>();
            if (r == null) return;
            var m = r.sharedMaterial;
            m.SetColor("_BaseColor", c);
            if (it.kind == "light")
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * 2f);
                var l = it.GetComponentInChildren<Light>();
                if (l != null) l.color = c;
            }
            else
            {
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
            CloseMenus();
            Commit(true);
        }

        void ImportModel()
        {
            CloseMenus();
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
                    Toast("No se pudo importar: " + e.Message, true);
                    return;
                }
                count++;
                string nm = Path.GetFileNameWithoutExtension(path);
                var d = new ItemData { kind = "model", name = string.IsNullOrEmpty(nm) ? "Modelo" : nm, position = Vector3.zero, modelFile = file };
                Select(Create(d));
                Toast("Importando «" + d.name + "»…");
                Commit(true);
            }, new string[] { "*/*" });
        }

        void Select(AtlasItem it)
        {
            selected = it;
            ShowGizmo();
            RefreshMode();
            RefreshProps();
            RefreshKeys();
        }

        void ShowGizmo()
        {
            gizmo.Show(selected != null && tool != Tool.Select && !playing ? selected.transform : null);
        }

        void DeleteSelected()
        {
            if (selected == null) { Toast("Selecciona algo para borrarlo"); return; }
            items.Remove(selected);
            Destroy(selected.gameObject);
            Select(null);
            Commit(true);
            Toast("Borrado");
        }

        void DuplicateSelected()
        {
            if (selected == null) { Toast("Selecciona algo para duplicarlo"); return; }
            var data = SceneStore.Capture(new[] { selected }).items[0];
            data.id = SceneStore.NewId();
            data.name = selected.gameObject.name + " (copia)";
            data.position += new Vector3(0.6f, 0f, 0.6f);
            data.keys = new List<KeyData>();
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
            d.fps = fps;
            d.endFrame = endFrame;
            d.showGrid = ground.activeSelf;
            return d;
        }

        /// After every finished change: one undo step (and autosave).
        void Commit(bool save)
        {
            store.Push(Snapshot());
            RefreshUndo();
            RefreshKeys();
            if (propsPanel != null && propsPanel.activeSelf && selected == null) RefreshProps();
            if (save) SaveNow(false);
        }

        void SaveNow(bool tell)
        {
            try
            {
                store.Save(Snapshot());
                if (tell) Toast("Proyecto guardado");
            }
            catch (System.Exception e)
            {
                Toast("No se pudo guardar: " + e.Message, true);
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
            fps = Mathf.Max(1, d.fps);
            endFrame = Mathf.Max(1, d.endFrame);
            ground.SetActive(d.showGrid);
            UpdateCamera();
            SetFrame(Mathf.Min(frame, endFrame));
            Select(sel);
            RefreshTimeline();
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

        /// A change made by hand on an animated object becomes its key at
        /// this frame (otherwise the animation would put it back).
        void AutoKey()
        {
            if (selected != null && selected.Animated)
                selected.SetKey(Mathf.RoundToInt(frame));
        }

        // ------------------------------------------------------------------
        // Animation

        void SetFrame(float f)
        {
            frame = Mathf.Clamp(f, 0f, endFrame);
            foreach (var it in items)
                if (it != null && it.Animated) it.Evaluate(frame);
            RefreshTimeline();
        }

        void TogglePlay()
        {
            playing = !playing;
            if (playing && frame >= endFrame) frame = 0f;
            AtlasTheme.SetButtonIcon(playBtn, playing ? "pause" : "play");
            ShowGizmo();
        }

        void InsertKey()
        {
            if (selected == null) { Toast("Selecciona un objeto para guardar su clave"); return; }
            selected.SetKey(Mathf.RoundToInt(frame));
            Commit(true);
            Toast("Clave guardada en el fotograma " + Mathf.RoundToInt(frame));
        }

        void DeleteKey()
        {
            if (selected == null || !selected.RemoveKey(Mathf.RoundToInt(frame)))
            {
                Toast("No hay clave en este fotograma");
                return;
            }
            Commit(true);
        }

        void JumpKey(int dir)
        {
            if (selected == null || !selected.Animated) { SetFrame(Mathf.RoundToInt(frame) + dir); return; }
            int f = Mathf.RoundToInt(frame);
            int best = -1;
            foreach (var k in selected.keys)
                if ((dir > 0 && k.frame > f && (best < 0 || k.frame < best)) || (dir < 0 && k.frame < f && (best < 0 || k.frame > best)))
                    best = k.frame;
            if (best >= 0) SetFrame(best); else SetFrame(f + dir);
        }

        void RefreshTimeline()
        {
            if (frameSlider == null) return;
            sliderLock = true;
            frameSlider.maxValue = endFrame;
            frameSlider.value = frame;
            sliderLock = false;
            frameText.text = Mathf.RoundToInt(frame).ToString();
            timeText.text = (frame / fps).ToString("0.0") + " / " + ((float)endFrame / fps).ToString("0.0") + " s";
            AtlasTheme.SetNumber(durationField, (float)endFrame / fps);
            if (keyBtn != null)
            {
                bool here = selected != null && selected.HasKey(Mathf.RoundToInt(frame));
                foreach (var ri in keyBtn.GetComponentsInChildren<RawImage>()) ri.color = here ? AtlasTheme.Key : new Color(0.75f, 0.62f, 0.32f);
            }
        }

        /// Yellow marks on the timeline where the selected object has keys.
        void RefreshKeys()
        {
            if (keyMarks == null) return;
            for (int i = keyMarks.childCount - 1; i >= 0; i--) Destroy(keyMarks.GetChild(i).gameObject);
            if (selected != null)
            {
                foreach (var k in selected.keys)
                {
                    var m = AtlasTheme.Box(keyMarks, "Clave", AtlasTheme.Key, 3);
                    var r = m.rectTransform;
                    float x = endFrame > 0 ? (float)k.frame / endFrame : 0f;
                    r.anchorMin = r.anchorMax = new Vector2(x, 0.5f);
                    r.sizeDelta = new Vector2(12f, 12f);
                    r.anchoredPosition = Vector2.zero;
                    r.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    m.raycastTarget = false;
                }
            }
            RefreshTimeline();
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
            if (playing)
            {
                float f = frame + Time.deltaTime * fps;
                if (f > endFrame) f = 0f;
                SetFrame(f);
            }
            UpdateNav();
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
                if (draggingGizmo) { gizmo.End(); draggingGizmo = false; AutoKey(); Commit(true); }
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

        void LateUpdate()
        {
            if (selected != null && propsPanel != null && propsPanel.activeSelf) FillTransformFields();
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
                AutoKey();
                Commit(true);
                moved = -1f;
                return;
            }
            bool tap = (pos - downPos).magnitude < 14f && moved < 24f;
            orbiting = false;
            moved = -1f;
            if (!tap) return;
            CloseMenus();
            RaycastHit hit;
            AtlasItem it = null;
            if (Physics.Raycast(cam.ScreenPointToRay(pos), out hit, 2000f))
                it = hit.collider.GetComponentInParent<AtlasItem>();
            Select(it);
        }

        void UpdateCamera()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            cam.transform.position = target.position + rot * new Vector3(0f, 0f, -distance);
            cam.transform.rotation = rot;
        }

        void FocusSelected()
        {
            if (selected == null)
            {
                target.position = new Vector3(0f, 0.5f, 0f);
                distance = 7f;
                UpdateCamera();
                return;
            }
            var rs = selected.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) { target.position = selected.transform.position; UpdateCamera(); return; }
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            target.position = b.center;
            distance = Mathf.Clamp(b.size.magnitude * 1.6f, 1f, 300f);
            UpdateCamera();
        }

        // ------------------------------------------------------------------
        // Render (picture of the view, without the interface)

        void RenderPicture()
        {
            CloseMenus();
            bool g = gizmo.gameObject.activeSelf;
            gizmo.gameObject.SetActive(false);
            int w = 1920, h = 1080;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            cam.Render();
            cam.targetTexture = prevTarget;
            cam.ResetAspect();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new UnityEngine.Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            gizmo.gameObject.SetActive(g);
            string path = Path.Combine(Exporter.ExportDir, "render_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Share(path, "Render");
        }

        void Share(string path, string what)
        {
            Toast(what + " listo: elige dónde guardarlo");
            NativeFilePicker.ExportFile(path, ok =>
            {
                Toast(ok ? what + " guardado" : what + " guardado en la carpeta de la app (" + Path.GetFileName(path) + ")");
            });
        }

        async void ExportAs(string format)
        {
            CloseMenus();
            Toast("Exportando a " + format + "…");
            try
            {
                string path = await Exporter.Export(format, items, "atlas3d");
                Share(path, format);
            }
            catch (System.Exception e)
            {
                Toast("No se pudo exportar a " + format + ": " + e.Message, true);
            }
        }

        void NewScene()
        {
            CloseMenus();
            foreach (var it in items) if (it != null) Destroy(it.gameObject);
            items.Clear();
            count = 0;
            Select(null);
            SetFrame(0f);
            Commit(true);
            Toast("Escena vacía (Deshacer la recupera)");
        }

        // ------------------------------------------------------------------
        // Interface

        void SetTool(Tool t)
        {
            tool = t;
            gizmo.SetTool(t);
            AtlasTheme.SetActive(selectBtn, t == Tool.Select);
            AtlasTheme.SetActive(moveBtn, t == Tool.Move);
            AtlasTheme.SetActive(rotateBtn, t == Tool.Rotate);
            AtlasTheme.SetActive(scaleBtn, t == Tool.Scale);
            ShowGizmo();
            RefreshMode();
        }

        void RefreshMode()
        {
            if (modeText == null) return;
            string toolName = tool == Tool.Select ? "Seleccionar" : tool == Tool.Move ? "Mover" : tool == Tool.Rotate ? "Girar" : "Escalar";
            modeText.text = toolName + " · " + (selected != null ? selected.gameObject.name : "Nada seleccionado");
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
            scaler.referenceResolution = new Vector2(1280f, 640f);
            scaler.matchWidthOrHeight = 0.75f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Behind the safe area (notch side): same colour as the rail.
            var edge = AtlasTheme.Box(canvasGo.transform, "Borde", AtlasTheme.Panel);
            edge.rectTransform.anchorMin = Vector2.zero;
            edge.rectTransform.anchorMax = new Vector2(0f, 1f);
            edge.rectTransform.pivot = new Vector2(0f, 0.5f);
            edge.rectTransform.offsetMin = Vector2.zero;
            edge.rectTransform.offsetMax = Vector2.zero;
            edge.raycastTarget = false;
            safeRoot = AtlasTheme.Rect(canvasGo.transform, "AreaSegura");
            ApplySafeArea();

            BuildRail();
            viewArea = AtlasTheme.Rect(safeRoot, "Visor");
            AtlasTheme.Fill(viewArea, RailWidth);
            BuildModePill();
            BuildNav();
            BuildTimeline();
            BuildProps();
            BuildTopBar();
            BuildAddMenu();
            BuildMainMenu();
            toastHolder = AtlasTheme.Rect(viewArea, "Avisos");
            AtlasTheme.Pin(toastHolder, 0.5f, 0f, new Vector2(0f, 20f), new Vector2(760f, 0f));
            var tc = AtlasTheme.Column(toastHolder.gameObject, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.LowerCenter);
            tc.childForceExpandWidth = false;
            var tfit = toastHolder.gameObject.AddComponent<ContentSizeFitter>();
            tfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            SetTool(Tool.Move);
            RefreshUndo();
            timeline.SetActive(false);
            propsPanel.SetActive(false);
        }

        void BuildRail()
        {
            var rail = AtlasTheme.Box(safeRoot, "Herramientas", AtlasTheme.Panel);
            var rr = rail.rectTransform;
            rr.anchorMin = new Vector2(0f, 0f);
            rr.anchorMax = new Vector2(0f, 1f);
            rr.pivot = new Vector2(0f, 0.5f);
            rr.offsetMin = new Vector2(0f, 0f);
            rr.offsetMax = new Vector2(RailWidth, 0f);
            var col = AtlasTheme.Column(rail.gameObject, 4f, new RectOffset(10, 10, 12, 12));
            col.childForceExpandWidth = false;
            col.childAlignment = TextAnchor.UpperCenter;
            AtlasTheme.IconButton(rail.transform, "home", () => ToggleMenu(mainMenu));
            AtlasTheme.Separator(rail.transform, false);
            selectBtn = AtlasTheme.IconButton(rail.transform, "select", () => SetTool(Tool.Select));
            moveBtn = AtlasTheme.IconButton(rail.transform, "move", () => SetTool(Tool.Move));
            rotateBtn = AtlasTheme.IconButton(rail.transform, "rotate", () => SetTool(Tool.Rotate));
            scaleBtn = AtlasTheme.IconButton(rail.transform, "scale", () => SetTool(Tool.Scale));
            AtlasTheme.Separator(rail.transform, false);
            AtlasTheme.IconButton(rail.transform, "add", () => ToggleMenu(addMenu));
            AtlasTheme.IconButton(rail.transform, "duplicate", DuplicateSelected);
            AtlasTheme.IconButton(rail.transform, "delete", DeleteSelected);
            AtlasTheme.IconButton(rail.transform, "deselect", () => Select(null));
            AtlasTheme.Separator(rail.transform, false);
            AtlasTheme.IconButton(rail.transform, "focus", FocusSelected);
        }

        void BuildTopBar()
        {
            var bar = AtlasTheme.Box(viewArea, "BarraFlotante", AtlasTheme.Panel, 18);
            var r = bar.rectTransform;
            AtlasTheme.Pin(r, 1f, 1f, new Vector2(-10f, -10f), new Vector2(0f, 62f));
            AtlasTheme.Row(bar.gameObject, 4f, new RectOffset(8, 8, 6, 6));
            var fit = bar.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            undoBtn = AtlasTheme.IconButton(bar.transform, "undo", Undo);
            redoBtn = AtlasTheme.IconButton(bar.transform, "redo", Redo);
            eyeBtn = AtlasTheme.IconButton(bar.transform, "eye", ToggleGrid);
            animBtn = AtlasTheme.TextButton(bar.transform, "timeline", "Animar", ToggleTimeline);
            animBtn.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            propsBtn = AtlasTheme.IconButton(bar.transform, "settings", ToggleProps);
            AtlasTheme.TextButton(bar.transform, "render", "Render", RenderPicture, true);
            var save = AtlasTheme.IconButton(bar.transform, "save", () => SaveNow(true));
            foreach (var ri in save.GetComponentsInChildren<RawImage>()) ri.color = AtlasTheme.Key;
            AtlasTheme.IconButton(bar.transform, "menu", () => ToggleMenu(mainMenu));
        }

        void BuildModePill()
        {
            var pill = AtlasTheme.Box(viewArea, "Modo", AtlasTheme.Panel, 20);
            AtlasTheme.Pin(pill.rectTransform, 0f, 1f, new Vector2(12f, -16f), new Vector2(0f, 42f));
            AtlasTheme.Row(pill.gameObject, 10f, new RectOffset(8, 16, 6, 6));
            var fit = pill.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            pill.raycastTarget = false;
            var badge = AtlasTheme.Box(pill.transform, "Insignia", AtlasTheme.Badge, 14);
            badge.raycastTarget = false;
            var ble = badge.gameObject.AddComponent<LayoutElement>();
            ble.preferredWidth = 92f;
            ble.preferredHeight = 30f;
            var badgeText = AtlasTheme.Label(badge.transform, "OBJETO", 15, Color.white, true, TextAnchor.MiddleCenter);
            AtlasTheme.Fill(badgeText.rectTransform);
            modeText = AtlasTheme.Label(pill.transform, "Mover · Nada seleccionado", 19, AtlasTheme.TextColor, true);
            modeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var tle = modeText.gameObject.AddComponent<LayoutElement>();
            tle.preferredHeight = 30f;
            tle.preferredWidth = 300f;
            tle.flexibleWidth = 0f;
        }

        // Axis gizmo: X / Y / Z balls that follow the view; a tap looks along it.
        void BuildNav()
        {
            var bg = AtlasTheme.Box(viewArea, "Ejes", new Color(AtlasTheme.Panel.r, AtlasTheme.Panel.g, AtlasTheme.Panel.b, 0.6f), 60);
            nav = bg.rectTransform;
            AtlasTheme.Pin(nav, 1f, 1f, new Vector2(-24f, -86f), new Vector2(120f, 120f));
            string[] labels = { "X", "Y", "Z", "", "", "" };
            Color[] cols = { AtlasTheme.AxisX, AtlasTheme.AxisY, AtlasTheme.AxisZ, AtlasTheme.AxisX * 0.6f, AtlasTheme.AxisY * 0.6f, AtlasTheme.AxisZ * 0.6f };
            for (int i = 0; i < 6; i++)
            {
                var ball = AtlasTheme.Box(nav, "Eje" + i, cols[i], 15);
                var br = ball.rectTransform;
                br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f);
                br.sizeDelta = i < 3 ? new Vector2(30f, 30f) : new Vector2(22f, 22f);
                if (i < 3)
                {
                    var t = AtlasTheme.Label(br, labels[i], 15, Color.white, true, TextAnchor.MiddleCenter);
                    AtlasTheme.Fill(t.rectTransform);
                }
                Vector3 d = NavDirs[i];
                var b = ball.gameObject.AddComponent<Button>();
                b.onClick.AddListener(() => LookAlong(d));
                navBalls[i] = br;
            }
        }

        void LookAlong(Vector3 axis)
        {
            // From the axis' side, looking at the centre.
            if (Mathf.Abs(axis.y) > 0.5f)
                pitch = axis.y > 0f ? 89f : -89f;
            else
            {
                pitch = 0f;
                yaw = Mathf.Atan2(-axis.x, -axis.z) * Mathf.Rad2Deg;
            }
            UpdateCamera();
        }

        readonly List<int> navOrder = new List<int> { 0, 1, 2, 3, 4, 5 };

        void UpdateNav()
        {
            if (nav == null) return;
            Quaternion inv = Quaternion.Inverse(cam.transform.rotation);
            for (int i = 0; i < 6; i++)
            {
                Vector3 v = inv * NavDirs[i];
                navBalls[i].anchoredPosition = new Vector2(v.x, v.y) * 40f;
            }
            // Furthest first, so the balls pointing at the viewer are on top.
            navOrder.Sort((a, b) => (inv * NavDirs[b]).z.CompareTo((inv * NavDirs[a]).z));
            for (int i = 0; i < 6; i++) navBalls[navOrder[i]].SetSiblingIndex(i);
        }

        void BuildTimeline()
        {
            var tl = AtlasTheme.Box(viewArea, "LineaDeTiempo", AtlasTheme.Panel);
            var r = tl.rectTransform;
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.sizeDelta = new Vector2(0f, 124f);
            r.anchoredPosition = Vector2.zero;
            timeline = tl.gameObject;
            var col = AtlasTheme.Column(timeline, 6f, new RectOffset(14, 14, 8, 10));
            col.childForceExpandWidth = true;

            var row = AtlasTheme.Rect(timeline.transform, "Controles").gameObject;
            AtlasTheme.Row(row, 6f, new RectOffset(0, 0, 0, 0));
            row.AddComponent<LayoutElement>().preferredHeight = 54f;
            playBtn = AtlasTheme.IconButton(row.transform, "play", TogglePlay, 54f, 26f);
            playBtn.GetComponent<Image>().color = AtlasTheme.AccentButton;
            AtlasTheme.IconButton(row.transform, "step_back", () => JumpKey(-1));
            AtlasTheme.IconButton(row.transform, "step_fwd", () => JumpKey(1));
            keyBtn = AtlasTheme.IconButton(row.transform, "key_fill", InsertKey);
            AtlasTheme.IconButton(row.transform, "delete", DeleteKey);
            var fbox = AtlasTheme.Box(row.transform, "Fotograma", AtlasTheme.Panel2, 10);
            var fle = fbox.gameObject.AddComponent<LayoutElement>();
            fle.preferredWidth = 70f;
            fle.preferredHeight = 44f;
            frameText = AtlasTheme.Label(fbox.transform, "0", 19, AtlasTheme.TextColor, true, TextAnchor.MiddleCenter);
            AtlasTheme.Fill(frameText.rectTransform);
            timeText = AtlasTheme.Label(row.transform, "0.0 / 5.0 s", 18, AtlasTheme.TextColor, true);
            timeText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var tle = timeText.gameObject.AddComponent<LayoutElement>();
            tle.preferredWidth = 130f;
            AtlasTheme.Spacer(row.transform);
            var dl = AtlasTheme.Label(row.transform, "Duración (s)", 16, AtlasTheme.TextDim);
            dl.horizontalOverflow = HorizontalWrapMode.Overflow;
            dl.gameObject.AddComponent<LayoutElement>().preferredWidth = 104f;
            durationField = AtlasTheme.NumberField(row.transform, endFrame / (float)fps, v =>
            {
                endFrame = Mathf.Clamp(Mathf.RoundToInt(v * fps), 1, 600 * fps);
                SetFrame(Mathf.Min(frame, endFrame));
                RefreshKeys();
                Commit(true);
            }, 70f);

            // Ruler: slider with the selected object's keys.
            var bar = AtlasTheme.Rect(timeline.transform, "Regla");
            bar.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            var track = AtlasTheme.Box(bar, "Pista", AtlasTheme.Panel3, 5);
            var trr = track.rectTransform;
            trr.anchorMin = new Vector2(0f, 0.5f);
            trr.anchorMax = new Vector2(1f, 0.5f);
            trr.sizeDelta = new Vector2(0f, 10f);
            track.raycastTarget = false;
            keyMarks = AtlasTheme.Rect(bar, "Claves");
            AtlasTheme.Fill(keyMarks, 12f, 12f);
            var area = AtlasTheme.Rect(bar, "Deslizador");
            AtlasTheme.Fill(area, 12f, 12f);
            var hitArea = area.gameObject.AddComponent<Image>();
            hitArea.color = new Color(0, 0, 0, 0);
            var handleArea = AtlasTheme.Rect(area, "Zona");
            AtlasTheme.Fill(handleArea);
            var handle = AtlasTheme.Box(handleArea, "Cabezal", AtlasTheme.Accent, 4);
            handle.rectTransform.anchorMin = new Vector2(0f, 0f);
            handle.rectTransform.anchorMax = new Vector2(0f, 1f);
            handle.rectTransform.sizeDelta = new Vector2(8f, 0f);
            frameSlider = area.gameObject.AddComponent<Slider>();
            frameSlider.handleRect = handle.rectTransform;
            frameSlider.targetGraphic = handle;
            frameSlider.direction = Slider.Direction.LeftToRight;
            frameSlider.minValue = 0f;
            frameSlider.maxValue = endFrame;
            frameSlider.wholeNumbers = true;
            var nav2 = frameSlider.navigation;
            nav2.mode = Navigation.Mode.None;
            frameSlider.navigation = nav2;
            frameSlider.onValueChanged.AddListener(v =>
            {
                if (sliderLock) return;
                if (playing) TogglePlay();
                SetFrame(v);
            });
        }

        void BuildProps()
        {
            var panel = AtlasTheme.Box(viewArea, "Propiedades", AtlasTheme.Panel, 18);
            var r = panel.rectTransform;
            r.anchorMin = new Vector2(1f, 0f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(1f, 1f);
            r.offsetMin = new Vector2(-370f, 10f);
            r.offsetMax = new Vector2(-10f, -82f);
            propsPanel = panel.gameObject;
            AtlasTheme.Column(propsPanel, 10f, new RectOffset(16, 16, 14, 14));
            propsTitle = AtlasTheme.Label(panel.transform, "PROPIEDADES", 15, AtlasTheme.TextDim, true);
            propsTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            // Object
            propsObject = AtlasTheme.Rect(panel.transform, "Objeto").gameObject;
            AtlasTheme.Column(propsObject, 10f, new RectOffset(0, 0, 0, 0));
            var nbox = AtlasTheme.Box(propsObject.transform, "Nombre", AtlasTheme.Panel2, 10);
            nbox.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            var nt = AtlasTheme.Label(nbox.transform, "", 19, AtlasTheme.TextColor, true);
            AtlasTheme.Fill(nt.rectTransform, 12f, 12f);
            nt.raycastTarget = true;
            nameField = nbox.gameObject.AddComponent<InputField>();
            nameField.textComponent = nt;
            nameField.onEndEdit.AddListener(s =>
            {
                if (selected == null || string.IsNullOrEmpty(s.Trim())) return;
                selected.gameObject.name = s.Trim();
                RefreshMode();
                Commit(true);
            });
            string[] rows = { "Posición", "Rotación", "Escala" };
            Color[] ac = { AtlasTheme.AxisX, AtlasTheme.AxisY, AtlasTheme.AxisZ };
            string[] an = { "X", "Y", "Z" };
            for (int row = 0; row < 3; row++)
            {
                var line = AtlasTheme.Rect(propsObject.transform, rows[row]).gameObject;
                AtlasTheme.Row(line, 5f, new RectOffset(0, 0, 0, 0));
                line.AddComponent<LayoutElement>().preferredHeight = 40f;
                var l = AtlasTheme.Label(line.transform, rows[row], 16, AtlasTheme.TextDim);
                l.gameObject.AddComponent<LayoutElement>().preferredWidth = 74f;
                for (int c = 0; c < 3; c++)
                {
                    var al = AtlasTheme.Label(line.transform, an[c], 14, ac[c], true, TextAnchor.MiddleCenter);
                    al.gameObject.AddComponent<LayoutElement>().preferredWidth = 12f;
                    int idx = row * 3 + c;
                    tf[idx] = AtlasTheme.NumberField(line.transform, 0f, v => SetTransformValue(idx, v), 64f);
                }
            }
            var colors = AtlasTheme.Rect(propsObject.transform, "Colores").gameObject;
            var grid = colors.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(34f, 34f);
            grid.spacing = new Vector2(5f, 5f);
            colors.AddComponent<LayoutElement>().preferredHeight = 34f;
            foreach (var c0 in Swatches)
            {
                Color cc = c0;
                var sw = AtlasTheme.Box(colors.transform, "Color", cc, 8);
                var sb = sw.gameObject.AddComponent<Button>();
                sb.onClick.AddListener(() =>
                {
                    if (selected == null) return;
                    if (selected.kind == "model") { Toast("Los modelos importados mantienen sus propios colores"); return; }
                    SetColor(selected, cc);
                    Commit(true);
                });
            }
            var actions = AtlasTheme.Rect(propsObject.transform, "Acciones").gameObject;
            AtlasTheme.Row(actions, 8f, new RectOffset(0, 0, 0, 0));
            actions.AddComponent<LayoutElement>().preferredHeight = 46f;
            AtlasTheme.TextButton(actions.transform, "duplicate", "Duplicar", DuplicateSelected, false, 46f, 17);
            var del = AtlasTheme.TextButton(actions.transform, "delete", "Borrar", DeleteSelected, false, 46f, 17);
            del.GetComponent<Image>().color = new Color(0.5f, 0.17f, 0.19f, 1f);

            // Scene (nothing selected): the list of objects
            propsScene = AtlasTheme.Rect(panel.transform, "Escena").gameObject;
            AtlasTheme.Column(propsScene, 6f, new RectOffset(0, 0, 0, 0));
            var hint = AtlasTheme.Label(propsScene.transform, "Nada seleccionado. Toca un objeto en la vista o en esta lista.", 16, AtlasTheme.TextDim);
            hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            sceneList = AtlasTheme.Rect(propsScene.transform, "Lista");
            AtlasTheme.Column(sceneList.gameObject, 6f, new RectOffset(0, 0, 0, 0));
            AtlasTheme.Spacer(panel.transform);
        }

        void SetTransformValue(int idx, float v)
        {
            if (selected == null) return;
            var t = selected.transform;
            int axis = idx % 3;
            if (idx < 3) { var p = t.position; p[axis] = v; t.position = p; }
            else if (idx < 6) { var e = t.eulerAngles; e[axis] = v; t.eulerAngles = e; }
            else { var s = t.localScale; s[axis] = Mathf.Max(0.001f, v); t.localScale = s; }
            AutoKey();
            Commit(true);
        }

        void FillTransformFields()
        {
            var t = selected.transform;
            for (int c = 0; c < 3; c++)
            {
                AtlasTheme.SetNumber(tf[c], t.position[c]);
                AtlasTheme.SetNumber(tf[3 + c], Mathf.Round(t.eulerAngles[c] * 10f) / 10f);
                AtlasTheme.SetNumber(tf[6 + c], t.localScale[c]);
            }
        }

        void RefreshProps()
        {
            if (propsPanel == null) return;
            bool has = selected != null;
            propsObject.SetActive(has);
            propsScene.SetActive(!has);
            propsTitle.text = has ? "PROPIEDADES" : "ESCENA";
            if (has)
            {
                if (!nameField.isFocused) nameField.text = selected.gameObject.name;
                FillTransformFields();
            }
            else
            {
                for (int i = sceneList.childCount - 1; i >= 0; i--) Destroy(sceneList.GetChild(i).gameObject);
                int shown = 0;
                foreach (var it in items)
                {
                    if (it == null || shown >= 9) continue;
                    var pick = it;
                    string icon = it.kind == "model" ? "import" : it.kind;
                    if (AtlasTheme.Icon(icon) == null) icon = "cube";
                    var b = AtlasTheme.TextButton(sceneList, icon, it.gameObject.name, () => Select(pick), false, 44f, 17);
                    b.GetComponent<Image>().color = AtlasTheme.Panel2;
                    shown++;
                }
            }
        }

        void BuildAddMenu()
        {
            var menu = AtlasTheme.Box(viewArea, "MenuAnadir", AtlasTheme.Panel2, 18);
            AtlasTheme.Pin(menu.rectTransform, 0f, 1f, new Vector2(12f, -66f), new Vector2(300f, 0f));
            AtlasTheme.Column(menu.gameObject, 6f, new RectOffset(12, 12, 12, 12));
            var fit = menu.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var title = AtlasTheme.Label(menu.transform, "AÑADIR", 15, AtlasTheme.TextDim, true);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            AtlasTheme.TextButton(menu.transform, "cube", "Cubo", () => AddShape("cube"), false, 48f);
            AtlasTheme.TextButton(menu.transform, "sphere", "Esfera", () => AddShape("sphere"), false, 48f);
            AtlasTheme.TextButton(menu.transform, "cylinder", "Cilindro", () => AddShape("cylinder"), false, 48f);
            AtlasTheme.TextButton(menu.transform, "capsule", "Cápsula", () => AddShape("capsule"), false, 48f);
            AtlasTheme.TextButton(menu.transform, "plane", "Plano (suelo)", () => AddShape("plane"), false, 48f);
            AtlasTheme.TextButton(menu.transform, "light", "Luz", () => AddShape("light"), false, 48f);
            AtlasTheme.TextButton(menu.transform, "import", "Importar modelo…", ImportModel, true, 50f);
            var formats = AtlasTheme.Label(menu.transform, "GLB · glTF · OBJ · STL · PLY · ZIP", 14, AtlasTheme.TextDim, false, TextAnchor.MiddleCenter);
            formats.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            addMenu = menu.gameObject;
            addMenu.SetActive(false);
        }

        void BuildMainMenu()
        {
            var menu = AtlasTheme.Box(viewArea, "Menu", AtlasTheme.Panel2, 18);
            AtlasTheme.Pin(menu.rectTransform, 1f, 1f, new Vector2(-10f, -80f), new Vector2(330f, 0f));
            AtlasTheme.Column(menu.gameObject, 6f, new RectOffset(12, 12, 12, 12));
            var fit = menu.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var t1 = AtlasTheme.Label(menu.transform, "PROYECTO", 15, AtlasTheme.TextDim, true);
            t1.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            AtlasTheme.TextButton(menu.transform, "import", "Importar modelo…", ImportModel, false, 48f);
            AtlasTheme.TextButton(menu.transform, "save", "Guardar", () => { CloseMenus(); SaveNow(true); }, false, 48f);
            AtlasTheme.TextButton(menu.transform, "close", "Nueva escena vacía", NewScene, false, 48f);
            var t2 = AtlasTheme.Label(menu.transform, "EXPORTAR", 15, AtlasTheme.TextDim, true);
            t2.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            var grid = AtlasTheme.Rect(menu.transform, "Formatos").gameObject;
            var g = grid.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(96f, 46f);
            g.spacing = new Vector2(6f, 6f);
            grid.AddComponent<LayoutElement>().preferredHeight = 98f;
            foreach (var f in Exporter.Formats)
            {
                string fmt = f;
                AtlasTheme.TextButton(grid.transform, null, fmt, () => ExportAs(fmt), false, 46f, 17, 96f);
            }
            var about = AtlasTheme.Label(menu.transform, "ATLAS 3D · versión Unity (fase 2)", 14, AtlasTheme.AccentLight, false, TextAnchor.MiddleCenter);
            about.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
            mainMenu = menu.gameObject;
            mainMenu.SetActive(false);
        }

        void ToggleMenu(GameObject m)
        {
            bool open = !m.activeSelf;
            CloseMenus();
            m.SetActive(open);
        }

        void CloseMenus()
        {
            if (addMenu != null) addMenu.SetActive(false);
            if (mainMenu != null) mainMenu.SetActive(false);
        }

        void ToggleTimeline()
        {
            timeline.SetActive(!timeline.activeSelf);
            AtlasTheme.SetActive(animBtn, timeline.activeSelf);
            toastHolder.anchoredPosition = new Vector2(0f, timeline.activeSelf ? 136f : 20f);
            var pr = propsPanel.GetComponent<RectTransform>();
            pr.offsetMin = new Vector2(pr.offsetMin.x, timeline.activeSelf ? 134f : 10f);
            if (timeline.activeSelf)
            {
                RefreshKeys();
                Toast("Animar: elige un fotograma, coloca el objeto y pulsa la clave amarilla");
            }
        }

        void ToggleProps()
        {
            propsPanel.SetActive(!propsPanel.activeSelf);
            AtlasTheme.SetActive(propsBtn, propsPanel.activeSelf);
            RefreshProps();
        }

        void ToggleGrid()
        {
            ground.SetActive(!ground.activeSelf);
            AtlasTheme.SetButtonIcon(eyeBtn, ground.activeSelf ? "eye" : "eye_off");
            Commit(true);
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
            var edge = safeRoot.parent.Find("Borde") as RectTransform;
            if (edge != null)
            {
                edge.anchorMax = new Vector2(min.x, 1f);
                edge.offsetMax = Vector2.zero;
            }
        }

        // Toasts (messages at the bottom, like the Godot version)
        void Toast(string msg, bool warning = false)
        {
            if (toastHolder == null) return;
            var box = AtlasTheme.Box(toastHolder, "Aviso", new Color(AtlasTheme.Panel.r, AtlasTheme.Panel.g, AtlasTheme.Panel.b, 0.94f), 16);
            box.raycastTarget = false;
            AtlasTheme.Row(box.gameObject, 10f, new RectOffset(16, 18, 10, 10));
            var fit = box.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var ic = AtlasTheme.IconImage(box.transform, warning ? "warning" : "info", 22f, warning ? AtlasTheme.Danger : AtlasTheme.Accent);
            var ile = ic.gameObject.AddComponent<LayoutElement>();
            ile.preferredWidth = 22f;
            ile.preferredHeight = 22f;
            var t = AtlasTheme.Label(box.transform, msg, 18, AtlasTheme.TextColor);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = Mathf.Min(660f, msg.Length * 9.6f + 10f);
            while (toastHolder.childCount > 3) DestroyImmediate(toastHolder.GetChild(0).gameObject);
            Destroy(box.gameObject, warning ? 6f : 3.5f);
        }
    }
}
