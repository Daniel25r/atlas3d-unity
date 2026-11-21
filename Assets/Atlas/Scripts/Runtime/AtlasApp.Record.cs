// ATLAS 5D (Unity) - «Grabar voz»: records from the microphone while the
// animation plays from the current frame (to dub in time with it) and puts
// the recording on the timeline as one more sound, starting at that frame.
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        AudioClip recClip;
        string recDevice;
        int recStartFrame;
        bool recording;
        GameObject recButton;
        const int RecRate = 44100, RecMaxSeconds = 600;

        void StartVoiceRecording()
        {
            CloseMenus();
            if (store == null || recording) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                var cb = new UnityEngine.Android.PermissionCallbacks();
                cb.PermissionGranted += _ => StartCoroutine(BeginRecordingSoon());
                cb.PermissionDenied += _ => Toast("Sin permiso para el micrófono: actívalo en los ajustes del teléfono para grabar la voz", true);
                cb.PermissionDeniedAndDontAskAgain += _ => Toast("El micrófono está bloqueado para ATLAS: actívalo en Ajustes → Apps → ATLAS 5D → Permisos", true);
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, cb);
                return;
            }
#endif
            StartCoroutine(BeginRecordingSoon());
        }

        IEnumerator BeginRecordingSoon()
        {
            yield return null;
            if (Microphone.devices == null || Microphone.devices.Length == 0) { Toast("No se encontró ningún micrófono", true); yield break; }
            recDevice = Microphone.devices[0];
            recClip = Microphone.Start(recDevice, false, RecMaxSeconds, RecRate);
            if (recClip == null) { Toast("No se pudo abrir el micrófono", true); yield break; }
            // Wait until it really starts (some phones take a moment).
            float t0 = Time.unscaledTime;
            while (Microphone.GetPosition(recDevice) <= 0 && Time.unscaledTime - t0 < 2f) yield return null;
            recording = true;
            recStartFrame = Mathf.RoundToInt(frame);
            if (!playing) TogglePlay();
            ShowRecButton(true);
            Toast("Grabando desde el fotograma " + recStartFrame + ": habla al ritmo de la animación. Toca ■ para parar");
        }

        void StopVoiceRecording()
        {
            if (!recording) return;
            recording = false;
            int len = Microphone.GetPosition(recDevice);
            Microphone.End(recDevice);
            if (playing) TogglePlay();
            ShowRecButton(false);
            if (recClip == null || len <= RecRate / 10) { Toast("Grabación demasiado corta"); return; }
            var data = new float[len * recClip.channels];
            recClip.GetData(data, 0);
            try
            {
                Directory.CreateDirectory(AudioDir);
                string name = "voz_" + SceneStore.NewId().Substring(0, 6) + ".wav";
                File.WriteAllBytes(Path.Combine(AudioDir, name), Wav(data, recClip.channels, recClip.frequency));
                sounds.Add(new SoundData { file = name, start = recStartFrame });
            }
            catch (System.Exception e) { Toast("No se pudo guardar la voz: " + e.Message, true); return; }
            finally { Destroy(recClip); recClip = null; }
            Commit(true);
            SyncSoundSources();
            RefreshKeys();
            SetFrame(recStartFrame);
            Toast("Voz guardada (" + (len / (float)RecRate).ToString("0.0") + " s) desde el fotograma " + recStartFrame + ". Para mover la boca con ella: Sincronizar labios");
        }

        void ShowRecButton(bool on)
        {
            if (!on) { if (recButton != null) recButton.SetActive(false); return; }
            if (recButton == null)
            {
                var b = AtlasTheme.TextButton(canvasRoot, null, "■  Grabando… toca para parar", StopVoiceRecording, false, 48f, 17, 330f);
                b.GetComponent<Image>().color = AtlasTheme.Danger;
                var r = (RectTransform)b.transform;
                AtlasTheme.Pin(r, 0.5f, 1f, new Vector2(0f, -70f), new Vector2(330f, 48f));
                recButton = b.gameObject;
            }
            recButton.SetActive(true);
            recButton.transform.SetAsLastSibling();
        }

        /// 16-bit PCM WAV.
        static byte[] Wav(float[] samples, int channels, int rate)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int bytes = samples.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(bytes);
                foreach (float s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
                return ms.ToArray();
            }
        }
    }
}
