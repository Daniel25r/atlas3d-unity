// ATLAS 5D (Unity) - «Editor de curvas» (like Blender's Graph Editor, made
// for the phone): the selected object's (or bone's) values drawn over the
// timeline, one line per axis. Tap between two keys to choose that stretch
// and shape how it moves: drag the two handles of its curve (start slow,
// end fast, overshoot…) or pick a ready-made one. The curve applies from
// that key to the next.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        GameObject curvePanel;
        RawImage curveView, curveMini;
        Texture2D curveTex, curveMiniTex;
        Color32[] curveBase, curvePx, miniPx;
        Text curveTitle, curveSegLabel;
        RectTransform curveChipRow;
        const int CurveW = 768, CurveH = 240, MiniRes = 160;
        const float MiniMin = -0.5f, MiniMax = 1.5f;

        struct CurveChannel { public string track, label; public bool bonePos; public int comps; }
        readonly List<CurveChannel> curveChannels = new List<CurveChannel>();
        int curveCh;
        string curveFor = "";
        int curveSegA = -1, curveSegB = -1;
        float curveF0, curveF1, curveV0, curveV1;
        int curveDrawnFrame = int.MinValue;
        int curveHandle = -1;
        bool curveEdited;

        bool CurvesOpen { get { return curvePanel != null && curvePanel.activeSelf; } }

        void ToggleCurveEditor()
        {
            if (CurvesOpen) { curvePanel.SetActive(false); return; }
            if (selected == null) { Toast("Elige primero un objeto animado (o un hueso en modo pose)"); return; }
            if (curvePanel == null) BuildCurvePanel();
            curvePanel.SetActive(true);
            curvePanel.transform.SetAsLastSibling();
            curveFor = "";
            UpdateCurves(true);
            Toast("Curvas: toca entre dos claves para elegir ese tramo y cambia cómo se mueve con los dos tiradores");
        }

        void BuildCurvePanel()
        {
            float w = Mathf.Clamp(viewArea.rect.width - 20f, 360f, 1100f);
            const float h = 330f;
            var p = AtlasTheme.Box(viewArea, "EditorCurvas", AtlasTheme.Panel2, 14);
            AtlasTheme.Pin(p.rectTransform, 0.5f, 0f, new Vector2(0f, 10f), new Vector2(w, h));
            curvePanel = p.gameObject;
            curveTitle = AtlasTheme.Label(p.transform, "CURVAS", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Pin(curveTitle.rectTransform, 0f, 1f, new Vector2(14f, -6f), new Vector2(260f, 26f));
            var x = AtlasTheme.IconButton(p.transform, "close", () => curvePanel.SetActive(false), 34f, 18f);
            AtlasTheme.Pin((RectTransform)x.transform, 1f, 1f, new Vector2(-4f, -2f), new Vector2(34f, 34f));
            // Channel chips (rebuilt for each object).
            var chips = AtlasTheme.Rect(p.transform, "Canales");
            AtlasTheme.Pin(chips, 0f, 1f, new Vector2(280f, -4f), new Vector2(w - 330f, 30f));
            curveChipRow = chips;
            // The graph.
            float side = 190f;
            float gw = w - side - 34f, gh = h - 54f;
            curveView = new GameObject("Grafica").AddComponent<RawImage>();
            curveView.transform.SetParent(p.transform, false);
            AtlasTheme.Pin(curveView.rectTransform, 0f, 0f, new Vector2(12f, 12f), new Vector2(gw, gh));
            curveTex = new Texture2D(CurveW, CurveH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            curveView.texture = curveTex;
            curveView.gameObject.AddComponent<CurveInput>().app = this;
            // The stretch's curve and ready-made shapes.
            curveSegLabel = AtlasTheme.Label(p.transform, "Toca entre dos claves", 13, AtlasTheme.TextColor, true, TextAnchor.MiddleCenter);
            AtlasTheme.Pin(curveSegLabel.rectTransform, 1f, 1f, new Vector2(-12f, -38f), new Vector2(side, 22f));
            curveMini = new GameObject("Tramo").AddComponent<RawImage>();
            curveMini.transform.SetParent(p.transform, false);
            AtlasTheme.Pin(curveMini.rectTransform, 1f, 1f, new Vector2(-12f - (side - 120f) * 0.5f, -62f), new Vector2(120f, 120f));
            curveMiniTex = new Texture2D(MiniRes, MiniRes, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            curveMini.texture = curveMiniTex;
            curveMini.gameObject.AddComponent<CurveHandleInput>().app = this;
            var g = AtlasTheme.Grid(p.transform, 3, side, 28f, 4f);
            AtlasTheme.Pin((RectTransform)g, 1f, 0f, new Vector2(-12f, 12f), new Vector2(side, 28f * 3 + 8f));
            ((RectTransform)g).pivot = new Vector2(1f, 0f);
            CurvePreset(g, "Lineal", Interp.Linear);
            CurvePreset(g, "Suave", Interp.EaseInOut);
            CurvePreset(g, "Acelerar", Interp.EaseIn);
            CurvePreset(g, "Frenar", Interp.EaseOut);
            CurvePreset(g, "Pesada", Interp.Heavy);
            CurvePreset(g, "Pasarse", Interp.Back);
            CurvePreset(g, "Rebote", Interp.Bounce);
            CurvePreset(g, "Muelle", Interp.Elastic);
            CurvePreset(g, "Salto", Interp.Constant);
        }

        void CurvePreset(Transform g, string label, string mode)
        {
            AtlasTheme.Chip(g, label, () => SetSegmentCurve(mode, true));
        }

        // ------------------------------------------------------------------
        // Channels

        void RebuildCurveChannels()
        {
            curveChannels.Clear();
            if (selected == null) return;
            if (poseMode && activeBone != "")
            {
                string bt = "b:" + activeBone;
                curveChannels.Add(new CurveChannel { track = bt, label = "Giro", comps = 3 });
                curveChannels.Add(new CurveChannel { track = bt, label = "Posición", bonePos = true, comps = 3 });
            }
            else
            {
                var tracks = selected.Tracks();
                if (tracks.Contains("T")) curveChannels.Add(new CurveChannel { track = "T", label = "Posición", comps = 3 });
                if (tracks.Contains("R")) curveChannels.Add(new CurveChannel { track = "R", label = "Rotación", comps = 3 });
                if (tracks.Contains("S")) curveChannels.Add(new CurveChannel { track = "S", label = "Escala", comps = 3 });
                if (tracks.Contains("O")) curveChannels.Add(new CurveChannel { track = "O", label = "Opacidad", comps = 1 });
                if (tracks.Contains("G")) curveChannels.Add(new CurveChannel { track = "G", label = "Brillo", comps = 1 });
                foreach (var t in tracks)
                    if (t.StartsWith("m:") && curveChannels.Count < 8)
                        curveChannels.Add(new CurveChannel { track = t, label = t.Substring(2), comps = 1 });
                if (curveChannels.Count == 0) curveChannels.Add(new CurveChannel { track = "T", label = "Posición", comps = 3 });
            }
            curveCh = Mathf.Clamp(curveCh, 0, curveChannels.Count - 1);
            AtlasTheme.Clear(curveChipRow);
            var g = AtlasTheme.Grid(curveChipRow, Mathf.Max(1, Mathf.Min(curveChannels.Count, 6)), curveChipRow.rect.width, 28f, 4f);
            AtlasTheme.Pin((RectTransform)g, 0f, 1f, Vector2.zero, new Vector2(curveChipRow.rect.width, 28f));
            for (int i = 0; i < curveChannels.Count && i < 6; i++)
            {
                int k = i;
                AtlasTheme.Chip(g, curveChannels[i].label, () => { curveCh = k; curveSegA = curveSegB = -1; RebuildCurveChannels(); DrawCurves(); }, i == curveCh);
            }
        }

        /// Called every frame from LateUpdate: follows the selection and the playhead.
        void UpdateCurves(bool force = false)
        {
            if (!CurvesOpen) return;
            if (selected == null) { curvePanel.SetActive(false); return; }
            string who = selected.id + "|" + (poseMode ? activeBone : "") + "|" + selected.keys.Count;
            if (force || who != curveFor)
            {
                curveFor = who;
                curveSegA = curveSegB = -1;
                RebuildCurveChannels();
                DrawCurves();
                return;
            }
            int f = Mathf.RoundToInt(frame);
            if (f != curveDrawnFrame) DrawPlayhead();
        }

        List<int> CurveKeys()
        {
            if (curveChannels.Count == 0 || selected == null) return new List<int>();
            return selected.TrackFrames(curveChannels[curveCh].track);
        }

        // ------------------------------------------------------------------
        // Drawing

        static readonly Color32[] AxisColor = { new Color32(235, 82, 82, 255), new Color32(110, 205, 90, 255), new Color32(80, 150, 245, 255) };

        void DrawCurves()
        {
            if (!CurvesOpen || curveTex == null || selected == null || curveChannels.Count == 0) return;
            var ch = curveChannels[curveCh];
            var keysF = CurveKeys();
            curveF0 = startFrame;
            curveF1 = Mathf.Max(startFrame + 1, endFrame);
            if (keysF.Count > 0) { curveF0 = Mathf.Min(curveF0, keysF[0]); curveF1 = Mathf.Max(curveF1, keysF[keysF.Count - 1]); }
            string name = selected.gameObject.name + (ch.track.StartsWith("b:") ? " · " + selected.BoneLabel(ch.track.Substring(2)) : "");
            curveTitle.text = "CURVAS · " + name.ToUpperInvariant();
            // Samples (rotations unwrapped so a turn past 360° stays one line).
            var vals = new Vector3[CurveW];
            var ok = new bool[CurveW];
            float vmin = float.MaxValue, vmax = float.MinValue;
            Vector3 prev = Vector3.zero;
            bool havePrev = false, rot = ch.track == "R" || (ch.track.StartsWith("b:") && !ch.bonePos);
            for (int i = 0; i < CurveW; i++)
            {
                float f = Mathf.Lerp(curveF0, curveF1, i / (CurveW - 1f));
                Vector3 v;
                if (!selected.TrackSample(ch.track, f, ch.bonePos, out v)) continue;
                if (rot && havePrev)
                    for (int c = 0; c < 3; c++) v[c] = prev[c] + Mathf.DeltaAngle(prev[c], v[c]);
                if (ch.track == "O") v.x *= 100f;
                vals[i] = v; ok[i] = true; prev = v; havePrev = true;
                for (int c = 0; c < ch.comps; c++) { vmin = Mathf.Min(vmin, v[c]); vmax = Mathf.Max(vmax, v[c]); }
            }
            if (vmin > vmax) { vmin = -1f; vmax = 1f; }
            float pad = Mathf.Max(0.05f, (vmax - vmin) * 0.12f);
            curveV0 = vmin - pad; curveV1 = vmax + pad;
            // Background, the chosen stretch, grid, keys.
            if (curveBase == null) curveBase = new Color32[CurveW * CurveH];
            var bg = new Color32(24, 25, 30, 255);
            var segBg = new Color32(40, 44, 58, 255);
            int sa = curveSegA >= 0 ? FrameToCurveX(curveSegA) : -1, sb = curveSegB >= 0 ? FrameToCurveX(curveSegB) : -1;
            for (int y = 0; y < CurveH; y++)
                for (int xx = 0; xx < CurveW; xx++)
                    curveBase[y * CurveW + xx] = sa >= 0 && xx >= sa && xx <= sb ? segBg : bg;
            var grid = new Color32(44, 46, 54, 255);
            for (int k = 1; k < 4; k++) HLine(curveBase, k * CurveH / 4, grid);
            if (curveV0 < 0f && curveV1 > 0f) HLine(curveBase, ValueToCurveY(0f), new Color32(70, 72, 84, 255));
            foreach (int kf in keysF) VLine(curveBase, FrameToCurveX(kf), new Color32(60, 62, 74, 255));
            // The lines.
            for (int c = 0; c < ch.comps; c++)
            {
                var col = ch.comps == 1 ? new Color32(240, 190, 70, 255) : AxisColor[c];
                int lx = -1, ly = 0;
                for (int i = 0; i < CurveW; i++)
                {
                    if (!ok[i]) { lx = -1; continue; }
                    int yy = ValueToCurveY(vals[i][c]);
                    if (lx >= 0) { Line(curveBase, CurveW, CurveH, new Vector2Int(lx, ly), new Vector2Int(i, yy), col); Line(curveBase, CurveW, CurveH, new Vector2Int(lx, ly + 1), new Vector2Int(i, yy + 1), col); }
                    lx = i; ly = yy;
                }
                // Key points.
                foreach (int kf in keysF)
                {
                    Vector3 v;
                    if (!selected.TrackSample(ch.track, kf, ch.bonePos, out v)) continue;
                    if (ch.track == "O") v.x *= 100f;
                    int px = FrameToCurveX(kf), py = ValueToCurveY(rot ? NearestUnwrapped(vals, ok, px, c, v[c]) : v[c]);
                    bool on = kf == curveSegA;
                    Dot(curveBase, px, py, on ? 5 : 4, on ? new Color32(255, 176, 46, 255) : new Color32(240, 240, 245, 255));
                }
            }
            curveDrawnFrame = int.MinValue;
            DrawPlayhead();
            DrawMiniCurve();
        }

        static float NearestUnwrapped(Vector3[] vals, bool[] ok, int px, int c, float v)
        {
            px = Mathf.Clamp(px, 0, vals.Length - 1);
            return ok[px] ? vals[px][c] : v;
        }

        void DrawPlayhead()
        {
            if (curveBase == null || curveTex == null) return;
            if (curvePx == null) curvePx = new Color32[CurveW * CurveH];
            System.Array.Copy(curveBase, curvePx, curvePx.Length);
            int f = Mathf.RoundToInt(frame);
            int x = FrameToCurveX(f);
            for (int y = 0; y < CurveH; y++)
                for (int dx = 0; dx < 2; dx++)
                {
                    int xx = x + dx;
                    if (xx >= 0 && xx < CurveW) curvePx[y * CurveW + xx] = new Color32(255, 140, 40, 255);
                }
            curveTex.SetPixels32(curvePx);
            curveTex.Apply(false);
            curveDrawnFrame = f;
        }

        int FrameToCurveX(float f) { return Mathf.RoundToInt((f - curveF0) / Mathf.Max(1e-4f, curveF1 - curveF0) * (CurveW - 1)); }
        float CurveXToFrame(float x) { return curveF0 + x / (CurveW - 1f) * (curveF1 - curveF0); }
        int ValueToCurveY(float v) { return Mathf.Clamp(Mathf.RoundToInt((v - curveV0) / Mathf.Max(1e-5f, curveV1 - curveV0) * (CurveH - 1)), 0, CurveH - 2); }

        static void HLine(Color32[] px, int y, Color32 c)
        {
            if (y < 0 || y >= CurveH) return;
            for (int x = 0; x < CurveW; x++) px[y * CurveW + x] = c;
        }

        static void VLine(Color32[] px, int x, Color32 c)
        {
            if (x < 0 || x >= CurveW) return;
            for (int y = 0; y < CurveH; y++) px[y * CurveW + x] = c;
        }

        static void Dot(Color32[] px, int cx, int cy, int r, Color32 c)
        {
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                    if (x >= 0 && y >= 0 && x < CurveW && y < CurveH && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) px[y * CurveW + x] = c;
        }

        static void Line(Color32[] px, int w, int h, Vector2Int a, Vector2Int b, Color32 c)
        {
            int dx = Mathf.Abs(b.x - a.x), dy = -Mathf.Abs(b.y - a.y);
            int sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1, err = dx + dy;
            int x = a.x, y = a.y;
            for (int n = 0; n < 8192; n++)
            {
                if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = c;
                if (x == b.x && y == b.y) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }

        // ------------------------------------------------------------------
        // The chosen stretch's curve (0..1 in time and in value)

        string SegmentMode()
        {
            if (curveSegA < 0 || selected == null) return "";
            var k = selected.KeyAt(curveSegA);
            return k != null ? k.interp : "";
        }

        Vector2Int MiniPx(float x, float y)
        {
            return new Vector2Int(Mathf.RoundToInt(x * (MiniRes - 1)), Mathf.RoundToInt((y - MiniMin) / (MiniMax - MiniMin) * (MiniRes - 1)));
        }

        void DrawMiniCurve()
        {
            if (curveMiniTex == null) return;
            if (miniPx == null) miniPx = new Color32[MiniRes * MiniRes];
            var bg = new Color32(24, 25, 30, 255);
            for (int i = 0; i < miniPx.Length; i++) miniPx[i] = bg;
            var box = new Color32(56, 58, 70, 255);
            var p00 = MiniPx(0f, 0f); var p11 = MiniPx(1f, 1f);
            Line(miniPx, MiniRes, MiniRes, new Vector2Int(p00.x, p00.y), new Vector2Int(p11.x, p00.y), box);
            Line(miniPx, MiniRes, MiniRes, new Vector2Int(p00.x, p11.y), new Vector2Int(p11.x, p11.y), box);
            if (curveSegA < 0)
            {
                curveSegLabel.text = "Toca entre dos claves";
                curveMiniTex.SetPixels32(miniPx);
                curveMiniTex.Apply(false);
                return;
            }
            string mode = SegmentMode();
            curveSegLabel.text = "Tramo " + curveSegA + " → " + curveSegB + " · " + Lang.T(Interp.Name(mode));
            var col = new Color32(255, 176, 46, 255);
            Vector2Int last = MiniPx(0f, mode == Interp.Constant ? 0f : Interp.Shape(mode, 0f));
            for (int i = 1; i <= 100; i++)
            {
                float t = i / 100f;
                float y = mode == Interp.Constant ? (t < 1f ? 0f : 1f) : Interp.Shape(mode, t);
                var p = MiniPx(t, y);
                Line(miniPx, MiniRes, MiniRes, last, p, col);
                Line(miniPx, MiniRes, MiniRes, new Vector2Int(last.x, last.y + 1), new Vector2Int(p.x, p.y + 1), col);
                last = p;
            }
            // The two handles (shown for every shape; dragging one makes the
            // curve your own).
            var h = Interp.HandlesFor(mode);
            var hc = new Color32(120, 190, 255, 255);
            var h1 = MiniPx(h.x, h.y); var h2 = MiniPx(h.z, h.w);
            Line(miniPx, MiniRes, MiniRes, p00, h1, hc);
            Line(miniPx, MiniRes, MiniRes, p11, h2, hc);
            MiniDot(h1, 6, hc);
            MiniDot(h2, 6, hc);
            curveMiniTex.SetPixels32(miniPx);
            curveMiniTex.Apply(false);
        }

        void MiniDot(Vector2Int c, int r, Color32 col)
        {
            for (int y = c.y - r; y <= c.y + r; y++)
                for (int x = c.x - r; x <= c.x + r; x++)
                    if (x >= 0 && y >= 0 && x < MiniRes && y < MiniRes && (x - c.x) * (x - c.x) + (y - c.y) * (y - c.y) <= r * r) miniPx[y * MiniRes + x] = col;
        }

        // ------------------------------------------------------------------
        // Touch

        Vector2 LocalFrac(RawImage img, PointerEventData ev)
        {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(img.rectTransform, ev.position, ev.pressEventCamera, out local);
            var r = img.rectTransform.rect;
            return new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);
        }

        public void CurveTap(PointerEventData ev)
        {
            if (selected == null) return;
            var fr = LocalFrac(curveView, ev);
            float f = CurveXToFrame(fr.x * (CurveW - 1));
            var ks = CurveKeys();
            curveSegA = curveSegB = -1;
            // Near a key: the stretch that starts there.
            float tol = (curveF1 - curveF0) * 12f / Mathf.Max(1f, curveView.rectTransform.rect.width);
            for (int i = 0; i < ks.Count; i++)
                if (Mathf.Abs(ks[i] - f) <= tol && i + 1 < ks.Count) { curveSegA = ks[i]; curveSegB = ks[i + 1]; break; }
            if (curveSegA < 0)
                for (int i = 0; i + 1 < ks.Count; i++)
                    if (f >= ks[i] && f <= ks[i + 1]) { curveSegA = ks[i]; curveSegB = ks[i + 1]; break; }
            SetFrame(Mathf.Clamp(Mathf.RoundToInt(f), startFrame, endFrame));
            if (curveSegA < 0 && ks.Count < 2) Toast("Hacen falta dos claves para tener un tramo que curvar");
            DrawCurves();
        }

        public void MiniDown(PointerEventData ev)
        {
            curveHandle = -1;
            curveEdited = false;
            if (curveSegA < 0) { Toast("Elige primero un tramo: toca la gráfica entre dos claves"); return; }
            var fr = LocalFrac(curveMini, ev);
            float x = fr.x, y = MiniMin + fr.y * (MiniMax - MiniMin);
            var h = Interp.HandlesFor(SegmentMode());
            float d1 = new Vector2(h.x - x, h.y - y).sqrMagnitude, d2 = new Vector2(h.z - x, h.w - y).sqrMagnitude;
            curveHandle = d1 <= d2 ? 0 : 1;
            MiniDrag(ev);
        }

        public void MiniDrag(PointerEventData ev)
        {
            if (curveHandle < 0 || curveSegA < 0) return;
            var fr = LocalFrac(curveMini, ev);
            float x = Mathf.Clamp01(fr.x), y = Mathf.Clamp(MiniMin + fr.y * (MiniMax - MiniMin), MiniMin, MiniMax);
            var h = Interp.HandlesFor(SegmentMode());
            if (curveHandle == 0) { h.x = x; h.y = y; } else { h.z = x; h.w = y; }
            SetSegmentCurve(Interp.Bezier(h), false);
            curveEdited = true;
        }

        public void MiniUp(PointerEventData ev)
        {
            if (curveEdited) { Commit(true); RefreshKeys(); }
            curveHandle = -1;
            curveEdited = false;
        }

        void SetSegmentCurve(string mode, bool commit)
        {
            if (selected == null || curveSegA < 0) { Toast("Elige primero un tramo: toca la gráfica entre dos claves"); return; }
            selected.SetInterp(curveSegA, mode);
            SetFrame(frame);
            DrawCurves();
            if (commit)
            {
                Commit(true);
                RefreshKeys();
                Toast("Tramo " + curveSegA + " → " + curveSegB + ": " + Lang.T(Interp.Name(mode)));
            }
        }

        // ------------------------------------------------------------------
        // Stretching keys in time

        /// The selected keys (or all of the selection's) made longer or
        /// shorter, from the first one: 200 % = twice as slow.
        void AskRetimeKeys()
        {
            var targets = new List<string>(selKeys);
            if (targets.Count == 0 && selected != null)
                foreach (int f in selected.TrackFrames("")) targets.Add(SelKey(selected, "", f));
            if (targets.Count < 2) { Toast("Selecciona dos o más claves (o un objeto con claves)"); return; }
            AtlasTheme.Dialog(canvasRoot, "Estirar claves en el tiempo",
                "Porcentaje: 200 = el doble de lento, 50 = el doble de rápido. Se cuenta desde la primera clave elegida.",
                "200", "Aplicar", false, txt =>
                {
                    float pct;
                    if (!float.TryParse(txt.Trim().TrimEnd('%').Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pct) || pct <= 0f)
                    { Toast("Escribe un porcentaje, por ejemplo 150", true); return; }
                    RetimeKeys(targets, pct / 100f);
                });
        }

        void RetimeKeys(List<string> targets, float k)
        {
            int first = int.MaxValue;
            foreach (var s in targets) first = Mathf.Min(first, int.Parse(s.Split('|')[2]));
            var list = new List<string[]>();
            foreach (var s in targets) list.Add(s.Split('|'));
            // Moving away from the first key: the farthest first, so no key lands on one not moved yet.
            list.Sort((a, b) => k >= 1f ? int.Parse(b[2]).CompareTo(int.Parse(a[2])) : int.Parse(a[2]).CompareTo(int.Parse(b[2])));
            var next = new List<string>();
            int last = first;
            foreach (var p in list)
            {
                var it = FindItem(p[0]);
                if (it == null) continue;
                int from = int.Parse(p[2]);
                int to = first + Mathf.RoundToInt((from - first) * k);
                it.MoveTrackKey(p[1], from, to);
                next.Add(SelKey(it, p[1], to));
                last = Mathf.Max(last, to);
            }
            selKeys.Clear();
            foreach (var s in next) selKeys.Add(s);
            if (last > endFrame) SetRange(startFrame, last);
            Commit(true);
            SetFrame(frame);
            RefreshKeys();
            if (CurvesOpen) DrawCurves();
            Toast("Claves estiradas al " + Mathf.RoundToInt(k * 100f) + " %");
        }
    }

    public class CurveInput : MonoBehaviour, IPointerClickHandler
    {
        public AtlasApp app;
        public void OnPointerClick(PointerEventData e) { if (app != null) app.CurveTap(e); }
    }

    public class CurveHandleInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public AtlasApp app;
        public void OnPointerDown(PointerEventData e) { if (app != null) app.MiniDown(e); }
        public void OnDrag(PointerEventData e) { if (app != null) app.MiniDrag(e); }
        public void OnPointerUp(PointerEventData e) { if (app != null) app.MiniUp(e); }
    }
}
