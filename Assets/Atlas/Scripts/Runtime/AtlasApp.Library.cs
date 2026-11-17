// ATLAS 3D (Unity) - «Mis modelos»: keep an object ready to use in any
// project: the model with its textures, materials, glow, face sliders,
// IK rig and pose (or a shape with its material). Saved in the app, not in
// one project: Añadir → Mis modelos puts it in the scene again.
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        static string LibraryDir
        {
            get
            {
                string d = Path.Combine(Application.persistentDataPath, "MisModelos");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        void SaveToLibrary(AtlasItem it)
        {
            if (it == null || store == null) return;
            if (it.IsHelper) { Toast("Las luces y cámaras no se guardan en Mis modelos", true); return; }
            AtlasTheme.Dialog(canvasRoot, "Guardar en Mis modelos", "Nombre con el que aparecerá en Añadir → Mis modelos:", it.gameObject.name, "Guardar", false, name =>
            {
                name = SceneStore.CleanName(string.IsNullOrEmpty(name) ? it.gameObject.name : name);
                if (string.IsNullOrEmpty(name)) name = "Modelo";
                try
                {
                    string dir = Path.Combine(LibraryDir, name);
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    Directory.CreateDirectory(dir);
                    var d = SceneStore.Capture(new List<AtlasItem> { it }).items[0];
                    d.keys = new List<KeyData>();          // the object, not its animation
                    d.anchors = new List<AnchorEvent>();
                    d.lookAtId = ""; d.followId = "";
                    // The model's file (and its folder: glTF + .bin + images).
                    if (!string.IsNullOrEmpty(d.modelFile))
                    {
                        string src = store.ModelPath(d.modelFile);
                        string folder = Path.GetDirectoryName(d.modelFile.Replace('\\', '/'));
                        string md = Path.Combine(dir, "modelo");
                        Directory.CreateDirectory(md);
                        if (!string.IsNullOrEmpty(folder)) CopyDir(Path.GetDirectoryName(src), md);
                        else File.Copy(src, Path.Combine(md, Path.GetFileName(src)), true);
                        d.modelFile = Path.GetFileName(d.modelFile);
                    }
                    // Its pictures.
                    string td = Path.Combine(dir, "texturas");
                    Directory.CreateDirectory(td);
                    foreach (var f in TexturesOf(d)) { string p = store.TexturePath(f); if (File.Exists(p)) File.Copy(p, Path.Combine(td, f), true); }
                    // Painted weights.
                    string wp = WeightsPath(it);
                    if (wp != "" && File.Exists(wp)) File.Copy(wp, Path.Combine(dir, "pesos.bin"), true);
                    File.WriteAllText(Path.Combine(dir, "objeto.json"), JsonUtility.ToJson(d));
                    Toast("Guardado en Mis modelos: «" + name + "» (Añadir → Mis modelos)");
                }
                catch (System.Exception e) { Toast("No se pudo guardar: " + e.Message, true); }
            });
            toastHolder.SetAsLastSibling();
        }

        static List<string> TexturesOf(ItemData d)
        {
            var l = new List<string>();
            System.Action<string> add = f => { if (!string.IsNullOrEmpty(f) && !l.Contains(f)) l.Add(f); };
            add(d.texture); add(d.normalMap); add(d.emissionMap);
            if (!string.IsNullOrEmpty(d.gobo) && d.gobo.StartsWith("img:")) add(d.gobo.Substring(4));
            if (d.mats != null) foreach (var o in d.mats) { add(o.texture); add(o.normalMap); add(o.emissionMap); }
            return l;
        }

        static void CopyDir(string from, string to)
        {
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(from.Length).TrimStart('/', '\\');
                if (rel.EndsWith("_prisma") || rel.Contains("_prisma/") || rel.Contains("_prisma\\")) continue;   // unpacked sources
                string dst = Path.Combine(to, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(f, dst, true);
            }
        }

        void OpenLibrary()
        {
            CloseMenus();
            if (store == null) return;
            string[] dirs;
            try { dirs = Directory.GetDirectories(LibraryDir); }
            catch (System.Exception) { dirs = new string[0]; }
            System.Array.Sort(dirs);
            var l = new List<AtlasTheme.MenuItem> { AtlasTheme.MenuItem.Header("Mis modelos") };
            foreach (var d0 in dirs)
            {
                string d = d0;
                if (!File.Exists(Path.Combine(d, "objeto.json"))) continue;
                l.Add(new AtlasTheme.MenuItem("person", Path.GetFileName(d), () => AddFromLibrary(d)));
            }
            if (l.Count == 1)
            {
                Toast("Todavía no hay nada en Mis modelos: selecciona un objeto y pulsa «Guardar en Mis modelos»");
                return;
            }
            l.Add(new AtlasTheme.MenuItem("delete", "Borrar uno…", () =>
            {
                var del = new List<AtlasTheme.MenuItem> { AtlasTheme.MenuItem.Header("Borrar de Mis modelos") };
                foreach (var d0 in dirs)
                {
                    string d = d0;
                    del.Add(new AtlasTheme.MenuItem("delete", Path.GetFileName(d), () =>
                    {
                        try { Directory.Delete(d, true); Toast("Borrado de Mis modelos"); } catch (System.Exception) { }
                    }));
                }
                AtlasTheme.Popup(canvasRoot, new Vector2(Screen.width * 0.5f, Screen.height * 0.6f), del);
            }));
            AtlasTheme.Popup(canvasRoot, new Vector2(Screen.width * 0.5f, Screen.height * 0.6f), l);
            toastHolder.SetAsLastSibling();
        }

        void AddFromLibrary(string dir)
        {
            if (store == null) return;
            ItemData d;
            try { d = JsonUtility.FromJson<ItemData>(File.ReadAllText(Path.Combine(dir, "objeto.json"))); }
            catch (System.Exception e) { Toast("No se pudo leer: " + e.Message, true); return; }
            if (d == null) return;
            try
            {
                d.FixOld();
                d.id = "";
                // Pictures go to this project's textures (same names kept).
                string td = Path.Combine(dir, "texturas");
                if (Directory.Exists(td))
                    foreach (var f in Directory.GetFiles(td))
                    {
                        string dst = store.TexturePath(Path.GetFileName(f));
                        if (!File.Exists(dst)) { Directory.CreateDirectory(Path.GetDirectoryName(dst)); File.Copy(f, dst); }
                    }
                if (!string.IsNullOrEmpty(d.modelFile))
                {
                    string folder = "biblioteca_" + SceneStore.NewId().Substring(0, 8);
                    string dst = Path.Combine(store.ModelsDir, folder);
                    Directory.CreateDirectory(dst);
                    CopyDir(Path.Combine(dir, "modelo"), dst);
                    d.modelFile = folder + "/" + d.modelFile;
                }
                d.position = new Vector3(target.position.x, d.position.y, target.position.z);
            }
            catch (System.Exception e) { Toast("No se pudo añadir: " + e.Message, true); return; }
            var it = Create(d);
            // Its painted weights come with it (read when the model loads).
            string pw = Path.Combine(dir, "pesos.bin");
            if (File.Exists(pw))
            {
                string wp = WeightsPath(it);
                try { Directory.CreateDirectory(Path.GetDirectoryName(wp)); File.Copy(pw, wp, true); } catch (System.Exception) { }
            }
            Select(it);
            Commit(true);
            Toast("«" + Path.GetFileName(dir) + "» añadido desde Mis modelos");
        }
    }
}
