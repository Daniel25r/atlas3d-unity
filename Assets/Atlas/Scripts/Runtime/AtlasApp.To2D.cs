// ATLAS 5D (Unity) - «Animar en 2D encima»: the 3D scene (set, camera moves,
// 3D characters as a guide) is rendered frame by frame and opened in Atlas 2D
// as a locked background layer, with an empty layer on top to draw the
// characters by hand: the way studios lay out 2D animation over 3D.
using System.Collections;
using System.IO;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        IEnumerator RenderFor2D()
        {
            CloseMenus();
            if (rendering || store == null) yield break;
            string root;
            try { root = Ensure2D(); }
            catch (System.Exception e) { Toast("No se pudo preparar el modo 2D: " + e.Message, true); yield break; }
            rendering = true;
            if (playing) TogglePlay();
            float keep = frame;
            int rA, rB;
            RenderRange(out rA, out rB);
            int total = rB - rA + 1;
            // Atlas 2D works at 1280 × 720 (or the same shape, never bigger than 1920).
            float aspect = renderH > 0 ? (float)renderW / renderH : 16f / 9f;
            int w = aspect >= 1f ? 1280 : Mathf.RoundToInt(1280 * aspect);
            int h = aspect >= 1f ? Mathf.RoundToInt(1280 / aspect) : 1280;
            w -= w % 2; h -= h % 2;
            string id = "a" + System.DateTime.Now.ToString("yyyyMMddHHmmss");
            string dir = Path.Combine(root, "import", id);
            bool ok = true;
            string error = "";
            try
            {
                // Only the last few renders are kept.
                string imp = Path.Combine(root, "import");
                if (Directory.Exists(imp))
                {
                    var old = Directory.GetDirectories(imp);
                    System.Array.Sort(old);
                    for (int i = 0; i < old.Length - 2; i++) Directory.Delete(old[i], true);
                }
                Directory.CreateDirectory(dir);
            }
            catch (System.Exception e) { ok = false; error = e.Message; }
            Toast("Preparando la escena 3D para dibujar encima: " + total + " fotogramas…");
            for (int f = rA; ok && f <= rB; f++)
            {
                SetFrame(f);
                yield return WaitVideos();
                try
                {
                    var tex = Capture(w, h, renderGround);
                    File.WriteAllBytes(Path.Combine(dir, (f - rA).ToString("0000") + ".jpg"), tex.EncodeToJPG(88));
                    Destroy(tex);
                }
                catch (System.Exception e) { ok = false; error = e.Message; }
                if (f % 24 == 23) Toast("Escena 3D: " + (f - rA + 1) + " / " + total);
                yield return null;
            }
            if (ok)
            {
                try
                {
                    string name = "Escena 3D " + System.DateTime.Now.ToString("dd-MM HH:mm");
                    string json = "{\"name\":\"" + name.Replace("\\", "").Replace("\"", "'") + "\",\"w\":" + w + ",\"h\":" + h + ",\"fps\":" + fps + ",\"count\":" + total + "}";
                    File.WriteAllText(Path.Combine(dir, "manifest.json"), json);
                }
                catch (System.Exception e) { ok = false; error = e.Message; }
            }
            SetFrame(keep);
            rendering = false;
            if (!ok) { Toast("No se pudo preparar la escena para 2D: " + error, true); yield break; }
            Open2D(id);
        }
    }
}
