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
        int renderQuality = 1;
        int renderFrom = -1, renderTo = -1;   // -1: the timeline's own start / end

        /// The frames the animation renders use (chosen in the Render panel,
        /// or the whole timeline).
        void RenderRange(out int a, out int b)
        {
            a = renderFrom >= 0 ? Mathf.Clamp(renderFrom, 0, endFrame) : startFrame;
            b = renderTo >= 0 ? Mathf.Clamp(renderTo, 0, endFrame) : endFrame;
            if (b < a) { int t = a; a = b; b = t; }
        }   // 0 normal, 1 alta (2× supersampling), 2 máxima (2× + 8× MSAA), 3 cine (many passes)
        readonly List<Button> sizeChips = new List<Button>();

        static readonly int[,] RenderSizes = { { 1280, 720 }, { 1920, 1080 }, { 2560, 1440 }, { 3840, 2160 }, { 1080, 1080 }, { 1080, 1920 } };
        static readonly string[] RenderSizeNames = { "HD 1280×720", "Full HD 1080p", "2K 1440p", "4K 2160p", "Cuadrado 1080", "Vertical 9:16" };

        void BuildRenderPanel()
        {
            var menu = AtlasTheme.Box(viewArea, "Render", AtlasTheme.Panel2, 18);
            AtlasTheme.Pin(menu.rectTransform, 1f, 1f, new Vector2(-10f, -80f), new Vector2(360f, 0f));
            // Scrolls when it is taller than the screen (FitMenu sizes it).
            ScrollRect msr;
            var content = AtlasTheme.ScrollArea(menu.transform, out msr, 8f, new RectOffset(14, 14, 12, 14));
            var t1 = AtlasTheme.Label(content, "RENDER · TAMAÑO", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(t1.gameObject, 22f);
            var g = AtlasTheme.Grid(content, 2, 332f, 40f);
            for (int i = 0; i < RenderSizeNames.Length; i++)
            {
                int idx = i;
                sizeChips.Add(AtlasTheme.Chip(g, RenderSizeNames[i], () =>
                {
                    renderW = RenderSizes[idx, 0];
                    renderH = RenderSizes[idx, 1];
                    RefreshSizeChips();
                    foreach (var ci in items) if (ci != null && ci.kind == "camera") UpdateFrustum(ci);
                }));
            }
            RefreshSizeChips();
            AtlasTheme.Switch(content, "Mostrar el suelo", renderGround, on => renderGround = on);
            var tq = AtlasTheme.Label(content, "CALIDAD", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(tq.gameObject, 22f);
            var qg = AtlasTheme.Grid(content, 2, 332f, 40f);
            string[] qn = { "Normal", "Alta", "Máxima", "Cine (pasadas)" };
            var qChips = new List<Button>();
            for (int i = 0; i < 4; i++)
            {
                int q = i;
                qChips.Add(AtlasTheme.Chip(qg, qn[i], () =>
                {
                    renderQuality = q;
                    for (int k = 0; k < qChips.Count; k++)
                    {
                        qChips[k].GetComponent<Image>().color = k == renderQuality ? AtlasTheme.AccentDim : AtlasTheme.Panel3;
                        qChips[k].GetComponentInChildren<Text>().color = k == renderQuality ? AtlasTheme.AccentLight : AtlasTheme.TextColor;
                    }
                }, i == renderQuality));
            }
            // Which frames (like the Godot version).
            var tf2 = AtlasTheme.Label(content, "FOTOGRAMAS DEL VÍDEO / GIF / ZIP", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(tf2.gameObject, 22f);
            var fr = AtlasTheme.Rect(content, "Rango").gameObject;
            AtlasTheme.Row(fr, 8f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft);
            AtlasTheme.Le(fr, 40f);
            var l1 = AtlasTheme.Label(fr.transform, "Desde", 15, AtlasTheme.TextColor);
            AtlasTheme.Le(l1.gameObject, 40f, 54f);
            InputField fromF = null, toF = null;
            fromF = AtlasTheme.NumberField(fr.transform, renderFrom >= 0 ? renderFrom : startFrame, v => { renderFrom = Mathf.Max(0, Mathf.RoundToInt(v)); }, 70f);
            var l2 = AtlasTheme.Label(fr.transform, "hasta", 15, AtlasTheme.TextColor);
            AtlasTheme.Le(l2.gameObject, 40f, 50f);
            toF = AtlasTheme.NumberField(fr.transform, renderTo >= 0 ? renderTo : endFrame, v => { renderTo = Mathf.Max(0, Mathf.RoundToInt(v)); }, 70f);
            var rg = AtlasTheme.Grid(content, 2, 332f, 36f);
            AtlasTheme.TextButton(rg, "timeline", "Toda la animación", () =>
            {
                renderFrom = renderTo = -1;
                AtlasTheme.SetNumber(fromF, startFrame);
                AtlasTheme.SetNumber(toF, endFrame);
            }, false, 36f, 13);
            AtlasTheme.TextButton(rg, "key", "Desde el fotograma actual", () =>
            {
                renderFrom = Mathf.RoundToInt(frame);
                AtlasTheme.SetNumber(fromF, renderFrom);
            }, false, 36f, 13);
            AtlasTheme.TextButton(content, "image", "Imagen (PNG)", RenderPicture, true, 50f);
            AtlasTheme.TextButton(content, "sparkle", "Render progresivo (mejora en vivo)", () => StartCoroutine(ProgressiveRender()), false, 46f, 16);
            AtlasTheme.TextButton(content, "video", "Vídeo MP4", () => StartCoroutine(RenderVideo()), true, 50f);
            var more = AtlasTheme.Grid(content, 2, 332f, 46f);
            AtlasTheme.TextButton(more, "film", "GIF animado", () => StartCoroutine(RenderGif()), false, 46f, 16);
            AtlasTheme.TextButton(more, "image", "PNG (ZIP)", () => StartCoroutine(RenderFrames(true)), false, 46f, 16);
            AtlasTheme.TextButton(content, "film", "Animación (fotogramas en ZIP)", () => StartCoroutine(RenderFrames()), false, 50f, 17);
            var hint = AtlasTheme.Label(content, "El MP4 se guarda en tu galería o donde elijas. Los fotogramas JPG (con el audio en el ZIP) sirven para editarlos en CapCut, VN o cualquier editor.", 14, AtlasTheme.TextDim);
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
            BeginCapture();
            try { return CaptureIn(w, h, showGround); }
            finally { EndCapture(); }
        }

        /// `sample` ≥ 0: just that pass of the «Cine» render (progressive render).
        Texture2D CaptureIn(int w, int h, bool showGround, int sample = -1)
        {
            bool g = gizmo.gameObject.activeSelf;
            gizmo.gameObject.SetActive(false);
            highlighter.SetVisible(false);
            // Lamps and cameras are tools: they light, but are not seen
            // (their volumetric beam or halo is).
            var hidden = new List<Renderer>();
            foreach (var it in items)
                if (it != null && it.IsHelper)
                    foreach (var r in it.GetComponentsInChildren<Renderer>())
                        if (r.enabled && r.gameObject.name != "Volumen") { r.enabled = false; hidden.Add(r); }
            // Effects show their particles, not their marker.
            foreach (var it in items)
                if (it != null && it.kind == "fx")
                {
                    var mk = it.transform.Find("Marca");
                    var mr = mk != null ? mk.GetComponent<Renderer>() : null;
                    if (mr != null && mr.enabled) { mr.enabled = false; hidden.Add(mr); }
                }
            // From the scene camera when there is one.
            var rc = RenderCamera();
            Vector3 keepPos = cam.transform.position;
            Quaternion keepRot = cam.transform.rotation;
            float keepFov = cam.fieldOfView;
            if (rc != null)
            {
                Vector3 cp;
                Quaternion cq;
                CameraPose(rc, frame, out cp, out cq);
                cam.transform.SetPositionAndRotation(cp, cq);
                cam.fieldOfView = rc.fov;
                FitClip(cam, 2f);
            }
            bool gr = ground.activeSelf;
            ground.SetActive(showGround && gr);
            // Supersampling (drawn bigger, reduced here on the CPU: no blit
            // shader that a phone may lack) as far as the phone allows.
            bool roomy = SystemInfo.systemMemorySize >= 3500;
            int ss = renderQuality > 0 && roomy && (long)w * h * 4 <= 9000000L && w * 2 <= SystemInfo.maxTextureSize && h * 2 <= SystemInfo.maxTextureSize ? 2 : 1;
            int aa = ss > 1 ? (renderQuality == 2 ? 2 : 1) : (renderQuality == 2 ? 8 : 4);
            Texture2D tex = null;
            if (sample >= 0)
            {
                try { tex = CinemaShot(w, h, rc, 1, sample); }
                catch (System.Exception e) { Debug.LogWarning("ATLAS: progresivo: " + e.Message); }
            }
            else if (renderQuality >= 3)
            {
                // Like SFM's progressive refinement: 16 passes for a picture,
                // 8 per frame of an animation.
                try { tex = CinemaShot(w, h, rc, rendering ? 8 : 16); }
                catch (System.Exception e) { Debug.LogWarning("ATLAS: cine: " + e.Message); }
            }
            else
            try { tex = Shot(w, h, ss, aa, true); }
            catch (System.Exception e) { Debug.LogWarning("ATLAS: render: " + e.Message); }
            // A black picture means the phone could not draw it that way:
            // again, the simplest way (no supersampling, no MSAA, no HDR).
            if (tex == null || (sample < 0 && LooksBlack(tex)))
            {
                if (tex != null) Destroy(tex);
                bool hdr = cam.allowHDR;
                cam.allowHDR = false;
                try { tex = Shot(w, h, 1, 1, false); }
                finally { cam.allowHDR = hdr; }
            }
            ground.SetActive(gr);
            gizmo.gameObject.SetActive(g);
            highlighter.SetVisible(true);
            foreach (var r in hidden) if (r != null) r.enabled = true;
            cam.transform.SetPositionAndRotation(keepPos, keepRot);
            cam.fieldOfView = keepFov;
            FitClip(cam, distance);
            return tex;
        }

        Texture2D Shot(int w, int h, int ss, int aa, bool depth)
        {
            int bw = w * ss, bh = h * ss;
            var rt = RenderTexture.GetTemporary(bw, bh, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, aa);
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            try { cam.Render(); }
            finally
            {
                cam.targetTexture = prevTarget;
                cam.ResetAspect();
            }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var big = new Texture2D(bw, bh, TextureFormat.RGB24, false);
            big.ReadPixels(new UnityEngine.Rect(0, 0, bw, bh), 0, 0);
            big.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            if (ss == 1) return big;
            // 2×2 → 1 pixel average.
            var src = big.GetPixels32();
            var dst = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                int r0 = (y * 2) * bw, r1 = r0 + bw;
                for (int x = 0; x < w; x++)
                {
                    var a = src[r0 + x * 2]; var b = src[r0 + x * 2 + 1]; var c = src[r1 + x * 2]; var d = src[r1 + x * 2 + 1];
                    dst[y * w + x] = new Color32((byte)((a.r + b.r + c.r + d.r + 2) >> 2), (byte)((a.g + b.g + c.g + d.g + 2) >> 2), (byte)((a.b + b.b + c.b + d.b + 2) >> 2), 255);
                }
            }
            Destroy(big);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.SetPixels32(dst);
            tex.Apply();
            return tex;
        }

        /// Every sampled pixel (nearly) black: the render failed.
        static bool LooksBlack(Texture2D t)
        {
            int w = t.width, h = t.height;
            for (int i = 0; i < 97; i++)
            {
                var c = t.GetPixel((i * 7919) % w, (i * 104729) % h);
                if (c.r + c.g + c.b > 0.02f) return false;
            }
            return true;
        }

        /// Animated GIF (looping), at most 480 px on the long side to stay
        /// light for sharing.
        IEnumerator RenderGif()
        {
            CloseMenus();
            if (rendering || store == null) yield break;
            rendering = true;
            if (playing) TogglePlay();
            float keep = frame;
            float big = Mathf.Max(renderW, renderH);
            float k = Mathf.Min(1f, 480f / big);
            int w = Mathf.Max(16, Mathf.RoundToInt(renderW * k)), h = Mathf.Max(16, Mathf.RoundToInt(renderH * k));
            string path = Path.Combine(Exporter.ExportDir, (store.name.Replace(' ', '_')) + "_" + Stamp() + ".gif");
            int rA, rB;
            RenderRange(out rA, out rB);
            int total = rB - rA + 1;
            Toast("GIF: " + total + " fotogramas…");
            bool ok = true;
            string err = "";
            FileStream fs = null;
            GifWriter gif = null;
            try { fs = File.Create(path); gif = new GifWriter(fs, w, h, fps); }
            catch (System.Exception e) { ok = false; err = e.Message; }
            for (int f = rA; ok && f <= rB; f++)
            {
                SetFrame(f);
                yield return WaitVideos();
                try
                {
                    var tex = Capture(w, h, renderGround);
                    gif.AddFrame(tex.GetPixels32());
                    Destroy(tex);
                }
                catch (System.Exception e) { ok = false; err = e.Message; }
                if ((f - rA) % 24 == 23) Toast("GIF: " + (f - rA + 1) + " / " + total);
                yield return null;
            }
            try { if (gif != null) gif.Dispose(); if (fs != null) fs.Dispose(); }
            catch (System.Exception e) { ok = false; err = e.Message; }
            SetFrame(keep);
            rendering = false;
            if (ok) Share(path, "GIF animado (" + total + " fotogramas)");
            else Toast("No se pudo hacer el GIF: " + err, true);
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

        IEnumerator RenderFrames(bool png = false)
        {
            CloseMenus();
            if (rendering || store == null) yield break;
            rendering = true;
            if (playing) TogglePlay();
            float keep = frame;
            string path = Path.Combine(Exporter.ExportDir, "animacion_" + Stamp() + ".zip");
            int rA, rB;
            RenderRange(out rA, out rB);
            int total = rB - rA + 1;
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
            for (int f = rA; ok && f <= rB; f++)
            {
                SetFrame(f);
                yield return WaitVideos();
                try
                {
                    var tex = Capture(renderW, renderH, renderGround);
                    byte[] jpg = png ? tex.EncodeToPNG() : tex.EncodeToJPG(92);
                    Destroy(tex);
                    var entry = zip.CreateEntry("fotograma_" + f.ToString("0000") + (png ? ".png" : ".jpg"), System.IO.Compression.CompressionLevel.NoCompression);
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
            // The animation's sound goes in the ZIP too.
            if (ok && zip != null && (!string.IsNullOrEmpty(audioFile) || sounds.Count > 0))
            {
                try
                {
                    string ap = Path.Combine(AudioDir, audioFile ?? "");
                    if (!string.IsNullOrEmpty(audioFile) && File.Exists(ap))
                    {
                        var entry = zip.CreateEntry("audio" + Path.GetExtension(ap), System.IO.Compression.CompressionLevel.NoCompression);
                        using (var dst = entry.Open())
                        using (var src = File.OpenRead(ap)) src.CopyTo(dst);
                    }
                    // The other sounds, named with the frame where each starts.
                    for (int si = 0; si < sounds.Count; si++)
                    {
                        string sp = Path.Combine(AudioDir, sounds[si].file);
                        if (!File.Exists(sp)) continue;
                        var se = zip.CreateEntry("sonido_" + (si + 2) + "_fotograma_" + sounds[si].start + Path.GetExtension(sp), System.IO.Compression.CompressionLevel.NoCompression);
                        using (var dst = se.Open())
                        using (var src = File.OpenRead(sp)) src.CopyTo(dst);
                    }
                }
                catch (System.Exception) { }
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

        // ------------------------------------------------------------------
        // MP4 video (Android's encoder; see Plugins/Android/AtlasVideoEncoder)

        IEnumerator RenderVideo()
        {
            CloseMenus();
            if (rendering || store == null) yield break;
#if UNITY_ANDROID && !UNITY_EDITOR
            rendering = true;
            if (playing) TogglePlay();
            float keep = frame;
            // Even sizes; phones' encoders go up to about 1920 on the long side.
            int w = renderW, h = renderH;
            float big = Mathf.Max(w, h);
            if (big > 1920f) { w = Mathf.RoundToInt(w * 1920f / big); h = Mathf.RoundToInt(h * 1920f / big); }
            w &= ~1;
            h &= ~1;
            string path = Path.Combine(Exporter.ExportDir, (store.name.Replace(' ', '_')) + "_" + Stamp() + ".mp4");
            int rA, rB;
            RenderRange(out rA, out rB);
            int total = rB - rA + 1;
            Toast("Vídeo MP4: " + total + " fotogramas…");
            string err = "";
            bool ok = false;
            // Some phones' encoders refuse big sizes: then smaller ones.
            int[] longSides = { Mathf.Max(w, h), 1280, 960 };
            int ow = w, oh = h;
            foreach (int side in longSides)
            {
                if (side > Mathf.Max(ow, oh)) continue;
                w = Mathf.RoundToInt(ow * (float)side / Mathf.Max(ow, oh)) & ~1;
                h = Mathf.RoundToInt(oh * (float)side / Mathf.Max(ow, oh)) & ~1;
                if (side != longSides[0]) Toast("Probando el vídeo a " + w + "×" + h + "…");
                AndroidJavaObject enc = null;
                ok = StartEncoder(path, w, h, ref enc, ref err);
                for (int f = rA; ok && f <= rB; f++)
                {
                    SetFrame(f);
                    yield return WaitVideos();
                    ok = EncodeFrame(enc, f, w, h, ref err);
                    if ((f - rA) % 24 == 23) Toast("Vídeo: " + (f - rA + 1) + " / " + total);
                    yield return null;
                }
                if (enc != null)
                {
                    bool fin = FinishEncoder(enc, ref err);
                    ok = ok && fin;
                    enc.Dispose();
                }
                if (ok) break;
                try { if (File.Exists(path)) File.Delete(path); } catch (System.Exception) { }
                System.GC.Collect();
            }
            SetFrame(keep);
            rendering = false;
            if (ok)
            {
                Share(path, "Vídeo MP4 (" + total + " fotogramas" + (string.IsNullOrEmpty(audioFile) ? "" : ", sin el audio") + ")");
                yield break;
            }
            Toast("El MP4 no funcionó en este teléfono (" + err + "): se guardan los fotogramas en ZIP", true);
            yield return RenderFrames();
#else
            Toast("El vídeo MP4 se hace en Android: aquí se guardan los fotogramas en ZIP");
            yield return RenderFrames();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        bool StartEncoder(string path, int w, int h, ref AndroidJavaObject enc, ref string err)
        {
            try
            {
                enc = new AndroidJavaObject("org.atlas3d.AtlasVideoEncoder");
                int bitrate = Mathf.Clamp(Mathf.RoundToInt(w * h * fps * 0.18f), 2000000, 20000000);
                if (enc.Call<bool>("start", path, w, h, fps, bitrate)) return true;
                err = enc.Call<string>("getError");
            }
            catch (System.Exception e)
            {
                err = e.Message;
            }
            return false;
        }

        bool EncodeFrame(AndroidJavaObject enc, int f, int w, int h, ref string err)
        {
            try
            {
                SetFrame(f);
                var tex = Capture(w, h, renderGround);
                byte[] raw = tex.GetRawTextureData();
                Destroy(tex);
                var sb = new sbyte[raw.Length];
                System.Buffer.BlockCopy(raw, 0, sb, 0, raw.Length);
                if (enc.Call<bool>("addFrame", sb)) return true;
                err = enc.Call<string>("getError");
            }
            catch (System.Exception e)
            {
                err = e.Message;
            }
            return false;
        }

        static bool FinishEncoder(AndroidJavaObject enc, ref string err)
        {
            try
            {
                if (enc.Call<bool>("finish")) return true;
                err = enc.Call<string>("getError");
            }
            catch (System.Exception e)
            {
                err = e.Message;
            }
            return false;
        }
#endif
    }
}
