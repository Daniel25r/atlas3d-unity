// ATLAS 3D (Unity) - «Modo pose», like the Godot version: the skeleton of
// the selected model is drawn over the view; a tap on a joint chooses the
// bone and the manipulator turns it (Girar), moves it (Mover) or stretches
// it (Escalar). Bones are animated with the same yellow key as objects.
// Also: picking objects by their real shape (not a box) and «Mis poses».
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    [Serializable]
    public class SavedPose
    {
        public string name;
        public List<BonePose> bones = new List<BonePose>();   // path = bone name
    }

    [Serializable]
    public class PoseLibrary
    {
        public List<SavedPose> poses = new List<SavedPose>();
    }

    public partial class AtlasApp
    {
        bool poseMode;
        string activeBone = "";
        Button boneBtn;
        RectTransform boneLayer;
        Highlighter highlighter;
        AtlasItem overlayItem;
        readonly List<string> overlayBones = new List<string>();
        readonly List<Image> overlayDots = new List<Image>();
        readonly List<Image> overlayLines = new List<Image>();
        readonly List<string> overlayParents = new List<string>();
        readonly InputField[] boneTf = new InputField[9];
        Mesh pickMesh;

        // ------------------------------------------------------------------
        // Entering / leaving

        void TogglePose()
        {
            if (poseMode) ExitPose();
            else EnterPose();
        }

        void EnterPose()
        {
            if (selected == null || !selected.HasBones)
            {
                Toast(selected != null && selected.kind == "model" && !selected.loaded ? "El modelo todavía se está cargando…" : "Selecciona un modelo con huesos para posarlo");
                return;
            }
            poseMode = true;
            activeBone = "";
            if (tool == Tool.Select || tool == Tool.Move) SetTool(Tool.Rotate);
            highlighter.Clear();
            ShowGizmo();
            RefreshMode();
            RefreshProps();
            Toast("Modo pose: toca una articulación y gírala. Pulsa la clave amarilla para animarla.");
        }

        void ExitPose()
        {
            if (!poseMode) return;
            poseMode = false;
            activeBone = "";
            HideBoneOverlay();
            if (selected != null) highlighter.Show(selected);
            ShowGizmo();
            RefreshMode();
            RefreshProps();
        }

        void SetActiveBone(string path)
        {
            activeBone = path ?? "";
            ShowGizmo();
            RefreshMode();
            RefreshProps();
        }

        /// After dragging the manipulator on a bone: store it in the pose.
        void BoneEdited()
        {
            if (!poseMode || selected == null || activeBone == "") return;
            selected.RecordBone(activeBone);
        }

        // ------------------------------------------------------------------
        // Skeleton drawn over the view

        void BuildBoneOverlay()
        {
            boneLayer = AtlasTheme.Rect(viewArea, "Huesos");
            AtlasTheme.Fill(boneLayer);
            boneLayer.pivot = new Vector2(0.5f, 0.5f);
            boneLayer.SetAsFirstSibling();
        }

        void HideBoneOverlay()
        {
            if (boneLayer == null) return;
            AtlasTheme.Clear(boneLayer);
            overlayBones.Clear();
            overlayDots.Clear();
            overlayLines.Clear();
            overlayParents.Clear();
            overlayItem = null;
        }

        void RebuildBoneOverlay(AtlasItem it)
        {
            HideBoneOverlay();
            overlayItem = it;
            foreach (var path in it.BoneOrder)
            {
                if (BoneNames.IsHelper(BoneNames.Leaf(path))) continue;
                overlayBones.Add(path);
                overlayParents.Add(it.ParentBone(path));
            }
            // Lines first (under the joints).
            for (int i = 0; i < overlayBones.Count; i++)
            {
                var line = AtlasTheme.Box(boneLayer, "Linea", new Color(1f, 1f, 1f, 0.55f));
                line.raycastTarget = false;
                var r = line.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0f, 0.5f);
                overlayLines.Add(line);
            }
            for (int i = 0; i < overlayBones.Count; i++)
            {
                var dot = AtlasTheme.Box(boneLayer, "Articulacion", AtlasTheme.AccentLight, 9);
                dot.raycastTarget = false;
                var r = dot.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(16f, 16f);
                overlayDots.Add(dot);
            }
        }

        bool ScreenToLayer(Vector3 world, out Vector2 local)
        {
            local = Vector2.zero;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0.01f) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(boneLayer, sp, null, out local);
        }

        void UpdateBoneOverlay()
        {
            if (boneLayer == null) return;
            if (!poseMode || selected == null || !selected.HasBones)
            {
                if (overlayItem != null) HideBoneOverlay();
                return;
            }
            if (overlayItem != selected) RebuildBoneOverlay(selected);
            var pos = new Vector2[overlayBones.Count];
            var ok = new bool[overlayBones.Count];
            var index = new Dictionary<string, int>();
            for (int i = 0; i < overlayBones.Count; i++)
            {
                index[overlayBones[i]] = i;
                var t = selected.Bone(overlayBones[i]);
                ok[i] = t != null && ScreenToLayer(t.position, out pos[i]);
            }
            for (int i = 0; i < overlayBones.Count; i++)
            {
                var dot = overlayDots[i];
                dot.enabled = ok[i];
                if (ok[i])
                {
                    bool active = overlayBones[i] == activeBone;
                    dot.rectTransform.anchoredPosition = pos[i];
                    dot.rectTransform.sizeDelta = active ? new Vector2(22f, 22f) : new Vector2(15f, 15f);
                    dot.color = active ? AtlasTheme.Key : (selected.IsPosed(overlayBones[i]) ? AtlasTheme.Accent : new Color(0.92f, 0.92f, 0.96f, 0.9f));
                }
                var line = overlayLines[i];
                int pi;
                bool show = ok[i] && overlayParents[i] != "" && index.TryGetValue(overlayParents[i], out pi) && ok[pi];
                line.enabled = show;
                if (show)
                {
                    index.TryGetValue(overlayParents[i], out pi);
                    Vector2 a = pos[pi], b = pos[i];
                    Vector2 d = b - a;
                    var r = line.rectTransform;
                    r.anchoredPosition = a;
                    r.sizeDelta = new Vector2(d.magnitude, 3f);
                    r.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                }
            }
        }

        /// The joint nearest to the finger (within a fingertip), or "".
        string PickBone(Vector2 screen)
        {
            if (selected == null) return "";
            float scale = canvasRoot.GetComponent<Canvas>().scaleFactor;
            float best = 30f * Mathf.Max(1f, scale);
            string found = "";
            foreach (var path in selected.BoneOrder)
            {
                if (BoneNames.IsHelper(BoneNames.Leaf(path))) continue;
                var t = selected.Bone(path);
                if (t == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(t.position);
                if (sp.z <= 0.01f) continue;
                float d = Vector2.Distance(new Vector2(sp.x, sp.y), screen);
                if (d < best) { best = d; found = path; }
            }
            return found;
        }

        // ------------------------------------------------------------------
        // Picking by the real shape

        /// The visible, unlocked object under the finger: its triangles are
        /// tested (skinned meshes in their current pose), so a tap between
        /// a character's legs does not select it, and any part of it does.
        AtlasItem PickItemTriangles(Vector2 screen)
        {
            Ray ray = cam.ScreenPointToRay(screen);
            AtlasItem best = null;
            float bestD = float.MaxValue;
            foreach (var it in items)
            {
                if (it == null || !it.visible || it.locked) continue;
                foreach (var r in it.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || r.gameObject.layer == Gizmo.Layer) continue;
                    float bd = 0f;
                    // Skinned meshes may report a stale box: test them always.
                    bool skinned = r is SkinnedMeshRenderer;
                    if (!skinned && (!r.bounds.IntersectRay(ray, out bd) || bd > bestD)) continue;
                    float d;
                    int res = RayRenderer(r, ray, out d);
                    if (res == 0) continue;            // missed its triangles
                    if (res < 0)
                    {
                        // Mesh not readable: its box.
                        if (skinned && !r.bounds.IntersectRay(ray, out bd)) continue;
                        d = bd;
                    }
                    if (d < bestD) { bestD = d; best = it; }
                }
            }
            return best;
        }

        /// 1 hit (distance in `dist`), 0 miss, -1 cannot test the mesh.
        int RayRenderer(Renderer r, Ray ray, out float dist)
        {
            dist = 0f;
            Mesh mesh;
            Matrix4x4 m;
            var skin = r as SkinnedMeshRenderer;
            if (skin != null)
            {
                if (skin.sharedMesh == null || !skin.sharedMesh.isReadable) return -1;
                if (pickMesh == null) pickMesh = new Mesh();
                skin.BakeMesh(pickMesh, true);
                mesh = pickMesh;
                m = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) return -1;
                if (!mf.sharedMesh.isReadable) return -1;
                mesh = mf.sharedMesh;
                m = r.transform.localToWorldMatrix;
            }
            Matrix4x4 inv = m.inverse;
            Vector3 o = inv.MultiplyPoint3x4(ray.origin);
            Vector3 dir = inv.MultiplyVector(ray.direction);
            var v = mesh.vertices;
            float best = float.MaxValue;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var t = mesh.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    float d;
                    if (RayTriangle(o, dir, v[t[i]], v[t[i + 1]], v[t[i + 2]], out d) && d < best) best = d;
                }
            }
            if (best == float.MaxValue) return 0;
            // Same parameter in world space (the direction was not normalised).
            dist = (m.MultiplyPoint3x4(o + dir * best) - ray.origin).magnitude;
            return 1;
        }

        static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-12f) return false;
            float inv = 1f / det;
            Vector3 s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float w = Vector3.Dot(d, q) * inv;
            if (w < 0f || u + w > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 0f;
        }

        // ------------------------------------------------------------------
        // Inspector: «Esqueleto y pose»

        void BuildSkeletonSection(AtlasItem it)
        {
            var box = Sec("Esqueleto y pose", "bone");
            bool here = poseMode && selected == it;
            var pb = AtlasTheme.TextButton(box, "bone", here ? "Salir del modo pose" : "Entrar en modo pose", TogglePose, !here, 48f, 17);
            if (!here)
            {
                Hint(box, "En modo pose, toca una articulación en el visor (o elige un hueso en la lista) y gírala con el manipulador. Las rotaciones se animan con claves.");
                return;
            }
            // Bone list with a search box.
            var rows = new List<GameObject>();
            var names = new List<string>();
            var filter = AtlasTheme.TextField(box, "", null, 42f);
            var ph = AtlasTheme.Label(filter.transform, "Buscar hueso (brazo, mano, cabeza…)", 15, AtlasTheme.TextDim);
            AtlasTheme.Fill(ph.rectTransform, 12f, 12f);
            filter.placeholder = ph;
            var list = AtlasTheme.Rect(box, "ListaHuesos");
            AtlasTheme.Column(list.gameObject, 4f, new RectOffset(0, 0, 0, 0));
            foreach (var path0 in it.BoneOrder)
            {
                string path = path0;
                string leaf = BoneNames.Leaf(path);
                if (BoneNames.IsHelper(leaf)) continue;
                string shown = BoneNames.Display(leaf);
                bool active = path == activeBone;
                var row = AtlasTheme.Box(list, "Hueso", active ? AtlasTheme.AccentDim : AtlasTheme.Panel2, 8);
                AtlasTheme.Le(row.gameObject, 38f);
                var b = row.gameObject.AddComponent<Button>();
                b.onClick.AddListener(() => SetActiveBone(path));
                var tl = AtlasTheme.Label(row.transform, shown, 15, active ? AtlasTheme.Key : (it.IsPosed(path) ? AtlasTheme.AccentLight : AtlasTheme.TextColor), active);
                AtlasTheme.Fill(tl.rectTransform, 12f, 8f);
                rows.Add(row.gameObject);
                names.Add((shown + " " + leaf).ToLowerInvariant());
            }
            filter.onValueChanged.AddListener(q =>
            {
                q = q.Trim().ToLowerInvariant();
                for (int i = 0; i < rows.Count; i++) rows[i].SetActive(q == "" || names[i].Contains(q));
            });
            for (int i = 0; i < boneTf.Length; i++) boneTf[i] = null;
            if (activeBone != "" && it.Bone(activeBone) != null)
            {
                var title = AtlasTheme.Label(box, "Hueso: " + BoneNames.Display(BoneNames.Leaf(activeBone)), 16, AtlasTheme.Key, true);
                AtlasTheme.Le(title.gameObject, 26f);
                string[] rowNames = { "Rotación", "Mover", "Estirar" };
                Color[] ac = { AtlasTheme.AxisX, AtlasTheme.AxisY, AtlasTheme.AxisZ };
                string[] an = { "X", "Y", "Z" };
                for (int row = 0; row < 3; row++)
                {
                    var line = AtlasTheme.FieldRow(box, rowNames[row], 70f, 40f);
                    for (int c = 0; c < 3; c++)
                    {
                        var al = AtlasTheme.Label(line, an[c], 14, ac[c], true, TextAnchor.MiddleCenter);
                        AtlasTheme.Le(al.gameObject, 40f, 12f);
                        int idx = row * 3 + c;
                        boneTf[idx] = AtlasTheme.NumberField(line, 0f, v => SetBoneValue(idx, v), 66f);
                    }
                }
                FillBoneFields();
                Hint(box, "Con Girar giras el hueso; con Mover lo desplazas y con Escalar lo estiras (personajes elásticos).");
                AtlasTheme.TextButton(box, "undo", "Restablecer hueso", () =>
                {
                    it.ResetBone(activeBone);
                    AutoKey();
                    Commit(true);
                    RefreshProps();
                }, false, 44f, 16);
            }
            AtlasTheme.TextButton(box, "undo", "Restablecer pose completa", () =>
            {
                it.ResetPose();
                AutoKey();
                Commit(true);
                RefreshProps();
                Toast("Pose restablecida (Deshacer la recupera)");
            }, false, 44f, 16);

            // «Mis poses»: saved by bone name, usable on any character with
            // the same skeleton.
            var lib = LoadPoses();
            if (lib.poses.Count > 0)
            {
                var g = AtlasTheme.Grid(box, 2, InnerWidth, 38f);
                foreach (var sp0 in lib.poses)
                {
                    var sp = sp0;
                    AtlasTheme.Chip(g, "★ " + sp.name, () => ApplySavedPose(it, sp));
                }
            }
            var prow = AtlasTheme.Grid(box, lib.poses.Count > 0 ? 2 : 1, InnerWidth, 42f);
            AtlasTheme.TextButton(prow, null, "★ Guardar mi pose…", () =>
            {
                if (it.pose.Count == 0) { Toast("Primero mueve algún hueso"); return; }
                AtlasTheme.Dialog(canvasRoot, "Guardar mi pose", "Se guarda en «Mis poses» y la puedes usar en otros personajes con el mismo esqueleto.", "Mi pose", "Guardar", false, n =>
                {
                    var l = LoadPoses();
                    var sp = new SavedPose { name = string.IsNullOrEmpty(n.Trim()) ? "Mi pose" : n.Trim() };
                    foreach (var b in it.pose) { var c = b.Copy(); c.path = BoneNames.Leaf(b.path); sp.bones.Add(c); }
                    l.poses.Add(sp);
                    SavePoses(l);
                    Toast("Pose «" + sp.name + "» guardada");
                    RefreshProps();
                });
                toastHolder.SetAsLastSibling();
            }, false, 42f, 16);
            if (lib.poses.Count > 0)
            {
                AtlasTheme.TextButton(prow, null, "Borrar mis poses…", () =>
                {
                    AtlasTheme.Dialog(canvasRoot, "¿Borrar «Mis poses»?", "Se borran todas las poses guardadas.", null, "Borrar", true, _ =>
                    {
                        SavePoses(new PoseLibrary());
                        RefreshProps();
                    });
                }, false, 42f, 16);
            }
        }

        void SetBoneValue(int idx, float v)
        {
            if (selected == null || activeBone == "") return;
            var t = selected.Bone(activeBone);
            if (t == null) return;
            int axis = idx % 3;
            if (idx < 3) { var e = t.localEulerAngles; e[axis] = v; t.localEulerAngles = e; }
            else if (idx < 6) { var p = t.localPosition; p[axis] = v; t.localPosition = p; }
            else { var s = t.localScale; s[axis] = Mathf.Max(0.001f, v); t.localScale = s; }
            selected.RecordBone(activeBone);
            AutoKey();
            Commit(true);
        }

        void FillBoneFields()
        {
            if (selected == null || activeBone == "" || boneTf[0] == null) return;
            var t = selected.Bone(activeBone);
            if (t == null) return;
            for (int c = 0; c < 3; c++)
            {
                float e = t.localEulerAngles[c];
                if (e > 180f) e -= 360f;
                AtlasTheme.SetNumber(boneTf[c], Mathf.Round(e * 10f) / 10f);
                AtlasTheme.SetNumber(boneTf[3 + c], t.localPosition[c]);
                AtlasTheme.SetNumber(boneTf[6 + c], t.localScale[c]);
            }
        }

        static string PosesPath { get { return Path.Combine(Application.persistentDataPath, "mis_poses.json"); } }

        static PoseLibrary LoadPoses()
        {
            try
            {
                if (File.Exists(PosesPath))
                {
                    var l = JsonUtility.FromJson<PoseLibrary>(File.ReadAllText(PosesPath));
                    if (l != null && l.poses != null) return l;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("ATLAS: poses: " + e.Message);
            }
            return new PoseLibrary();
        }

        static void SavePoses(PoseLibrary l)
        {
            try { File.WriteAllText(PosesPath, JsonUtility.ToJson(l)); }
            catch (Exception e) { Debug.LogWarning("ATLAS: poses: " + e.Message); }
        }

        void ApplySavedPose(AtlasItem it, SavedPose sp)
        {
            int applied = 0;
            foreach (var path in it.BoneOrder)
            {
                string leaf = BoneNames.Leaf(path);
                foreach (var b in sp.bones)
                {
                    if (b.path != leaf) continue;
                    var t = it.Bone(path);
                    if (t == null) break;
                    t.localRotation = b.r;
                    it.RecordBone(path);
                    applied++;
                    break;
                }
            }
            if (applied == 0)
            {
                Toast("Esta pose es de otro esqueleto: ningún hueso coincide.", true);
                return;
            }
            AutoKey();
            Commit(true);
            RefreshProps();
            Toast("Pose «" + sp.name + "» aplicada (" + applied + " huesos)");
        }
    }
}
