// ATLAS 3D (Unity) - audio for the animation: a song, voice or sound
// effect plays with the timeline (from the frame where it starts), shown
// as a bar on the timeline. Saved in the project («audio» folder).
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Atlas
{
    public partial class AtlasApp
    {
        AudioSource audioSrc;
        string audioFile = "";
        int audioStart;            // frame where the sound begins
        float audioVolume = 1f;
        string loadedAudio = "";

        string AudioDir { get { return store != null ? Path.Combine(store.projectDir, "audio") : ""; } }

        void PickAudio()
        {
            CloseMenus();
            if (store == null || NativeFilePicker.IsFilePickerBusy()) return;
            // There is already a sound: this one is added, not swapped.
            if (!string.IsNullOrEmpty(audioFile)) { PickExtraSound(); return; }
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path) || store == null) return;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".mp3" && ext != ".ogg" && ext != ".wav")
                {
                    Toast("Usa un audio MP3, OGG o WAV", true);
                    return;
                }
                try
                {
                    Directory.CreateDirectory(AudioDir);
                    string name = Path.GetFileNameWithoutExtension(path) + "_" + SceneStore.NewId().Substring(0, 6) + ext;
                    File.Copy(path, Path.Combine(AudioDir, name), true);
                    audioFile = name;
                    audioStart = Mathf.RoundToInt(frame);
                }
                catch (System.Exception e)
                {
                    Toast("No se pudo usar el audio: " + e.Message, true);
                    return;
                }
                StartCoroutine(LoadAudio(true));
                Commit(true);
            }, new string[] { "audio/*" });
        }

        void RemoveAudio()
        {
            audioFile = "";
            if (audioSrc != null) { audioSrc.Stop(); audioSrc.clip = null; }
            loadedAudio = "";
            Commit(true);
            RefreshKeys();
            Toast("Audio quitado (Deshacer lo recupera)");
        }

        IEnumerator LoadAudio(bool tell)
        {
            if (store == null || string.IsNullOrEmpty(audioFile)) yield break;
            string path = Path.Combine(AudioDir, audioFile);
            if (!File.Exists(path)) yield break;
            if (audioSrc == null)
            {
                audioSrc = gameObject.AddComponent<AudioSource>();
                audioSrc.playOnAwake = false;
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            AudioType type = ext == ".mp3" ? AudioType.MPEG : ext == ".ogg" ? AudioType.OGGVORBIS : AudioType.WAV;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(path).AbsoluteUri, type))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Toast("No se pudo leer el audio: " + req.error, true);
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null) yield break;
                audioSrc.clip = clip;
                loadedAudio = audioFile;
                audioSrc.volume = audioVolume;
                int needed = audioStart + Mathf.CeilToInt(clip.length * fps);
                if (tell)
                {
                    if (needed > endFrame)
                    {
                        endFrame = needed;
                        Commit(true);
                        Toast("Audio puesto: la animación se alargó a " + (endFrame / (float)fps).ToString("0.0") + " s para que quepa");
                    }
                    else Toast("Audio puesto: suena al reproducir la animación");
                }
                RefreshKeys();
            }
        }

        /// Starts / stops / re-syncs the sound with the timeline.
        void SyncAudio()
        {
            if (audioSrc == null || audioSrc.clip == null || string.IsNullOrEmpty(audioFile)) return;
            if (!playing || rendering) { if (audioSrc.isPlaying) audioSrc.Stop(); return; }
            float t = (frame - audioStart) / fps;
            if (t < 0f || t >= audioSrc.clip.length)
            {
                if (audioSrc.isPlaying) audioSrc.Stop();
                return;
            }
            if (!audioSrc.isPlaying || Mathf.Abs(audioSrc.time - t) > 0.15f)
            {
                audioSrc.time = t;
                if (!audioSrc.isPlaying) audioSrc.Play();
            }
        }

        /// The sound's bar on the timeline (start and end frames).
        bool AudioBar(out float start, out float end)
        {
            start = end = 0f;
            if (audioSrc == null || audioSrc.clip == null || string.IsNullOrEmpty(audioFile)) return false;
            start = audioStart;
            end = audioStart + audioSrc.clip.length * fps;
            return true;
        }

        /// After undo / opening: the sound of the saved state.
        void RestoreAudio(SceneData d)
        {
            audioFile = d.audioFile ?? "";
            audioStart = d.audioStart;
            audioVolume = d.audioVolume > 0f ? d.audioVolume : 1f;
            if (string.IsNullOrEmpty(audioFile))
            {
                if (audioSrc != null) { audioSrc.Stop(); audioSrc.clip = null; }
                loadedAudio = "";
            }
            else if (audioFile != loadedAudio) StartCoroutine(LoadAudio(false));
            else if (audioSrc != null) audioSrc.volume = audioVolume;
        }
    }
}
