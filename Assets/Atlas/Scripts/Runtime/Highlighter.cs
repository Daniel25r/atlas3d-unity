// ATLAS 3D (Unity) - the selected object glows in the selection colour
// with an outline (like Godot). Each of its meshes is drawn once more with
// the highlight material; skinned meshes share the same bones, so the glow
// follows the pose and the animation. The copies live outside the object,
// so exporting, picking and counting never see them.
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public class Highlighter : MonoBehaviour
    {
        public static readonly Color SelectionColor = new Color(0.94f, 0.65f, 0.25f, 1f);   // #f0a640
        Material mat;
        AtlasItem target;

        class Copy
        {
            public Renderer src;
            public Renderer dst;
            public SkinnedMeshRenderer srcSkin, dstSkin;
        }

        readonly List<Copy> copies = new List<Copy>();

        public void Init(Material unlitTemplate)
        {
            var tmpl = Resources.Load<Material>("AtlasHighlight");
            if (tmpl != null && tmpl.shader != null && tmpl.shader.isSupported)
            {
                mat = new Material(tmpl);
                mat.SetColor("_Color", SelectionColor);
            }
            else
            {
                // Plain see-through tint if the shader cannot run here.
                var t2 = Resources.Load<Material>("AtlasUnlitTransparent");
                mat = new Material(t2 != null ? t2 : unlitTemplate);
                MatUtil.SetTransparent(mat, true);
                mat.SetColor("_BaseColor", new Color(SelectionColor.r, SelectionColor.g, SelectionColor.b, 0.3f));
            }
        }

        public void Show(AtlasItem it)
        {
            Show(it, null, null);
        }

        /// `only`: just the parts that use this material.
        public void Show(AtlasItem it, Material only, Dictionary<Renderer, Material[]> original)
        {
            Clear();
            target = it;
            if (it == null || mat == null) return;
            foreach (var r in it.GetComponentsInChildren<Renderer>())
            {
                if (r.gameObject.layer == Gizmo.Layer) continue;
                if (r is LineRenderer) continue;
                if (only != null)
                {
                    Material[] arr;
                    if (original == null || !original.TryGetValue(r, out arr)) arr = r.sharedMaterials;
                    if (System.Array.IndexOf(arr, only) < 0) continue;
                }
                var skin = r as SkinnedMeshRenderer;
                var mf = r.GetComponent<MeshFilter>();
                Mesh mesh = skin != null ? skin.sharedMesh : (mf != null ? mf.sharedMesh : null);
                if (mesh == null) continue;
                var go = new GameObject("Resaltado");
                go.transform.SetParent(transform, false);
                go.layer = Gizmo.Layer;
                var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                var c = new Copy { src = r };
                if (skin != null)
                {
                    var d = go.AddComponent<SkinnedMeshRenderer>();
                    d.sharedMesh = mesh;
                    d.bones = skin.bones;
                    d.rootBone = skin.rootBone;
                    d.localBounds = skin.localBounds;
                    d.updateWhenOffscreen = true;
                    d.quality = skin.quality;
                    c.srcSkin = skin;
                    c.dstSkin = d;
                    c.dst = d;
                }
                else
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    c.dst = go.AddComponent<MeshRenderer>();
                }
                c.dst.sharedMaterials = mats;
                c.dst.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                c.dst.receiveShadows = false;
                copies.Add(c);
            }
            LateUpdate();
        }

        public void Clear()
        {
            foreach (var c in copies)
                if (c.dst != null) Destroy(c.dst.gameObject);
            copies.Clear();
            target = null;
        }

        /// Hides the glow for a moment (renders, thumbnails).
        public void SetVisible(bool on)
        {
            foreach (var c in copies)
                if (c.dst != null) c.dst.gameObject.SetActive(on);
        }

        void LateUpdate()
        {
            if (target == null)
            {
                if (copies.Count > 0) Clear();
                return;
            }
            foreach (var c in copies)
            {
                if (c.src == null || c.dst == null) continue;
                c.dst.enabled = c.src.enabled && c.src.gameObject.activeInHierarchy;
                if (c.dstSkin == null)
                {
                    var t = c.src.transform;
                    c.dst.transform.SetPositionAndRotation(t.position, t.rotation);
                    c.dst.transform.localScale = t.lossyScale;
                }
                else
                {
                    var t = c.srcSkin.transform;
                    c.dst.transform.SetPositionAndRotation(t.position, t.rotation);
                    c.dst.transform.localScale = t.lossyScale;
                    int n = c.srcSkin.sharedMesh != null ? c.srcSkin.sharedMesh.blendShapeCount : 0;
                    for (int i = 0; i < n; i++) c.dstSkin.SetBlendShapeWeight(i, c.srcSkin.GetBlendShapeWeight(i));
                }
            }
        }
    }
}
