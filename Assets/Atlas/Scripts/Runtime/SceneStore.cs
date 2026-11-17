// ATLAS 3D (Unity) - the scene as data: every object the user adds has an
// AtlasItem; the whole scene can be written to JSON and rebuilt from it.
// That one mechanism gives undo / redo (a list of snapshots) and saving
// the project (scene.json + the imported model files next to it).
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Atlas
{
    [Serializable]
    public class ItemData
    {
        public string id;
        public string kind;
        public string name;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale = Vector3.one;
        public Color color = Color.white;
        public string modelFile;
        public List<KeyData> keys = new List<KeyData>();
        // Since phase 3 (v = 3); older saves get the defaults.
        public int v;
        public float metallic;
        public float smoothness = 0.45f;
        public float emission;
        public float opacity = 1f;
        public string texture = "";
        public float lightIntensity = 3f;
        public float lightRange = 10f;
        public float spotAngle = 45f;
        public bool shadows = true;
        public bool visible = true;
        public bool locked;
        public string clip = "";
        public List<BonePose> pose = new List<BonePose>();
        public float fov = 50f;
        public bool renderCam;
        public string shader = "";
        public float modelGloss = -1f;
        public string meshFile = "";
        public bool noShadow;      // this object casts no shadow
        public List<MatOverride> mats = new List<MatOverride>();
        public Vector4 uv = new Vector4(1f, 1f, 0f, 0f);
        public int uvMat = -1;
        public float glow;
        public int glowMat = -1;
        public Color glowColor = new Color(1f, 1f, 1f, 0f);
        public bool volumetric;
        public float volIntensity = 1f;
        public string physics = "";
        public float mass = 1f;
        public string videoFile = "";
        public string preset = "";
        public float amount = 1f;
        public int seed = 1;
        public int fxStart;
        public string text = "";
        public List<AnchorEvent> anchors = new List<AnchorEvent>();
        public List<IkChain> ik = new List<IkChain>();
        public List<MorphValue> morphs = new List<MorphValue>();
        public string gobo = "";
        public float flicker;
        public bool toon;
        public float outline = 1.5f, toonSoft;
        public int toonSteps = 2;
        public Color outlineColor = new Color(0.05f, 0.04f, 0.06f);
        public FxCustom fx = new FxCustom();
        public List<Import.PcfFx> pcf = new List<Import.PcfFx>();   // SFM particles (.pcf)
        public string normalMap = "", emissionMap = "";
        public float bumpStrength = 1f;
        public string lookAtId = "", followId = "";
        public Vector3 followOffset;
        public float shake, shakeSpeed = 0.6f;

        public void FixOld()
        {
            if (pose == null) pose = new List<BonePose>();
            if (keys == null) keys = new List<KeyData>();
            foreach (var k in keys) if (k.bones == null) k.bones = new List<BonePose>();
            if (v >= 3) return;
            v = 3;
            metallic = 0f;
            smoothness = 0.45f;
            emission = 0f;
            opacity = 1f;
            texture = "";
            lightIntensity = 3f;
            lightRange = 10f;
            spotAngle = 45f;
            shadows = true;
            visible = true;
            locked = false;
            clip = "";
        }
    }

    /// The scene's look: one of the «Ambiente rápido» presets plus the
    /// image effects the user moved after picking it.
    [Serializable]
    public class EnvData
    {
        public bool set;
        public string preset = "studio";
        public float exposure;
        public float contrast;
        public float saturation;
        public float bloom;
        public float vignette;
        public float grain;
        public float fog;
        public bool shadowsOff;   // «Sombras» off for the whole scene (faster on slow phones)
        // Physics: where gravity pulls (unit direction) and how hard; bounce.
        public Vector3 gravityDir = new Vector3(0f, -1f, 0f);
        public float gravity = 9.81f;
        public float bounce = 0.2f;
        // Animated weather: "", rain, storm, snow, fog.
        public string weather = "";
        // Film camera: depth of field (focus in metres, blur 0..1), motion
        // blur and chromatic aberration.
        public bool dof;
        public float focus = 5f, dofBlur = 0.5f, motionBlur, chroma;
        // Ambient occlusion (soft shadows in corners) and camera cuts.
        public bool ao;
        public List<CamCut> cuts = new List<CamCut>();
        // Sky with the hour of the day (-1: the ambience's own) and clouds.
        public float timeOfDay = -1f, cloudCover = -1f;
    }

    [Serializable]
    public class SceneData
    {
        public int version = 1;
        public List<ItemData> items = new List<ItemData>();
        public Vector3 cameraTarget = new Vector3(0f, 0.5f, 0f);
        public float cameraYaw = 30f;
        public float cameraPitch = 20f;
        public float cameraDistance = 7f;
        public int fps = 24;
        public int endFrame = 120;
        public int startFrame;
        public string audioFile = "";
        public int audioStart;
        public float audioVolume = 1f;
        public bool showGrid = true;
        public EnvData env = new EnvData();
    }

    public class ProjectInfo
    {
        public string name;
        public string dir;
        public DateTime modified;
        public string thumbnail;   // "" when there is none yet
    }

    public class SceneStore
    {
        public readonly string projectDir;
        public readonly string name;
        public string ModelsDir { get { return Path.Combine(projectDir, "models"); } }
        public string TexturesDir { get { return Path.Combine(projectDir, "textures"); } }
        public string MeshesDir { get { return Path.Combine(projectDir, "meshes"); } }
        public string ThumbPath { get { return Path.Combine(projectDir, "thumb.png"); } }
        string ScenePath { get { return Path.Combine(projectDir, "scene.json"); } }

        readonly List<string> history = new List<string>();
        int index = -1;
        const int MaxHistory = 60;

        public SceneStore(string name)
        {
            this.name = name;
            projectDir = Path.Combine(Root, name);
            Directory.CreateDirectory(ModelsDir);
        }

        // ------------------------------------------------------------------
        // The projects folder (home screen)

        public static string Root
        {
            get
            {
                string d = Path.Combine(Application.persistentDataPath, "Proyectos");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        /// Every project, the most recently changed first.
        public static List<ProjectInfo> ListProjects()
        {
            var list = new List<ProjectInfo>();
            foreach (string dir in Directory.GetDirectories(Root))
            {
                string scene = Path.Combine(dir, "scene.json");
                string thumb = Path.Combine(dir, "thumb.png");
                list.Add(new ProjectInfo
                {
                    name = Path.GetFileName(dir),
                    dir = dir,
                    modified = File.Exists(scene) ? File.GetLastWriteTime(scene) : Directory.GetLastWriteTime(dir),
                    thumbnail = File.Exists(thumb) ? thumb : "",
                });
            }
            list.Sort((a, b) => b.modified.CompareTo(a.modified));
            return list;
        }

        public static bool Exists(string name)
        {
            return Directory.Exists(Path.Combine(Root, name));
        }

        /// A name not used yet: «Proyecto 1», «Proyecto 2»…
        public static string UniqueName(string baseName)
        {
            baseName = CleanName(baseName);
            if (!Exists(baseName) && !baseName.StartsWith("Proyecto")) return baseName;
            for (int i = 1; i < 10000; i++)
            {
                string n = baseName + " " + i;
                if (!Exists(n)) return n;
            }
            return baseName + " " + NewId().Substring(0, 4);
        }

        public static string CleanName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Proyecto";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, ' ');
            s = s.Trim().Trim('.');
            if (s.Length > 40) s = s.Substring(0, 40).Trim();
            return s.Length == 0 ? "Proyecto" : s;
        }

        public static void Delete(string name)
        {
            string d = Path.Combine(Root, name);
            if (Directory.Exists(d)) Directory.Delete(d, true);
        }

        /// Returns the new name (or throws if it is taken).
        public static string Rename(string name, string newName)
        {
            newName = CleanName(newName);
            if (newName == name) return name;
            if (Exists(newName)) throw new Exception("ya existe un proyecto con ese nombre");
            Directory.Move(Path.Combine(Root, name), Path.Combine(Root, newName));
            return newName;
        }

        public static string Duplicate(string name)
        {
            string copy = name + " (copia)";
            int i = 2;
            while (Exists(copy)) copy = name + " (copia " + i++ + ")";
            CopyDir(Path.Combine(Root, name), Path.Combine(Root, copy));
            return copy;
        }

        static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from))
                File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(from))
                CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
        }

        public bool HasSaved { get { return File.Exists(ScenePath); } }

        public static string NewId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        // ------------------------------------------------------------------
        // Snapshots

        public static SceneData Capture(IEnumerable<AtlasItem> items)
        {
            var d = new SceneData();
            foreach (var it in items)
            {
                if (it == null) continue;
                var t = it.transform;
                d.items.Add(new ItemData
                {
                    id = it.id,
                    kind = it.kind,
                    name = it.gameObject.name,
                    position = t.position,
                    rotation = t.eulerAngles,
                    scale = t.localScale,
                    color = it.color,
                    modelFile = it.modelFile,
                    keys = new List<KeyData>(it.keys),
                    v = 3,
                    metallic = it.metallic,
                    smoothness = it.smoothness,
                    emission = it.emission,
                    opacity = it.opacity,
                    texture = it.texture ?? "",
                    lightIntensity = it.lightIntensity,
                    lightRange = it.lightRange,
                    spotAngle = it.spotAngle,
                    shadows = it.shadows,
                    visible = it.visible,
                    locked = it.locked,
                    clip = it.clip ?? "",
                    pose = CopyPose(it.pose),
                    fov = it.fov,
                    shader = it.shader ?? "",
                    modelGloss = it.modelGloss,
                    meshFile = it.meshFile ?? "",
                    noShadow = it.noShadow,
                    mats = CopyMats(it.matOverrides),
                    uv = it.uv,
                    uvMat = it.uvMat,
                    glow = it.glow,
                    glowMat = it.glowMat,
                    glowColor = it.glowColor,
                    volumetric = it.volumetric,
                    volIntensity = it.volIntensity,
                    physics = it.physics,
                    mass = it.mass,
                    videoFile = it.videoFile,
                    preset = it.preset,
                    amount = it.amount,
                    seed = it.seed,
                    fxStart = it.fxStart,
                    text = it.text,
                    anchors = CopyAnchors(it.anchors),
                    ik = IkRig.Copy(it.ik),
                    morphs = CopyMorphs(it.morphs),
                    gobo = it.gobo ?? "",
                    flicker = it.flicker,
                    toon = it.toon,
                    outline = it.outline,
                    toonSoft = it.toonSoft,
                    toonSteps = it.toonSteps,
                    outlineColor = it.outlineColor,
                    fx = it.fx != null ? it.fx.Copy() : new FxCustom(),
                    pcf = Import.PcfImport.CopyList(it.pcf),
                    normalMap = it.normalMap ?? "",
                    emissionMap = it.emissionMap ?? "",
                    bumpStrength = it.bumpStrength,
                    lookAtId = it.lookAtId ?? "",
                    followId = it.followId ?? "",
                    followOffset = it.followOffset,
                    shake = it.shake,
                    shakeSpeed = it.shakeSpeed,
                    renderCam = it.renderCam,
                });
            }
            return d;
        }

        static List<AnchorEvent> CopyAnchors(List<AnchorEvent> src)
        {
            var l = new List<AnchorEvent>();
            if (src != null) foreach (var a in src) l.Add(a.Copy());
            return l;
        }

        static List<MatOverride> CopyMats(List<MatOverride> src)
        {
            var l = new List<MatOverride>();
            if (src != null) foreach (var m in src) l.Add(m.Copy());
            return l;
        }

        public static List<MorphValue> CopyMorphs(List<MorphValue> l)
        {
            var r = new List<MorphValue>();
            if (l != null) foreach (var m in l) if (m != null) r.Add(m.Copy());
            return r;
        }

        static List<BonePose> CopyPose(List<BonePose> src)
        {
            var l = new List<BonePose>();
            if (src != null) foreach (var b in src) l.Add(b.Copy());
            return l;
        }

        /// Records the state after a change (clears what was undone).
        public void Push(SceneData d)
        {
            string json = JsonUtility.ToJson(d);
            if (index >= 0 && index < history.Count && history[index] == json) return;
            if (index < history.Count - 1)
                history.RemoveRange(index + 1, history.Count - index - 1);
            history.Add(json);
            if (history.Count > MaxHistory) history.RemoveAt(0);
            index = history.Count - 1;
        }

        public bool CanUndo { get { return index > 0; } }
        public bool CanRedo { get { return index < history.Count - 1; } }

        public SceneData Undo()
        {
            if (!CanUndo) return null;
            index--;
            return JsonUtility.FromJson<SceneData>(history[index]);
        }

        public SceneData Redo()
        {
            if (!CanRedo) return null;
            index++;
            return JsonUtility.FromJson<SceneData>(history[index]);
        }

        // ------------------------------------------------------------------
        // Project files

        public void Save(SceneData d)
        {
            Directory.CreateDirectory(projectDir);
            string tmp = ScenePath + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(d, true));
            if (File.Exists(ScenePath)) File.Delete(ScenePath);
            File.Move(tmp, ScenePath);
        }

        public SceneData Load()
        {
            try
            {
                if (!File.Exists(ScenePath)) return null;
                var d = JsonUtility.FromJson<SceneData>(File.ReadAllText(ScenePath));
                if (d != null)
                {
                    if (d.env == null) d.env = new EnvData();
                    foreach (var it in d.items) it.FixOld();
                }
                return d;
            }
            catch (Exception e)
            {
                Debug.LogWarning("ATLAS: no se pudo leer el proyecto: " + e.Message);
                return null;
            }
        }

        /// Copies a picked model into the project. Returns its file name.
        public string AddModelFile(string source)
        {
            Directory.CreateDirectory(ModelsDir);
            string ext = Path.GetExtension(source).ToLowerInvariant();
            string why = Import.ModelConverter.Unsupported(ext);
            if (why != "") throw new Exception(why);
            if (System.Array.IndexOf(MeshFormats.Importable, ext) < 0) ext = MeshFormats.Sniff(source);
            string name;
            if (ext == ".zip") name = MeshFormats.ExtractPackage(source, ModelsDir);
            else if (ext == ".gma")
            {
                // Garry's Mod addon: unpacked, then its main model.
                string folder = "addon_" + NewId().Substring(0, 8);
                string dest = Path.Combine(ModelsDir, folder);
                Directory.CreateDirectory(dest);
                Import.SourceMdlImport.ExtractGma(source, dest);
                string main = Import.SourceMdlImport.MainMdl(dest);
                if (main == null) throw new Exception("el addon no tiene ningún modelo (.mdl)");
                name = folder + "/" + main.Substring(dest.Length).Replace('\\', '/').TrimStart('/');
            }
            else
            {
                string baseName = Path.GetFileNameWithoutExtension(source);
                if (string.IsNullOrEmpty(baseName)) baseName = "modelo";
                // OBJ / FBX / 3DS look for their textures next to them: keep
                // them in their own folder with whatever was next to them.
                if (System.Array.IndexOf(Import.ModelConverter.Convertible, ext) >= 0)
                {
                    string folder = "modelo_" + NewId().Substring(0, 8);
                    Directory.CreateDirectory(Path.Combine(ModelsDir, folder));
                    name = folder + "/" + baseName + ext;
                    File.Copy(source, Path.Combine(ModelsDir, name), true);
                    CopySidecars(source, Path.Combine(ModelsDir, folder));
                    if (ext == ".mdl") SourceSidecars(source, Path.Combine(ModelsDir, folder), baseName);
                }
                else
                {
                    name = baseName + "_" + NewId().Substring(0, 6) + ext;
                    File.Copy(source, Path.Combine(ModelsDir, name), true);
                }
            }
            return ConvertStored(name);
        }

        /// After an import: the Prisma3D project it came from and the pictures
        /// it could not find (asked for afterwards).
        public string missingTexFrom = "";
        public string[] missingTex = new string[0];

        /// Other formats become one GLB with everything inside.
        string ConvertStored(string name)
        {
            string inExt = Path.GetExtension(name).ToLowerInvariant();
            missingTex = new string[0];
            missingTexFrom = "";
            if (System.Array.IndexOf(Import.ModelConverter.Convertible, inExt) >= 0)
            {
                string full = Path.Combine(ModelsDir, name);
                string glb;
                try { glb = Import.ModelConverter.ToGlb(full, inExt); }
                catch (Import.MissingContentException e)
                {
                    e.folder = Path.GetDirectoryName(full);
                    e.file = name;
                    throw;
                }
                if (inExt == ".prisma" || inExt == ".proj" || inExt == ".pobject")
                {
                    missingTex = Import.PrismaImport.MissingTextures.ToArray();
                    missingTexFrom = name;
                }
                if (glb != null) name = Path.GetDirectoryName(name).Replace('\\', '/') + (Path.GetDirectoryName(name) != "" ? "/" : "") + Path.GetFileName(glb);
            }
            return name;
        }

        static readonly string[] MainOrder = { ".prisma", ".glb", ".gltf", ".proj", ".fbx", ".mdl", ".gma", ".bbmodel", ".obj", ".3ds", ".blend", ".zip", ".ply", ".stl", ".json", ".pobject" };

        /// Several files picked together (a .proj with its .pobject files, an
        /// OBJ with its MTL and textures, a glTF with its .bin…): all of them
        /// go in one folder and the main one is imported.
        public string AddModelFiles(string[] sources)
        {
            if (sources == null || sources.Length == 0) throw new Exception("no se eligió ningún archivo");
            if (sources.Length == 1) return AddModelFile(sources[0]);
            string main = null;
            int rank = 99;
            foreach (var s in sources)
            {
                string e = Path.GetExtension(s).ToLowerInvariant();
                if (System.Array.IndexOf(MeshFormats.Importable, e) < 0 && Import.ModelConverter.Unsupported(e) == "") e = MeshFormats.Sniff(s);
                int r = System.Array.IndexOf(MainOrder, e);
                if (r >= 0 && r < rank) { rank = r; main = s; }
            }
            if (main == null) throw new Exception("entre esos archivos no hay ningún modelo (GLB, FBX, OBJ, Prisma3D…)");
            string mext = Path.GetExtension(main).ToLowerInvariant();
            if (mext == ".zip" || mext == ".prisma" || mext == ".gma") return AddModelFile(main);
            Directory.CreateDirectory(ModelsDir);
            string folder = "modelo_" + NewId().Substring(0, 8);
            string dir = Path.Combine(ModelsDir, folder);
            Directory.CreateDirectory(dir);
            foreach (var s in sources) CopyInto(s, dir);
            string fn = Path.GetFileName(main);
            if (Path.GetExtension(fn) == "") fn += System.Array.IndexOf(MainOrder, MeshFormats.Sniff(main)) >= 0 ? MeshFormats.Sniff(main) : "";
            return ConvertStored(folder + "/" + fn);
        }

        /// The files a project was missing, put next to it; then converted again.
        public string AddMissingFiles(string stored, string[] sources)
        {
            string dir = Path.GetDirectoryName(Path.Combine(ModelsDir, stored));
            foreach (var s in sources) CopyInto(s, dir);
            return ConvertStored(stored);
        }

        static void CopyInto(string source, string dir)
        {
            string n = Path.GetFileName(source);
            if (string.IsNullOrEmpty(n)) return;
            string e = Path.GetExtension(n).ToLowerInvariant();
            if (e == ".pobject")
            {
                Directory.CreateDirectory(Path.Combine(dir, "content"));
                File.Copy(source, Path.Combine(dir, "content", n), true);
            }
            else File.Copy(source, Path.Combine(dir, n), true);
        }

        /// The files next to a picked model (its .mtl and textures), when the
        /// phone lets us see the folder; small images and .mtl only.
        /// A Source model's .vvd / .vtx / .phy, and where it came from (its
        /// textures are in a «materials» folder somewhere above it).
        static void SourceSidecars(string source, string destDir, string baseName)
        {
            try
            {
                string dir = Path.GetDirectoryName(source);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir))
                {
                    string fn = Path.GetFileName(f);
                    if (!fn.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase)) continue;
                    string e = Path.GetExtension(fn).ToLowerInvariant();
                    if (e == ".vvd" || e == ".vtx" || e == ".phy") File.Copy(f, Path.Combine(destDir, fn), true);
                }
                File.WriteAllText(Path.Combine(destDir, baseName + ".srcroot"), dir);
            }
            catch (Exception) { }
        }

        static void CopySidecars(string source, string destDir)
        {
            try
            {
                string dir = Path.GetDirectoryName(source);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                int copied = 0;
                foreach (var f in Directory.GetFiles(dir))
                {
                    string e = Path.GetExtension(f).ToLowerInvariant();
                    if (e != ".mtl" && e != ".png" && e != ".jpg" && e != ".jpeg" && e != ".tga" && e != ".bmp") continue;
                    if (new FileInfo(f).Length > 64L * 1024 * 1024) continue;
                    File.Copy(f, Path.Combine(destDir, Path.GetFileName(f)), true);
                    if (++copied > 200) break;
                }
            }
            catch (Exception) { }
        }

        /// Copies a picked image into the project. Returns its file name.
        public string AddTextureFile(string source)
        {
            Directory.CreateDirectory(TexturesDir);
            string ext = Path.GetExtension(source).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") ext = ".png";
            string baseName = Path.GetFileNameWithoutExtension(source);
            if (string.IsNullOrEmpty(baseName)) baseName = "textura";
            string file = baseName + "_" + NewId().Substring(0, 6) + ext;
            File.Copy(source, Path.Combine(TexturesDir, file), true);
            return file;
        }

        public string TexturePath(string file)
        {
            return Path.Combine(TexturesDir, file);
        }

        public string ModelPath(string file)
        {
            return Path.Combine(ModelsDir, file);
        }
    }
}
