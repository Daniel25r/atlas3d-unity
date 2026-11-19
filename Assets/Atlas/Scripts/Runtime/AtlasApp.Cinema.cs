// ATLAS 3D (Unity) - film tools in the spirit of Source Filmmaker:
//  · gobos: a spotlight projects a shape (window, blinds, leaves, disco…)
//  · flicker: lamps that blink like a broken light (horror!)
//  · ambient occlusion (soft shadows in corners and folds)
//  · «Cine» render quality: many passes averaged (like SFM's progressive
//    refinement): real lens depth of field, motion blur and smooth edges
//  · camera cuts: several cameras in one render, switching at frames
//  · motion tools: smooth, shake, reverse and stretch the keys
//  · ready-made sets: show stage, horror corridor, night forest, city
//  · face sliders (blend shapes) and lip sync with mouth shapes
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    [System.Serializable]
    public class CamCut
    {
        public int frame;
        public string id = "";
    }

    public partial class AtlasApp
    {
        // ------------------------------------------------------------------
        // Gobos (light cookies)

        public static readonly string[] Gobos = { "", "window", "blinds", "leaves", "disco", "grid", "circle", "stars", "bars" };
        public static readonly string[] GoboNames = { "Ninguno", "Ventana", "Persiana", "Hojas", "Disco", "Rejilla", "Círculo", "Estrellas", "Barrotes" };
        static readonly Dictionary<string, Texture2D> goboCache = new Dictionary<string, Texture2D>();

        static Texture2D GoboTexture(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Texture2D t;
            if (goboCache.TryGetValue(id, out t) && t != null) return t;
            const int N = 256;
            t = new Texture2D(N, N, TextureFormat.RGBA32, true);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            var px = new Color32[N * N];
            var rnd = new System.Random(7);
            var stars = new List<Vector3>();
            for (int i = 0; i < 70; i++) stars.Add(new Vector3((float)rnd.NextDouble(), (float)rnd.NextDouble(), 0.006f + (float)rnd.NextDouble() * 0.014f));
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                    float cx = u - 0.5f, cy = v - 0.5f, r = Mathf.Sqrt(cx * cx + cy * cy);
                    float a = 0f;
                    switch (id)
                    {
                        case "window":
                            {
                                bool inside = u > 0.12f && u < 0.88f && v > 0.08f && v < 0.92f;
                                bool frameBar = Mathf.Abs(u - 0.5f) < 0.025f || Mathf.Abs(v - 0.5f) < 0.025f;
                                a = inside && !frameBar ? 1f : 0f;
                                break;
                            }
                        case "blinds": a = Mathf.Repeat(v * 9f, 1f) < 0.55f && r < 0.48f ? 1f : 0f; break;
                        case "leaves":
                            {
                                float n = Mathf.PerlinNoise(u * 7f, v * 7f) * 0.65f + Mathf.PerlinNoise(u * 19f + 3f, v * 19f) * 0.35f;
                                a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.6f, n)) * Mathf.Clamp01((0.48f - r) * 12f);
                                break;
                            }
                        case "disco":
                            {
                                float gx = Mathf.Repeat(u * 10f, 1f) - 0.5f, gy = Mathf.Repeat(v * 10f, 1f) - 0.5f;
                                a = gx * gx + gy * gy < 0.06f && r < 0.47f ? 1f : 0f;
                                break;
                            }
                        case "grid":
                            {
                                float d1 = Mathf.Repeat((u + v) * 8f, 1f), d2 = Mathf.Repeat((u - v) * 8f, 1f);
                                a = d1 > 0.12f && d2 > 0.12f && r < 0.48f ? 1f : 0f;
                                break;
                            }
                        case "circle": a = Mathf.Clamp01((0.33f - r) * 40f); break;
                        case "stars":
                            foreach (var s in stars)
                            {
                                float dx = u - s.x, dy = v - s.y;
                                if (dx * dx + dy * dy < s.z * s.z) { a = 1f; break; }
                            }
                            if (r > 0.48f) a = 0f;
                            break;
                        case "bars": a = Mathf.Repeat(u * 6f, 1f) > 0.18f && r < 0.48f ? 1f : 0f; break;
                    }
                    // The border stays dark (spotlights smear their edge).
                    if (x < 2 || y < 2 || x > N - 3 || y > N - 3) a = 0f;
                    byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                    px[y * N + x] = new Color32(b, b, b, b);
                }
            t.SetPixels32(px);
            t.Apply(true);
            goboCache[id] = t;
            return t;
        }

        /// The cookie of a light (preset or a picture of the project).
        Texture GoboOf(AtlasItem it)
        {
            if (it.kind != "spot" || string.IsNullOrEmpty(it.gobo)) return null;
            if (it.gobo.StartsWith("img:"))
            {
                var tex = LoadTexture(it.gobo.Substring(4));
                if (tex != null) tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            return GoboTexture(it.gobo);
        }

        void BuildGoboSection(AtlasItem it, Transform box)
        {
            if (it.kind == "spot")
            {
                var t = AtlasTheme.Label(box, "Gobo (forma que proyecta el foco)", 15, AtlasTheme.TextColor, true);
                AtlasTheme.Le(t.gameObject, 24f);
                var g = AtlasTheme.Grid(box, 3, InnerWidth, 36f, 4f);
                for (int i = 0; i < Gobos.Length; i++)
                {
                    string id = Gobos[i];
                    AtlasTheme.Chip(g, GoboNames[i], () => { it.gobo = id; ApplyLook(it); Commit(true); RefreshProps(); }, it.gobo == id);
                }
                AtlasTheme.TextButton(box, "image", it.gobo.StartsWith("img:") ? "Cambiar imagen del gobo…" : "Gobo con tu imagen…", () => PickGoboImage(it), false, 40f, 15);
            }
            AtlasTheme.SliderRow(box, "Parpadeo (luz rota)", 0f, 1f, it.flicker, "0.00", x => { it.flicker = x; UpdateLights(); }, () => Commit(true));
        }

        void PickGoboImage(AtlasItem it)
        {
            if (store == null || NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path) || store == null) return;
                try { it.gobo = "img:" + store.AddTextureFile(path); }
                catch (System.Exception e) { Toast("No se pudo usar la imagen: " + e.Message, true); return; }
                ApplyLook(it);
                Commit(true);
                RefreshProps();
                Toast("Gobo puesto: las zonas blancas de la imagen dejan pasar la luz");
            }, new string[] { "image/*" });
        }

        // ------------------------------------------------------------------
        // Flicker

        /// Lamps that blink (same on screen and in the render: by frame).
        void UpdateLights()
        {
            foreach (var it in items)
            {
                if (it == null || !it.IsLight) continue;
                var l = it.GetComponentInChildren<Light>(true);
                if (l == null) continue;
                float k = 1f;
                if (it.flicker > 0.001f)
                {
                    float f = frame;
                    float n = Mathf.PerlinNoise(f * 0.45f, it.id.GetHashCode() % 97 * 0.13f);
                    k = 1f - it.flicker * 0.6f * n;
                    // Short drop-outs, more often with more flicker.
                    int fi = Mathf.FloorToInt(f);
                    uint h = (uint)(fi * 2654435761u) ^ (uint)it.id.GetHashCode();
                    h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                    if ((h % 1000) / 1000f < it.flicker * 0.22f) k = 0.06f;
                }
                l.intensity = it.lightIntensity * k;
                var vol = it.transform.Find("Volumen");
                if (vol != null)
                {
                    var r = vol.GetComponent<Renderer>();
                    if (r != null) r.enabled = it.visible && it.volumetric && k > 0.1f;
                }
            }
        }

        // ------------------------------------------------------------------
        // Ambient occlusion (URP's SSAO, added to the renderer by the build)

        static UnityEngine.Rendering.Universal.ScriptableRendererFeature aoFeature;
        static bool aoLooked;

        static void SetAO(bool on)
        {
            if (!aoLooked)
            {
                aoLooked = true;
                try
                {
                    var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                    var fi = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var list = asset != null && fi != null ? fi.GetValue(asset) as UnityEngine.Rendering.Universal.ScriptableRendererData[] : null;
                    if (list != null)
                        foreach (var data in list)
                            if (data != null)
                                foreach (var f in data.rendererFeatures)
                                    if (f != null && f.name == "AtlasAO") aoFeature = f;
                }
                catch (System.Exception e) { Debug.LogWarning("ATLAS: oclusión ambiental: " + e.Message); }
            }
            if (aoFeature != null) aoFeature.SetActive(on);
        }

        bool AOWanted { get { return env.ao || (capturing && renderQuality >= 3); } }

        // ------------------------------------------------------------------
        // «Cine» render: many passes averaged

        /// Renders `samples` times with the lens moved over its opening
        /// (depth of field), the time moved inside the shutter (motion blur)
        /// and a sub-pixel shift (smooth edges), and averages them.
        Texture2D CinemaShot(int w, int h, AtlasItem rc, int samples, int first = 0)
        {
            float baseFrame = frame;
            Vector3 basePos = cam.transform.position;
            Quaternion baseRot = cam.transform.rotation;
            float shutter = Mathf.Clamp01(env.motionBlur);
            bool dof = env.dof;
            float focus = Mathf.Max(0.1f, env.focus);
            // The same lens as the view's depth of field (URP Bokeh: focal
            // length and f-number from «Cuánto desenfoca», 24 mm film), so
            // the render is as blurred as what you see, not more.
            float bl = Mathf.Clamp01(env.dofBlur);
            float fl = Mathf.Lerp(35f, 120f, bl) / 1000f, fNum = Mathf.Lerp(16f, 1.2f, bl);
            float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float lens = fl * fl / fNum * tanHalf / 0.024f;
            lens = Mathf.Min(lens, 0.05f * focus * tanHalf);
            float pixAngle = cam.fieldOfView / h;
            var sum = new int[w * h * 3];
            int done = 0;
            Ambience.SuspendFilm(true);
            try
            {
                for (int s = 0; s < samples; s++)
                {
                    // Halton points: evenly spread, the same each render.
                    float hx = Halton(s + first + 1, 2), hy = Halton(s + first + 1, 3), ht = Halton(s + first + 1, 5);
                    if (shutter > 0.001f && (samples > 1 || first > 0))
                    {
                        SetFrame(Mathf.Clamp(baseFrame + (ht - 0.5f) * shutter, 0f, endFrame));
                        if (rc != null)
                        {
                            Vector3 cp; Quaternion cq;
                            CameraPose(rc, frame, out cp, out cq);
                            basePos = cp; baseRot = cq;
                        }
                    }
                    Vector3 pos = basePos;
                    Quaternion rot = baseRot;
                    if (dof)
                    {
                        float ang = hx * Mathf.PI * 2f, rad = Mathf.Sqrt(hy) * lens;
                        Vector3 off = (baseRot * Vector3.right) * Mathf.Cos(ang) * rad + (baseRot * Vector3.up) * Mathf.Sin(ang) * rad;
                        Vector3 focusPoint = basePos + baseRot * Vector3.forward * focus;
                        pos = basePos + off;
                        rot = Quaternion.LookRotation(focusPoint - pos, baseRot * Vector3.up);
                    }
                    // Sub-pixel shift for smooth edges.
                    rot = rot * Quaternion.Euler((hy - 0.5f) * pixAngle, (hx - 0.5f) * pixAngle, 0f);
                    cam.transform.SetPositionAndRotation(pos, rot);
                    Texture2D t = null;
                    try { t = Shot(w, h, 1, 2, true); }
                    catch (System.Exception e) { Debug.LogWarning("ATLAS: cine: " + e.Message); }
                    if (t == null) continue;
                    var px = t.GetPixels32();
                    for (int i = 0; i < px.Length && i < w * h; i++)
                    {
                        sum[i * 3] += px[i].r;
                        sum[i * 3 + 1] += px[i].g;
                        sum[i * 3 + 2] += px[i].b;
                    }
                    Destroy(t);
                    done++;
                }
            }
            finally
            {
                Ambience.SuspendFilm(false);
                if (Mathf.Abs(frame - baseFrame) > 1e-4f) SetFrame(baseFrame);
            }
            if (done == 0) return null;
            var o = new Color32[w * h];
            for (int i = 0; i < o.Length; i++)
                o[i] = new Color32((byte)(sum[i * 3] / done), (byte)(sum[i * 3 + 1] / done), (byte)(sum[i * 3 + 2] / done), 255);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.SetPixels32(o);
            tex.Apply();
            return tex;
        }

        static float Halton(int i, int b)
        {
            float f = 1f, r = 0f;
            while (i > 0) { f /= b; r += f * (i % b); i /= b; }
            return r;
        }

        // ------------------------------------------------------------------
        // Camera cuts

        /// The camera the render uses at `f`: the last cut before it, else
        /// the active camera.
        AtlasItem CutCameraAt(float f)
        {
            if (env.cuts == null || env.cuts.Count == 0) return null;
            CamCut best = null;
            foreach (var c in env.cuts) if (c.frame <= f + 0.001f && (best == null || c.frame >= best.frame)) best = c;
            if (best == null) best = env.cuts[0];
            foreach (var it in items) if (it != null && it.id == best.id && it.kind == "camera") return it;
            return null;
        }

        void BuildCutSection(AtlasItem it, Transform box)
        {
            var t = AtlasTheme.Label(box, "Tomas (cortes entre cámaras)", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t.gameObject, 24f);
            int f = Mathf.RoundToInt(frame);
            AtlasTheme.TextButton(box, "film", "Cortar a esta cámara en el fotograma " + f, () =>
            {
                if (env.cuts == null) env.cuts = new List<CamCut>();
                env.cuts.RemoveAll(c => c.frame == f);
                env.cuts.Add(new CamCut { frame = f, id = it.id });
                env.cuts.Sort((a, b) => a.frame.CompareTo(b.frame));
                Commit(true);
                RefreshProps();
                Toast("Corte: desde el fotograma " + f + " el render usa «" + it.gameObject.name + "»");
            }, true, 42f, 14);
            if (env.cuts != null && env.cuts.Count > 0)
            {
                var g = AtlasTheme.Grid(box, 2, InnerWidth, 34f, 4f);
                foreach (var c0 in env.cuts)
                {
                    var c = c0;
                    string name = "?";
                    foreach (var o in items) if (o != null && o.id == c.id) name = o.gameObject.name;
                    AtlasTheme.Chip(g, "✕ " + c.frame + ": " + name, () => { env.cuts.Remove(c); Commit(true); RefreshProps(); });
                }
                Hint(box, "Toca un corte para quitarlo. Con cortes, el render salta de cámara en cada uno (como los planos de una película).");
            }
        }

        // ------------------------------------------------------------------
        // Motion tools (on the selected object's keys)

        void OpenMotionTools()
        {
            var it = selected;
            if (it == null || it.keys.Count < 2) { Toast("Selecciona un objeto con al menos 2 claves", true); return; }
            var l = new List<AtlasTheme.MenuItem>
            {
                AtlasTheme.MenuItem.Header("Herramientas de movimiento"),
                new AtlasTheme.MenuItem("curve", "Suavizar (movimiento más fluido)", () => MotionSmooth(it)),
                new AtlasTheme.MenuItem("sparkle", "Añadir temblor", () => MotionShake(it)),
                new AtlasTheme.MenuItem("loop", "Invertir (al revés)", () => MotionRetime(it, -1f)),
                new AtlasTheme.MenuItem("to_end", "Más lento (×2 de duración)", () => MotionRetime(it, 2f)),
                new AtlasTheme.MenuItem("to_start", "Más rápido (×0,5)", () => MotionRetime(it, 0.5f)),
                new AtlasTheme.MenuItem("delete", "Reducir claves (quitar las que sobran)", () => MotionReduce(it)),
                new AtlasTheme.MenuItem("key", "Rebote al final de cada movimiento", () => { foreach (var k in it.keys) k.interp = "back"; Commit(true); SetFrame(frame); RefreshKeys(); Toast("Rebote puesto"); }),
            };
            AtlasTheme.Popup(canvasRoot, new Vector2(Screen.width * 0.5f, Screen.height * 0.4f), l);
            toastHolder.SetAsLastSibling();
        }

        void MotionSmooth(AtlasItem it)
        {
            var ks = it.keys;
            if (ks.Count < 3) { Toast("Hacen falta 3 claves o más para suavizar", true); return; }
            var pos = new Vector3[ks.Count];
            var rot = new Quaternion[ks.Count];
            for (int i = 0; i < ks.Count; i++) { pos[i] = ks[i].position; rot[i] = ks[i].rotation; }
            var bonesNew = new List<List<BonePose>>();
            for (int i = 0; i < ks.Count; i++)
            {
                var nb = new List<BonePose>();
                if (ks[i].bones != null)
                    foreach (var b in ks[i].bones)
                    {
                        var c = b.Copy();
                        if (i > 0 && i < ks.Count - 1)
                        {
                            var pb = ks[i - 1].GetBone(b.path);
                            var qb = ks[i + 1].GetBone(b.path);
                            if (pb != null && qb != null)
                            {
                                c.r = Quaternion.Slerp(b.r, Quaternion.Slerp(pb.r, qb.r, 0.5f), 0.5f);
                                c.p = Vector3.Lerp(b.p, (pb.p + qb.p) * 0.5f, 0.5f);
                            }
                        }
                        nb.Add(c);
                    }
                bonesNew.Add(nb);
            }
            for (int i = 1; i < ks.Count - 1; i++)
            {
                ks[i].position = Vector3.Lerp(pos[i], (pos[i - 1] + pos[i + 1]) * 0.5f, 0.5f);
                ks[i].rotation = Quaternion.Slerp(rot[i], Quaternion.Slerp(rot[i - 1], rot[i + 1], 0.5f), 0.5f);
            }
            for (int i = 0; i < ks.Count; i++) ks[i].bones = bonesNew[i];
            foreach (var k in ks) k.interp = "";
            Commit(true);
            SetFrame(frame);
            RefreshKeys();
            Toast("Movimiento suavizado (se puede deshacer)");
        }

        void MotionShake(AtlasItem it)
        {
            int a = it.keys[0].frame, b = it.keys[it.keys.Count - 1].frame;
            var rnd = new System.Random(a * 31 + b);
            float size = it.GetComponentInChildren<Renderer>() != null ? Mathf.Max(0.05f, it.GetComponentInChildren<Renderer>().bounds.size.magnitude * 0.012f) : 0.03f;
            for (int f = a + 2; f < b; f += 2)
            {
                SetFrame(f);
                var k = it.GetOrAddKey(f);
                k.position = it.transform.position + new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * size;
                k.rotation = it.transform.rotation * Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 3f, ((float)rnd.NextDouble() - 0.5f) * 3f, ((float)rnd.NextDouble() - 0.5f) * 3f);
                k.skip &= ~(AtlasItem.ChPos | AtlasItem.ChRot);
            }
            Commit(true);
            SetFrame(frame);
            RefreshKeys();
            Toast("Temblor añadido entre el fotograma " + a + " y el " + b);
        }

        /// Removes keys that the keys around them already give (almost the
        /// same position, rotation, scale and bones halfway): lighter
        /// animations after lip sync, shake or imported motion.
        void MotionReduce(AtlasItem it)
        {
            int removed = 0;
            for (int pass = 0; pass < 3; pass++)
                for (int i = 1; i < it.keys.Count - 1; i++)
                {
                    var a = it.keys[i - 1]; var k = it.keys[i]; var b = it.keys[i + 1];
                    if (k.uvKey || k.glowKey || k.opKey || (k.morphs != null && k.morphs.Count > 0) || k.skip != a.skip || k.skip != b.skip) continue;
                    float t = (k.frame - a.frame) / (float)Mathf.Max(1, b.frame - a.frame);
                    if ((Vector3.Lerp(a.position, b.position, t) - k.position).magnitude > 0.005f) continue;
                    if (Quaternion.Angle(Quaternion.Slerp(a.rotation, b.rotation, t), k.rotation) > 1f) continue;
                    if ((Vector3.Lerp(a.scale, b.scale, t) - k.scale).magnitude > 0.005f) continue;
                    bool ok = true;
                    if (k.bones != null)
                        foreach (var kb in k.bones)
                        {
                            var ab = a.GetBone(kb.path); var bb = b.GetBone(kb.path);
                            if (ab == null || bb == null || Quaternion.Angle(Quaternion.Slerp(ab.r, bb.r, t), kb.r) > 1.5f || (Vector3.Lerp(ab.p, bb.p, t) - kb.p).magnitude > 0.003f) { ok = false; break; }
                        }
                    if (!ok) continue;
                    it.keys.RemoveAt(i);
                    i--;
                    removed++;
                }
            Commit(true);
            SetFrame(frame);
            RefreshKeys();
            Toast(removed > 0 ? removed + " claves que sobraban quitadas" : "No había claves que sobraran");
        }

        /// factor < 0: reverse; else stretch from the first key.
        void MotionRetime(AtlasItem it, float factor)
        {
            int a = it.keys[0].frame, b = it.keys[it.keys.Count - 1].frame;
            foreach (var k in it.keys)
            {
                if (factor < 0f) k.frame = a + (b - k.frame);
                else k.frame = a + Mathf.RoundToInt((k.frame - a) * factor);
            }
            // Keys that landed on the same frame: keep one.
            it.keys.Sort((x, y) => x.frame.CompareTo(y.frame));
            for (int i = it.keys.Count - 1; i > 0; i--) if (it.keys[i].frame == it.keys[i - 1].frame) it.keys.RemoveAt(i);
            if (factor < 0f)
            {
                // The curve of each segment belongs to its first key.
                var ip = new List<string>();
                foreach (var k in it.keys) ip.Add(k.interp);
                for (int i = 0; i < it.keys.Count; i++) it.keys[i].interp = i + 1 < ip.Count ? ip[ip.Count - 2 - i < 0 ? 0 : ip.Count - 2 - i] : "";
            }
            int last = it.keys[it.keys.Count - 1].frame;
            if (last > endFrame) SetRange(startFrame, last);
            Commit(true);
            SetFrame(frame);
            RefreshKeys();
            Toast(factor < 0f ? "Animación invertida" : factor > 1f ? "Animación más lenta" : "Animación más rápida");
        }

        // ------------------------------------------------------------------
        // Ready-made sets

        public static readonly string[] SetIds = { "stage", "corridor", "forest", "city" };
        public static readonly string[] SetNames = { "Escenario de show", "Pasillo de terror", "Bosque nocturno", "Ciudad de noche" };

        AtlasItem SetPiece(string kind, string name, Vector3 pos, Vector3 rot, Vector3 scale, Color color, System.Action<ItemData> tweak = null)
        {
            var d = new ItemData { v = 3, kind = kind, name = name, position = pos, rotation = rot, scale = scale, color = color };
            if (tweak != null) tweak(d);
            return Create(d);
        }

        /// A tiled pattern saved in the project's textures (checker floor,
        /// bricks, building windows).
        string PatternTexture(string id)
        {
            if (store == null) return "";
            string file = "atlas_" + id + ".png";
            string path = store.TexturePath(file);
            if (File.Exists(path)) return file;
            const int N = 256;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Color c = Color.white;
                    if (id == "checker") c = ((x / 32) + (y / 32)) % 2 == 0 ? new Color(0.92f, 0.92f, 0.9f) : new Color(0.06f, 0.06f, 0.07f);
                    else if (id == "bricks")
                    {
                        int row = y / 32, off = row % 2 == 0 ? 0 : 32;
                        bool mortar = y % 32 < 3 || (x + off) % 64 < 3;
                        float n = Mathf.PerlinNoise(x * 0.08f, y * 0.08f) * 0.25f;
                        c = mortar ? new Color(0.25f, 0.24f, 0.24f) : new Color(0.55f + n, 0.22f + n * 0.5f, 0.18f + n * 0.4f);
                    }
                    else if (id == "windows")
                    {
                        int cx = x / 32, cy = y / 32;
                        bool win = x % 32 > 8 && x % 32 < 24 && y % 32 > 6 && y % 32 < 26;
                        bool lit = ((cx * 7 + cy * 13) % 5) < 2;
                        c = win ? (lit ? new Color(1f, 0.85f, 0.5f) : new Color(0.12f, 0.14f, 0.2f)) : new Color(0.16f, 0.17f, 0.2f);
                    }
                    px[y * N + x] = c;
                }
            t.SetPixels32(px);
            t.Apply();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, t.EncodeToPNG());
            }
            catch (System.Exception e) { Debug.LogWarning("ATLAS: textura: " + e.Message); file = ""; }
            Destroy(t);
            return file;
        }

        void AddSet(string id)
        {
            CloseMenus();
            if (store == null) return;
            Vector3 o = new Vector3(target.position.x, 0f, target.position.z);
            switch (id)
            {
                case "stage":
                    {
                        string bricks = PatternTexture("bricks");
                        SetPiece("cube", "Tarima", o + new Vector3(0f, 0.25f, 0f), Vector3.zero, new Vector3(10f, 0.5f, 5f), new Color(0.32f, 0.2f, 0.12f), d => { d.smoothness = 0.55f; });
                        SetPiece("cube", "Pared", o + new Vector3(0f, 3f, 2.6f), Vector3.zero, new Vector3(10f, 6f, 0.3f), Color.white, d => { d.texture = bricks; });
                        SetPiece("cube", "Telón izq.", o + new Vector3(-4.6f, 3f, 2.2f), Vector3.zero, new Vector3(1.2f, 6f, 0.4f), new Color(0.65f, 0.04f, 0.06f), d => { d.smoothness = 0.25f; });
                        SetPiece("cube", "Telón der.", o + new Vector3(4.6f, 3f, 2.2f), Vector3.zero, new Vector3(1.2f, 6f, 0.4f), new Color(0.65f, 0.04f, 0.06f), d => { d.smoothness = 0.25f; });
                        SetPiece("cube", "Altavoz izq.", o + new Vector3(-3.6f, 1.2f, -1.6f), Vector3.zero, new Vector3(1f, 1.4f, 0.8f), new Color(0.05f, 0.05f, 0.06f));
                        SetPiece("cube", "Altavoz der.", o + new Vector3(3.6f, 1.2f, -1.6f), Vector3.zero, new Vector3(1f, 1.4f, 0.8f), new Color(0.05f, 0.05f, 0.06f));
                        Color[] cols = { new Color(0.75f, 0.2f, 1f), new Color(1f, 0.8f, 0.25f), new Color(1f, 0.2f, 0.25f) };
                        for (int i = 0; i < 3; i++)
                        {
                            float x = (i - 1) * 3f;
                            var c = cols[i];
                            SetPiece("spot", "Foco " + (i + 1), o + new Vector3(x, 6.5f, -2.5f), new Vector3(60f, -x * 4f, 0f), Vector3.one, c, d =>
                            {
                                d.lightIntensity = 25f; d.lightRange = 14f; d.spotAngle = 34f; d.volumetric = true; d.volIntensity = 1.3f;
                            });
                        }
                        SetPiece("spot", "Foco central", o + new Vector3(0f, 7.5f, -0.5f), new Vector3(85f, 0f, 0f), Vector3.one, new Color(1f, 0.92f, 0.75f), d =>
                        {
                            d.lightIntensity = 30f; d.lightRange = 14f; d.spotAngle = 26f; d.volumetric = true; d.volIntensity = 1.8f; d.gobo = "circle";
                        });
                        env.preset = "stage";
                        Ambience.Reset(env, "stage");
                        break;
                    }
                case "corridor":
                    {
                        string checker = PatternTexture("checker");
                        var floor = SetPiece("plane", "Suelo de ajedrez", o + new Vector3(0f, 0.01f, 6f), Vector3.zero, new Vector3(0.4f, 1f, 1.6f), Color.white, d => { d.texture = checker; d.smoothness = 0.6f; d.uv = new Vector4(2f, 8f, 0f, 0f); });
                        SetPiece("cube", "Pared izq.", o + new Vector3(-2.1f, 1.6f, 6f), Vector3.zero, new Vector3(0.2f, 3.2f, 16f), new Color(0.32f, 0.36f, 0.42f));
                        SetPiece("cube", "Pared der.", o + new Vector3(2.1f, 1.6f, 6f), Vector3.zero, new Vector3(0.2f, 3.2f, 16f), new Color(0.32f, 0.36f, 0.42f));
                        SetPiece("cube", "Techo", o + new Vector3(0f, 3.25f, 6f), Vector3.zero, new Vector3(4.4f, 0.1f, 16f), new Color(0.12f, 0.12f, 0.14f));
                        for (int i = 0; i < 4; i++)
                        {
                            int k = i;
                            SetPiece("light", "Lámpara " + (i + 1), o + new Vector3(0f, 2.9f, 0.5f + i * 4f), Vector3.zero, Vector3.one, new Color(0.85f, 0.95f, 1f), d =>
                            {
                                d.lightIntensity = 6f; d.lightRange = 5f;
                                d.flicker = k == 2 ? 0.8f : k == 0 ? 0.2f : 0f;
                            });
                        }
                        env.preset = "terror";
                        Ambience.Reset(env, "terror");
                        break;
                    }
                case "forest":
                    {
                        AddWorldPiece("terrain", "hills");
                        SetPiece("sun", "Luna", o + new Vector3(0f, 8f, 0f), new Vector3(35f, 140f, 0f), Vector3.one, new Color(0.55f, 0.65f, 1f), d => { d.lightIntensity = 0.6f; });
                        AddFx("dust");
                        env.preset = "misty";
                        Ambience.Reset(env, "misty");
                        break;
                    }
                case "city":
                    {
                        string win = PatternTexture("windows");
                        SetPiece("plane", "Calle", o + new Vector3(0f, 0.01f, 0f), Vector3.zero, new Vector3(4f, 1f, 4f), new Color(0.12f, 0.12f, 0.13f), d => { d.smoothness = 0.7f; });
                        var rnd = new System.Random(5);
                        for (int gx = -2; gx <= 2; gx++)
                            for (int gz = -2; gz <= 2; gz++)
                            {
                                if (gx == 0 || gz == 0) continue;   // the streets
                                float hgt = 4f + (float)rnd.NextDouble() * 12f;
                                SetPiece("cube", "Edificio", o + new Vector3(gx * 6f, hgt * 0.5f, gz * 6f), Vector3.zero, new Vector3(4f, hgt, 4f), Color.white, d =>
                                {
                                    d.texture = win; d.emission = 0.9f; d.uv = new Vector4(1f, hgt / 4f, 0f, 0f);
                                });
                            }
                        for (int i = -1; i <= 1; i += 2)
                            SetPiece("light", "Farola", o + new Vector3(i * 1.5f, 3f, i * 1.5f), Vector3.zero, Vector3.one, new Color(1f, 0.7f, 0.35f), d => { d.lightIntensity = 10f; d.lightRange = 9f; });
                        env.preset = "misty";
                        Ambience.Reset(env, "misty");
                        break;
                    }
            }
            ApplyEnv();
            SetFrame(frame);
            Commit(true);
            RefreshProps();
            Toast("Escenario «" + SetNames[System.Array.IndexOf(SetIds, id)] + "» listo: cada pieza se puede mover, cambiar o borrar");
        }

        // ------------------------------------------------------------------
        // Face sliders (blend shapes) in the inspector

        string morphFilter = "";

        void BuildMorphSection(AtlasItem it)
        {
            var names = it.MorphNames;
            if (names.Count == 0) return;
            var box = Sec("Cara y formas (" + names.Count + ")", "person", false);
            Hint(box, "Deslizadores de la cara y del cuerpo que trae el modelo (sonrisa, parpadeo, cejas, bocas…). Mueve y guarda una clave para animarlos.");
            var filter = AtlasTheme.TextField(box, morphFilter, null, 40f);
            var ph = AtlasTheme.Label(filter.transform, "Buscar (boca, ojo, smile, blink…)", 15, AtlasTheme.TextDim);
            AtlasTheme.Fill(ph.rectTransform, 12f, 12f);
            filter.placeholder = ph;
            var rows = new List<GameObject>();
            var keysOf = new List<string>();
            int shown = 0;
            foreach (var n0 in names)
            {
                string n = n0;
                var row = AtlasTheme.Rect(box, "Forma");
                AtlasTheme.Column(row.gameObject, 0f, new RectOffset(0, 0, 0, 0));
                AtlasTheme.SliderRow(row, n, 0f, 100f, it.MorphValueOf(n), "0", x => it.SetMorph(n, x), () => Commit(true));
                rows.Add(row.gameObject);
                keysOf.Add(n.ToLowerInvariant());
                bool vis = morphFilter == "" || n.ToLowerInvariant().Contains(morphFilter);
                if (vis && shown >= 40) vis = false;
                if (vis) shown++;
                row.gameObject.SetActive(vis);
            }
            filter.onValueChanged.AddListener(q =>
            {
                morphFilter = q.Trim().ToLowerInvariant();
                int c = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    bool v = (morphFilter == "" || keysOf[i].Contains(morphFilter)) && c < 40;
                    if (v) c++;
                    rows[i].SetActive(v);
                }
            });
            var g = AtlasTheme.Grid(box, 2, InnerWidth, 42f);
            AtlasTheme.TextButton(g, "key", "Clave de formas (◆)", () =>
            {
                it.SetKey(Mathf.RoundToInt(frame), AtlasItem.ChMorph, new List<string>());
                Commit(true);
                RefreshKeys();
                Toast("Clave de formas en el fotograma " + Mathf.RoundToInt(frame));
            }, true, 42f, 14);
            AtlasTheme.TextButton(g, "undo", "Todo a cero", () => { it.ResetMorphs(); Commit(true); RefreshProps(); }, false, 42f, 14);
            if (audioSrc != null && audioSrc.clip != null && MouthShapes(it).Count > 0)
                AtlasTheme.TextButton(box, "music", "Sincronizar labios con las formas de la boca", () => LipSyncShapes(it), true, 44f, 15);
        }

        static readonly string[][] Visemes =
        {
            new[] { "jawopen", "mouthopen", "mouth_open", "open", "aa", "v_aa", "mth_a", "mouth_a", "vowel_a", "a" },
            new[] { "oh", "v_oh", "ou", "v_ou", "mth_o", "mth_u", "mouth_o", "mouth_u", "vowel_o", "o", "u", "funnel", "pucker" },
            new[] { "ee", "ih", "v_ee", "v_ih", "mth_i", "mth_e", "mouth_i", "mouth_e", "vowel_i", "vowel_e", "i", "e", "smile", "wide" },
        };

        /// [open (A), round (O/U), wide (E/I)] shape names found on the model.
        List<string> MouthShapes(AtlasItem it)
        {
            var r = new List<string>();
            var names = it.MorphNames;
            foreach (var group in Visemes)
            {
                string found = "";
                foreach (var key in group)
                {
                    foreach (var n in names)
                    {
                        string l = n.ToLowerInvariant().Replace(".", "_");
                        string last = l.Contains("_") ? l.Substring(l.LastIndexOf('_') + 1) : l;
                        if (l == key || last == key || (key.Length > 2 && l.Contains(key))) { found = n; break; }
                    }
                    if (found != "") break;
                }
                if (found != "" && !r.Contains(found)) r.Add(found);
                else if (r.Count == 0) return r;   // without an «open» shape there is nothing to do
            }
            return r;
        }

        /// Mouth shapes from the voice: loudness opens it; the sound's
        /// brightness picks round (O/U) or wide (E/I) mouths.
        void LipSyncShapes(AtlasItem it)
        {
            var shapes = MouthShapes(it);
            var clip = audioSrc != null ? audioSrc.clip : null;
            if (shapes.Count == 0 || clip == null) { Toast("Falta el audio o el modelo no tiene formas de boca", true); return; }
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) { Toast("No se pudo leer el audio", true); return; }
            int frames = Mathf.CeilToInt(clip.length * fps);
            int per = Mathf.Max(1, clip.frequency / fps) * clip.channels;
            var level = new float[frames];
            var zcr = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                double sum = 0;
                int cross = 0;
                int a = f * per, b = Mathf.Min(data.Length, a + per);
                for (int i = a; i < b; i++)
                {
                    sum += data[i] * data[i];
                    if (i > a && (data[i] >= 0f) != (data[i - clip.channels < a ? a : i - clip.channels] >= 0f)) cross++;
                }
                level[f] = b > a ? Mathf.Sqrt((float)(sum / (b - a))) : 0f;
                zcr[f] = b > a ? cross / (float)(b - a) : 0f;
            }
            var sorted = (float[])level.Clone();
            System.Array.Sort(sorted);
            float top = Mathf.Max(1e-4f, sorted[Mathf.Clamp((int)(sorted.Length * 0.95f), 0, sorted.Length - 1)]);
            var zs = (float[])zcr.Clone();
            System.Array.Sort(zs);
            float zMid = zs[zs.Length / 2];
            int from = audioStart, to = Mathf.Min(endFrame, audioStart + frames);
            foreach (var k in it.keys)
                if (k.frame >= from && k.frame <= to && k.morphs != null)
                    k.morphs.RemoveAll(m => shapes.Contains(m.name));
            it.keys.RemoveAll(k => k.Empty);
            float open = 0f;
            for (int f = from; f <= to; f++)
            {
                int i = f - audioStart;
                float target = i >= 0 && i < frames ? Mathf.Clamp01((level[i] / top - 0.12f) / 0.88f) : 0f;
                open = target > open ? Mathf.Lerp(open, target, 0.8f) : Mathf.Lerp(open, target, 0.45f);
                if ((f - from) % 2 != 0 && f != to) continue;
                float bright = i >= 0 && i < frames ? Mathf.Clamp((zcr[i] - zMid) / Mathf.Max(1e-4f, zMid), -1f, 1f) : 0f;
                foreach (var s in shapes) it.SetMorph(s, 0f);
                it.SetMorph(shapes[0], open * 100f * (shapes.Count > 1 ? 0.75f : 1f));
                if (shapes.Count > 1 && bright < 0f) it.SetMorph(shapes[1], open * -bright * 100f);
                if (shapes.Count > 2 && bright > 0f) it.SetMorph(shapes[2], open * bright * 100f);
                var keep = new List<MorphValue>(it.morphs);
                it.morphs.RemoveAll(m => !shapes.Contains(m.name));
                it.SetKey(f, AtlasItem.ChMorph, new List<string>());
                it.morphs = keep;
            }
            foreach (var s in shapes) it.SetMorph(s, 0f);
            SetFrame(frame);
            Commit(true);
            RefreshKeys();
            Toast("Boca animada con " + shapes.Count + " formas: " + (to - from) + " fotogramas (se puede deshacer)");
        }
    }
}
