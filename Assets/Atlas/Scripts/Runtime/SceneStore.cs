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
    }

    public class SceneStore
    {
        public readonly string projectDir;
        public string ModelsDir { get { return Path.Combine(projectDir, "models"); } }
        string ScenePath { get { return Path.Combine(projectDir, "scene.json"); } }

        readonly List<string> history = new List<string>();
        int index = -1;
        const int MaxHistory = 60;

        public SceneStore(string name)
        {
            projectDir = Path.Combine(Application.persistentDataPath, "Proyectos", name);
            Directory.CreateDirectory(ModelsDir);
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
                });
            }
            return d;
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
                return JsonUtility.FromJson<SceneData>(File.ReadAllText(ScenePath));
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
            if (ext != ".glb" && ext != ".gltf") ext = ".glb";
            string baseName = Path.GetFileNameWithoutExtension(source);
            if (string.IsNullOrEmpty(baseName)) baseName = "modelo";
            string name = baseName + "_" + NewId().Substring(0, 6) + ext;
            File.Copy(source, Path.Combine(ModelsDir, name), true);
            return name;
        }

        public string ModelPath(string file)
        {
            return Path.Combine(ModelsDir, file);
        }
    }
}
