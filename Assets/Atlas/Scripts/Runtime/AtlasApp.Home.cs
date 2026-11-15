// ATLAS 3D (Unity) - home screen, like the Godot version: the ATLAS logo,
// «Nuevo proyecto», and the recent projects as cards with a picture of the
// scene (open, rename, duplicate, delete).
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        GameObject home;
        RectTransform homeSafe, homeContent;
        readonly List<Texture2D> homeThumbs = new List<Texture2D>();
        static readonly float[] CardHues = { 0.78f, 0.6f, 0.52f, 0.08f, 0.92f, 0.36f, 0.68f, 0f };

        void BuildHome()
        {
            var bg = AtlasTheme.Box(canvasRoot, "Inicio", AtlasTheme.Bg);
            AtlasTheme.Fill(bg.rectTransform);
            home = bg.gameObject;
            homeSafe = AtlasTheme.Rect(bg.transform, "AreaSegura");
            ApplySafeArea();
            ScrollRect sr;
            homeContent = AtlasTheme.ScrollArea(homeSafe, out sr, 18f, new RectOffset(32, 32, 28, 32));
        }

        void ShowHome(bool save)
        {
            CloseMenus();
            if (store != null && save)
            {
                SaveNow(false);
                SaveThumbnail();
            }
            ClearScene();
            store = null;
            RefreshUndo();
            home.SetActive(true);
            home.transform.SetAsLastSibling();
            toastHolder.SetAsLastSibling();
            cam.enabled = false;
            RefreshHome();
        }

        void OpenProject(string name)
        {
            ClearScene();
            store = new SceneStore(name);
            frame = 0f;
            var saved = store.Load();
            home.SetActive(false);
            cam.enabled = true;
            if (saved != null)
            {
                Restore(saved, true);
                Commit(false);
                Toast("Proyecto «" + name + "»");
            }
            else
            {
                target.position = new Vector3(0f, 0.5f, 0f);
                yaw = 30f;
                pitch = 22f;
                distance = 7f;
                fps = 24;
                endFrame = 120;
                ground.SetActive(true);
                AtlasTheme.SetButtonIcon(eyeBtn, "eye");
                env = new EnvData();
                ApplyEnv();
                UpdateCamera();
                SetFrame(0f);
                if (!skipStarterCube) AddShape("cube");
                Select(null);
                Toast("Toca un objeto para seleccionarlo · un dedo gira la vista · dos dedos: zoom y desplazar");
            }
            RefreshUndo();
            RefreshProps();
        }

        /// Asks for the project's name first (a free one is suggested).
        void NewProject(bool import)
        {
            string suggestion = SceneStore.UniqueName("Proyecto");
            AtlasTheme.Dialog(canvasRoot, "Nuevo proyecto", "¿Cómo se llama? (lo puedes cambiar después)", suggestion, "Crear", false, v =>
            {
                string name = SceneStore.CleanName(string.IsNullOrEmpty(v.Trim()) ? suggestion : v.Trim());
                if (SceneStore.Exists(name)) name = SceneStore.UniqueName(name);
                OpenProject(name);
                if (import) ImportModel();
            });
            toastHolder.SetAsLastSibling();
        }

        /// Small picture of the view for the project's card.
        void SaveThumbnail()
        {
            if (store == null) return;
            try
            {
                var tex = Capture(384, 216, ground.activeSelf);
                File.WriteAllBytes(store.ThumbPath, tex.EncodeToPNG());
                Destroy(tex);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("ATLAS: miniatura no guardada: " + e.Message);
            }
        }

        void RefreshHome()
        {
            AtlasTheme.Clear(homeContent);
            foreach (var t in homeThumbs) if (t != null) Destroy(t);
            homeThumbs.Clear();

            // Header: logo, name and version.
            var head = AtlasTheme.Rect(homeContent, "Cabecera").gameObject;
            AtlasTheme.Row(head, 16f, new RectOffset(0, 0, 0, 0));
            AtlasTheme.Le(head, 84f);
            var logo = AtlasTheme.Rect(head.transform, "Logo").gameObject.AddComponent<RawImage>();
            logo.texture = Resources.Load<Texture2D>("AtlasLogo");
            logo.raycastTarget = false;
            AtlasTheme.Le(logo.gameObject, 80f, 80f);
            var titles = AtlasTheme.Rect(head.transform, "Titulos").gameObject;
            AtlasTheme.Column(titles, 2f, new RectOffset(0, 0, 10, 0), TextAnchor.UpperLeft);
            AtlasTheme.Le(titles, 84f, -1f, 1f);
            var t1 = AtlasTheme.Label(titles.transform, "ATLAS 3D", 32, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t1.gameObject, 40f);
            var t2 = AtlasTheme.Label(titles.transform, "Estudio de creación 3D · versión " + Version, 16, AtlasTheme.TextDim);
            AtlasTheme.Le(t2.gameObject, 24f);

            // Actions
            var acts = AtlasTheme.Rect(homeContent, "Acciones").gameObject;
            AtlasTheme.Row(acts, 10f, new RectOffset(0, 0, 0, 0));
            AtlasTheme.Le(acts, 54f);
            AtlasTheme.TextButton(acts.transform, "add", "Nuevo proyecto", () => NewProject(false), true, 54f, 18);
            AtlasTheme.TextButton(acts.transform, "import", "Importar modelo", () => NewProject(true), false, 54f, 18);
            AtlasTheme.TextButton(acts.transform, "settings", "Preferencias", () => ShowPrefs(0), false, 54f, 18);
            AtlasTheme.TextButton(acts.transform, "info", "Acerca de", ShowAbout, false, 54f, 18);

            var rl = AtlasTheme.Label(homeContent, "PROYECTOS RECIENTES", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(rl.gameObject, 26f);

            List<ProjectInfo> list;
            try { list = SceneStore.ListProjects(); }
            catch (System.Exception e)
            {
                list = new List<ProjectInfo>();
                Toast("No se pudo leer la carpeta de proyectos: " + e.Message, true);
            }
            if (list.Count == 0)
            {
                var empty = AtlasTheme.Label(homeContent, "Todavía no hay proyectos. Crea uno nuevo para empezar.", 17, AtlasTheme.TextDim);
                AtlasTheme.Le(empty.gameObject, 40f);
                return;
            }
            var gridGo = AtlasTheme.Rect(homeContent, "Tarjetas").gameObject;
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(248f, 206f);
            grid.spacing = new Vector2(16f, 16f);
            grid.constraint = GridLayoutGroup.Constraint.Flexible;
            grid.childAlignment = TextAnchor.UpperLeft;
            foreach (var p in list) MakeCard(gridGo.transform, p);
        }

        void MakeCard(Transform parent, ProjectInfo p)
        {
            var card = AtlasTheme.Box(parent, "Tarjeta", AtlasTheme.Panel2, 16);
            var b = card.gameObject.AddComponent<Button>();
            var cols = b.colors;
            cols.pressedColor = new Color(0.8f, 0.8f, 0.85f);
            b.colors = cols;
            string name = p.name;
            b.onClick.AddListener(() => OpenProject(name));

            // Picture (or a colour with the initials)
            var thumbBox = AtlasTheme.Rect(card.transform, "Imagen");
            thumbBox.anchorMin = new Vector2(0f, 1f);
            thumbBox.anchorMax = new Vector2(1f, 1f);
            thumbBox.pivot = new Vector2(0.5f, 1f);
            thumbBox.sizeDelta = new Vector2(-16f, 129f);
            thumbBox.anchoredPosition = new Vector2(0f, -8f);
            Texture2D tex = null;
            if (p.thumbnail != "")
            {
                try
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!tex.LoadImage(File.ReadAllBytes(p.thumbnail))) { Destroy(tex); tex = null; }
                }
                catch { tex = null; }
            }
            if (tex != null)
            {
                homeThumbs.Add(tex);
                var ri = thumbBox.gameObject.AddComponent<RawImage>();
                ri.texture = tex;
                ri.raycastTarget = false;
            }
            else
            {
                int code = 0;
                for (int i = 0; i < name.Length; i++) code += name[i] * (i + 3);
                float hue = CardHues[Mathf.Abs(code) % CardHues.Length];
                var ph = thumbBox.gameObject.AddComponent<Image>();
                ph.sprite = AtlasTheme.Rounded(10);
                ph.type = Image.Type.Sliced;
                ph.color = Color.HSVToRGB(hue, 0.55f, 0.36f);
                ph.raycastTarget = false;
                string initials = "";
                foreach (var w in name.Split(' '))
                    if (initials.Length < 2 && w.Length > 0 && char.IsLetter(w[0])) initials += char.ToUpperInvariant(w[0]);
                var il = AtlasTheme.Label(thumbBox, initials != "" ? initials : "3D", 38, new Color(1f, 1f, 1f, 0.85f), true, TextAnchor.MiddleCenter);
                AtlasTheme.Fill(il.rectTransform);
            }

            var nl = AtlasTheme.Label(card.transform, name, 17, AtlasTheme.TextColor, true);
            nl.horizontalOverflow = HorizontalWrapMode.Wrap;
            nl.verticalOverflow = VerticalWrapMode.Truncate;
            AtlasTheme.Pin(nl.rectTransform, 0f, 0f, new Vector2(12f, 34f), new Vector2(180f, 26f));
            var dl = AtlasTheme.Label(card.transform, PrettyDate(p.modified), 14, AtlasTheme.TextDim);
            AtlasTheme.Pin(dl.rectTransform, 0f, 0f, new Vector2(12f, 10f), new Vector2(180f, 22f));
            var more = AtlasTheme.IconButton(card.transform, "more", () => CardMenu(name), 44f, 22f);
            AtlasTheme.Pin((RectTransform)more.transform, 1f, 0f, new Vector2(-6f, 8f), new Vector2(44f, 44f));
        }

        static string PrettyDate(System.DateTime d)
        {
            var now = System.DateTime.Now;
            string hm = d.ToString("HH:mm");
            if (d.Date == now.Date) return "Hoy, " + hm;
            if (d.Date == now.Date.AddDays(-1)) return "Ayer, " + hm;
            return d.ToString("dd/MM/yyyy") + ", " + hm;
        }

        void CardMenu(string name)
        {
            var dim = AtlasTheme.Box(canvasRoot, "Dialogo", new Color(0f, 0f, 0f, 0.6f));
            AtlasTheme.Fill(dim.rectTransform);
            var go = dim.gameObject;
            var closeB = dim.gameObject.AddComponent<Button>();
            closeB.transition = Selectable.Transition.None;
            closeB.onClick.AddListener(() => Destroy(go));
            var card = AtlasTheme.Box(dim.transform, "Tarjeta", AtlasTheme.Panel2, 18);
            AtlasTheme.Pin(card.rectTransform, 0.5f, 0.5f, Vector2.zero, new Vector2(340f, 0f));
            AtlasTheme.Column(card.gameObject, 8f, new RectOffset(16, 16, 16, 16));
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            var t = AtlasTheme.Label(card.transform, name, 19, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t.gameObject, 30f);
            AtlasTheme.TextButton(card.transform, "folder", "Abrir", () => { Destroy(go); OpenProject(name); }, true, 48f);
            AtlasTheme.TextButton(card.transform, "edit", "Cambiar nombre…", () => { Destroy(go); RenameProject(name); }, false, 48f);
            AtlasTheme.TextButton(card.transform, "copy", "Duplicar", () =>
            {
                Destroy(go);
                try
                {
                    string c = SceneStore.Duplicate(name);
                    Toast("Copia creada: «" + c + "»");
                }
                catch (System.Exception e) { Toast("No se pudo duplicar: " + e.Message, true); }
                RefreshHome();
            }, false, 48f);
            var del = AtlasTheme.TextButton(card.transform, "delete", "Borrar…", () =>
            {
                Destroy(go);
                AtlasTheme.Dialog(canvasRoot, "¿Borrar «" + name + "»?", "Se borra el proyecto con sus modelos y texturas. No se puede deshacer.", null, "Borrar", true, _ =>
                {
                    try
                    {
                        SceneStore.Delete(name);
                        Toast("Proyecto borrado");
                    }
                    catch (System.Exception e) { Toast("No se pudo borrar: " + e.Message, true); }
                    RefreshHome();
                });
            }, false, 48f);
            foreach (var ri in del.GetComponentsInChildren<RawImage>()) ri.color = AtlasTheme.Danger;
            toastHolder.SetAsLastSibling();
        }

        void RenameProject(string name)
        {
            AtlasTheme.Dialog(canvasRoot, "Cambiar nombre", null, name, "Guardar", false, v =>
            {
                if (string.IsNullOrEmpty(v.Trim())) return;
                try
                {
                    string n = SceneStore.Rename(name, v);
                    Toast("Ahora se llama «" + n + "»");
                }
                catch (System.Exception e) { Toast("No se pudo cambiar el nombre: " + e.Message, true); }
                RefreshHome();
            });
            toastHolder.SetAsLastSibling();
        }

        void RenameCurrentProject()
        {
            if (store == null) return;
            string oldName = store.name;
            AtlasTheme.Dialog(canvasRoot, "Renombrar proyecto", null, oldName, "Guardar", false, v =>
            {
                if (string.IsNullOrEmpty(v.Trim()) || store == null) return;
                try
                {
                    SaveNow(false);
                    SaveThumbnail();
                    var keepSel = selected != null ? selected.id : null;
                    string n = SceneStore.Rename(oldName, v);
                    OpenProject(n);
                    foreach (var it in items) if (it != null && it.id == keepSel) Select(it);
                    Toast("Ahora se llama «" + n + "»");
                }
                catch (System.Exception e) { Toast("No se pudo cambiar el nombre: " + e.Message, true); }
            });
            toastHolder.SetAsLastSibling();
        }

        void ShowProjectInfo()
        {
            if (store == null) return;
            int shapes = 0, lights = 0, models = 0, keys = 0;
            foreach (var it in items)
            {
                if (it == null) continue;
                if (it.IsLight) lights++; else if (it.kind == "model") models++; else shapes++;
                keys += it.keys.Count;
            }
            AtlasTheme.Dialog(canvasRoot, store.name,
                "Formas: " + shapes + " · Luces: " + lights + " · Modelos: " + models +
                "\nClaves de animación: " + keys + " · Duración: " + ((float)endFrame / fps).ToString("0.0") + " s a " + fps + " fps" +
                "\nAmbiente: " + Ambience.Find(env.preset).name, null, "Cerrar", false, null);
            toastHolder.SetAsLastSibling();
        }

        void ShowGuide()
        {
            AtlasTheme.Dialog(canvasRoot, "Guía de inicio",
                "• Un dedo gira la vista; dos dedos acercan y desplazan.\n" +
                "• Toca un objeto para seleccionarlo (brilla en naranja).\n" +
                "• Barra izquierda: Seleccionar, Mover, Girar, Escalar; + añade formas, luces y modelos.\n" +
                "• Modelos con huesos: botón del hueso → toca una articulación y gírala.\n" +
                "• Animar: elige un fotograma, coloca el objeto y pulsa la clave amarilla.\n" +
                "• Engranaje: propiedades, materiales y «Ambiente rápido».\n" +
                "• + → Cámara: los renders salen por ella; se puede animar.\n" +
                "• Imán (barra izquierda): mover, girar y escalar a saltos exactos.\n" +
                "• Formas: botones Modelar (vértices y caras, extruir…) y Esculpir (pinceles como arcilla).\n" +
                "• Render: imagen o la animación completa.", null, "Entendido", false, null);
            toastHolder.SetAsLastSibling();
        }

        void ShowAbout()
        {
            AtlasTheme.Dialog(canvasRoot, "ATLAS 3D " + Version,
                "Estudio de creación y animación 3D para móviles.\n\n" +
                "Esta es la nueva versión hecha en Unity: más fluida y con mejor imagen (URP, sombras suaves, efectos de cine). " +
                "Importa GLB, glTF, FBX, OBJ (+MTL), BLEND, Prisma3D, 3DS, STL, PLY y ZIP; exporta GLB, glTF, OBJ, STL, PLY y 3MF.\n\n" +
                "Letra Inter (licencia OFL).", null, "Cerrar", false, null);
            toastHolder.SetAsLastSibling();
        }
    }
}
