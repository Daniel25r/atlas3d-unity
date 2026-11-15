// ATLAS 3D (Unity) - «Agarrar»: an object held by a bone of a character
// (a hand, the mouth, a tail…) from one frame and let go at another, like a
// prop in an animation. And «Sincronizar labios»: the mouth opens with the
// loudness of the scene's audio, as editable keys.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        AtlasItem grabModel;
        string grabFilter = "";
        float lipAngle = 18f;

        // ------------------------------------------------------------------
        // Anchors

        static AnchorEvent ActiveAnchor(AtlasItem it, float f)
        {
            AnchorEvent best = null;
            foreach (var a in it.anchors) if (a.frame <= f + 0.0001f && (best == null || a.frame >= best.frame)) best = a;
            return best != null && best.target != "" ? best : null;
        }

        Transform AnchorTransform(AnchorEvent a)
        {
            var model = FindItem(a.target);
            if (model == null) return null;
            if (string.IsNullOrEmpty(a.bone)) return model.transform;
            return model.Bone(a.bone);
        }

        /// Held objects follow their bone (after the bones are posed).
        void ApplyAnchors()
        {
            foreach (var it in items)
            {
                if (it == null || it.anchors.Count == 0) continue;
                var a = ActiveAnchor(it, frame);
                if (a == null) continue;
                var t = AnchorTransform(a);
                if (t == null) continue;
                it.transform.SetPositionAndRotation(t.TransformPoint(a.pos), t.rotation * a.rot);
            }
        }

        /// Moving a held object with the manipulator changes how it is held.
        bool Regrip(AtlasItem it)
        {
            if (it == null || it.anchors.Count == 0) return false;
            var a = ActiveAnchor(it, frame);
            if (a == null) return false;
            var t = AnchorTransform(a);
            if (t == null) return false;
            a.pos = t.InverseTransformPoint(it.transform.position);
            a.rot = Quaternion.Inverse(t.rotation) * it.transform.rotation;
            Toast("Agarre ajustado");
            return true;
        }

        void Grab(AtlasItem it, AtlasItem model, string bone)
        {
            int f = Mathf.RoundToInt(frame);
            var t = string.IsNullOrEmpty(bone) ? model.transform : model.Bone(bone);
            if (t == null) return;
            // Where it was just before: a key, so it waits there until grabbed.
            it.SetKey(f, AtlasItem.ChPos | AtlasItem.ChRot, new List<string>());
            it.anchors.RemoveAll(a => a.frame == f);
            it.anchors.Add(new AnchorEvent
            {
                frame = f,
                target = model.id,
                bone = bone ?? "",
                pos = t.InverseTransformPoint(it.transform.position),
                rot = Quaternion.Inverse(t.rotation) * it.transform.rotation,
            });
            it.anchors.Sort((x, y) => x.frame.CompareTo(y.frame));
            grabModel = null;
            Commit(true);
            RefreshKeys();
            RefreshProps();
            Toast("Agarrado en el fotograma " + f + ": ahora sigue a «" + BoneNames.Display(BoneNames.Leaf(bone)) + "»");
        }

        void LetGo(AtlasItem it)
        {
            int f = Mathf.RoundToInt(frame);
            ApplyAnchors();
            // Stays where the hand left it.
            it.SetKey(f, AtlasItem.ChPos | AtlasItem.ChRot, new List<string>());
            it.anchors.RemoveAll(a => a.frame == f);
            it.anchors.Add(new AnchorEvent { frame = f, target = "" });
            it.anchors.Sort((x, y) => x.frame.CompareTo(y.frame));
            Commit(true);
            RefreshKeys();
            RefreshProps();
            Toast("Soltado en el fotograma " + f);
        }

        void BuildAnchorSection(AtlasItem it)
        {
            var models = new List<AtlasItem>();
            foreach (var o in items) if (o != null && o != it && o.kind == "model" && o.HasBones) models.Add(o);
            if (models.Count == 0 && it.anchors.Count == 0) return;
            var box = Sec("Agarrar con un hueso", "bone", it.anchors.Count == 0);
            var held = ActiveAnchor(it, frame);
            if (held != null)
            {
                var m = FindItem(held.target);
                Hint(box, "Ahora lo sostiene «" + (m != null ? m.gameObject.name : "?") + "» (" + BoneNames.Display(BoneNames.Leaf(held.bone)) + "). Muévelo con el manipulador para ajustar cómo lo agarra.");
                AtlasTheme.TextButton(box, "unlock", "Soltar aquí (fotograma " + Mathf.RoundToInt(frame) + ")", () => LetGo(it), true, 46f, 16);
            }
            else
            {
                Hint(box, "Ve al fotograma donde lo agarra, coloca el objeto en la mano y elige el personaje y el hueso.");
                var g = AtlasTheme.Grid(box, 2, InnerWidth, 38f);
                foreach (var m0 in models)
                {
                    var m = m0;
                    AtlasTheme.Chip(g, m.gameObject.name, () => { grabModel = grabModel == m ? null : m; grabFilter = ""; RefreshProps(); }, grabModel == m);
                }
                if (grabModel != null && models.Contains(grabModel))
                {
                    var field = AtlasTheme.TextField(box, grabFilter, v => { grabFilter = v.Trim().ToLowerInvariant(); RefreshProps(); }, 40f);
                    var ph = AtlasTheme.Label(field.transform, "Buscar hueso (mano, boca…) y pulsa Intro", 14, AtlasTheme.TextDim);
                    AtlasTheme.Fill(ph.rectTransform, 12f, 12f);
                    field.placeholder = ph;
                    var bl = AtlasTheme.Grid(box, 2, InnerWidth, 36f);
                    int shown = 0;
                    var model = grabModel;
                    foreach (var path in model.BoneOrder)
                    {
                        string leaf = BoneNames.Leaf(path);
                        if (!ShowBone(model, path)) continue;
                        string disp = BoneNames.Display(leaf);
                        if (grabFilter != "" && !(disp + " " + leaf).ToLowerInvariant().Contains(grabFilter)) continue;
                        string p = path;
                        AtlasTheme.Chip(bl, disp, () => Grab(it, model, p));
                        if (++shown >= 60) break;
                    }
                }
            }
            if (it.anchors.Count > 0)
            {
                var ll = AtlasTheme.Label(box, "Momentos", 15, AtlasTheme.TextColor, true);
                AtlasTheme.Le(ll.gameObject, 24f);
                foreach (var a0 in it.anchors)
                {
                    var a = a0;
                    var m = FindItem(a.target);
                    string txt = a.target == "" ? "◆ " + a.frame + ": suelto" : "◆ " + a.frame + ": lo agarra " + (m != null ? m.gameObject.name : "?") + " · " + BoneNames.Display(BoneNames.Leaf(a.bone));
                    var row = AtlasTheme.Rect(box, "Momento").gameObject;
                    AtlasTheme.Row(row, 6f, new RectOffset(0, 0, 0, 0));
                    AtlasTheme.Le(row, 40f);
                    var b = AtlasTheme.TextButton(row.transform, null, txt, () => { SetFrame(a.frame); RefreshProps(); }, false, 40f, 14);
                    AtlasTheme.Le(b.gameObject, 40f, -1f, 1f);
                    AtlasTheme.IconButton(row.transform, "close", () => { it.anchors.Remove(a); SetFrame(frame); Commit(true); RefreshProps(); }, 40f, 20f);
                }
            }
        }

        // ------------------------------------------------------------------
        // Lip sync

        /// The jaw / mouth bone by its name, or the active one.
        string MouthBone(AtlasItem it)
        {
            if (activeBone != "" && it.Bone(activeBone) != null) return activeBone;
            foreach (var p in it.BoneOrder)
            {
                string l = BoneNames.Leaf(p).ToLowerInvariant();
                if (l.Contains("jaw") || l.Contains("mouth") || l.Contains("mandib") || l.Contains("boca") || l.Contains("chin")) return p;
            }
            return "";
        }

        void BuildLipSyncSection(AtlasItem it, Transform box)
        {
            var t = AtlasTheme.Label(box, "Sincronizar labios (audio)", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t.gameObject, 26f);
            if (audioSrc == null || audioSrc.clip == null) { Hint(box, "Primero pon un audio (+ → Audio…): la boca se moverá con la voz."); return; }
            string bone = MouthBone(it);
            Hint(box, bone == "" ? "Elige el hueso de la mandíbula (tócalo en el visor) y vuelve aquí." : "Hueso de la boca: " + BoneNames.Display(BoneNames.Leaf(bone)) + " (si no es, toca otro hueso en el visor).");
            AtlasTheme.SliderRow(box, "Apertura de la boca (°)", 3f, 45f, lipAngle, "0", x => lipAngle = x, null);
            AtlasTheme.TextButton(box, "music", "Generar movimiento de boca", () => LipSync(it, MouthBone(it)), true, 44f, 15);
        }

        void LipSync(AtlasItem it, string bone)
        {
            var t = it.Bone(bone);
            var clip = audioSrc != null ? audioSrc.clip : null;
            if (t == null || clip == null) { Toast("Falta el audio o el hueso de la boca", true); return; }
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) { Toast("No se pudo leer el audio", true); return; }
            int frames = Mathf.CeilToInt(clip.length * fps);
            var level = new float[frames];
            int per = Mathf.Max(1, clip.frequency / fps) * clip.channels;
            for (int f = 0; f < frames; f++)
            {
                double sum = 0;
                int a = f * per, b = Mathf.Min(data.Length, a + per);
                for (int i = a; i < b; i++) sum += data[i] * data[i];
                level[f] = b > a ? Mathf.Sqrt((float)(sum / (b - a))) : 0f;
            }
            var sorted = (float[])level.Clone();
            System.Array.Sort(sorted);
            float top = Mathf.Max(1e-4f, sorted[Mathf.Clamp((int)(sorted.Length * 0.95f), 0, sorted.Length - 1)]);
            // The pose the mouth has now is «closed».
            var rest = t.localRotation;
            int from = audioStart, to = Mathf.Min(endFrame, audioStart + frames);
            foreach (var k in it.keys) if (k.frame >= from && k.frame <= to && k.bones != null) k.bones.RemoveAll(b => b.path == bone);
            it.keys.RemoveAll(k => k.Empty);
            float open = 0f;
            for (int f = from; f <= to; f++)
            {
                int i = f - audioStart;
                float target = i >= 0 && i < frames ? Mathf.Clamp01((level[i] / top - 0.12f) / 0.88f) : 0f;
                open = target > open ? Mathf.Lerp(open, target, 0.8f) : Mathf.Lerp(open, target, 0.45f);
                if ((f - from) % 2 != 0 && f != to) continue;
                t.localRotation = rest * Quaternion.Euler(open * lipAngle, 0f, 0f);
                it.SetKey(f, 0, new List<string> { bone });
            }
            t.localRotation = rest;
            it.RecordBone(bone);
            SetFrame(frame);
            Commit(true);
            RefreshKeys();
            Toast("Boca animada con el audio: " + (to - from) + " fotogramas (se puede deshacer)");
        }
    }
}
