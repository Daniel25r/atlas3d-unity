// ATLAS 5D (Unity) - «Pasar a claves»: a model's own animation (from its
// file) becomes ordinary bone keys from the current frame on, so it can be
// retouched in pose mode, mixed with your own keys, stretched in time or
// shaped in the curve editor.
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        void BakeClipToKeys(AtlasItem it, int every)
        {
            if (it == null || string.IsNullOrEmpty(it.clip)) return;
            float len = it.ClipLength(it.clip);
            if (len <= 0f) { Toast("Esa animación está vacía"); return; }
            int frames = Mathf.Max(1, Mathf.RoundToInt(len * fps));
            int start = Mathf.RoundToInt(frame);
            var paths = new List<string>();
            foreach (var p in it.BoneOrder) if (!IkRig.IsControl(p)) paths.Add(p);
            if (paths.Count == 0) { Toast("Este modelo no tiene huesos"); return; }
            string name = it.clip.Contains("|") ? it.clip.Substring(it.clip.LastIndexOf('|') + 1) : it.clip;
            every = Mathf.Max(1, every);
            for (int f = 0; f <= frames; f += every)
            {
                it.SampleClip(Mathf.Min(f, frames - 0.001f) / fps);
                it.SetKey(start + f, 0, paths);
                it.SetInterp(start + f, every == 1 ? Interp.Linear : Interp.Cubic);
            }
            if (frames % every != 0)
            {
                it.SampleClip((frames - 0.001f) / fps);
                it.SetKey(start + frames, 0, paths);
            }
            it.clip = "";   // the keys move the bones now
            if (start + frames > endFrame) SetRange(startFrame, start + frames);
            Commit(true);
            SetFrame(frame);
            RefreshKeys();
            RefreshProps();
            Toast("«" + name + "» pasada a claves: " + (frames / every + 1) + " claves desde el fotograma " + start + ". Ya puedes retocarla en modo pose");
        }
    }
}
