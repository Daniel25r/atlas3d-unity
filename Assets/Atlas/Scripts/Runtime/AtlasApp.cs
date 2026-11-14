// ATLAS 3D (Unity) - the editor, with the same interface as the Godot
// version: home screen with the projects, left tool rail (home, select,
// move, rotate, scale | add, duplicate, delete, deselect | centre),
// floating top bar (undo, redo, view, Animar, properties, Render, save,
// menu), the «OBJETO · Mover» pill, the axis gizmo, the inspector and the
// animation timeline.
// This file: the 3D world, objects, undo, animation and touch input.
// AtlasApp.Ui.cs: rail, bars, menus, timeline. AtlasApp.Inspector.cs: the
// properties panel. AtlasApp.Home.cs: projects. AtlasApp.Render.cs: render.
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp : MonoBehaviour
    {
        public const string Version = "3.0.0";

        Camera cam;
        Light sunLight;
        Material litTemplate, unlitTemplate;
        Transform target;   // orbit centre
        float yaw = 30f, pitch = 22f, distance = 7f;
        readonly List<AtlasItem> items = new List<AtlasItem>();
        AtlasItem selected;
        Gizmo gizmo;
        Tool tool = Tool.Move;
        SceneStore store;   // null on the home screen
        GameObject ground;
        int count;
        EnvData env = new EnvData();
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        // Animation
        int fps = 24;
        int endFrame = 120;
        float frame;
        bool playing;

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Input.simulateMouseWithTouches = false;   // a touch is not also a mouse click
            litTemplate = Resources.Load<Material>("AtlasLit");
            unlitTemplate = Resources.Load<Material>("AtlasUnlit");
            BuildWorld();
            Ambience.Init(cam);
            ApplyEnv();
            BuildUi();
            BuildHome();
            ShowHome(false);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) SaveNow(false);
        }

        void OnApplicationQuit()
        {
            SaveNow(false);
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
            cam.allowHDR = true;
            UpdateCamera();

            sunLight = new GameObject("Sol").AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 1.25f;
            sunLight.color = new Color(1f, 0.97f, 0.92f);
            sunLight.shadows = LightShadows.Soft;
            sunLight.shadowStrength = 0.85f;
            sunLight.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

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

        void ApplyEnv()
        {
            Ambience.Apply(env, cam, sunLight);
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
                case "cone": return "Cono";
                case "pyramid": return "Pirámide";
                case "torus": return "Toroide";
                case "light": return "Luz";
                case "spot": return "Foco";
                case "sun": return "Sol";
                default: return "Modelo";
            }
        }

        static string KindIcon(string kind)
        {
            if (kind == "model") return "import";
            return AtlasTheme.Icon(kind) != null ? kind : "cube";
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
                case "cone":
                case "pyramid":
                case "torus":
                    go = new GameObject();
                    var mesh = Primitives.Get(d.kind);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>();
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    break;
                case "light":
                case "spot":
                case "sun":
                    go = MakeLamp(d.kind);
                    break;
                default: go = new GameObject(); break;
            }
            go.name = string.IsNullOrEmpty(d.name) ? DisplayName(d.kind) : d.name;
            var it = go.AddComponent<AtlasItem>();
            it.id = string.IsNullOrEmpty(d.id) ? SceneStore.NewId() : d.id;
            it.kind = d.kind;
            it.modelFile = d.modelFile;
            it.keys = d.keys != null ? new List<KeyData>(d.keys) : new List<KeyData>();
            it.color = d.color;
            it.metallic = d.metallic;
            it.smoothness = d.smoothness;
            it.emission = d.emission;
            it.opacity = d.opacity;
            it.texture = d.texture ?? "";
            it.lightIntensity = d.lightIntensity;
            it.lightRange = d.lightRange;
            it.spotAngle = d.spotAngle;
            it.shadows = d.shadows;
            it.visible = d.visible;
            it.locked = d.locked;
            it.clip = d.clip ?? "";
            go.transform.position = d.position;
            go.transform.eulerAngles = d.rotation;
            go.transform.localScale = d.scale;
            items.Add(it);
            if (d.kind == "model") LoadModel(it);
            else ApplyLook(it);
            return it;
        }

        /// Lamp: a small glowing ball with the light inside; a spotlight
        /// also shows its cone and the sun a ray, so the direction is seen.
        GameObject MakeLamp(string kind)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var l = new GameObject("Bombilla").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.type = kind == "spot" ? LightType.Spot : kind == "sun" ? LightType.Directional : LightType.Point;
            l.shadows = LightShadows.Soft;
            if (kind == "spot")
            {
                var beam = new GameObject("Haz");
                beam.transform.SetParent(go.transform, false);
                beam.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                beam.transform.localScale = Vector3.one * 1.6f;
                beam.transform.localPosition = new Vector3(0f, 0f, 0.8f);
                beam.AddComponent<MeshFilter>().sharedMesh = Primitives.Get("cone");
                var r = beam.AddComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            else if (kind == "sun")
            {
                var ray = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ray.name = "Rayo";
                Destroy(ray.GetComponent<Collider>());
                ray.transform.SetParent(go.transform, false);
                ray.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ray.transform.localScale = new Vector3(0.25f, 1f, 0.25f);
                ray.transform.localPosition = new Vector3(0f, 0f, 1.4f);
                ray.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go;
        }

        async void LoadModel(AtlasItem it)
        {
            if (store == null) return;
            string path = store.ModelPath(it.modelFile);
            string err = await ModelLoader.Load(path, it, litTemplate, true);
            if (it == null) return;
            if (err != "")
                Toast(err, true);
            else
            {
                ApplyLook(it);
                if (!string.IsNullOrEmpty(it.clip)) it.SampleClip(frame / fps);
                if (selected == it) { ShowGizmo(); RefreshProps(); }
            }
        }

        /// Puts the object's settings on screen: material, light, visibility.
        void ApplyLook(AtlasItem it)
        {
            if (it == null) return;
            foreach (var r in it.GetComponentsInChildren<Renderer>(true)) r.enabled = it.visible;
            foreach (var c in it.GetComponentsInChildren<Collider>(true)) c.enabled = it.visible;
            if (it.kind == "model") return;
            if (it.material == null) it.material = new Material(litTemplate);
            var m = it.material;
            Color col = it.color;
            if (it.IsLight)
            {
                col.a = 1f;
                m.SetColor("_BaseColor", col);
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", 0.5f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", col * 2.5f);
                m.SetTexture("_BaseMap", null);
                MatUtil.SetTransparent(m, false);
                var l = it.GetComponentInChildren<Light>(true);
                if (l != null)
                {
                    l.color = col;
                    l.intensity = it.lightIntensity;
                    l.range = Mathf.Max(0.1f, it.lightRange);
                    l.spotAngle = Mathf.Clamp(it.spotAngle, 1f, 179f);
                    l.innerSpotAngle = l.spotAngle * 0.6f;
                    l.shadows = it.shadows ? LightShadows.Soft : LightShadows.None;
                    l.enabled = it.visible;
                }
            }
            else
            {
                col.a = Mathf.Clamp01(it.opacity);
                m.SetColor("_BaseColor", col);
                m.SetFloat("_Metallic", it.metallic);
                m.SetFloat("_Smoothness", it.smoothness);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(it.color.r, it.color.g, it.color.b) * it.emission);
                m.SetTexture("_BaseMap", LoadTexture(it.texture));
                MatUtil.SetTransparent(m, it.opacity < 0.995f);
            }
            foreach (var r in it.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = m;
        }

        Texture2D LoadTexture(string file)
        {
            if (string.IsNullOrEmpty(file) || store == null) return null;
            Texture2D t;
            if (textures.TryGetValue(file, out t) && t != null) return t;
            string path = store.TexturePath(file);
            if (!File.Exists(path)) return null;
            t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!t.LoadImage(File.ReadAllBytes(path)))
            {
                Destroy(t);
                return null;
            }
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            textures[file] = t;
            return t;
        }

        void AddShape(string kind)
        {
            count++;
            float a = count * 1.3f;
            float rad = items.Count > 0 ? 1.8f : 0f;
            var d = new ItemData
            {
                v = 3,
                kind = kind,
                name = DisplayName(kind) + " " + count,
                position = new Vector3(Mathf.Cos(a) * rad, 0.5f, Mathf.Sin(a) * rad),
                color = Color.HSVToRGB((count * 0.17f) % 1f, 0.45f, 0.9f),
            };
            switch (kind)
            {
                case "plane": d.position.y = 0.01f; break;
                case "cylinder":
                case "capsule": d.position.y = 1f; break;
                case "torus": d.position.y = 0.17f; break;
                case "light":
                    d.position.y = 2.5f;
                    d.scale = Vector3.one * 0.25f;
                    d.color = new Color(1f, 0.92f, 0.75f);
                    d.lightIntensity = 3f;
                    d.lightRange = 10f;
                    break;
                case "spot":
                    d.position = new Vector3(Mathf.Cos(a) * 2.5f, 3.2f, Mathf.Sin(a) * 2.5f);
                    d.rotation = Quaternion.LookRotation(new Vector3(0f, 0.5f, 0f) - d.position).eulerAngles;
                    d.scale = Vector3.one * 0.25f;
                    d.color = new Color(1f, 0.95f, 0.85f);
                    d.lightIntensity = 10f;
                    d.lightRange = 14f;
                    d.spotAngle = 45f;
                    break;
                case "sun":
                    d.position = new Vector3(0f, 4.5f, 0f);
                    d.rotation = new Vector3(50f, 150f, 0f);
                    d.scale = Vector3.one * 0.35f;
                    d.color = Mathf.CorrelatedColorTemperatureToRGB(5600f);
                    d.lightIntensity = 1f;
                    break;
            }
            Select(Create(d));
            CloseMenus();
            Commit(true);
        }

        void ImportModel()
        {
            CloseMenus();
            if (store == null || NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path) || store == null) return;
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
                var d = new ItemData { v = 3, kind = "model", name = string.IsNullOrEmpty(nm) ? "Modelo" : nm, position = Vector3.zero, modelFile = file };
                Select(Create(d));
                Toast("Importando «" + d.name + "»…");
                Commit(true);
            }, new string[] { "*/*" });
        }

        void PickTexture(AtlasItem it)
        {
            if (store == null || it == null || NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path) || store == null || it == null) return;
                try
                {
                    it.texture = store.AddTextureFile(path);
                }
                catch (System.Exception e)
                {
                    Toast("No se pudo usar la imagen: " + e.Message, true);
                    return;
                }
                if (LoadTexture(it.texture) == null)
                {
                    it.texture = "";
                    Toast("Esa imagen no se puede leer (usa PNG o JPG)", true);
                    return;
                }
                if (it.color.maxColorComponent < 0.95f) it.color = Color.white;
                ApplyLook(it);
                Commit(true);
                RefreshProps();
                Toast("Textura puesta");
            }, new string[] { "image/*" });
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
            gizmo.Show(selected != null && !selected.locked && tool != Tool.Select && !playing ? selected.transform : null);
        }

        void DeleteSelected()
        {
            if (selected == null) { Toast("Selecciona algo para borrarlo"); return; }
            items.Remove(selected);
            Destroy(selected.gameObject);
            Select(null);
            Commit(true);
            Toast("Borrado (Deshacer lo recupera)");
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

        void ClearScene()
        {
            if (playing) TogglePlay();
            foreach (var it in items) if (it != null) Destroy(it.gameObject);
            items.Clear();
            selected = null;
            gizmo.Show(null);
            foreach (var t in textures.Values) if (t != null) Destroy(t);
            textures.Clear();
            count = 0;
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
            d.env = JsonUtility.FromJson<EnvData>(JsonUtility.ToJson(env));
            return d;
        }

        /// After every finished change: one undo step (and autosave).
        void Commit(bool save)
        {
            if (store == null) return;
            store.Push(Snapshot());
            RefreshUndo();
            RefreshKeys();
            if (save) SaveNow(false);
        }

        void SaveNow(bool tell)
        {
            if (store == null) return;
            try
            {
                store.Save(Snapshot());
                if (tell)
                {
                    SaveThumbnail();
                    Toast("Proyecto guardado");
                }
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
            AtlasTheme.SetButtonIcon(eyeBtn, ground.activeSelf ? "eye" : "eye_off");
            env = d.env != null ? JsonUtility.FromJson<EnvData>(JsonUtility.ToJson(d.env)) : new EnvData();
            ApplyEnv();
            UpdateCamera();
            SetFrame(Mathf.Min(frame, endFrame));
            Select(sel);
            RefreshTimeline();
        }

        void Undo()
        {
            if (store == null) return;
            var d = store.Undo();
            if (d == null) return;
            Restore(d);
            RefreshUndo();
            SaveNow(false);
        }

        void Redo()
        {
            if (store == null) return;
            var d = store.Redo();
            if (d == null) return;
            Restore(d);
            RefreshUndo();
            SaveNow(false);
        }

        void RefreshUndo()
        {
            if (undoBtn != null) undoBtn.interactable = store != null && store.CanUndo;
            if (redoBtn != null) redoBtn.interactable = store != null && store.CanRedo;
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
            {
                if (it == null) continue;
                if (it.Animated) it.Evaluate(frame);
                if (!string.IsNullOrEmpty(it.clip)) it.SampleClip(frame / fps);
            }
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
            RefreshProps();
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
            RefreshProps();
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
            if (Input.GetKeyDown(KeyCode.Escape)) Back();
            if (store == null || home.activeSelf || rendering) return;
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
            if (inspectorDirty && propsPanel != null && propsPanel.activeSelf)
            {
                inspectorDirty = false;
                BuildInspector();
            }
            if (selected != null && propsPanel != null && propsPanel.activeSelf && tf[0] != null) FillTransformFields();
        }

        /// Android «back»: closes a dialog or a menu, or goes to the projects.
        void Back()
        {
            var dlg = canvasRoot != null ? canvasRoot.Find("Dialogo") : null;
            if (dlg != null) { Destroy(dlg.gameObject); return; }
            if (home.activeSelf) return;
            if ((addMenu != null && addMenu.activeSelf) || (mainMenu != null && mainMenu.activeSelf) || (renderPanel != null && renderPanel.activeSelf))
            {
                CloseMenus();
                return;
            }
            ShowHome(true);
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
            AtlasItem it = null;
            // The nearest object that is not locked.
            var hits = Physics.RaycastAll(cam.ScreenPointToRay(pos), 2000f);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                var hi = h.collider.GetComponentInParent<AtlasItem>();
                if (hi == null || hi.locked) continue;
                if (h.distance < best) { best = h.distance; it = hi; }
            }
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
                string path = await Exporter.Export(format, items, store != null ? store.name : "atlas3d");
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
    }
}
