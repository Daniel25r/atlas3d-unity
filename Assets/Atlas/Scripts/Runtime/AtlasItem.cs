// ATLAS 3D (Unity) - marks a scene object made by the user: its settings,
// its animation keys and, for rigged models, the bones and their pose.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    /// One object of the scene (on the root GameObject of each shape,
    /// light or imported model).
    public class AtlasItem : MonoBehaviour
    {
        public string id;
        public string kind;          // cube | sphere | cylinder | capsule | plane | cone | pyramid | torus | light | spot | sun | model
        public Color color = Color.white;
        // Material (shapes)
        public float metallic = 0f;
        public float smoothness = 0.45f;
        public float emission = 0f;
        public float opacity = 1f;
        public string texture = "";  // file inside the project's «textures» folder
        public Material material;    // this object's own material
        // Light
        public float lightIntensity = 3f;
        public float lightRange = 10f;
        public float spotAngle = 45f;
        public bool shadows = true;
        // Scene
        public bool visible = true;
        public bool locked = false;
        public string clip = "";     // imported model: animation clip played on the timeline
        public string modelFile;     // models: file name inside the project's «models» folder
        public IDisposable importer; // keeps the imported model's resources alive
        public bool loaded;          // models: the file is in the scene
        /// Animation: keys sorted by frame (position, rotation, scale and
        /// the posed bones).
        public List<KeyData> keys = new List<KeyData>();
        /// Bones moved by hand (local position / rotation / scale).
        public List<BonePose> pose = new List<BonePose>();

        Animation anim;
        // Bones: path from this object -> bone, in hierarchy order.
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly List<string> boneOrder = new List<string>();
        readonly Dictionary<string, BonePose> rest = new Dictionary<string, BonePose>();

        public bool IsLight { get { return kind == "light" || kind == "spot" || kind == "sun"; } }
        public bool IsShape { get { return !IsLight && kind != "model"; } }
        public bool HasBones { get { return bones.Count > 0; } }
        public List<string> BoneOrder { get { return boneOrder; } }

        public Animation ModelAnimation
        {
            get
            {
                if (anim == null) anim = GetComponentInChildren<Animation>();
                return anim;
            }
        }

        /// Names of the animations that came inside an imported model.
        public List<string> ClipNames()
        {
            var list = new List<string>();
            var a = ModelAnimation;
            if (a == null) return list;
            foreach (AnimationState s in a)
                if (s != null && s.clip != null) list.Add(s.name);
            return list;
        }

        /// Poses the model's own animation at `seconds` (looping).
        public void SampleClip(float seconds)
        {
            if (string.IsNullOrEmpty(clip)) return;
            var a = ModelAnimation;
            if (a == null) return;
            AnimationState st = a[clip];
            if (st == null) return;
            float len = st.length;
            st.enabled = true;
            st.weight = 1f;
            st.time = len > 0f ? Mathf.Repeat(seconds, len) : 0f;
            a.Sample();
            st.enabled = false;
        }

        // ------------------------------------------------------------------
        // Bones

        public string PathOf(Transform t)
        {
            var parts = new List<string>();
            while (t != null && t != transform)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// Finds the skeleton (the bones used by the model's skinned meshes)
        /// and remembers the rest pose. Call once the model is loaded.
        public void BuildBones()
        {
            bones.Clear();
            boneOrder.Clear();
            rest.Clear();
            var used = new HashSet<Transform>();
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var b in smr.bones)
                    if (b != null && b.IsChildOf(transform)) used.Add(b);
            if (used.Count == 0) return;
            // Hierarchy order (parents before children).
            var stack = new Stack<Transform>();
            stack.Push(transform);
            var ordered = new List<Transform>();
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (used.Contains(t)) ordered.Add(t);
                for (int i = t.childCount - 1; i >= 0; i--) stack.Push(t.GetChild(i));
            }
            foreach (var t in ordered)
            {
                string p = PathOf(t);
                if (bones.ContainsKey(p)) continue;
                bones[p] = t;
                boneOrder.Add(p);
                rest[p] = BonePose.From(p, t);
            }
        }

        public Transform Bone(string path)
        {
            Transform t;
            return path != null && bones.TryGetValue(path, out t) ? t : null;
        }

        /// The bone's parent bone (for drawing the skeleton), or "".
        public string ParentBone(string path)
        {
            var t = Bone(path);
            if (t == null) return "";
            for (var p = t.parent; p != null && p != transform; p = p.parent)
            {
                string pp = PathOf(p);
                if (bones.ContainsKey(pp)) return pp;
            }
            return "";
        }

        public BonePose FindPose(string path)
        {
            foreach (var b in pose) if (b.path == path) return b;
            return null;
        }

        /// Stores the bone's current transform in the pose.
        public void RecordBone(string path)
        {
            var t = Bone(path);
            if (t == null) return;
            var bp = BonePose.From(path, t);
            for (int i = 0; i < pose.Count; i++)
                if (pose[i].path == path) { pose[i] = bp; return; }
            pose.Add(bp);
        }

        public void ResetBone(string path)
        {
            var t = Bone(path);
            BonePose r;
            if (t == null || !rest.TryGetValue(path, out r)) return;
            r.ApplyTo(t);
            RecordBone(path);
        }

        public void ResetPose()
        {
            foreach (var kv in rest)
            {
                var t = Bone(kv.Key);
                if (t != null) kv.Value.ApplyTo(t);
            }
            pose.Clear();
        }

        public bool IsPosed(string path)
        {
            if (FindPose(path) != null) return true;
            foreach (var k in keys)
                if (k.bones != null)
                    foreach (var b in k.bones) if (b.path == path) return true;
            return false;
        }

        /// Puts the posed bones where the pose says.
        public void ApplyPose()
        {
            foreach (var b in pose)
            {
                var t = Bone(b.path);
                if (t != null) b.ApplyTo(t);
            }
        }

        // ------------------------------------------------------------------
        // Keys

        public bool Animated { get { return keys.Count > 0; } }

        /// Stores the object's transform and its posed bones as a key.
        public void SetKey(int frame)
        {
            var k = new KeyData { frame = frame, position = transform.position, rotation = transform.rotation, scale = transform.localScale };
            foreach (var b in pose)
            {
                var t = Bone(b.path);
                k.bones.Add(t != null ? BonePose.From(b.path, t) : b.Copy());
            }
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].frame == frame) { keys[i] = k; return; }
                if (keys[i].frame > frame) { keys.Insert(i, k); return; }
            }
            keys.Add(k);
        }

        public bool RemoveKey(int frame)
        {
            return keys.RemoveAll(k => k.frame == frame) > 0;
        }

        public bool HasKey(int frame)
        {
            foreach (var k in keys) if (k.frame == frame) return true;
            return false;
        }

        static float Ease(float t)
        {
            return t * t * (3f - 2f * t);
        }

        /// Everything at `frame`: the model's own animation, the object's
        /// keys and the bones (keys, or the pose when they have none).
        public void Evaluate(float frame, float fps)
        {
            if (!string.IsNullOrEmpty(clip)) SampleClip(frame / Mathf.Max(1f, fps));
            if (keys.Count > 0)
            {
                KeyData a = keys[0], b = keys[keys.Count - 1];
                if (frame <= a.frame) Apply(a);
                else if (frame >= b.frame) Apply(b);
                else
                {
                    for (int i = 0; i < keys.Count - 1; i++)
                        if (frame >= keys[i].frame && frame <= keys[i + 1].frame) { a = keys[i]; b = keys[i + 1]; break; }
                    float t = Ease(Mathf.InverseLerp(a.frame, b.frame, frame));
                    transform.position = Vector3.Lerp(a.position, b.position, t);
                    transform.rotation = Quaternion.Slerp(a.rotation, b.rotation, t);
                    transform.localScale = Vector3.Lerp(a.scale, b.scale, t);
                }
            }
            if (bones.Count == 0) return;
            // Bones with keys follow them; the other posed bones keep the pose.
            var done = new HashSet<string>();
            if (keys.Count > 0)
            {
                foreach (var k in keys)
                {
                    if (k.bones == null) continue;
                    foreach (var kb in k.bones)
                    {
                        if (done.Contains(kb.path)) continue;
                        done.Add(kb.path);
                        var t = Bone(kb.path);
                        if (t == null) continue;
                        var v = BoneAt(kb.path, frame);
                        if (v == null) continue;
                        v.ApplyTo(t);
                        // The pose shows what is on screen now.
                        var p = FindPose(kb.path);
                        if (p != null) { p.p = v.p; p.r = v.r; p.s = v.s; }
                    }
                }
            }
            foreach (var b in pose)
            {
                if (done.Contains(b.path)) continue;
                var t = Bone(b.path);
                if (t != null) b.ApplyTo(t);
            }
        }

        BonePose BoneAt(string path, float frame)
        {
            BonePose before = null, after = null;
            int fb = 0, fa = 0;
            foreach (var k in keys)
            {
                if (k.bones == null) continue;
                BonePose v = null;
                foreach (var b in k.bones) if (b.path == path) { v = b; break; }
                if (v == null) continue;
                if (k.frame <= frame) { before = v; fb = k.frame; }
                if (k.frame >= frame && after == null) { after = v; fa = k.frame; }
            }
            if (before == null) return after;
            if (after == null || fa == fb) return before;
            float t = Ease(Mathf.InverseLerp(fb, fa, frame));
            return new BonePose
            {
                path = path,
                p = Vector3.Lerp(before.p, after.p, t),
                r = Quaternion.Slerp(before.r, after.r, t),
                s = Vector3.Lerp(before.s, after.s, t),
            };
        }

        void Apply(KeyData k)
        {
            transform.position = k.position;
            transform.rotation = k.rotation;
            transform.localScale = k.scale;
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (importer != null)
            {
                importer.Dispose();
                importer = null;
            }
        }
    }

    [Serializable]
    public class KeyData
    {
        public int frame;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 scale = Vector3.one;
        public List<BonePose> bones = new List<BonePose>();
    }

    /// A bone's local transform (by its path inside the model).
    [Serializable]
    public class BonePose
    {
        public string path;
        public Vector3 p;
        public Quaternion r = Quaternion.identity;
        public Vector3 s = Vector3.one;

        public static BonePose From(string path, Transform t)
        {
            return new BonePose { path = path, p = t.localPosition, r = t.localRotation, s = t.localScale };
        }

        public void ApplyTo(Transform t)
        {
            t.localPosition = p;
            t.localRotation = r;
            t.localScale = s;
        }

        public BonePose Copy()
        {
            return new BonePose { path = path, p = p, r = r, s = s };
        }
    }
}
