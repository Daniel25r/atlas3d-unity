// ATLAS 3D (Unity) - «Descargas y biblioteca»: download a model from a link
// (direct links, Google Drive, Dropbox, the ATLAS Workshop), catch what the
// web browser downloads (PC: the Downloads folder), the files saved so far
// with «Importar», and the ATLAS 3D Workshop's creations to download with
// one tap. Models from the internet: only what you may use.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        const string WorkshopRepo = "Daniel25r/atlas3d-workshop";
        const string WorkshopSite = "https://daniel25r.github.io/atlas3d-workshop/";

        static readonly string[] ModelExts = { ".glb", ".gltf", ".fbx", ".obj", ".3ds", ".blend", ".prisma", ".proj", ".zip", ".mdl", ".gma", ".stl", ".ply" };

        GameObject dlWindow;
        Transform dlSaved, dlWorkshop;
        Text dlStatus;
        bool dlBusy;
        float catchAt;
        readonly Dictionary<string, long> catchSeen = new Dictionary<string, long>();

        static string DownloadsDir
        {
            get
            {
                string d = Path.Combine(Application.persistentDataPath, "Descargas");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        static bool IsModelFile(string path)
        {
            return System.Array.IndexOf(ModelExts, Path.GetExtension(path).ToLowerInvariant()) >= 0;
        }

        // ------------------------------------------------------------------
        // Window

        void ShowDownloads()
        {
            CloseMenus();
            if (dlWindow != null) Destroy(dlWindow);
            var dim = AtlasTheme.Box(canvasRoot, "Dialogo", new Color(0f, 0f, 0f, 0.55f));
            AtlasTheme.Fill(dim.rectTransform);
            dlWindow = dim.gameObject;
            var closeBg = dim.gameObject.AddComponent<Button>();
            closeBg.transition = Selectable.Transition.None;
            closeBg.onClick.AddListener(() => Destroy(dlWindow));
            var card = AtlasTheme.Box(dim.transform, "Ventana", AtlasTheme.Panel2, 18);
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            var cr = card.rectTransform;
            cr.anchorMin = new Vector2(0.5f, 0.05f);
            cr.anchorMax = new Vector2(0.5f, 0.95f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            float w = Mathf.Min(760f, canvasRoot.rect.width - 24f);
            cr.sizeDelta = new Vector2(w, 0f);
            cr.anchoredPosition = Vector2.zero;
            var title = AtlasTheme.Label(card.transform, "Descargas y biblioteca", 20, AtlasTheme.TextColor, true, TextAnchor.MiddleCenter);
            AtlasTheme.Pin(title.rectTransform, 0.5f, 1f, new Vector2(0f, -26f), new Vector2(420f, 34f));
            title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var x = AtlasTheme.IconButton(card.transform, "close", () => Destroy(dlWindow), 44f, 22f);
            AtlasTheme.Pin((RectTransform)x.transform, 1f, 1f, new Vector2(-8f, -6f), new Vector2(44f, 44f));
            ((RectTransform)x.transform).pivot = new Vector2(1f, 1f);
            var body = AtlasTheme.Rect(card.transform, "Cuerpo");
            AtlasTheme.Fill(body, 0f, 0f, 56f, 10f);
            ScrollRect sr;
            var c = AtlasTheme.ScrollArea(body, out sr, 8f, new RectOffset(18, 18, 0, 12));
            float iw = w - 36f;

            // From a link
            var l1 = AtlasTheme.Label(c, "DESCARGAR DESDE UN ENLACE", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Le(l1.gameObject, 22f);
            var hint = AtlasTheme.Label(c, "Pega un enlace directo a un modelo (GLB, FBX, ZIP, MDL, GMA, Prisma…) o de Google Drive, Dropbox o el Workshop de ATLAS.", 14, AtlasTheme.TextDim);
            AtlasTheme.Le(hint.gameObject, 40f);
            var row = AtlasTheme.Rect(c, "Enlace").gameObject;
            AtlasTheme.Row(row, 8f, new RectOffset(0, 0, 0, 0));
            AtlasTheme.Le(row, 46f);
            var field = AtlasTheme.TextField(row.transform, "", null, 46f);
            var ph = AtlasTheme.Label(field.transform, "https://…", 15, AtlasTheme.TextDim);
            AtlasTheme.Fill(ph.rectTransform, 12f, 12f);
            field.placeholder = ph;
            AtlasTheme.Le(field.gameObject, 46f, -1f, 1f);
            var dlb = AtlasTheme.TextButton(row.transform, "import", "Descargar", () => StartDownload(field.text, false), true, 46f, 16, 150f);
            dlStatus = AtlasTheme.Label(c, "", 14, AtlasTheme.AccentLight);
            AtlasTheme.Le(dlStatus.gameObject, 22f);

            // Browser downloads (PC)
            if (Desktop)
            {
                var l2 = AtlasTheme.Label(c, "ATRAPAR DESCARGAS DEL NAVEGADOR", 14, AtlasTheme.TextDim, true);
                AtlasTheme.Le(l2.gameObject, 22f);
                AtlasTheme.Switch(c, "Traer aquí los modelos que descargue el navegador (carpeta Descargas)", Prefs.catchDownloads, on =>
                {
                    Prefs.catchDownloads = on;
                    Prefs.Save();
                    catchSeen.Clear();
                    if (on) ScanBrowserDownloads(true);
                });
                var bg = AtlasTheme.Grid(c, 3, iw, 40f);
                AtlasTheme.TextButton(bg, "image", "Sketchfab", () => Application.OpenURL("https://sketchfab.com/features/free-3d-models"), false, 40f, 14);
                AtlasTheme.TextButton(bg, "image", "Workshop web", () => Application.OpenURL(WorkshopSite), false, 40f, 14);
                AtlasTheme.TextButton(bg, "folder", "Abrir carpeta", () => Application.OpenURL("file://" + DownloadsDir), false, 40f, 14);
            }

            // Saved files
            var l3 = AtlasTheme.Label(c, "ARCHIVOS GUARDADOS", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Le(l3.gameObject, 22f);
            dlSaved = AtlasTheme.Rect(c, "Guardados");
            AtlasTheme.Column(dlSaved.gameObject, 4f, new RectOffset(0, 0, 0, 0));
            FillSaved();

            // Workshop
            var l4 = AtlasTheme.Label(c, "WORKSHOP DE ATLAS 3D", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Le(l4.gameObject, 22f);
            dlWorkshop = AtlasTheme.Rect(c, "Workshop");
            AtlasTheme.Column(dlWorkshop.gameObject, 6f, new RectOffset(0, 0, 0, 0));
            var wl = AtlasTheme.Label(dlWorkshop, "Cargando creaciones…", 14, AtlasTheme.TextDim);
            AtlasTheme.Le(wl.gameObject, 24f);
            StartCoroutine(LoadWorkshop(iw));
            var up = AtlasTheme.TextButton(c, "export", "Subir mi creación al Workshop…", () => Application.OpenURL("https://github.com/" + WorkshopRepo + "/issues/new?template=subir.yml"), false, 42f, 15);
            toastHolder.SetAsLastSibling();
        }

        void FillSaved()
        {
            if (dlSaved == null) return;
            AtlasTheme.Clear(dlSaved);
            string[] files;
            try { files = System.Array.FindAll(Directory.GetFiles(DownloadsDir), p => !Path.GetFileName(p).StartsWith(".")); }
            catch (System.Exception) { files = new string[0]; }
            System.Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            if (files.Length == 0)
            {
                var e = AtlasTheme.Label(dlSaved, "Todavía no hay nada descargado.", 14, AtlasTheme.TextDim);
                AtlasTheme.Le(e.gameObject, 24f);
                return;
            }
            foreach (var f0 in files)
            {
                string f = f0;
                var row = AtlasTheme.Box(dlSaved, "Archivo", AtlasTheme.Panel3, 10);
                AtlasTheme.Le(row.gameObject, 46f);
                long kb = new FileInfo(f).Length / 1024;
                var name = AtlasTheme.Label(row.transform, Path.GetFileName(f) + "   ·   " + (kb > 1024 ? (kb / 1024f).ToString("0.0") + " MB" : kb + " KB"), 14, AtlasTheme.TextColor);
                AtlasTheme.Fit(name, 10);
                AtlasTheme.Fill(name.rectTransform, 12f, 220f, 0f, 0f);
                var imp = AtlasTheme.TextButton(row.transform, "import", "Importar", () => ImportDownloaded(f), true, 36f, 14, 110f);
                AtlasTheme.Pin((RectTransform)imp.transform, 1f, 0.5f, new Vector2(-100f, 0f), new Vector2(110f, 36f));
                ((RectTransform)imp.transform).pivot = new Vector2(1f, 0.5f);
                var del = AtlasTheme.IconButton(row.transform, "delete", () =>
                {
                    try { File.Delete(f); } catch (System.Exception) { }
                    FillSaved();
                }, 36f, 18f);
                AtlasTheme.Pin((RectTransform)del.transform, 1f, 0.5f, new Vector2(-8f, 0f), new Vector2(36f, 36f));
                ((RectTransform)del.transform).pivot = new Vector2(1f, 0.5f);
            }
        }

        void ImportDownloaded(string path)
        {
            if (!File.Exists(path)) { FillSaved(); return; }
            if (dlWindow != null) Destroy(dlWindow);
            if (store == null || home.activeSelf)
            {
                string nm = SceneStore.CleanName(Path.GetFileNameWithoutExtension(path));
                if (string.IsNullOrEmpty(nm)) nm = "Modelo";
                if (SceneStore.Exists(nm)) nm = SceneStore.UniqueName(nm);
                skipStarterCube = true;
                OpenProject(nm);
                skipStarterCube = false;
            }
            ImportFiles(new[] { path }, null);
        }

        // ------------------------------------------------------------------
        // Downloading

        /// Share links of the usual hosts → the file itself.
        static string DirectLink(string url)
        {
            url = (url ?? "").Trim();
            if (url.Contains("drive.google.com"))
            {
                string id = "";
                int i = url.IndexOf("/d/");
                if (i >= 0) { id = url.Substring(i + 3); int e = id.IndexOf('/'); if (e >= 0) id = id.Substring(0, e); }
                else
                {
                    i = url.IndexOf("id=");
                    if (i >= 0) { id = url.Substring(i + 3); int e = id.IndexOf('&'); if (e >= 0) id = id.Substring(0, e); }
                }
                if (id != "") return "https://drive.google.com/uc?export=download&confirm=t&id=" + id;
            }
            if (url.Contains("dropbox.com")) return url.Replace("?dl=0", "?dl=1").Replace("&dl=0", "&dl=1") + (url.Contains("dl=") ? "" : (url.Contains("?") ? "&dl=1" : "?dl=1"));
            if (url.Contains("github.com") && url.Contains("/blob/")) return url.Replace("github.com", "raw.githubusercontent.com").Replace("/blob/", "/");
            return url;
        }

        void StartDownload(string url, bool importAfter)
        {
            if (dlBusy) { Toast("Ya hay una descarga en curso"); return; }
            url = (url ?? "").Trim();
            if (!url.StartsWith("https://") && !url.StartsWith("http://")) { Toast("Pega un enlace que empiece por https://", true); return; }
            if (url.Contains("steamcommunity.com")) { Toast("Los enlaces de Steam Workshop no se pueden descargar directamente: descarga el archivo con un «workshop downloader» y pega aquí su enlace o impórtalo.", true); return; }
            if (url.Contains("mediafire.com/file/")) { Toast("Mediafire: abre el enlace en el navegador y copia el enlace del botón «Download».", true); }
            StartCoroutine(Download(DirectLink(url), importAfter));
        }

        IEnumerator Download(string url, bool importAfter)
        {
            dlBusy = true;
            string tmp = Path.Combine(DownloadsDir, ".descargando");
            using (var req = UnityWebRequest.Get(url))
            {
                req.downloadHandler = new DownloadHandlerFile(tmp) { removeFileOnAbort = true };
                req.timeout = 600;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (dlStatus != null) Lang.Set(dlStatus, "Descargando… " + Mathf.RoundToInt(req.downloadProgress * 100f) + "% · " + (req.downloadedBytes / 1024) + " KB");
                    yield return null;
                }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    dlBusy = false;
                    if (dlStatus != null) Lang.Set(dlStatus, "");
                    Toast("No se pudo descargar: " + req.error, true);
                    yield break;
                }
                string name = FileNameOf(req, url, tmp);
                string dest = Path.Combine(DownloadsDir, name);
                int n = 2;
                while (File.Exists(dest)) dest = Path.Combine(DownloadsDir, Path.GetFileNameWithoutExtension(name) + "_" + n++ + Path.GetExtension(name));
                try { File.Move(tmp, dest); }
                catch (System.Exception e) { dlBusy = false; Toast("No se pudo guardar: " + e.Message, true); yield break; }
                dlBusy = false;
                if (dlStatus != null) Lang.Set(dlStatus, "Guardado: " + Path.GetFileName(dest));
                FillSaved();
                if (!IsModelFile(dest))
                {
                    Toast("Descargado, pero no parece un modelo (" + Path.GetExtension(dest) + "). Si es una página web, el enlace no era directo.", true);
                    yield break;
                }
                if (importAfter) ImportDownloaded(dest);
                else Toast("Descargado: pulsa «Importar» en Archivos guardados");
            }
        }

        /// The file's name: from the server, the link or its first bytes.
        static string FileNameOf(UnityWebRequest req, string url, string tmp)
        {
            string name = "";
            string cd = req.GetResponseHeader("Content-Disposition") ?? "";
            int i = cd.IndexOf("filename=");
            if (i >= 0)
            {
                name = cd.Substring(i + 9).Trim().Trim('"', '\'', ';');
                int sc = name.IndexOf(';');
                if (sc >= 0) name = name.Substring(0, sc).Trim('"');
            }
            if (name == "")
            {
                string p = url;
                int q = p.IndexOf('?');
                if (q >= 0) p = p.Substring(0, q);
                name = System.Uri.UnescapeDataString(Path.GetFileName(p.TrimEnd('/')));
            }
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (string.IsNullOrEmpty(name)) name = "descarga";
            if (!IsModelFile(name))
            {
                string sniff = MeshFormats.Sniff(tmp);
                if (!string.IsNullOrEmpty(sniff)) name = Path.GetFileNameWithoutExtension(name) + sniff;
            }
            return name;
        }

        // ------------------------------------------------------------------
        // Catching the browser's downloads (PC)

        static string BrowserDownloads()
        {
            try
            {
                string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                string d = Path.Combine(home, "Downloads");
                if (Directory.Exists(d)) return d;
                d = Path.Combine(home, "Descargas");
                if (Directory.Exists(d)) return d;
            }
            catch (System.Exception) { }
            return null;
        }

        /// Every few seconds: new model files in the browser's Downloads
        /// folder are copied to ATLAS («atrapados»). `quiet`: only learn
        /// what is there now.
        void ScanBrowserDownloads(bool quiet)
        {
            if (!Desktop || !Prefs.catchDownloads) return;
            string dir = BrowserDownloads();
            if (dir == null) return;
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (System.Exception) { return; }
            bool first = catchSeen.Count == 0;
            foreach (var f in files)
            {
                if (!IsModelFile(f)) continue;
                long len;
                try { len = new FileInfo(f).Length; } catch (System.Exception) { continue; }
                long before;
                bool known = catchSeen.TryGetValue(f, out before);
                catchSeen[f] = len;
                if (quiet || first) continue;
                if (known && before == len) continue;
                if (!known) continue;   // still being written: next time
                try
                {
                    string dest = Path.Combine(DownloadsDir, Path.GetFileName(f));
                    File.Copy(f, dest, true);
                    catchSeen[f] = -1;   // done; a new file with this name is caught again
                    Toast("Atrapado del navegador: " + Path.GetFileName(f) + " (Descargas y biblioteca → Importar)");
                    FillSaved();
                }
                catch (System.Exception) { }
            }
        }

        void UpdateCatch()
        {
            if (!Desktop || !Prefs.catchDownloads || Time.unscaledTime < catchAt) return;
            catchAt = Time.unscaledTime + 2.5f;
            ScanBrowserDownloads(false);
        }

        // ------------------------------------------------------------------
        // Workshop creations (approved issues of the site's repository)

        IEnumerator LoadWorkshop(float width)
        {
            var list = new List<Dictionary<string, string>>();
            using (var req = UnityWebRequest.Get("https://api.github.com/repos/" + WorkshopRepo + "/issues?labels=aprobado&state=all&per_page=60"))
            {
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.timeout = 20;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) ParseIssues(req.downloadHandler.text, list);
            }
            if (dlWorkshop == null) yield break;
            AtlasTheme.Clear(dlWorkshop);
            if (list.Count == 0)
            {
                var e = AtlasTheme.Label(dlWorkshop, "Todavía no hay creaciones publicadas (o no hay internet). ¡Sube la primera!", 14, AtlasTheme.TextDim);
                AtlasTheme.Le(e.gameObject, 40f);
                yield break;
            }
            var grid = AtlasTheme.Grid(dlWorkshop, 2, width, 210f, 8f);
            foreach (var it0 in list)
            {
                var it = it0;
                var card = AtlasTheme.Box(grid, "Creacion", AtlasTheme.Panel3, 12);
                var img = AtlasTheme.Rect(card.transform, "Imagen").gameObject.AddComponent<RawImage>();
                var ir = (RectTransform)img.transform;
                ir.anchorMin = new Vector2(0f, 1f); ir.anchorMax = new Vector2(1f, 1f); ir.pivot = new Vector2(0.5f, 1f);
                ir.sizeDelta = new Vector2(-12f, 110f); ir.anchoredPosition = new Vector2(0f, -6f);
                img.color = AtlasTheme.Panel2;
                var t = AtlasTheme.Label(card.transform, it["title"], 15, AtlasTheme.TextColor, true);
                AtlasTheme.Pin(t.rectTransform, 0f, 1f, new Vector2(10f, -122f), new Vector2(width * 0.5f - 30f, 22f));
                var by = AtlasTheme.Label(card.transform, it["type"] + " · " + it["author"] + "  ♥ " + it["likes"], 13, AtlasTheme.TextDim);
                AtlasTheme.Pin(by.rectTransform, 0f, 1f, new Vector2(10f, -144f), new Vector2(width * 0.5f - 30f, 20f));
                string file = it["file"];
                if (file != "")
                {
                    var b = AtlasTheme.TextButton(card.transform, "import", "Descargar e importar", () => StartDownload(file, true), true, 34f, 14);
                    AtlasTheme.Pin((RectTransform)b.transform, 0f, 0f, new Vector2(8f, 8f), new Vector2(width * 0.5f - 28f, 34f));
                }
                if (it["image"] != "") StartCoroutine(LoadThumb(it["image"], img));
            }
        }

        IEnumerator LoadThumb(string url, RawImage img)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 20;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success || img == null) yield break;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(req.downloadHandler.data)) { Destroy(tex); yield break; }
                img.texture = tex;
                img.color = Color.white;
                var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
                fit.aspectRatio = tex.width / (float)Mathf.Max(1, tex.height);
            }
        }

        /// The fields of the «Subir creación» form in each issue.
        static void ParseIssues(string json, List<Dictionary<string, string>> list)
        {
            var arr = MiniJson.Parse(json) as List<object>;
            if (arr == null) return;
            foreach (var o in arr)
            {
                var is0 = o as Dictionary<string, object>;
                if (is0 == null || is0.ContainsKey("pull_request")) continue;
                string body = is0.ContainsKey("body") ? is0["body"] as string ?? "" : "";
                var f = Form(body);
                string title;
                if (!f.TryGetValue("Nombre", out title) || title == "") title = (is0["title"] as string ?? "").Replace("[Creación]", "").Trim();
                var user = is0.ContainsKey("user") ? is0["user"] as Dictionary<string, object> : null;
                var reactions = is0.ContainsKey("reactions") ? is0["reactions"] as Dictionary<string, object> : null;
                double likes = 0;
                if (reactions != null)
                {
                    if (reactions.ContainsKey("+1") && reactions["+1"] is double) likes += (double)reactions["+1"];
                    if (reactions.ContainsKey("heart") && reactions["heart"] is double) likes += (double)reactions["heart"];
                }
                string v;
                list.Add(new Dictionary<string, string>
                {
                    { "title", title },
                    { "type", f.TryGetValue("Tipo", out v) && v != "" ? v : "Modelo" },
                    { "author", f.TryGetValue("Autor", out v) && v != "" ? v : (user != null ? user["login"] as string ?? "" : "") },
                    { "image", FirstUrl(f.TryGetValue("Imagen", out v) ? v : body, true) },
                    { "file", FirstUrl(f.TryGetValue("Archivo", out v) ? v : "", false) },
                    { "likes", ((int)likes).ToString() },
                });
            }
        }

        static Dictionary<string, string> Form(string body)
        {
            var d = new Dictionary<string, string>();
            var parts = body.Replace("\r", "").Split(new[] { "### " }, System.StringSplitOptions.None);
            for (int i = 1; i < parts.Length; i++)
            {
                int nl = parts[i].IndexOf('\n');
                if (nl < 0) continue;
                string val = parts[i].Substring(nl + 1).Trim();
                if (val == "_No response_") val = "";
                d[parts[i].Substring(0, nl).Trim()] = val;
            }
            return d;
        }

        static string FirstUrl(string t, bool image)
        {
            if (string.IsNullOrEmpty(t)) return "";
            int i = 0;
            while ((i = t.IndexOf("https://", i)) >= 0)
            {
                int e = i;
                while (e < t.Length && !char.IsWhiteSpace(t[e]) && t[e] != ')' && t[e] != '"' && t[e] != '>' && t[e] != ']') e++;
                string u = t.Substring(i, e - i);
                if (!image || u.Contains("user-images") || u.Contains("githubusercontent") || u.Contains("/assets/") || u.EndsWith(".png") || u.EndsWith(".jpg") || u.EndsWith(".jpeg") || u.EndsWith(".webp")) return u;
                i = e;
            }
            return "";
        }
    }
}
