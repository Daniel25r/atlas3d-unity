// ATLAS 3D (Unity) - four tools asked for by the community:
//  · Volumetric light: a visible beam for spotlights and a halo for lamps.
//  · Video planes: an MP4 on a plane, in step with the timeline (also in
//    renders, frame by frame).
//  · IK: in pose mode, drag the tip of a leg, arm or tail with «Mover» and
//    the chain bends to follow it (2, 3 or 4 bones: bipeds and quadrupeds).
//  · Physics: objects fall, collide and bounce, with gravity in any
//    direction; the result becomes keys on the timeline («hornear»), so it
//    can be edited, played back and rendered exactly.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace Atlas
{
    public partial class AtlasApp
    {
        // ------------------------------------------------------------------
        // Volumetric light

        Mesh beamMesh;
        Material volumeTemplate;

        /// Open cone along +Z (the spotlight's direction), 1 long and 1 wide
        /// at the end; uv.y = how far along it is.
        Mesh BeamMesh()
        {
            if (beamMesh != null) return beamMesh;
            const int seg = 32, rings = 8;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float z = r / (float)rings;
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    v.Add(dir * z + new Vector3(0f, 0f, z));
                    n.Add(new Vector3(dir.x, dir.y, -1f).normalized);
                    uv.Add(new Vector2(s / (float)seg, z));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    t.Add(a); t.Add(b); t.Add(a + 1);
                    t.Add(a + 1); t.Add(b); t.Add(b + 1);
                }
            beamMesh = new Mesh { name = "Haz volumétrico" };
            beamMesh.SetVertices(v);
            beamMesh.SetNormals(n);
            beamMesh.SetUVs(0, uv);
            beamMesh.SetTriangles(t, 0);
            beamMesh.RecalculateBounds();
            return beamMesh;
        }

        void UpdateVolume(AtlasItem it, Light l, Color col)
        {
            var vol = it.transform.Find("Volumen");
            bool want = it.volumetric && it.visible && l != null && (l.type == LightType.Spot || l.type == LightType.Point);
            if (!want)
            {
                if (vol != null) Destroy(vol.gameObject);
                return;
            }
            if (volumeTemplate == null) volumeTemplate = Resources.Load<Material>("AtlasVolume");
            if (volumeTemplate == null) return;
            if (vol == null)
            {
                var go = new GameObject("Volumen");
                go.layer = Gizmo.Layer;   // light, not an object: never picked or outlined
                go.transform.SetParent(it.transform, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.sharedMaterial = new Material(volumeTemplate);
                vol = go.transform;
            }
            var mf = vol.GetComponent<MeshFilter>();
            var mat = vol.GetComponent<MeshRenderer>().sharedMaterial;
            // Undo the lamp's own scale: the light's reach is in metres.
            Vector3 ls = it.transform.lossyScale;
            var inv = new Vector3(1f / Mathf.Max(0.0001f, ls.x), 1f / Mathf.Max(0.0001f, ls.y), 1f / Mathf.Max(0.0001f, ls.z));
            float range = Mathf.Max(0.1f, l.range);
            if (l.type == LightType.Spot)
            {
                mf.sharedMesh = BeamMesh();
                float w = Mathf.Tan(Mathf.Clamp(l.spotAngle, 1f, 170f) * 0.5f * Mathf.Deg2Rad) * range;
                vol.localScale = Vector3.Scale(new Vector3(w, w, range), inv);
                vol.localPosition = Vector3.zero;
                vol.localRotation = l.transform.localRotation;
                mat.SetFloat("_Mode", 0f);
            }
            else
            {
                if (mf.sharedMesh == null || mf.sharedMesh.name != "Halo") mf.sharedMesh = HaloMesh();
                vol.localScale = Vector3.Scale(Vector3.one * range * 0.6f, inv);
                vol.localPosition = Vector3.zero;
                vol.localRotation = Quaternion.identity;
                mat.SetFloat("_Mode", 1f);
            }
            mat.SetColor("_BaseColor", col);
            mat.SetFloat("_Intensity", Mathf.Clamp(it.volIntensity * Mathf.Sqrt(Mathf.Max(0f, it.lightIntensity)) * 0.6f, 0f, 8f));
        }

        Mesh haloMesh;

        Mesh HaloMesh()
        {
            if (haloMesh != null) return haloMesh;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            haloMesh = Instantiate(tmp.GetComponent<MeshFilter>().sharedMesh);
            haloMesh.name = "Halo";
            Destroy(tmp);
            return haloMesh;
        }

        // ------------------------------------------------------------------
        // Video planes

        string VideosDir { get { return store != null ? Path.Combine(store.projectDir, "videos") : ""; } }

        static readonly string[] VideoExts = { ".mp4", ".m4v", ".mov", ".webm", ".3gp", ".mkv", ".avi", ".wmv", ".mpg", ".mpeg", ".ogv" };

        public static bool IsVideoFile(string path)
        {
            return System.Array.IndexOf(VideoExts, Path.GetExtension(path ?? "").ToLowerInvariant()) >= 0;
        }

        /// A file name the video player always reads: letters, digits, «_»
        /// (spaces, accents and emojis made it fail on Android).
        static string SafeVideoName(string name, string ext)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in name ?? "")
                sb.Append((ch < 128 && char.IsLetterOrDigit(ch)) || ch == '-' ? ch : '_');
            string s = sb.ToString().Trim('_');
            if (s.Length > 40) s = s.Substring(0, 40);
            if (s == "") s = "video";
            return s + "_" + SceneStore.NewId().Substring(0, 6) + ext;
        }

        void AddVideoPlane()
        {
            CloseMenus();
            if (store == null || NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path) || store == null) return;
                AddVideoFromPath(path);
            }, new string[] { "video/*" });
        }

        /// Puts the video file in the project and on a plane in the scene.
        void AddVideoFromPath(string path)
        {
            if (store == null || string.IsNullOrEmpty(path)) return;
            {
                string file;
                try
                {
                    Directory.CreateDirectory(VideosDir);
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == "" || ext.Length > 5) ext = ".mp4";
                    file = SafeVideoName(Path.GetFileNameWithoutExtension(path), ext);
                    File.Copy(path, Path.Combine(VideosDir, file), true);
                }
                catch (System.Exception e) { Toast("No se pudo usar el vídeo: " + e.Message, true); return; }
                count++;
                string nm = Path.GetFileNameWithoutExtension(path);
                var d = new ItemData
                {
                    v = 3,
                    kind = "video",
                    name = string.IsNullOrEmpty(nm) ? "Vídeo" : nm,
                    position = new Vector3(target.position.x, 1.2f, target.position.z),
                    scale = new Vector3(3.2f, 1.8f, 1f),
                    color = Color.white,
                    videoFile = file,
                };
                Select(Create(d));
                Commit(true);
                Toast("Vídeo colocado: sigue a la línea de tiempo y sale en el render");
            }
        }

        void SetupVideo(AtlasItem it)
        {
            if (string.IsNullOrEmpty(it.videoFile) || store == null) return;
            string full = Path.Combine(VideosDir, it.videoFile);
            if (!File.Exists(full)) { Toast("Falta el vídeo «" + it.videoFile + "»", true); return; }
            // Older projects: a name with spaces / accents is renamed once.
            string ext0 = Path.GetExtension(it.videoFile);
            string bare = Path.GetFileNameWithoutExtension(it.videoFile);
            bool unsafeName = false;
            foreach (char ch in bare) if (!((ch < 128 && char.IsLetterOrDigit(ch)) || ch == '_' || ch == '-')) unsafeName = true;
            if (unsafeName)
            {
                try
                {
                    string nn = SafeVideoName(bare, ext0 == "" ? ".mp4" : ext0.ToLowerInvariant());
                    File.Copy(full, Path.Combine(VideosDir, nn), true);
                    it.videoFile = nn;
                    full = Path.Combine(VideosDir, nn);
                }
                catch (System.Exception) { }
            }
            var vp = it.gameObject.GetComponent<VideoPlayer>();
            bool fresh = vp == null;
            if (vp == null) vp = it.gameObject.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.isLooping = true;
            vp.skipOnDrop = true;
            vp.waitForFirstFrame = true;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.source = VideoSource.Url;
            // The plain path: an escaped «file://» address failed on some phones.
            vp.url = full;
            if (fresh)
            {
                string shown = it.gameObject.name;
                vp.errorReceived += (p, msg) =>
                {
                    Debug.LogWarning("ATLAS: vídeo: " + msg);
                    Toast("El vídeo «" + shown + "» no se puede reproducir en este dispositivo (formato o códec no compatible, p. ej. HEVC/H.265). Conviértelo a MP4 (H.264) con CapCut, InShot o cualquier editor y vuelve a ponerlo", true);
                };
            }
            if (it.videoRT == null) it.videoRT = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = it.videoRT;
            if (!fresh) { vp.Prepare(); return; }
            vp.prepareCompleted += p =>
            {
                // The plane takes the video's shape (keeping its height).
                if (p.width > 0 && p.height > 0 && it != null && Mathf.Abs(it.transform.localScale.x / Mathf.Max(0.001f, it.transform.localScale.y) - 16f / 9f) < 0.01f)
                {
                    var s = it.transform.localScale;
                    it.transform.localScale = new Vector3(s.y * p.width / p.height, s.y, s.z);
                }
                SyncVideo(it, true);
            };
            vp.Prepare();
        }

        /// The video at the timeline's time (from the frame it starts on: 0).
        void SyncVideo(AtlasItem it, bool force)
        {
            var vp = it.GetComponent<VideoPlayer>();
            if (vp == null || !vp.isPrepared) return;
            double len = vp.length > 0 ? vp.length : 1;
            double t = frame / Mathf.Max(1f, fps);
            t = vp.isLooping ? t % len : System.Math.Min(t, len);
            if (playing && !rendering)
            {
                if (!vp.isPlaying) { vp.time = t; vp.Play(); }
                else if (System.Math.Abs(vp.time - t) > 0.25) vp.time = t;
            }
            else
            {
                if (vp.isPlaying) vp.Pause();
                if (force || System.Math.Abs(vp.time - t) > 0.5 / Mathf.Max(1f, fps)) vp.time = t;
            }
        }

        void SyncVideos()
        {
            foreach (var it in items) if (it != null && it.kind == "video") SyncVideo(it, false);
        }

        /// Renders wait until every video shows the right frame.
        IEnumerator WaitVideos()
        {
            var list = new List<VideoPlayer>();
            foreach (var it in items)
                if (it != null && it.kind == "video")
                {
                    var vp = it.GetComponent<VideoPlayer>();
                    if (vp != null && vp.isPrepared) { SyncVideo(it, true); list.Add(vp); }
                }
            if (list.Count == 0) yield break;
            float until = Time.realtimeSinceStartup + 1.5f;
            foreach (var vp in list)
            {
                vp.Play();
                vp.Pause();
                long want = (long)(vp.time * vp.frameRate);
                while (Time.realtimeSinceStartup < until && System.Math.Abs(vp.frame - want) > 1) yield return null;
            }
            yield return null;
        }

        // ------------------------------------------------------------------
        // IK (pose mode)

        bool ikOn;
        int ikChain = 2;
        readonly List<string> ikBones = new List<string>();
        Vector3 ikStartLocal;
        string ikTip = "";

        /// Called when a drag of the «Mover» manipulator starts on a bone.
        void IkBegin()
        {
            ikBones.Clear();
            ikTip = "";
            if (!ikOn || !poseMode || selected == null || activeBone == "" || gizmo.tool != Tool.Move) return;
            var t = selected.Bone(activeBone);
            if (t == null) return;
            ikTip = activeBone;
            ikStartLocal = t.localPosition;
            string p = activeBone;
            for (int i = 0; i < ikChain; i++)
            {
                p = selected.ParentBone(p);
                if (string.IsNullOrEmpty(p) || selected.Bone(p) == null) break;
                ikBones.Add(p);
            }
        }

        /// The tip stays where it was in its bone; the chain turns so it
        /// reaches the point the finger moved it to (CCD).
        void IkSolve()
        {
            if (ikTip == "" || ikBones.Count == 0 || selected == null) return;
            var tip = selected.Bone(ikTip);
            if (tip == null) return;
            Vector3 goal = tip.position;
            tip.localPosition = ikStartLocal;
            for (int iter = 0; iter < 12; iter++)
            {
                foreach (var path in ikBones)
                {
                    var j = selected.Bone(path);
                    if (j == null) continue;
                    Vector3 toTip = tip.position - j.position, toGoal = goal - j.position;
                    if (toTip.sqrMagnitude < 1e-8f || toGoal.sqrMagnitude < 1e-8f) continue;
                    var q = Quaternion.FromToRotation(toTip, toGoal);
                    // Gentle steps keep knees and elbows natural.
                    j.rotation = Quaternion.Slerp(Quaternion.identity, q, 0.6f) * j.rotation;
                }
                if ((tip.position - goal).sqrMagnitude < 1e-6f) break;
            }
            foreach (var path in ikBones) selected.RecordBone(path);
        }

        /// The bones the last IK drag moved (for the keys).
        List<string> IkKeyBones()
        {
            var l = new List<string>(ikBones);
            if (ikTip != "") l.Add(ikTip);
            return l;
        }

        // ------------------------------------------------------------------
        // Physics

        /// Plays the physics from the current frame to the end and stores
        /// the falling objects' position and rotation as keys.
        IEnumerator BakePhysics()
        {
            CloseMenus();
            var dyn = new List<AtlasItem>();
            var stat = new List<AtlasItem>();
            foreach (var it in items)
            {
                if (it == null || !it.visible) continue;
                if (it.physics == "dynamic") dyn.Add(it);
                else if (it.physics == "static") stat.Add(it);
            }
            if (dyn.Count == 0) { Toast("Ningún objeto cae: en Propiedades → Física elige «Cae y choca»", true); yield break; }
            if (playing) TogglePlay();
            rendering = true;
            Toast("Simulando la física…");
            yield return null;
            var keepGravity = Physics.gravity;
            Vector3 dir = env.gravityDir.sqrMagnitude > 1e-6f ? env.gravityDir.normalized : Vector3.down;
            Physics.gravity = dir * env.gravity;
            Scene sim = default(Scene);
            var proxies = new Dictionary<AtlasItem, Rigidbody>();
            proxyOffset.Clear();
            bool ok = true;
            try
            {
                sim = SceneManager.CreateScene("Fisica_" + SceneStore.NewId().Substring(0, 6), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
#if UNITY_6000_0_OR_NEWER
                var pm = new PhysicsMaterial { bounciness = Mathf.Clamp01(env.bounce), dynamicFriction = 0.5f, staticFriction = 0.6f, bounceCombine = PhysicsMaterialCombine.Maximum };
#else
                var pm = new PhysicMaterial { bounciness = Mathf.Clamp01(env.bounce), dynamicFriction = 0.5f, staticFriction = 0.6f, bounceCombine = PhysicMaterialCombine.Maximum };
#endif
                if (ground.activeSelf)
                {
                    var g = new GameObject("Suelo");
                    SceneManager.MoveGameObjectToScene(g, sim);
                    var bc = g.AddComponent<BoxCollider>();
                    bc.size = new Vector3(2000f, 1f, 2000f);
                    bc.center = new Vector3(0f, -0.5f, 0f);
                    bc.sharedMaterial = pm;
                }
                foreach (var it in stat) Proxy(it, sim, pm, false);
                foreach (var it in dyn) proxies[it] = Proxy(it, sim, pm, true);
            }
            catch (System.Exception e)
            {
                ok = false;
                Toast("No se pudo preparar la física: " + e.Message, true);
            }
            if (ok)
            {
                var ps = sim.GetPhysicsScene();
                int start = Mathf.RoundToInt(frame);
                float dt = 1f / Mathf.Max(1, fps);
                for (int f = start; f <= endFrame; f++)
                {
                    if (f > start) for (int s = 0; s < 4; s++) ps.Simulate(dt / 4f);
                    foreach (var kv in proxies)
                    {
                        var it = kv.Key;
                        var rb = kv.Value;
                        it.transform.SetPositionAndRotation(rb.position - rb.rotation * proxyOffset[rb], rb.rotation);
                        it.SetKey(f, AtlasItem.ChPos | AtlasItem.ChRot, new List<string>());
                    }
                    if ((f - start) % 24 == 23) { Toast("Física: " + (f - start + 1) + " / " + (endFrame - start + 1)); yield return null; }
                }
            }
            if (sim.IsValid()) SceneManager.UnloadSceneAsync(sim);
            Physics.gravity = keepGravity;
            rendering = false;
            if (ok)
            {
                SetFrame(frame);
                Commit(true);
                RefreshKeys();
                Toast("Física lista: " + dyn.Count + " objeto(s) con claves hasta el final (Deshacer la quita)");
            }
        }

        readonly Dictionary<Rigidbody, Vector3> proxyOffset = new Dictionary<Rigidbody, Vector3>();

        /// A stand-in for the object in the physics scene: a box, ball or
        /// capsule like its shape (models: their box).
#if UNITY_6000_0_OR_NEWER
        Rigidbody Proxy(AtlasItem it, Scene sim, PhysicsMaterial pm, bool dynamic)
#else
        Rigidbody Proxy(AtlasItem it, Scene sim, PhysicMaterial pm, bool dynamic)
#endif
        {
            var go = new GameObject(it.gameObject.name);
            SceneManager.MoveGameObjectToScene(go, sim);
            // Box of the object in its own space (without its scale).
            Bounds local = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            var inv = it.transform.worldToLocalMatrix;
            foreach (var r in it.GetComponentsInChildren<Renderer>())
            {
                if (r is LineRenderer || r.gameObject.name == "Volumen" || r.gameObject.layer == Gizmo.Layer) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = inv.MultiplyPoint3x4(c);
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; } else local.Encapsulate(p);
                }
            }
            if (!any) local = new Bounds(Vector3.zero, Vector3.one);
            Vector3 ls = it.transform.lossyScale;
            var size = Vector3.Scale(local.size, ls);
            var center = Vector3.Scale(local.center, ls);
            go.transform.SetPositionAndRotation(it.transform.position + it.transform.rotation * center, it.transform.rotation);
            Collider col;
            if (it.kind == "sphere") { var sc = go.AddComponent<SphereCollider>(); sc.radius = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 0.5f; col = sc; }
            else if (it.kind == "capsule" || it.kind == "cylinder")
            {
                var cc = go.AddComponent<CapsuleCollider>();
                cc.height = size.y;
                cc.radius = Mathf.Max(size.x, size.z) * 0.5f;
                col = cc;
            }
            else { var bc = go.AddComponent<BoxCollider>(); bc.size = Vector3.Max(size, Vector3.one * 0.01f); col = bc; }
            col.sharedMaterial = pm;
            if (!dynamic) return null;
            var rb = go.AddComponent<Rigidbody>();
            proxyOffset[rb] = center;
            rb.mass = Mathf.Max(0.01f, it.mass);
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            return rb;
        }

        static readonly string[] GravityNames = { "Abajo", "Arriba", "Izquierda", "Derecha", "Adelante", "Atrás", "Sin gravedad" };
        static readonly Vector3[] GravityDirs = { Vector3.down, Vector3.up, Vector3.left, Vector3.right, Vector3.forward, Vector3.back, Vector3.zero };

        void BuildPhysicsSceneSection()
        {
            var box = Sec("Física y gravedad", "sparkle", false);
            Hint(box, "Elige en cada objeto «Cae y choca» o «Fijo (choca)» (Propiedades → Física). Luego «Simular» guarda la caída como claves desde el fotograma actual.");
            var g = AtlasTheme.Grid(box, 3, InnerWidth, 36f);
            Vector3 cur = env.gravityDir;
            for (int i = 0; i < GravityNames.Length; i++)
            {
                int k = i;
                bool on = i == 6 ? env.gravity <= 0.001f : env.gravity > 0.001f && Vector3.Distance(cur.normalized, GravityDirs[i]) < 0.01f;
                AtlasTheme.Chip(g, GravityNames[i], () =>
                {
                    if (k == 6) env.gravity = 0f;
                    else { env.gravityDir = GravityDirs[k]; if (env.gravity <= 0.001f) env.gravity = 9.81f; }
                    Commit(true);
                    RefreshProps();
                }, on);
            }
            AtlasTheme.SliderRow(box, "Fuerza de la gravedad", 0f, 30f, env.gravity, "0.0", x => env.gravity = x, () => Commit(true));
            AtlasTheme.SliderRow(box, "Rebote", 0f, 1f, env.bounce, "0.00", x => env.bounce = x, () => Commit(true));
            AtlasTheme.TextButton(box, "play", "Simular física → claves", () => StartCoroutine(BakePhysics()), true, 46f, 16);
        }

        void BuildPhysicsItemSection(AtlasItem it)
        {
            var box = Sec("Física", "sparkle", false);
            var g = AtlasTheme.Grid(box, 3, InnerWidth, 38f);
            AtlasTheme.Chip(g, "Sin física", () => { it.physics = ""; Commit(true); RefreshProps(); }, it.physics == "");
            AtlasTheme.Chip(g, "Cae y choca", () => { it.physics = "dynamic"; Commit(true); RefreshProps(); }, it.physics == "dynamic");
            AtlasTheme.Chip(g, "Fijo (choca)", () => { it.physics = "static"; Commit(true); RefreshProps(); }, it.physics == "static");
            if (it.physics == "dynamic")
                AtlasTheme.SliderRow(box, "Masa (kg)", 0.1f, 100f, it.mass, "0.0", x => it.mass = x, () => Commit(true));
            Hint(box, "La gravedad (dirección y fuerza) está en la escena: toca el fondo y busca «Física y gravedad».");
        }
    }
}
