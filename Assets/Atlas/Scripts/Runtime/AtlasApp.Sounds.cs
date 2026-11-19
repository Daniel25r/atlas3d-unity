// ATLAS 3D (Unity) - more than one sound: besides the main audio (the one
// lip sync listens to), any number of extra sounds (effects, music, voices),
// each with the frame where it starts and its volume, all playing together
// with the timeline. One bar per sound on the timeline.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Atlas
{
    [System.Serializable]
    public class SoundData
    {
        public string file = "";
        public int start;
        public float volume = 1f;
        public SoundData Copy() { return (SoundData)MemberwiseClone(); }
    }

    public partial class AtlasApp
    {
        List<SoundData> sounds = new List<SoundData>();
        readonly List<AudioSource> soundSrc = new List<AudioSource>();
        readonly Dictionary<string, AudioClip> soundClips = new Dictionary<string, AudioClip>();
        readonly HashSet<string> soundLoading = new HashSet<string>();

        static List<SoundData> CopySounds(List<SoundData> l)
        {
            var r = new List<SoundData>();
            if (l != null) foreach (var s in l) if (s != null && !string.IsNullOrEmpty(s.file)) r.Add(s.Copy());
            return r;
        }

        /// One more sound, starting at the current frame.
        void PickExtraSound()
        {
            CloseMenus();
            if (store == null || NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path) || store == null) return;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".mp3" && ext != ".ogg" && ext != ".wav") { Toast("Usa un audio MP3, OGG o WAV", true); return; }
                try
                {
                    Directory.CreateDirectory(AudioDir);
                    string name = Path.GetFileNameWithoutExtension(path) + "_" + SceneStore.NewId().Substring(0, 6) + ext;
                    File.Copy(path, Path.Combine(AudioDir, name), true);
                    sounds.Add(new SoundData { file = name, start = Mathf.RoundToInt(frame) });
                }
                catch (System.Exception e) { Toast("No se pudo usar el audio: " + e.Message, true); return; }
                Commit(true);
                SyncSoundSources();
                Toast("Sonido añadido en el fotograma " + Mathf.RoundToInt(frame) + " (" + (sounds.Count + (string.IsNullOrEmpty(audioFile) ? 0 : 1)) + " sonidos en total)");
            }, new string[] { "audio/*" });
        }

        /// The sources match the list; clips load in the background.
        void SyncSoundSources()
        {
            while (soundSrc.Count < sounds.Count)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                soundSrc.Add(s);
            }
            for (int i = 0; i < soundSrc.Count; i++)
            {
                var src = soundSrc[i];
                if (i >= sounds.Count) { if (src.isPlaying) src.Stop(); src.clip = null; continue; }
                var sd = sounds[i];
                AudioClip clip;
                if (soundClips.TryGetValue(sd.file, out clip)) src.clip = clip;
                else { src.clip = null; if (!soundLoading.Contains(sd.file)) StartCoroutine(LoadSound(sd.file)); }
                src.volume = Mathf.Clamp01(sd.volume);
            }
            RefreshKeys();
        }

        IEnumerator LoadSound(string file)
        {
            if (store == null) yield break;
            string path = Path.Combine(AudioDir, file);
            if (!File.Exists(path)) yield break;
            soundLoading.Add(file);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            AudioType type = ext == ".mp3" ? AudioType.MPEG : ext == ".ogg" ? AudioType.OGGVORBIS : AudioType.WAV;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(path).AbsoluteUri, type))
            {
                yield return req.SendWebRequest();
                soundLoading.Remove(file);
                if (req.result != UnityWebRequest.Result.Success) { Toast("No se pudo leer el audio: " + req.error, true); yield break; }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null) yield break;
                soundClips[file] = clip;
            }
            SyncSoundSources();
        }

        /// Plays / stops / re-syncs every extra sound with the timeline.
        void SyncExtraSounds()
        {
            for (int i = 0; i < soundSrc.Count && i < sounds.Count; i++)
            {
                var src = soundSrc[i];
                if (src == null || src.clip == null) continue;
                if (!playing || rendering) { if (src.isPlaying) src.Stop(); continue; }
                float t = (frame - sounds[i].start) / fps;
                if (t < 0f || t >= src.clip.length) { if (src.isPlaying) src.Stop(); continue; }
                if (!src.isPlaying || Mathf.Abs(src.time - t) > 0.15f)
                {
                    src.time = t;
                    if (!src.isPlaying) src.Play();
                }
            }
        }

        void StopExtraSounds()
        {
            foreach (var s in soundSrc) if (s != null) { s.Stop(); s.clip = null; }
            sounds = new List<SoundData>();
        }

        void RestoreSounds(SceneData d)
        {
            sounds = CopySounds(d.sounds);
            SyncSoundSources();
        }

        /// Timeline rows: one bar per extra sound.
        void AddSoundRows(List<TimelineRow> rows)
        {
            for (int i = 0; i < sounds.Count && i < soundSrc.Count; i++)
            {
                var src = soundSrc[i];
                if (src == null || src.clip == null) continue;
                var sd = sounds[i];
                rows.Insert(0, new TimelineRow { item = null, track = "snd:" + i, label = "♪ " + ShortSound(sd.file), barStart = sd.start, barEnd = sd.start + src.clip.length * fps });
            }
        }

        static string ShortSound(string file)
        {
            string n = Path.GetFileNameWithoutExtension(file);
            int u = n.LastIndexOf('_');
            return u > 0 ? n.Substring(0, u) : n;
        }

        /// Menu entries for the extra sounds (timeline options).
        void AddSoundMenu(List<AtlasTheme.MenuItem> l)
        {
            l.Add(new AtlasTheme.MenuItem("music", "Añadir otro sonido aquí…", PickExtraSound));
            for (int i = 0; i < sounds.Count; i++)
            {
                int k = i;
                string n = ShortSound(sounds[i].file);
                l.Add(new AtlasTheme.MenuItem("music", "♪ " + n + ": opciones…", () => OpenSoundMenu(k)));
            }
        }

        void OpenSoundsMenu()
        {
            var l = new List<AtlasTheme.MenuItem> { AtlasTheme.MenuItem.Header("Sonidos") };
            AddSoundMenu(l);
            AtlasTheme.Popup(canvasRoot, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), l);
            toastHolder.SetAsLastSibling();
        }

        void OpenSoundMenu(int i)
        {
            if (i < 0 || i >= sounds.Count) return;
            var sd = sounds[i];
            var l = new List<AtlasTheme.MenuItem>
            {
                AtlasTheme.MenuItem.Header("♪ " + ShortSound(sd.file)),
                new AtlasTheme.MenuItem("music", "Empezar en el fotograma " + Mathf.RoundToInt(frame), () => { sd.start = Mathf.RoundToInt(frame); Commit(true); RefreshKeys(); }),
                new AtlasTheme.MenuItem("music", "Volumen 100 %", () => { sd.volume = 1f; Commit(true); SyncSoundSources(); }),
                new AtlasTheme.MenuItem("music", "Volumen 50 %", () => { sd.volume = 0.5f; Commit(true); SyncSoundSources(); }),
                new AtlasTheme.MenuItem("music", "Volumen 25 %", () => { sd.volume = 0.25f; Commit(true); SyncSoundSources(); }),
                new AtlasTheme.MenuItem("close", "Quitar este sonido", () =>
                {
                    sounds.RemoveAt(i);
                    Commit(true);
                    SyncSoundSources();
                    Toast("Sonido quitado (Deshacer lo recupera)");
                }),
            };
            AtlasTheme.Popup(canvasRoot, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), l);
            toastHolder.SetAsLastSibling();
        }
    }
}
