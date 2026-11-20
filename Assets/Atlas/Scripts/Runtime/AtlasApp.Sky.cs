// ATLAS 3D (Unity) - «Cielo propio», like the Godot version: any picture as
// the sky. A 360° panorama (2:1, from Poly Haven, a 360 camera or a game)
// wraps all around; a normal photo is stretched around too. The sky also
// lights the scene with its colours, can be turned and made brighter or
// darker, and is saved in the project.
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas
{
    public partial class AtlasApp
    {
        Material panoSky;

        /// After the ambience: the own sky on top of it (when there is one).
        void ApplyCustomSky()
        {
            if (string.IsNullOrEmpty(env.skyImage)) return;
            var tex = LoadTexture(env.skyImage);
            if (tex == null) return;
            if (panoSky == null)
            {
                var tmpl = Resources.Load<Material>("AtlasSkyPanoramic");
                if (tmpl == null || tmpl.shader == null || !tmpl.shader.isSupported) return;
                panoSky = new Material(tmpl);
            }
            tex.wrapModeU = TextureWrapMode.Repeat;
            tex.wrapModeV = TextureWrapMode.Clamp;
            panoSky.SetTexture("_MainTex", tex);
            if (panoSky.HasProperty("_Mapping")) panoSky.SetFloat("_Mapping", 1f);      // latitude-longitude
            if (panoSky.HasProperty("_ImageType")) panoSky.SetFloat("_ImageType", 0f);  // 360°
            panoSky.SetFloat("_Rotation", Mathf.Repeat(env.skyRotation, 360f));
            panoSky.SetFloat("_Exposure", Mathf.Clamp(env.skyExposure <= 0f ? 1f : env.skyExposure, 0.05f, 8f));
            panoSky.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1f));
            cam.clearFlags = CameraClearFlags.Skybox;
            RenderSettings.skybox = panoSky;
            // The scene's ambient light comes from the picture.
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            try { DynamicGI.UpdateEnvironment(); } catch (System.Exception) { }
        }

        void PickCustomSky()
        {
            if (store == null || NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path) || store == null) return;
                string file;
                try { file = store.AddTextureFile(path); }
                catch (System.Exception e) { Toast("No se pudo usar la imagen: " + e.Message, true); return; }
                var t = LoadTexture(file);
                if (t == null) { Toast("Esa imagen no se puede leer (usa PNG o JPG)", true); return; }
                env.skyImage = file;
                if (env.skyExposure <= 0f) env.skyExposure = 1f;
                ApplyEnv();
                Commit(true);
                RefreshProps();
                float ratio = (float)t.width / Mathf.Max(1, t.height);
                Toast(Mathf.Abs(ratio - 2f) < 0.15f
                    ? "Cielo puesto: ilumina la escena con sus colores. Gíralo para elegir dónde queda cada parte"
                    : "Cielo puesto. Para que no se vea estirado, usa una imagen 360° (el doble de ancha que de alta)");
            }, new string[] { "image/*" });
        }

        void BuildCustomSkyControls(Transform sky)
        {
            var g = AtlasTheme.Grid(sky, 2, InnerWidth, 40f);
            AtlasTheme.TextButton(g, "image", string.IsNullOrEmpty(env.skyImage) ? "Cielo propio (imagen)…" : "Cambiar cielo…", PickCustomSky, string.IsNullOrEmpty(env.skyImage), 40f, 14);
            if (!string.IsNullOrEmpty(env.skyImage))
            {
                AtlasTheme.TextButton(g, "close", "Quitar cielo propio", () => { env.skyImage = ""; ApplyEnv(); Commit(true); RefreshProps(); }, false, 40f, 14);
                AtlasTheme.SliderRow(sky, "Girar el cielo", 0f, 360f, env.skyRotation, "0", x => { env.skyRotation = x; ApplyEnv(); }, () => Commit(true));
                AtlasTheme.SliderRow(sky, "Brillo del cielo", 0.1f, 3f, env.skyExposure <= 0f ? 1f : env.skyExposure, "0.00", x => { env.skyExposure = x; ApplyEnv(); }, () => Commit(true));
            }
            Hint(sky, "Tu propia imagen de cielo. Mejor una imagen 360° (panorama el doble de ancho que de alto, como las de Poly Haven); también ilumina la escena con sus colores.");
        }
    }
}
