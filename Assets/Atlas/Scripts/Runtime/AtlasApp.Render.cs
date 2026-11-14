// ATLAS 3D (Unity) - «Render»: a picture of the scene without the
// interface (PNG, several sizes) or the whole animation as numbered
// frames in a ZIP (to turn into a video in any editor).
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        GameObject renderPanel;
        bool rendering;
        int renderW = 1920, renderH = 1080;
        bool renderGround = true;
        readonly List<Button> sizeChips = new List<Button>();

        static readonly int[,] RenderSizes = { { 1280, 720 }, { 1920, 1080 }, { 2560, 1440 }, { 3840, 2160 }, { 1080, 1080 }, { 1080, 1920 } };
        static readonly string[] RenderSizeNames = { "HD 1280×720", "Full HD 1080p", "2K 1440p", "4K 2160p", "Cuadrado 1080", "Vertical 9:16" };

        void BuildRenderPanel()
        {
            var menu = AtlasTheme.Box(viewArea, "Render", AtlasTheme.Panel2, 18);
            AtlasTheme.Pin(menu.rectTransform, 1f, 1f, new Vector2(-10f, -80f), new Vector2(360f, 0f));
            AtlasTheme.Column(menu.gameObject, 8f, new RectOffset(14, 14, 12, 14));
            var fit = menu.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var t1 = AtlasTheme.Label(menu.transform, "RENDER · TAMAÑO", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(t1.gameObject, 22f);
            var g = AtlasTheme.Grid(menu.transform, 2, 332f, 40f);
            for (int i = 0; i < RenderSizeNames.Length; i++)
            {
                int idx = i;
                sizeChips.Add(AtlasTheme.Chip(g, RenderSizeNames[i], () =>
                {
                    renderW = RenderSizes[idx, 0];
                    renderH = RenderSizes[idx, 1];
                    RefreshSizeChips();
                }));
            }
            RefreshSizeChips();
            AtlasTheme.Switch(menu.transform, "Mostrar el suelo", renderGround, on => renderGround = on);
            AtlasTheme.TextButton(menu.transform, "image", "Imagen (PNG)", RenderPicture, true, 50f);
            AtlasTheme.TextButton(menu.transform, "film", "Animación (fotogramas en ZIP)", () => StartCoroutine(RenderFrames()), false, 50f, 17);
            var hint = AtlasTheme.Label(menu.transform, "Los fotogramas JPG numerados se convierten en vídeo con CapCut, VN o cualquier editor.", 14, AtlasTheme.TextDim);
            AtlasTheme.Le(hint.gameObject, 40f);
            renderPanel = menu.gameObject;
            renderPanel.SetActive(false);
        }

        void RefreshSizeChips()
        {
            for (int i = 0; i < sizeChips.Count; i++)
            {
                bool on = RenderSizes[i, 0] == renderW && RenderSizes[i, 1] == renderH;
                sizeChips[i].GetComponent<Image>().color = on ? AtlasTheme.AccentDim : AtlasTheme.Panel3;
                sizeChips[i].GetComponentInChildren<Text>().color = on ? AtlasTheme.AccentLight : AtlasTheme.TextColor;
            }
        }

        /// Renders the camera's view (no interface, no gizmo) at w × h.
        Texture2D Capture(int w, int h, bool showGround)
        {
            bool g = gizmo.gameObject.activeSelf;
            gizmo.gameObject.SetActive(false);
            bool gr = ground.activeSelf;
            ground.SetActive(showGround && gr);
            int aa = (long)w * h > 4000000L ? 1 : 4;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, aa);
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            cam.Render();
            cam.targetTexture = prevTarget;
            cam.ResetAspect();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new UnityEngine.Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            ground.SetActive(gr);
            gizmo.gameObject.SetActive(g);
            return tex;
        }

        static string Stamp()
        {
            return System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        void RenderPicture()
        {
            CloseMenus();
            if (rendering) return;
            try
            {
                var tex = Capture(renderW, renderH, renderGround);
                string path = Path.Combine(Exporter.ExportDir, "render_" + Stamp() + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Destroy(tex);
                Share(path, "Render");
            }
            catch (System.Exception e)
            {
                Toast("No se pudo hacer el render: " + e.Message, true);
            }
        }

        IEnumerator RenderFrames()
        {
            CloseMenus();
            if (rendering || store == null) yield break;
            rendering = true;
            if (playing) TogglePlay();
            float keep = frame;
            string path = Path.Combine(Exporter.ExportDir, "animacion_" + Stamp() + ".zip");
            int total = endFrame + 1;
            Toast("Render de la animación: " + total + " fotogramas…");
            bool ok = true;
            string error = "";
            FileStream fs = null;
            ZipArchive zip = null;
            try
            {
                fs = File.Create(path);
                zip = new ZipArchive(fs, ZipArchiveMode.Create);
            }
            catch (System.Exception e)
            {
                ok = false;
                error = e.Message;
            }
            for (int f = 0; ok && f <= endFrame; f++)
            {
                try
                {
                    SetFrame(f);
                    var tex = Capture(renderW, renderH, renderGround);
                    byte[] jpg = tex.EncodeToJPG(92);
                    Destroy(tex);
                    var entry = zip.CreateEntry("fotograma_" + f.ToString("0000") + ".jpg", System.IO.Compression.CompressionLevel.NoCompression);
                    using (var s = entry.Open()) s.Write(jpg, 0, jpg.Length);
                }
                catch (System.Exception e)
                {
                    ok = false;
                    error = e.Message;
                }
                if (f % 24 == 23) Toast("Render: " + (f + 1) + " / " + total);
                yield return null;
            }
            try
            {
                if (zip != null) zip.Dispose();
                if (fs != null) fs.Dispose();
            }
            catch (System.Exception e)
            {
                ok = false;
                error = e.Message;
            }
            SetFrame(keep);
            rendering = false;
            if (ok) Share(path, "Animación (" + total + " fotogramas, " + fps + " fps)");
            else Toast("No se pudo hacer el render de la animación: " + error, true);
        }
    }
}
