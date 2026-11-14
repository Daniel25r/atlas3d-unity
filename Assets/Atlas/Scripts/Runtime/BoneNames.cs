// ATLAS 3D (Unity) - readable bone names, the same rules as the Godot
// version: «mixamorig:LeftForeArm» → «Antebrazo izq.», «RightHandIndex2» →
// «Índice 2 der.»; unknown names are shown as they are.
using System.Collections.Generic;
using System.Text;

namespace Atlas
{
    public static class BoneNames
    {
        static readonly Dictionary<string, string> Words = new Dictionary<string, string>
        {
            { "hand", "Mano" }, { "upperarm", "Brazo" }, { "arm", "Brazo" }, { "lowerarm", "Antebrazo" }, { "forearm", "Antebrazo" },
            { "thigh", "Muslo" }, { "upleg", "Muslo" }, { "calf", "Pierna" }, { "shin", "Pierna" }, { "leg", "Pierna" }, { "foot", "Pie" },
            { "toe", "Dedo del pie" }, { "toebase", "Dedo del pie" }, { "ball", "Punta del pie" }, { "head", "Cabeza" }, { "neck", "Cuello" },
            { "spine", "Columna" }, { "chest", "Pecho" }, { "pelvis", "Cadera" }, { "hips", "Cadera" }, { "hip", "Cadera" },
            { "clavicle", "Clavícula" }, { "shoulder", "Hombro" }, { "thumb", "Pulgar" }, { "index", "Índice" }, { "middle", "Medio" },
            { "ring", "Anular" }, { "pinky", "Meñique" }, { "little", "Meñique" }, { "eye", "Ojo" }, { "jaw", "Mandíbula" }, { "ear", "Oreja" },
            { "tail", "Cola" }, { "root", "Raíz" }, { "wing", "Ala" }, { "finger", "Dedo" }, { "eyelid", "Párpado" }, { "eyebrow", "Ceja" },
        };
        static readonly HashSet<string> Fingers = new HashSet<string> { "thumb", "index", "middle", "ring", "pinky", "little" };
        static readonly Dictionary<string, string> Sides = new Dictionary<string, string>
        {
            { "l", "izq." }, { "left", "izq." }, { "r", "der." }, { "right", "der." },
        };
        static readonly string[] Prefixes = { "mixamorig:", "mixamorig_", "bip01_", "bip01 ", "bip_", "def-", "def_", "org-", "valvebiped." };

        /// Helper bones (ends, IK targets, attachments) are hidden.
        public static bool IsHelper(string bone)
        {
            string n = bone.ToLowerInvariant();
            return n.Contains("_end") || n.EndsWith("end") || n.StartsWith("ik_") || n.Contains("attach") || n.Contains("weapon");
        }

        static bool IsInt(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        public static string Display(string bone)
        {
            if (string.IsNullOrEmpty(bone)) return "";
            string s = bone;
            foreach (var p in Prefixes)
                if (s.ToLowerInvariant().StartsWith(p)) s = s.Substring(p.Length);
            if (s.Contains(".") && !s.StartsWith(".")) s = s.Substring(0, s.IndexOf('.'));
            int u = s.LastIndexOf('_');
            if (u > 0 && IsInt(s.Substring(u + 1)) && s.Length - u - 1 >= 3) s = s.Substring(0, u);
            var spaced = new StringBuilder();
            for (int k = 0; k < s.Length; k++)
            {
                char c = s[k];
                if (k > 0 && char.IsUpper(c) && !char.IsUpper(s[k - 1]) && "_ -.".IndexOf(s[k - 1]) < 0) spaced.Append(' ');
                spaced.Append(c);
            }
            var words = new List<string>();
            foreach (var w in spaced.ToString().Replace('_', ' ').Replace('-', ' ').Replace('.', ' ').ToLowerInvariant().Split(' '))
                if (w.Length > 0) words.Add(w);
            string part = "", side = "", num = "";
            foreach (var w in words)
            {
                string stem = w.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                string digits = w.Substring(stem.Length);
                if (Sides.ContainsKey(w)) side = Sides[w];
                else if (IsInt(w)) num = w;
                else if (Words.ContainsKey(stem) && (part == "" || Fingers.Contains(stem)))
                {
                    part = Words[stem];
                    if (digits != "") num = digits;
                }
            }
            if (words.Contains("fore") || (words.Contains("lower") && words.Contains("arm"))) part = "Antebrazo";
            else if (words.Contains("up") && words.Contains("leg")) part = "Muslo";
            if (part == "") return s;
            string outp = part;
            if (num != "") outp += " " + num;
            if (side != "") outp += " " + side;
            return outp;
        }

        /// Last part of a bone path («Armature/Hips/Spine» → «Spine»).
        public static string Leaf(string path)
        {
            int i = path.LastIndexOf('/');
            return i >= 0 ? path.Substring(i + 1) : path;
        }
    }
}
