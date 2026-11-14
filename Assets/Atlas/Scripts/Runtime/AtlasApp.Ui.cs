// ATLAS 3D (Unity) - editor interface: tool rail, floating top bar, mode
// pill, axis gizmo, timeline, the «Añadir» and main menus and the toasts.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        RectTransform canvasRoot, safeRoot, viewArea;
        Button selectBtn, moveBtn, rotateBtn, scaleBtn, undoBtn, redoBtn, animBtn, propsBtn, eyeBtn, renderBtn;
        GameObject addMenu, mainMenu, timeline;
        Text modeText, badgeText;
        Image badge;
        RectTransform toastHolder;
        UnityEngine.Rect lastSafe;
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
            if (poseMode)
            {
                badgeText.text = "POSE";
                badge.color = AtlasTheme.AccentButton;
                modeText.text = activeBone != "" ? toolName + " · " + BoneNames.Display(BoneNames.Leaf(activeBone)) : "Toca una articulación";
            }
            else
            {
                badgeText.text = "OBJETO";
                badge.color = AtlasTheme.Badge;
                string what = selected != null ? selected.gameObject.name + (selected.locked ? " (bloqueado)" : "") : "Nada seleccionado";
                modeText.text = toolName + " · " + what;
            }
            if (boneBtn != null)
            {
                boneBtn.gameObject.SetActive(poseMode || (selected != null && selected.HasBones));
                AtlasTheme.SetActive(boneBtn, poseMode);
            }
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
            canvasRoot = (RectTransform)canvasGo.transform;

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
            BuildBoneOverlay();
            BuildModePill();
            BuildNav();
            BuildTimeline();
            BuildProps();
            BuildTopBar();
            BuildAddMenu();
            BuildMainMenu();
            BuildRenderPanel();
            toastHolder = AtlasTheme.Rect(canvasGo.transform, "Avisos");
            AtlasTheme.Pin(toastHolder, 0.5f, 0f, new Vector2(RailWidth * 0.5f, 20f), new Vector2(760f, 0f));
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
            AtlasTheme.IconButton(rail.transform, "home", () => ShowHome(true));
            AtlasTheme.Separator(rail.transform, false);
            selectBtn = AtlasTheme.IconButton(rail.transform, "select", () => SetTool(Tool.Select));
            moveBtn = AtlasTheme.IconButton(rail.transform, "move", () => SetTool(Tool.Move));
            rotateBtn = AtlasTheme.IconButton(rail.transform, "rotate", () => SetTool(Tool.Rotate));
            scaleBtn = AtlasTheme.IconButton(rail.transform, "scale", () => SetTool(Tool.Scale));
            boneBtn = AtlasTheme.IconButton(rail.transform, "bone", TogglePose);
            boneBtn.gameObject.SetActive(false);
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
            renderBtn = AtlasTheme.TextButton(bar.transform, "render", "Render", () => ToggleMenu(renderPanel), true);
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
            badge = AtlasTheme.Box(pill.transform, "Insignia", AtlasTheme.Badge, 14);
            badge.raycastTarget = false;
            var ble = badge.gameObject.AddComponent<LayoutElement>();
            ble.preferredWidth = 92f;
            ble.preferredHeight = 30f;
            badgeText = AtlasTheme.Label(badge.transform, "OBJETO", 15, Color.white, true, TextAnchor.MiddleCenter);
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
            AtlasTheme.IconButton(row.transform, "to_start", () => SetFrame(0f));
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
            AtlasTheme.Clear(keyMarks);
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

        void BuildAddMenu()
        {
            var menu = AtlasTheme.Box(viewArea, "MenuAnadir", AtlasTheme.Panel2, 18);
            AtlasTheme.Pin(menu.rectTransform, 0f, 1f, new Vector2(12f, -66f), new Vector2(340f, 0f));
            AtlasTheme.Column(menu.gameObject, 6f, new RectOffset(12, 12, 12, 12));
            var fit = menu.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var title = AtlasTheme.Label(menu.transform, "FORMAS", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(title.gameObject, 22f);
            var shapes = AtlasTheme.Grid(menu.transform, 2, 316f, 46f);
            string[] kinds = { "cube", "sphere", "cylinder", "capsule", "cone", "pyramid", "torus", "plane" };
            foreach (var k in kinds)
            {
                string kind = k;
                AtlasTheme.TextButton(shapes, kind, DisplayName(kind), () => AddShape(kind), false, 46f, 17);
            }
            var t2 = AtlasTheme.Label(menu.transform, "LUCES", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(t2.gameObject, 22f);
            var lights = AtlasTheme.Grid(menu.transform, 3, 316f, 46f);
            AtlasTheme.TextButton(lights, "light", "Luz", () => AddShape("light"), false, 46f, 16);
            AtlasTheme.TextButton(lights, "spot", "Foco", () => AddShape("spot"), false, 46f, 16);
            AtlasTheme.TextButton(lights, "sun", "Sol", () => AddShape("sun"), false, 46f, 16);
            AtlasTheme.TextButton(menu.transform, "import", "Importar modelo…", ImportModel, true, 50f);
            var formats = AtlasTheme.Label(menu.transform, "GLB · glTF · OBJ · STL · PLY · ZIP", 14, AtlasTheme.TextDim, false, TextAnchor.MiddleCenter);
            AtlasTheme.Le(formats.gameObject, 20f);
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
            AtlasTheme.Le(t1.gameObject, 22f);
            AtlasTheme.TextButton(menu.transform, "home", "Mis proyectos", () => ShowHome(true), false, 46f);
            AtlasTheme.TextButton(menu.transform, "import", "Importar modelo…", ImportModel, false, 46f);
            AtlasTheme.TextButton(menu.transform, "save", "Guardar", () => { CloseMenus(); SaveNow(true); }, false, 46f);
            AtlasTheme.TextButton(menu.transform, "close", "Vaciar la escena", NewScene, false, 46f);
            var t2 = AtlasTheme.Label(menu.transform, "EXPORTAR MODELO 3D", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(t2.gameObject, 22f);
            var grid = AtlasTheme.Grid(menu.transform, 3, 306f, 44f);
            foreach (var f in Exporter.Formats)
            {
                string fmt = f;
                AtlasTheme.Chip(grid, fmt, () => ExportAs(fmt));
            }
            var about = AtlasTheme.Label(menu.transform, "ATLAS 3D · versión " + Version + " (Unity)", 14, AtlasTheme.AccentLight, false, TextAnchor.MiddleCenter);
            AtlasTheme.Le(about.gameObject, 22f);
            mainMenu = menu.gameObject;
            mainMenu.SetActive(false);
        }

        void ToggleMenu(GameObject m)
        {
            bool open = !m.activeSelf;
            CloseMenus();
            m.SetActive(open);
            if (m == renderPanel) AtlasTheme.SetActive(renderBtn, false);
        }

        void CloseMenus()
        {
            if (addMenu != null) addMenu.SetActive(false);
            if (mainMenu != null) mainMenu.SetActive(false);
            if (renderPanel != null) renderPanel.SetActive(false);
        }

        void ToggleTimeline()
        {
            timeline.SetActive(!timeline.activeSelf);
            AtlasTheme.SetActive(animBtn, timeline.activeSelf);
            toastHolder.anchoredPosition = new Vector2(RailWidth * 0.5f, timeline.activeSelf ? 136f : 20f);
            var pr = propsPanel.GetComponent<RectTransform>();
            pr.offsetMin = new Vector2(pr.offsetMin.x, timeline.activeSelf ? 134f : 10f);
            if (timeline.activeSelf)
            {
                RefreshKeys();
                Toast("Animar: elige un fotograma, coloca el objeto y pulsa la clave amarilla");
            }
            else if (playing) TogglePlay();
        }

        void ToggleProps()
        {
            propsPanel.SetActive(!propsPanel.activeSelf);
            AtlasTheme.SetActive(propsBtn, propsPanel.activeSelf);
            nav.anchoredPosition = new Vector2(propsPanel.activeSelf ? -(PropsWidth + 24f) : -24f, -86f);
            if (propsPanel.activeSelf) BuildInspector();
        }

        void ToggleGrid()
        {
            ground.SetActive(!ground.activeSelf);
            AtlasTheme.SetButtonIcon(eyeBtn, ground.activeSelf ? "eye" : "eye_off");
            Commit(true);
            RefreshProps();
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
            if (homeSafe != null)
            {
                homeSafe.anchorMin = min;
                homeSafe.anchorMax = max;
                homeSafe.offsetMin = Vector2.zero;
                homeSafe.offsetMax = Vector2.zero;
            }
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
            toastHolder.SetAsLastSibling();
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
