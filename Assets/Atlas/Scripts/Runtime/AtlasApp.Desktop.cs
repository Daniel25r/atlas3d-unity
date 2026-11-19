// ATLAS 3D (Unity) - desktop (PC) interface: a real desktop layout like
// Blender's instead of the phone one made bigger. A menu bar (File, Edit,
// Add, Object, Animation, Render, View, Help) with the shortcuts written
// next to each entry, the scene list (outliner) and the properties docked
// on the right, a status bar with the mouse / keyboard hints, compact sizes
// that follow the screen's DPI and Blender-style keys (Shift+A, X, H, I,
// numpad views, middle mouse to orbit). Phones keep their layout.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        /// PC / Mac / Linux (also the editor): the desktop layout.
        static bool Desktop { get { return !Application.isMobilePlatform; } }

        const float MenuBarH = 40f, StatusH = 30f, OutlinerH = 230f;

        RectTransform menuBar, statusBar, outliner, outlinerList, dockBg;
        bool dockOpen = true;
        Text statusLeft, statusRight, projectLabel;
        string outlinerSig = "";
        float outlinerAt, statsAt;
        long statTris, statVerts;

        static string Short(long n) { return n >= 1000000 ? (n / 1000000f).ToString("0.0") + "M" : n >= 10000 ? (n / 1000) + "K" : n.ToString(); }

        /// Triangles and vertices of what is visible (the selection when
        /// something is selected).
        void CountScene(out long tris, out long verts)
        {
            tris = 0; verts = 0;
            foreach (var it in items)
            {
                if (it == null || !it.visible || (selected != null && it != selected)) continue;
                foreach (var r in it.GetComponentsInChildren<Renderer>())
                {
                    Mesh m = null;
                    var smr = r as SkinnedMeshRenderer;
                    if (smr != null) m = smr.sharedMesh;
                    else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; }
                    if (m == null) continue;
                    verts += m.vertexCount;
                    for (int s = 0; s < m.subMeshCount; s++) tris += m.GetIndexCount(s) / 3;
                }
            }
        }
        Vector2Int lastScreen;

        // ------------------------------------------------------------------
        // Size

        /// Compact like a desktop program: about 0.8 of the phone size at
        /// 96 DPI, more on high-DPI screens, and never so big that the
        /// panels do not fit the window.
        float DesktopScale()
        {
            float dpi = Screen.dpi > 1f ? Screen.dpi : 96f;
            float k = Mathf.Clamp(dpi / 96f, 1f, 3f) * 0.8f * Prefs.uiScale;
            k = Mathf.Min(k, Screen.width / 1180f, Screen.height / 660f);
            return Mathf.Max(0.35f, k);
        }

        void ApplyDesktopScale()
        {
            if (!Desktop || canvasRoot == null) return;
            var sc = canvasRoot.GetComponent<CanvasScaler>();
            if (sc == null) return;
            sc.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            sc.scaleFactor = DesktopScale();
            lastScreen = new Vector2Int(Screen.width, Screen.height);
        }

        // ------------------------------------------------------------------
        // Layout

        /// Called by BuildUi on desktops, after every panel exists.
        void BuildDesktopUi()
        {
            ApplyDesktopScale();
            // Rail between the menu bar and the status bar.
            var rail = safeRoot.Find("Herramientas") as RectTransform;
            if (rail != null)
            {
                rail.offsetMin = new Vector2(0f, StatusH);
                rail.offsetMax = new Vector2(RailWidth, -MenuBarH);
            }
            // The 3D view leaves room for the dock on the right.
            AtlasTheme.Fill(viewArea, RailWidth, PropsWidth, MenuBarH, StatusH);
            // Solid background of the whole dock (nothing of the 3D view
            // shows under the panels).
            var bg = AtlasTheme.Box(viewArea, "FondoPanel", AtlasTheme.Panel);
            dockBg = bg.rectTransform;
            dockBg.anchorMin = new Vector2(1f, 0f);
            dockBg.anchorMax = new Vector2(1f, 1f);
            dockBg.pivot = new Vector2(0f, 0.5f);
            dockBg.offsetMin = Vector2.zero;
            dockBg.offsetMax = new Vector2(PropsWidth, 0f);
            dockBg.SetAsFirstSibling();

            BuildMenuBar();
            BuildStatusBar();
            BuildOutliner();

            // Properties: docked under the outliner, full height, square.
            var pr = propsPanel.GetComponent<RectTransform>();
            pr.anchorMin = new Vector2(1f, 0f);
            pr.anchorMax = new Vector2(1f, 1f);
            pr.pivot = new Vector2(0f, 1f);
            pr.offsetMin = new Vector2(0f, 0f);
            pr.offsetMax = new Vector2(PropsWidth, -OutlinerH);
            Square(propsPanel);
            var close = propsPanel.transform.Find("Titulo");
            if (close != null)
                foreach (var b in close.GetComponentsInChildren<Button>(true)) b.gameObject.SetActive(false);
            propsPanel.SetActive(true);
            // The ⚙ button of the top bar shows / hides the side panel (N).
            if (propsBtn != null) AtlasTheme.SetActive(propsBtn, true);
            nav.anchoredPosition = new Vector2(-24f, -86f);
            // The floating «☰» is the menu bar now.
            if (topBarRect != null)
            {
                var bar = topBarRect.GetChild(topBarRect.childCount - 1);
                if (bar != null) bar.gameObject.SetActive(false);
            }
            LayoutTimelineNeighbours();
            BuildInspector();
        }

        /// Shows or hides the side panel (scene list + properties): the 3D
        /// view takes the whole width when it is hidden.
        void ToggleDock()
        {
            dockOpen = !dockOpen;
            AtlasTheme.Fill(viewArea, RailWidth, dockOpen ? PropsWidth : 0f, MenuBarH, StatusH);
            if (dockBg != null) dockBg.gameObject.SetActive(dockOpen);
            if (outliner != null) outliner.gameObject.SetActive(dockOpen);
            propsPanel.SetActive(dockOpen);
            if (propsBtn != null) AtlasTheme.SetActive(propsBtn, dockOpen);
            if (dockOpen) BuildInspector();
            LayoutTimelineNeighbours();
            LayoutModePill();
        }

        static void Square(GameObject go)
        {
            var img = go.GetComponent<Image>();
            if (img != null) img.sprite = null;
        }

        void BuildMenuBar()
        {
            var bar = AtlasTheme.Box(safeRoot, "BarraDeMenus", AtlasTheme.Bg);
            menuBar = bar.rectTransform;
            menuBar.anchorMin = new Vector2(0f, 1f);
            menuBar.anchorMax = new Vector2(1f, 1f);
            menuBar.pivot = new Vector2(0.5f, 1f);
            menuBar.offsetMin = new Vector2(0f, -MenuBarH);
            menuBar.offsetMax = Vector2.zero;
            var row = AtlasTheme.Rect(menuBar, "Menus");
            AtlasTheme.Fill(row, 0f, 420f, 0f, 0f);
            var hl = AtlasTheme.Row(row.gameObject, 2f, new RectOffset(10, 0, 3, 3));
            hl.childForceExpandWidth = false;
            var names = new[] { "Archivo", "Editar", "Añadir", "Objeto", "Animación", "Render", "Ver", "Ayuda" };
            foreach (var n0 in names)
            {
                string n = n0;
                var b = AtlasTheme.Box(row, "Menu" + n, new Color(0f, 0f, 0f, 0f), 6);
                var t = AtlasTheme.Label(b.transform, n, 16, AtlasTheme.TextColor, false, TextAnchor.MiddleCenter);
                AtlasTheme.Fill(t.rectTransform);
                var le = b.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = Mathf.Max(64f, t.preferredWidth + 24f);
                le.preferredHeight = MenuBarH - 6f;
                var btn = b.gameObject.AddComponent<Button>();
                // The tint multiplies the box colour: invisible at rest,
                // a lighter box under the mouse, purple when pressed.
                b.color = AtlasTheme.Panel3;
                var colors = btn.colors;
                colors.normalColor = new Color(1f, 1f, 1f, 0f);
                colors.highlightedColor = Color.white;
                colors.selectedColor = new Color(1f, 1f, 1f, 0f);
                colors.pressedColor = new Color(1.6f, 1.2f, 2f, 1f);
                colors.fadeDuration = 0.05f;
                btn.colors = colors;
                btn.targetGraphic = b;
                var rt = b.rectTransform;
                btn.onClick.AddListener(() => OpenDesktopMenu(n, rt));
            }
            projectLabel = AtlasTheme.Label(menuBar, "", 14, AtlasTheme.TextDim, false, TextAnchor.MiddleRight);
            projectLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            var pl = projectLabel.rectTransform;
            pl.anchorMin = new Vector2(1f, 0f);
            pl.anchorMax = new Vector2(1f, 1f);
            pl.pivot = new Vector2(1f, 0.5f);
            pl.sizeDelta = new Vector2(400f, 0f);
            pl.anchoredPosition = new Vector2(-14f, 0f);
        }

        void BuildStatusBar()
        {
            var bar = AtlasTheme.Box(safeRoot, "BarraDeEstado", AtlasTheme.Bg);
            statusBar = bar.rectTransform;
            statusBar.anchorMin = new Vector2(0f, 0f);
            statusBar.anchorMax = new Vector2(1f, 0f);
            statusBar.pivot = new Vector2(0.5f, 0f);
            statusBar.offsetMin = Vector2.zero;
            statusBar.offsetMax = new Vector2(0f, StatusH);
            statusLeft = AtlasTheme.Label(statusBar, "", 13, AtlasTheme.TextDim);
            AtlasTheme.Fit(statusLeft, 9);
            AtlasTheme.Fill(statusLeft.rectTransform, 12f, 580f, 0f, 0f);
            statusRight = AtlasTheme.Label(statusBar, "", 13, AtlasTheme.TextDim, false, TextAnchor.MiddleRight);
            AtlasTheme.Fit(statusRight, 9);
            var sr = statusRight.rectTransform;
            sr.anchorMin = new Vector2(1f, 0f);
            sr.anchorMax = new Vector2(1f, 1f);
            sr.pivot = new Vector2(1f, 0.5f);
            sr.sizeDelta = new Vector2(560f, 0f);
            sr.anchoredPosition = new Vector2(-12f, 0f);
        }

        void BuildOutliner()
        {
            var box = AtlasTheme.Box(viewArea, "Outliner", AtlasTheme.Panel);
            outliner = box.rectTransform;
            outliner.anchorMin = new Vector2(1f, 1f);
            outliner.anchorMax = new Vector2(1f, 1f);
            outliner.pivot = new Vector2(0f, 1f);
            outliner.offsetMin = new Vector2(0f, -OutlinerH);
            outliner.offsetMax = new Vector2(PropsWidth, 0f);
            var title = AtlasTheme.Label(outliner, "OBJETOS", 14, AtlasTheme.TextDim, true);
            var tr = title.rectTransform;
            tr.anchorMin = new Vector2(0f, 1f);
            tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.sizeDelta = new Vector2(-28f, 34f);
            tr.anchoredPosition = new Vector2(0f, -2f);
            var body = AtlasTheme.Rect(outliner, "Lista");
            AtlasTheme.Fill(body, 0f, 0f, 36f, 4f);
            ScrollRect sr;
            outlinerList = (RectTransform)AtlasTheme.ScrollArea(body, out sr, 1f, new RectOffset(6, 6, 0, 6));
            // A thin line between the outliner and the properties.
            var line = AtlasTheme.Box(outliner, "Linea", AtlasTheme.Bg);
            line.rectTransform.anchorMin = new Vector2(0f, 0f);
            line.rectTransform.anchorMax = new Vector2(1f, 0f);
            line.rectTransform.sizeDelta = new Vector2(0f, 2f);
            line.rectTransform.anchoredPosition = Vector2.zero;
            line.raycastTarget = false;
        }

        static string OutlinerIcon(string kind)
        {
            switch (kind)
            {
                case "model": return "person";
                case "camera": return "camera";
                case "light": case "spot": case "sun": return kind;
                case "fx": return "sparkle";
                case "text": return "edit";
                case "water": return "plane";
                case "terrain": return "pyramid";
                case "flag": case "image": return "image";
                case "video": return "video";
                case "cube": case "sphere": case "cylinder": case "capsule": case "cone": case "pyramid": case "torus": case "plane": return kind;
            }
            return "cube";
        }

        void RebuildOutliner()
        {
            if (outlinerList == null) return;
            AtlasTheme.Clear(outlinerList);
            foreach (var it0 in items)
            {
                var it = it0;
                if (it == null) continue;
                bool sel = it == selected || multi.Contains(it);
                var row = AtlasTheme.Box(outlinerList, "Fila", sel ? AtlasTheme.AccentDim : new Color(0f, 0f, 0f, 0f), 6);
                AtlasTheme.Le(row.gameObject, 32f);
                var b = row.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => { SelectTap(it); });
                var icon = AtlasTheme.IconButton(row.transform, OutlinerIcon(it.kind), () => SelectTap(it), 28f, 18f);
                var ir = icon.GetComponent<RectTransform>();
                ir.anchorMin = new Vector2(0f, 0f);
                ir.anchorMax = new Vector2(0f, 1f);
                ir.pivot = new Vector2(0f, 0.5f);
                ir.sizeDelta = new Vector2(28f, 28f);
                ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f);
                ir.anchoredPosition = new Vector2(3f, 0f);
                var name = AtlasTheme.Label(row.transform, it.gameObject.name, 14, it.visible ? AtlasTheme.TextColor : AtlasTheme.TextDim, sel);
                AtlasTheme.Fit(name, 10);
                AtlasTheme.Fill(name.rectTransform, 34f, 70f, 0f, 0f);
                var eye = AtlasTheme.IconButton(row.transform, it.visible ? "eye" : "eye_off", () =>
                {
                    it.visible = !it.visible;
                    ApplyLook(it);
                    Commit(true);
                    RefreshProps();
                    outlinerSig = "";
                }, 30f, 18f);
                var er = eye.GetComponent<RectTransform>();
                er.anchorMin = er.anchorMax = new Vector2(1f, 0.5f);
                er.pivot = new Vector2(1f, 0.5f);
                er.anchoredPosition = new Vector2(-30f, 0f);
                var lk = AtlasTheme.IconButton(row.transform, it.locked ? "lock" : "unlock", () =>
                {
                    it.locked = !it.locked;
                    Commit(true);
                    RefreshProps();
                    outlinerSig = "";
                }, 30f, 16f);
                var lr = lk.GetComponent<RectTransform>();
                lr.anchorMin = lr.anchorMax = new Vector2(1f, 0.5f);
                lr.pivot = new Vector2(1f, 0.5f);
                lr.anchoredPosition = new Vector2(0f, 0f);
            }
            if (items.Count == 0)
            {
                var e = AtlasTheme.Label(outlinerList, "Escena vacía: Añadir (Shift+A) o Archivo → Importar", 13, AtlasTheme.TextDim);
                AtlasTheme.Le(e.gameObject, 30f);
            }
        }

        /// Every frame on desktops: window size, outliner and status bar.
        void UpdateDesktop()
        {
            if (!Desktop || canvasRoot == null) return;
            if (Screen.width != lastScreen.x || Screen.height != lastScreen.y) ApplyDesktopScale();
            if (menuBar == null) return;
            if (Time.unscaledTime < outlinerAt) return;
            outlinerAt = Time.unscaledTime + 0.25f;
            var sb = new System.Text.StringBuilder();
            sb.Append(selected != null ? selected.id : "-").Append('|');
            foreach (var m in multi) if (m != null) sb.Append(m.id);
            sb.Append('|');
            foreach (var it in items) if (it != null) sb.Append(it.id).Append(it.gameObject.name).Append(it.visible ? '1' : '0').Append(it.locked ? '1' : '0');
            string sig = sb.ToString();
            if (sig != outlinerSig)
            {
                outlinerSig = sig;
                RebuildOutliner();
            }
            if (projectLabel != null) Lang.Set(projectLabel, (store != null ? store.name + "  ·  " : "") + "v" + Version);
            if (statusLeft != null)
            {
                string hint;
                if (poseMode) hint = "Modo pose · Clic en una articulación: elegirla · G mover · E rotar · I clave · Tab salir";
                else if (meshMode == "model") hint = "Modelar · Clic: elegir vértices · Shift+clic: añadir · G mover · Tab salir";
                else if (meshMode == "sculpt") hint = "Esculpir · Arrastrar sobre la forma: pincel";
                else hint = "Clic: seleccionar · Arrastrar o clic medio: girar · Shift+clic medio o clic derecho: desplazar · Rueda: zoom · Shift+A: añadir · G/E/R: mover, rotar, escalar · X: borrar · Espacio: reproducir";
                Lang.Set(statusLeft, hint);
            }
            if (Time.unscaledTime >= statsAt)
            {
                statsAt = Time.unscaledTime + 1f;
                CountScene(out statTris, out statVerts);
            }
            if (statusRight != null)
                Lang.Set(statusRight, "Tris " + Short(statTris) + " · Verts " + Short(statVerts) + "  ·  Objetos: " + items.Count + "  ·  Fotograma " + Mathf.RoundToInt(frame) + " / " + endFrame);
        }

        // ------------------------------------------------------------------
        // Menus

        void OpenDesktopMenu(string name, RectTransform at)
        {
            CloseMenus();
            var l = new List<AtlasTheme.MenuItem>();
            switch (name)
            {
                case "Archivo":
                    l.Add(new AtlasTheme.MenuItem("add", "Nuevo proyecto", () => { ShowHome(true); NewProject(false); }));
                    l.Add(new AtlasTheme.MenuItem("home", "Abrir proyecto…  (lista de proyectos)", () => ShowHome(true)));
                    l.Add(new AtlasTheme.MenuItem("save", "Guardar                Ctrl+S", () => SaveNow(true)));
                    l.Add(new AtlasTheme.MenuItem("edit", "Renombrar…", RenameCurrentProject));
                    l.Add(new AtlasTheme.MenuItem("info", "Información del proyecto", ShowProjectInfo));
                    l.Add(AtlasTheme.MenuItem.Header("Importar y exportar"));
                    l.Add(new AtlasTheme.MenuItem("import", "Importar modelo…       Ctrl+I", ImportModel));
                    l.Add(new AtlasTheme.MenuItem("import", "Descargas (Workshop)…", ShowDownloads));
                    foreach (var f0 in Exporter.Formats)
                    {
                        string f = f0;
                        l.Add(new AtlasTheme.MenuItem("save", "Exportar " + f + "…", () => ExportAs(f)));
                    }
                    l.Add(new AtlasTheme.MenuItem("save", "Copia del proyecto (.zip)", () => ExportProjectZip(store != null ? store.name : null)));
                    l.Add(AtlasTheme.MenuItem.Header(""));
                    l.Add(new AtlasTheme.MenuItem("settings", "Preferencias…", () => ShowPrefs(0)));
                    l.Add(new AtlasTheme.MenuItem("close", "Cerrar proyecto", () => ShowHome(true)));
                    break;
                case "Editar":
                    l.Add(new AtlasTheme.MenuItem("undo", "Deshacer               Ctrl+Z", Undo));
                    l.Add(new AtlasTheme.MenuItem("redo", "Rehacer                 Ctrl+Y", Redo));
                    l.Add(new AtlasTheme.MenuItem("duplicate", "Duplicar                Ctrl+D", DuplicateSelected));
                    l.Add(new AtlasTheme.MenuItem("delete", "Borrar                  X / Supr", DeleteSelected));
                    l.Add(new AtlasTheme.MenuItem("deselect", "Deseleccionar           Alt+A", () => Select(null)));
                    l.Add(new AtlasTheme.MenuItem("settings", "Preferencias…", () => ShowPrefs(0)));
                    break;
                case "Añadir":
                    l.Add(AtlasTheme.MenuItem.Header("Formas"));
                    foreach (var k0 in new[] { "cube", "sphere", "cylinder", "capsule", "cone", "pyramid", "torus", "plane" })
                    {
                        string k = k0;
                        l.Add(new AtlasTheme.MenuItem(k, DisplayName(k), () => AddShape(k)));
                    }
                    l.Add(AtlasTheme.MenuItem.Header("Luces y cámara"));
                    l.Add(new AtlasTheme.MenuItem("light", "Luz", () => AddShape("light")));
                    l.Add(new AtlasTheme.MenuItem("spot", "Foco", () => AddShape("spot")));
                    l.Add(new AtlasTheme.MenuItem("sun", "Sol", () => AddShape("sun")));
                    l.Add(new AtlasTheme.MenuItem("camera", "Cámara", AddCamera));
                    l.Add(AtlasTheme.MenuItem.Header("Mundo y efectos"));
                    l.Add(new AtlasTheme.MenuItem("plane", "Agua", () => AddWorldPiece("water", "sea")));
                    l.Add(new AtlasTheme.MenuItem("pyramid", "Terreno", () => AddWorldPiece("terrain", "hills")));
                    l.Add(new AtlasTheme.MenuItem("image", "Tela", () => AddWorldPiece("flag", "flag")));
                    l.Add(new AtlasTheme.MenuItem("edit", "Texto 3D", AddText3D));
                    for (int i = 0; i < FxPresets.Length; i++)
                    {
                        string p = FxPresets[i];
                        l.Add(new AtlasTheme.MenuItem("sparkle", FxNames[i], () => AddFx(p)));
                    }
                    l.Add(AtlasTheme.MenuItem.Header("Escenarios listos"));
                    for (int i = 0; i < SetIds.Length; i++)
                    {
                        string sid = SetIds[i];
                        l.Add(new AtlasTheme.MenuItem("film", SetNames[i], () => AddSet(sid)));
                    }
                    l.Add(AtlasTheme.MenuItem.Header("Archivos"));
                    l.Add(new AtlasTheme.MenuItem("image", "Imagen…", AddImagePlane));
                    l.Add(new AtlasTheme.MenuItem("video", "Vídeo…", AddVideoPlane));
                    l.Add(new AtlasTheme.MenuItem("music", "Audio…", PickAudio));
                    l.Add(new AtlasTheme.MenuItem("import", "Importar modelo…", ImportModel));
                    l.Add(new AtlasTheme.MenuItem("folder", "Biblioteca…", ShowLibrary));
                    break;
                case "Objeto":
                    l.Add(new AtlasTheme.MenuItem("select", "Seleccionar             Q", () => SetTool(Tool.Select)));
                    l.Add(new AtlasTheme.MenuItem("move", "Mover                   G", () => SetTool(Tool.Move)));
                    l.Add(new AtlasTheme.MenuItem("rotate", "Rotar                   E", () => SetTool(Tool.Rotate)));
                    l.Add(new AtlasTheme.MenuItem("scale", "Escalar                 S / R", () => SetTool(Tool.Scale)));
                    l.Add(new AtlasTheme.MenuItem("focus", "Enfocar                 F / . del teclado numérico", FocusSelected));
                    l.Add(new AtlasTheme.MenuItem("eye", "Ocultar / mostrar       H", ToggleSelectedVisible));
                    l.Add(new AtlasTheme.MenuItem("magnet", "Imán (ajustar a la rejilla)", ToggleSnap));
                    if (selected != null && selected.HasBones) l.Add(new AtlasTheme.MenuItem("bone", "Modo pose               Ctrl+Tab", TogglePose));
                    if (selected != null && MeshEditable(selected))
                    {
                        l.Add(new AtlasTheme.MenuItem("edit", "Modelar                 Tab", () => SetMeshMode(meshMode == "model" ? "" : "model")));
                        l.Add(new AtlasTheme.MenuItem("sculpt", "Esculpir", () => SetMeshMode(meshMode == "sculpt" ? "" : "sculpt")));
                    }
                    break;
                case "Animación":
                    l.Add(new AtlasTheme.MenuItem("timeline", "Línea de tiempo", ToggleTimeline));
                    l.Add(new AtlasTheme.MenuItem("play", "Reproducir / pausa      Espacio", TogglePlay));
                    l.Add(new AtlasTheme.MenuItem("key", "Insertar clave          I / K", () => { if (selected != null) AutoKeyNow(); }));
                    l.Add(new AtlasTheme.MenuItem("step_back", "Fotograma anterior      ←", () => StepFrame(-1)));
                    l.Add(new AtlasTheme.MenuItem("step_fwd", "Fotograma siguiente     →", () => StepFrame(1)));
                    l.Add(new AtlasTheme.MenuItem("curve", "Herramientas de movimiento…", OpenMotionTools));
                    l.Add(new AtlasTheme.MenuItem("music", "Añadir audio…", PickAudio));
                    break;
                case "Render":
                    l.Add(new AtlasTheme.MenuItem("render", "Renderizar imagen o vídeo…   F12", () => ToggleMenu(renderPanel)));
                    l.Add(new AtlasTheme.MenuItem("camera", "Ver desde la cámara           0 numérico", ToggleCameraView));
                    l.Add(new AtlasTheme.MenuItem("sun", "Ambiente y efectos de imagen", () => Select(null)));
                    break;
                case "Ver":
                    l.Add(AtlasTheme.MenuItem.Check("settings", "Panel lateral (objetos y propiedades)   N", dockOpen, ToggleDock));
                    l.Add(new AtlasTheme.MenuItem("eye", "Rejilla y suelo", ToggleGrid));
                    l.Add(new AtlasTheme.MenuItem("eye", "Frente                  1 numérico", () => LookAlong(Vector3.forward)));
                    l.Add(new AtlasTheme.MenuItem("eye", "Atrás                   Ctrl+1 numérico", () => LookAlong(Vector3.back)));
                    l.Add(new AtlasTheme.MenuItem("eye", "Derecha                 3 numérico", () => LookAlong(Vector3.left)));
                    l.Add(new AtlasTheme.MenuItem("eye", "Izquierda               Ctrl+3 numérico", () => LookAlong(Vector3.right)));
                    l.Add(new AtlasTheme.MenuItem("eye", "Arriba                  7 numérico", () => LookAlong(Vector3.up)));
                    l.Add(new AtlasTheme.MenuItem("eye", "Abajo                   Ctrl+7 numérico", () => LookAlong(Vector3.down)));
                    l.Add(new AtlasTheme.MenuItem("focus", "Enfocar la selección    F", FocusSelected));
                    break;
                case "Ayuda":
                    l.Add(new AtlasTheme.MenuItem("help", "Guía de inicio", ShowGuide));
                    l.Add(new AtlasTheme.MenuItem("key", "Atajos de teclado", ShowShortcuts));
                    l.Add(new AtlasTheme.MenuItem("info", "Acerca de ATLAS 3D", ShowAbout));
                    break;
            }
            var corners = new Vector3[4];
            at.GetWorldCorners(corners);
            AtlasTheme.Popup(canvasRoot, RectTransformUtility.WorldToScreenPoint(null, corners[0]), l, 340f, true);
            if (toastHolder != null) toastHolder.SetAsLastSibling();
        }

        void ToggleSelectedVisible()
        {
            if (selected == null) return;
            selected.visible = !selected.visible;
            ApplyLook(selected);
            Commit(true);
            RefreshProps();
            Toast(selected.visible ? "Visible" : "Oculto (sigue en la lista de la escena)");
        }

        void ShowShortcuts()
        {
            AtlasTheme.Dialog(canvasRoot, "Atajos de teclado",
                "Ratón: clic para seleccionar (Ctrl o Shift + clic: varios a la vez; Ctrl+G agrupar, Ctrl+Shift+G desagrupar); arrastrar o clic medio para girar la vista; Shift+clic medio o clic derecho para desplazarla; rueda para el zoom.\n\n" +
                "Ctrl+Z / Ctrl+Y: deshacer / rehacer · Ctrl+S: guardar · Ctrl+D: duplicar · Ctrl+I: importar\n" +
                "Shift+A: añadir · X o Supr: borrar · H: ocultar · Alt+A: deseleccionar · N: panel lateral\n" +
                "Q: seleccionar · G o W: mover · E: rotar · S o R: escalar · F: enfocar\n" +
                "Tab: modelar · Ctrl+Tab: modo pose\n" +
                "Espacio: reproducir · I o K: clave · ← →: fotograma\n" +
                "Teclado numérico: 1 frente, 3 derecha, 7 arriba (con Ctrl, el lado contrario), 0 cámara, . enfocar\n" +
                "F12: render", null, "Cerrar", false, null);
            if (toastHolder != null) toastHolder.SetAsLastSibling();
        }

        /// Blender-style keys on top of the common ones (Shortcuts()).
        /// Returns true when the key was used.
        bool DesktopKeys(bool ctrl, bool shift)
        {
            if (!Desktop) return false;
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (shift && Input.GetKeyDown(KeyCode.A)) { OpenDesktopMenu("Añadir", menuBar != null ? menuBar : canvasRoot); return true; }
            if (alt && Input.GetKeyDown(KeyCode.A)) { Select(null); return true; }
            if (ctrl && Input.GetKeyDown(KeyCode.I)) { ImportModel(); return true; }
            if (ctrl && shift && Input.GetKeyDown(KeyCode.G)) { UngroupSelection(); return true; }
            if (ctrl && Input.GetKeyDown(KeyCode.G)) { GroupSelection(); return true; }
            if (ctrl && Input.GetKeyDown(KeyCode.Tab)) { if (selected != null && selected.HasBones) TogglePose(); return true; }
            if (ctrl && Input.GetKeyDown(KeyCode.Keypad1)) { LookAlong(Vector3.back); return true; }
            if (ctrl && Input.GetKeyDown(KeyCode.Keypad3)) { LookAlong(Vector3.right); return true; }
            if (ctrl && Input.GetKeyDown(KeyCode.Keypad7)) { LookAlong(Vector3.down); return true; }
            if (ctrl || alt) return false;
            if (Input.GetKeyDown(KeyCode.X)) { if (selected != null && meshMode == "") DeleteSelected(); return true; }
            if (Input.GetKeyDown(KeyCode.H)) { ToggleSelectedVisible(); return true; }
            if (Input.GetKeyDown(KeyCode.N)) { ToggleDock(); return true; }
            if (Input.GetKeyDown(KeyCode.I)) { if (selected != null) AutoKeyNow(); return true; }
            if (Input.GetKeyDown(KeyCode.S) && !shift) { SetTool(Tool.Scale); return true; }
            if (Input.GetKeyDown(KeyCode.F12)) { ToggleMenu(renderPanel); return true; }
            if (Input.GetKeyDown(KeyCode.Keypad1)) { LookAlong(Vector3.forward); return true; }
            if (Input.GetKeyDown(KeyCode.Keypad3)) { LookAlong(Vector3.left); return true; }
            if (Input.GetKeyDown(KeyCode.Keypad7)) { LookAlong(Vector3.up); return true; }
            if (Input.GetKeyDown(KeyCode.Keypad0)) { ToggleCameraView(); return true; }
            if (Input.GetKeyDown(KeyCode.KeypadPeriod)) { FocusSelected(); return true; }
            return false;
        }
    }
}
