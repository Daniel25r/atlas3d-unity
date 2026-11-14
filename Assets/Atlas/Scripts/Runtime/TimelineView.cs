// ATLAS 3D (Unity) - track area of the timeline, drawn like the Godot
// version: frame ruler, one summary row per object (plus one row per track
// when expanded), keyframe diamonds and the playhead.
//   ruler: tap or drag = scrub
//   key:   tap = select (tap again: add / remove), drag = move the selected
//          keys by whole frames, hold = key menu
//   empty: tap = deselect and jump to the frame, drag = pan in time
//   two fingers = zoom, mouse wheel = scroll rows
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Atlas
{
    public class TimelineRow
    {
        public AtlasItem item;
        public string track = "";   // "" summary, "T" / "R" / "S", "b:<bone>"
        public string label;
        public bool focus;
        public List<int> frames = new List<int>();
        public float y;
        public float barStart, barEnd;   // a bar (the audio), when barEnd > barStart
    }

    public class TimelineView : MaskableGraphic, IPointerDownHandler, IDragHandler, IPointerUpHandler, IBeginDragHandler, IEndDragHandler, IScrollHandler
    {
        public const float RulerH = 26f, RowH = 30f, KeyR = 7f, HitR = 18f;

        // Filled by the app.
        public List<TimelineRow> rows = new List<TimelineRow>();
        public float frame;
        public int startFrame, endFrame = 120;
        public bool playing;
        public string emptyHint = "Selecciona un objeto y pulsa ◆ para guardar una clave aquí.";
        public Func<TimelineRow, int, bool> isSelected;
        public Action<float> onScrub;
        public Action<TimelineRow, int> onTapKey;
        public Action<float> onTapEmpty;
        public Action<int> onMoveSelected;          // delta in frames
        public Action<TimelineRow, int, Vector2> onKeyMenu;   // screen position

        public float labelWidth = 150f;
        public float pxPerFrame = 8f;
        public float firstFrame = -2f;
        public float rowScroll;

        readonly List<Text> labelPool = new List<Text>();
        readonly List<Text> rulerPool = new List<Text>();
        Text tagText, hintText;

        // Pointer state
        string mode = "";   // scrub | move | pan | pinch
        Vector2 pressPos, lastPos;
        float pressTime = -1f;
        TimelineRow pressRow;
        int pressFrame = -1;
        int moveDelta;
        float pinchDist;
        bool fitted;

        static readonly Color BgCol = AtlasTheme.Bg;
        static readonly Color PanelCol = AtlasTheme.Panel;
        static readonly Color Faint = new Color(1f, 1f, 1f, 0.35f);

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = true;
        }

        public void Fit()
        {
            float w = Mathf.Max(50f, rectTransform.rect.width - labelWidth - 20f);
            pxPerFrame = Mathf.Clamp(w / Mathf.Max(1f, endFrame - startFrame + 4), 0.5f, 60f);
            firstFrame = startFrame - 2f;
            fitted = rectTransform.rect.width > 10f;
        }

        public float FrameToX(float f) { return labelWidth + (f - firstFrame) * pxPerFrame; }
        public float XToFrame(float x) { return (x - labelWidth) / pxPerFrame + firstFrame; }

        float ContentHeight() { return rows.Count * RowH; }

        public void Refresh()
        {
            if (!fitted) Fit();
            if (playing)
            {
                float x = FrameToX(frame);
                if (x > rectTransform.rect.width - 10f || x < labelWidth) firstFrame = frame - 2f;
            }
            float y = RulerH - rowScroll;
            foreach (var r in rows) { r.y = y; y += RowH; }
            SetVerticesDirty();
            UpdateTexts();
        }

        // ------------------------------------------------------------------
        // Drawing (top-left coordinates, y down, like Godot)

        Rect R { get { return rectTransform.rect; } }

        void Quad(VertexHelper vh, float x, float y, float w, float h, Color c)
        {
            var r = R;
            float x0 = r.xMin + x, x1 = r.xMin + x + w;
            float y0 = r.yMax - y, y1 = r.yMax - y - h;
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        void Diamond(VertexHelper vh, float cx, float cy, float rr, Color c)
        {
            var r = R;
            float x = r.xMin + cx, y = r.yMax - cy;
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x, y + rr), c, Vector2.zero);
            vh.AddVert(new Vector3(x + rr, y), c, Vector2.zero);
            vh.AddVert(new Vector3(x, y - rr), c, Vector2.zero);
            vh.AddVert(new Vector3(x - rr, y), c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = R;
            float W = rect.width, H = rect.height;
            if (W <= 1f || H <= 1f) return;
            Quad(vh, 0, 0, W, H, BgCol);
            float xs = FrameToX(startFrame), xe = FrameToX(endFrame);
            if (xs > labelWidth) Quad(vh, labelWidth, 0, xs - labelWidth, H, new Color(0, 0, 0, 0.35f));
            if (xe < W) Quad(vh, Mathf.Max(labelWidth, xe), 0, W - Mathf.Max(labelWidth, xe), H, new Color(0, 0, 0, 0.35f));
            foreach (var row in rows)
            {
                if (row.y + RowH < RulerH || row.y > H) continue;
                if (row.focus) Quad(vh, 0, row.y, W, RowH, new Color(AtlasTheme.Accent.r, AtlasTheme.Accent.g, AtlasTheme.Accent.b, 0.14f));
                else if (row.track == "") Quad(vh, 0, row.y, W, RowH, new Color(1, 1, 1, 0.035f));
                Quad(vh, 0, row.y + RowH - 1f, W, 1f, new Color(1, 1, 1, 0.04f));
                if (row.barEnd > row.barStart)
                {
                    float bx0 = Mathf.Max(labelWidth, FrameToX(row.barStart)), bx1 = Mathf.Min(W, FrameToX(row.barEnd));
                    if (bx1 > bx0)
                    {
                        Quad(vh, bx0, row.y + 6f, bx1 - bx0, RowH - 12f, new Color(0.18f, 0.55f, 0.45f, 0.85f));
                        // simple wave marks
                        for (float x = bx0 + 3f; x < bx1 - 2f; x += 5f)
                        {
                            float hgt = 3f + (Mathf.PerlinNoise(x * 0.11f, row.barStart * 0.01f)) * (RowH - 18f);
                            Quad(vh, x, row.y + RowH * 0.5f - hgt * 0.5f, 2f, hgt, new Color(0.7f, 1f, 0.9f, 0.6f));
                        }
                    }
                }
                float cy = row.y + RowH * 0.5f;
                foreach (int f in row.frames)
                {
                    bool sel = isSelected != null && isSelected(row, f);
                    float shown = f + (mode == "move" && sel ? moveDelta : 0);
                    float x = FrameToX(shown);
                    if (x < labelWidth - KeyR || x > W + KeyR) continue;
                    float rr = row.track != "" ? KeyR : KeyR + 1f;
                    Color fill = sel ? Highlighter.SelectionColor : (row.track == "" ? new Color(0.86f, 0.87f, 0.9f) : new Color(0.7f, 0.72f, 0.76f));
                    Diamond(vh, x, cy, rr + 1.2f, new Color(0, 0, 0, 0.6f));
                    Diamond(vh, x, cy, rr, fill);
                }
            }
            // Ruler
            Quad(vh, 0, 0, W, RulerH, PanelCol);
            int step = TickStep();
            int f0 = Mathf.FloorToInt(firstFrame / step) * step;
            for (int f = f0; FrameToX(f) < W; f += step)
            {
                float x = FrameToX(f);
                if (x < labelWidth) continue;
                Quad(vh, x, RulerH - 6f, 1f, 6f, Faint);
                Quad(vh, x, RulerH, 1f, H - RulerH, new Color(1, 1, 1, 0.03f));
            }
            // Label column
            Quad(vh, 0, 0, labelWidth, H, new Color(PanelCol.r, PanelCol.g, PanelCol.b, 0.96f));
            Quad(vh, labelWidth, 0, 1f, H, new Color(1, 1, 1, 0.08f));
            // Playhead
            float px = FrameToX(frame);
            if (px >= labelWidth)
            {
                Quad(vh, px - 1f, 0, 2f, H, AtlasTheme.Accent);
                string tag = Mathf.RoundToInt(frame).ToString();
                float tw = tag.Length * 7f + 12f;
                Quad(vh, px - tw * 0.5f, 2f, tw, RulerH - 6f, AtlasTheme.Accent);
            }
        }

        int TickStep()
        {
            int[] steps = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000 };
            foreach (int s in steps) if (s * pxPerFrame >= 46f) return s;
            return 1000;
        }

        // ------------------------------------------------------------------
        // Texts (children drawn above the mesh)

        Text MakeText(int size, Color c, TextAnchor a)
        {
            var t = AtlasTheme.Label(transform, "", size, c, false, a);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            return t;
        }

        void Place(Text t, float x, float y, float w, float h)
        {
            var rt = t.rectTransform;
            rt.anchoredPosition = new Vector2(x, -y - h * 0.5f);
            rt.sizeDelta = new Vector2(w, h);
        }

        void UpdateTexts()
        {
            float W = R.width, H = R.height;
            // Ruler numbers
            int step = TickStep();
            int f0 = Mathf.FloorToInt(firstFrame / step) * step;
            int n = 0;
            for (int f = f0; FrameToX(f) < W && n < 200; f += step)
            {
                float x = FrameToX(f);
                if (x < labelWidth) continue;
                if (n >= rulerPool.Count) rulerPool.Add(MakeText(11, AtlasTheme.TextDim, TextAnchor.MiddleLeft));
                var t = rulerPool[n++];
                t.enabled = true;
                t.text = f.ToString();
                Place(t, x + 3f, 2f, 40f, RulerH - 8f);
            }
            for (int i = n; i < rulerPool.Count; i++) rulerPool[i].enabled = false;
            // Row labels
            int m = 0;
            foreach (var row in rows)
            {
                if (row.y + RowH < RulerH || row.y > H) continue;
                if (m >= labelPool.Count) labelPool.Add(MakeText(13, AtlasTheme.TextColor, TextAnchor.MiddleLeft));
                var t = labelPool[m++];
                t.enabled = true;
                t.text = row.label;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                t.color = row.focus ? AtlasTheme.AccentLight : (row.track == "" ? AtlasTheme.TextColor : AtlasTheme.TextDim);
                Place(t, 8f, row.y, labelWidth - 12f, RowH);
            }
            for (int i = m; i < labelPool.Count; i++) labelPool[i].enabled = false;
            // Hint when there is nothing
            if (hintText == null) hintText = MakeText(13, new Color(1, 1, 1, 0.4f), TextAnchor.MiddleLeft);
            bool showHint = rows.Count == 0 || (rows.Count > 0 && rows[0].focus && rows[0].frames.Count == 0);
            hintText.enabled = showHint;
            if (showHint)
            {
                hintText.text = rows.Count == 0 ? emptyHint : "Este hueso no tiene claves: pulsa ◆ para guardar una aquí.";
                Place(hintText, labelWidth + 12f, rows.Count == 0 ? RulerH + 6f : rows[0].y, Mathf.Max(50f, W - labelWidth - 20f), RowH);
            }
            // Playhead tag (on top of everything)
            if (tagText == null) tagText = MakeText(11, Color.white, TextAnchor.MiddleCenter);
            float px = FrameToX(frame);
            tagText.enabled = px >= labelWidth;
            if (tagText.enabled)
            {
                string tag = Mathf.RoundToInt(frame).ToString();
                float tw = tag.Length * 7f + 12f;
                tagText.text = tag;
                tagText.transform.SetAsLastSibling();
                Place(tagText, px - tw * 0.5f, 2f, tw, RulerH - 6f);
            }
        }

        // ------------------------------------------------------------------
        // Input

        Vector2 Local(PointerEventData e)
        {
            Vector2 l;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, e.position, e.pressEventCamera, out l);
            return new Vector2(l.x - R.xMin, R.yMax - l.y);
        }

        bool KeyAt(Vector2 pos, out TimelineRow row, out int frame)
        {
            row = null;
            frame = -1;
            if (pos.y < RulerH || pos.x < labelWidth) return false;
            foreach (var r in rows)
            {
                if (pos.y < r.y || pos.y >= r.y + RowH) continue;
                float best = HitR;
                foreach (int f in r.frames)
                {
                    float d = Mathf.Abs(FrameToX(f) - pos.x);
                    if (d < best) { best = d; frame = f; row = r; }
                }
                return row != null;
            }
            return false;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (Input.touchCount >= 2) { StartPinch(); return; }
            pressPos = lastPos = Local(e);
            pressTime = Time.unscaledTime;
            mode = "";
            moveDelta = 0;
            pressRow = null;
            pressFrame = -1;
            if (pressPos.y < RulerH && pressPos.x >= labelWidth)
            {
                mode = "scrub";
                if (onScrub != null) onScrub(Mathf.Round(XToFrame(pressPos.x)));
                return;
            }
            KeyAt(pressPos, out pressRow, out pressFrame);
        }

        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            if (mode == "pinch") return;
            var pos = Local(e);
            switch (mode)
            {
                case "scrub":
                    if (onScrub != null) onScrub(Mathf.Round(XToFrame(pos.x)));
                    break;
                case "move":
                    moveDelta = Mathf.RoundToInt((pos.x - pressPos.x) / pxPerFrame);
                    SetVerticesDirty();
                    break;
                case "pan":
                    firstFrame -= (pos.x - lastPos.x) / pxPerFrame;
                    rowScroll = Mathf.Clamp(rowScroll - (pos.y - lastPos.y), 0f, Mathf.Max(0f, ContentHeight() - (R.height - RulerH)));
                    lastPos = pos;
                    Refresh();
                    break;
                case "":
                    if ((pos - pressPos).magnitude < 8f) return;
                    pressTime = -1f;
                    if (pressRow != null)
                    {
                        // Dragging a key: select it first if it was not.
                        if (isSelected == null || !isSelected(pressRow, pressFrame))
                            if (onTapKey != null) onTapKey(pressRow, pressFrame);
                        mode = "move";
                        moveDelta = Mathf.RoundToInt((pos.x - pressPos.x) / pxPerFrame);
                        SetVerticesDirty();
                    }
                    else
                    {
                        mode = "pan";
                        lastPos = pos;
                    }
                    break;
            }
        }

        public void OnEndDrag(PointerEventData e) { }

        public void OnPointerUp(PointerEventData e)
        {
            var pos = Local(e);
            string m = mode;
            mode = "";
            if (m == "pinch") { pressTime = -1f; return; }
            if (m == "move")
            {
                int d = moveDelta;
                moveDelta = 0;
                if (d != 0 && onMoveSelected != null) onMoveSelected(d);
                SetVerticesDirty();
            }
            else if (m == "" && pressTime >= 0f)
            {
                if (pressRow != null) { if (onTapKey != null) onTapKey(pressRow, pressFrame); }
                else if (pos.x >= labelWidth && onTapEmpty != null) onTapEmpty(Mathf.Round(XToFrame(pos.x)));
            }
            pressTime = -1f;
            pressRow = null;
        }

        public void OnScroll(PointerEventData e)
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                ZoomAt(Local(e).x, e.scrollDelta.y > 0 ? 1.15f : 1f / 1.15f);
            else
                rowScroll = Mathf.Clamp(rowScroll - e.scrollDelta.y * RowH, 0f, Mathf.Max(0f, ContentHeight() - (R.height - RulerH)));
            Refresh();
        }

        void ZoomAt(float x, float factor)
        {
            float f = XToFrame(x);
            pxPerFrame = Mathf.Clamp(pxPerFrame * factor, 0.5f, 60f);
            firstFrame = f - (x - labelWidth) / pxPerFrame;
        }

        void StartPinch()
        {
            mode = "pinch";
            pressTime = -1f;
            pinchDist = 0f;
        }

        void Update()
        {
            // Hold on a key: its menu.
            if (pressTime >= 0f && mode == "" && pressRow != null && Time.unscaledTime - pressTime > 0.55f)
            {
                pressTime = -1f;
                if (onKeyMenu != null)
                {
                    Vector3 sp = RectTransformUtility.WorldToScreenPoint(null, rectTransform.TransformPoint(new Vector3(R.xMin + pressPos.x, R.yMax - pressPos.y)));
                    onKeyMenu(pressRow, pressFrame, sp);
                }
                pressRow = null;
            }
            // Two fingers: zoom around their centre and pan.
            if (mode == "pinch" && Input.touchCount >= 2)
            {
                Vector2 a = Input.GetTouch(0).position, b = Input.GetTouch(1).position;
                float d = (a - b).magnitude;
                Vector2 c = (a + b) * 0.5f;
                Vector2 l;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, c, null, out l);
                float cx = l.x - R.xMin;
                if (pinchDist > 1f) ZoomAt(cx, d / pinchDist);
                pinchDist = d;
                Refresh();
            }
            else if (mode == "pinch" && Input.touchCount == 0) mode = "";
        }
    }
}
