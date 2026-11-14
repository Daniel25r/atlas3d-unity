// ATLAS 3D (Unity) - «Papel cebolla»: see-through copies of the selected
// object at its previous key (red) and its next key (blue), or a few frames
// away when it has no keys around. Only in the view; renders never show it.
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public class OnionSkin : MonoBehaviour
    {
        public AtlasItem target;
        public bool dirty = true;
        bool on;
        Material before, after;
        readonly List<GameObject> ghosts = new List<GameObject>();
        readonly List<Mesh> baked = new List<Mesh>();
        float lastFrame = -1f;
        AtlasItem lastTarget;

        public void Init(Material template)
        {
            before = new Material(template);
            MatUtil.SetTransparent(before, true);
            before.SetColor("_BaseColor", new Color(1f, 0.3f, 0.3f, 0.28f));
            after = new Material(template);
            MatUtil.SetTransparent(after, true);
            after.SetColor("_BaseColor", new Color(0.3f, 0.55f, 1f, 0.28f));
        }

        public void SetEnabled(bool v)
        {
            on = v;
            dirty = true;
            if (!on) Clear();
        }

        public void SetVisible(bool v)
        {
            foreach (var g in ghosts) if (g != null) g.SetActive(v);
        }

        void Clear()
        {
            foreach (var g in ghosts) if (g != null) Destroy(g);
            ghosts.Clear();
            foreach (var m in baked) if (m != null) Destroy(m);
            baked.Clear();
        }

        public void Rebuild(float frame, int start, int end, int fps, bool playing)
        {
            if (!on) return;
            if (playing || target == null)
            {
                if (ghosts.Count > 0) Clear();
                lastTarget = null;
                return;
            }
            if (!dirty && Mathf.Approximately(frame, lastFrame) && target == lastTarget) return;
            dirty = false;
            lastFrame = frame;
            lastTarget = target;
            Clear();
            int f = Mathf.RoundToInt(frame);
            var frames = target.TrackFrames("");
            int prev = -1, next = -1;
            foreach (int k in frames)
            {
                if (k < f) prev = k;
                if (k > f && next < 0) next = k;
            }
            if (prev < 0 && frames.Count == 0) prev = f - 6;
            if (next < 0 && frames.Count == 0) next = f + 6;
            if (prev >= start && prev < f) Ghost(prev, before, fps);
            if (next > f && next <= end) Ghost(next, after, fps);
            // Back to the frame on screen.
            target.Evaluate(frame, fps);
        }

        void Ghost(int frame, Material mat, int fps)
        {
            target.Evaluate(frame, fps);
            foreach (var r in target.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r.gameObject.layer == Gizmo.Layer) continue;
                Mesh mesh = null;
                Matrix4x4 m;
                var skin = r as SkinnedMeshRenderer;
                if (skin != null)
                {
                    if (skin.sharedMesh == null) continue;
                    mesh = new Mesh();
                    skin.BakeMesh(mesh, true);
                    baked.Add(mesh);
                    m = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                }
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    mesh = mf.sharedMesh;
                    m = r.transform.localToWorldMatrix;
                }
                var go = new GameObject("Cebolla");
                go.layer = Gizmo.Layer;
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);
                go.transform.localScale = m.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                ghosts.Add(go);
            }
        }
    }
}
