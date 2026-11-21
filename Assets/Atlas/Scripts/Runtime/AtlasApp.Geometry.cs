// ATLAS 5D (Unity) - «Geometría»: the settings of a shape (segments,
// rings, size), a radial deformation (ribs or grooves, twist, holes) and
// the kind of faces (quads, triangles, R-Cube). The mesh is made again on
// every change; everything can be undone.
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        void BuildGeoSection(AtlasItem it)
        {
            if (!ShapeGen.Supports(it.kind)) return;
            if (it.geo == null) it.geo = new GeoParams();
            var g = it.geo;
            var box = Sec("Geometría", "cube");
            if (!string.IsNullOrEmpty(it.meshFile))
                Hint(box, "Esta forma ya está modelada: al cambiar su geometría se hace de nuevo y se pierde lo modelado (Deshacer lo recupera).");
            int seg = g.seg > 0 ? g.seg : ShapeGen.DefaultSeg(it.kind);
            int rings = g.rings > 0 ? g.rings : ShapeGen.DefaultRings(it.kind);
            bool flat = it.kind == "plane" || it.kind == "cube";
            AtlasTheme.SliderRow(box, flat ? "Divisiones" : "Segmentos", it.kind == "cube" ? 1f : flat ? 1f : 3f, it.kind == "cube" ? 16f : 96f, seg, "0",
                v => { int n = Mathf.RoundToInt(v); if (n != (g.seg > 0 ? g.seg : ShapeGen.DefaultSeg(it.kind))) { g.seg = n; GeoChanged(it); } }, () => Commit(true));
            if (ShapeGen.HasRings(it.kind))
                AtlasTheme.SliderRow(box, it.kind == "plane" ? "Divisiones (fondo)" : it.kind == "sphere" && g.topo == 1 ? "Detalle" : "Anillos", 1f, 64f, rings, "0",
                    v => { int n = Mathf.RoundToInt(v); if (n != (g.rings > 0 ? g.rings : ShapeGen.DefaultRings(it.kind))) { g.rings = n; GeoChanged(it); } }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Radio (tamaño)", 0.1f, 5f, g.radius, "0.00", v => { g.radius = v; GeoChanged(it); }, () => Commit(true));

            var t1 = AtlasTheme.Label(box, "Deformación radial", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t1.gameObject, 26f);
            AtlasTheme.SliderRow(box, "Cantidad", 0f, 1f, g.deform, "0.00", v => { g.deform = v; GeoChanged(it); }, () => Commit(true));
            var dg = AtlasTheme.Grid(box, 2, InnerWidth, 36f, 4f);
            AtlasTheme.Chip(dg, "Hundir", () => { g.sink = true; GeoChanged(it); Commit(true); RefreshProps(); }, g.sink);
            AtlasTheme.Chip(dg, "Extender", () => { g.sink = false; GeoChanged(it); Commit(true); RefreshProps(); }, !g.sink);
            AtlasTheme.SliderRow(box, "Ondas", 1f, 32f, g.waves, "0", v => { int n = Mathf.RoundToInt(v); if (n != g.waves) { g.waves = n; GeoChanged(it); } }, () => Commit(true));
            var ag = AtlasTheme.Grid(box, 3, InnerWidth, 36f, 4f);
            string[] axes = { "Eje X", "Eje Y", "Eje Z" };
            for (int a0 = 0; a0 < 3; a0++)
            {
                int a = a0;
                AtlasTheme.Chip(ag, axes[a], () => { g.axis = a; GeoChanged(it); Commit(true); RefreshProps(); }, g.axis == a);
            }
            AtlasTheme.Switch(box, "Espejo (igual en toda la altura; apagado crece hacia arriba)", g.mirror, on => { g.mirror = on; GeoChanged(it); Commit(true); });
            AtlasTheme.SliderRow(box, "Torsión geométrica (°)", -360f, 360f, g.twist, "0", v => { g.twist = v; GeoChanged(it); }, () => Commit(true));
            AtlasTheme.Switch(box, "Huecos (quita caras alternas, como una rejilla)", g.holes, on => { g.holes = on; GeoChanged(it); Commit(true); });

            var t2 = AtlasTheme.Label(box, "Topología", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t2.gameObject, 26f);
            bool rc = ShapeGen.HasRCube(it.kind);
            var tg = AtlasTheme.Grid(box, rc ? 3 : 2, InnerWidth, 36f, 4f);
            AtlasTheme.Chip(tg, "Cuadriláteros", () => { g.topo = 0; GeoChanged(it); Commit(true); RefreshProps(); }, g.topo == 0);
            AtlasTheme.Chip(tg, "Triángulos", () => { g.topo = 1; GeoChanged(it); Commit(true); RefreshProps(); }, g.topo == 1);
            if (rc) AtlasTheme.Chip(tg, "R-Cube", () => { g.topo = 2; GeoChanged(it); Commit(true); RefreshProps(); }, g.topo == 2);
            if (rc && g.topo == 2)
            {
                if (it.kind == "cube")
                    AtlasTheme.SliderRow(box, "Redondeo de bordes", 0.02f, 0.5f, g.round, "0.00", v => { g.round = v; GeoChanged(it); }, () => Commit(true));
                else Hint(box, "R-Cube: una esfera hecha con un cubo; sus caras son cuadradas y parejas, sin polos (mejor para modelar y esculpir).");
            }
            if (g.on)
                AtlasTheme.TextButton(box, "undo", "Forma de siempre", () =>
                {
                    it.geo = new GeoParams();
                    if (it.edit == null) LoadItemMesh(it);
                    highlighter.Show(it);
                    Commit(true);
                    RefreshProps();
                }, false, 40f, 14);
        }

        /// The shape is made again with its settings (lo modelado se sustituye).
        void GeoChanged(AtlasItem it)
        {
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
