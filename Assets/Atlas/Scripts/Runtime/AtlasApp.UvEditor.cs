// ATLAS 3D (Unity) - «Editor UV» (like Blender's UV Editing): next to the
// 3D view, the mesh laid flat over its texture. Tap a piece to choose it
// (it is chosen in 3D too), drag to move / turn / scale it, unwrap by
// islands or as a box, fit it to the picture, and export the layout as a
// PNG template to paint in any drawing app.
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        GameObject uvPanel;
        RawImage uvView;
        Texture2D uvTex;
        Color32[] uvBack, uvPx;
        string uvBackFor = "";
        string uvMode = "move";
        const int UvRes = 512;
        const float UvMin = -0.05f, UvMax = 1.05f;   // what the panel shows
        // Dragging
        Vector2 uvStart, uvCenter;
        readonly Dictionary<int, Vector2[]> uvOrig = new Dictionary<int, Vector2[]>();
        bool uvDragging, uvMoved;

        bool UvOpen { get { return uvPanel != null && uvPanel.activeSelf; } }

        void ToggleUvEditor()
        {
            if (UvOpen) { CloseUvEditor(); return; }
            if (selected == null || selected.edit == null) return;
            if (selMode != "face") { selMode = "face"; selVerts.Clear(); selEdges.Clear(); vertOrder.Clear(); BuildMeshBar(); ShowGizmo(); }
            if (uvPanel == null) BuildUvPanel();
            uvPanel.SetActive(true);
            uvPanel.transform.SetAsLastSibling();
            uvBackFor = "";
            DrawUv();
            Toast("Editor UV: toca una pieza para elegirla y arrástrala para colocarla sobre la textura");
        }

        void CloseUvEditor()
        {
            if (uvPanel != null) uvPanel.SetActive(false);
        }

        void BuildUvPanel()
        {
            float side = Mathf.Clamp(viewArea.rect.height - 250f, 200f, 460f);
            var p = AtlasTheme.Box(viewArea, "EditorUV", AtlasTheme.Panel2, 14);
            AtlasTheme.Pin(p.rectTransform, 0f, 1f, new Vector2(10f, -70f), new Vector2(side + 20f, side + 150f));
            p.rectTransform.pivot = new Vector2(0f, 1f);
            uvPanel = p.gameObject;
            var title = AtlasTheme.Label(p.transform, "EDITOR UV", 14, AtlasTheme.TextDim, true);
            AtlasTheme.Pin(title.rectTransform, 0f, 1f, new Vector2(14f, -8f), new Vector2(200f, 24f));
            title.rectTransform.pivot = new Vector2(0f, 1f);
            var x = AtlasTheme.IconButton(p.transform, "close", CloseUvEditor, 34f, 18f);
            AtlasTheme.Pin((RectTransform)x.transform, 1f, 1f, new Vector2(-4f, -2f), new Vector2(34f, 34f));
            ((RectTransform)x.transform).pivot = new Vector2(1f, 1f);
            uvView = new GameObject("Plano").AddComponent<RawImage>();
            uvView.transform.SetParent(p.transform, false);
            AtlasTheme.Pin(uvView.rectTransform, 0.5f, 1f, new Vector2(0f, -36f), new Vector2(side, side));
            uvView.rectTransform.pivot = new Vector2(0.5f, 1f);
            uvTex = new Texture2D(UvRes, UvRes, TextureFormat.RGBA32, false);
            uvTex.filterMode = FilterMode.Bilinear;
            uvView.texture = uvTex;
            var input = uvView.gameObject.AddComponent<UvInput>();
            input.app = this;
            // Tools under the picture.
            var tools = AtlasTheme.Rect(p.transform, "Herramientas");
            AtlasTheme.Pin(tools, 0.5f, 0f, new Vector2(0f, 8f), new Vector2(side, 104f));
            tools.pivot = new Vector2(0.5f, 0f);
            var g = AtlasTheme.Grid(tools, 3, side, 32f, 4f);
            UvChip(g, "Mover", "move");
            UvChip(g, "Girar", "rotate");
            UvChip(g, "Escalar", "scale");
            AtlasTheme.TextButton(g, "image", "Desplegar", () => UvOp("Desplegar por islas", m => m.SmartUV(selFaces.Count > 0 ? selFaces : null)), false, 32f, 13);
            AtlasTheme.TextButton(g, "cube", "Caja", () => UvOp("UV de caja", m => m.AutoUV(1.7f / Mathf.Max(0.0001f, EditBoundsSize()))), false, 32f, 13);
            AtlasTheme.TextButton(g, "scale", "Ajustar", UvFit, false, 32f, 13);
            AtlasTheme.TextButton(g, "multiselect", "Pieza entera", () =>
            {
                if (selFaces.Count == 0) { Toast("Toca primero una cara de la pieza"); return; }
                foreach (int f in selected.edit.Linked(new HashSet<int>(selFaces))) selFaces.Add(f);
                DrawUv();
            }, false, 32f, 13);
            AtlasTheme.TextButton(g, "multiselect", "Todo", () => { for (int i = 0; i < selected.edit.faces.Count; i++) selFaces.Add(i); DrawUv(); }, false, 32f, 13);
            AtlasTheme.TextButton(g, "export", "Plantilla PNG", ExportUvTemplate, false, 32f, 13);
        }

        readonly List<KeyValuePair<Button, string>> uvChips = new List<KeyValuePair<Button, string>>();

        void UvChip(Transform g, string label, string mode)
        {
            var b = AtlasTheme.Chip(g, label, () =>
            {
                uvMode = mode;
                foreach (var kv in uvChips)
                {
                    bool on = kv.Value == uvMode;
                    kv.Key.GetComponent<Image>().color = on ? AtlasTheme.AccentDim : AtlasTheme.Panel3;
                    kv.Key.GetComponentInChildren<Text>().color = on ? AtlasTheme.AccentLight : AtlasTheme.TextColor;
                }
            }, uvMode == mode);
            uvChips.Add(new KeyValuePair<Button, string>(b, mode));
        }

        /// An UV change made whole (one undo step), shown in 3D and in the panel.
        void UvOp(string name, System.Action<EditMesh> op)
        {
            if (selected == null || selected.edit == null) return;
            op(selected.edit);
            selected.edit.ToMesh(selected.editMesh);
            SaveEdit(selected);
            DrawUv();
            Toast(name);
        }

        /// The chosen pieces (or all) fill the picture, keeping their shape.
        void UvFit()
        {
            UvOp("Ajustado a la imagen", m =>
            {
                var fs = new List<int>();
                for (int i = 0; i < m.faces.Count; i++) if (selFaces.Count == 0 || selFaces.Contains(i)) fs.Add(i);
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (int i in fs) foreach (var u in m.uvs[i]) { x0 = Mathf.Min(x0, u.x); y0 = Mathf.Min(y0, u.y); x1 = Mathf.Max(x1, u.x); y1 = Mathf.Max(y1, u.y); }
                float s = 1f / Mathf.Max(1e-5f, Mathf.Max(x1 - x0, y1 - y0));
                foreach (int i in fs)
                {
                    var a = m.uvs[i];
                    for (int k = 0; k < a.Length; k++) a[k] = new Vector2((a[k].x - x0) * s, (a[k].y - y0) * s);
                }
            });
        }

        // ------------------------------------------------------------------
        // Drawing (into a small picture: lines over the texture)

        static Vector2Int UvToPx(Vector2 uv, int res)
        {
            float t = (UvMax - UvMin);
            return new Vector2Int(Mathf.RoundToInt((uv.x - UvMin) / t * (res - 1)), Mathf.RoundToInt((uv.y - UvMin) / t * (res - 1)));
        }

        void UvBackground()
        {
            string key = selected != null ? (selected.texture ?? "") : "";
            if (uvBack != null && key == uvBackFor) return;
            uvBackFor = key;
            uvBack = new Color32[UvRes * UvRes];
            var tex = selected != null ? LoadTexture(selected.texture) : null;
            for (int y = 0; y < UvRes; y++)
                for (int x = 0; x < UvRes; x++)
                {
                    float u = UvMin + (UvMax - UvMin) * x / (UvRes - 1f), v = UvMin + (UvMax - UvMin) * y / (UvRes - 1f);
                    bool inside = u >= 0f && u <= 1f && v >= 0f && v <= 1f;
                    Color c;
                    if (!inside) c = new Color(0.12f, 0.12f, 0.14f);
                    else if (tex != null && tex.isReadable) c = tex.GetPixelBilinear(u, v) * 0.6f;
                    else c = ((Mathf.FloorToInt(u * 8f) + Mathf.FloorToInt(v * 8f)) & 1) == 0 ? new Color(0.32f, 0.32f, 0.36f) : new Color(0.24f, 0.24f, 0.28f);
                    c.a = 1f;
                    uvBack[y * UvRes + x] = c;
                }
        }

        void DrawUv()
        {
            if (!UvOpen || selected == null || selected.edit == null || uvTex == null) return;
            UvBackground();
            if (uvPx == null) uvPx = new Color32[UvRes * UvRes];
            System.Array.Copy(uvBack, uvPx, uvPx.Length);
            var e = selected.edit;
            var line = new Color32(225, 225, 235, 255);
            var hot = new Color32(255, 170, 60, 255);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < e.faces.Count; i++)
                {
                    bool sel = selFaces.Contains(i);
                    if (sel != (pass == 1)) continue;   // chosen ones on top
                    var u = e.uvs[i];
                    for (int k = 0; k < u.Length; k++) Line(uvPx, UvRes, UvToPx(u[k], UvRes), UvToPx(u[(k + 1) % u.Length], UvRes), sel ? hot : line);
                }
            uvTex.SetPixels32(uvPx);
            uvTex.Apply(false);
        }

        static void Line(Color32[] px, int res, Vector2Int a, Vector2Int b, Color32 c)
        {
            int dx = Mathf.Abs(b.x - a.x), dy = -Mathf.Abs(b.y - a.y);
            int sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1, err = dx + dy;
            int x = a.x, y = a.y;
            for (int n = 0; n < 4096; n++)
            {
                if (x >= 0 && y >= 0 && x < res && y < res) px[y * res + x] = c;
                if (x == b.x && y == b.y) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }

        // ------------------------------------------------------------------
        // Touch on the panel

        Vector2 PanelToUv(PointerEventData ev)
        {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(uvView.rectTransform, ev.position, ev.pressEventCamera, out local);
            var r = uvView.rectTransform.rect;
            float fx = (local.x - r.xMin) / r.width, fy = (local.y - r.yMin) / r.height;
            return new Vector2(UvMin + (UvMax - UvMin) * fx, UvMin + (UvMax - UvMin) * fy);
        }

        public void UvDown(PointerEventData ev)
        {
            if (selected == null || selected.edit == null) return;
            uvStart = PanelToUv(ev);
            uvMoved = false;
            uvDragging = false;
            uvOrig.Clear();
            // Dragging works on what is chosen (if the finger starts on it or anywhere for turn / scale).
            if (selFaces.Count > 0)
            {
                Vector2 c = Vector2.zero;
                int n = 0;
                foreach (int f in selFaces)
                {
                    var a = selected.edit.uvs[f];
                    uvOrig[f] = (Vector2[])a.Clone();
                    foreach (var u in a) { c += u; n++; }
                }
                uvCenter = n > 0 ? c / n : uvStart;
                uvDragging = true;
            }
        }

        public void UvDrag(PointerEventData ev)
        {
            if (!uvDragging || selected == null || selected.edit == null) return;
            Vector2 now = PanelToUv(ev);
            if ((now - uvStart).magnitude > 0.004f) uvMoved = true;
            if (!uvMoved) return;
            Vector2 d = now - uvStart;
            float ang = 0f, sc = 1f;
            if (uvMode == "rotate")
            {
                Vector2 a = uvStart - uvCenter, b = now - uvCenter;
                ang = Mathf.Atan2(b.y, b.x) - Mathf.Atan2(a.y, a.x);
            }
            else if (uvMode == "scale")
            {
                float a = (uvStart - uvCenter).magnitude, b = (now - uvCenter).magnitude;
                sc = a > 1e-4f ? b / a : 1f;
            }
            float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
            foreach (var kv in uvOrig)
            {
                var dst = selected.edit.uvs[kv.Key];
                for (int k = 0; k < dst.Length; k++)
                {
                    Vector2 p = kv.Value[k];
                    if (uvMode == "move") dst[k] = p + d;
                    else
                    {
                        Vector2 q = (p - uvCenter) * sc;
                        dst[k] = uvCenter + new Vector2(q.x * cs - q.y * sn, q.x * sn + q.y * cs);
                    }
                }
            }
            selected.edit.ToMesh(selected.editMesh);
            DrawUv();
        }

        public void UvUp(PointerEventData ev)
        {
            if (selected == null || selected.edit == null) return;
            if (uvMoved && uvDragging)
            {
                SaveEdit(selected);
                uvDragging = false;
                return;
            }
            uvDragging = false;
            // A tap: the face under the finger (or nothing).
            Vector2 p = PanelToUv(ev);
            int hit = -1;
            var e = selected.edit;
            for (int i = e.faces.Count - 1; i >= 0 && hit < 0; i--)
                if (Inside(e.uvs[i], p)) hit = i;
            bool add = AddKeyHeld();
            if (!add) selFaces.Clear();
            if (hit >= 0) { if (add && selFaces.Contains(hit)) selFaces.Remove(hit); else selFaces.Add(hit); }
            ShowGizmo();
            DrawUv();
        }

        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if (((poly[i].y > p.y) != (poly[j].y > p.y)) && (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y + 1e-12f) + poly[i].x)) c = !c;
            return c;
        }

        // ------------------------------------------------------------------
        // Template

        /// The layout as a PNG (lines on transparent): paint it in any app
        /// and put it back as the texture.
        void ExportUvTemplate()
        {
            if (selected == null || selected.edit == null) return;
            const int R = 1024;
            var px = new Color32[R * R];
            var e = selected.edit;
            var c = new Color32(0, 0, 0, 255);
            for (int i = 0; i < e.faces.Count; i++)
            {
                var u = e.uvs[i];
                for (int k = 0; k < u.Length; k++)
                {
                    Vector2 a = u[k], b = u[(k + 1) % u.Length];
                    var pa = new Vector2Int(Mathf.RoundToInt(Mathf.Repeat(a.x, 1.0001f) * (R - 1)), Mathf.RoundToInt(Mathf.Repeat(a.y, 1.0001f) * (R - 1)));
                    var pb = new Vector2Int(Mathf.RoundToInt(Mathf.Repeat(b.x, 1.0001f) * (R - 1)), Mathf.RoundToInt(Mathf.Repeat(b.y, 1.0001f) * (R - 1)));
                    if (Mathf.Abs(pa.x - pb.x) > R / 2 || Mathf.Abs(pa.y - pb.y) > R / 2) continue;   // wrapped across the edge
                    Line(px, R, pa, pb, c);
                }
            }
            var t = new Texture2D(R, R, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            t.Apply(false);
            try
            {
                string path = Path.Combine(Exporter.ExportDir, "plantilla_uv_" + Stamp() + ".png");
                File.WriteAllBytes(path, t.EncodeToPNG());
                Share(path, "Plantilla UV: píntala y ponla como textura");
            }
            catch (System.Exception ex) { Toast("No se pudo guardar: " + ex.Message, true); }
            Destroy(t);
        }
    }

    /// Touches on the UV picture go to the app.
    public class UvInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public AtlasApp app;
        public void OnPointerDown(PointerEventData e) { if (app != null) app.UvDown(e); }
        public void OnDrag(PointerEventData e) { if (app != null) app.UvDrag(e); }
        public void OnPointerUp(PointerEventData e) { if (app != null) app.UvUp(e); }
    }
}
