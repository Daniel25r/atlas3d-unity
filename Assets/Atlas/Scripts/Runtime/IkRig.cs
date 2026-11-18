// ATLAS 3D (Unity) - IK rig, like the Godot version: a control on each hand
// and foot (any model: human names, Mixamo, Rigify, Unreal… or, without
// names, the longest limbs). Move a control and the arm or leg follows,
// with the elbow / knee bending the natural way; turn it and the hand or
// foot turns. Controls are keyed like bones, so feet stay planted while the
// body moves.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Atlas
{
    [Serializable]
    public class IkChain
    {
        public string name = "";              // arm_l, arm_r, leg_l, leg_r, limb_1…
        public string root = "", mid = "", tip = "";   // bone paths
        public bool enabled = true;
        public Vector3 pole;                  // bend direction in the root's parent space
        public Quaternion tipOffset = Quaternion.identity;   // tip rotation in the control

        public IkChain Copy() { return (IkChain)MemberwiseClone(); }
    }

    public static class IkRig
    {
        public const string Prefix = "ATLAS_IK_";

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "arm_l", "Mano izq." }, { "arm_r", "Mano der." }, { "leg_l", "Pie izq." }, { "leg_r", "Pie der." },
        };
        static readonly string[] Skip = { "twist", "deform", "offset", "end", "roll", "weapon", "ik", "pole", "target", "helper", "socket", "nub", "metacarpal", "carpal", "toe", "ball", "thumb", "index", "middle", "ring", "pinky", "finger" };

        public static bool IsControl(string path) { return path != null && path.StartsWith(Prefix); }

        public static string Label(string chain)
        {
            string n;
            if (Names.TryGetValue(chain, out n)) return "IK " + Lang.T(n);
            if (chain.StartsWith("limb_")) return "IK " + Lang.T("Extremidad") + " " + chain.Substring(5);
            return "IK " + chain;
        }

        /// «ATLAS_IK_arm_l» → «IK Mano izq.»; other names → "".
        public static string Display(string leaf)
        {
            return IsControl(leaf) ? Label(leaf.Substring(Prefix.Length)) : "";
        }

        public static List<IkChain> Copy(List<IkChain> l)
        {
            var r = new List<IkChain>();
            if (l != null) foreach (var c in l) if (c != null) r.Add(c.Copy());
            return r;
        }

        static string[] Tokens(string bone)
        {
            string s = Regex.Replace(bone, "([a-z])([A-Z])", "$1 $2");
            s = Regex.Replace(s, "([A-Za-z])([0-9])|([0-9])([A-Za-z])", "$1$3 $2$4");
            s = Regex.Replace(s, "[^A-Za-z0-9]+", " ");
            return s.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        static string Side(string[] t)
        {
            foreach (var w in t) if (w == "l" || w == "left" || w == "lft" || w == "izq" || w == "izquierda") return "l";
            foreach (var w in t) if (w == "r" || w == "right" || w == "rgt" || w == "der" || w == "derecha") return "r";
            return "";
        }

        static bool Has(string[] t, string w) { return Array.IndexOf(t, w) >= 0; }

        static bool Skipped(string[] t)
        {
            foreach (var s in Skip) if (Has(t, s)) return true;
            return false;
        }

        /// Hands and feet by name; without names, the longest limbs.
        public static List<IkChain> Detect(AtlasItem it)
        {
            var found = new Dictionary<string, IkChain>();
            foreach (var path in it.BoneOrder)
            {
                if (IsControl(path)) continue;
                string leaf = BoneNames.Leaf(path);
                var t = Tokens(leaf);
                if (Skipped(t)) continue;
                string kind = "";
                if (Has(t, "hand") || Has(t, "wrist") || Has(t, "mano") || Has(t, "muneca")) kind = "arm";
                else if (Has(t, "foot") || Has(t, "ankle") || Has(t, "pie") || Has(t, "tobillo")) kind = "leg";
                if (kind == "") continue;
                string side = Side(t);
                if (side == "") continue;
                string key = kind + "_" + side;
                if (found.ContainsKey(key)) continue;
                var c = Chain(it, path, key);
                if (c != null) found[key] = c;
            }
            var r = new List<IkChain>(found.Values);
            if (r.Count >= 2) return r;
            return Limbs(it);
        }

        /// The joint above: twist / roll / helper bones in between are
        /// skipped (Source and game rigs have them between knee and ankle).
        static string Joint(AtlasItem it, string bone)
        {
            string p = it.ParentBone(bone);
            for (int i = 0; i < 4 && p != ""; i++)
            {
                string leaf = BoneNames.Leaf(p);
                var t = Tokens(leaf);
                bool helper = Has(t, "twist") || Has(t, "roll") || Has(t, "deform") || Has(t, "offset") || Has(t, "helper")
                    || Has(t, "ik") || Has(t, "pole") || Has(t, "target") || BoneNames.IsHelper(leaf);
                if (!helper) break;
                p = it.ParentBone(p);
            }
            return p ?? "";
        }

        static IkChain Chain(AtlasItem it, string tip, string name)
        {
            string mid = Joint(it, tip);
            if (mid == "") return null;
            string root = Joint(it, mid);
            if (root == "") return null;
            Transform r = it.Bone(root), m = it.Bone(mid), t = it.Bone(tip);
            if (r == null || m == null || t == null) return null;
            // A tiny piece just above the ankle / wrist is not the shin:
            // one more joint up.
            float upper = Vector3.Distance(r.position, m.position), lower = Vector3.Distance(m.position, t.position);
            if (lower < upper * 0.3f)
            {
                string up = Joint(it, root);
                var u = up != "" ? it.Bone(up) : null;
                if (u != null && Vector3.Distance(u.position, r.position) > 1e-4f)
                {
                    mid = root; root = up;
                    m = r; r = u;
                }
            }
            if (Vector3.Distance(r.position, m.position) < 1e-4f || Vector3.Distance(m.position, t.position) < 1e-4f) return null;
            return new IkChain { name = name, root = root, mid = mid, tip = tip };
        }

        /// Models with unknown bone names: the bone chains that reach
        /// farthest from the body (legs, arms, tentacles…), up to four.
        static List<IkChain> Limbs(AtlasItem it)
        {
            var r = new List<IkChain>();
            var kids = new HashSet<string>();
            foreach (var p in it.BoneOrder) { string pp = it.ParentBone(p); if (pp != "") kids.Add(pp); }
            Transform first = it.BoneOrder.Count > 0 ? it.Bone(it.BoneOrder[0]) : null;
            if (first == null) return r;
            var cands = new List<KeyValuePair<float, IkChain>>();
            foreach (var p in it.BoneOrder)
            {
                if (IsControl(p) || kids.Contains(p)) continue;
                string leaf = BoneNames.Leaf(p);
                if (BoneNames.IsSecondary(leaf)) continue;
                // End bones that only mark the tip: use their parent.
                string tip = p;
                string par = it.ParentBone(p);
                if (BoneNames.IsHelper(leaf) && par != "") tip = par;
                var c = Chain(it, tip, "");
                if (c == null) continue;
                float d = Vector3.Distance(it.Bone(tip).position, first.position);
                cands.Add(new KeyValuePair<float, IkChain>(d, c));
            }
            cands.Sort((a, b) => b.Key.CompareTo(a.Key));
            var usedRoots = new HashSet<string>();
            foreach (var kv in cands)
            {
                if (r.Count >= 4) break;
                if (usedRoots.Contains(kv.Value.root) || usedRoots.Contains(kv.Value.mid)) continue;
                usedRoots.Add(kv.Value.root);
                usedRoots.Add(kv.Value.mid);
                kv.Value.name = "limb_" + (r.Count + 1);
                r.Add(kv.Value);
            }
            return r;
        }

        /// Creates the controls where the hands and feet are now. Returns
        /// how many limbs got one.
        public static int Create(AtlasItem it)
        {
            Remove(it);
            var chains = Detect(it);
            foreach (var c in chains)
            {
                Transform r = it.Bone(c.root), m = it.Bone(c.mid), t = it.Bone(c.tip);
                Vector3 axis = (t.position - r.position).normalized;
                Vector3 bend = (m.position - r.position) - axis * Vector3.Dot(m.position - r.position, axis);
                if (bend.magnitude < 1e-3f * Mathf.Max(1e-4f, (t.position - r.position).magnitude))
                {
                    // Straight limb: knees forward, elbows back.
                    bend = c.name.StartsWith("leg") ? it.transform.forward : -it.transform.forward;
                    bend -= axis * Vector3.Dot(bend, axis);
                }
                var space = r.parent != null ? r.parent : it.transform;
                c.pole = space.InverseTransformDirection(bend.normalized);
                it.ik.Add(c);
            }
            Sync(it);
            return chains.Count;
        }

        public static void Remove(AtlasItem it)
        {
            foreach (var c in it.ik) Destroy(it, c.name);
            it.ik.Clear();
        }

        static void Destroy(AtlasItem it, string chain)
        {
            var go = it.transform.Find(Prefix + chain);
            it.RemoveExtraBone(Prefix + chain);
            if (go != null)
            {
                go.name = "_";
                UnityEngine.Object.Destroy(go.gameObject);
            }
        }

        /// Makes the controls match the item's chains (after loading, undo…).
        public static void Sync(AtlasItem it)
        {
            var want = new HashSet<string>();
            foreach (var c in it.ik) want.Add(Prefix + c.name);
            for (int i = it.transform.childCount - 1; i >= 0; i--)
            {
                var ch = it.transform.GetChild(i);
                if (ch.name.StartsWith(Prefix) && !want.Contains(ch.name))
                {
                    it.RemoveExtraBone(ch.name);
                    UnityEngine.Object.Destroy(ch.gameObject);
                    ch.name = "_";
                }
            }
            foreach (var c in it.ik)
            {
                var tip = it.Bone(c.tip);
                if (tip == null) continue;
                var ctl = it.transform.Find(Prefix + c.name);
                if (ctl == null)
                {
                    ctl = new GameObject(Prefix + c.name).transform;
                    ctl.SetParent(it.transform, false);
                    ctl.position = tip.position;
                    ctl.rotation = tip.rotation;
                    c.tipOffset = Quaternion.identity;
                    it.AddExtraBone(ctl);
                    // An earlier pose of this control (saved projects).
                    var bp = it.FindPose(Prefix + c.name);
                    if (bp != null) bp.ApplyTo(ctl);
                }
                else if (it.Bone(Prefix + c.name) == null) it.AddExtraBone(ctl);
            }
        }

        /// Turns each enabled limb so its hand / foot reaches its control.
        public static void Solve(AtlasItem it)
        {
            foreach (var c in it.ik)
            {
                if (!c.enabled) continue;
                Transform r = it.Bone(c.root), m = it.Bone(c.mid), t = it.Bone(c.tip), ctl = it.Bone(Prefix + c.name);
                if (r == null || m == null || t == null || ctl == null) continue;
                SolveTwo(r, m, t, ctl.position, (r.parent != null ? r.parent : it.transform).TransformDirection(c.pole));
                t.rotation = ctl.rotation * c.tipOffset;
            }
        }

        static void SolveTwo(Transform root, Transform mid, Transform tip, Vector3 target, Vector3 poleDir)
        {
            Vector3 pr = root.position, pm = mid.position, pt = tip.position;
            float a = Vector3.Distance(pr, pm), b = Vector3.Distance(pm, pt);
            if (a < 1e-6f || b < 1e-6f) return;
            Vector3 toT = target - pr;
            Vector3 dir = toT.magnitude > 1e-6f ? toT.normalized : (pt - pr).normalized;
            float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(a - b) + 1e-4f, a + b - 1e-4f);
            Vector3 bend = poleDir - dir * Vector3.Dot(poleDir, dir);
            if (bend.sqrMagnitude < 1e-10f)
            {
                Vector3 cur = pm - pr;
                bend = cur - dir * Vector3.Dot(cur, dir);
            }
            if (bend.sqrMagnitude < 1e-10f) bend = Vector3.Cross(dir, Mathf.Abs(dir.x) < 0.9f ? Vector3.right : Vector3.up);
            bend.Normalize();
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
            Vector3 newMid = pr + (dir * cosA + bend * sinA) * a;
            root.rotation = Quaternion.FromToRotation(pm - pr, newMid - pr) * root.rotation;
            pm = mid.position;
            pt = tip.position;
            Vector3 goal = pr + dir * d;
            mid.rotation = Quaternion.FromToRotation(pt - pm, goal - pm) * mid.rotation;
        }
    }
}
