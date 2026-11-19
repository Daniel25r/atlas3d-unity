// ATLAS 3D (Unity) - more for animating characters:
//   Rebote         hair, tails, ears, cables, breasts, cloth bones that swing
//                  by themselves with the body (a spring simulated along the
//                  timeline and stored as keys, so it can still be edited).
//   Copiar animación  the animation of one character onto another (Mixamo
//                  on a Source model, a model's clip on a Prisma one…):
//                  bones matched by their names, turned the same way from
//                  each one's rest pose, stored as keys.
//   Mirar a        a bone (head, eyes, a turret…) always points at another
//                  object, also while everything is animated.
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    [System.Serializable]
    public class LookAtBone
    {
        public string bone = "", target = "";
        public float weight = 1f;
        public Vector3 aim = Vector3.forward;   // the bone's «front», in its own space
        [System.NonSerialized] public Quaternion lastBase, lastWritten;
        [System.NonSerialized] public bool written;

        public LookAtBone Copy() { var c = (LookAtBone)MemberwiseClone(); c.written = false; return c; }

        public static List<LookAtBone> CopyList(List<LookAtBone> l)
        {
            var r = new List<LookAtBone>();
            if (l != null) foreach (var x in l) if (x != null) r.Add(x.Copy());
            return r;
        }
    }

    public partial class AtlasApp
    {
        float springStiff = 0.25f, springDamp = 0.2f, springGravity = 0.3f;
        AtlasItem retargetFrom;

        // ------------------------------------------------------------------
        // Rebote (spring bones)

        /// The bone and every bone under it (a tail, a lock of hair…).
        List<string> ChainFrom(AtlasItem it, string start)
        {
            var l = new List<string>();
            if (it.Bone(start) == null) return l;
            l.Add(start);
            for (int i = 0; i < l.Count && l.Count < 64; i++)
                foreach (var p in it.BoneOrder)
                    if (!IkRig.IsControl(p) && !l.Contains(p) && it.ParentBone(p) == l[i]) l.Add(p);
            return l;
        }

        void BakeSpring(AtlasItem it, string start)
        {
            var chain = ChainFrom(it, start);
            if (chain.Count == 0) { Toast("Elige primero el hueso donde empieza (la base de la cola, del pelo…)", true); return; }
            int from = 0, to = Mathf.Max(1, endFrame);
            float keepFrame = frame;
            // The old swing of these bones goes away (it is made again).
            var set = new HashSet<string>(chain);
            foreach (var k in it.keys) if (k.bones != null) k.bones.RemoveAll(b => set.Contains(b.path));
            it.keys.RemoveAll(k => k.Empty);

            var bones = new List<Transform>();
            var tailLocal = new List<Vector3>();
            var len = new List<float>();
            it.Evaluate(from, fps);
            foreach (var p in chain)
            {
                var t = it.Bone(p);
                bones.Add(t);
                // Where the bone ends: its children (or the same length as its parent).
                Vector3 sum = Vector3.zero;
                int n = 0;
                foreach (var q in chain)
                    if (it.ParentBone(q) == p) { var c = it.Bone(q); if (c != null) { sum += c.position; n++; } }
                Vector3 tail;
                if (n > 0) tail = sum / n;
                else
                {
                    var par = it.Bone(it.ParentBone(p));
                    Vector3 d = par != null ? t.position - par.position : t.up * 0.1f;
                    if (d.sqrMagnitude < 1e-8f) d = t.up * 0.05f;
                    tail = t.position + d * 0.6f;
                }
                Vector3 local = Quaternion.Inverse(t.rotation) * (tail - t.position);
                if (local.sqrMagnitude < 1e-10f) local = Vector3.up * 0.02f;
                tailLocal.Add(local);
                len.Add(local.magnitude);
            }
            // The animation of these bones, frame by frame, before the swing.
            var anim = new Quaternion[to - from + 1][];
            for (int f = from; f <= to; f++)
            {
                it.Evaluate(f, fps);
                if (it.ik.Count > 0) IkRig.Solve(it);
                var row = new Quaternion[bones.Count];
                for (int i = 0; i < bones.Count; i++) row[i] = bones[i] != null ? bones[i].localRotation : Quaternion.identity;
                anim[f - from] = row;
            }
            var pos = new Vector3[bones.Count];
            var prev = new Vector3[bones.Count];
            float scale = Mathf.Max(0.01f, it.transform.lossyScale.y);
            Vector3 g = Vector3.down * 9.8f * springGravity * scale;
            float dt = 1f / Mathf.Max(1f, fps);
            const int sub = 3;
            float h = dt / sub;
            float stiff = Mathf.Clamp(springStiff, 0.01f, 1f), damp = Mathf.Clamp01(springDamp);
            for (int f = from; f <= to; f++)
            {
                it.Evaluate(f, fps);
                if (it.ik.Count > 0) IkRig.Solve(it);
                for (int i = 0; i < bones.Count; i++) if (bones[i] != null) bones[i].localRotation = anim[f - from][i];
                for (int s = 0; s < sub; s++)
                    for (int i = 0; i < bones.Count; i++)
                    {
                        var t = bones[i];
                        if (t == null) continue;
                        Vector3 head = t.position;
                        // Where the animation (and the parent's swing) puts the tip.
                        Vector3 target = head + t.rotation * tailLocal[i];
                        if (f == from && s == 0) { pos[i] = prev[i] = target; continue; }
                        Vector3 vel = (pos[i] - prev[i]) * (1f - damp);
                        Vector3 next = pos[i] + vel + (target - pos[i]) * stiff + g * h * h;
                        // The bone keeps its length.
                        Vector3 dir = next - head;
                        next = dir.sqrMagnitude > 1e-12f ? head + dir.normalized * len[i] : target;
                        prev[i] = pos[i];
                        pos[i] = next;
                        // Turned so its tip points there (only in the last
                        // step: the children start from it next frame).
                        if (s == sub - 1)
                        {
                            Vector3 cur = t.rotation * tailLocal[i];
                            t.rotation = Quaternion.FromToRotation(cur, pos[i] - head) * t.rotation;
                        }
                    }
                it.SetKey(f, 0, chain);
            }
            foreach (var p in chain) it.RecordBone(p);
            SetFrame(keepFrame);
            Commit(true);
            RefreshKeys();
            RefreshProps();
            Toast("Rebote generado en " + chain.Count + " huesos (" + (to - from + 1) + " fotogramas). Se puede deshacer o volver a generar con otros valores");
        }

        void BuildSpringSection(AtlasItem it, Transform box)
        {
            var t = AtlasTheme.Label(box, "Rebote (pelo, cola, orejas, cables…)", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t.gameObject, 26f);
            if (activeBone == "" || IkRig.IsControl(activeBone))
            {
                Hint(box, "Toca el primer hueso (la base de la cola, de un mechón, de una oreja) y vuelve aquí: ese hueso y los que cuelgan de él se balancean solos con el movimiento del cuerpo.");
                return;
            }
            int n = ChainFrom(it, activeBone).Count;
            Hint(box, "Desde «" + BoneNames.Display(BoneNames.Leaf(activeBone)) + "»: " + n + " huesos. Anima primero el cuerpo y luego genera el rebote.");
            AtlasTheme.SliderRow(box, "Rigidez", 0.02f, 1f, springStiff, "0.00", x => springStiff = x, null);
            AtlasTheme.SliderRow(box, "Frenado", 0f, 0.9f, springDamp, "0.00", x => springDamp = x, null);
            AtlasTheme.SliderRow(box, "Peso (gravedad)", 0f, 1f, springGravity, "0.00", x => springGravity = x, null);
            AtlasTheme.TextButton(box, "sparkle", "Generar rebote", () => BakeSpring(it, activeBone), true, 44f, 15);
        }

        // ------------------------------------------------------------------
        // Copiar animación de otro personaje (retarget)

        /// Each bone's rest pose (as skinned, before any animation), in the
        /// item's own space: rotation and position.
        static Dictionary<Transform, Matrix4x4> BindPoses(AtlasItem it)
        {
            var d = new Dictionary<Transform, Matrix4x4>();
            var toItem = it.transform.worldToLocalMatrix;
            foreach (var r in it.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = r.sharedMesh;
                if (m == null || r.bones == null) continue;
                var bp = m.bindposes;
                var l2w = r.transform.localToWorldMatrix;
                for (int i = 0; i < r.bones.Length && i < bp.Length; i++)
                {
                    var b = r.bones[i];
                    if (b == null || d.ContainsKey(b)) continue;
                    d[b] = toItem * l2w * bp[i].inverse;
                }
            }
            return d;
        }

        static Quaternion RotOf(Matrix4x4 m)
        {
            Vector3 f = m.GetColumn(2), u = m.GetColumn(1);
            if (f.sqrMagnitude < 1e-12f || u.sqrMagnitude < 1e-12f) return Quaternion.identity;
            return Quaternion.LookRotation(f, u);
        }

        /// Bones by what they are (left hand, hips, spine 2…): the name
        /// shown in ATLAS, which already understands Mixamo, Source,
        /// Blender, Unreal, Rigify… names.
        static Dictionary<string, string> BoneKinds(AtlasItem it)
        {
            var d = new Dictionary<string, string>();
            var dup = new HashSet<string>();
            foreach (var p in it.BoneOrder)
            {
                if (IkRig.IsControl(p)) continue;
                string k = BoneNames.Display(BoneNames.Leaf(p)).ToLowerInvariant();
                if (k == "" ) continue;
                if (d.ContainsKey(k)) { dup.Add(k); continue; }
                d[k] = p;
            }
            foreach (var k in dup) d.Remove(k);
            return d;
        }

        void Retarget(AtlasItem src, AtlasItem dst)
        {
            if (src == null || dst == null || src == dst) return;
            var ka = BoneKinds(src);
            var kb = BoneKinds(dst);
            var bindA = BindPoses(src);
            var bindB = BindPoses(dst);
            // Pairs, in the destination's skeleton order (parents first).
            var pairs = new List<KeyValuePair<Transform, Transform>>();
            var paths = new List<string>();
            string hipKey = null;
            foreach (var p in dst.BoneOrder)
            {
                if (IkRig.IsControl(p)) continue;
                string k = BoneNames.Display(BoneNames.Leaf(p)).ToLowerInvariant();
                string ap;
                if (!kb.ContainsKey(k) || kb[k] != p || !ka.TryGetValue(k, out ap)) continue;
                Transform ta = src.Bone(ap), tb = dst.Bone(p);
                if (ta == null || tb == null || !bindA.ContainsKey(ta) || !bindB.ContainsKey(tb)) continue;
                pairs.Add(new KeyValuePair<Transform, Transform>(ta, tb));
                paths.Add(p);
                if (hipKey == null && k.StartsWith("cadera")) hipKey = p;
            }
            if (pairs.Count < 4)
            {
                Toast("No se pudieron emparejar los huesos (" + pairs.Count + "). Funciona con esqueletos de personaje (cadera, columna, brazos, piernas…) con nombres reconocibles", true);
                return;
            }
            float keepFrame = frame;
            var set = new HashSet<string>(paths);
            foreach (var k in dst.keys) if (k.bones != null) k.bones.RemoveAll(b => set.Contains(b.path));
            dst.keys.RemoveAll(k => k.Empty);
            // The hips move as far as the legs are long.
            Transform hipA = null, hipB = null;
            if (hipKey != null) foreach (var pr in pairs) if (pr.Value == dst.Bone(hipKey)) { hipA = pr.Key; hipB = pr.Value; }
            float scale = 1f;
            Vector3 restA = Vector3.zero, restB = Vector3.zero;
            if (hipA != null)
            {
                restA = bindA[hipA].GetColumn(3);
                restB = bindB[hipB].GetColumn(3);
                scale = Mathf.Abs(restA.y) > 1e-4f ? restB.y / restA.y : 1f;
                if (scale <= 0f || float.IsNaN(scale)) scale = 1f;
            }
            int to = Mathf.Max(1, endFrame);
            for (int f = 0; f <= to; f++)
            {
                src.Evaluate(f, fps);
                if (src.ik.Count > 0) IkRig.Solve(src);
                foreach (var pr in pairs)
                {
                    Transform a = pr.Key, b = pr.Value;
                    Quaternion aNow = Quaternion.Inverse(src.transform.rotation) * a.rotation;
                    Quaternion delta = aNow * Quaternion.Inverse(RotOf(bindA[a]));
                    b.rotation = dst.transform.rotation * (delta * RotOf(bindB[b]));
                    if (a == hipA)
                    {
                        Vector3 now = src.transform.InverseTransformPoint(a.position);
                        Vector3 p = restB + (now - restA) * scale;
                        b.position = dst.transform.TransformPoint(p);
                    }
                }
                dst.SetKey(f, 0, paths);
            }
            foreach (var p in paths) dst.RecordBone(p);
            // The IK controls would pull the arms and legs back.
            int offIk = 0;
            foreach (var c in dst.ik) if (c.enabled) { c.enabled = false; offIk++; }
            SetFrame(keepFrame);
            Commit(true);
            RefreshKeys();
            RefreshProps();
            Toast("Animación de «" + src.gameObject.name + "» copiada: " + pairs.Count + " huesos, " + (to + 1) + " fotogramas" + (offIk > 0 ? ". El IK de este personaje se ha apagado para no deshacerla" : ""));
        }

        void BuildRetargetSection(AtlasItem it)
        {
            var others = new List<AtlasItem>();
            foreach (var o in items) if (o != null && o != it && o.kind == "model" && o.HasBones && (o.Animated || !string.IsNullOrEmpty(o.clip))) others.Add(o);
            var box = Sec("Copiar animación de otro personaje", "copy", false);
            if (others.Count == 0)
            {
                Hint(box, "Añade otro personaje animado (con una animación elegida o con claves) y aquí podrás pasar su animación a este, aunque sus huesos se llamen distinto.");
                return;
            }
            var g = AtlasTheme.Grid(box, 2, InnerWidth, 38f);
            foreach (var o0 in others)
            {
                var o = o0;
                AtlasTheme.Chip(g, o.gameObject.name, () => { retargetFrom = retargetFrom == o ? null : o; RefreshProps(); }, retargetFrom == o);
            }
            if (retargetFrom != null && others.Contains(retargetFrom))
            {
                Hint(box, "Los dos deben estar en una postura parecida de reposo (en T o en A). Se guardan claves en toda la línea de tiempo; se puede deshacer.");
                var src = retargetFrom;
                AtlasTheme.TextButton(box, "copy", "Copiar la animación de «" + src.gameObject.name + "»", () => Retarget(src, it), true, 44f, 15);
            }
        }

        // ------------------------------------------------------------------
        // Mirar a

        /// Points each «Mirar a» bone at its object (after the animation).
        /// Safe to call every frame: it starts again from the animated pose.
        readonly Dictionary<AtlasItem, Vector3> lookGoal = new Dictionary<AtlasItem, Vector3>();
        int lookGoalFrame = -1;

        void ApplyLookAts()
        {
            // A model's middle is measured once per frame, not per bone.
            if (lookGoalFrame != Time.frameCount) { lookGoal.Clear(); lookGoalFrame = Time.frameCount; }
            foreach (var it in items)
            {
                if (it == null || it.lookAts == null || it.lookAts.Count == 0) continue;
                foreach (var la in it.lookAts)
                {
                    var b = it.Bone(la.bone);
                    var tg = FindItem(la.target);
                    if (b == null || tg == null) continue;
                    // The animation's own rotation (not the one this wrote).
                    Quaternion baseRot = la.written && b.localRotation == la.lastWritten ? la.lastBase : b.localRotation;
                    b.localRotation = baseRot;
                    Vector3 goal;
                    if (tg.kind != "model") goal = tg.transform.position;
                    else if (!lookGoal.TryGetValue(tg, out goal)) { goal = VisualCenter(tg); lookGoal[tg] = goal; }
                    Vector3 cur = b.rotation * la.aim;
                    Vector3 want = goal - b.position;
                    if (want.sqrMagnitude > 1e-8f && cur.sqrMagnitude > 1e-8f)
                    {
                        var q = Quaternion.FromToRotation(cur, want);
                        b.rotation = Quaternion.Slerp(Quaternion.identity, q, Mathf.Clamp01(la.weight)) * b.rotation;
                    }
                    la.lastBase = baseRot;
                    la.lastWritten = b.localRotation;
                    la.written = true;
                }
            }
        }

        void BuildLookAtSection(AtlasItem it, Transform box)
        {
            var t = AtlasTheme.Label(box, "Mirar a (cabeza, ojos…)", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t.gameObject, 26f);
            if (activeBone == "" || IkRig.IsControl(activeBone))
            {
                Hint(box, "Toca la cabeza o un ojo y elige un objeto: ese hueso lo mirará siempre, aunque se mueva.");
                return;
            }
            LookAtBone cur = null;
            foreach (var la in it.lookAts) if (la.bone == activeBone) cur = la;
            string bn = BoneNames.Display(BoneNames.Leaf(activeBone));
            if (cur != null)
            {
                var tg = FindItem(cur.target);
                Hint(box, "«" + bn + "» mira a «" + (tg != null ? tg.gameObject.name : "?") + "».");
                var la0 = cur;
                AtlasTheme.TextButton(box, "undo", "Mira al revés: darle la vuelta", () =>
                {
                    var b = it.Bone(la0.bone);
                    if (b != null && la0.written) b.localRotation = la0.lastBase;
                    la0.written = false;
                    la0.aim = -la0.aim;
                    ApplyLookAts();
                    Commit(true);
                }, false, 40f, 14);
                AtlasTheme.SliderRow(box, "Fuerza", 0f, 1f, cur.weight, "0.00", x => { la0.weight = x; ApplyLookAts(); }, () => Commit(true));
                AtlasTheme.TextButton(box, "close", "Dejar de mirar", () =>
                {
                    var b = it.Bone(la0.bone);
                    if (b != null && la0.written) b.localRotation = la0.lastBase;
                    it.lookAts.Remove(la0);
                    Commit(true);
                    RefreshProps();
                }, false, 40f, 14);
                return;
            }
            var g = AtlasTheme.Grid(box, 2, InnerWidth, 38f);
            int shown = 0;
            foreach (var o0 in items)
            {
                var o = o0;
                if (o == null || o == it || o.kind == "fx" || shown >= 16) continue;
                shown++;
                AtlasTheme.Chip(g, o.gameObject.name, () =>
                {
                    var b = it.Bone(activeBone);
                    if (b == null) return;
                    // Its «front» is where the character faces now.
                    var la = new LookAtBone { bone = activeBone, target = o.id, aim = Quaternion.Inverse(b.rotation) * it.transform.forward };
                    it.lookAts.Add(la);
                    ApplyLookAts();
                    Commit(true);
                    RefreshProps();
                    Toast("«" + bn + "» mira ahora a «" + o.gameObject.name + "». Mueve ese objeto y lo sigue");
                });
            }
        }
    }
}

namespace Atlas
{
    public partial class AtlasApp
    {
        // ------------------------------------------------------------------
        // Simetría al posar (modo espejo): what is turned on one side is
        // turned the same way on the other (left ↔ right), like Blender's
        // X-Axis Mirror.

        bool poseMirror;
        AtlasItem mirrorItem;
        Dictionary<Transform, Matrix4x4> mirrorBind;

        static readonly string[][] SidePairs =
        {
            new[] { "Left", "Right" }, new[] { "left", "right" }, new[] { "LEFT", "RIGHT" },
            new[] { "_L", "_R" }, new[] { ".L", ".R" }, new[] { "-L", "-R" }, new[] { " L ", " R " },
            new[] { "_l", "_r" }, new[] { ".l", ".r" }, new[] { "L_", "R_" }, new[] { "l_", "r_" },
            new[] { "Izq", "Der" }, new[] { "izq", "der" }, new[] { "Izquierda", "Derecha" },
        };

        /// The same path with left and right swapped (one swap per part).
        static string MirrorPath(string path)
        {
            var parts = path.Split('/');
            bool any = false;
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i], o = SwapSide(s);
                if (o != s) { parts[i] = o; any = true; }
            }
            return any ? string.Join("/", parts) : "";
        }

        static string SwapSide(string s)
        {
            foreach (var p in SidePairs)
            {
                for (int k = 0; k < 2; k++)
                {
                    string a = p[k], b = p[1 - k];
                    int i = s.IndexOf(a, System.StringComparison.Ordinal);
                    if (i < 0) continue;
                    // «_L» only at the end or before a separator (not «_Leg»).
                    if (a.Length == 2 && (a[0] == '_' || a[0] == '.' || a[0] == '-'))
                    {
                        int end = i + 2;
                        if (end < s.Length && char.IsLetterOrDigit(s[end])) continue;
                    }
                    if (a.Length == 2 && (a[1] == '_') && i > 0 && char.IsLetterOrDigit(s[i - 1])) continue;
                    return s.Substring(0, i) + b + s.Substring(i + a.Length);
                }
            }
            // Bare «L» / «R» as a whole word: «Bip01 L Thigh».
            var w = s.Split(' ');
            for (int i = 0; i < w.Length; i++)
                if (w[i] == "L" || w[i] == "R") { w[i] = w[i] == "L" ? "R" : "L"; return string.Join(" ", w); }
            return s;
        }

        /// Turns the other side's bone like `path` (mirrored across the
        /// character's middle). Returns the other bone's path, or "".
        string MirrorPose(AtlasItem it, string path)
        {
            if (!poseMirror || it == null || string.IsNullOrEmpty(path) || IkRig.IsControl(path)) return "";
            string other = MirrorPath(path);
            if (other == "" || other == path) return "";
            Transform a = it.Bone(path), b = it.Bone(other);
            if (a == null || b == null) return "";
            if (mirrorItem != it || mirrorBind == null) { mirrorItem = it; mirrorBind = BindPoses(it); }
            Matrix4x4 ra, rb;
            if (!mirrorBind.TryGetValue(a, out ra) || !mirrorBind.TryGetValue(b, out rb))
            {
                // The model was loaded again (new bones): measure once more.
                mirrorBind = BindPoses(it);
                if (!mirrorBind.TryGetValue(a, out ra) || !mirrorBind.TryGetValue(b, out rb)) return "";
            }
            Quaternion now = Quaternion.Inverse(it.transform.rotation) * a.rotation;
            Quaternion delta = now * Quaternion.Inverse(RotOf(ra));
            // Mirror across the YZ plane (x ↔ -x) of the character.
            var md = new Quaternion(delta.x, -delta.y, -delta.z, delta.w);
            b.rotation = it.transform.rotation * (md * RotOf(rb));
            return other;
        }
    }
}
