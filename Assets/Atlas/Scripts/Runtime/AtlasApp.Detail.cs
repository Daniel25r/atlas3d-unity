// ATLAS 3D (Unity) - «Reducir polígonos»: heavy imported models are made
// lighter (fewer triangles) so the phone moves them smoothly. The file is
// not changed: the reduction is a setting of the object, made again each
// time the project opens, and «0 %» gives the original back. Textures,
// bones, weights and face sliders are kept (UnityMeshSimplifier, MIT).
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        // Original meshes of each reduced part, and reductions already made.
        readonly Dictionary<Object, Mesh> origMesh = new Dictionary<Object, Mesh>();
        readonly Dictionary<string, Mesh> reducedCache = new Dictionary<string, Mesh>();
        bool reducing;

        static int Tris(Mesh m)
        {
            if (m == null) return 0;
            long n = 0;
            for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3;
            return (int)n;
        }

        int ModelTris(AtlasItem it)
        {
            int n = 0;
            foreach (var r in it.GetComponentsInChildren<SkinnedMeshRenderer>(true)) n += Tris(r.sharedMesh);
            foreach (var f in it.GetComponentsInChildren<MeshFilter>(true)) if (f.gameObject.layer != Gizmo.Layer) n += Tris(f.sharedMesh);
            return n;
        }

        /// Shows the model with its reduction (or the original).
        async void ApplyDetail(AtlasItem it, bool tell)
        {
            if (it == null || it.kind != "model" || !it.loaded) return;
            float quality = 1f - Mathf.Clamp(it.reduce, 0f, 0.95f);
            int before = ModelTris(it);
            var parts = new List<Object>();
            foreach (var r in it.GetComponentsInChildren<SkinnedMeshRenderer>(true)) parts.Add(r);
            foreach (var f in it.GetComponentsInChildren<MeshFilter>(true)) if (f.gameObject.layer != Gizmo.Layer) parts.Add(f);
            if (quality < 0.999f) { reducing = true; if (tell) Toast("Optimizando el modelo…"); }
            try
            {
                foreach (var p in parts)
                {
                    if (p == null) continue;
                    var smr = p as SkinnedMeshRenderer;
                    var mf = p as MeshFilter;
                    Mesh cur = smr != null ? smr.sharedMesh : mf.sharedMesh;
                    Mesh orig;
                    if (!origMesh.TryGetValue(p, out orig) || orig == null) { orig = cur; if (orig != null) origMesh[p] = orig; }
                    if (orig == null) continue;
                    Mesh want = orig;
                    // Small parts are not worth it (and lose their shape).
                    if (quality < 0.999f && orig.isReadable && Tris(orig) > 300)
                    {
                        string key = orig.GetInstanceID() + "@" + Mathf.RoundToInt(quality * 100f);
                        if (!reducedCache.TryGetValue(key, out want) || want == null)
                        {
                            var s = new UnityMeshSimplifier.MeshSimplifier();
                            s.PreserveUVSeamEdges = true;
                            s.PreserveBorderEdges = true;
                            s.Initialize(orig);
                            await Task.Run(() => s.SimplifyMesh(quality));
                            if (it == null || p == null) return;
                            want = s.ToMesh();
                            want.name = orig.name + " (reducida)";
                            want.bindposes = orig.bindposes;
                            reducedCache[key] = want;
                        }
                    }
                    if (smr != null) smr.sharedMesh = want; else mf.sharedMesh = want;
                }
            }
            catch (System.Exception e)
            {
                Toast("No se pudo optimizar: " + e.Message, true);
            }
            finally { reducing = false; }
            if (it == null) return;
            it.ResetMorphsBuilt();
            it.ApplyMorphs();
            if (selected == it && !poseMode) highlighter.Show(it);
            if (tell)
            {
                int after = ModelTris(it);
                Toast(quality >= 0.999f ? "Modelo original (" + after.ToString("N0") + " triángulos)"
                    : "Optimizado: " + before.ToString("N0") + " → " + after.ToString("N0") + " triángulos");
                RefreshProps();
            }
        }

        void BuildDetailSection(AtlasItem it, Transform box)
        {
            int tris = ModelTris(it);
            Hint(box, "Este modelo tiene " + tris.ToString("N0") + " triángulos." + (tris > 60000 ? " Es pesado para un móvil: redúcelo para que vaya más fluido." : ""));
            AtlasTheme.SliderRow(box, "Reducir polígonos (%)", 0f, 95f, it.reduce * 100f, "0", x => it.reduce = x / 100f, () =>
            {
                if (reducing) return;
                Commit(true);
                ApplyDetail(it, true);
            });
        }
    }
}
