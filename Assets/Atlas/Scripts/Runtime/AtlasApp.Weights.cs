// ATLAS 3D (Unity) - weight painting: how much each bone moves each part of
// a rigged model. Pick a bone (pose mode), paint on the model: Add, Sub,
// Smooth or Set, with size, strength, normalize and mirror on X. The model
// shows the chosen bone's weights as colours (blue none … red all). The
// painted weights are saved with the project and come back when it opens.
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        bool weightMode, weightPainting;
        string wpOp = "add";
        float wpSize = 70f, wpStrength = 0.5f;
        bool wpNormalize = true, wpMirror;
        GameObject wpPanel;
        Material weightViewMat;
        readonly Dictionary<Renderer, Material[]> wpKeepMats = new Dictionary<Renderer, Material[]>();
        readonly Dictionary<Mesh, List<int>[]> wpNeighbours = new Dictionary<Mesh, List<int>[]>();
        Mesh wpBaked;
        float wpLastPaint;

        string WeightsPath(AtlasItem it)
        {
            if (store == null || it == null) return "";
            return Path.Combine(Path.Combine(store.projectDir, "weights"), it.id + ".bin");
        }

        // ------------------------------------------------------------------
        // Mode

        void EnterWeightMode()
        {
            var it = selected;
            if (it == null || !it.HasBones) { Toast("Elige un modelo con huesos", true); return; }
            if (activeBone == "" || IkRig.IsControl(activeBone)) { Toast("Primero toca el hueso que quieres pintar (en modo pose)", true); return; }
            weightMode = true;
            if (weightViewMat == null)
            {
                var t = Resources.Load<Material>("AtlasWeightView");
                weightViewMat = t != null ? new Material(t) : null;
            }
            wpKeepMats.Clear();
            foreach (var smr in it.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                wpKeepMats[smr] = smr.sharedMaterials;
                if (weightViewMat != null)
                {
                    var arr = new Material[smr.sharedMaterials.Length];
                    for (int i = 0; i < arr.Length; i++) arr[i] = weightViewMat;
                    smr.sharedMaterials = arr;
                }
            }
            ShowWeights(it);
            gizmo.Show(null);
            BuildWeightPanel();
            Toast("Pintar pesos: pinta sobre el modelo (dos dedos o clic medio giran la vista)");
        }

        void ExitWeightMode(bool save)
        {
            if (!weightMode) return;
            weightMode = false;
            weightPainting = false;
            foreach (var kv in wpKeepMats) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
            wpKeepMats.Clear();
            if (wpPanel != null) Destroy(wpPanel);
            if (save && selected != null) SaveWeights(selected);
            if (selected != null) ApplyLook(selected);
            ShowGizmo();
            RefreshProps();
        }

        void BuildWeightPanel()
        {
            if (wpPanel != null) Destroy(wpPanel);
            var box = AtlasTheme.Box(viewArea, "PintarPesos", AtlasTheme.Panel2, 14);
            wpPanel = box.gameObject;
            AtlasTheme.Pin(box.rectTransform, 1f, 0f, new Vector2(-14f, Desktop ? 14f : 120f), new Vector2(330f, 0f));
            box.rectTransform.pivot = new Vector2(1f, 0f);
            AtlasTheme.Column(box.gameObject, 6f, new RectOffset(12, 12, 10, 12));
            var fit = box.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var t = AtlasTheme.Label(box.transform, "Pintar pesos · " + BoneNames.Display(BoneNames.Leaf(activeBone)), 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t.gameObject, 24f);
            var g = AtlasTheme.Grid(box.transform, 4, 306f, 34f, 4f);
            string[] ops = { "add", "sub", "smooth", "set" };
            string[] names = { "Sumar", "Restar", "Suavizar", "Fijar" };
            for (int i = 0; i < 4; i++)
            {
                string op = ops[i];
                AtlasTheme.Chip(g, names[i], () => { wpOp = op; BuildWeightPanel(); }, wpOp == op);
            }
            AtlasTheme.SliderRow(box.transform, "Tamaño (px)", 10f, 250f, wpSize, "0", x => wpSize = x, null);
            AtlasTheme.SliderRow(box.transform, "Fuerza", 0.02f, 1f, wpStrength, "0.00", x => wpStrength = x, null);
            var g2 = AtlasTheme.Grid(box.transform, 2, 306f, 34f, 4f);
            AtlasTheme.Chip(g2, "Normalizar", () => { wpNormalize = !wpNormalize; BuildWeightPanel(); }, wpNormalize);
            AtlasTheme.Chip(g2, "Espejo X", () => { wpMirror = !wpMirror; BuildWeightPanel(); }, wpMirror);
            var hint = AtlasTheme.Label(box.transform, "Azul: no le afecta · Rojo: lo mueve del todo. Toca otro hueso en la lista de la derecha para pintarlo.", 13, AtlasTheme.TextDim);
            AtlasTheme.Le(hint.gameObject, 36f);
            var g3 = AtlasTheme.Grid(box.transform, 2, 306f, 40f, 4f);
            AtlasTheme.TextButton(g3, "undo", "Pesos originales", () =>
            {
                var it = selected;
                if (it == null) return;
                string p = WeightsPath(it);
                try { if (File.Exists(p)) File.Delete(p); } catch (System.Exception) { }
                ExitWeightMode(false);
                Toast("Se usarán los pesos originales al volver a abrir el proyecto");
            }, false, 40f, 14);
            AtlasTheme.TextButton(g3, null, "Listo", () => ExitWeightMode(true), true, 40f, 15);
        }

        /// Colours: the chosen bone's weight on every vertex.
        void ShowWeights(AtlasItem it)
        {
            var bone = it.Bone(activeBone);
            foreach (var smr in it.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                int bi = System.Array.IndexOf(smr.bones, bone);
                var bw = mesh.boneWeights;
                var cols = new Color32[mesh.vertexCount];
                for (int v = 0; v < cols.Length; v++)
                {
                    float w = v < bw.Length ? WeightOf(bw[v], bi) : 0f;
                    cols[v] = Heat(w);
                }
                mesh.colors32 = cols;
            }
        }

        static float WeightOf(BoneWeight b, int bone)
        {
            if (bone < 0) return 0f;
            float w = 0f;
            if (b.boneIndex0 == bone) w += b.weight0;
            if (b.boneIndex1 == bone) w += b.weight1;
            if (b.boneIndex2 == bone) w += b.weight2;
            if (b.boneIndex3 == bone) w += b.weight3;
            return w;
        }

        static Color32 Heat(float w)
        {
            w = Mathf.Clamp01(w);
            Color c = w < 0.5f ? Color.Lerp(new Color(0.05f, 0.15f, 0.85f), new Color(0.1f, 0.85f, 0.3f), w * 2f)
                               : Color.Lerp(new Color(0.1f, 0.85f, 0.3f), new Color(0.95f, 0.15f, 0.08f), (w - 0.5f) * 2f);
            if (w > 0.4f && w < 0.6f) c = Color.Lerp(c, new Color(1f, 0.9f, 0.1f), 1f - Mathf.Abs(w - 0.5f) * 10f);
            return c;
        }

        // ------------------------------------------------------------------
        // Painting (pointer)

        bool WeightBegin(Vector2 screen)
        {
            if (!weightMode || selected == null) return false;
            weightPainting = true;
            WeightPaint(screen);
            return true;
        }

        void WeightMove(Vector2 screen)
        {
            if (!weightPainting) return;
            if (Time.unscaledTime - wpLastPaint < 0.03f) return;
            WeightPaint(screen);
        }

        void WeightEnd()
        {
            if (!weightPainting) return;
            weightPainting = false;
            if (selected != null) SaveWeights(selected);
        }

        void WeightPaint(Vector2 screen)
        {
            wpLastPaint = Time.unscaledTime;
            var it = selected;
            var bone = it.Bone(activeBone);
            if (bone == null) return;
            float scale = canvasRoot.GetComponent<Canvas>().scaleFactor;
            float rad = wpSize * Mathf.Max(0.5f, scale);
            if (wpBaked == null) wpBaked = new Mesh();
            Vector3 camFwd = cam.transform.forward;
            foreach (var smr in it.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                int bi = System.Array.IndexOf(smr.bones, bone);
                if (bi < 0) continue;
                smr.BakeMesh(wpBaked, true);
                var verts = wpBaked.vertices;
                var nors = wpBaked.normals;
                var bw = mesh.boneWeights;
                if (bw.Length != verts.Length) continue;
                var tr = smr.transform;
                var hits = new List<int>();
                var falloff = new List<float>();
                for (int v = 0; v < verts.Length; v++)
                {
                    Vector3 wp = tr.TransformPoint(verts[v]);
                    Vector3 sp = cam.WorldToScreenPoint(wp);
                    if (sp.z <= 0f) continue;
                    float d = Vector2.Distance(new Vector2(sp.x, sp.y), screen);
                    if (d > rad) continue;
                    if (v < nors.Length && Vector3.Dot(tr.TransformDirection(nors[v]), (wp - cam.transform.position).normalized) > 0.15f) continue;   // the back side
                    hits.Add(v);
                    float f = 1f - d / rad;
                    falloff.Add(f * f * (3f - 2f * f));
                }
                if (hits.Count == 0) continue;
                int mirrorBone = wpMirror ? MirrorBoneIndex(smr, bi) : -1;
                List<int>[] nb = wpOp == "smooth" ? Neighbours(mesh) : null;
                var mirrorOf = wpMirror ? MirrorVertices(mesh) : null;
                var before = (BoneWeight[])bw.Clone();
                for (int k = 0; k < hits.Count; k++)
                {
                    int v = hits[k];
                    PaintVertex(bw, before, v, bi, falloff[k] * wpStrength, nb);
                    if (mirrorOf != null && mirrorOf[v] >= 0 && mirrorBone >= 0 && mirrorOf[v] != v)
                        PaintVertex(bw, before, mirrorOf[v], mirrorBone, falloff[k] * wpStrength, nb);
                }
                mesh.boneWeights = bw;
                var cols = mesh.colors32;
                if (cols.Length == bw.Length)
                {
                    foreach (int v in hits) cols[v] = Heat(WeightOf(bw[v], bi));
                    if (mirrorOf != null) foreach (int v in hits) if (mirrorOf[v] >= 0) cols[mirrorOf[v]] = Heat(WeightOf(bw[mirrorOf[v]], bi));
                    mesh.colors32 = cols;
                }
            }
        }

        void PaintVertex(BoneWeight[] bw, BoneWeight[] before, int v, int bone, float amount, List<int>[] nb)
        {
            var d = ToDict(bw[v]);
            float cur;
            d.TryGetValue(bone, out cur);
            float target = cur;
            switch (wpOp)
            {
                case "add": target = cur + amount; break;
                case "sub": target = cur - amount; break;
                case "set": target = Mathf.Lerp(cur, wpStrength, Mathf.Clamp01(amount / Mathf.Max(0.01f, wpStrength))); break;
                case "smooth":
                    if (nb != null && nb[v] != null && nb[v].Count > 0)
                    {
                        float avg = 0f;
                        foreach (int n in nb[v]) avg += WeightOf(before[n], bone);
                        avg /= nb[v].Count;
                        target = Mathf.Lerp(cur, avg, Mathf.Clamp01(amount * 2f));
                    }
                    break;
            }
            target = Mathf.Clamp01(target);
            if (wpNormalize)
            {
                // The others share what is left, keeping their proportions.
                float others = 0f;
                foreach (var kv in d) if (kv.Key != bone) others += kv.Value;
                var keys = new List<int>(d.Keys);
                foreach (var k in keys)
                    if (k != bone) d[k] = others > 1e-6f ? d[k] / others * (1f - target) : 0f;
                d[bone] = target;
                if (others <= 1e-6f && target < 1f && keys.Count <= 1)
                {
                    // Nothing else held this vertex: give the rest to the parent-most bone it had.
                }
            }
            else d[bone] = target;
            bw[v] = FromDict(d);
        }

        static Dictionary<int, float> ToDict(BoneWeight b)
        {
            var d = new Dictionary<int, float>();
            System.Action<int, float> add = (i, w) => { if (w <= 0f) return; float o; d.TryGetValue(i, out o); d[i] = o + w; };
            add(b.boneIndex0, b.weight0);
            add(b.boneIndex1, b.weight1);
            add(b.boneIndex2, b.weight2);
            add(b.boneIndex3, b.weight3);
            return d;
        }

        /// The 4 biggest, adding up to 1.
        static BoneWeight FromDict(Dictionary<int, float> d)
        {
            var l = new List<KeyValuePair<int, float>>(d);
            l.RemoveAll(kv => kv.Value <= 1e-4f);
            l.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (l.Count > 4) l.RemoveRange(4, l.Count - 4);
            float sum = 0f;
            foreach (var kv in l) sum += kv.Value;
            var r = new BoneWeight();
            if (l.Count == 0 || sum <= 1e-6f) { r.boneIndex0 = 0; r.weight0 = 1f; return r; }
            for (int i = 0; i < l.Count; i++)
            {
                float w = l[i].Value / sum;
                switch (i)
                {
                    case 0: r.boneIndex0 = l[i].Key; r.weight0 = w; break;
                    case 1: r.boneIndex1 = l[i].Key; r.weight1 = w; break;
                    case 2: r.boneIndex2 = l[i].Key; r.weight2 = w; break;
                    case 3: r.boneIndex3 = l[i].Key; r.weight3 = w; break;
                }
            }
            return r;
        }

        List<int>[] Neighbours(Mesh mesh)
        {
            List<int>[] nb;
            if (wpNeighbours.TryGetValue(mesh, out nb)) return nb;
            nb = new List<int>[mesh.vertexCount];
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var t = mesh.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                    for (int a = 0; a < 3; a++)
                    {
                        int v = t[i + a];
                        if (nb[v] == null) nb[v] = new List<int>(6);
                        int b1 = t[i + (a + 1) % 3], b2 = t[i + (a + 2) % 3];
                        if (!nb[v].Contains(b1)) nb[v].Add(b1);
                        if (!nb[v].Contains(b2)) nb[v].Add(b2);
                    }
            }
            wpNeighbours[mesh] = nb;
            return nb;
        }

        readonly Dictionary<Mesh, int[]> wpMirrors = new Dictionary<Mesh, int[]>();

        /// For each vertex, the one at the same place on the other side (X).
        int[] MirrorVertices(Mesh mesh)
        {
            int[] m;
            if (wpMirrors.TryGetValue(mesh, out m)) return m;
            var v = mesh.vertices;
            var b = mesh.bounds;
            float cell = Mathf.Max(1e-4f, b.size.magnitude * 0.004f);
            var grid = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < v.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v[i].x / cell), Mathf.RoundToInt(v[i].y / cell), Mathf.RoundToInt(v[i].z / cell));
                List<int> l;
                if (!grid.TryGetValue(k, out l)) { l = new List<int>(); grid[k] = l; }
                l.Add(i);
            }
            m = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                m[i] = -1;
                var p = new Vector3(-v[i].x, v[i].y, v[i].z);
                var k = new Vector3Int(Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.y / cell), Mathf.RoundToInt(p.z / cell));
                float best = cell * cell * 4f;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            List<int> l;
                            if (!grid.TryGetValue(new Vector3Int(k.x + dx, k.y + dy, k.z + dz), out l)) continue;
                            foreach (int j in l)
                            {
                                float d = (v[j] - p).sqrMagnitude;
                                if (d < best) { best = d; m[i] = j; }
                            }
                        }
            }
            wpMirrors[mesh] = m;
            return m;
        }

        /// The bone on the other side (Left ↔ Right in its name).
        static int MirrorBoneIndex(SkinnedMeshRenderer smr, int bi)
        {
            var bones = smr.bones;
            if (bi < 0 || bi >= bones.Length || bones[bi] == null) return bi;
            string n = bones[bi].name;
            string[,] pairs = { { "Left", "Right" }, { "left", "right" }, { "_L", "_R" }, { ".L", ".R" }, { "_l", "_r" }, { ".l", ".r" }, { "L_", "R_" }, { "l_", "r_" }, { "Izq", "Der" } };
            for (int p = 0; p < pairs.GetLength(0); p++)
            {
                string a = pairs[p, 0], b = pairs[p, 1];
                string m = n.Contains(a) ? n.Replace(a, b) : n.Contains(b) ? n.Replace(b, a) : null;
                if (m == null) continue;
                for (int i = 0; i < bones.Length; i++) if (bones[i] != null && bones[i].name == m) return i;
            }
            return bi;   // a middle bone (spine, head): itself
        }

        // ------------------------------------------------------------------
        // Saved with the project

        void SaveWeights(AtlasItem it)
        {
            string p = WeightsPath(it);
            if (p == "") return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                using (var w = new BinaryWriter(File.Create(p)))
                {
                    var smrs = it.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    w.Write(0x57544C41);   // "ATLW"
                    w.Write(smrs.Length);
                    foreach (var smr in smrs)
                    {
                        var bw = smr.sharedMesh != null && smr.sharedMesh.isReadable ? smr.sharedMesh.boneWeights : new BoneWeight[0];
                        w.Write(bw.Length);
                        foreach (var b in bw)
                        {
                            w.Write(b.boneIndex0); w.Write(b.boneIndex1); w.Write(b.boneIndex2); w.Write(b.boneIndex3);
                            w.Write(b.weight0); w.Write(b.weight1); w.Write(b.weight2); w.Write(b.weight3);
                        }
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("ATLAS: pesos: " + e.Message); }
        }

        /// After the model loads: the weights painted before.
        void LoadWeights(AtlasItem it)
        {
            string p = WeightsPath(it);
            if (p == "" || !File.Exists(p)) return;
            try
            {
                using (var r = new BinaryReader(File.OpenRead(p)))
                {
                    if (r.ReadInt32() != 0x57544C41) return;
                    var smrs = it.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    int n = r.ReadInt32();
                    for (int s = 0; s < n; s++)
                    {
                        int count = r.ReadInt32();
                        var bw = new BoneWeight[count];
                        for (int i = 0; i < count; i++)
                        {
                            bw[i].boneIndex0 = r.ReadInt32(); bw[i].boneIndex1 = r.ReadInt32(); bw[i].boneIndex2 = r.ReadInt32(); bw[i].boneIndex3 = r.ReadInt32();
                            bw[i].weight0 = r.ReadSingle(); bw[i].weight1 = r.ReadSingle(); bw[i].weight2 = r.ReadSingle(); bw[i].weight3 = r.ReadSingle();
                        }
                        if (s < smrs.Length && smrs[s].sharedMesh != null && smrs[s].sharedMesh.isReadable && smrs[s].sharedMesh.vertexCount == count)
                            smrs[s].sharedMesh.boneWeights = bw;
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning("ATLAS: pesos: " + e.Message); }
        }
    }
}
