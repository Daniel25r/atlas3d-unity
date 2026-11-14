// ATLAS 3D (Unity) - «Ambiente rápido»: the same one-tap looks as the
// Godot version (sky or plain background, sun, ambient light, fog and the
// image effects), done with URP's post-processing: bloom, ACES / neutral
// tone mapping, colour adjustments, vignette and film grain.
using UnityEngine;
using URP = UnityEngine.Rendering.Universal;
using Vol = UnityEngine.Rendering;

namespace Atlas
{
    public class AmbiencePreset
    {
        public string id, name;
        public string sky = "";          // "" plain colour, or day / sunset / night / clouds
        // Cloud sky
        public Color skyTop = new Color(0.16f, 0.42f, 0.88f), skyHorizon = new Color(0.72f, 0.86f, 1f);
        public float cloudCover = 0.5f, skyExposure = 1f;
        public Color background;
        public Color ambSky, ambEquator, ambGround;
        public Color sun = Color.white;
        public float sunIntensity = 1.2f;
        public Vector3 sunEuler = new Vector3(50f, -35f, 0f);
        public Color fogColor = Color.gray;
        public bool aces;
        public Color filter = Color.white;
        // Image effect defaults (the sliders start here)
        public float exposure, contrast, saturation, bloom, vignette, grain, fog;
    }

    public static class Ambience
    {
        static Color K(float kelvin) { return Mathf.CorrelatedColorTemperatureToRGB(kelvin); }
        static Color C(float r, float g, float b) { return new Color(r, g, b); }

        public static readonly AmbiencePreset[] Presets =
        {
            new AmbiencePreset { id = "studio", name = "Estudio ATLAS", background = AtlasTheme.Viewport,
                ambSky = C(0.55f, 0.58f, 0.66f), ambEquator = C(0.38f, 0.39f, 0.43f), ambGround = C(0.16f, 0.16f, 0.18f),
                sun = C(1f, 0.97f, 0.92f), sunIntensity = 1.25f, bloom = 0.1f },
            // Frutiger Aero: bright optimistic sky, soft clouds, glossy glow.
            new AmbiencePreset { id = "aero", name = "Frutiger Aero", sky = "clouds",
                skyTop = C(0.12f, 0.42f, 0.95f), skyHorizon = C(0.78f, 0.92f, 1f), cloudCover = 0.55f, skyExposure = 1.1f,
                ambSky = C(0.7f, 0.85f, 1f), ambEquator = C(0.55f, 0.68f, 0.82f), ambGround = C(0.3f, 0.42f, 0.38f),
                sun = C(1f, 0.97f, 0.9f), sunIntensity = 1.45f, sunEuler = new Vector3(52f, -30f, 0f), fogColor = C(0.75f, 0.88f, 1f),
                exposure = 0.2f, contrast = 6f, saturation = 22f, bloom = 0.55f, fog = 0.003f },
            new AmbiencePreset { id = "clouds", name = "Cielo con nubes", sky = "clouds",
                skyTop = C(0.2f, 0.45f, 0.82f), skyHorizon = C(0.7f, 0.8f, 0.92f), cloudCover = 0.45f,
                ambSky = C(0.62f, 0.72f, 0.88f), ambEquator = C(0.48f, 0.52f, 0.58f), ambGround = C(0.24f, 0.24f, 0.22f),
                sun = K(5800f), sunIntensity = 1.35f, sunEuler = new Vector3(45f, -40f, 0f), fogColor = C(0.72f, 0.8f, 0.9f),
                contrast = 6f, saturation = 5f, bloom = 0.2f, vignette = 0.12f, fog = 0.004f },
            new AmbiencePreset { id = "realistic", name = "Realista cine", sky = "clouds", aces = true, cloudCover = 0.4f,
                ambSky = C(0.62f, 0.7f, 0.86f), ambEquator = C(0.45f, 0.47f, 0.5f), ambGround = C(0.2f, 0.18f, 0.16f),
                sun = K(5600f), sunIntensity = 1.7f, sunEuler = new Vector3(38f, -40f, 0f), fogColor = C(0.72f, 0.8f, 0.9f),
                exposure = 0.15f, contrast = 12f, saturation = 5f, bloom = 0.3f, vignette = 0.2f, fog = 0.006f },
            new AmbiencePreset { id = "natural", name = "Natural suave", sky = "clouds", cloudCover = 0.35f,
                ambSky = C(0.62f, 0.7f, 0.85f), ambEquator = C(0.48f, 0.5f, 0.55f), ambGround = C(0.25f, 0.23f, 0.2f),
                sun = K(5600f), sunIntensity = 1.1f, sunEuler = new Vector3(45f, -60f, 0f), fogColor = C(0.75f, 0.82f, 0.92f),
                contrast = 6f, saturation = -5f, bloom = 0.08f, vignette = 0.15f, fog = 0.004f },
            new AmbiencePreset { id = "sfm", name = "Estilo SFM", background = C(0.05f, 0.055f, 0.07f), aces = true,
                ambSky = C(0.3f, 0.33f, 0.4f), ambEquator = C(0.18f, 0.19f, 0.22f), ambGround = C(0.06f, 0.06f, 0.07f),
                sun = K(4300f), sunIntensity = 1.5f, sunEuler = new Vector3(35f, 220f, 0f), fogColor = C(0.12f, 0.13f, 0.16f),
                contrast = 12f, saturation = -5f, bloom = 0.35f, vignette = 0.35f, grain = 0.07f, fog = 0.02f },
            new AmbiencePreset { id = "cinema", name = "Cine", sky = "sunset", aces = true, filter = C(1f, 0.94f, 0.86f),
                ambSky = C(0.8f, 0.6f, 0.5f), ambEquator = C(0.45f, 0.35f, 0.3f), ambGround = C(0.15f, 0.12f, 0.1f),
                sun = K(2600f), sunIntensity = 1.6f, sunEuler = new Vector3(10f, 205f, 0f), fogColor = C(0.8f, 0.5f, 0.35f),
                contrast = 12f, saturation = 10f, bloom = 0.45f, vignette = 0.4f, grain = 0.08f, fog = 0.012f },
            new AmbiencePreset { id = "sunset", name = "Atardecer", sky = "sunset", aces = true,
                ambSky = C(0.9f, 0.6f, 0.45f), ambEquator = C(0.5f, 0.38f, 0.32f), ambGround = C(0.16f, 0.12f, 0.1f),
                sun = K(2400f), sunIntensity = 1.4f, sunEuler = new Vector3(7f, 215f, 0f), fogColor = C(0.85f, 0.55f, 0.4f),
                exposure = -0.2f, contrast = 5f, saturation = 15f, bloom = 0.25f, vignette = 0.3f, fog = 0.015f },
            new AmbiencePreset { id = "night", name = "Noche", sky = "night",
                ambSky = C(0.16f, 0.22f, 0.4f), ambEquator = C(0.08f, 0.1f, 0.17f), ambGround = C(0.03f, 0.03f, 0.05f),
                sun = K(12000f), sunIntensity = 0.35f, sunEuler = new Vector3(55f, 30f, 0f), fogColor = C(0.05f, 0.07f, 0.12f),
                contrast = 10f, saturation = -10f, bloom = 0.3f, vignette = 0.5f, grain = 0.1f, fog = 0.03f },
            new AmbiencePreset { id = "misty", name = "Noche con niebla", background = C(0.03f, 0.04f, 0.06f), aces = true,
                ambSky = C(0.2f, 0.25f, 0.4f), ambEquator = C(0.1f, 0.12f, 0.18f), ambGround = C(0.03f, 0.03f, 0.05f),
                sun = K(9000f), sunIntensity = 0.5f, sunEuler = new Vector3(30f, 160f, 0f), fogColor = C(0.12f, 0.15f, 0.22f),
                contrast = 8f, saturation = -15f, bloom = 0.5f, vignette = 0.45f, grain = 0.12f, fog = 0.07f },
            new AmbiencePreset { id = "terror", name = "Terror", background = C(0.012f, 0.01f, 0.018f), aces = true,
                ambSky = C(0.12f, 0.12f, 0.2f), ambEquator = C(0.05f, 0.05f, 0.08f), ambGround = C(0.01f, 0.01f, 0.02f),
                sun = C(0.75f, 0.8f, 1f), sunIntensity = 0.45f, sunEuler = new Vector3(60f, 160f, 0f), fogColor = C(0.07f, 0.05f, 0.1f),
                contrast = 25f, saturation = -15f, bloom = 0.35f, vignette = 0.65f, grain = 0.22f, fog = 0.06f },
            new AmbiencePreset { id = "stage", name = "Escenario", background = C(0.02f, 0.02f, 0.03f),
                ambSky = C(0.4f, 0.3f, 0.5f), ambEquator = C(0.15f, 0.12f, 0.2f), ambGround = C(0.03f, 0.03f, 0.04f),
                sun = C(1f, 0.4f, 0.85f), sunIntensity = 1.1f, sunEuler = new Vector3(55f, 150f, 0f), fogColor = C(0.1f, 0.08f, 0.15f),
                contrast = 15f, saturation = 25f, bloom = 0.6f, vignette = 0.4f, grain = 0.05f, fog = 0.04f },
            new AmbiencePreset { id = "anime", name = "Anime", sky = "clouds", skyTop = C(0.1f, 0.4f, 0.95f), skyHorizon = C(0.65f, 0.85f, 1f), cloudCover = 0.5f,
                ambSky = C(0.75f, 0.8f, 0.95f), ambEquator = C(0.6f, 0.62f, 0.7f), ambGround = C(0.35f, 0.33f, 0.35f),
                sun = K(6000f), sunIntensity = 1.4f, sunEuler = new Vector3(50f, -30f, 0f),
                contrast = 5f, saturation = 20f, bloom = 0.15f },
            new AmbiencePreset { id = "comic", name = "Cómic", sky = "day",
                ambSky = C(0.75f, 0.7f, 0.95f), ambEquator = C(0.5f, 0.48f, 0.6f), ambGround = C(0.2f, 0.18f, 0.25f),
                sun = K(5200f), sunIntensity = 1.2f, sunEuler = new Vector3(45f, 40f, 0f),
                exposure = -0.2f, contrast = 18f, saturation = 35f, bloom = 0.1f },
            new AmbiencePreset { id = "retro", name = "Retro VHS", background = C(0.03f, 0.02f, 0.06f), filter = C(1f, 0.9f, 1f),
                ambSky = C(0.45f, 0.3f, 0.7f), ambEquator = C(0.2f, 0.12f, 0.3f), ambGround = C(0.05f, 0.03f, 0.08f),
                sun = C(1f, 0.35f, 0.8f), sunIntensity = 1f, sunEuler = new Vector3(40f, 120f, 0f),
                contrast = 15f, saturation = 30f, bloom = 0.4f, vignette = 0.45f, grain = 0.15f },
        };

        public static AmbiencePreset Find(string id)
        {
            foreach (var p in Presets) if (p.id == id) return p;
            return Presets[0];
        }

        /// Sliders back to the preset's own values.
        public static void Reset(EnvData e, string id)
        {
            var p = Find(id);
            e.set = true;
            e.preset = p.id;
            e.exposure = p.exposure;
            e.contrast = p.contrast;
            e.saturation = p.saturation;
            e.bloom = p.bloom;
            e.vignette = p.vignette;
            e.grain = p.grain;
            e.fog = p.fog;
        }

        // ------------------------------------------------------------------

        static Vol.VolumeProfile profile;
        static URP.Bloom bloom;
        static URP.Tonemapping tone;
        static URP.ColorAdjustments color;
        static URP.Vignette vignette;
        static URP.FilmGrain grain;
        static Material skyMat, cloudMat;

        /// Global post-processing volume and the camera's post switch.
        public static void Init(Camera cam)
        {
            var go = new GameObject("Ambiente");
            var vol = go.AddComponent<Vol.Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            profile = ScriptableObject.CreateInstance<Vol.VolumeProfile>();
            bloom = profile.Add<URP.Bloom>(true);
            tone = profile.Add<URP.Tonemapping>(true);
            color = profile.Add<URP.ColorAdjustments>(true);
            vignette = profile.Add<URP.Vignette>(true);
            grain = profile.Add<URP.FilmGrain>(true);
            vol.sharedProfile = profile;
            try
            {
                var data = URP.CameraExtensions.GetUniversalAdditionalCameraData(cam);
                data.renderPostProcessing = true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("ATLAS: sin efectos de imagen: " + e.Message);
            }
            var tmpl = Resources.Load<Material>("AtlasSky");
            if (tmpl != null) skyMat = new Material(tmpl);
            var ct = Resources.Load<Material>("AtlasClouds");
            if (ct != null && ct.shader != null && ct.shader.isSupported) cloudMat = new Material(ct);
        }

        public static void Apply(EnvData e, Camera cam, Light sun)
        {
            if (!e.set) Reset(e, string.IsNullOrEmpty(e.preset) ? "studio" : e.preset);
            var p = Find(e.preset);

            // Background: cloud sky, procedural sky or a plain colour.
            if (p.sky == "clouds" && cloudMat != null)
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                RenderSettings.skybox = cloudMat;
                cloudMat.SetColor("_TopColor", p.skyTop);
                cloudMat.SetColor("_HorizonColor", p.skyHorizon);
                cloudMat.SetColor("_GroundColor", Color.Lerp(p.skyHorizon, p.ambGround, 0.6f));
                cloudMat.SetFloat("_Cover", p.cloudCover);
                cloudMat.SetFloat("_Exposure", p.skyExposure);
                cloudMat.SetColor("_SunColor", p.sun);
                if (sun != null)
                {
                    sun.transform.rotation = Quaternion.Euler(p.sunEuler);
                    Vector3 sd = -sun.transform.forward;
                    cloudMat.SetVector("_SunDir", new Vector4(sd.x, sd.y, sd.z, 0f));
                }
            }
            else if (p.sky != "" && skyMat != null)
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                RenderSettings.skybox = skyMat;
                if (p.sky == "day" || p.sky == "clouds")
                {
                    skyMat.SetColor("_SkyTint", new Color(0.5f, 0.55f, 0.65f));
                    skyMat.SetColor("_GroundColor", new Color(0.37f, 0.35f, 0.34f));
                    skyMat.SetFloat("_AtmosphereThickness", 0.9f);
                    skyMat.SetFloat("_Exposure", 1.25f);
                    skyMat.SetFloat("_SunSize", 0.04f);
                }
                else if (p.sky == "sunset")
                {
                    skyMat.SetColor("_SkyTint", new Color(0.55f, 0.45f, 0.45f));
                    skyMat.SetColor("_GroundColor", new Color(0.25f, 0.18f, 0.16f));
                    skyMat.SetFloat("_AtmosphereThickness", 1.6f);
                    skyMat.SetFloat("_Exposure", 1.35f);
                    skyMat.SetFloat("_SunSize", 0.07f);
                }
                else
                {
                    skyMat.SetColor("_SkyTint", new Color(0.12f, 0.16f, 0.3f));
                    skyMat.SetColor("_GroundColor", new Color(0.02f, 0.02f, 0.03f));
                    skyMat.SetFloat("_AtmosphereThickness", 0.4f);
                    skyMat.SetFloat("_Exposure", 0.18f);
                    skyMat.SetFloat("_SunSize", 0.02f);
                }
            }
            else
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = p.background;
                RenderSettings.skybox = null;
            }

            // Sun and ambient light
            if (sun != null)
            {
                sun.color = p.sun;
                sun.intensity = p.sunIntensity;
                sun.transform.rotation = Quaternion.Euler(p.sunEuler);
                RenderSettings.sun = sun;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = p.ambSky;
            RenderSettings.ambientEquatorColor = p.ambEquator;
            RenderSettings.ambientGroundColor = p.ambGround;

            // Fog
            RenderSettings.fog = e.fog > 0.0005f;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = e.fog;
            RenderSettings.fogColor = p.sky == "" ? Color.Lerp(p.fogColor, p.background, 0.3f) : p.fogColor;

            // Image effects
            if (profile == null) return;
            bloom.active = e.bloom > 0.01f;
            bloom.intensity.Override(e.bloom * 2.5f);
            bloom.threshold.Override(0.95f);
            bloom.scatter.Override(0.7f);
            tone.mode.Override(p.aces ? URP.TonemappingMode.ACES : URP.TonemappingMode.Neutral);
            color.postExposure.Override(e.exposure);
            color.contrast.Override(e.contrast);
            color.saturation.Override(e.saturation);
            color.colorFilter.Override(p.filter);
            vignette.active = e.vignette > 0.01f;
            vignette.intensity.Override(e.vignette);
            vignette.smoothness.Override(0.45f);
            grain.active = e.grain > 0.01f;
            grain.type.Override(URP.FilmGrainLookup.Medium1);
            grain.intensity.Override(e.grain);
        }
    }
}
