// ATLAS 3D (Unity) - the materials of an imported model, one by one: tap a
// selected model again and the material under the finger is chosen (the
// Godot version did the same with pieces). Its colour, metal, roughness,
// reflections, glow, relief (normal map), glow map and visibility can be
// changed; the changes are kept in the project and undone like any other.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    [Serializable]
    public class MatOverride
    {
        public int index;
        public string name = "";
        public bool tint;
        public Color color = Color.white;
        public float metallic = -1f, smoothness = -1f, emission = -1f;
        public string normalMap = "", emissionMap = "", texture = "";
        public float bump = 1f;
        public bool hidden;

        public MatOverride Copy() { return (MatOverride)MemberwiseClone(); }
    }

    public class MatOriginal
    {
        public Color color, emission;
        public float metallic, smoothness, bump;
        public Texture bumpMap, emissionMap, baseMap;
    }

    public partial class AtlasApp
    {
        int activeMat = -1;
        Material invisibleMat;

        /// The model's distinct materials, in the order of its parts.
        static List<Material> CollectMaterials(AtlasItem it)
        {
            var l = new List<Material>();
            foreach (var r in it.GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.layer == Gizmo.Layer) continue;
                Material[] arr;
                if (!it.rendererMats.TryGetValue(r, out arr)) { arr = r.sharedMaterials; it.rendererMats[r] = arr; }
                foreach (var m in arr) if (m != null && !l.Contains(m)) l.Add(m);
            }
            return l;
        }

        MatOverride OverrideFor(AtlasItem it, int index, bool create)
        {
            foreach (var o in it.matOverrides) if (o.index == index) return o;
            if (!create) return null;
            var n = new MatOverride { index = index, name = index < it.modelMats.Count ? it.modelMats[index].name : "" };
            it.matOverrides.Add(n);
            return n;
        }

        static float GetF(Material m, string p, float def) { return m.HasProperty(p) ? m.GetFloat(p) : def; }

        /// Originals, then the model's shine, then each material's changes.
        void ApplyModelMaterials(AtlasItem it)
        {
            if (it.modelMats.Count == 0) it.modelMats = CollectMaterials(it);
            // Parts back to their own materials (hidden ones are swapped below).
            foreach (var kv in it.rendererMats) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
            for (int i = 0; i < it.modelMats.Count; i++)
            {
                var m = it.modelMats[i];
                if (m == null) continue;
                MatOriginal o;
                if (!it.matOriginals.TryGetValue(m, out o))
                {
                    o = new MatOriginal
                    {
                        color = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white,
                        emission = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black,
                        metallic = GetF(m, "_Metallic", 0f),
                        smoothness = GetF(m, "_Smoothness", 0.4f),
                        bump = GetF(m, "_BumpScale", 1f),
                        bumpMap = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null,
                        emissionMap = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null,
                        baseMap = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null,
                    };
                    it.matOriginals[m] = o;
                }
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", o.color);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", o.emission);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", it.modelGloss < 0f ? o.metallic : 0f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", it.modelGloss < 0f ? o.smoothness : it.modelGloss * 0.85f);
                if (m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", o.bumpMap); m.SetFloat("_BumpScale", o.bump); }
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", o.emissionMap);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", o.baseMap);
                var ov = OverrideFor(it, i, false);
                if (ov == null) continue;
                var btex = LoadTexture(ov.texture);
                if (btex != null && m.HasProperty("_BaseMap"))
                {
                    m.SetTexture("_BaseMap", btex);
                    // Parts made without a texture layout (UV) showed one
                    // pixel of the picture (often just black).
                    foreach (var kv in it.rendererMats)
                        if (kv.Key != null && System.Array.IndexOf(kv.Value, m) >= 0) UvFix.Ensure(kv.Key);
                }
                Color baseCol = ov.tint ? new Color(ov.color.r, ov.color.g, ov.color.b, o.color.a) : o.color;
                if (ov.tint && m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseCol);
                if (ov.metallic >= 0f && m.HasProperty("_Metallic")) m.SetFloat("_Metallic", ov.metallic);
                if (ov.smoothness >= 0f && m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", ov.smoothness);
                var nmap = LoadTexture(ov.normalMap, true);
                if (nmap != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", nmap); m.SetFloat("_BumpScale", ov.bump); }
                var emap = LoadTexture(ov.emissionMap);
                if (emap != null && m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emap);
                if ((ov.emission >= 0f || emap != null) && m.HasProperty("_EmissionColor"))
                {
                    float e = Mathf.Max(ov.emission, emap != null ? 1f : 0f);
                    if (e > 0f) m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    m.SetColor("_EmissionColor", new Color(baseCol.r, baseCol.g, baseCol.b) * e);
                }
            }
            // Hidden materials: their slots draw nothing.
            foreach (var ov in it.matOverrides)
            {
                if (!ov.hidden || ov.index >= it.modelMats.Count) continue;
                var hm = it.modelMats[ov.index];
                foreach (var kv in it.rendererMats)
                {
                    if (kv.Key == null) continue;
                    var arr = kv.Key.sharedMaterials;
                    bool changed = false;
                    for (int k = 0; k < arr.Length; k++) if (kv.Value.Length > k && kv.Value[k] == hm) { arr[k] = InvisibleMat(); changed = true; }
                    if (changed) kv.Key.sharedMaterials = arr;
                }
            }
        }

        Material InvisibleMat()
        {
            if (invisibleMat == null)
            {
                var t = Resources.Load<Material>("AtlasLitTransparent");
                invisibleMat = new Material(t != null ? t : litTemplate);
                MatUtil.SetTransparent(invisibleMat, true);
                invisibleMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
                if (invisibleMat.HasProperty("_EmissionColor")) invisibleMat.SetColor("_EmissionColor", Color.black);
            }
            return invisibleMat;
        }

        /// The material under the finger on the selected model (-1: none).
        int PickModelMaterial(AtlasItem it, Vector2 screen)
        {
            if (it == null || it.modelMats.Count == 0) return -1;
            Ray ray = cam.ScreenPointToRay(screen);
            float best = float.MaxValue;
            Material found = null;
            foreach (var kv in it.rendererMats)
            {
                var r = kv.Key;
                if (r == null || !r.enabled) continue;
                Mesh mesh;
                Matrix4x4 m;
                var skin = r as SkinnedMeshRenderer;
                if (skin != null)
                {
                    if (skin.sharedMesh == null || !skin.sharedMesh.isReadable) continue;
                    if (pickMesh == null) pickMesh = new Mesh();
                    skin.BakeMesh(pickMesh, true);
                    mesh = pickMesh;
                    m = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                }
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                    mesh = mf.sharedMesh;
                    m = r.transform.localToWorldMatrix;
                }
                Matrix4x4 inv = m.inverse;
                Vector3 o = inv.MultiplyPoint3x4(ray.origin), d = inv.MultiplyVector(ray.direction);
                var v = mesh.vertices;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                    var tri = mesh.GetTriangles(s);
                    for (int i = 0; i + 2 < tri.Length; i += 3)
                    {
                        float t;
                        if (!RayTriangle(o, d, v[tri[i]], v[tri[i + 1]], v[tri[i + 2]], out t)) continue;
                        float dist = (m.MultiplyPoint3x4(o + d * t) - ray.origin).magnitude;
                        if (dist < best && kv.Value.Length > 0)
                        {
                            best = dist;
                            found = kv.Value[Mathf.Min(s, kv.Value.Length - 1)];
                        }
                    }
                }
            }
            return found != null ? it.modelMats.IndexOf(found) : -1;
        }

        void SetActiveMaterial(int index)
        {
            activeMat = index;
            if (selected != null && index >= 0 && index < selected.modelMats.Count) highlighter.Show(selected, selected.modelMats[index], selected.rendererMats);
            else if (selected != null && !poseMode) highlighter.Show(selected);
            RefreshMode();
            RefreshProps();
        }

        // ------------------------------------------------------------------
        // Inspector

        void BuildModelMaterialsList(AtlasItem it)
        {
            if (it.modelMats.Count == 0) return;
            var box = Sec("Materiales del modelo", "material", activeMat < 0);
            AtlasTheme.Switch(box, "Píxeles nítidos (estilo Minecraft)", it.pixelated, on => { it.pixelated = on; ApplyLook(it); Commit(true); });
            Hint(box, "Toca otra vez el modelo para elegir el material de esa parte, o elígelo aquí.");
            var g = AtlasTheme.Grid(box, 2, InnerWidth, 38f);
            for (int i = 0; i < it.modelMats.Count; i++)
            {
                int idx = i;
                var m = it.modelMats[i];
                string n = m != null && !string.IsNullOrEmpty(m.name) ? m.name.Replace(" (Instance)", "") : "Material " + (i + 1);
                var ov = OverrideFor(it, i, false);
                if (ov != null && ov.hidden) n = "(oculto) " + n;
                AtlasTheme.Chip(g, n, () => SetActiveMaterial(activeMat == idx ? -1 : idx), activeMat == idx);
            }
        }

        void BuildModelMaterialSection(AtlasItem it, int index)
        {
            var m = it.modelMats[index];
            string name = m != null ? m.name.Replace(" (Instance)", "") : "Material";
            var box = Sec("Material: " + name, "material");
            AtlasTheme.TextButton(box, "material", "Abrir en el editor de materiales…", () => OpenMatEditor(it, index), true, 44f, 15);
            AtlasTheme.TextButton(box, "undo", "Volver a todo el modelo", () => SetActiveMaterial(-1), false, 40f, 15);
            MatOriginal orig;
            it.matOriginals.TryGetValue(m, out orig);
            var ov = OverrideFor(it, index, false);
            Color cur = ov != null && ov.tint ? ov.color : (orig != null ? orig.color : Color.white);
            float metal = ov != null && ov.metallic >= 0f ? ov.metallic : GetF(m, "_Metallic", 0f);
            float smooth = ov != null && ov.smoothness >= 0f ? ov.smoothness : GetF(m, "_Smoothness", 0.4f);
            float emis = ov != null && ov.emission >= 0f ? ov.emission : 0f;
            // Colour (tints the texture)
            var cl = AtlasTheme.Label(box, "Color (tiñe la textura)", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(cl.gameObject, 24f);
            var g = AtlasTheme.Grid(box, 8, InnerWidth, 34f, 6f);
            foreach (var c0 in Swatches)
            {
                Color cc = c0;
                var sw = AtlasTheme.Box(g, "Color", cc, 8);
                sw.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    var o = OverrideFor(it, index, true);
                    o.tint = true;
                    o.color = cc;
                    ApplyLook(it);
                    Commit(true);
                    RefreshProps();
                });
            }
            float h, s, v;
            Color.RGBToHSV(cur, out h, out s, out v);
            float[] hsv = { h, s, v };
            AtlasTheme.SliderRow(box, "Tono", 0f, 1f, h, "0.00", x => { hsv[0] = x; SetMatHsv(it, index, hsv); }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Saturación", 0f, 1f, s, "0.00", x => { hsv[1] = x; SetMatHsv(it, index, hsv); }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Brillo", 0f, 1f, v, "0.00", x => { hsv[2] = x; SetMatHsv(it, index, hsv); }, () => Commit(true));
            // Surface
            AtlasTheme.SliderRow(box, "Reflejos", 0f, 1f, Mathf.Clamp01(metal / 0.85f), "0.00", x =>
            {
                var o = OverrideFor(it, index, true);
                o.metallic = x * 0.85f;
                o.smoothness = 0.35f + 0.6f * x;
                ApplyLook(it);
            }, () => { Commit(true); RefreshProps(); });
            AtlasTheme.SliderRow(box, "Metálico", 0f, 1f, metal, "0.00", x => { OverrideFor(it, index, true).metallic = x; ApplyLook(it); }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Rugosidad", 0f, 1f, 1f - smooth, "0.00", x => { OverrideFor(it, index, true).smoothness = 1f - x; ApplyLook(it); }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Emisión", 0f, 8f, emis, "0.0", x => { OverrideFor(it, index, true).emission = x; ApplyLook(it); }, () => Commit(true));
            // Maps
            var mg = AtlasTheme.Grid(box, 2, InnerWidth, 40f);
            bool hasNormal = ov != null && !string.IsNullOrEmpty(ov.normalMap);
            if (!hasNormal)
            {
                AtlasTheme.TextButton(mg, "image", "Mapa normal…", () => PickMaterialMap(it, index, "normal"), false, 40f, 15);
                AtlasTheme.TextButton(mg, "image", "Relieve (bump)…", () => PickMaterialMap(it, index, "bump"), false, 40f, 15);
            }
            else
            {
                AtlasTheme.TextButton(mg, "image", "Cambiar relieve…", () => PickMaterialMap(it, index, "normal"), false, 40f, 15);
                AtlasTheme.TextButton(mg, "close", "Quitar relieve", () => { OverrideFor(it, index, true).normalMap = ""; ApplyLook(it); Commit(true); RefreshProps(); }, false, 40f, 15);
            }
            if (ov == null || string.IsNullOrEmpty(ov.emissionMap))
                AtlasTheme.TextButton(mg, "sparkle", "Mapa de emisión…", () => PickMaterialMap(it, index, "emission"), false, 40f, 15);
            else
                AtlasTheme.TextButton(mg, "close", "Quitar emisión", () => { OverrideFor(it, index, true).emissionMap = ""; ApplyLook(it); Commit(true); RefreshProps(); }, false, 40f, 15);
            if (hasNormal)
                AtlasTheme.SliderRow(box, "Relieve", 0f, 3f, ov.bump, "0.0", x => { OverrideFor(it, index, true).bump = x; ApplyLook(it); }, () => Commit(true));
            bool hidden = ov != null && ov.hidden;
            AtlasTheme.Switch(box, "Visible", !hidden, on =>
            {
                OverrideFor(it, index, true).hidden = !on;
                ApplyLook(it);
                Commit(true);
                RefreshProps();
            });
            AtlasTheme.TextButton(box, "undo", "Restablecer este material", () =>
            {
                it.matOverrides.RemoveAll(o => o.index == index);
                ApplyLook(it);
                Commit(true);
                RefreshProps();
            }, false, 40f, 15);
        }

        void SetMatHsv(AtlasItem it, int index, float[] hsv)
        {
            var o = OverrideFor(it, index, true);
            o.tint = true;
            o.color = Color.HSVToRGB(hsv[0], hsv[1], hsv[2]);
            ApplyLook(it);
        }

        void PickMaterialMap(AtlasItem it, int index, string slot)
        {
            if (store == null || NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path) || store == null || it == null) return;
                string file;
                try { file = store.AddTextureFile(path); }
                catch (Exception e) { Toast("No se pudo usar la imagen: " + e.Message, true); return; }
                var o = OverrideFor(it, index, true);
                if (slot == "bump")
                {
                    string made = BumpToNormal(file);
                    if (made == null) { Toast("Esa imagen no se puede leer (usa PNG o JPG)", true); return; }
                    o.normalMap = made;
                }
                else if (slot == "normal") o.normalMap = file;
                else { o.emissionMap = file; if (o.emission < 1f) o.emission = 1f; }
                ApplyLook(it);
                Commit(true);
                RefreshProps();
                Toast(slot == "emission" ? "Mapa de emisión puesto" : "Relieve puesto");
            }, new string[] { "image/*" });
        }
    }
}
