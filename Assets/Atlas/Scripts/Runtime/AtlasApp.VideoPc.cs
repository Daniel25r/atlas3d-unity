// ATLAS 5D (Unity) - MP4 on the PC. Unity has no video encoder of its own
// outside the editor, so the Windows build carries FFmpeg (LGPL build, in
// the «ffmpeg» folder next to the program, added by the build workflow).
// The frames are rendered as PNG pictures, and FFmpeg joins them with the
// project's sounds (each at its frame and volume) into an MP4 (H.264).
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        static string FfmpegPath()
        {
            try
            {
                string root = Path.GetDirectoryName(Application.dataPath);
                foreach (var p in new[] { Path.Combine(root, "ffmpeg", "ffmpeg.exe"), Path.Combine(root, "ffmpeg.exe"), Path.Combine(root, "ffmpeg", "ffmpeg") })
                    if (File.Exists(p)) return p;
            }
            catch (System.Exception) { }
            return null;
        }

        IEnumerator RenderVideoFfmpeg()
        {
            rendering = true;
            if (playing) TogglePlay();
            float keep = frame;
            int w = renderW & ~1, h = renderH & ~1;
            int rA, rB;
            RenderRange(out rA, out rB);
            int total = rB - rA + 1;
            string tmp = Path.Combine(Application.temporaryCachePath, "video_" + Stamp());
            string outPath = Path.Combine(Exporter.ExportDir, (store.name.Replace(' ', '_')) + "_" + Stamp() + ".mp4");
            bool ok = true;
            string err = "";
            try { Directory.CreateDirectory(tmp); }
            catch (System.Exception e) { ok = false; err = e.Message; }
            Toast("Vídeo MP4: " + total + " fotogramas…");
            for (int f = rA; ok && f <= rB; f++)
            {
                SetFrame(f);
                yield return WaitVideos();
                try
                {
                    var tex = Capture(w, h, renderGround);
                    File.WriteAllBytes(Path.Combine(tmp, "f_" + (f - rA).ToString("00000") + ".png"), tex.EncodeToPNG());
                    Destroy(tex);
                }
                catch (System.Exception e) { ok = false; err = e.Message; }
                if ((f - rA) % 24 == 23) Toast("Vídeo: " + (f - rA + 1) + " / " + total);
                yield return null;
            }
            SetFrame(keep);
            if (ok)
            {
                Toast("Uniendo el vídeo y el sonido…");
                // Encoders to try: Windows' own H.264 (fast), then the plain MPEG-4 one.
                foreach (var codec in new[] { "h264_mf", "libopenh264", "mpeg4" })
                {
                    string args = FfmpegArgs(tmp, outPath, codec, w, h, rA, rB);
                    System.Diagnostics.Process proc = null;
                    System.Threading.Tasks.Task<string> errTask = null;
                    string log = "";
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo(FfmpegPath(), args)
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardError = true,
                            RedirectStandardOutput = false,
                        };
                        proc = System.Diagnostics.Process.Start(psi);
                        errTask = proc.StandardError.ReadToEndAsync();
                    }
                    catch (System.Exception e) { proc = null; log = e.Message; }
                    while (proc != null && !proc.HasExited) yield return null;
                    if (errTask != null) { try { log = errTask.Result; } catch (System.Exception) { } }
                    if (proc != null && proc.ExitCode == 0 && File.Exists(outPath) && new FileInfo(outPath).Length > 0) { ok = true; err = ""; break; }
                    ok = false;
                    var lines = (log ?? "").Trim().Split('\n');
                    err = lines.Length > 0 ? lines[lines.Length - 1].Trim() : "ffmpeg falló";
                    try { if (File.Exists(outPath)) File.Delete(outPath); } catch (System.Exception) { }
                }
            }
            try { Directory.Delete(tmp, true); } catch (System.Exception) { }
            rendering = false;
            if (ok) { Share(outPath, "Vídeo MP4 (" + total + " fotogramas, " + fps + " fps, con sonido)"); yield break; }
            Toast("No se pudo crear el MP4 (" + err + "): se guardan los fotogramas en ZIP", true);
            yield return RenderFrames();
        }

        /// The command for FFmpeg: the pictures, every sound delayed to its
        /// frame (and cut to the rendered stretch), mixed, into an MP4.
        string FfmpegArgs(string dir, string outPath, string codec, int w, int h, int rA, int rB)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append("-y -hide_banner -loglevel error ");
            sb.Append("-framerate ").Append(fps).Append(" -i \"").Append(Path.Combine(dir, "f_%05d.png")).Append("\" ");
            var audio = new List<KeyValuePair<string, KeyValuePair<int, float>>>();
            if (!string.IsNullOrEmpty(audioFile))
            {
                string ap = Path.Combine(AudioDir, audioFile);
                if (File.Exists(ap)) audio.Add(new KeyValuePair<string, KeyValuePair<int, float>>(ap, new KeyValuePair<int, float>(audioStart, audioVolume)));
            }
            foreach (var s in sounds)
            {
                string sp = Path.Combine(AudioDir, s.file);
                if (File.Exists(sp)) audio.Add(new KeyValuePair<string, KeyValuePair<int, float>>(sp, new KeyValuePair<int, float>(s.start, s.volume)));
            }
            foreach (var a in audio) sb.Append("-i \"").Append(a.Key).Append("\" ");
            if (audio.Count > 0)
            {
                sb.Append("-filter_complex \"");
                for (int i = 0; i < audio.Count; i++)
                {
                    int ms = Mathf.Max(0, Mathf.RoundToInt(audio[i].Value.Key * 1000f / Mathf.Max(1, fps)));
                    sb.Append('[').Append(i + 1).Append(":a]adelay=").Append(ms).Append('|').Append(ms)
                      .Append(",volume=").Append(audio[i].Value.Value.ToString("0.###", ci)).Append("[a").Append(i).Append("];");
                }
                for (int i = 0; i < audio.Count; i++) sb.Append("[a").Append(i).Append(']');
                float from = rA / (float)Mathf.Max(1, fps), len = (rB - rA + 1) / (float)Mathf.Max(1, fps);
                sb.Append("amix=inputs=").Append(audio.Count).Append(":normalize=0,atrim=start=").Append(from.ToString("0.###", ci))
                  .Append(":duration=").Append(len.ToString("0.###", ci)).Append(",asetpts=PTS-STARTPTS[aout]\" ");
                sb.Append("-map 0:v -map \"[aout]\" -c:a aac -b:a 192k ");
            }
            int bitrate = Mathf.Clamp(Mathf.RoundToInt(w * h * fps * 0.2f), 3000000, 40000000);
            sb.Append("-c:v ").Append(codec).Append(" -b:v ").Append(bitrate).Append(" -pix_fmt yuv420p ");
            if (codec == "mpeg4") sb.Append("-q:v 2 ");
            sb.Append("-movflags +faststart \"").Append(outPath).Append('"');
            return sb.ToString();
        }
    }
}
