// ATLAS 3D (Unity) - «Biblioteca»: every model kept in ATLAS, ready to use
// again without downloading or looking for the file:
//   · the files downloaded with ATLAS and the ones imported from the phone
//     or the PC (a copy is kept by itself, «Guardar en la biblioteca lo que
//     importo», on by default), in the Descargas folder;
//   · «Mis modelos»: objects saved with their materials, pose and rig.
// From the start screen «Usar» opens a new project with it; inside a
// project it is added to the scene.
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        GameObject libWindow;
        Transform libList;
        string libFilter = "";

        // ------------------------------------------------------------------
        // Keeping what is imported

        /// Copies the imported files into the library (one file, or a folder
        /// for several: an OBJ with its MTL and pictures, a .proj with its
        /// .pobject…). Same name and size already there: nothing is copied.
        /// Runs on a worker thread: only file work (the folder comes in).
        static void KeepInLibrary(string[] paths, string libDir)
        {
            if (paths == null || paths.Length == 0 || string.IsNullOrEmpty(libDir)) return;
            string full = Path.GetFullPath(libDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var p in paths)
                if (string.IsNullOrEmpty(p) || !File.Exists(p) || Path.GetFullPath(p).StartsWith(full)) return;   // already from the library
            long total = 0;
            foreach (var p in paths) total += new FileInfo(p).Length;
            if (total > 600L * 1024 * 1024) return;   // very big: not doubled on the phone
            if (paths.Length == 1)
            {
                string src = paths[0];
                if (!IsModelFile(src)) return;
                string name = Path.GetFileName(src);
                string dst = Path.Combine(libDir, name);
                if (File.Exists(dst) && new FileInfo(dst).Length == total) return;
                int n = 2;
                while (File.Exists(dst)) dst = Path.Combine(libDir, Path.GetFileNameWithoutExtension(name) + " (" + n++ + ")" + Path.GetExtension(name));
                File.Copy(src, dst);
                return;
            }
            // Several files: a folder named after the main one.
            string main = paths[0];
            foreach (var p in paths) if (IsModelFile(p) && Path.GetExtension(p).ToLowerInvariant() != ".pobject") { main = p; break; }
            string folderName = Path.GetFileNameWithoutExtension(main);
            string folder = Path.Combine(libDir, folderName);
            if (Directory.Exists(folder) && FolderSize(folder) == total) return;
            int k = 2;
            while (Directory.Exists(folder)) folder = Path.Combine(libDir, folderName + " (" + k++ + ")");
            Directory.CreateDirectory(folder);
            foreach (var p in paths) File.Copy(p, Path.Combine(folder, Path.GetFileName(p)), true);
        }

        static long FolderSize(string dir)
        {
            long s = 0;
            try { foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) s += new FileInfo(f).Length; }
            catch (System.Exception) { }
            return s;
        }

        // ------------------------------------------------------------------
        // Window

        void ShowLibrary()
        {
            CloseMenus();
            if (libWindow != null) Destroy(libWindow);
            var dim = AtlasTheme.Box(canvasRoot, "Dialogo", new Color(0f, 0f, 0f, 0.55f));
            AtlasTheme.Fill(dim.rectTransform);
            libWindow = dim.gameObject;
            var closeBg = dim.gameObject.AddComponent<Button>();
            closeBg.transition = Selectable.Transition.None;
            closeBg.onClick.AddListener(() => Destroy(libWindow));
            var card = AtlasTheme.Box(dim.transform, "Ventana", AtlasTheme.Panel2, 18);
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            var cr = card.rectTransform;
            cr.anchorMin = new Vector2(0.5f, 0.05f);
            cr.anchorMax = new Vector2(0.5f, 0.95f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            float w = Mathf.Min(760f, canvasRoot.rect.width - 24f);
            cr.sizeDelta = new Vector2(w, 0f);
            cr.anchoredPosition = Vector2.zero;
            var title = AtlasTheme.Label(card.transform, "Biblioteca", 20, AtlasTheme.TextColor, true, TextAnchor.MiddleCenter);
            AtlasTheme.Pin(title.rectTransform, 0.5f, 1f, new Vector2(0f, -26f), new Vector2(420f, 34f));
            title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var x = AtlasTheme.IconButton(card.transform, "close", () => Destroy(libWindow), 44f, 22f);
            AtlasTheme.Pin((RectTransform)x.transform, 1f, 1f, new Vector2(-8f, -6f), new Vector2(44f, 44f));
            ((RectTransform)x.transform).pivot = new Vector2(1f, 1f);
            var body = AtlasTheme.Rect(card.transform, "Cuerpo");
            AtlasTheme.Fill(body, 0f, 0f, 56f, 10f);
            ScrollRect sr;
            var c = AtlasTheme.ScrollArea(body, out sr, 8f, new RectOffset(18, 18, 0, 12));
            float iw = w - 36f;

            var hint = AtlasTheme.Label(c, "Todo lo que guardas queda aquí para usarlo otra vez sin volver a descargarlo. Desde el inicio, «Usar» abre un proyecto nuevo con él; dentro de un proyecto, lo añade a la escena.", 14, AtlasTheme.TextDim);
            AtlasTheme.Le(hint.gameObject, 40f);
            var tb = AtlasTheme.Grid(c, 2, iw, 42f);
            AtlasTheme.TextButton(tb, "import", "Añadir archivos…", AddFilesToLibrary, true, 42f, 15);
            AtlasTheme.TextButton(tb, "import", "Descargar de internet…", () => { Destroy(libWindow); ShowDownloads(); }, false, 42f, 15);
            AtlasTheme.Switch(c, "Guardar en la biblioteca los modelos que importo", Prefs.keepImports, on => { Prefs.keepImports = on; Prefs.Save(); });
            var field = AtlasTheme.TextField(c, libFilter, v => { libFilter = v.Trim().ToLowerInvariant(); FillLibrary(); }, 42f);
            var ph = AtlasTheme.Label(field.transform, "Buscar en la biblioteca…", 15, AtlasTheme.TextDim);
            AtlasTheme.Fill(ph.rectTransform, 12f, 12f);
            field.placeholder = ph;
            libList = AtlasTheme.Rect(c, "Lista");
            AtlasTheme.Column(libList.gameObject, 4f, new RectOffset(0, 0, 0, 0));
            FillLibrary();
            toastHolder.SetAsLastSibling();
        }

        void FillLibrary()
        {
            if (libList == null) return;
            AtlasTheme.Clear(libList);
            // Model files and folders (downloaded / imported).
            var entries = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(DownloadsDir)) if (!Path.GetFileName(f).StartsWith(".") && IsModelFile(f)) entries.Add(f);
                foreach (var d in Directory.GetDirectories(DownloadsDir)) if (!Path.GetFileName(d).StartsWith(".")) entries.Add(d);
            }
            catch (System.Exception) { }
            entries.Sort((a, b) => LastWrite(b).CompareTo(LastWrite(a)));
            var l1 = AtlasTheme.Label(libList, "MODELOS GUARDADOS (" + entries.Count + ")", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Le(l1.gameObject, 26f);
            int shown = 0;
            foreach (var e0 in entries)
            {
                string e = e0;
                string nm = Path.GetFileName(e);
                if (libFilter != "" && !nm.ToLowerInvariant().Contains(libFilter)) continue;
                bool dir = Directory.Exists(e);
                long kb = (dir ? FolderSize(e) : new FileInfo(e).Length) / 1024;
                string info = (dir ? Lang.T("carpeta") + " · " : Path.GetExtension(e).TrimStart('.').ToUpperInvariant() + " · ") + (kb > 1024 ? (kb / 1024f).ToString("0.0") + " MB" : kb + " KB");
                LibraryRow(OutlinerIcon("model"), nm, info, () => UseLibraryFile(e), () =>
                {
                    try { if (dir) Directory.Delete(e, true); else File.Delete(e); } catch (System.Exception) { }
                    FillLibrary();
                });
                shown++;
            }
            if (shown == 0) Empty(entries.Count == 0 ? "Todavía no hay modelos guardados. Importa uno o descárgalo y aparecerá aquí." : "Nada coincide con la búsqueda.");

            // «Mis modelos»: objects saved with their materials and pose.
            var mine = new List<string>();
            try { foreach (var d in Directory.GetDirectories(LibraryDir)) if (File.Exists(Path.Combine(d, "objeto.json"))) mine.Add(d); }
            catch (System.Exception) { }
            mine.Sort();
            var l2 = AtlasTheme.Label(libList, "MIS MODELOS, CON SUS MATERIALES Y POSE (" + mine.Count + ")", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Le(l2.gameObject, 30f);
            shown = 0;
            foreach (var d0 in mine)
            {
                string d = d0;
                string nm = Path.GetFileName(d);
                if (libFilter != "" && !nm.ToLowerInvariant().Contains(libFilter)) continue;
                long kb = FolderSize(d) / 1024;
                LibraryRow("person", nm, kb > 1024 ? (kb / 1024f).ToString("0.0") + " MB" : kb + " KB", () => UseMyModel(d), () =>
                {
                    try { Directory.Delete(d, true); } catch (System.Exception) { }
                    FillLibrary();
                });
                shown++;
            }
            if (shown == 0) Empty(mine.Count == 0 ? "Selecciona un objeto en un proyecto y pulsa «Guardar en Mis modelos»." : "Nada coincide con la búsqueda.");
        }

        static System.DateTime LastWrite(string p)
        {
            try { return Directory.Exists(p) ? Directory.GetLastWriteTimeUtc(p) : File.GetLastWriteTimeUtc(p); }
            catch (System.Exception) { return System.DateTime.MinValue; }
        }

        void Empty(string text)
        {
            var e = AtlasTheme.Label(libList, text, 14, AtlasTheme.TextDim);
            AtlasTheme.Le(e.gameObject, 24f);
        }

        void LibraryRow(string icon, string name, string info, UnityEngine.Events.UnityAction use, System.Action delete)
        {
            var row = AtlasTheme.Box(libList, "Modelo", AtlasTheme.Panel3, 10);
            AtlasTheme.Le(row.gameObject, 54f);
            var ic = AtlasTheme.IconImage(row.transform, icon, 24f, AtlasTheme.AccentLight);
            AtlasTheme.Pin(ic.rectTransform, 0f, 0.5f, new Vector2(14f, 0f), new Vector2(24f, 24f));
            ic.rectTransform.pivot = new Vector2(0f, 0.5f);
            var n = AtlasTheme.Label(row.transform, name, 15, AtlasTheme.TextColor, true);
            AtlasTheme.Fit(n, 10);
            AtlasTheme.Fill(n.rectTransform, 48f, 170f, 4f, 24f);
            var i = AtlasTheme.Label(row.transform, info, 12, AtlasTheme.TextDim);
            AtlasTheme.Fit(i, 9);
            AtlasTheme.Fill(i.rectTransform, 48f, 170f, 28f, 4f);
            var u = AtlasTheme.TextButton(row.transform, "add", "Usar", use, true, 38f, 14, 100f);
            AtlasTheme.Pin((RectTransform)u.transform, 1f, 0.5f, new Vector2(-54f, 0f), new Vector2(100f, 38f));
            ((RectTransform)u.transform).pivot = new Vector2(1f, 0.5f);
            Button del = null;
            del = AtlasTheme.IconButton(row.transform, "delete", () =>
            {
                AtlasTheme.Dialog(canvasRoot, "Borrar de la biblioteca", "¿Borrar «" + name + "»? No se puede deshacer.", null, "Borrar", true, _ => delete());
                toastHolder.SetAsLastSibling();
            }, 38f, 18f);
            AtlasTheme.Pin((RectTransform)del.transform, 1f, 0.5f, new Vector2(-8f, 0f), new Vector2(38f, 38f));
            ((RectTransform)del.transform).pivot = new Vector2(1f, 0.5f);
        }

        // ------------------------------------------------------------------
        // Using

        /// A project to put it in: the open one, or a new one from the start screen.
        void EnsureProjectFor(string name)
        {
            if (store != null && !home.activeSelf) return;
            string nm = SceneStore.CleanName(name);
            if (string.IsNullOrEmpty(nm)) nm = "Modelo";
            if (SceneStore.Exists(nm)) nm = SceneStore.UniqueName(nm);
            skipStarterCube = true;
            OpenProject(nm);
            skipStarterCube = false;
        }

        void UseLibraryFile(string path)
        {
            if (libWindow != null) Destroy(libWindow);
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                if (files.Length == 0) return;
                // The main model first, then its companions.
                System.Array.Sort(files, (a, b) => (IsModelFile(b) ? 1 : 0).CompareTo(IsModelFile(a) ? 1 : 0));
                EnsureProjectFor(Path.GetFileName(path));
                ImportFiles(files, null);
                return;
            }
            if (!File.Exists(path)) { Toast("Ese archivo ya no está en la biblioteca", true); return; }
            EnsureProjectFor(Path.GetFileNameWithoutExtension(path));
            ImportFiles(new[] { path }, null);
        }

        void UseMyModel(string dir)
        {
            if (libWindow != null) Destroy(libWindow);
            EnsureProjectFor(Path.GetFileName(dir));
            AddFromLibrary(dir);
        }

        /// Files from the phone / PC straight into the library (not into a project).
        void AddFilesToLibrary()
        {
            if (NativeFilePicker.IsFilePickerBusy()) return;
            string lib = DownloadsDir;
            NativeFilePicker.PickMultipleFiles(paths =>
            {
                if (paths == null || paths.Length == 0) return;
                try
                {
                    // Each model file on its own; companions (textures, .mtl, .bin)
                    // with the model when there is just one.
                    var models = System.Array.FindAll(paths, IsModelFile);
                    if (models.Length == 0) { Toast("Elige al menos un modelo (GLB, FBX, OBJ, Prisma, MDL…)", true); return; }
                    if (models.Length == 1) KeepInLibrary(paths, lib);
                    else foreach (var m in models) KeepInLibrary(new[] { m }, lib);
                    Toast("Guardado en la biblioteca");
                }
                catch (System.Exception e) { Toast("No se pudo guardar: " + e.Message, true); }
                FillLibrary();
            }, new string[] { "*/*" });
        }
    }
}
