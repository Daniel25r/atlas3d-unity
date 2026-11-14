// ATLAS 3D (Unity) - phase 0: the cloud build works end to end.
// A 3D view with URP lighting, a ground grid, touch camera (one finger
// orbits, two fingers zoom and pan), ATLAS's purple top bar with buttons to
// add shapes, tap to select, drag to move the selection on the ground.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Atlas
{
    public class AtlasApp : MonoBehaviour
    {
        // ATLAS theme (same colours as the Godot version).
        public static readonly Color Bg = Hex("0e0f13");
        public static readonly Color Panel = Hex("16181d");
        public static readonly Color Panel2 = Hex("1d2027");
        public static readonly Color Accent = Hex("b25cff");
        public static readonly Color AccentDim = Hex("6b2fc0");
        public static readonly Color TextCol = Hex("e8eaf0");
        public static readonly Color TextDim = Hex("959ba7");

        Camera cam;
        Material litTemplate;
        Font font;
        Text status;
        Transform target;               // orbit centre
        float yaw = 30f, pitch = 20f, distance = 7f;
        readonly List<GameObject> objects = new List<GameObject>();
        GameObject selected;
        Color selectedColor;
        bool draggingObject;
        Vector3 dragOffset;
        int count;

        static Color Hex(string h)
        {
            Color c;
            ColorUtility.TryParseHtmlString("#" + h, out c);
            return c;
        }

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            litTemplate = Resources.Load<Material>("AtlasLit");
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildWorld();
            BuildUi();
            Add(PrimitiveType.Cube);
            SetStatus("Toca una figura para seleccionarla · un dedo: girar · dos dedos: zoom y desplazar");
        }

        // ------------------------------------------------------------------
        // 3D world

        void BuildWorld()
        {
            var pivot = new GameObject("Centro");
            target = pivot.transform;
            target.position = new Vector3(0f, 0.5f, 0f);

            var camGo = new GameObject("Cámara");
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("2a2d36");
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 500f;
            cam.allowMSAA = true;
            camGo.tag = "MainCamera";
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

            // Ground with a crisp grid texture (mipmaps + anisotropic: no
            // flicker or vanishing lines at a distance).
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Suelo";
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            var gm = new Material(litTemplate);
            gm.SetTexture("_BaseMap", GridTexture());
            gm.SetTextureScale("_BaseMap", new Vector2(100f, 100f));
            gm.SetColor("_BaseColor", Color.white);
            gm.SetFloat("_Smoothness", 0.15f);
            ground.GetComponent<Renderer>().sharedMaterial = gm;
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

        void Add(PrimitiveType kind)
        {
            var go = GameObject.CreatePrimitive(kind);
            count++;
            go.name = NameOf(kind) + " " + count;
            float a = count * 1.3f;
            go.transform.position = new Vector3(Mathf.Cos(a) * (count > 1 ? 1.8f : 0f), kind == PrimitiveType.Plane ? 0f : 0.5f, Mathf.Sin(a) * (count > 1 ? 1.8f : 0f));
            if (kind == PrimitiveType.Cylinder || kind == PrimitiveType.Capsule)
                go.transform.position += Vector3.up * 0.5f;
            var m = new Material(litTemplate);
            m.SetColor("_BaseColor", Color.HSVToRGB((count * 0.17f) % 1f, 0.45f, 0.9f));
            m.SetFloat("_Smoothness", 0.45f);
            go.GetComponent<Renderer>().sharedMaterial = m;
            objects.Add(go);
            Select(go);
        }

        static string NameOf(PrimitiveType k)
        {
            switch (k)
            {
                case PrimitiveType.Cube: return "Cubo";
                case PrimitiveType.Sphere: return "Esfera";
                case PrimitiveType.Cylinder: return "Cilindro";
                case PrimitiveType.Capsule: return "Cápsula";
                default: return "Objeto";
            }
        }

        void Select(GameObject go)
        {
            if (selected != null)
                selected.GetComponent<Renderer>().sharedMaterial.SetColor("_EmissionColor", Color.black);
            selected = go;
            if (go != null)
            {
                var m = go.GetComponent<Renderer>().sharedMaterial;
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Accent * 0.35f);
                SetStatus("Seleccionado: " + go.name + " · arrástralo para moverlo");
            }
            else
            {
                SetStatus("Nada seleccionado");
            }
        }

        void DeleteSelected()
        {
            if (selected == null) return;
            objects.Remove(selected);
            Destroy(selected);
            selected = null;
            SetStatus("Borrado");
        }

        // ------------------------------------------------------------------
        // Touch / mouse input

        void Update()
        {
            if (Input.touchCount == 1)
            {
                Touch t = Input.GetTouch(0);
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(t.fingerId))
                    return;
                if (t.phase == TouchPhase.Began)
                    BeginPointer(t.position);
                else if (t.phase == TouchPhase.Moved)
                    MovePointer(t.position, t.deltaPosition);
                else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                    EndPointer(t.position, t.deltaPosition.magnitude);
            }
            else if (Input.touchCount >= 2)
            {
                draggingObject = false;
                Touch a = Input.GetTouch(0), b = Input.GetTouch(1);
                Vector2 pa = a.position - a.deltaPosition, pb = b.position - b.deltaPosition;
                float before = (pa - pb).magnitude, now = (a.position - b.position).magnitude;
                if (before > 1f)
                    distance = Mathf.Clamp(distance * before / Mathf.Max(1f, now), 0.5f, 200f);
                Vector2 pan = (a.deltaPosition + b.deltaPosition) * 0.5f;
                float k = distance * 0.0018f;
                target.position -= cam.transform.right * pan.x * k + cam.transform.up * pan.y * k;
                UpdateCamera();
            }
            else
            {
                // Mouse (editor / PC): left drag orbits or moves, wheel zooms.
                bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (Input.GetMouseButtonDown(0) && !overUi) BeginPointer(Input.mousePosition);
                else if (Input.GetMouseButton(0) && !overUi) MovePointer(Input.mousePosition, new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 12f);
                else if (Input.GetMouseButtonUp(0)) EndPointer(Input.mousePosition, 0f);
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f && !overUi)
                {
                    distance = Mathf.Clamp(distance * (1f - wheel * 0.1f), 0.5f, 200f);
                    UpdateCamera();
                }
            }
        }

        Vector2 downPos;
        float moved;

        void BeginPointer(Vector2 pos)
        {
            downPos = pos;
            moved = 0f;
            draggingObject = false;
            RaycastHit hit;
            if (selected != null && Physics.Raycast(cam.ScreenPointToRay(pos), out hit) && hit.collider.gameObject == selected)
            {
                draggingObject = true;
                Vector3 g;
                if (GroundPoint(pos, selected.transform.position.y, out g))
                    dragOffset = selected.transform.position - g;
            }
        }

        void MovePointer(Vector2 pos, Vector2 delta)
        {
            moved += delta.magnitude;
            if (draggingObject && selected != null)
            {
                Vector3 g;
                if (GroundPoint(pos, selected.transform.position.y, out g))
                    selected.transform.position = g + dragOffset;
                return;
            }
            yaw += delta.x * 0.25f;
            pitch = Mathf.Clamp(pitch - delta.y * 0.25f, -85f, 85f);
            UpdateCamera();
        }

        void EndPointer(Vector2 pos, float lastDelta)
        {
            bool tap = (pos - downPos).magnitude < 12f && moved < 20f;
            draggingObject = false;
            if (!tap) return;
            RaycastHit hit;
            if (Physics.Raycast(cam.ScreenPointToRay(pos), out hit) && objects.Contains(hit.collider.gameObject))
                Select(hit.collider.gameObject);
            else
                Select(null);
        }

        bool GroundPoint(Vector2 screen, float height, out Vector3 point)
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            Ray r = cam.ScreenPointToRay(screen);
            float d;
            if (plane.Raycast(r, out d))
            {
                point = r.GetPoint(d);
                return true;
            }
            point = Vector3.zero;
            return false;
        }

        void UpdateCamera()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            cam.transform.position = target.position + rot * new Vector3(0f, 0f, -distance);
            cam.transform.rotation = rot;
        }

        // ------------------------------------------------------------------
        // Interface (ATLAS look: dark panels, purple accent)

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
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Top bar
            var bar = Box(canvasGo.transform, "BarraSuperior", Panel);
            var br = bar.GetComponent<RectTransform>();
            br.anchorMin = new Vector2(0f, 1f);
            br.anchorMax = new Vector2(1f, 1f);
            br.pivot = new Vector2(0.5f, 1f);
            br.sizeDelta = new Vector2(0f, 64f);
            br.anchoredPosition = Vector2.zero;
            var row = bar.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(16, 16, 10, 10);
            row.spacing = 10f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = false;
            row.childControlHeight = true;
            row.childForceExpandHeight = true;
            row.childForceExpandWidth = false;

            var title = Label(bar.transform, "ATLAS 3D", 26, TextCol, FontStyle.Bold);
            title.GetComponent<RectTransform>().sizeDelta = new Vector2(150f, 44f);
            Button(bar.transform, "+ Cubo", () => Add(PrimitiveType.Cube), true);
            Button(bar.transform, "+ Esfera", () => Add(PrimitiveType.Sphere), false);
            Button(bar.transform, "+ Cilindro", () => Add(PrimitiveType.Cylinder), false);
            Button(bar.transform, "+ Cápsula", () => Add(PrimitiveType.Capsule), false);
            Button(bar.transform, "Borrar", DeleteSelected, false);

            // Status line at the bottom
            var foot = Box(canvasGo.transform, "Estado", new Color(Panel.r, Panel.g, Panel.b, 0.85f));
            var fr = foot.GetComponent<RectTransform>();
            fr.anchorMin = new Vector2(0f, 0f);
            fr.anchorMax = new Vector2(1f, 0f);
            fr.pivot = new Vector2(0.5f, 0f);
            fr.sizeDelta = new Vector2(0f, 40f);
            fr.anchoredPosition = Vector2.zero;
            status = Label(foot.transform, "", 18, TextDim, FontStyle.Normal);
            var sr = status.GetComponent<RectTransform>();
            sr.anchorMin = Vector2.zero;
            sr.anchorMax = Vector2.one;
            sr.offsetMin = new Vector2(16f, 0f);
            sr.offsetMax = new Vector2(-16f, 0f);
            status.alignment = TextAnchor.MiddleLeft;

            var version = Label(foot.transform, "Unity · fase 0", 16, AccentDim + new Color(0.2f, 0.2f, 0.2f, 0f), FontStyle.Normal);
            var vr = version.GetComponent<RectTransform>();
            vr.anchorMin = new Vector2(1f, 0f);
            vr.anchorMax = new Vector2(1f, 1f);
            vr.pivot = new Vector2(1f, 0.5f);
            vr.sizeDelta = new Vector2(200f, 0f);
            vr.anchoredPosition = new Vector2(-16f, 0f);
            version.alignment = TextAnchor.MiddleRight;
        }

        static GameObject Box(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = c;
            return go;
        }

        Text Label(Transform parent, string text, int size, Color color, FontStyle style)
        {
            var go = new GameObject("Texto", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        void Button(Transform parent, string text, UnityEngine.Events.UnityAction onClick, bool accent)
        {
            var go = Box(parent, "Boton", accent ? AccentDim : Panel2);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(text.Length * 11f + 36f, 44f);
            var b = go.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = Accent;
            b.colors = colors;
            b.onClick.AddListener(onClick);
            var t = Label(go.transform, text, 19, TextCol, FontStyle.Bold);
            t.alignment = TextAnchor.MiddleCenter;
            var r = t.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        void SetStatus(string s)
        {
            if (status != null) status.text = s;
        }
    }
}
