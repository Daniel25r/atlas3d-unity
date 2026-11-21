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
            ApplyHomeBackground();
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
            AtlasTheme.Le(head, 112f);
            var logo = AtlasTheme.Rect(head.transform, "Logo").gameObject.AddComponent<RawImage>();
            logo.texture = LogoTexture(logo);
            logo.raycastTarget = false;
            AtlasTheme.Le(logo.gameObject, 80f, 80f);
            var titles = AtlasTheme.Rect(head.transform, "Titulos").gameObject;
            AtlasTheme.Column(titles, 2f, new RectOffset(0, 0, 10, 0), TextAnchor.UpperLeft);
            AtlasTheme.Le(titles, 112f, -1f, 1f);
            var t1 = AtlasTheme.Label(titles.transform, "ATLAS 5D", 32, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t1.gameObject, 40f);
            var t2 = AtlasTheme.Label(titles.transform, "Estudio de creación 3D · versión " + Version, 16, AtlasTheme.TextDim);
            AtlasTheme.Le(t2.gameObject, 24f);
            Build3D2DSwitch(titles.transform);

            // Actions
            var acts = AtlasTheme.Rect(homeContent, "Acciones").gameObject;
            AtlasTheme.Row(acts, 10f, new RectOffset(0, 0, 0, 0));
            AtlasTheme.Le(acts, 54f);
            AtlasTheme.TextButton(acts.transform, "add", "Nuevo proyecto", () => NewProject(false), true, 54f, 18);
            AtlasTheme.TextButton(acts.transform, "import", "Importar modelo", () => NewProject(true), false, 54f, 18);
            AtlasTheme.TextButton(acts.transform, "folder", "Biblioteca", ShowLibrary, false, 54f, 18);
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
            // Search and order (with many projects).
            var bar = AtlasTheme.Rect(homeContent, "Buscar");
            AtlasTheme.Le(bar.gameObject, 44f);
            AtlasTheme.Row(bar.gameObject, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft);
            var search = AtlasTheme.TextField(bar, homeFilter, null, 44f);
            AtlasTheme.Le(search.gameObject, 44f, 300f);
            var ph = AtlasTheme.Label(search.transform, "Buscar proyectos…", 16, AtlasTheme.TextDim, false);
            AtlasTheme.Fill(ph.rectTransform, 12f, 12f);
            search.placeholder = ph;
            string[] orders = { "Recientes", "Nombre", "Antiguos" };
            var orderBtns = new List<Button>();
            for (int i = 0; i < orders.Length; i++)
            {
                int k = i;
                var ch = AtlasTheme.Chip(bar, orders[i], () =>
                {
                    homeOrder = k;
                    for (int j = 0; j < orderBtns.Count; j++) AtlasTheme.SetActive(orderBtns[j], j == homeOrder);
                    FillProjectCards(list);
                }, homeOrder == i);
                AtlasTheme.Le(ch.gameObject, 40f, 110f);
                orderBtns.Add(ch);
            }
            var gridGo = AtlasTheme.Rect(homeContent, "Tarjetas").gameObject;
            var grid = gridGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(248f, 206f);
            grid.spacing = new Vector2(16f, 16f);
            grid.constraint = GridLayoutGroup.Constraint.Flexible;
            grid.childAlignment = TextAnchor.UpperLeft;
            homeGrid = gridGo.transform;
            search.onValueChanged.AddListener(v => { homeFilter = v; FillProjectCards(list); });
            FillProjectCards(list);
        }

        static string homeFilter = "";
        static int homeOrder;
        Transform homeGrid;

        void FillProjectCards(List<ProjectInfo> all)
        {
            if (homeGrid == null) return;
            AtlasTheme.Clear(homeGrid);
            var l = new List<ProjectInfo>();
            string f = (homeFilter ?? "").Trim().ToLowerInvariant();
            foreach (var p in all) if (f == "" || p.name.ToLowerInvariant().Contains(f)) l.Add(p);
            if (homeOrder == 1) l.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.CurrentCultureIgnoreCase));
            else if (homeOrder == 2) l.Sort((a, b) => a.modified.CompareTo(b.modified));
            else l.Sort((a, b) => b.modified.CompareTo(a.modified));
            foreach (var p in l) MakeCard(homeGrid, p);
            if (l.Count == 0)
            {
                var none = AtlasTheme.Label(homeGrid, "Ningún proyecto se llama así", 16, AtlasTheme.TextDim);
                none.rectTransform.sizeDelta = new Vector2(248f, 40f);
            }
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
            AtlasTheme.Fit(nl, 12);
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
            AtlasTheme.Fit(t, 12);
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

        const string WhatsNew =
            "• NUEVO · Animaciones del modelo «Pasar a claves»: la animación que trae un modelo se convierte en claves de los huesos para retocarla en modo pose, mezclarla con lo tuyo o cambiar su ritmo.\n" +
            "• NUEVO · «Aspecto realista» (Escena → Estilo de la imagen): con un toque, color de cine, sombras en rincones, sombras activadas, brillo natural de los modelos y sin estilo dibujo.\n" +
            "• NUEVO · Curva (Añadir → Curva): un tubo que pasa suave por varios puntos. Mueve cada punto con el manipulador, añade o quita puntos y elige grosor, lados, cerrada (aro) y puntas finas. Para cables, colas, tubos, cuerdas o letras.\n" +
            "• NUEVO · Shading (editor de materiales → Texturas generadas): degradado, ruido, nubes, cuadros, rayas, puntos, madera, mármol, ladrillos y baldosas en los dos colores que elijas, sin cortes al repetirse. Se pueden usar como textura, como relieve o como brillo propio, y mezclar la textura con otra imagen usando el patrón de máscara.\n" +
            "• NUEVO · Copiar y pegar materiales entre objetos y piezas.\n" +
            "• Arreglado: el render «Cine» a veces salía lleno de ruido de colores en algunos teléfonos (gráficos Mali). Ahora la imagen se lee de forma segura y una pasada corrupta se descarta.\n" +
            "• El desenfoque de movimiento solo se aplica en el render: en la vista del editor emborronaba todo al girar la cámara.\n" +
            "• NUEVO · Exportar MP4 también en PC, con todos los sonidos mezclados (antes daba un ZIP de imágenes).\n" +
            "• NUEVO · Animar los efectos de imagen: pon claves de exposición, color, resplandor, desenfoque, estilo, pixelado… y cambian durante la animación (Escena → Estilo de la imagen → Animar los efectos).\n" +
            "• NUEVO · Quitar huesos: el que elijas o todos los de un modelo, para que no estorben (se recuperan cuando quieras). «Mostrar todos los huesos» ahora enseña también puntas y extremos que antes no salían.\n" +
            "• Arreglado: con el modelo girado, el manipulador de los huesos sigue los ejes del modelo.\n" +
            "• Arreglado: extruir un plano lo convierte en un sólido cerrado (antes quedaba abierto por debajo y se veía raro).\n" +
            "• Arreglado: el relieve (normal maps) se ve también con el estilo dibujo (anime).\n" +
            "• NUEVO · Estilo de la imagen (Escena → Estilo de la imagen): Spider-Verse, anime, cómic, contorno de tinta, póster, pintura, boceto, retro y VHS, con su intensidad. Encima: pixelado, gran angular, lente sucia, polvo en el aire y película antigua. Más etalonaje (cálido, frío, cine, blanco y negro, dramático, sueño) y mapeo de tonos.\n" +
            "• NUEVO · Versiones anteriores (menú → Versiones anteriores): una copia del proyecto cada pocos minutos para volver atrás cuando quieras.\n" +
            "• NUEVO · Informar de un error dentro de la app, con etiquetas, los datos del teléfono y una captura de la pantalla (menú → Informar de un error).\n" +
            "• Inicio: buscador de proyectos y orden por recientes, nombre o antiguos.\n" +
            "• NUEVO · Editor de curvas (como el de Blender): la línea de cada eje sobre el tiempo; toca entre dos claves y dale forma al movimiento con dos tiradores, o elige Suave, Acelerar, Frenar, Pasarse, Rebote, Muelle… (línea de tiempo → icono de curva).\n" +
            "• NUEVO · Estirar claves en el tiempo: haz una parte de la animación más lenta o más rápida de golpe (200 % = el doble de lenta).\n" +
            "• NUEVO · Modelar: «Rellenar agujero» tapa un hueco entero con una cara y «Puente» une dos bordes con caras (dos anillos o dos filas de aristas).\n" +
            "• NUEVO · Grabar voz con el micrófono mientras se reproduce la animación; queda como un sonido más en la línea de tiempo (opciones de la línea de tiempo → Grabar voz aquí).\n" +
            "• ATLAS 3D ahora se llama ATLAS 5D: 3D + 2D en un solo programa.\n" +
            "• Al elegir un hueso en modo pose, la línea de tiempo muestra solo las claves de ese hueso (con «Todo el modelo» debajo como referencia).\n" +
            "• Cielo con nubes arreglado: las nubes ya no se ven como láminas con bordes rectos, y el render «Cine» hace más pasadas cuando la cámara gira rápido o hay mucho desenfoque, para que el fondo salga suave y no repetido.\n" +
            "• Artlas 2D a lo profesional (como Harmony): personajes por piezas con punto de giro y capas pegadas (mano → brazo → cuerpo), estirar y aplastar, trayectoria del movimiento, efectos de capa (sombra, resplandor, desenfoque, color), cambiar dibujo por cuadro, labios automáticos con la voz y secuencia PNG con fondo transparente.\n" +
            "• Arreglado: modelos (sobre todo de Sketchfab) que se veían completamente negros en algunos móviles. Si el teléfono no puede reducir una textura grande, ahora se usa la original.\n" +
            "• Normal maps arreglados de verdad: en PC el relieve salía torcido (rayas, lados oscuros, luz al revés) cuando la imagen tenía transparencia o máscara de brillo; también se torcía un poco la luz de modelos sin relieve. En Android se leen siempre bien.\n" +
            "• Relieve que antes no se cargaba: modelos de Source ($bumpmap) y OBJ (map_Bump / bump, también mapas en blanco y negro).\n" +
            "• Gran optimización: en el móvil las texturas gigantes (4K, 8K) se reducen al tamaño que aguanta el teléfono (mucha menos memoria, menos cierres), el suavizado de bordes baja solo cuando el móvil va lento, y el modo aristas y el modo pose gastan mucho menos.\n" +
            "• NUEVO · Artlas 2D: importar vídeo para rotoscopia (☰ → Importar vídeo). Cada fotograma del vídeo entra en una capa de referencia semitransparente para dibujar encima.\n" +
            "• Vuelve el cielo propio (como en la versión Godot): pon cualquier imagen como cielo, mejor una 360°. Ilumina la escena, se gira y se ajusta su brillo (Escena → Cielo y hora del día).\n" +
            "• NUEVO · Inicio a tu gusto: tu propia imagen de fondo (Preferencias → Interfaz), el logo del color de la interfaz y botones 3D | 2D junto al logo.\n" +
            "• NUEVO · Expresiones guardadas: guarda caras (formas y huesos de la cara) con un nombre y ponlas con un toque, también en otros proyectos.\n" +
            "• NUEVO · Render: elige desde qué fotograma hasta cuál se hace el vídeo, el GIF o el ZIP.\n" +
            "• NUEVO · Modelar → Dar grosor, y las extrusiones ya no se ven transparentes ni negras por un lado.\n" +
            "• Optimización: resolución automática si el móvil va lento y ahorro en reposo (menos calor y batería), en Preferencias.\n" +
            "• Vídeos arreglados: se pueden poner también con «Importar», funcionan aunque el nombre tenga espacios, tildes o emojis, aceptan más formatos (MP4, MOV, WEBM, 3GP, MKV…) y, si el móvil no puede leer uno, ATLAS te dice por qué y cómo convertirlo.\n" +
            "• NUEVO · Editor UV (Modelar → Editor UV), como en Blender: la malla desplegada sobre su textura; toca una pieza y muévela, gírala o escálala; «Desplegar» por islas; «Plantilla PNG» para pintarla en cualquier app y ponerla de textura.\n" +
            "• Arreglos: los modelos ya no vuelven a su tamaño original al abrir el proyecto; las texturas mantienen el filtro que trae el modelo (el pixel-art grande ya no sale borroso); el manipulador vuelve a moverse con los ejes del mundo (el modo Prisma3D está en Preferencias).\n" +
            "• Normal maps arreglados: ahora se ven en pelo, encajes y partes transparentes, en modelos que no traían relieve, y con PNG con transparencia. Nuevo «Relieve al revés» para los normal maps de DirectX.\n" +
            "• NUEVO · Render progresivo: la imagen mejora en vivo pasada a pasada; guárdala cuando te guste (Render → Render progresivo).\n" +
            "• NUEVO · Varios sonidos a la vez en la línea de tiempo, cada uno con su inicio y volumen.\n" +
            "• NUEVO · Reducir polígonos: aligera los modelos pesados para que el móvil vaya fluido (Aspecto del modelo).\n" +
            "• NUEVO · Animación en bucle (Claves de animación → En bucle) y modo espejo al posar (izquierda ↔ derecha).\n" +
            "• El manipulador se mueve con los ejes del objeto girado, como Prisma3D (se puede cambiar en Preferencias). Más zoom en las cámaras.\n" +
            "• Modelar: aristas y caras elegidas se ven como líneas de color, y lo extruido se ve por los dos lados. Los botones responden al tocarlos.\n" +
            "• Optimización: menos trabajo en cada fotograma y calidad inicial según la memoria del móvil.\n" +
            "• NUEVO · Biblioteca (pantalla de inicio): los modelos que importas o descargas se quedan guardados en ATLAS para usarlos otra vez sin volver a descargarlos, junto con Mis modelos. Buscar, usar y borrar.\n" +
            "• NUEVO · Animación 2D: botón en la pantalla de inicio que abre Artlas 2D (dibujo y animación cuadro a cuadro). En el móvil se abre dentro de ATLAS y «Atrás» vuelve; en PC se abre en el navegador.\n" +
            "• NUEVO · Rebote: pelo, colas, orejas y cables se balancean solos con el cuerpo (modo pose → elige el primer hueso → Generar rebote).\n" +
            "• NUEVO · Copiar animación de otro personaje, aunque sus huesos se llamen distinto (Mixamo, Source, Blender, Prisma…).\n" +
            "• NUEVO · Mirar a: la cabeza o los ojos siguen a otro objeto aunque todo se mueva.\n" +
            "• Texturas: ya se leen las texturas repetidas o desplazadas, los materiales «specular-glossiness» (salían oscuros o sin textura) y los «sin luz».\n" +
            "• Huesos: ya no salen todos como «Hueso»; los de Blender conservan el lado (izq./der.) y los repetidos se pueden mover.\n" +
            "• Modelar: vuelven las líneas oscuras de la malla, y Extruir funciona también con aristas.\n" +
            "• Opacidad: en 0 el objeto desaparece del todo, funciona en modelos importados y se anima con claves.\n" +
            "• Emisión pura: el color sin luz ni sombra, como en Blender.\n" +
            "• Arreglos: el render con desenfoque de fondo ya no sale todo borroso; los deslizadores de formas (shape keys) van suaves de 0 a 100; IK: el pie o la mano se mueven con su control y la pierna ya no se estira (vuelve a crear el IK en modelos con huesos intermedios).\n" +
            "• Selección múltiple: Ctrl o Shift + clic en PC, o el botón de «varios» en el móvil. Muévelos, gíralos y escálalos juntos; cada uno conserva su animación.\n" +
            "• Grupos: agrupa objetos y al tocar uno se seleccionan todos.\n" +
            "• Pegar a otro objeto o hueso: cualquier objeto o modelo se pega a otro (o a un hueso de otro personaje) desde un fotograma y se suelta en otro.\n" +
            "• Píxeles nítidos (estilo Minecraft) en los materiales del modelo.\n" +
            "• NUEVO: partículas de Source / SFM (.pcf). Impórtalas como un modelo: cada sistema con sus hijos, tiempos, colores, tamaños, gravedad, giros y atracción. Empiezan en el fotograma donde las añades.\n" +
            "• Estilo dibujo (anime): sombreado en bandas y contorno, en un objeto o en toda la escena.\n" +
            "• Creador de partículas: personaliza cualquier efecto (cantidad, duración, velocidad, tamaño, gravedad, forma, partícula, color, brillo y estela) y nuevo efecto Láser.\n" +
            "• Color de la interfaz a elegir en Preferencias.\n" +
            "• Modelos de Source (.mdl): ya no salen rayas negras atravesando el modelo.\n" +
            "• Prisma3D en un solo archivo, modelos de Minecraft, Mis modelos y textos que ya no se salen (1.2.1).";

        /// Once per version: what it brings.
        void ShowWhatsNew()
        {
            string seen = "";
            try { seen = PlayerPrefs.GetString("atlas.seen", ""); } catch (System.Exception) { }
            if (seen == Version) return;
            try { PlayerPrefs.SetString("atlas.seen", Version); PlayerPrefs.Save(); } catch (System.Exception) { }
            if (seen == "") return;   // first install: the guide is enough
            AtlasTheme.Dialog(canvasRoot, "Novedades · ATLAS 5D " + Version, WhatsNew, null, "Entendido", false, null);
            toastHolder.SetAsLastSibling();
        }

        void ShowAbout()
        {
            AtlasTheme.Dialog(canvasRoot, "ATLAS 5D " + Version,
                "Estudio de creación y animación 3D para móviles.\n\n" +
                "Esta es la nueva versión hecha en Unity: más fluida y con mejor imagen (URP, sombras suaves, efectos de cine). " +
                "Importa GLB, glTF, FBX, OBJ (+MTL), BLEND, Prisma3D, 3DS, STL, PLY y ZIP; exporta GLB, glTF, OBJ, STL, PLY y 3MF.\n\n" +
                "Letra Inter (licencia OFL).", null, "Cerrar", false, null);
            toastHolder.SetAsLastSibling();
        }
    }
}
