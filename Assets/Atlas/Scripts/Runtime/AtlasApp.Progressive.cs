// ATLAS 3D (Unity) - «Render progresivo»: the picture is drawn again and
// again with the lens, the shutter and the pixel moved a little each time,
// and the passes are averaged on screen: it gets smoother (edges, depth of
// field, motion blur) while you watch. «Guardar» keeps it when it looks
// good; it stops by itself at 1024 passes.
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        IEnumerator ProgressiveRender()
        {
            CloseMenus();
            if (rendering || store == null) yield break;
            if (playing) TogglePlay();
            int w = renderW, h = renderH;
            // Up to 2K: the sums of every pass have to fit in the phone's memory.
            const long MaxPx = 2560L * 1440L;
            if ((long)w * h > MaxPx)
            {
                float k = Mathf.Sqrt(MaxPx / (float)((long)w * h));
                w = Mathf.Max(2, Mathf.RoundToInt(w * k));
                h = Mathf.Max(2, Mathf.RoundToInt(h * k));
                Toast("Render progresivo a " + w + "×" + h + " (el máximo para que quepa en memoria)");
            }
            rendering = true;
            bool stop = false, save = false;

            // Window: the picture, how many passes, Guardar / Cancelar.
            var dim = AtlasTheme.Box(canvasRoot, "Progresivo", new Color(0.02f, 0.02f, 0.03f, 0.97f));
            AtlasTheme.Fill(dim.rectTransform);
            dim.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            var view = new GameObject("Imagen").AddComponent<RawImage>();
            view.transform.SetParent(dim.transform, false);
            AtlasTheme.Fill(view.rectTransform, 16f, 16f, 16f, 72f);
            var fit = view.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = (float)w / h;
            var info = AtlasTheme.Label(dim.transform, "Preparando…", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Pin(info.rectTransform, 0f, 0f, new Vector2(24f, 16f), new Vector2(520f, 40f));
            info.rectTransform.pivot = new Vector2(0f, 0f);
            var saveB = AtlasTheme.TextButton(dim.transform, "save", "Guardar", () => { save = true; stop = true; }, true, 44f, 16, 150f);
            AtlasTheme.Pin((RectTransform)saveB.transform, 1f, 0f, new Vector2(-180f, 14f), new Vector2(150f, 44f));
            ((RectTransform)saveB.transform).pivot = new Vector2(1f, 0f);
            var cancelB = AtlasTheme.TextButton(dim.transform, "close", "Cancelar", () => { stop = true; }, false, 44f, 16, 150f);
            AtlasTheme.Pin((RectTransform)cancelB.transform, 1f, 0f, new Vector2(-18f, 14f), new Vector2(150f, 44f));
            ((RectTransform)cancelB.transform).pivot = new Vector2(1f, 0f);
            toastHolder.SetAsLastSibling();

            var sum = new int[w * h * 3];
            var shown = new Texture2D(w, h, TextureFormat.RGB24, false);
            var px = new Color32[w * h];
            view.texture = shown;
            int done = 0;
            string error = "";
            BeginCapture();
            try
            {
                for (int s = 0; s < 1024 && !stop; s++)
                {
                    Texture2D t = null;
                    try { t = CaptureIn(w, h, renderGround, s); }
                    catch (System.Exception e) { error = e.Message; }
                    if (t == null) { if (error != "") break; continue; }
                    var p = t.GetPixels32();
                    Destroy(t);
                    int n = Mathf.Min(p.Length, w * h);
                    for (int i = 0; i < n; i++)
                    {
                        sum[i * 3] += p[i].r;
                        sum[i * 3 + 1] += p[i].g;
                        sum[i * 3 + 2] += p[i].b;
                    }
                    done++;
                    // The picture on screen: every pass at first, then less often.
                    if (done <= 16 || done % 4 == 0)
                    {
                        for (int i = 0; i < px.Length; i++)
                            px[i] = new Color32((byte)(sum[i * 3] / done), (byte)(sum[i * 3 + 1] / done), (byte)(sum[i * 3 + 2] / done), 255);
                        shown.SetPixels32(px);
                        shown.Apply(false);
                    }
                    info.text = Lang.T("Pasada") + " " + done + " · " + w + "×" + h + " · " + Lang.T("se ve cada vez más suave; pulsa Guardar cuando te guste");
                    yield return null;
                }
            }
            finally
            {
                EndCapture();
                rendering = false;
            }
            if (save && done > 0)
            {
                for (int i = 0; i < px.Length; i++)
                    px[i] = new Color32((byte)(sum[i * 3] / done), (byte)(sum[i * 3 + 1] / done), (byte)(sum[i * 3 + 2] / done), 255);
                shown.SetPixels32(px);
                shown.Apply(false);
                try
                {
                    string path = Path.Combine(Exporter.ExportDir, "render_" + Stamp() + ".png");
                    File.WriteAllBytes(path, shown.EncodeToPNG());
                    Share(path, "Render progresivo (" + done + " pasadas)");
                }
                catch (System.Exception e) { Toast("No se pudo guardar: " + e.Message, true); }
            }
            else if (error != "") Toast("No se pudo hacer el render: " + error, true);
            Destroy(shown);
            Destroy(dim.gameObject);
        }
    }
}
