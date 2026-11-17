// ATLAS 3D (Unity) - several objects at once: multiple selection (Ctrl or
// Shift + click on PC, the «varios» button on the phone), groups that are
// always selected together, and moving / turning / scaling them all with
// the manipulator. Each object keeps its own animation; the move becomes a
// key on each of them like on the main one.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        /// Selected together with `selected` (not including it).
        readonly List<AtlasItem> multi = new List<AtlasItem>();
        bool multiMode;   // phone: taps add to the selection
        Button multiBtn;

        struct Pose3 { public Vector3 p; public Quaternion r; public Vector3 s; }
        Pose3 groupStart;
        readonly List<Pose3> multiStart = new List<Pose3>();
        bool groupDragging;

        static bool AddKeyHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand)
                || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        /// A tap / click on an object: adds or removes it when adding to the
        /// selection, otherwise selects only it (and its group).
        void SelectTap(AtlasItem it)
        {
            bool add = (multiMode || AddKeyHeld()) && !poseMode && meshMode == "";
            if (!add || it == null || selected == null) { Select(it); return; }
            if (it == selected)
            {
                // The main one leaves: the next one becomes the main one.
                if (multi.Count == 0) return;
                var next = multi[0];
                multi.RemoveAt(0);
                var rest = new List<AtlasItem>(multi);
                Select(next, false);
                multi.AddRange(rest);
            }
            else if (multi.Contains(it)) foreach (var g in GroupOf(it)) multi.Remove(g);
            else foreach (var g in GroupOf(it)) if (g != selected && !multi.Contains(g)) multi.Add(g);
            ShowMultiHighlight();
            RefreshProps();
            RefreshOutlinerSoon();
            if (multi.Count > 0) Toast((multi.Count + 1) + " objetos seleccionados: muévelos, gíralos o escálalos juntos");
        }

        void Select(AtlasItem it, bool withGroup)
        {
            Select(it);
            if (!withGroup) multi.Clear();
            ShowMultiHighlight();
        }

        /// The objects that share `it`'s group (just `it` if it has none).
        List<AtlasItem> GroupOf(AtlasItem it)
        {
            var l = new List<AtlasItem>();
            if (it == null) return l;
            if (string.IsNullOrEmpty(it.group)) { l.Add(it); return l; }
            foreach (var o in items) if (o != null && o.group == it.group) l.Add(o);
            if (!l.Contains(it)) l.Add(it);
            return l;
        }

        /// Called from Select: the selection becomes the object and its group.
        void SelectionChanged(AtlasItem it)
        {
            multi.Clear();
            if (it != null && !string.IsNullOrEmpty(it.group))
                foreach (var o in GroupOf(it)) if (o != it) multi.Add(o);
            ShowMultiHighlight();
        }

        void ShowMultiHighlight()
        {
            multi.RemoveAll(o => o == null || o == selected);
            if (selected == null || poseMode || lookThrough != null || multi.Count == 0) return;
            highlighter.Show(selected);
            foreach (var o in multi) highlighter.Add(o);
        }

        void RefreshOutlinerSoon()
        {
            outlinerSig = "";
            outlinerAt = 0f;
        }

        // ------------------------------------------------------------------
        // «Píxeles nítidos»: textures without smoothing (Minecraft style).

        void ApplyPixelated(AtlasItem it)
        {
            if (it == null) return;
            foreach (var r in it.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    foreach (var prop in PixelProps)
                    {
                        if (!m.HasProperty(prop)) continue;
                        var tex = m.GetTexture(prop);
                        if (tex == null || tex is RenderTexture) continue;
                        bool small = tex.width <= 128 && tex.height <= 128;
                        tex.filterMode = it.pixelated || small ? FilterMode.Point : FilterMode.Trilinear;
                    }
                }
            }
        }

        static readonly string[] PixelProps = { "_BaseMap", "_MainTex", "_EmissionMap", "_BumpMap" };

        void ToggleMultiMode()
        {
            multiMode = !multiMode;
            if (multiBtn != null) AtlasTheme.SetActive(multiBtn, multiMode);
            Toast(multiMode
                ? "Selección múltiple: toca los objetos para añadirlos o quitarlos"
                : "Selección múltiple desactivada");
        }

        // ------------------------------------------------------------------
        // Moving them together: the others follow the main one as if they
        // were its children while the manipulator is dragged.

        void GroupDragBegin()
        {
            multi.RemoveAll(o => o == null || o == selected);
            groupDragging = false;
            multiStart.Clear();
            if (selected == null || poseMode || meshMode != "" || multi.Count == 0) return;
            var t = selected.transform;
            groupStart = new Pose3 { p = t.position, r = t.rotation, s = t.localScale };
            foreach (var o in multi)
                multiStart.Add(new Pose3 { p = o.transform.position, r = o.transform.rotation, s = o.transform.localScale });
            groupDragging = true;
        }

        void GroupDragMove()
        {
            if (!groupDragging || selected == null || multiStart.Count != multi.Count) return;
            var t = selected.transform;
            Quaternion dr = t.rotation * Quaternion.Inverse(groupStart.r);
            Vector3 ratio = new Vector3(
                Mathf.Abs(groupStart.s.x) > 1e-5f ? t.localScale.x / groupStart.s.x : 1f,
                Mathf.Abs(groupStart.s.y) > 1e-5f ? t.localScale.y / groupStart.s.y : 1f,
                Mathf.Abs(groupStart.s.z) > 1e-5f ? t.localScale.z / groupStart.s.z : 1f);
            for (int i = 0; i < multi.Count; i++)
            {
                var o = multi[i];
                if (o == null || o.locked) continue;
                var s0 = multiStart[i];
                // Where it was, in the main object's own space, then back.
                Vector3 local = Quaternion.Inverse(groupStart.r) * (s0.p - groupStart.p);
                local = Vector3.Scale(local, ratio);
                o.transform.position = t.position + t.rotation * local;
                o.transform.rotation = dr * s0.r;
                o.transform.localScale = Vector3.Scale(s0.s, ratio);
            }
        }

        void GroupDragEnd()
        {
            if (!groupDragging) return;
            groupDragging = false;
            int f = Mathf.RoundToInt(frame);
            foreach (var o in multi)
            {
                if (o == null || o.locked) continue;
                if (Regrip(o)) continue;
                if (autoKey || o.Animated) o.SetKey(f, AtlasItem.ChPos | AtlasItem.ChRot | AtlasItem.ChScale, new List<string>());
            }
        }

        // ------------------------------------------------------------------
        // Groups and the panel of a multiple selection

        void GroupSelection()
        {
            multi.RemoveAll(o => o == null || o == selected);
            if (selected == null || multi.Count == 0) { Toast("Selecciona dos o más objetos para agruparlos"); return; }
            string g = SceneStore.NewId();
            selected.group = g;
            foreach (var o in multi) o.group = g;
            Commit(true);
            RefreshProps();
            RefreshOutlinerSoon();
            Toast("Agrupados " + (multi.Count + 1) + " objetos: al tocar uno se seleccionan todos");
        }

        void UngroupSelection()
        {
            multi.RemoveAll(o => o == null || o == selected);
            int n = 0;
            if (selected != null && !string.IsNullOrEmpty(selected.group)) { selected.group = ""; n++; }
            foreach (var o in multi) if (!string.IsNullOrEmpty(o.group)) { o.group = ""; n++; }
            if (n == 0) { Toast("No hay grupo que deshacer"); return; }
            Commit(true);
            RefreshProps();
            RefreshOutlinerSoon();
            Toast("Grupo deshecho: cada objeto vuelve a ir por su cuenta");
        }

        /// The other selected objects hold on to the main one from this frame:
        /// they move with it (and with its animation) until they are let go.
        void AttachSelectionToMain()
        {
            multi.RemoveAll(o => o == null || o == selected);
            if (selected == null || multi.Count == 0) return;
            int n = 0;
            foreach (var o in new List<AtlasItem>(multi))
            {
                if (o == null) continue;
                if (HoldsOn(selected, o)) continue;   // would hold itself
                Grab(o, selected, "", false);
                n++;
            }
            Commit(true);
            RefreshKeys();
            RefreshProps();
            Toast(n > 0
                ? n + (n == 1 ? " objeto pegado" : " objetos pegados") + " a «" + selected.gameObject.name + "» desde el fotograma " + Mathf.RoundToInt(frame) + ". Para soltarlos: selecciona cada uno → «Pegar a otro objeto» → Soltar"
                : "No se pudo pegar (uno ya sostiene al otro)");
        }

        void BuildMultiSection()
        {
            multi.RemoveAll(o => o == null || o == selected);
            if (selected == null || multi.Count == 0) return;
            bool grouped = !string.IsNullOrEmpty(selected.group);
            foreach (var o in multi) if (o.group != selected.group) grouped = false;
            var box = Sec((multi.Count + 1) + " objetos seleccionados", "multiselect");
            var names = new List<string>();
            foreach (var o in multi) names.Add(o.gameObject.name);
            Hint(box, "Principal: «" + selected.gameObject.name + "». También: " + string.Join(", ", names.ToArray()) +
                ". El manipulador los mueve, gira y escala a todos juntos; cada uno conserva su animación.");
            if (grouped)
                AtlasTheme.TextButton(box, "unlock", "Desagrupar", UngroupSelection, false, 42f, 15);
            else
                AtlasTheme.TextButton(box, "merge", "Agrupar (se seleccionan siempre juntos)", GroupSelection, true, 44f, 15);
            AtlasTheme.TextButton(box, "bone", "Pegar los demás a «" + selected.gameObject.name + "» (desde este fotograma)", AttachSelectionToMain, false, 44f, 14);
            AtlasTheme.TextButton(box, "deselect", "Quedarse solo con «" + selected.gameObject.name + "»", () => { multi.Clear(); Select(selected, false); }, false, 40f, 14);
        }
    }
}
