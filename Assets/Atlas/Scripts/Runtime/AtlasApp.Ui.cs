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
        LayoutElement modeLe;
        RectTransform pillRect, topBarRect;
        string modeFull = "";
        Image badge;
        RectTransform toastHolder;
        UnityEngine.Rect lastSafe;
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
            if (meshMode != "")
            {
                badgeText.text = meshMode == "model" ? "MODELAR" : "ESCULPIR";
                badge.color = AtlasTheme.Key * 0.85f;
                modeFull = meshMode == "model"
                    ? (selMode == "vert" ? "Vértices" : "Caras") + " · " + (selected != null ? selected.gameObject.name : "")
                    : SculptBrush.Names[System.Array.IndexOf(SculptBrush.Order, brush)] + " · " + (selected != null ? selected.gameObject.name : "");
            }
            else if (poseMode)
            {
                badgeText.text = "POSE";
                badge.color = AtlasTheme.AccentButton;
                modeFull = activeBone != "" ? toolName + " · " + BoneNames.Display(BoneNames.Leaf(activeBone)) : "Toca una articulación";
            }
            else
            {
                badgeText.text = "OBJETO";
                badge.color = AtlasTheme.Badge;
                string what = selected != null ? selected.gameObject.name + (selected.locked ? " (bloqueado)" : "") : "Nada seleccionado";
                modeFull = toolName + " · " + what;
            }
            LayoutModePill();
            if (boneBtn != null)
            {
                boneBtn.gameObject.SetActive(poseMode || (selected != null && selected.HasBones));
                AtlasTheme.SetActive(boneBtn, poseMode);
                bool shape = MeshEditable(selected);
                modelBtn.gameObject.SetActive(shape);
                sculptBtn.gameObject.SetActive(shape);
                AtlasTheme.SetActive(modelBtn, meshMode == "model");
                AtlasTheme.SetActive(sculptBtn, meshMode == "sculpt");
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
            BuildMeshUi();
            BuildModePill();
            BuildNav();
            BuildTimeline();
            BuildProps();
            BuildTopBar();
            BuildAddMenu();
            BuildMainMenu();
            BuildRenderPanel();
            BuildLookBar();
            toastHolder = AtlasTheme.Rect(canvasGo.transform, "Avisos");
            AtlasTheme.Pin(toastHolder, 0.5f, 0f, new Vector2(RailWidth * 0.5f, 20f), new Vector2(760f, 0f));
            var tc = AtlasTheme.Column(toastHolder.gameObject, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.LowerCenter);
            tc.childForceExpandWidth = false;
            var tfit = toastHolder.gameObject.AddComponent<ContentSizeFitter>();
            tfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            SetTool(Tool.Move);
            RefreshUndo();
            timeline.SetActive(false);
            SetTimelineState(1);
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
            // Scrolls when the screen is not tall enough for every tool.
            ScrollRect rsr;
            var c = AtlasTheme.ScrollArea(rail.transform, out rsr, 4f, new RectOffset(10, 10, 12, 12));
            AtlasTheme.IconButton(c, "home", () => ShowHome(true));
            AtlasTheme.Separator(c, false);
            selectBtn = AtlasTheme.IconButton(c, "select", () => SetTool(Tool.Select));
            moveBtn = AtlasTheme.IconButton(c, "move", () => SetTool(Tool.Move));
            rotateBtn = AtlasTheme.IconButton(c, "rotate", () => SetTool(Tool.Rotate));
            scaleBtn = AtlasTheme.IconButton(c, "scale", () => SetTool(Tool.Scale));
            boneBtn = AtlasTheme.IconButton(c, "bone", TogglePose);
            modelBtn = AtlasTheme.IconButton(c, "edit", () => SetMeshMode(meshMode == "model" ? "" : "model"));
            sculptBtn = AtlasTheme.IconButton(c, "sculpt", () => SetMeshMode(meshMode == "sculpt" ? "" : "sculpt"));
            snapBtn = AtlasTheme.IconButton(c, "magnet", ToggleSnap);
            boneBtn.gameObject.SetActive(false);
            modelBtn.gameObject.SetActive(false);
            sculptBtn.gameObject.SetActive(false);
            AtlasTheme.Separator(c, false);
            AtlasTheme.IconButton(c, "add", () => ToggleMenu(addMenu));
            AtlasTheme.IconButton(c, "duplicate", DuplicateSelected);
            AtlasTheme.IconButton(c, "delete", DeleteSelected);
            AtlasTheme.IconButton(c, "deselect", () => Select(null));
            AtlasTheme.Separator(c, false);
            AtlasTheme.IconButton(c, "focus", FocusSelected);
        }

        void BuildTopBar()
        {
            var bar = AtlasTheme.Box(viewArea, "BarraFlotante", AtlasTheme.Panel, 18);
            var r = bar.rectTransform;
            topBarRect = r;
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

        /// The pill shows as much of its text as fits, and goes under the
        /// floating bar when both do not fit side by side (narrow screens).
        void LayoutModePill()
        {
            if (modeText == null || pillRect == null || viewArea == null) return;
            float W = viewArea.rect.width;
            float bar = topBarRect != null ? topBarRect.rect.width : 600f;
            bool below = W - bar - 40f < 260f;
            pillRect.anchoredPosition = new Vector2(12f, below ? -84f : -16f);
            float maxText = Mathf.Max(120f, (below ? W - 160f : W - bar - 150f));
            int maxChars = Mathf.Max(8, Mathf.FloorToInt(maxText / 10.5f));
            string t = modeFull.Length > maxChars ? modeFull.Substring(0, maxChars - 1) + "…" : modeFull;
            modeText.text = t;
            modeLe.preferredWidth = Mathf.Min(maxText, t.Length * 10.5f + 6f);
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
            modeLe = modeText.gameObject.AddComponent<LayoutElement>();
            modeLe.preferredHeight = 30f;
            modeLe.preferredWidth = 300f;
            modeLe.flexibleWidth = 0f;
            pillRect = pill.rectTransform;
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

        void BuildAddMenu()
        {
            var menu = AtlasTheme.Box(viewArea, "MenuAnadir", AtlasTheme.Panel2, 18);
            AtlasTheme.Pin(menu.rectTransform, 0f, 1f, new Vector2(12f, -66f), new Vector2(340f, 0f));
            // Scrolls when it is taller than the screen (FitMenu sizes it).
            ScrollRect msr;
            var content = AtlasTheme.ScrollArea(menu.transform, out msr, 6f, new RectOffset(12, 12, 12, 12));
            var title = AtlasTheme.Label(content, "FORMAS", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(title.gameObject, 22f);
            var shapes = AtlasTheme.Grid(content, 2, 316f, 46f);
            string[] kinds = { "cube", "sphere", "cylinder", "capsule", "cone", "pyramid", "torus", "plane" };
            foreach (var k in kinds)
            {
                string kind = k;
                AtlasTheme.TextButton(shapes, kind, DisplayName(kind), () => AddShape(kind), false, 46f, 17);
            }
            var t2 = AtlasTheme.Label(content, "LUCES", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(t2.gameObject, 22f);
            var lights = AtlasTheme.Grid(content, 3, 316f, 46f);
            AtlasTheme.TextButton(lights, "light", "Luz", () => AddShape("light"), false, 46f, 16);
            AtlasTheme.TextButton(lights, "spot", "Foco", () => AddShape("spot"), false, 46f, 16);
            AtlasTheme.TextButton(lights, "sun", "Sol", () => AddShape("sun"), false, 46f, 16);
            var t3 = AtlasTheme.Label(content, "OTROS", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(t3.gameObject, 22f);
            var others = AtlasTheme.Grid(content, 2, 316f, 46f);
            AtlasTheme.TextButton(others, "camera", "Cámara", AddCamera, false, 46f, 16);
            AtlasTheme.TextButton(others, "image", "Imagen…", AddImagePlane, false, 46f, 16);
            AtlasTheme.TextButton(content, "import", "Importar modelo…", ImportModel, true, 50f);
            var formats = AtlasTheme.Label(content, "GLB · glTF · FBX · OBJ · BLEND · PRISMA · 3DS · STL · PLY · ZIP", 14, AtlasTheme.TextDim, false, TextAnchor.MiddleCenter);
            AtlasTheme.Le(formats.gameObject, 20f);
            addMenu = menu.gameObject;
            addMenu.SetActive(false);
        }

        void BuildMainMenu()
        {
            var menu = AtlasTheme.Box(viewArea, "Menu", AtlasTheme.Panel2, 18);
            AtlasTheme.Pin(menu.rectTransform, 1f, 1f, new Vector2(-10f, -80f), new Vector2(330f, 0f));
            // Scrolls when it is taller than the screen (FitMenu sizes it).
            ScrollRect msr;
            var content = AtlasTheme.ScrollArea(menu.transform, out msr, 6f, new RectOffset(12, 12, 12, 12));
            var t1 = AtlasTheme.Label(content, "PROYECTO", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(t1.gameObject, 22f);
            // The same entries as the Godot «more» menu (importing lives in +).
            AtlasTheme.TextButton(content, "save", "Guardar", () => { CloseMenus(); SaveNow(true); }, false, 46f);
            AtlasTheme.TextButton(content, "edit", "Renombrar…", () => { CloseMenus(); RenameCurrentProject(); }, false, 46f);
            AtlasTheme.TextButton(content, "info", "Información", () => { CloseMenus(); ShowProjectInfo(); }, false, 46f);
            AtlasTheme.TextButton(content, "camera", "Renderizar imagen o vídeo…", () => ToggleMenu(renderPanel), false, 46f);
            var t2 = AtlasTheme.Label(content, "EXPORTAR MODELO 3D", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(t2.gameObject, 22f);
            var grid = AtlasTheme.Grid(content, 3, 306f, 44f);
            foreach (var f in Exporter.Formats)
            {
                string fmt = f;
                AtlasTheme.Chip(grid, fmt, () => ExportAs(fmt));
            }
            AtlasTheme.Separator(content, false);
            AtlasTheme.TextButton(content, "help", "Guía de inicio", () => { CloseMenus(); ShowGuide(); }, false, 46f);
            AtlasTheme.TextButton(content, "home", "Cerrar proyecto", () => ShowHome(true), false, 46f);
            var about = AtlasTheme.Label(content, "ATLAS 3D · versión " + Version, 14, AtlasTheme.AccentLight, false, TextAnchor.MiddleCenter);
            AtlasTheme.Le(about.gameObject, 22f);
            mainMenu = menu.gameObject;
            mainMenu.SetActive(false);
        }

        void ToggleMenu(GameObject m)
        {
            bool open = !m.activeSelf;
            CloseMenus();
            m.SetActive(open);
            if (open) FitMenu(m);
            if (m == renderPanel) AtlasTheme.SetActive(renderBtn, false);
        }

        /// A menu as tall as its entries, but never past the bottom of the
        /// view (or the timeline): then it scrolls.
        void FitMenu(GameObject m)
        {
            var r = (RectTransform)m.transform;
            var content = r.Find("Vista/Contenido") as RectTransform;
            if (content == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            float pref = LayoutUtility.GetPreferredHeight(content);
            float top = -r.anchoredPosition.y;
            float bottom = timeline != null && timeline.activeSelf ? TimelineHeight : 0f;
            float avail = viewArea.rect.height - top - bottom - 10f;
            r.sizeDelta = new Vector2(r.sizeDelta.x, Mathf.Max(80f, Mathf.Min(pref, avail)));
            content.anchoredPosition = Vector2.zero;
        }

        void CloseMenus()
        {
            if (addMenu != null) addMenu.SetActive(false);
            if (mainMenu != null) mainMenu.SetActive(false);
            if (renderPanel != null) renderPanel.SetActive(false);
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
