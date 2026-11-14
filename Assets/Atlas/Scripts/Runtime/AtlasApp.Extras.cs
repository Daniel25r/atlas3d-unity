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

        /// Camera prop: a small body with a lens pointing forward.
        GameObject MakeCameraProp()
        {
            var go = new GameObject();
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Cuerpo";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(0.28f, 0.22f, 0.36f);
            body.transform.localPosition = new Vector3(0f, 0f, -0.1f);
            var lens = new GameObject("Objetivo");
            lens.transform.SetParent(go.transform, false);
            lens.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            lens.transform.localScale = new Vector3(0.24f, 0.22f, 0.24f);
            lens.transform.localPosition = new Vector3(0f, 0f, 0.19f);
            lens.AddComponent<MeshFilter>().sharedMesh = Primitives.Get("cone");
            lens.AddComponent<MeshRenderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
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
            lookThrough = it;
            if (it == null) return;
            foreach (var r in it.GetComponentsInChildren<Renderer>()) r.enabled = false;
            gizmo.Show(null);
            highlighter.Clear();
            lookBar.SetActive(true);
            Toast("Mirando por «" + it.gameObject.name + "»: muévela con los campos de Propiedades o anima sus claves");
        }

        void StopLookThrough()
        {
            var it = lookThrough;
            lookThrough = null;
            if (lookBar != null) lookBar.SetActive(false);
            if (it != null) ApplyLook(it);
            cam.fieldOfView = viewFov;
            UpdateCamera();
            if (selected != null && !poseMode) highlighter.Show(selected);
            ShowGizmo();
        }

        float viewFov = 50f;

        void UpdateLookThrough()
        {
            if (lookThrough == null)
            {
                viewFov = cam.fieldOfView;
                return;
            }
            if (!items.Contains(lookThrough)) { StopLookThrough(); return; }
            cam.transform.SetPositionAndRotation(lookThrough.transform.position, lookThrough.transform.rotation);
            cam.fieldOfView = lookThrough.fov;
        }

        void BuildLookBar()
        {
            var b = AtlasTheme.TextButton(viewArea, "camera", "Salir de la cámara", StopLookThrough, true, 48f, 17);
            AtlasTheme.Pin((RectTransform)b.transform, 0.5f, 1f, new Vector2(0f, -84f), new Vector2(230f, 48f));
            lookBar = b.gameObject;
            lookBar.SetActive(false);
        }

        void BuildCameraSection(AtlasItem it)
        {
            var box = Sec("Cámara", "camera");
            AtlasTheme.SliderRow(box, "Campo visual", 10f, 120f, it.fov, "0", x => { it.fov = x; }, () => Commit(true));
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
                Toast("Cámara movida a la vista actual");
            });
            AtlasTheme.Switch(box, "Renderizar desde esta cámara", it.renderCam, on => SetRenderCamera(it, on));
            AtlasTheme.TextButton(box, "view", lookThrough == it ? "Salir de la cámara" : "Mirar por esta cámara", () =>
            {
                if (lookThrough == it) StopLookThrough();
                else LookThrough(it);
                RefreshProps();
            }, lookThrough != it, 46f, 16);
            Hint(box, "Anímala con la clave amarilla como cualquier objeto: el render de la animación sigue su recorrido.");
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
                    position = new Vector3(0f, 1f, 0f),
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
