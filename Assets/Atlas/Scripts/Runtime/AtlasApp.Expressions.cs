// ATLAS 3D (Unity) - «Expresiones»: a face made with the shape sliders
// (smile, blink, mouths…) and the face's bones (jaw, eyes, brows) is saved
// with a name and put back with one tap. They are kept per model, outside
// the project: the same character in another project has them too.
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Atlas
{
    [System.Serializable]
    public class Expression
    {
        public string name = "";
        public List<MorphValue> morphs = new List<MorphValue>();
        public List<BonePose> bones = new List<BonePose>();
    }

    [System.Serializable]
    public class ExpressionSet
    {
        public List<Expression> list = new List<Expression>();
    }

    public partial class AtlasApp
    {
        static string ExpressionsFile(AtlasItem it)
        {
            string key = Path.GetFileNameWithoutExtension(it.modelFile ?? "").ToLowerInvariant();
            if (key == "") key = "modelo";
            var sb = new System.Text.StringBuilder();
            foreach (char c in key) sb.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '_');
            string dir = Path.Combine(Application.persistentDataPath, "Expresiones");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, sb + ".json");
        }

        static ExpressionSet LoadExpressions(AtlasItem it)
        {
            try
            {
                string f = ExpressionsFile(it);
                if (File.Exists(f)) return JsonUtility.FromJson<ExpressionSet>(File.ReadAllText(f)) ?? new ExpressionSet();
            }
            catch (System.Exception) { }
            return new ExpressionSet();
        }

        static void SaveExpressions(AtlasItem it, ExpressionSet set)
        {
            try { File.WriteAllText(ExpressionsFile(it), JsonUtility.ToJson(set)); }
            catch (System.Exception) { }
        }

        /// The bones of the face: everything under the head (jaw, eyes,
        /// brows, lids, tongue…), not the head itself.
        static List<string> FaceBones(AtlasItem it)
        {
            var l = new List<string>();
            string head = "";
            foreach (var p in it.BoneOrder)
                if (BoneNames.Display(BoneNames.Leaf(p)).StartsWith("Cabeza")) { head = p; break; }
            if (head == "") return l;
            foreach (var p in it.BoneOrder)
                if (p != head && p.StartsWith(head + "/") && !IkRig.IsControl(p)) l.Add(p);
            return l;
        }

        void SaveExpression(AtlasItem it)
        {
            AtlasTheme.Dialog(canvasRoot, "Guardar expresión", "Nombre de esta cara (feliz, enfadado, sorpresa…):", "", "Guardar", false, name =>
            {
                name = (name ?? "").Trim();
                if (name == "") name = "Expresión";
                var e = new Expression { name = name };
                foreach (var m in it.morphs) if (Mathf.Abs(m.w) > 0.01f) e.morphs.Add(m.Copy());
                foreach (var p in FaceBones(it)) { var t = it.Bone(p); if (t != null) e.bones.Add(BonePose.From(p, t)); }
                if (e.morphs.Count == 0 && e.bones.Count == 0) { Toast("Mueve antes los deslizadores de la cara (o los huesos de la cara) y guárdala", true); return; }
                var set = LoadExpressions(it);
                set.list.RemoveAll(x => x.name == name);
                set.list.Add(e);
                SaveExpressions(it, set);
                RefreshProps();
                Toast("Expresión «" + name + "» guardada (también para este modelo en otros proyectos)");
            });
            toastHolder.SetAsLastSibling();
        }

        void ApplyExpression(AtlasItem it, Expression e)
        {
            // The face changes to that one: shapes not in it go back to 0.
            var want = new Dictionary<string, float>();
            foreach (var m in e.morphs) want[m.name] = m.w;
            foreach (var n in it.MorphNames)
            {
                float w;
                if (!want.TryGetValue(n, out w)) w = 0f;
                if (Mathf.Abs(it.MorphValueOf(n) - w) > 0.001f || want.ContainsKey(n)) it.SetMorph(n, w);
            }
            var keyed = new List<string>();
            foreach (var b in e.bones)
            {
                var t = it.Bone(b.path);
                if (t == null) continue;
                b.ApplyTo(t);
                it.RecordBone(b.path);
                keyed.Add(b.path);
            }
            if (autoKey || it.Animated) it.SetKey(Mathf.RoundToInt(frame), AtlasItem.ChMorph, keyed);
            Commit(true);
            RefreshKeys();
            RefreshProps();
            Toast("Expresión «" + e.name + "»" + (autoKey || it.Animated ? " con clave en el fotograma " + Mathf.RoundToInt(frame) : ""));
        }

        void BuildExpressionsSection(AtlasItem it)
        {
            bool shapes = it.MorphNames.Count > 0, faceBones = FaceBones(it).Count > 0;
            if (!shapes && !faceBones) return;
            var box = Sec("Expresiones guardadas", "person", false);
            var set = LoadExpressions(it);
            Hint(box, set.list.Count == 0
                ? "Pon la cara como quieras (deslizadores de formas o huesos de la cara en modo pose) y guárdala con un nombre. Luego, un toque la pone otra vez; con claves, en el fotograma actual."
                : "Toca una para ponerla" + (autoKey || it.Animated ? " (guarda clave en el fotograma actual)" : "") + ". Con «Borrar…» quitas alguna.");
            if (set.list.Count > 0)
            {
                var g = AtlasTheme.Grid(box, 2, InnerWidth, 40f);
                foreach (var e0 in set.list)
                {
                    var e = e0;
                    AtlasTheme.Chip(g, e.name, () => ApplyExpression(it, e));
                }
            }
            var row = AtlasTheme.Grid(box, 2, InnerWidth, 42f);
            AtlasTheme.TextButton(row, "save", "Guardar expresión…", () => SaveExpression(it), true, 42f, 14);
            if (set.list.Count > 0)
                AtlasTheme.TextButton(row, "delete", "Borrar…", () =>
                {
                    var l = new List<AtlasTheme.MenuItem> { AtlasTheme.MenuItem.Header("Borrar expresión") };
                    foreach (var e0 in set.list)
                    {
                        var e = e0;
                        l.Add(new AtlasTheme.MenuItem("delete", e.name, () =>
                        {
                            var s2 = LoadExpressions(it);
                            s2.list.RemoveAll(x => x.name == e.name);
                            SaveExpressions(it, s2);
                            RefreshProps();
                        }));
                    }
                    AtlasTheme.Popup(canvasRoot, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), l);
                    toastHolder.SetAsLastSibling();
                }, false, 42f, 14);
        }
    }
}
