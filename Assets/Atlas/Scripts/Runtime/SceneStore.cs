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
                });
            }
            return d;
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
            if (ext == ".zip") return MeshFormats.ExtractPackage(source, ModelsDir);
            if (System.Array.IndexOf(MeshFormats.Importable, ext) < 0) ext = MeshFormats.Sniff(source);
            string baseName = Path.GetFileNameWithoutExtension(source);
            if (string.IsNullOrEmpty(baseName)) baseName = "modelo";
            string name = baseName + "_" + NewId().Substring(0, 6) + ext;
            File.Copy(source, Path.Combine(ModelsDir, name), true);
            return name;
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
