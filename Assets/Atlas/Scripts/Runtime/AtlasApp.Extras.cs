// ATLAS 3D (Unity) - extras that make ATLAS more than before:
//   Cámara de escena   a camera placed from the current view; it can be
//                      animated with keys, looked through, and renders
//                      (picture and animation) are made from it.
//   Imagen en un plano a photo or drawing standing in the scene (references,
//                      backgrounds, 2D characters).
//   Imán               snaps moving (0.25 m), turning (15°) and scaling (×0.1).
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        AtlasItem lookThrough;
        GameObject lookBar;
        Button snapBtn;

        // ------------------------------------------------------------------
        // Scene camera

        /// Camera prop, like the Godot version: a film camera with its body,
        /// lens (pointing forward, +Z), two reels on top, a red «recording»
        /// light and a small blue screen on the side.
        GameObject MakeCameraProp()
        {
            var go = new GameObject();
            Part(go, PrimitiveType.Cube, "Cuerpo", new Vector3(0f, 0f, -0.04f), Vector3.zero, new Vector3(0.26f, 0.24f, 0.42f));
            Part(go, PrimitiveType.Cylinder, "Objetivo", new Vector3(0f, 0.01f, 0.24f), new Vector3(90f, 0f, 0f), new Vector3(0.17f, 0.07f, 0.17f));
            Part(go, PrimitiveType.Cylinder, "Parasol", new Vector3(0f, 0.01f, 0.32f), new Vector3(90f, 0f, 0f), new Vector3(0.21f, 0.015f, 0.21f));
            Part(go, PrimitiveType.Cylinder, "Carrete", new Vector3(0f, 0.24f, 0.08f), new Vector3(0f, 0f, 90f), new Vector3(0.22f, 0.025f, 0.22f));
            Part(go, PrimitiveType.Cylinder, "Carrete", new Vector3(0f, 0.22f, -0.17f), new Vector3(0f, 0f, 90f), new Vector3(0.18f, 0.025f, 0.18f));
            Part(go, PrimitiveType.Cube, "Visor", new Vector3(-0.1f, 0.08f, -0.3f), Vector3.zero, new Vector3(0.06f, 0.06f, 0.1f));
            Part(go, PrimitiveType.Sphere, "Luz", new Vector3(0.132f, 0.06f, 0.1f), Vector3.zero, new Vector3(0.035f, 0.035f, 0.035f));
            Part(go, PrimitiveType.Cube, "Pantalla", new Vector3(0.132f, 0.03f, -0.04f), Vector3.zero, new Vector3(0.01f, 0.07f, 0.09f));
            // What it sees, like Blender: a pyramid from the lens to the
            // picture's frame, with a filled triangle showing «up».
            var fr = new GameObject("Encuadre");
            fr.transform.SetParent(go.transform, false);
            for (int i = 0; i < 3; i++)
            {
                var lgo = new GameObject("Linea" + i);
                lgo.transform.SetParent(fr.transform, false);
                var lr = lgo.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.widthMultiplier = 0.012f;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sharedMaterial = FrameLineMat();
                lr.loop = i == 0;
            }
            var up = new GameObject("Arriba");
            up.transform.SetParent(fr.transform, false);
            up.AddComponent<MeshFilter>().sharedMesh = new Mesh { name = "Arriba" };
            up.AddComponent<MeshRenderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        Material frameLineMat;

        Material FrameLineMat()
        {
            if (frameLineMat == null)
            {
                frameLineMat = new Material(unlitTemplate != null ? unlitTemplate : litTemplate);
                frameLineMat.SetColor("_BaseColor", new Color(0.92f, 0.93f, 0.96f, 1f));
            }
            return frameLineMat;
        }

        /// The pyramid follows the camera's field of view and the render's
        /// proportions (16:9, square, vertical…).
        void UpdateFrustum(AtlasItem it)
        {
            var fr = it.transform.Find("Encuadre");
            if (fr == null) return;
            float aspect = renderH > 0 ? (float)renderW / renderH : 16f / 9f;
            float d = 0.9f;
            float h = Mathf.Clamp(d * Mathf.Tan(Mathf.Clamp(it.fov, 5f, 150f) * 0.5f * Mathf.Deg2Rad), 0.05f, 2f);
            float w = h * aspect;
            var apex = new Vector3(0f, 0.01f, 0.17f);
            var c = new[] { new Vector3(-w, -h, d), new Vector3(w, -h, d), new Vector3(w, h, d), new Vector3(-w, h, d) };
            for (int i = 0; i < 4; i++) c[i] += new Vector3(0f, 0.01f, 0.17f);
            var lines = fr.GetComponentsInChildren<LineRenderer>();
            if (lines.Length >= 3)
            {
                lines[0].positionCount = 4;
                lines[0].SetPositions(c);
                lines[1].positionCount = 3;
                lines[1].SetPositions(new[] { c[0], apex, c[2] });
                lines[2].positionCount = 3;
                lines[2].SetPositions(new[] { c[1], apex, c[3] });
                foreach (var l in lines) l.widthMultiplier = 0.012f * Mathf.Max(0.2f, it.transform.lossyScale.x);
            }
            var up = fr.Find("Arriba");
            if (up != null)
            {
                var mesh = up.GetComponent<MeshFilter>().sharedMesh;
                float top = c[2].y + h * 0.08f;
                var v = new[] { new Vector3(-w * 0.35f, top, c[2].z), new Vector3(0f, top + w * 0.3f, c[2].z), new Vector3(w * 0.35f, top, c[2].z) };
                mesh.Clear();
                mesh.vertices = v;
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 1 };   // both sides
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
        }

        static void Part(GameObject parent, PrimitiveType type, string name, Vector3 pos, Vector3 euler, Vector3 scale)
        {
            var p = GameObject.CreatePrimitive(type);
            p.name = name;
            var col = p.GetComponent<Collider>();
            if (col != null) Destroy(col);
            p.transform.SetParent(parent.transform, false);
            p.transform.localPosition = pos;
            p.transform.localRotation = Quaternion.Euler(euler);
            p.transform.localScale = scale;
        }

        Material camRedMat, camBlueMat, camUpMat;

        /// The red light and the blue screen keep their own colours.
        Material CameraAccent(string part)
        {
            if (part == "Luz")
            {
                if (camRedMat == null)
                {
                    camRedMat = new Material(litTemplate);
                    camRedMat.SetColor("_BaseColor", new Color(0.9f, 0.1f, 0.08f));
                    camRedMat.EnableKeyword("_EMISSION");
                    camRedMat.SetColor("_EmissionColor", new Color(1.6f, 0.15f, 0.1f));
                }
                return camRedMat;
            }
            if (part == "Arriba")
            {
                if (camUpMat == null)
                {
                    camUpMat = new Material(unlitTemplate != null ? unlitTemplate : litTemplate);
                    camUpMat.SetColor("_BaseColor", new Color(1f, 0.6f, 0.15f, 1f));
                    camUpMat.SetFloat("_Cull", 0f);
                }
                return camUpMat;
            }
            if (part == "Pantalla")
            {
                if (camBlueMat == null)
                {
                    camBlueMat = new Material(litTemplate);
                    camBlueMat.SetColor("_BaseColor", new Color(0.1f, 0.35f, 0.9f));
                    camBlueMat.EnableKeyword("_EMISSION");
                    camBlueMat.SetColor("_EmissionColor", new Color(0.1f, 0.4f, 1.2f));
                }
                return camBlueMat;
            }
            return null;
        }

        AtlasItem RenderCamera()
        {
            foreach (var it in items) if (it != null && it.kind == "camera" && it.renderCam) return it;
            return null;
        }

        void AddCamera()
        {
            CloseMenus();
            count++;
            bool first = true;
            foreach (var it in items) if (it != null && it.kind == "camera") first = false;
            var d = new ItemData
            {
                v = 3,
                kind = "camera",
                name = "Cámara " + count,
                position = cam.transform.position,
                rotation = cam.transform.eulerAngles,
                scale = Vector3.one,
                fov = cam.fieldOfView,
                renderCam = first,
            };
            Select(Create(d));
            Commit(true);
            Toast(first ? "Cámara colocada desde la vista actual: los renders salen por ella" : "Cámara colocada desde la vista actual");
        }

        void SetRenderCamera(AtlasItem it, bool on)
        {
            foreach (var o in items)
                if (o != null && o.kind == "camera")
                {
                    o.renderCam = on && o == it;
                    ApplyLook(o);
                }
            Commit(true);
            RefreshProps();
        }

        void LookThrough(AtlasItem it)
        {
            if (lookThrough != null && lookThrough != it) StopLookThrough();
            if (it == null) return;
            if (selected != it) Select(it);
            lookThrough = it;
            foreach (var r in it.GetComponentsInChildren<Renderer>()) r.enabled = false;
            gizmo.Show(null);
            highlighter.Clear();
            lookBar.SetActive(true);
            frameLayer.gameObject.SetActive(true);
            Toast("Dentro de la cámara: un dedo la gira, dos dedos la mueven, pellizca para acercarla. El recuadro es lo que sale en el render.");
        }

        void StopLookThrough()
        {
            var it = lookThrough;
            lookThrough = null;
            if (lookBar != null) lookBar.SetActive(false);
            if (frameLayer != null) frameLayer.gameObject.SetActive(false);
            if (it != null) ApplyLook(it);
            cam.fieldOfView = viewFov;
            UpdateCamera();
            if (selected != null && !poseMode) highlighter.Show(selected);
            ShowGizmo();
            RefreshProps();
        }

        /// Camera button of the top bar: in and out of the scene camera
        /// (made from the current view when there is none).
        void ToggleCameraView()
        {
            if (lookThrough != null) { StopLookThrough(); return; }
            AtlasItem c = RenderCamera();
            if (c == null) foreach (var it in items) if (it != null && it.kind == "camera") { c = it; break; }
            if (c == null)
            {
                AddCamera();
                c = selected;
            }
            if (c != null) LookThrough(c);
        }

        float viewFov = 50f;

        void UpdateLookThrough()
        {
            ApplyCameraRigs();
            if (lookThrough == null)
            {
                viewFov = cam.fieldOfView;
                return;
            }
            if (!items.Contains(lookThrough)) { StopLookThrough(); return; }
            Vector3 p;
            Quaternion q;
            CameraPose(lookThrough, frame, out p, out q);
            cam.transform.SetPositionAndRotation(p, q);
            cam.fieldOfView = lookThrough.fov;
            FitClip(cam, 2f);
            UpdateFrameOverlay();
        }

        /// «Mirar a» and «Seguir a» of every scene camera.
        void ApplyCameraRigs()
        {
            if (cameraDragging) return;
            foreach (var it in items)
            {
                if (it == null || it.kind != "camera") continue;
                var follow = string.IsNullOrEmpty(it.followId) ? null : FindItem(it.followId);
                if (follow != null) it.transform.position = follow.transform.position + it.followOffset;
                var look = string.IsNullOrEmpty(it.lookAtId) ? null : FindItem(it.lookAtId);
                if (look != null)
                {
                    Vector3 c = look.transform.position;
                    var rs = look.GetComponentsInChildren<Renderer>();
                    if (rs.Length > 0) { Bounds bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds); c = bb.center; }
                    Vector3 d = c - it.transform.position;
                    if (d.sqrMagnitude > 1e-6f) it.transform.rotation = Quaternion.LookRotation(d, Vector3.up);
                }
            }
        }

        /// The camera's pose for the view and the render, with its shake.
        void CameraPose(AtlasItem it, float f, out Vector3 pos, out Quaternion rot)
        {
            pos = it.transform.position;
            rot = it.transform.rotation;
            if (it.shake <= 0.001f) return;
            float t = f / Mathf.Max(1, fps) * Mathf.Max(0.05f, it.shakeSpeed) * 2f;
            float k = it.shake * 2.5f;
            float rx = (Mathf.PerlinNoise(t, 0.37f) - 0.5f) * 2f * k;
            float ry = (Mathf.PerlinNoise(0.71f, t) - 0.5f) * 2f * k;
            float rz = (Mathf.PerlinNoise(t + 13.1f, 4.2f) - 0.5f) * k;
            rot = rot * Quaternion.Euler(rx, ry, rz);
        }

        // ------------------------------------------------------------------
        // Moving the camera from inside

        bool cameraDragging;

        void LookThroughDrag(Vector2 delta)
        {
            var t = lookThrough.transform;
            cameraDragging = true;
            t.rotation = Quaternion.AngleAxis(delta.x * 0.15f, Vector3.up) * Quaternion.AngleAxis(-delta.y * 0.15f, t.right) * t.rotation;
        }

        void LookThroughTwoFingers(Vector2 pan, float pinch)
        {
            var t = lookThrough.transform;
            cameraDragging = true;
            float k = 0.004f * Mathf.Max(1f, t.position.magnitude * 0.2f);
            t.position -= (t.right * pan.x + t.up * pan.y) * k;
            t.position += t.forward * pinch * k * 2.5f;
        }

        void LookThroughEnd()
        {
            if (!cameraDragging || lookThrough == null) return;
            cameraDragging = false;
            var keep = selected;
            selected = lookThrough;
            AutoKey();
            selected = keep;
            Commit(true);
        }

        // ------------------------------------------------------------------
        // Frame of the render while inside the camera

        RectTransform frameLayer;
        readonly Image[] frameShade = new Image[4];
        readonly Image[] frameLines = new Image[4];
        Text frameLabel;

        void BuildLookBar()
        {
            frameLayer = AtlasTheme.Rect(canvasRoot, "Encuadre");
            AtlasTheme.Fill(frameLayer);
            frameLayer.SetSiblingIndex(1);
            for (int i = 0; i < 4; i++)
            {
                frameShade[i] = AtlasTheme.Box(frameLayer, "Sombra", new Color(0f, 0f, 0f, 0.55f));
                frameShade[i].raycastTarget = false;
                frameLines[i] = AtlasTheme.Box(frameLayer, "Tercio", new Color(1f, 1f, 1f, 0.25f));
                frameLines[i].raycastTarget = false;
            }
            frameLabel = AtlasTheme.Label(frameLayer, "", 14, new Color(1f, 1f, 1f, 0.8f), true, TextAnchor.LowerRight);
            frameLayer.gameObject.SetActive(false);
            var b = AtlasTheme.TextButton(viewArea, "camera", "Salir de la cámara", StopLookThrough, true, 48f, 17);
            AtlasTheme.Pin((RectTransform)b.transform, 0.5f, 1f, new Vector2(0f, -84f), new Vector2(230f, 48f));
            lookBar = b.gameObject;
            lookBar.SetActive(false);
        }

        static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.pivot = Vector2.zero;
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(Mathf.Max(0f, w), Mathf.Max(0f, h));
        }

        void UpdateFrameOverlay()
        {
            if (frameLayer == null) return;
            float W = frameLayer.rect.width, H = frameLayer.rect.height;
            float aspect = (float)renderW / Mathf.Max(1, renderH);
            // Same vertical field as the render: the frame is as tall as
            // the screen (narrower formats), or narrower in height.
            float fw = Mathf.Min(W, H * aspect), fh = fw / aspect;
            float fx = (W - fw) * 0.5f, fy = (H - fh) * 0.5f;
            Place(frameShade[0].rectTransform, 0f, 0f, fx, H);
            Place(frameShade[1].rectTransform, fx + fw, 0f, W - fx - fw, H);
            Place(frameShade[2].rectTransform, fx, 0f, fw, fy);
            Place(frameShade[3].rectTransform, fx, fy + fh, fw, H - fy - fh);
            Place(frameLines[0].rectTransform, fx + fw / 3f, fy, 1.5f, fh);
            Place(frameLines[1].rectTransform, fx + fw * 2f / 3f, fy, 1.5f, fh);
            Place(frameLines[2].rectTransform, fx, fy + fh / 3f, fw, 1.5f);
            Place(frameLines[3].rectTransform, fx, fy + fh * 2f / 3f, fw, 1.5f);
            frameLabel.text = renderW + "×" + renderH;
            Place(frameLabel.rectTransform, fx + fw - 140f, fy + 6f, 132f, 22f);
        }

        void BuildCameraSection(AtlasItem it)
        {
            var box = Sec("Cámara", "camera");
            AtlasTheme.TextButton(box, "view", lookThrough == it ? "Salir de la cámara" : "Ver por esta cámara", () =>
            {
                if (lookThrough == it) StopLookThrough();
                else LookThrough(it);
                RefreshProps();
            }, lookThrough != it, 46f, 16);
            AtlasTheme.Switch(box, "Cámara activa (la que usa el render)", it.renderCam, on => SetRenderCamera(it, on));
            AtlasTheme.SliderRow(box, "Campo visual", 10f, 120f, it.fov, "0", x => { it.fov = x; UpdateFrustum(it); }, () => Commit(true));
            var g = AtlasTheme.Grid(box, 3, InnerWidth, 38f);
            float[] lens = { 24f, 35f, 50f, 85f, 135f };
            string[] lensNames = { "Gran angular", "35 mm", "50 mm", "85 mm", "Tele 135" };
            for (int i = 0; i < lens.Length; i++)
            {
                // Full-frame focal length -> vertical field of view.
                float fovV = 2f * Mathf.Atan(12f / lens[i]) * Mathf.Rad2Deg;
                AtlasTheme.Chip(g, lensNames[i], () => { it.fov = fovV; Commit(true); RefreshProps(); });
            }
            AtlasTheme.Chip(g, "Desde la vista", () =>
            {
                it.transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
                it.fov = cam.fieldOfView;
                AutoKey();
                Commit(true);
                Toast("Cámara puesta en la vista actual");
            });
            // Look at / follow another object
            var others = new List<AtlasItem>();
            foreach (var o in items) if (o != null && o != it && o.kind != "camera") others.Add(o);
            var l1 = AtlasTheme.Label(box, "Mirar a", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(l1.gameObject, 24f);
            var g1 = AtlasTheme.Grid(box, 2, InnerWidth, 36f);
            AtlasTheme.Chip(g1, "Nada (libre)", () => { it.lookAtId = ""; Commit(true); RefreshProps(); }, string.IsNullOrEmpty(it.lookAtId));
            foreach (var o0 in others)
            {
                var o = o0;
                AtlasTheme.Chip(g1, o.gameObject.name, () => { it.lookAtId = o.id; Commit(true); RefreshProps(); Toast("La cámara apunta siempre a «" + o.gameObject.name + "»"); }, it.lookAtId == o.id);
            }
            var l2 = AtlasTheme.Label(box, "Seguir a", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(l2.gameObject, 24f);
            var g2 = AtlasTheme.Grid(box, 2, InnerWidth, 36f);
            AtlasTheme.Chip(g2, "Nada (quieta)", () => { it.followId = ""; Commit(true); RefreshProps(); }, string.IsNullOrEmpty(it.followId));
            foreach (var o0 in others)
            {
                var o = o0;
                AtlasTheme.Chip(g2, o.gameObject.name, () =>
                {
                    it.followId = o.id;
                    it.followOffset = it.transform.position - o.transform.position;
                    Commit(true);
                    RefreshProps();
                    Toast("La cámara viaja con «" + o.gameObject.name + "» a la distancia de ahora");
                }, it.followId == o.id);
            }
            // Shake
            var l3 = AtlasTheme.Label(box, "Temblor de cámara", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(l3.gameObject, 24f);
            AtlasTheme.SliderRow(box, "Intensidad", 0f, 1f, it.shake, "0.00", x => it.shake = x, () => Commit(true));
            AtlasTheme.SliderRow(box, "Velocidad", 0.1f, 3f, it.shakeSpeed, "0.0", x => it.shakeSpeed = x, () => Commit(true));
            var g3 = AtlasTheme.Grid(box, 3, InnerWidth, 36f);
            AtlasTheme.Chip(g3, "En mano", () => { it.shake = 0.25f; it.shakeSpeed = 0.6f; Commit(true); RefreshProps(); });
            AtlasTheme.Chip(g3, "Correr", () => { it.shake = 0.55f; it.shakeSpeed = 1.6f; Commit(true); RefreshProps(); });
            AtlasTheme.Chip(g3, "Terremoto", () => { it.shake = 1f; it.shakeSpeed = 3f; Commit(true); RefreshProps(); });
            Hint(box, "Anímala con la clave amarilla como cualquier objeto: el render de la animación sigue su recorrido, con su temblor.");
        }

        // ------------------------------------------------------------------
        // Image on a plane

        void AddImagePlane()
        {
            CloseMenus();
            if (store == null || NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path) || store == null) return;
                string file;
                try { file = store.AddTextureFile(path); }
                catch (System.Exception e) { Toast("No se pudo usar la imagen: " + e.Message, true); return; }
                var tex = LoadTexture(file);
                if (tex == null) { Toast("Esa imagen no se puede leer (usa PNG o JPG)", true); return; }
                float aspect = tex.height > 0 ? (float)tex.width / tex.height : 1f;
                count++;
                string nm = Path.GetFileNameWithoutExtension(path);
                var d = new ItemData
                {
                    v = 3,
                    kind = "image",
                    name = string.IsNullOrEmpty(nm) ? "Imagen" : nm,
                    position = new Vector3(target.position.x, 1f, target.position.z),
                    scale = new Vector3(2f * aspect, 2f, 1f),
                    color = Color.white,
                    texture = file,
                    smoothness = 0.1f,
                };
                Select(Create(d));
                Commit(true);
                Toast("Imagen colocada: muévela, gírala o escálala como cualquier objeto");
            }, new string[] { "image/*" });
        }

        // ------------------------------------------------------------------
        // Snap (imán)

        void ToggleSnap()
        {
            gizmo.snap = !gizmo.snap;
            AtlasTheme.SetActive(snapBtn, gizmo.snap);
            Toast(gizmo.snap ? "Imán: mover de 0,25 m en 0,25 m, girar de 15° en 15°, escalar de 0,1 en 0,1" : "Imán desactivado");
        }
    }
}
