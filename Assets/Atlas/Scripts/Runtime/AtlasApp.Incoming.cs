// ATLAS 3D (Unity) - models sent from other apps ("Open with" / "Share":
// Prisma3D, the file manager, Drive…). The Android side (AtlasOpenFile.java)
// leaves them in files/incoming/b_<time>/; they are imported into the open
// project, or into a new one named after the file.
using System.IO;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        bool skipStarterCube;
        float nextIncomingCheck;

        void OnApplicationFocus(bool focus)
        {
            if (focus) nextIncomingCheck = 0f;
        }

        static string IncomingDir()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var cls = new AndroidJavaClass("org.atlas3d.AtlasOpenFile"))
                    return cls.CallStatic<string>("incomingPath", act);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("ATLAS: archivos recibidos no disponibles: " + e.Message);
                return null;
            }
#else
            return Path.Combine(Application.persistentDataPath, "incoming");
#endif
        }

        /// Called every frame; looks for received files now and then.
        void CheckIncoming()
        {
            if (Time.unscaledTime < nextIncomingCheck || rendering) return;
            nextIncomingCheck = Time.unscaledTime + 2f;
            string dir = IncomingDir();
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            string[] batches;
            try { batches = Directory.GetDirectories(dir, "b_*"); }
            catch (System.Exception) { return; }
            if (batches.Length == 0) return;
            System.Array.Sort(batches);
            string batch = batches[0];
            string[] files = Directory.GetFiles(batch, "*", SearchOption.AllDirectories);
            if (files.Length == 0) { TryDelete(batch); return; }
            if (store == null)
            {
                // From the home screen: a new project named after the file.
                string main = files[0];
                foreach (var f in files) if (!Path.GetExtension(f).Equals(".pobject", System.StringComparison.OrdinalIgnoreCase)) { main = f; break; }
                string nm = SceneStore.CleanName(Path.GetFileNameWithoutExtension(main));
                if (string.IsNullOrEmpty(nm)) nm = "Modelo";
                if (SceneStore.Exists(nm)) nm = SceneStore.UniqueName(nm);
                skipStarterCube = true;
                OpenProject(nm);
                skipStarterCube = false;
            }
            // Out of the queue now, deleted once imported.
            string taken = Path.Combine(dir, "t" + Path.GetFileName(batch));
            try { Directory.Move(batch, taken); }
            catch (System.Exception) { TryDelete(batch); return; }
            files = Directory.GetFiles(taken, "*", SearchOption.AllDirectories);
            ImportFiles(files, taken);
        }

        static void TryDelete(string dir)
        {
            try { Directory.Delete(dir, true); } catch (System.Exception) { }
        }
    }
}
