// ATLAS 5D (Unity) - «Configuración de geometría»: a panel that floats over
// the view (under the mode pill) while a shape is selected. Each shape has
// its own numbers (segments / vertices, rings, radius, depth, top radius),
// then a radial deformation (sink or extend, waves, axis, mirror, twist,
// holes) and the topology (quads, triangles, R-Cube). The mesh is made
// again on every change; everything can be undone.
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        RectTransform geoPanel;
        string geoKey = "";
        bool geoOpen = true, geoDefOpen, geoTopoOpen;

        bool GeoWanted()
        {
            return selected != null && ShapeGen.Supports(selected.kind) && meshMode == "" && !poseMode && !playing && viewArea != null && !selected.locked;
        }

        string GeoStateKey()
        {
            if (!GeoWanted()) return "";
            if (selected.geo == null) selected.geo = new GeoParams();
            return selected.id + "|" + selected.kind + "|" + JsonUtility.ToJson(selected.geo) + "|" + geoOpen + geoDefOpen + geoTopoOpen + "|" +
                   (selected.meshFile ?? "") + "|" + Mathf.RoundToInt(viewArea.rect.width) + "x" + Mathf.RoundToInt(viewArea.rect.height) + "|" + Lang.Current;
        }

        /// Called every frame: the panel is built again only when what it shows changes.
        void UpdateGeoPanel()
        {
            string key = GeoStateKey();
            if (geoPanel != null && pillRect != null)
                geoPanel.anchoredPosition = new Vector2(12f, pillRect.anchoredPosition.y - 52f);
            if (key == geoKey) return;
            geoKey = key;
            if (geoPanel != null) { Destroy(geoPanel.gameObject); geoPanel = null; }
            if (key != "") BuildGeoPanel(selected);
        }

        void BuildGeoPanel(AtlasItem it)
        {
            var g = it.geo;
            string k = it.kind;
            float vw = viewArea.rect.width, vh = viewArea.rect.height;
            float w = Mathf.Min(400f, vw - 24f);
            float top = pillRect != null ? pillRect.anchoredPosition.y - 52f : -68f;
            float maxH = Mathf.Max(120f, vh + top - 90f);

            var panel = AtlasTheme.Box(viewArea, "Geometria", AtlasTheme.Panel, 14);
            geoPanel = panel.rectTransform;
            AtlasTheme.Pin(geoPanel, 0f, 1f, new Vector2(12f, top), new Vector2(w, 52f));

            // Header: title, the shape's icon, open / close.
            var head = AtlasTheme.Box(panel.transform, "Cabecera", AtlasTheme.AccentDim, 14);
            AtlasTheme.Pin(head.rectTransform, 0f, 1f, Vector2.zero, new Vector2(w, 52f));
            var hb = head.gameObject.AddComponent<Button>();
            hb.onClick.AddListener(() => { geoOpen = !geoOpen; });
            var title = AtlasTheme.Label(head.transform, "Configuración de geometría", 17, Color.white, true);
            AtlasTheme.Fill(title.rectTransform, 14f, 96f);
            title.raycastTarget = false;
            var icon = AtlasTheme.Icon(KindIcon(k));
            if (icon != null)
            {
                var ri = new GameObject("Icono", typeof(RectTransform)).AddComponent<RawImage>();
                ri.transform.SetParent(head.transform, false);
                ri.texture = icon;
                ri.raycastTarget = false;
                AtlasTheme.Pin(ri.rectTransform, 1f, 0.5f, new Vector2(-56f, 0f), new Vector2(28f, 28f));
                ri.rectTransform.pivot = new Vector2(1f, 0.5f);
            }
            var arrow = AtlasTheme.IconImage(head.transform, geoOpen ? "chevron_down" : "chevron_right", 22f, Color.white);
            AtlasTheme.Pin(arrow.rectTransform, 1f, 0.5f, new Vector2(-14f, 0f), new Vector2(22f, 22f));
            arrow.raycastTarget = false;
            if (!geoOpen) return;

            var area = AtlasTheme.Rect(panel.transform, "Area");
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(0f, 6f);
            area.offsetMax = new Vector2(0f, -56f);
            ScrollRect sr;
            var c = AtlasTheme.ScrollArea(area, out sr, 8f, new RectOffset(12, 12, 6, 10));
            float iw = w - 24f;

            // --- The shape's own numbers
            var grid = AtlasTheme.Grid(c, 2, iw, 68f, 8f);
            int seg = g.seg > 0 ? g.seg : ShapeGen.DefaultSeg(k);
            int rings = g.rings > 0 ? g.rings : ShapeGen.DefaultRings(k);
            bool lathe = ShapeGen.HasHeight(k);
            if (lathe)
            {
                GeoNum(grid, k == "capsule" ? "Altura" : "Profundidad", ShapeGen.Height(k, g), true, 0.01f, 100f, v => g.height = v);
                GeoNum(grid, k == "pyramid" ? "Lados" : "Vértices", seg, false, 3f, 128f, v => g.seg = Mathf.RoundToInt(v));
                GeoNum(grid, ShapeGen.HasTop(k) ? "Radio 1 (abajo)" : "Radio", ShapeGen.Radius(g), true, 0.01f, 20f, v => g.radius = v * 2f);
                if (ShapeGen.HasTop(k)) GeoNum(grid, "Radio 2 (arriba)", ShapeGen.Top(k, g), true, 0f, 20f, v => g.radius2 = v * 2f);
                GeoNum(grid, k == "capsule" ? "Anillos" : "Cortes (altura)", rings, false, 1f, 64f, v => g.rings = Mathf.RoundToInt(v));
            }
            else if (k == "plane")
            {
                GeoNum(grid, "Divisiones X", seg, false, 1f, 128f, v => g.seg = Mathf.RoundToInt(v));
                GeoNum(grid, "Divisiones Z", rings, false, 1f, 128f, v => g.rings = Mathf.RoundToInt(v));
                GeoNum(grid, "Tamaño", 10f * g.radius, true, 0.05f, 400f, v => g.radius = v / 10f);
            }
            else if (k == "cube")
            {
                GeoNum(grid, "Divisiones", seg, false, 1f, 32f, v => g.seg = Mathf.RoundToInt(v));
                GeoNum(grid, "Tamaño", g.radius, true, 0.02f, 40f, v => g.radius = v);
            }
            else
            {
                GeoNum(grid, "Segmentos", seg, false, 3f, 128f, v => g.seg = Mathf.RoundToInt(v));
                GeoNum(grid, k == "sphere" && g.topo == 1 ? "Detalle" : "Anillos", rings, false, k == "sphere" ? 2f : 3f, 64f, v => g.rings = Mathf.RoundToInt(v));
                GeoNum(grid, "Radio", ShapeGen.Radius(g), true, 0.01f, 20f, v => g.radius = v * 2f);
            }

            // --- Radial deformation
            GeoSub(c, "Deformación radial", geoDefOpen, () => geoDefOpen = !geoDefOpen);
            if (geoDefOpen)
            {
                AtlasTheme.SliderRow(c, "Cantidad", 0f, 1f, g.deform, "0.00", v => { g.deform = v; GeoChanged(it); geoKey = GeoStateKey(); }, () => Commit(true));
                var dg = AtlasTheme.Grid(c, 2, iw, 38f, 6f);
                AtlasTheme.Chip(dg, "Hundir", () => GeoSet(it, () => g.sink = true), g.sink);
                AtlasTheme.Chip(dg, "Extender", () => GeoSet(it, () => g.sink = false), !g.sink);
                var ag = AtlasTheme.Grid(c, 4, iw, 38f, 6f);
                AtlasTheme.Chip(ag, "Espejo", () => GeoSet(it, () => g.mirror = !g.mirror), g.mirror);
                string[] axes = { "X", "Y", "Z" };
                for (int a0 = 0; a0 < 3; a0++)
                {
                    int a = a0;
                    AtlasTheme.Chip(ag, axes[a], () => GeoSet(it, () => g.axis = a), g.axis == a);
                }
                var ng = AtlasTheme.Grid(c, 2, iw, 68f, 8f);
                GeoNum(ng, "Ondas", g.waves, false, 1f, 64f, v => g.waves = Mathf.RoundToInt(v));
                GeoNum(ng, "Torsión geométrica (°)", g.twist, false, -1440f, 1440f, v => g.twist = v);
                var hg = AtlasTheme.Grid(c, 1, iw, 38f, 6f);
                AtlasTheme.Chip(hg, "Huecos (rejilla)", () => GeoSet(it, () => g.holes = !g.holes), g.holes);
            }

            // --- Topology
            GeoSub(c, "Topología", geoTopoOpen, () => geoTopoOpen = !geoTopoOpen);
            if (geoTopoOpen)
            {
                bool rc = ShapeGen.HasRCube(k);
                var tg = AtlasTheme.Grid(c, rc ? 3 : 2, iw, 38f, 6f);
                AtlasTheme.Chip(tg, "Cuadriláteros", () => GeoSet(it, () => g.topo = 0), g.topo == 0);
                AtlasTheme.Chip(tg, "Triángulos", () => GeoSet(it, () => g.topo = 1), g.topo == 1);
                if (rc) AtlasTheme.Chip(tg, "R-Cube", () => GeoSet(it, () => g.topo = 2), g.topo == 2);
                if (rc && g.topo == 2 && k == "cube")
                    AtlasTheme.SliderRow(c, "Redondeo", 0.02f, 0.5f, g.round, "0.00", v => { g.round = v; GeoChanged(it); geoKey = GeoStateKey(); }, () => Commit(true));
            }

            if (!string.IsNullOrEmpty(it.meshFile))
                Hint(c, "Esta forma ya está modelada: al cambiar su geometría se hace de nuevo y se pierde lo modelado (Deshacer lo recupera).");
            if (g.on)
                AtlasTheme.TextButton(c, "undo", "Forma de siempre", () =>
                {
                    it.geo = new GeoParams();
                    if (it.edit == null) LoadItemMesh(it);
                    highlighter.Show(it);
                    Commit(true);
                }, false, 38f, 14);

            // Height: as tall as its content, never past the bottom of the view.
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(c);
            float h = Mathf.Min(maxH, 56f + 6f + c.rect.height + 4f);
            geoPanel.sizeDelta = new Vector2(w, h);
        }

        /// A titled number box. `metres` adds «m» and allows decimals.
        void GeoNum(Transform parent, string label, float value, bool metres, float min, float max, System.Action<float> set)
        {
            var cell = AtlasTheme.Rect(parent, "Celda");
            AtlasTheme.Column(cell.gameObject, 2f, new RectOffset(0, 0, 0, 0));
            var l = AtlasTheme.Label(cell, label, 14, AtlasTheme.TextColor, true, TextAnchor.MiddleCenter);
            AtlasTheme.Le(l.gameObject, 22f);
            string shown = metres ? value.ToString("0.###", CultureInfo.InvariantCulture) + " m" : Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
            var it = selected;
            var f = AtlasTheme.TextField(cell, shown, null, 40f);
            f.textComponent.alignment = TextAnchor.MiddleCenter;
            f.keyboardType = TouchScreenKeyboardType.NumbersAndPunctuation;
            f.onEndEdit.AddListener(s =>
            {
                float v;
                string t = (s ?? "").Replace("m", "").Replace(",", ".").Replace("°", "").Trim();
                if (!float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { geoKey = ""; return; }
                v = Mathf.Clamp(v, min, max);
                if (it == null || it != selected) return;
                set(v);
                GeoChanged(it);
                Commit(true);
            });
        }

        void GeoSub(Transform parent, string title, bool open, System.Action toggle)
        {
            var b = AtlasTheme.Box(parent, "Sub", AtlasTheme.AccentDim, 10);
            AtlasTheme.Le(b.gameObject, 40f);
            var btn = b.gameObject.AddComponent<Button>();
            btn.onClick.AddListener(() => toggle());
            var t = AtlasTheme.Label(b.transform, title, 16, Color.white, true);
            AtlasTheme.Fill(t.rectTransform, 12f, 40f);
            t.raycastTarget = false;
            var a = AtlasTheme.IconImage(b.transform, open ? "chevron_down" : "chevron_right", 18f, Color.white);
            AtlasTheme.Pin(a.rectTransform, 1f, 0.5f, new Vector2(-12f, 0f), new Vector2(18f, 18f));
            a.raycastTarget = false;
        }

        void GeoSet(AtlasItem it, System.Action change)
        {
            change();
            GeoChanged(it);
            Commit(true);
        }

        /// The shape is made again with its settings (lo modelado se sustituye).
        void GeoChanged(AtlasItem it)
        {
            if (it.geo == null) it.geo = new GeoParams();
            it.geo.on = true;
            if (!string.IsNullOrEmpty(it.meshFile) || it.edit != null)
            {
                if (meshMode != "") ExitMeshMode();
                it.meshFile = "";
                it.edit = null;
                LoadItemMesh(it);
                ApplyLook(it);
            }
            else LoadItemMesh(it);
            highlighter.Show(it);
        }
    }
}
