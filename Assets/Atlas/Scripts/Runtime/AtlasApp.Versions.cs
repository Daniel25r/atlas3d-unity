// ATLAS 5D (Unity) - «Versiones anteriores»: besides saving after every
// change, a copy of the project is kept every few minutes (the newest ten
// by default). Any of them can be brought back; the state before it is kept
// as one more version and the change can be undone.
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        float lastVersionAt = -1f;
        string lastVersionFor = "";

        void MaybeKeepVersion()
        {
            if (store == null || Prefs.versionEvery <= 0) return;
            // The first save of each session also makes a version (what the
            // project was like when it was opened).
            if (lastVersionFor != store.name) { lastVersionFor = store.name; lastVersionAt = -1f; }
            if (lastVersionAt >= 0f && Time.realtimeSinceStartup - lastVersionAt < Prefs.versionEvery * 60f) return;
            lastVersionAt = Time.realtimeSinceStartup;
            try { store.KeepVersion(Prefs.versionKeep); }
            catch (System.Exception e) { Debug.LogWarning("ATLAS: versión: " + e.Message); }
        }

        void ShowVersions()
        {
            if (store == null) return;
            var l = new List<AtlasTheme.MenuItem> { AtlasTheme.MenuItem.Header("Versiones anteriores · toca una para volver a ella") };
            var vs = store.Versions();
            if (vs.Count == 0) l.Add(new AtlasTheme.MenuItem("info", "Aún no hay versiones: se guarda una cada " + Prefs.versionEvery + " min mientras trabajas", () => { }));
            foreach (var path in vs)
            {
                string p = path;
                string label = VersionLabel(p);
                l.Add(new AtlasTheme.MenuItem("undo", label, () => AskRestoreVersion(p, label)));
            }
            l.Add(AtlasTheme.MenuItem.Header("Guardar una versión cada"));
            foreach (int m in new[] { 1, 3, 5, 10, 15, 0 })
            {
                int mm = m;
                l.Add(new AtlasTheme.MenuItem(null, (Prefs.versionEvery == m ? "✓ " : "") + (m == 0 ? "Nunca" : m + " min"), () => { Prefs.versionEvery = mm; Prefs.Save(); Toast(mm == 0 ? "Sin versiones automáticas" : "Una versión cada " + mm + " min"); }));
            }
            l.Add(AtlasTheme.MenuItem.Header("Conservar las últimas"));
            foreach (int k in new[] { 5, 10, 20, 50 })
            {
                int kk = k;
                l.Add(new AtlasTheme.MenuItem(null, (Prefs.versionKeep == k ? "✓ " : "") + k + " versiones", () => { Prefs.versionKeep = kk; Prefs.Save(); Toast("Se conservan las últimas " + kk + " versiones"); }));
            }
            l.Add(new AtlasTheme.MenuItem("save", "Guardar una versión ahora", () =>
            {
                SaveNow(false);
                try { store.KeepVersion(Prefs.versionKeep); lastVersionAt = Time.realtimeSinceStartup; Toast("Versión guardada"); }
                catch (System.Exception e) { Toast("No se pudo: " + e.Message, true); }
            }));
            AtlasTheme.Popup(canvasRoot, new Vector2(Screen.width * 0.5f, Screen.height * 0.75f), l);
            toastHolder.SetAsLastSibling();
        }

        static string VersionLabel(string path)
        {
            string n = Path.GetFileNameWithoutExtension(path);
            System.DateTime t;
            string when = System.DateTime.TryParseExact(n, "yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t)
                ? t.ToString("dd/MM  HH:mm:ss") : n;
            long kb = 0;
            try { kb = new FileInfo(path).Length / 1024; } catch (System.Exception) { }
            return when + "  ·  " + kb + " KB";
        }

        void AskRestoreVersion(string path, string label)
        {
            AtlasTheme.Dialog(canvasRoot, "Volver a esta versión", "El proyecto volverá a como estaba el " + label.Split('·')[0].Trim() +
                ". Lo de ahora queda guardado como otra versión y también se puede deshacer.", null, "Volver a ella", true, _ =>
            {
                SceneData d = null;
                try { d = SceneStore.LoadFile(path); }
                catch (System.Exception e) { Toast("Esa versión no se puede leer: " + e.Message, true); return; }
                if (d == null) { Toast("Esa versión está vacía", true); return; }
                try { store.KeepVersion(Prefs.versionKeep + 1); } catch (System.Exception) { }
                Restore(d, true);
                Commit(true);
                Toast("Proyecto devuelto a la versión del " + label.Split('·')[0].Trim());
            });
        }
    }
}
