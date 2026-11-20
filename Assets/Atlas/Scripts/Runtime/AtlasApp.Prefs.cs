// ATLAS 3D (Unity) - «Preferencias», like the Godot version: Interfaz,
// Visor, Rendimiento and Guardado. They belong to the phone (not to a
// project) and only change the editor's view: renders always come out at
// full quality. Also the project backups (.zip) to keep work safe when the
// app is uninstalled or moved to another phone.
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.UI;
using URPAsset = UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;

namespace Atlas
{
    public static class Prefs
    {
        public static float uiScale = 1f, orbitSens = 1f, gizmoSize = 1f, snapMove = 0.25f, snapAngle = 15f, viewRes = 1f;
        public static bool invertY, showFps, viewShadows = true, viewEffects = true, catchDownloads, keepImports = true, localGizmo, autoRes = true, saveBattery = true;
        public static int accent;
        public static float homeDim = 0.45f;   // darkening over the start screen's picture
        public static int profile = 2, maxFps = 60, msaa = 4;
        public static string lang = "";

        /// The phone's language the first time: Arabic, English or Spanish.
        static string DefaultLang()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Arabic: return "ar";
                case SystemLanguage.English: return "en";
            }
            return "es";
        }

        public static readonly string[] Profiles = { "Tostadora", "Ligero", "Equilibrado", "Avanzado" };

        static float F(string k, float d) { try { return PlayerPrefs.GetFloat("atlas." + k, d); } catch (System.Exception) { return d; } }
        static int I(string k, int d) { try { return PlayerPrefs.GetInt("atlas." + k, d); } catch (System.Exception) { return d; } }

        public static void Load()
        {
            uiScale = Mathf.Clamp(F("ui", 1f), 0.7f, 1.5f);
            orbitSens = Mathf.Clamp(F("orbit", 1f), 0.2f, 3f);
            gizmoSize = Mathf.Clamp(F("gizmo", 1f), 0.5f, 2f);
            snapMove = Mathf.Clamp(F("snapMove", 0.25f), 0.01f, 5f);
            snapAngle = Mathf.Clamp(F("snapAngle", 15f), 1f, 90f);
            viewRes = Mathf.Clamp(F("viewRes", 1f), 0.5f, 1f);
            invertY = I("invertY", 0) == 1;
            showFps = I("fps", 0) == 1;
            viewShadows = I("viewShadows", 1) == 1;
            catchDownloads = I("catchDownloads", 0) == 1;
            keepImports = I("keepImports", 1) == 1;
            homeDim = Mathf.Clamp01(F("homeDim", 0.45f));
            autoRes = I("autoRes", 1) == 1;
            saveBattery = I("saveBattery", 1) == 1;
            localGizmo = I("localGizmo2", 0) == 1;
            accent = I("accent", 0);
            viewEffects = I("viewEffects", 1) == 1;
            profile = Mathf.Clamp(I("profile", 2), 0, 3);
            maxFps = I("maxFps", 60);
            try { lang = PlayerPrefs.GetString("atlas.lang", ""); } catch (System.Exception) { lang = ""; }
            if (string.IsNullOrEmpty(lang)) lang = DefaultLang();
            msaa = I("msaa", 4);
            // First time on this device: a profile that suits it (phones
            // with little memory start lighter instead of freezing).
            bool known = false;
            try { known = PlayerPrefs.HasKey("atlas.profile"); } catch (System.Exception) { }
            if (!known) SetProfile(AutoProfile());
        }

        public static void Save()
        {
            try
            {
                PlayerPrefs.SetFloat("atlas.ui", uiScale);
                PlayerPrefs.SetFloat("atlas.orbit", orbitSens);
                PlayerPrefs.SetFloat("atlas.gizmo", gizmoSize);
                PlayerPrefs.SetFloat("atlas.snapMove", snapMove);
                PlayerPrefs.SetFloat("atlas.snapAngle", snapAngle);
                PlayerPrefs.SetFloat("atlas.viewRes", viewRes);
                PlayerPrefs.SetInt("atlas.invertY", invertY ? 1 : 0);
                PlayerPrefs.SetInt("atlas.fps", showFps ? 1 : 0);
                PlayerPrefs.SetInt("atlas.viewShadows", viewShadows ? 1 : 0);
                PlayerPrefs.SetInt("atlas.catchDownloads", catchDownloads ? 1 : 0);
                PlayerPrefs.SetInt("atlas.keepImports", keepImports ? 1 : 0);
                PlayerPrefs.SetFloat("atlas.homeDim", homeDim);
                PlayerPrefs.SetInt("atlas.autoRes", autoRes ? 1 : 0);
                PlayerPrefs.SetInt("atlas.saveBattery", saveBattery ? 1 : 0);
                PlayerPrefs.SetInt("atlas.localGizmo2", localGizmo ? 1 : 0);
                PlayerPrefs.SetInt("atlas.accent", accent);
                PlayerPrefs.SetInt("atlas.viewEffects", viewEffects ? 1 : 0);
                PlayerPrefs.SetInt("atlas.profile", profile);
                PlayerPrefs.SetInt("atlas.maxFps", maxFps);
                PlayerPrefs.SetInt("atlas.msaa", msaa);
                PlayerPrefs.SetString("atlas.lang", lang);
                PlayerPrefs.Save();
            }
            catch (System.Exception) { }
        }

        static int AutoProfile()
        {
            if (!Application.isMobilePlatform) return 2;
            int mem = SystemInfo.systemMemorySize;
            if (mem > 0 && mem < 3200) return 0;
            if (mem > 0 && mem < 5500) return 1;
            return 2;
        }

        /// A quality profile sets the performance options together.
        public static void SetProfile(int p)
        {
            profile = p;
            switch (p)
            {
                case 0: viewRes = 0.55f; msaa = 1; viewShadows = false; viewEffects = false; maxFps = 30; break;
                case 1: viewRes = 0.75f; msaa = 1; viewShadows = false; viewEffects = true; maxFps = 30; break;
                case 2: viewRes = 1f; msaa = 4; viewShadows = true; viewEffects = true; maxFps = 60; break;
                default: viewRes = 1f; msaa = 4; viewShadows = true; viewEffects = true; maxFps = 120; break;
            }
        }
    }

    public partial class AtlasApp
    {
        bool capturing;
        Text fpsLabel;
        float fpsTimer;
        int fpsFrames;
        GameObject prefsWindow;

        static URPAsset Urp { get { return UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as URPAsset; } }

        /// Everything the preferences change, applied now.
        void ApplyPrefs()
        {
            if (canvasRoot != null)
            {
                var scaler = canvasRoot.GetComponent<CanvasScaler>();
                if (scaler != null) scaler.referenceResolution = new Vector2(1280f, 640f) / Prefs.uiScale;
                ApplyDesktopScale();
            }
            Application.targetFrameRate = Prefs.maxFps;
            var urp = Urp;
            if (urp != null && !capturing)
            {
                urp.renderScale = Prefs.viewRes;
                appliedScale = -1f;   // the automatic resolution measures again
                urp.msaaSampleCount = Prefs.msaa;
                appliedMsaa = Prefs.msaa;
            }
            if (gizmo != null)
            {
                gizmo.sizeMul = Prefs.gizmoSize;
                gizmo.localMove = Prefs.localGizmo;
                gizmo.snapMove = Prefs.snapMove;
                gizmo.snapAngle = Prefs.snapAngle;
            }
            ViewEffects(Prefs.viewEffects || capturing);
            RefreshShadows();
            if (fpsLabel != null) fpsLabel.gameObject.SetActive(Prefs.showFps);
        }

        void ViewEffects(bool on)
        {
            try
            {
                var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
                if (data != null) data.renderPostProcessing = on;
            }
            catch (System.Exception) { }
        }

        /// Shadows as the project asks; in the editor's view also only when
        /// «Sombras en el visor» is on (renders always have them).
        void RefreshShadows()
        {
            bool view = Prefs.viewShadows || capturing;
            if (sunLight != null) sunLight.shadows = env != null && !env.shadowsOff && view ? LightShadows.Soft : LightShadows.None;
            foreach (var it in items)
            {
                if (it == null || !it.IsLight) continue;
                var l = it.GetComponentInChildren<Light>();
                if (l != null) l.shadows = it.shadows && env != null && !env.shadowsOff && view ? LightShadows.Soft : LightShadows.None;
            }
        }

        /// Renders always at full quality, whatever the view's settings.
        void BeginCapture()
        {
            capturing = true;
            var urp = Urp;
            if (urp != null) { urp.renderScale = 1f; urp.msaaSampleCount = 4; }
            ViewEffects(true);
            RefreshShadows();
            SetAO(AOWanted);
        }

        void EndCapture()
        {
            capturing = false;
            ApplyPrefs();
            SetAO(AOWanted);
        }

        void BuildFpsLabel()
        {
            fpsLabel = AtlasTheme.Label(viewArea, "", 13, AtlasTheme.Key, true);
            fpsLabel.raycastTarget = false;
            AtlasTheme.Pin(fpsLabel.rectTransform, 0f, 1f, new Vector2(16f, -64f), new Vector2(220f, 20f));
            fpsLabel.gameObject.SetActive(Prefs.showFps);
        }

        void UpdateFps()
        {
            if (fpsLabel == null || !Prefs.showFps) return;
            fpsFrames++;
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer < 0.5f) return;
            float fpsNow = fpsFrames / fpsTimer;
            fpsLabel.text = Mathf.RoundToInt(fpsNow) + " FPS · " + (System.GC.GetTotalMemory(false) / (1024 * 1024)) + " MB";
            fpsFrames = 0;
            fpsTimer = 0f;
        }

        // ------------------------------------------------------------------
        // Window

        int prefsTab;

        float PrefsInner { get { return Mathf.Min(500f, canvasRoot.rect.width - 70f); } }

        void ShowPrefs(int tab = 0)
        {
            CloseMenus();
            if (prefsWindow != null) Destroy(prefsWindow);
            prefsTab = tab;
            var dim = AtlasTheme.Box(canvasRoot, "Preferencias", new Color(0f, 0f, 0f, 0.55f));
            AtlasTheme.Fill(dim.rectTransform);
            prefsWindow = dim.gameObject;
            var closeBg = dim.gameObject.AddComponent<Button>();
            closeBg.transition = Selectable.Transition.None;
            closeBg.onClick.AddListener(ClosePrefs);
            var card = AtlasTheme.Box(dim.transform, "Ventana", AtlasTheme.Panel2, 18);
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            var cr = card.rectTransform;
            cr.anchorMin = new Vector2(0.5f, 0.06f);
            cr.anchorMax = new Vector2(0.5f, 0.94f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            cr.sizeDelta = new Vector2(Mathf.Min(560f, canvasRoot.rect.width - 24f), 0f);
            cr.anchoredPosition = Vector2.zero;
            // Title and close
            var title = AtlasTheme.Label(card.transform, "Preferencias", 20, AtlasTheme.TextColor, true, TextAnchor.MiddleCenter);
            AtlasTheme.Pin(title.rectTransform, 0.5f, 1f, new Vector2(0f, -26f), new Vector2(300f, 34f));
            title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var x = AtlasTheme.IconButton(card.transform, "close", ClosePrefs, 44f, 22f);
            AtlasTheme.Pin((RectTransform)x.transform, 1f, 1f, new Vector2(-8f, -6f), new Vector2(44f, 44f));
            ((RectTransform)x.transform).pivot = new Vector2(1f, 1f);
            // Tabs
            var tabs = AtlasTheme.Rect(card.transform, "Pestanas");
            AtlasTheme.Pin(tabs, 0.5f, 1f, new Vector2(0f, -68f), new Vector2(520f, 40f));
            tabs.pivot = new Vector2(0.5f, 0.5f);
            AtlasTheme.Row(tabs.gameObject, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);
            string[] names = { "Interfaz", "Visor", "Rendimiento", "Guardado" };
            for (int i = 0; i < names.Length; i++)
            {
                int t = i;
                var chip = AtlasTheme.Chip(tabs, names[i], () => ShowPrefs(t), i == prefsTab);
                AtlasTheme.Le(chip.gameObject, 38f, 124f);
            }
            // Content
            var area = AtlasTheme.Rect(card.transform, "Area");
            area.anchorMin = new Vector2(0f, 0f);
            area.anchorMax = new Vector2(1f, 1f);
            area.offsetMin = new Vector2(10f, 10f);
            area.offsetMax = new Vector2(-10f, -94f);
            ScrollRect sr;
            var content = AtlasTheme.ScrollArea(area, out sr, 8f, new RectOffset(12, 12, 6, 12));
            switch (prefsTab)
            {
                case 0: PrefsInterface(content); break;
                case 1: PrefsView(content); break;
                case 2: PrefsPerformance(content); break;
                default: PrefsSaving(content); break;
            }
            if (toastHolder != null) toastHolder.SetAsLastSibling();
        }

        void ClosePrefs()
        {
            Prefs.Save();
            if (prefsWindow != null) Destroy(prefsWindow);
            prefsWindow = null;
        }

        void PrefHint(Transform parent, string text)
        {
            var l = AtlasTheme.Label(parent, text, 14, AtlasTheme.TextDim);
            l.horizontalOverflow = HorizontalWrapMode.Wrap;
            AtlasTheme.Le(l.gameObject, 40f);
        }

        void PrefsInterface(Transform c)
        {
            var ll = AtlasTheme.Label(c, "Idioma", 16, AtlasTheme.TextColor, true);
            AtlasTheme.Le(ll.gameObject, 26f);
            var lg = AtlasTheme.Grid(c, 3, PrefsInner, 42f);
            for (int i = 0; i < Lang.Codes.Length; i++)
            {
                string code = Lang.Codes[i];
                // Each language's name in its own script.
                var chip = AtlasTheme.Chip(lg, Lang.Names[i], () => SwitchLanguage(code), Prefs.lang == code);
                var ct = chip.GetComponentInChildren<Text>();
                if (code == "ar" && ct != null && Lang.ArabicFont != null) { ct.font = Lang.ArabicFont; ct.text = ArabicShaper.Visual(ArabicShaper.Shape(Lang.Names[i])); }
                else if (ct != null) ct.text = Lang.Names[i];
            }
            var al = AtlasTheme.Label(c, "Color de la interfaz", 16, AtlasTheme.TextColor, true);
            AtlasTheme.Le(al.gameObject, 26f);
            var ag = AtlasTheme.Grid(c, 4, PrefsInner, 38f);
            for (int i = 0; i < AtlasTheme.AccentNames.Length; i++)
            {
                int k = i;
                var chip = AtlasTheme.Chip(ag, AtlasTheme.AccentNames[i], () =>
                {
                    if (Prefs.accent == k) return;
                    Prefs.accent = k;
                    Prefs.Save();
                    ReloadInterface();
                }, Prefs.accent == k);
                var ct = chip.GetComponentInChildren<Text>();
                if (ct != null) ct.color = AtlasTheme.AccentPreview(k);
            }
            var bl = AtlasTheme.Label(c, "Fondo de la pantalla de inicio", 16, AtlasTheme.TextColor, true);
            AtlasTheme.Le(bl.gameObject, 26f);
            var bgg = AtlasTheme.Grid(c, 2, PrefsInner, 40f);
            AtlasTheme.TextButton(bgg, "image", "Elegir imagen…", PickHomeBackground, false, 40f, 15);
            AtlasTheme.TextButton(bgg, "close", "Quitar fondo", RemoveHomeBackground, false, 40f, 15);
            AtlasTheme.SliderRow(c, "Oscurecer el fondo", 0f, 0.9f, Prefs.homeDim, "0.00", v => { Prefs.homeDim = v; ApplyHomeBackground(); }, Prefs.Save);
            AtlasTheme.SliderRow(c, "Escala de la interfaz", 0.7f, 1.5f, Prefs.uiScale, "0.00", v => { Prefs.uiScale = v; }, () => { ApplyPrefs(); Prefs.Save(); ShowPrefs(0); });
            PrefHint(c, "Más grande si los botones se ven pequeños; más pequeña para ver más escena. Se aplica al soltar.");
            AtlasTheme.TextButton(c, "help", "Ver la guía de inicio", () => { ClosePrefs(); ShowGuide(); }, false, 44f, 16);
            AtlasTheme.TextButton(c, "info", "Acerca de ATLAS 3D", () => { ClosePrefs(); ShowAbout(); }, false, 44f, 16);
        }

        void PrefsView(Transform c)
        {
            AtlasTheme.Switch(c, "Mostrar FPS y memoria", Prefs.showFps, on => { Prefs.showFps = on; ApplyPrefs(); });
            AtlasTheme.Switch(c, "Sombras en el visor", Prefs.viewShadows, on => { Prefs.viewShadows = on; ApplyPrefs(); });
            AtlasTheme.Switch(c, "Efectos de imagen en el visor", Prefs.viewEffects, on => { Prefs.viewEffects = on; ApplyPrefs(); });
            AtlasTheme.Switch(c, "Invertir órbita vertical", Prefs.invertY, on => { Prefs.invertY = on; });
            AtlasTheme.SliderRow(c, "Sensibilidad de órbita", 0.2f, 3f, Prefs.orbitSens, "0.0", v => Prefs.orbitSens = v, Prefs.Save);
            AtlasTheme.SliderRow(c, "Tamaño del manipulador", 0.5f, 2f, Prefs.gizmoSize, "0.0", v => { Prefs.gizmoSize = v; ApplyPrefs(); }, Prefs.Save);
            AtlasTheme.Switch(c, "Mover con los ejes del objeto (girados con él, como Prisma3D)", Prefs.localGizmo, on => { Prefs.localGizmo = on; Prefs.Save(); ApplyPrefs(); });
            AtlasTheme.SliderRow(c, "Imán: mover (m)", 0.05f, 2f, Prefs.snapMove, "0.00", v => { Prefs.snapMove = v; ApplyPrefs(); }, Prefs.Save);
            AtlasTheme.SliderRow(c, "Imán: girar (°)", 5f, 90f, Prefs.snapAngle, "0", v => { Prefs.snapAngle = Mathf.Round(v); ApplyPrefs(); }, Prefs.Save);
            PrefHint(c, "Las sombras y los efectos del visor sólo cambian lo que ves al editar: el render siempre sale completo.");
        }

        void PrefsPerformance(Transform c)
        {
            var t = AtlasTheme.Label(c, "Perfil de calidad", 16, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t.gameObject, 26f);
            var g = AtlasTheme.Grid(c, 4, PrefsInner, 40f);
            for (int i = 0; i < Prefs.Profiles.Length; i++)
            {
                int p = i;
                AtlasTheme.Chip(g, Prefs.Profiles[i], () => { Prefs.SetProfile(p); ApplyPrefs(); Prefs.Save(); ShowPrefs(2); }, Prefs.profile == i);
            }
            PrefHint(c, "Tostadora: móviles muy sencillos (sin sombras ni efectos, 30 FPS). Ligero: sin sombras. Equilibrado: todo, 60 FPS. Avanzado: hasta 120 FPS. El render final sale siempre con toda la calidad.");
            AtlasTheme.SliderRow(c, "Resolución del visor", 0.5f, 1f, Prefs.viewRes, "0%", v => { Prefs.viewRes = v; ApplyPrefs(); }, Prefs.Save);
            AtlasTheme.Switch(c, "Resolución automática (baja un poco si el móvil va lento)", Prefs.autoRes, on => { Prefs.autoRes = on; autoScale = 1f; Prefs.Save(); ApplyPrefs(); });
            AtlasTheme.Switch(c, "Ahorro en reposo (menos calor y batería)", Prefs.saveBattery, on => { Prefs.saveBattery = on; Prefs.Save(); });
            var fl = AtlasTheme.Label(c, "FPS máximos", 16, AtlasTheme.TextColor, true);
            AtlasTheme.Le(fl.gameObject, 26f);
            var fg = AtlasTheme.Grid(c, 3, PrefsInner, 40f);
            foreach (int f0 in new[] { 30, 60, 120 })
            {
                int f = f0;
                AtlasTheme.Chip(fg, f + " FPS", () => { Prefs.maxFps = f; ApplyPrefs(); Prefs.Save(); ShowPrefs(2); }, Prefs.maxFps == f);
            }
            var al = AtlasTheme.Label(c, "Bordes suaves (antialiasing)", 16, AtlasTheme.TextColor, true);
            AtlasTheme.Le(al.gameObject, 26f);
            var ag = AtlasTheme.Grid(c, 3, PrefsInner, 40f);
            int[] levels = { 1, 2, 4 };
            string[] ln = { "No", "×2", "×4" };
            for (int i = 0; i < 3; i++)
            {
                int m = levels[i];
                AtlasTheme.Chip(ag, ln[i], () => { Prefs.msaa = m; ApplyPrefs(); Prefs.Save(); ShowPrefs(2); }, Prefs.msaa == m);
            }
            PrefHint(c, "Dispositivo: " + SystemInfo.deviceModel + " · " + SystemInfo.processorCount + " núcleos · " + (SystemInfo.systemMemorySize / 1024f).ToString("0.0") + " GB RAM · " + SystemInfo.graphicsDeviceName);
        }

        void PrefsSaving(Transform c)
        {
            PrefHint(c, "Tus proyectos se guardan solos con cada cambio. Al desinstalar la app se borran: haz una copia antes (sirve también para pasarlos a otro teléfono).");
            if (store != null)
                AtlasTheme.TextButton(c, "save", "Copia de «" + store.name + "» (.zip)", () => { ClosePrefs(); ExportProjectZip(store.name); }, true, 46f, 16);
            AtlasTheme.TextButton(c, "save", "Copia de todos los proyectos (.zip)", () => { ClosePrefs(); ExportProjectZip(null); }, false, 46f, 16);
            AtlasTheme.TextButton(c, "import", "Recuperar proyectos de una copia…", () => { ClosePrefs(); ImportProjectZip(); }, false, 46f, 16);
            PrefHint(c, "Carpeta de proyectos: " + SceneStore.Root);
        }

        /// The whole interface is built again in the new language (the open
        /// project is saved and opened again).
        void SwitchLanguage(string code)
        {
            if (code == Prefs.lang) return;
            Prefs.lang = code;
            Prefs.Save();
            ReloadInterface();
        }

        /// The interface built again (language, colours): the open project
        /// is saved and opened again.
        void ReloadInterface()
        {
            string reopen = store != null ? store.name : null;
            if (store != null) SaveNow(false);
            if (reopen != null) PlayerPrefs.SetString("atlas.reopen", reopen);
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        /// After a language change: the project that was open.
        void ReopenAfterLanguage()
        {
            string name = "";
            try { name = PlayerPrefs.GetString("atlas.reopen", ""); PlayerPrefs.DeleteKey("atlas.reopen"); } catch (System.Exception) { }
            if (!string.IsNullOrEmpty(name) && SceneStore.Exists(name)) OpenProject(name);
        }

        // ------------------------------------------------------------------
        // Project backups

        /// One project (or all of them, name = null) in a ZIP to share.
        void ExportProjectZip(string name)
        {
            try
            {
                if (store != null) SaveNow(false);
                string zip = Path.Combine(Exporter.ExportDir, (name != null ? SceneStore.CleanName(name).Replace(' ', '_') : "ATLAS3D_proyectos") + "_" + Stamp() + ".zip");
                if (File.Exists(zip)) File.Delete(zip);
                using (var fs = File.Create(zip))
                using (var arch = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    var dirs = new List<string>();
                    if (name != null) dirs.Add(Path.Combine(SceneStore.Root, name));
                    else dirs.AddRange(Directory.GetDirectories(SceneStore.Root));
                    foreach (var d in dirs)
                    {
                        string pname = Path.GetFileName(d);
                        foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                        {
                            string rel = pname + "/" + f.Substring(d.Length + 1).Replace('\\', '/');
                            var e = arch.CreateEntry(rel, System.IO.Compression.CompressionLevel.Fastest);
                            using (var src = File.OpenRead(f))
                            using (var dst = e.Open()) src.CopyTo(dst);
                        }
                    }
                }
                Share(zip, name != null ? "Copia del proyecto" : "Copia de todos los proyectos");
            }
            catch (System.Exception e)
            {
                Toast("No se pudo hacer la copia: " + e.Message, true);
            }
        }

        void ImportProjectZip()
        {
            if (NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                int n;
                string first;
                try { n = RestoreProjects(path, out first); }
                catch (System.Exception e) { Toast("No se pudo leer la copia: " + e.Message, true); return; }
                if (n == 0) { Toast("Ese archivo no es una copia de proyectos de ATLAS 3D", true); return; }
                Toast(n == 1 ? "Proyecto «" + first + "» recuperado" : n + " proyectos recuperados");
                if (store == null) RefreshHome();
            }, new string[] { "*/*" });
        }

        /// Unpacks every project (a folder with scene.json) of a backup.
        /// Names already in use get a number.
        static int RestoreProjects(string zipPath, out string firstName)
        {
            firstName = "";
            int count = 0;
            using (var arch = new ZipArchive(File.OpenRead(zipPath), ZipArchiveMode.Read))
            {
                var prefixes = new List<string>();
                foreach (var e in arch.Entries)
                {
                    string full = e.FullName.Replace('\\', '/');
                    if (full.EndsWith("scene.json")) prefixes.Add(full.Substring(0, full.Length - "scene.json".Length));
                }
                foreach (var pre in prefixes)
                {
                    string pname = pre.TrimEnd('/');
                    pname = pname.Contains("/") ? pname.Substring(pname.LastIndexOf('/') + 1) : pname;
                    if (pname == "") pname = Path.GetFileNameWithoutExtension(zipPath);
                    pname = SceneStore.CleanName(pname);
                    if (SceneStore.Exists(pname)) pname = SceneStore.UniqueName(pname);
                    string dest = Path.Combine(SceneStore.Root, pname);
                    Directory.CreateDirectory(dest);
                    foreach (var e in arch.Entries)
                    {
                        string full = e.FullName.Replace('\\', '/');
                        if (!full.StartsWith(pre) || string.IsNullOrEmpty(e.Name)) continue;
                        string rel = full.Substring(pre.Length);
                        if (rel.Contains("..")) continue;
                        string outPath = Path.Combine(dest, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                        using (var src = e.Open())
                        using (var dst = File.Create(outPath)) src.CopyTo(dst);
                    }
                    if (count == 0) firstName = pname;
                    count++;
                }
            }
            return count;
        }
    }
}
