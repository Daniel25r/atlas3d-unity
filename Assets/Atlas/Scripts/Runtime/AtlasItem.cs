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
        public string shader = "";   // "" lit (PBR), "aero" glossy Frutiger Aero glass
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
        // Scene camera
        public float fov = 50f;
        public bool renderCam;       // renders are made from this camera
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
        public bool IsShape { get { return !IsLight && kind != "model" && kind != "camera"; } }
        /// Lights and cameras: tools of the scene, not part of exported models.
        public bool IsHelper { get { return IsLight || kind == "camera"; } }
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
        // Keys. Each key can hold position, rotation and scale (or only some:
        // `skip`) and any posed bones: they behave like the separate tracks
        // of the Godot timeline.

        public const int ChPos = 1, ChRot = 2, ChScale = 4;

        public bool Animated { get { return keys.Count > 0; } }

        public KeyData KeyAt(int frame)
        {
            foreach (var k in keys) if (k.frame == frame) return k;
            return null;
        }

        public KeyData GetOrAddKey(int frame)
        {
            var k = KeyAt(frame);
            if (k != null) return k;
            k = new KeyData { frame = frame, skip = ChPos | ChRot | ChScale, position = transform.position, rotation = transform.rotation, scale = transform.localScale };
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].frame > frame) { keys.Insert(i, k); return k; }
            keys.Add(k);
            return k;
        }

        /// Key of the object's transform (all of it by default) and the
        /// posed bones.
        public void SetKey(int frame)
        {
            SetKey(frame, ChPos | ChRot | ChScale, null);
        }

        /// `channels`: ChPos | ChRot | ChScale (0 = none). `bonePaths`: null =
        /// every posed bone, empty = none, or the bones to store.
        public void SetKey(int frame, int channels, List<string> bonePaths)
        {
            var k = GetOrAddKey(frame);
            if ((channels & ChPos) != 0) { k.position = transform.position; k.skip &= ~ChPos; }
            if ((channels & ChRot) != 0) { k.rotation = transform.rotation; k.skip &= ~ChRot; }
            if ((channels & ChScale) != 0) { k.scale = transform.localScale; k.skip &= ~ChScale; }
            if (k.bones == null) k.bones = new List<BonePose>();
            var paths = new List<string>();
            if (bonePaths == null) foreach (var b in pose) paths.Add(b.path);
            else paths.AddRange(bonePaths);
            foreach (var path in paths)
            {
                var t = Bone(path);
                BonePose v = t != null ? BonePose.From(path, t) : (FindPose(path) != null ? FindPose(path).Copy() : null);
                if (v == null) continue;
                k.bones.RemoveAll(b => b.path == path);
                k.bones.Add(v);
            }
            if (k.Empty) keys.Remove(k);
        }

        public bool RemoveKey(int frame)
        {
            return keys.RemoveAll(k => k.frame == frame) > 0;
        }

        public bool HasKey(int frame)
        {
            return KeyAt(frame) != null;
        }

        /// Frames that have something on a track: "" any, "T" / "R" / "S" the
        /// object's position / rotation / scale, "b:<path>" a bone.
        public List<int> TrackFrames(string track)
        {
            var l = new List<int>();
            foreach (var k in keys)
                if (k.Has(track)) l.Add(k.frame);
            return l;
        }

        /// Tracks that have keys (for the expanded timeline).
        public List<string> Tracks()
        {
            var l = new List<string>();
            bool tp = false, tr = false, ts = false;
            var bonesSeen = new List<string>();
            foreach (var k in keys)
            {
                if ((k.skip & ChPos) == 0) tp = true;
                if ((k.skip & ChRot) == 0) tr = true;
                if ((k.skip & ChScale) == 0) ts = true;
                if (k.bones != null)
                    foreach (var b in k.bones) if (!bonesSeen.Contains(b.path)) bonesSeen.Add(b.path);
            }
            if (tp) l.Add("T");
            if (tr) l.Add("R");
            if (ts) l.Add("S");
            // Bones in skeleton order.
            foreach (var path in boneOrder) if (bonesSeen.Contains(path)) l.Add("b:" + path);
            foreach (var path in bonesSeen) if (!boneOrder.Contains(path)) l.Add("b:" + path);
            return l;
        }

        /// Moves one track's value from one frame to another (merging into
        /// the key there). "" moves the whole key.
        public void MoveTrackKey(string track, int from, int to)
        {
            if (from == to) return;
            var src = KeyAt(from);
            if (src == null || !src.Has(track)) return;
            if (track == "")
            {
                var other = KeyAt(to);
                if (other != null) keys.Remove(other);
                src.frame = to;
                keys.Sort((a, b) => a.frame.CompareTo(b.frame));
                return;
            }
            var dst = GetOrAddKey(to);
            KeyData.CopyTrack(src, dst, track);
            src.ClearTrack(track);
            if (src.Empty) keys.Remove(src);
        }

        public void DeleteTrackKey(string track, int frame)
        {
            var k = KeyAt(frame);
            if (k == null) return;
            if (track == "") { keys.Remove(k); return; }
            k.ClearTrack(track);
            if (k.Empty) keys.Remove(k);
        }

        public void SetInterp(int frame, string mode)
        {
            var k = KeyAt(frame);
            if (k != null) k.interp = mode;
        }

        /// The keys around `frame` that hold `track`: before (a), after (b)
        /// and their neighbours (for the cubic curve).
        bool Segment(string track, float frame, out KeyData a0, out KeyData a, out KeyData b, out KeyData b1)
        {
            a0 = a = b = b1 = null;
            int ia = -1, ib = -1;
            var list = new List<KeyData>();
            foreach (var k in keys) if (k.Has(track)) list.Add(k);
            if (list.Count == 0) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].frame <= frame) ia = i;
                if (list[i].frame >= frame && ib < 0) ib = i;
            }
            if (ia < 0) ia = ib;
            if (ib < 0) ib = ia;
            a = list[ia];
            b = list[ib];
            a0 = list[Mathf.Max(0, ia - 1)];
            b1 = list[Mathf.Min(list.Count - 1, ib + 1)];
            return true;
        }

        float SegT(KeyData a, KeyData b, float frame)
        {
            if (a == b || b.frame == a.frame) return 0f;
            return Mathf.Clamp01(Mathf.InverseLerp(a.frame, b.frame, frame));
        }

        /// Everything at `frame`: the model's own animation, the object's
        /// keys and the bones (keys, or the pose when they have none).
        public void Evaluate(float frame, float fps)
        {
            if (!string.IsNullOrEmpty(clip)) SampleClip(frame / Mathf.Max(1f, fps));
            if (keys.Count > 0)
            {
                KeyData a0, a, b, b1;
                if (Segment("T", frame, out a0, out a, out b, out b1))
                    transform.position = Interp.Blend(a.interp, a0.position, a.position, b.position, b1.position, SegT(a, b, frame));
                if (Segment("R", frame, out a0, out a, out b, out b1))
                    transform.rotation = Interp.Blend(a.interp, a.rotation, b.rotation, SegT(a, b, frame));
                if (Segment("S", frame, out a0, out a, out b, out b1))
                    transform.localScale = Interp.Blend(a.interp, a0.scale, a.scale, b.scale, b1.scale, SegT(a, b, frame));
            }
            if (bones.Count == 0) return;
            // Bones with keys follow them; the other posed bones keep the pose.
            var done = new HashSet<string>();
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
            foreach (var b in pose)
            {
                if (done.Contains(b.path)) continue;
                var t = Bone(b.path);
                if (t != null) b.ApplyTo(t);
            }
        }

        BonePose BoneAt(string path, float frame)
        {
            KeyData a0, a, b, b1;
            string track = "b:" + path;
            if (!Segment(track, frame, out a0, out a, out b, out b1)) return null;
            var va = a.GetBone(path);
            var vb = b.GetBone(path);
            if (a == b || va == null || vb == null) return va ?? vb;
            var v0 = a0.GetBone(path) ?? va;
            var v3 = b1.GetBone(path) ?? vb;
            float t = SegT(a, b, frame);
            return new BonePose
            {
                path = path,
                p = Interp.Blend(a.interp, v0.p, va.p, vb.p, v3.p, t),
                r = Interp.Blend(a.interp, va.r, vb.r, t),
                s = Interp.Blend(a.interp, v0.s, va.s, vb.s, v3.s, t),
            };
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
        /// Channels this key does NOT hold (AtlasItem.ChPos / ChRot / ChScale);
        /// 0 = the whole transform (also what older saves mean).
        public int skip;
        /// Curve from this key to the next ("" = smooth ease in-out).
        public string interp = "";

        public bool Empty { get { return skip == 7 && (bones == null || bones.Count == 0); } }

        public BonePose GetBone(string path)
        {
            if (bones != null) foreach (var b in bones) if (b.path == path) return b;
            return null;
        }

        public bool Has(string track)
        {
            switch (track)
            {
                case "": return true;
                case "T": return (skip & 1) == 0;
                case "R": return (skip & 2) == 0;
                case "S": return (skip & 4) == 0;
            }
            return track.StartsWith("b:") && GetBone(track.Substring(2)) != null;
        }

        public void ClearTrack(string track)
        {
            switch (track)
            {
                case "T": skip |= 1; return;
                case "R": skip |= 2; return;
                case "S": skip |= 4; return;
            }
            if (track.StartsWith("b:") && bones != null) bones.RemoveAll(b => b.path == track.Substring(2));
        }

        public static void CopyTrack(KeyData src, KeyData dst, string track)
        {
            switch (track)
            {
                case "T": dst.position = src.position; dst.skip &= ~1; return;
                case "R": dst.rotation = src.rotation; dst.skip &= ~2; return;
                case "S": dst.scale = src.scale; dst.skip &= ~4; return;
            }
            if (!track.StartsWith("b:")) return;
            var v = src.GetBone(track.Substring(2));
            if (v == null) return;
            if (dst.bones == null) dst.bones = new List<BonePose>();
            dst.bones.RemoveAll(b => b.path == v.path);
            dst.bones.Add(v.Copy());
        }
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
