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
        public const string Version = "3.7.0";

        Camera cam;
        Light sunLight;
        Material litTemplate, unlitTemplate, aeroTemplate, normalTemplate;
        ReflectionProbe probe;
        bool reflDirty = true;
        float nextRefl;
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
        float lastViewW;

        void Awake()
        {
            Application.targetFrameRate = 60;
            // Always landscape (either side), from the first frame.
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            if (Screen.orientation == ScreenOrientation.Portrait || Screen.orientation == ScreenOrientation.PortraitUpsideDown)
                Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.orientation = ScreenOrientation.AutoRotation;
            QualitySettings.vSyncCount = 0;
            Input.simulateMouseWithTouches = false;   // a touch is not also a mouse click
            litTemplate = Resources.Load<Material>("AtlasLit");
            aeroTemplate = Resources.Load<Material>("AtlasAero");
            normalTemplate = Resources.Load<Material>("AtlasLitNormal");
            if (aeroTemplate != null && (aeroTemplate.shader == null || !aeroTemplate.shader.isSupported)) aeroTemplate = null;
            unlitTemplate = Resources.Load<Material>("AtlasUnlit");
            BuildWorld();
            highlighter = new GameObject("Resaltado").AddComponent<Highlighter>();
            highlighter.Init(unlitTemplate);
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

            // Reflections of the sky and the scene on glossy materials.
            var pg = new GameObject("Reflejos");
            probe = pg.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 128;
            probe.size = new Vector3(2000f, 2000f, 2000f);
            probe.boxProjection = false;
            probe.clearFlags = UnityEngine.Rendering.ReflectionProbeClearFlags.Skybox;
            probe.hdr = true;
            probe.importance = 1;
            probe.cullingMask = ~(1 << Gizmo.Layer);

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
            reflDirty = true;
        }

        /// The reflections are captured again after changes (at most twice
        /// a second, never while playing).
        void UpdateReflections()
        {
            if (probe == null || !reflDirty || playing || Time.unscaledTime < nextRefl || store == null) return;
            reflDirty = false;
            nextRefl = Time.unscaledTime + 0.5f;
            probe.transform.position = target.position + Vector3.up * 0.5f;
            highlighter.SetVisible(false);
            probe.RenderProbe();
            highlighter.SetVisible(true);
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
                case "camera": return "Cámara";
                case "image": return "Imagen";
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
                case "camera":
                    go = MakeCameraProp();
                    break;
                case "image":
                    go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    break;
                default: go = new GameObject(); break;
            }
            var it = go.AddComponent<AtlasItem>();
            it.id = string.IsNullOrEmpty(d.id) ? SceneStore.NewId() : d.id;
            it.kind = d.kind;
            it.modelFile = d.modelFile;
            ApplyData(it, d);
            items.Add(it);
            if (!string.IsNullOrEmpty(it.meshFile)) LoadItemMesh(it);
            if (d.kind == "model") LoadModel(it);
            else ApplyLook(it);
            return it;
        }

        /// Puts saved settings on an object that already exists (undo and
        /// redo reuse the objects: a model is not loaded again).
        void ApplyData(AtlasItem it, ItemData d)
        {
            it.gameObject.name = string.IsNullOrEmpty(d.name) ? DisplayName(d.kind) : d.name;
            it.keys = new List<KeyData>();
            if (d.keys != null)
                foreach (var k in d.keys)
                {
                    var kc = JsonUtility.FromJson<KeyData>(JsonUtility.ToJson(k));
                    if (kc.bones == null) kc.bones = new List<BonePose>();
                    it.keys.Add(kc);
                }
            it.pose = new List<BonePose>();
            if (d.pose != null) foreach (var b in d.pose) it.pose.Add(b.Copy());
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
            it.fov = d.fov > 1f ? d.fov : 50f;
            it.shader = d.shader ?? "";
            it.modelGloss = d.modelGloss;
            it.noShadow = d.noShadow;
            it.matOverrides = new List<MatOverride>();
            if (d.mats != null) foreach (var mo in d.mats) it.matOverrides.Add(mo.Copy());
            it.normalMap = d.normalMap ?? "";
            it.emissionMap = d.emissionMap ?? "";
            it.bumpStrength = d.bumpStrength > 0f ? d.bumpStrength : 1f;
            it.lookAtId = d.lookAtId ?? "";
            it.followId = d.followId ?? "";
            it.followOffset = d.followOffset;
            it.shake = d.shake;
            it.shakeSpeed = d.shakeSpeed > 0f ? d.shakeSpeed : 0.6f;
            string mf = d.meshFile ?? "";
            if (mf != (it.meshFile ?? ""))
            {
                it.meshFile = mf;
                if (it.GetComponent<MeshFilter>() != null && it.IsShape && (it.edit != null || mf != "")) LoadItemMesh(it);
                selVerts.Clear();
                selFaces.Clear();
            }
            it.renderCam = d.renderCam;
            it.transform.position = d.position;
            it.transform.eulerAngles = d.rotation;
            it.transform.localScale = d.scale;
            if (it.loaded && it.HasBones)
            {
                it.ResetPose();
                foreach (var b in d.pose) it.pose.Add(b.Copy());
                it.Evaluate(frame, fps);
                if (it.keys.Count > 0)
                {
                    // Keys must not move the object away from what was restored.
                    it.transform.position = d.position;
                    it.transform.eulerAngles = d.rotation;
                    it.transform.localScale = d.scale;
                }
            }
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
                // Light cone drawn as thin lines: its real angle and reach.
                var beam = new GameObject("Haz");
                beam.transform.SetParent(go.transform, false);
                var lr = beam.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.widthMultiplier = 0.015f;
                lr.numCapVertices = 0;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sharedMaterial = new Material(unlitTemplate);
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
                it.loaded = true;
                it.modelMats = CollectMaterials(it);
                it.BuildBones();
                it.Evaluate(frame, fps);
                ApplyLook(it);
                if (selected == it)
                {
                    if (!poseMode) highlighter.Show(it);
                    ShowGizmo();
                    RefreshMode();
                    RefreshProps();
                }
                if (it.HasBones && selected == it)
                    Toast("Modelo con " + it.BoneOrder.Count + " huesos: pulsa el botón del hueso para posarlo");
            }
        }

        /// Puts the object's settings on screen: material, light, visibility.
        void ApplyLook(AtlasItem it)
        {
            if (it == null) return;
            foreach (var r in it.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = it.visible;
                if (!it.IsLight && it.kind != "camera")
                    r.shadowCastingMode = it.noShadow || env.shadowsOff ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            foreach (var c in it.GetComponentsInChildren<Collider>(true)) c.enabled = it.visible;
            if (it.kind == "model") { if (it.loaded) ApplyModelMaterials(it); return; }
            // The material's kind follows the item (lit / Frutiger Aero glass).
            bool wantAero = it.shader == "aero" && aeroTemplate != null && it.IsShape;
            bool isAero = it.material != null && aeroTemplate != null && it.material.shader == aeroTemplate.shader;
            if (it.material != null && isAero != wantAero)
            {
                Destroy(it.material);
                it.material = null;
            }
            bool wantNormal = !wantAero && !string.IsNullOrEmpty(it.normalMap) && normalTemplate != null && it.IsShape;
            if (it.material != null && !wantAero && it.IsShape && it.material.IsKeywordEnabled("_NORMALMAP") != wantNormal)
            {
                Destroy(it.material);
                it.material = null;
            }
            if (it.material == null) it.material = new Material(wantAero ? aeroTemplate : (wantNormal ? normalTemplate : litTemplate));
            if (wantAero)
            {
                var am = it.material;
                am.SetColor("_BaseColor", new Color(it.color.r, it.color.g, it.color.b, Mathf.Clamp01(it.opacity)));
                am.SetFloat("_Glow", Mathf.Clamp(0.6f + it.emission * 0.25f, 0f, 2f));
                am.SetFloat("_Smoothness", it.smoothness);
                foreach (var r in it.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = am;
                return;
            }
            var m = it.material;
            Color col = it.color;
            if (it.kind == "camera")
            {
                m.SetColor("_BaseColor", new Color(0.16f, 0.17f, 0.2f));
                m.SetFloat("_Metallic", 0.3f);
                m.SetFloat("_Smoothness", 0.6f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", it.renderCam ? new Color(0.35f, 0.12f, 0.6f) : Color.black);
                m.SetTexture("_BaseMap", null);
                MatUtil.SetTransparent(m, false);
                foreach (var r in it.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = m;
                return;
            }
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
                    l.shadows = it.shadows && !env.shadowsOff ? LightShadows.Soft : LightShadows.None;
                    l.enabled = it.visible;
                }
                var cone = it.GetComponentInChildren<LineRenderer>(true);
                if (cone != null) SpotCone(it, cone, col);
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
                var nmap = LoadTexture(it.normalMap, true);
                if (m.HasProperty("_BumpMap"))
                {
                    m.SetTexture("_BumpMap", nmap);
                    m.SetFloat("_BumpScale", it.bumpStrength);
                }
                var emap = LoadTexture(it.emissionMap);
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emap);
                if (emap != null)
                    m.SetColor("_EmissionColor", new Color(it.color.r, it.color.g, it.color.b) * Mathf.Max(1f, it.emission));
                MatUtil.SetTransparent(m, it.opacity < 0.995f);
                // Pictures are seen from both sides.
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", it.kind == "image" ? 0f : 2f);
            }
            foreach (var r in it.GetComponentsInChildren<Renderer>(true))
                if (!(r is LineRenderer)) r.sharedMaterial = m;
        }

        /// Spotlight lines: a ring where the light reaches (up to 4 m) and
        /// four rays from the lamp to it.
        static void SpotCone(AtlasItem it, LineRenderer lr, Color col)
        {
            float s = Mathf.Max(0.0001f, it.transform.lossyScale.x);
            float len = Mathf.Min(it.lightRange, 4f) / s;
            float rad = len * Mathf.Tan(Mathf.Clamp(it.spotAngle, 1f, 170f) * 0.5f * Mathf.Deg2Rad);
            const int seg = 32;
            var pts = new List<Vector3>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                pts.Add(new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, len));
            }
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f;
                pts.Add(Vector3.zero);
                pts.Add(new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, len));
                // along the ring to the next ray
                float a2 = (k + 1) * Mathf.PI * 0.5f;
                for (int j = 1; j <= seg / 4; j++)
                {
                    float b = a + (a2 - a) * j / (seg / 4f);
                    pts.Add(new Vector3(Mathf.Cos(b) * rad, Mathf.Sin(b) * rad, len));
                }
            }
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            lr.widthMultiplier = 0.015f;
            col.a = 0.9f;
            lr.sharedMaterial.SetColor("_BaseColor", col);
            lr.enabled = it.visible;
        }

        /// «Brillo» of an imported model: its own materials, or all of them
        /// from matte to glossy (and not metal).
        void ApplyModelGloss(AtlasItem it)
        {
            foreach (var r in it.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.HasProperty("_Smoothness")) continue;
                    if (!it.originalGloss.ContainsKey(m))
                        it.originalGloss[m] = new Vector2(m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f, m.GetFloat("_Smoothness"));
                    var o = it.originalGloss[m];
                    if (it.modelGloss < 0f)
                    {
                        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", o.x);
                        m.SetFloat("_Smoothness", o.y);
                    }
                    else
                    {
                        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                        m.SetFloat("_Smoothness", it.modelGloss * 0.85f);
                    }
                }
        }

        /// `linear`: data maps (normal maps) are not colours.
        Texture2D LoadTexture(string file, bool linear = false)
        {
            if (string.IsNullOrEmpty(file) || store == null) return null;
            Texture2D t;
            string key = linear ? file + "|lineal" : file;
            if (textures.TryGetValue(key, out t) && t != null) return t;
            string path = store.TexturePath(file);
            if (!File.Exists(path)) return null;
            t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            if (!t.LoadImage(File.ReadAllBytes(path)))
            {
                Destroy(t);
                return null;
            }
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 4;
            textures[key] = t;
            return t;
        }

        void AddShape(string kind)
        {
            count++;
            float a = count * 1.3f;
            // New things appear in the middle of the view, on the ground.
            Vector3 c = new Vector3(target.position.x, 0f, target.position.z);
            var d = new ItemData
            {
                v = 3,
                kind = kind,
                name = DisplayName(kind) + " " + count,
                position = c + new Vector3(0f, 0.5f, 0f),
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
                    d.position = c + new Vector3(Mathf.Cos(a) * 2.5f, 3.2f, Mathf.Sin(a) * 2.5f);
                    d.rotation = Quaternion.LookRotation(c + new Vector3(0f, 0.5f, 0f) - d.position).eulerAngles;
                    d.scale = Vector3.one * 0.25f;
                    d.color = new Color(1f, 0.95f, 0.85f);
                    d.lightIntensity = 10f;
                    d.lightRange = 14f;
                    d.spotAngle = 45f;
                    break;
                case "sun":
                    d.position = c + new Vector3(0f, 4.5f, 0f);
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
                ImportFile(path);
            }, new string[] { "*/*" });
        }

        /// Copies (and converts, for FBX / OBJ / 3DS) the model in the
        /// background, then puts it in the scene.
        async void ImportFile(string path)
        {
            var st = store;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            Toast(System.Array.IndexOf(Import.ModelConverter.Convertible, ext) >= 0 ? "Convirtiendo «" + Path.GetFileName(path) + "»…" : "Importando «" + Path.GetFileName(path) + "»…");
            string file;
            try
            {
                file = await System.Threading.Tasks.Task.Run(() => st.AddModelFile(path));
            }
            catch (System.Exception e)
            {
                Toast("No se pudo importar: " + e.Message, true);
                return;
            }
            if (store != st) return;   // the project was closed meanwhile
            {
                count++;
                string nm = Path.GetFileNameWithoutExtension(path);
                var d = new ItemData { v = 3, kind = "model", name = string.IsNullOrEmpty(nm) ? "Modelo" : nm, position = new Vector3(target.position.x, 0f, target.position.z), modelFile = file };
                Select(Create(d));
                Commit(true);
            }
        }

        /// Image for a map of the material: "normal" (normal map), "bump"
        /// (black and white relief, turned into a normal map) or "emission".
        void PickMap(AtlasItem it, string slot)
        {
            if (store == null || it == null || NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path) || store == null || it == null) return;
                string file;
                try { file = store.AddTextureFile(path); }
                catch (System.Exception e) { Toast("No se pudo usar la imagen: " + e.Message, true); return; }
                if (slot == "bump")
                {
                    string made = BumpToNormal(file);
                    if (made == null) { Toast("Esa imagen no se puede leer (usa PNG o JPG)", true); return; }
                    it.normalMap = made;
                }
                else if (slot == "normal")
                {
                    if (LoadTexture(file, true) == null) { Toast("Esa imagen no se puede leer (usa PNG o JPG)", true); return; }
                    it.normalMap = file;
                }
                else
                {
                    if (LoadTexture(file) == null) { Toast("Esa imagen no se puede leer (usa PNG o JPG)", true); return; }
                    it.emissionMap = file;
                    if (it.emission < 1f) it.emission = 1f;
                }
                ApplyLook(it);
                Commit(true);
                RefreshProps();
                Toast(slot == "emission" ? "Mapa de emisión puesto" : "Relieve puesto");
            }, new string[] { "image/*" });
        }

        /// Height (black low, white high) -> tangent-space normal map PNG.
        string BumpToNormal(string file)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!src.LoadImage(File.ReadAllBytes(store.TexturePath(file)))) return null;
                int w = src.width, h = src.height;
                var px = src.GetPixels32();
                var hgt = new float[w * h];
                for (int i = 0; i < px.Length; i++) hgt[i] = (px[i].r * 0.299f + px[i].g * 0.587f + px[i].b * 0.114f) / 255f;
                var outp = new Color32[w * h];
                const float strength = 3f;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float l = hgt[y * w + (x + w - 1) % w], r = hgt[y * w + (x + 1) % w];
                        float d = hgt[((y + h - 1) % h) * w + x], u = hgt[((y + 1) % h) * w + x];
                        Vector3 n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                        outp[y * w + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255f), (byte)((n.y * 0.5f + 0.5f) * 255f), (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
                    }
                var dst = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                dst.SetPixels32(outp);
                dst.Apply();
                string name = Path.GetFileNameWithoutExtension(file) + "_normal.png";
                File.WriteAllBytes(store.TexturePath(name), dst.EncodeToPNG());
                Destroy(dst);
                return name;
            }
            catch (System.Exception)
            {
                return null;
            }
            finally
            {
                Destroy(src);
            }
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
            if (it != selected) activeMat = -1;
            if (meshMode != "" && it != selected)
            {
                if (sculpting) EndSculpt();
                meshMode = "";
                selVerts.Clear();
                selFaces.Clear();
                BuildMeshBar();
            }
            if (poseMode && it != selected)
            {
                poseMode = false;
                activeBone = "";
                HideBoneOverlay();
            }
            selected = it;
            if (poseMode || lookThrough != null) highlighter.Clear();
            else highlighter.Show(it);
            ShowGizmo();
            RefreshMode();
            RefreshProps();
            RefreshKeys();
        }

        void ShowGizmo()
        {
            if (lookThrough != null) { gizmo.Show(null); return; }
            if (meshMode != "")
            {
                gizmo.Show(meshMode == "model" && tool != Tool.Select && !playing && PlaceMeshPivot() ? meshPivot : null);
                return;
            }
            if (poseMode)
            {
                var bone = selected != null && activeBone != "" ? selected.Bone(activeBone) : null;
                gizmo.Show(bone != null && tool != Tool.Select && !playing ? bone : null);
                return;
            }
            gizmo.Show(selected != null && !selected.locked && tool != Tool.Select && !playing ? selected.transform : null);
        }

        void DeleteSelected()
        {
            if (selected == null) { Toast("Selecciona algo para borrarlo"); return; }
            ExitPose();
            ExitMeshMode();
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
            data.renderCam = false;
            Select(Create(data));
            Commit(true);
        }

        void ClearScene()
        {
            if (playing) TogglePlay();
            foreach (var it in items) if (it != null) Destroy(it.gameObject);
            items.Clear();
            selected = null;
            meshMode = "";
            sculpting = false;
            selVerts.Clear();
            selFaces.Clear();
            if (meshBar != null) meshBar.SetActive(false);
            poseMode = false;
            activeBone = "";
            HideBoneOverlay();
            highlighter.Clear();
            gizmo.Show(null);
            if (audioSrc != null) { audioSrc.Stop(); audioSrc.clip = null; }
            audioFile = "";
            loadedAudio = "";
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
            d.startFrame = startFrame;
            d.audioFile = audioFile;
            d.audioStart = audioStart;
            d.audioVolume = audioVolume;
            d.showGrid = ground.activeSelf;
            d.env = JsonUtility.FromJson<EnvData>(JsonUtility.ToJson(env));
            return d;
        }

        /// After every finished change: one undo step (and autosave).
        void Commit(bool save)
        {
            if (store == null) return;
            store.Push(Snapshot());
            reflDirty = true;
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

        /// `view`: also put the camera where it was saved (opening a
        /// project). Undo / redo keep the view where the user is looking.
        void Restore(SceneData d, bool view = false)
        {
            string keep = selected != null ? selected.id : null;
            var old = new Dictionary<string, AtlasItem>();
            foreach (var it in items)
                if (it != null && !old.ContainsKey(it.id)) old[it.id] = it;
            items.Clear();
            AtlasItem sel = null;
            foreach (var data in d.items)
            {
                AtlasItem it;
                if (old.TryGetValue(data.id, out it) && it.kind == data.kind && it.modelFile == data.modelFile)
                {
                    old.Remove(data.id);
                    items.Add(it);
                    ApplyData(it, data);
                    ApplyLook(it);
                }
                else it = Create(data);
                if (data.id == keep) sel = it;
            }
            foreach (var it in old.Values) if (it != null) Destroy(it.gameObject);
            bool keepPose = poseMode && sel != null && sel == selected;
            string keepBone = activeBone;
            count = items.Count;
            if (view)
            {
                target.position = d.cameraTarget;
                yaw = d.cameraYaw;
                pitch = d.cameraPitch;
                distance = d.cameraDistance;
            }
            fps = Mathf.Max(1, d.fps);
            endFrame = Mathf.Max(1, d.endFrame);
            startFrame = Mathf.Clamp(d.startFrame, 0, endFrame - 1);
            RestoreAudio(d);
            ground.SetActive(d.showGrid);
            AtlasTheme.SetButtonIcon(eyeBtn, ground.activeSelf ? "eye" : "eye_off");
            env = d.env != null ? JsonUtility.FromJson<EnvData>(JsonUtility.ToJson(d.env)) : new EnvData();
            ApplyEnv();
            frame = Mathf.Min(frame, endFrame);
            UpdateCamera();
            Select(sel);
            if (keepPose && sel.HasBones)
            {
                poseMode = true;
                activeBone = keepBone;
                highlighter.Clear();
                ShowGizmo();
                RefreshMode();
            }
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
            if (selected == null || !(autoKey || selected.Animated)) return;
            int f = Mathf.RoundToInt(frame);
            if (poseMode && activeBone != "") selected.SetKey(f, 0, new List<string> { activeBone });
            else selected.SetKey(f, AtlasItem.ChPos | AtlasItem.ChRot | AtlasItem.ChScale, new List<string>());
            RefreshKeys();
        }

        // ------------------------------------------------------------------
        // Animation

        void SetFrame(float f)
        {
            frame = Mathf.Clamp(f, 0f, endFrame);
            foreach (var it in items)
            {
                if (it == null) continue;
                it.Evaluate(frame, fps);
            }
            ApplyCameraRigs();
            RefreshTimeline();
        }

        void TogglePlay()
        {
            playing = !playing;
            if (playing && frame >= endFrame) frame = startFrame;
            AtlasTheme.SetButtonIcon(playBtn, playing ? "pause" : "play");
            ShowGizmo();
            RefreshTimeline();
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
                float f = frame + Time.deltaTime * fps * speed;
                if (f > endFrame)
                {
                    if (loopPlay) f = startFrame;
                    else { f = endFrame; TogglePlay(); }
                }
                SetFrame(f);
            }
            SyncAudio();
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
                if (sculpting) EndSculpt();
                if (draggingGizmo && meshDragging) { gizmo.End(); draggingGizmo = false; MeshDragEnd(); }
                if (draggingGizmo) { gizmo.End(); draggingGizmo = false; BoneEdited(); AutoKey(); Commit(true); }
                moved = 99f;   // not a tap any more
                Touch a = Input.GetTouch(0), b = Input.GetTouch(1);
                Vector2 pa = a.position - a.deltaPosition, pb = b.position - b.deltaPosition;
                float before = (pa - pb).magnitude, now = (a.position - b.position).magnitude;
                if (lookThrough != null)
                {
                    // Inside the camera: two fingers move it, pinching pushes it forward.
                    LookThroughTwoFingers((a.deltaPosition + b.deltaPosition) * 0.5f, now - before);
                    if (a.phase == TouchPhase.Ended || b.phase == TouchPhase.Ended || a.phase == TouchPhase.Canceled || b.phase == TouchPhase.Canceled) LookThroughEnd();
                    return;
                }
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
                if (Mathf.Abs(wheel) > 0.01f && !OverUi(-1) && lookThrough != null)
                {
                    LookThroughTwoFingers(Vector2.zero, wheel * 40f);
                    LookThroughEnd();
                }
                else if (Mathf.Abs(wheel) > 0.01f && !OverUi(-1))
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
            if (poseMode && propsPanel != null && propsPanel.activeSelf) FillBoneFields();
            UpdateBoneOverlay();
            UpdateMeshOverlay();
            UpdateLookThrough();
            UpdateReflections();
            if (viewArea != null && Mathf.Abs(viewArea.rect.width - lastViewW) > 1f)
            {
                lastViewW = viewArea.rect.width;
                LayoutModePill();
            }
            if (canvasRoot != null) gizmo.pxScale = canvasRoot.GetComponent<Canvas>().scaleFactor;
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
            // Sculpting: a finger on the shape is the brush.
            if (BeginSculpt(pos)) { draggingGizmo = false; orbiting = false; return; }
            draggingGizmo = gizmo.TryBegin(pos);
            if (draggingGizmo && meshMode == "model") MeshDragBegin();
            orbiting = !draggingGizmo;
        }

        void MovePointer(Vector2 pos, Vector2 delta)
        {
            moved += delta.magnitude;
            if (sculpting) { SculptMove(pos); return; }
            if (lookThrough != null && !draggingGizmo)
            {
                if (moved > 6f) LookThroughDrag(delta);
                return;
            }
            if (draggingGizmo)
            {
                gizmo.Drag(pos);
                if (meshDragging) MeshDragMove();
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
            if (sculpting) { EndSculpt(); moved = -1f; return; }
            if (lookThrough != null && cameraDragging) { LookThroughEnd(); moved = -1f; orbiting = false; return; }
            if (draggingGizmo && meshDragging)
            {
                gizmo.End();
                draggingGizmo = false;
                MeshDragEnd();
                moved = -1f;
                return;
            }
            if (draggingGizmo)
            {
                gizmo.End();
                draggingGizmo = false;
                BoneEdited();
                AutoKey();
                Commit(true);
                moved = -1f;
                return;
            }
            // A fingertip wobbles: the tap tolerance follows the screen density.
            float tol = Mathf.Max(14f, (Screen.dpi > 0f ? Screen.dpi : 320f) * 0.09f);
            bool tap = (pos - downPos).magnitude < tol && moved < tol * 2.2f;
            orbiting = false;
            moved = -1f;
            if (!tap) return;
            CloseMenus();
            if (lookThrough != null) return;
            if (MeshTap(pos)) return;
            if (meshMode == "sculpt") return;   // taps do not change the selection while sculpting
            if (poseMode)
            {
                string bone = PickBone(pos);
                if (bone != "") { SetActiveBone(bone); return; }
            }
            AtlasItem it = PickItem(pos);
            if (!poseMode && meshMode == "" && it != null && it == selected && it.kind == "model" && it.loaded)
            {
                int mi = PickModelMaterial(it, pos);
                if (mi >= 0)
                {
                    SetActiveMaterial(mi == activeMat ? -1 : mi);
                    if (activeMat >= 0) Toast("Material «" + it.modelMats[mi].name.Replace(" (Instance)", "") + "»: cámbialo en Propiedades");
                    return;
                }
            }
            if (poseMode)
            {
                if (it == null || it == selected) { if (activeBone != "") SetActiveBone(""); return; }
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
                var opt = new GltfSceneWriter.Options { setFrame = SetFrame, endFrame = endFrame, fps = fps, currentFrame = frame };
                bool anyAnim = false;
                foreach (var it in items)
                    if (it != null && (it.Animated || !string.IsNullOrEmpty(it.clip))) anyAnim = true;
                if (!anyAnim) opt.setFrame = null;
                if (playing) TogglePlay();
                string path = await Exporter.Export(format, items, store != null ? store.name : "atlas3d", opt);
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
