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
        /// Several passes, so a chain (a cup on a tray in a hand) follows too.
        void ApplyAnchors()
        {
            for (int pass = 0; pass < 3; pass++)
            {
                bool any = false;
                foreach (var it in items)
                {
                    if (it == null || it.anchors.Count == 0) continue;
                    var a = ActiveAnchor(it, frame);
                    if (a == null) continue;
                    var t = AnchorTransform(a);
                    if (t == null) continue;
                    it.transform.SetPositionAndRotation(t.TransformPoint(a.pos), t.rotation * a.rot);
                    any = true;
                }
                if (!any) break;
            }
        }

        /// True when `a` is held (directly or through others) by `b` now.
        bool HoldsOn(AtlasItem a, AtlasItem b)
        {
            var cur = a;
            for (int i = 0; i < 16 && cur != null && b != null; i++)
            {
                var an = ActiveAnchor(cur, frame);
                if (an == null) return false;
                if (an.target == b.id) return true;
                cur = FindItem(an.target);
            }
            return false;
        }

        static string GripName(string bone)
        {
            return string.IsNullOrEmpty(bone) ? "todo el objeto" : BoneNames.Display(BoneNames.Leaf(bone));
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

        void Grab(AtlasItem it, AtlasItem model, string bone, bool finish = true)
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
            if (!finish) return;
            Commit(true);
            RefreshKeys();
            RefreshProps();
            Toast("Pegado en el fotograma " + f + ": ahora sigue a «" + model.gameObject.name + "» (" + GripName(bone) + ")");
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
            foreach (var o in items) if (o != null && o != it && !HoldsOn(o, it)) models.Add(o);
            // Characters (with bones) first.
            models.Sort((x, y) => (y.HasBones ? 1 : 0).CompareTo(x.HasBones ? 1 : 0));
            if (models.Count == 0 && it.anchors.Count == 0) return;
            var box = Sec("Unir / pegar a otro objeto o hueso", "bone", true);
            var held = ActiveAnchor(it, frame);
            if (held != null)
            {
                var m = FindItem(held.target);
                Hint(box, "Ahora va pegado a «" + (m != null ? m.gameObject.name : "?") + "» (" + GripName(held.bone) + "). Muévelo con el manipulador para ajustar dónde va. Su propia animación de huesos sigue igual.");
                AtlasTheme.TextButton(box, "unlock", "Soltar aquí (fotograma " + Mathf.RoundToInt(frame) + ")", () => LetGo(it), true, 46f, 16);
            }
            else
            {
                Hint(box, "Ve al fotograma donde se pega, colócalo donde debe ir (en la mano, encima de otro…) y elige a qué objeto se pega y, si tiene huesos, a qué hueso. Desde ahí lo sigue, con su animación, hasta que lo sueltes en otro fotograma.");
                var g = AtlasTheme.Grid(box, 2, InnerWidth, 38f);
                foreach (var m0 in models)
                {
                    var m = m0;
                    AtlasTheme.Chip(g, m.gameObject.name, () => { grabModel = grabModel == m ? null : m; grabFilter = ""; RefreshProps(); }, grabModel == m);
                }
                if (grabModel != null && models.Contains(grabModel) && !grabModel.HasBones)
                {
                    var model = grabModel;
                    AtlasTheme.TextButton(box, "add", "Pegar a «" + model.gameObject.name + "»", () => Grab(it, model, ""), true, 42f, 15);
                }
                else if (grabModel != null && models.Contains(grabModel))
                {
                    var field = AtlasTheme.TextField(box, grabFilter, v => { grabFilter = v.Trim().ToLowerInvariant(); RefreshProps(); }, 40f);
                    var ph = AtlasTheme.Label(field.transform, "Buscar hueso (mano, boca…) y pulsa Intro", 14, AtlasTheme.TextDim);
                    AtlasTheme.Fill(ph.rectTransform, 12f, 12f);
                    field.placeholder = ph;
                    var model = grabModel;
                    AtlasTheme.TextButton(box, "add", "Pegar a todo «" + model.gameObject.name + "»", () => Grab(it, model, ""), true, 42f, 15);
                    var bl = AtlasTheme.Grid(box, 2, InnerWidth, 36f);
                    int shown = 0;
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
                    string txt = a.target == "" ? "◆ " + a.frame + ": suelto" : "◆ " + a.frame + ": pegado a " + (m != null ? m.gameObject.name : "?") + " · " + GripName(a.bone);
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
