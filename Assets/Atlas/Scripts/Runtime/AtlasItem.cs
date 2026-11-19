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
        public string normalMap = "", emissionMap = "";   // more maps of the material
        public float bumpStrength = 1f;
        public float modelGloss = -1f;
        public string meshFile = "";
        public bool noShadow;     // shapes changed with Modelar / Esculpir (project «meshes» folder)
        public EditMesh edit;
        public Mesh editMesh;   // imported models: -1 their own shine, else 0 (matte) .. 1 (glossy)
        public readonly Dictionary<Material, Vector2> originalGloss = new Dictionary<Material, Vector2>();
        // Imported models: their materials one by one.
        public List<MatOverride> matOverrides = new List<MatOverride>();
        public List<Material> modelMats = new List<Material>();
        public readonly Dictionary<Material, MatOriginal> matOriginals = new Dictionary<Material, MatOriginal>();
        public readonly Dictionary<Renderer, Material[]> rendererMats = new Dictionary<Renderer, Material[]>();
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
        // Volumetric light (spotlight beam / lamp halo), physics and video.
        public bool volumetric;
        public float volIntensity = 1f;
        public string physics = "";      // "" none, "dynamic", "static"
        public float mass = 1f;
        public string videoFile = "";
        public RenderTexture videoRT;
        // Effects and world pieces: «fx» (particles), «water», «terrain»,
        // «flag» (cloth); their style, strength and random seed.
        public string preset = "";
        public float amount = 1f;
        public int seed = 1;
        public int fxStart;              // frame an explosion (one-shot effect) goes off
        public string text = "";         // 3D text (titles)
        // Held by a bone («agarrar»): attach / release events on the timeline.
        public List<AnchorEvent> anchors = new List<AnchorEvent>();
        public List<IkChain> ik = new List<IkChain>();   // IK rig: hands / feet controls
        public List<MorphValue> morphs = new List<MorphValue>();   // face / shape sliders set now
        public string group = "";        // objects selected together
        public bool pureEmit;            // «Emisión pura»
        public List<LookAtBone> lookAts = new List<LookAtBone>();   // «Mirar a»
        public bool pixelated;           // textures without smoothing
        public string gobo = "";         // spotlight: shape it projects (window, blinds, leaves…)
        public float flicker;            // lights: 0 steady .. 1 broken lamp
        public bool toon;                // «Estilo dibujo»: cel shading + outline
        public float outline = 1.5f, toonSoft;
        public int toonSteps = 2;
        public Color outlineColor = new Color(0.05f, 0.04f, 0.06f);
        public readonly Dictionary<Material, Material> toonMats = new Dictionary<Material, Material>();
        public FxCustom fx = new FxCustom();   // particle creator settings
        public System.Collections.Generic.List<Import.PcfFx> pcf = new System.Collections.Generic.List<Import.PcfFx>();   // SFM particles
        public bool renderCam;       // renders are made from this camera
        public string lookAtId = "", followId = "";
        public Vector3 followOffset;
        public float shake, shakeSpeed = 0.6f;
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
        public bool IsShape { get { return !IsLight && kind != "model" && kind != "camera" && kind != "video" && kind != "fx" && kind != "water" && kind != "terrain" && kind != "text"; } }
        /// Built by ATLAS (not editable with Modelar).
        public bool IsGenerated { get { return kind == "fx" || kind == "water" || kind == "terrain" || kind == "flag" || kind == "video" || kind == "text"; } }
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
            // Two bones with the same name under the same parent (Prisma3D
            // calls them all «Hueso») had the same path: the second one could
            // not be posed. It gets a number, the same on every load.
            var parents = new HashSet<Transform>();
            foreach (var b in used) if (b.parent != null) parents.Add(b.parent);
            foreach (var par in parents)
            {
                // Original names first, then numbers in child order.
                var names = new string[par.childCount];
                for (int i = 0; i < names.Length; i++) names[i] = par.GetChild(i).name;
                var taken = new HashSet<string>(names);
                var seenName = new Dictionary<string, int>();
                for (int i = 0; i < names.Length; i++)
                {
                    int n;
                    seenName.TryGetValue(names[i], out n);
                    seenName[names[i]] = ++n;
                    if (n < 2) continue;
                    var c = par.GetChild(i);
                    if (!used.Contains(c)) continue;
                    string nn = names[i] + " (" + n + ")";
                    while (taken.Contains(nn)) nn += "'";
                    taken.Add(nn);
                    c.name = nn;
                }
            }
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
            // Names shown: bones that would read the same get a number.
            labels.Clear();
            var count = new Dictionary<string, int>();
            foreach (var p in boneOrder) { string d = BoneNames.Display(BoneNames.Leaf(p)); int c; count.TryGetValue(d, out c); count[d] = c + 1; }
            var seen = new Dictionary<string, int>();
            foreach (var p in boneOrder)
            {
                string d = BoneNames.Display(BoneNames.Leaf(p));
                if (count[d] < 2) continue;
                int c; seen.TryGetValue(d, out c); seen[d] = ++c;
                labels[p] = d + " · " + c;
            }
            IkRig.Sync(this);
            morphsBuilt = false;
            ApplyMorphs();
        }

        /// IK controls behave like bones (posed, keyed, saved by path).
        public void AddExtraBone(Transform t)
        {
            string p = PathOf(t);
            if (!bones.ContainsKey(p)) boneOrder.Add(p);
            bones[p] = t;
            rest[p] = BonePose.From(p, t);
        }

        public void RemoveExtraBone(string p)
        {
            bones.Remove(p);
            boneOrder.Remove(p);
            rest.Remove(p);
            pose.RemoveAll(b => b.path == p);
            foreach (var k in keys) if (k.bones != null) k.bones.RemoveAll(b => b.path == p);
        }

        void LateUpdate()
        {
            if (ik.Count > 0) IkRig.Solve(this);
        }

        readonly Dictionary<string, string> labels = new Dictionary<string, string>();

        /// The bone's name as shown (numbered when several read the same).
        public string BoneLabel(string path)
        {
            string l;
            if (path != null && labels.TryGetValue(path, out l)) return l;
            return BoneNames.Display(BoneNames.Leaf(path ?? ""));
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
            if (ik.Count > 0) IkRig.Solve(this);
        }

        // ------------------------------------------------------------------
        // Keys. Each key can hold position, rotation and scale (or only some:
        // `skip`) and any posed bones: they behave like the separate tracks
        // of the Godot timeline.

        public const int ChPos = 1, ChRot = 2, ChScale = 4, ChUv = 8, ChGlow = 16, ChMorph = 32, ChOpacity = 64;

        /// Set by the app: shows a new opacity (keys on the timeline).
        public static System.Action<AtlasItem> OpacityChanged;

        // ------------------------------------------------------------------
        // Shape sliders (blend shapes / shape keys / flexes): smile, blink,
        // brows, mouth shapes… by name, 0..100 like Unity and SFM's sliders.

        readonly Dictionary<string, List<KeyValuePair<SkinnedMeshRenderer, int>>> morphIndex = new Dictionary<string, List<KeyValuePair<SkinnedMeshRenderer, int>>>();
        readonly List<string> morphNames = new List<string>();
        bool morphsBuilt;

        public List<string> MorphNames { get { BuildMorphs(); return morphNames; } }

        void BuildMorphs()
        {
            if (morphsBuilt) return;
            morphsBuilt = true;
            morphIndex.Clear();
            morphNames.Clear();
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = smr.sharedMesh;
                if (m == null) continue;
                for (int i = 0; i < m.blendShapeCount; i++)
                {
                    string n = m.GetBlendShapeName(i);
                    List<KeyValuePair<SkinnedMeshRenderer, int>> l;
                    if (!morphIndex.TryGetValue(n, out l)) { l = new List<KeyValuePair<SkinnedMeshRenderer, int>>(); morphIndex[n] = l; morphNames.Add(n); }
                    l.Add(new KeyValuePair<SkinnedMeshRenderer, int>(smr, i));
                }
            }
        }

        public float MorphValueOf(string name)
        {
            foreach (var m in morphs) if (m.name == name) return m.w;
            return 0f;
        }

        /// Sets a slider now (0..100) and shows it.
        public void SetMorph(string name, float w)
        {
            MorphValue mv = null;
            foreach (var m in morphs) if (m.name == name) mv = m;
            if (mv == null) { mv = new MorphValue { name = name }; morphs.Add(mv); }
            mv.w = w;
            ShowMorph(name, w);
        }

        void ShowMorph(string name, float w)
        {
            BuildMorphs();
            List<KeyValuePair<SkinnedMeshRenderer, int>> l;
            if (!morphIndex.TryGetValue(name, out l)) return;
            foreach (var kv in l) if (kv.Key != null) kv.Key.SetBlendShapeWeight(kv.Value, w * 0.01f * FullWeight(kv.Key, kv.Value));
        }

        /// The weight at which a shape is complete: 100 in most files, but
        /// some bring 1 (then 0→1 was all of it and past 1 it overshot).
        static float FullWeight(SkinnedMeshRenderer r, int index)
        {
            var m = r.sharedMesh;
            if (m == null || index < 0 || index >= m.blendShapeCount) return 100f;
            int n = m.GetBlendShapeFrameCount(index);
            if (n <= 0) return 100f;
            float fw = m.GetBlendShapeFrameWeight(index, n - 1);
            return fw > 1e-4f ? fw : 100f;
        }

        public void ApplyMorphs()
        {
            foreach (var m in morphs) ShowMorph(m.name, m.w);
        }

        public void ResetMorphs()
        {
            foreach (var m in morphs) ShowMorph(m.name, 0f);
            morphs.Clear();
        }

        // Animatable glow (light of its own) of one material of a model
        // (glowMat) or of the whole object (-1): spikes that light up, eyes,
        // screens… glowColor.a < 0.5: the material's own color.
        public float glow;
        public int glowMat = -1;
        public Color glowColor = new Color(1f, 1f, 1f, 0f);
        readonly Dictionary<Material, Color> glowBase = new Dictionary<Material, Color>();

        /// After the materials were rebuilt: their emission is the base the
        /// glow is added to.
        public void LookChanged()
        {
            glowBase.Clear();
            ApplyGlow();
        }

        public void ApplyGlow()
        {
            if (glow <= 0f && glowBase.Count == 0) return;
            var mats = new List<Material>();
            if (kind == "model") { for (int i = 0; i < modelMats.Count; i++) if (glowMat < 0 || i == glowMat) mats.Add(modelMats[i]); }
            else if (material != null) mats.Add(material);
            foreach (var m in mats)
            {
                if (m == null || !m.HasProperty("_EmissionColor")) continue;
                Color b;
                if (!glowBase.TryGetValue(m, out b)) { b = m.GetColor("_EmissionColor"); glowBase[m] = b; }
                Color c = glowColor.a >= 0.5f ? glowColor : (m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white);
                c.a = 1f;
                if (glow > 0f) m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetColor("_EmissionColor", b + c * glow);
            }
        }

        // Texture placement (repeat x, y, shift x, y), animatable: faces
        // that change expression, scrolling water, screens… On a model it is
        // for one of its materials (uvMat) or all of them (-1).
        public Vector4 uv = new Vector4(1f, 1f, 0f, 0f);
        public int uvMat = -1;

        // The tiling the file gave each material (texture transform): the
        // placement keys go on top of it instead of wiping it.
        readonly Dictionary<Material, Vector4> fileST = new Dictionary<Material, Vector4>();

        public void ApplyUv()
        {
            var sc = new Vector2(uv.x, uv.y);
            var of = new Vector2(uv.z, uv.w);
            if (kind == "model")
            {
                for (int i = 0; i < modelMats.Count; i++)
                {
                    var m = modelMats[i];
                    if (m == null || !m.HasProperty("_BaseMap")) continue;
                    Vector4 st;
                    if (!fileST.TryGetValue(m, out st))
                    {
                        var s0 = m.GetTextureScale("_BaseMap");
                        var o0 = m.GetTextureOffset("_BaseMap");
                        st = new Vector4(s0.x, s0.y, o0.x, o0.y);
                        fileST[m] = st;
                    }
                    bool mine = !(uvMat >= 0 && i != uvMat);
                    Vector2 s1 = mine ? sc : Vector2.one, o1 = mine ? of : Vector2.zero;
                    m.SetTextureScale("_BaseMap", new Vector2(st.x * s1.x, st.y * s1.y));
                    m.SetTextureOffset("_BaseMap", new Vector2(st.z * s1.x + o1.x, st.w * s1.y + o1.y));
                }
            }
            else if (material != null && material.HasProperty("_BaseMap"))
            {
                material.SetTextureScale("_BaseMap", sc);
                material.SetTextureOffset("_BaseMap", of);
            }
        }

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
            if ((channels & ChUv) != 0) { k.uv = uv; k.uvKey = true; }
            if ((channels & ChGlow) != 0) { k.glow = glow; k.glowKey = true; }
            if ((channels & ChOpacity) != 0) { k.opacity = opacity; k.opKey = true; }
            if ((channels & ChMorph) != 0)
            {
                if (k.morphs == null) k.morphs = new List<MorphValue>();
                foreach (var m in morphs)
                {
                    k.morphs.RemoveAll(x => x.name == m.name);
                    k.morphs.Add(new MorphValue { name = m.name, w = m.w });
                }
            }
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
            bool tp = false, tr = false, ts = false, tu = false, tg = false, to = false;
            var morphSeen = new List<string>();
            var bonesSeen = new List<string>();
            foreach (var k in keys)
            {
                if ((k.skip & ChPos) == 0) tp = true;
                if ((k.skip & ChRot) == 0) tr = true;
                if ((k.skip & ChScale) == 0) ts = true;
                if (k.uvKey) tu = true;
                if (k.glowKey) tg = true;
                if (k.opKey) to = true;
                if (k.morphs != null) foreach (var m in k.morphs) if (!morphSeen.Contains(m.name)) morphSeen.Add(m.name);
                if (k.bones != null)
                    foreach (var b in k.bones) if (!bonesSeen.Contains(b.path)) bonesSeen.Add(b.path);
            }
            if (tp) l.Add("T");
            if (tr) l.Add("R");
            if (ts) l.Add("S");
            if (tu) l.Add("U");
            if (tg) l.Add("G");
            if (to) l.Add("O");
            foreach (var mn in morphSeen) l.Add("m:" + mn);
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
        /// Shape sliders that have keys.
        List<string> KeyedMorphs()
        {
            var l = new List<string>();
            foreach (var k in keys)
                if (k.morphs != null)
                    foreach (var m in k.morphs) if (!l.Contains(m.name)) l.Add(m.name);
            return l;
        }

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
                if (Segment("U", frame, out a0, out a, out b, out b1))
                {
                    float t = SegT(a, b, frame);
                    Vector3 sc = Interp.Blend(a.interp, (Vector3)(Vector2)a0.uv, (Vector3)(Vector2)a.uv, (Vector3)(Vector2)b.uv, (Vector3)(Vector2)b1.uv, t);
                    Vector3 of = Interp.Blend(a.interp, new Vector3(a0.uv.z, a0.uv.w), new Vector3(a.uv.z, a.uv.w), new Vector3(b.uv.z, b.uv.w), new Vector3(b1.uv.z, b1.uv.w), t);
                    uv = new Vector4(sc.x, sc.y, of.x, of.y);
                    ApplyUv();
                }
                foreach (var mn in KeyedMorphs())
                {
                    if (!Segment("m:" + mn, frame, out a0, out a, out b, out b1)) continue;
                    float va = a.Morph(mn), vb = b.Morph(mn);
                    float w = a == b ? va : Interp.Blend(a.interp, new Vector3(a0.Morph(mn), 0f, 0f), new Vector3(va, 0f, 0f), new Vector3(vb, 0f, 0f), new Vector3(b1.Morph(mn), 0f, 0f), SegT(a, b, frame)).x;
                    SetMorph(mn, Mathf.Clamp(w, -100f, 200f));
                }
                if (Segment("G", frame, out a0, out a, out b, out b1))
                {
                    glow = Mathf.Max(0f, Interp.Blend(a.interp, new Vector3(a0.glow, 0f, 0f), new Vector3(a.glow, 0f, 0f), new Vector3(b.glow, 0f, 0f), new Vector3(b1.glow, 0f, 0f), SegT(a, b, frame)).x);
                    ApplyGlow();
                }
                if (Segment("O", frame, out a0, out a, out b, out b1))
                {
                    float op = Mathf.Clamp01(Interp.Blend(a.interp, new Vector3(a0.opacity, 0f, 0f), new Vector3(a.opacity, 0f, 0f), new Vector3(b.opacity, 0f, 0f), new Vector3(b1.opacity, 0f, 0f), SegT(a, b, frame)).x);
                    if (Mathf.Abs(op - opacity) > 1e-4f)
                    {
                        opacity = op;
                        if (OpacityChanged != null) OpacityChanged(this);
                    }
                }
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
            if (ik.Count > 0) IkRig.Solve(this);
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
            if (editMesh != null) Destroy(editMesh);
            if (importer != null)
            {
                importer.Dispose();
                importer = null;
            }
        }
    }

    /// The object is held by `bone` of model `target` from `frame` on (an
    /// empty target: let go there). `pos` / `rot`: where it sits in the bone.
    [Serializable]
    public class AnchorEvent
    {
        public int frame;
        public string target = "", bone = "";
        public Vector3 pos;
        public Quaternion rot = Quaternion.identity;
        public Vector3 scale = Vector3.one;

        public AnchorEvent Copy() { return (AnchorEvent)MemberwiseClone(); }
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
        /// Texture placement key (repeat x, y, shift x, y).
        public bool uvKey;
        public Vector4 uv = new Vector4(1f, 1f, 0f, 0f);

        /// Glow key (light of its own).
        public bool glowKey;
        public float glow;

        /// Opacity key (0 = invisible).
        public bool opKey;
        public float opacity = 1f;

        /// Shape slider values of this key.
        public List<MorphValue> morphs = new List<MorphValue>();

        public float Morph(string name)
        {
            if (morphs != null) foreach (var m in morphs) if (m.name == name) return m.w;
            return 0f;
        }

        public bool Empty { get { return skip == 7 && !uvKey && !glowKey && !opKey && (bones == null || bones.Count == 0) && (morphs == null || morphs.Count == 0); } }

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
                case "U": return uvKey;
                case "G": return glowKey;
                case "O": return opKey;
            }
            if (track.StartsWith("m:")) { string n = track.Substring(2); if (morphs != null) foreach (var m in morphs) if (m.name == n) return true; return false; }
            return track.StartsWith("b:") && GetBone(track.Substring(2)) != null;
        }

        public void ClearTrack(string track)
        {
            switch (track)
            {
                case "T": skip |= 1; return;
                case "R": skip |= 2; return;
                case "S": skip |= 4; return;
                case "U": uvKey = false; return;
                case "G": glowKey = false; return;
                case "O": opKey = false; return;
            }
            if (track.StartsWith("m:")) { if (morphs != null) morphs.RemoveAll(m => m.name == track.Substring(2)); return; }
            if (track.StartsWith("b:") && bones != null) bones.RemoveAll(b => b.path == track.Substring(2));
        }

        public static void CopyTrack(KeyData src, KeyData dst, string track)
        {
            switch (track)
            {
                case "T": dst.position = src.position; dst.skip &= ~1; return;
                case "R": dst.rotation = src.rotation; dst.skip &= ~2; return;
                case "S": dst.scale = src.scale; dst.skip &= ~4; return;
                case "U": dst.uv = src.uv; dst.uvKey = true; return;
                case "G": dst.glow = src.glow; dst.glowKey = true; return;
                case "O": dst.opacity = src.opacity; dst.opKey = true; return;
            }
            if (track.StartsWith("m:"))
            {
                string n = track.Substring(2);
                if (dst.morphs == null) dst.morphs = new List<MorphValue>();
                dst.morphs.RemoveAll(m => m.name == n);
                dst.morphs.Add(new MorphValue { name = n, w = src.Morph(n) });
                return;
            }
            if (!track.StartsWith("b:")) return;
            var v = src.GetBone(track.Substring(2));
            if (v == null) return;
            if (dst.bones == null) dst.bones = new List<BonePose>();
            dst.bones.RemoveAll(b => b.path == v.path);
            dst.bones.Add(v.Copy());
        }
    }

    /// One shape slider (blend shape) value, 0..100.
    [Serializable]
    public class MorphValue
    {
        public string name;
        public float w;
        public MorphValue Copy() { return (MorphValue)MemberwiseClone(); }
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
