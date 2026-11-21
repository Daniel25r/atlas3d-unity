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
        // Full-screen look (AtlasStylize): 0 none, 1 halftone horror, 2 comic ink, 3 VHS.
        public int stylize;
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
            // Clay / plaster study: dark room, one soft light from above to the side,
            // deep soft shadows and dark corners (sculpture or still-life photo).
            new AmbiencePreset { id = "sculpt", name = "Luz de estudio", background = C(0.11f, 0.11f, 0.115f), aces = true,
                ambSky = C(0.16f, 0.16f, 0.17f), ambEquator = C(0.1f, 0.1f, 0.105f), ambGround = C(0.05f, 0.05f, 0.05f),
                sun = K(5200f), sunIntensity = 2.1f, sunEuler = new Vector3(58f, 38f, 0f),
                exposure = 0.1f, contrast = 18f, saturation = -20f, bloom = 0.05f, vignette = 0.42f, grain = 0.03f },
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
            // Printed horror poster: dark corridor, one hard yellow light,
            // blue-violet shadows and halftone dots.
            new AmbiencePreset { id = "halftone", name = "Pesadilla retro", background = C(0.04f, 0.045f, 0.12f), aces = true, stylize = 1,
                ambSky = C(0.22f, 0.24f, 0.55f), ambEquator = C(0.1f, 0.1f, 0.25f), ambGround = C(0.03f, 0.03f, 0.08f),
                sun = C(1f, 0.92f, 0.55f), sunIntensity = 2.1f, sunEuler = new Vector3(28f, 150f, 0f), fogColor = C(0.08f, 0.08f, 0.2f),
                exposure = 0.25f, contrast = 30f, saturation = 10f, bloom = 0.25f, vignette = 0.35f, fog = 0.025f },
            // Comic book: bright day, flat colours and ink outlines.
            new AmbiencePreset { id = "comic_ink", name = "Cómic tinta", sky = "clouds", stylize = 2,
                skyTop = C(0.15f, 0.5f, 1f), skyHorizon = C(0.7f, 0.9f, 1f), cloudCover = 0.4f, skyExposure = 1.15f,
                ambSky = C(0.8f, 0.82f, 0.95f), ambEquator = C(0.55f, 0.55f, 0.62f), ambGround = C(0.3f, 0.28f, 0.3f),
                sun = K(5600f), sunIntensity = 1.5f, sunEuler = new Vector3(48f, 35f, 0f),
                exposure = 0.1f, contrast = 20f, saturation = 30f, bloom = 0.05f },
            // Found-footage tape: night, cold light, rolling lines.
            new AmbiencePreset { id = "vhs_cursed", name = "VHS maldito", background = C(0.02f, 0.02f, 0.03f), aces = true, stylize = 3,
                ambSky = C(0.25f, 0.28f, 0.35f), ambEquator = C(0.1f, 0.1f, 0.13f), ambGround = C(0.03f, 0.03f, 0.04f),
                sun = C(0.85f, 0.9f, 1f), sunIntensity = 0.9f, sunEuler = new Vector3(40f, 200f, 0f), fogColor = C(0.06f, 0.06f, 0.08f),
                contrast = 15f, saturation = -25f, bloom = 0.35f, vignette = 0.3f, grain = 0.25f, fog = 0.04f },
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
        static URP.DepthOfField dof;
        static URP.MotionBlur motion;
        static URP.ChromaticAberration chroma;
        static URP.SplitToning split;
        static Material skyMat, cloudMat, stylizeMat;
        static EnvData lastEnv;
        /// Renders in progress: motion blur only then (in the editor's view
        /// it smeared everything while turning the camera).
        public static bool Capturing;
        static bool glowWanted;

        /// Something in the scene glows (neon, emission): bloom makes it
        /// shine even in looks with little bloom.
        public static void SetGlow(bool on)
        {
            if (on == glowWanted) return;
            glowWanted = on;
            if (lastEnv != null) ApplyBloom(lastEnv);
        }

        /// The «Cine» render makes depth of field and motion blur itself
        /// (many passes): URP's versions are switched off meanwhile.
        public static void SuspendFilm(bool off)
        {
            if (dof == null || lastEnv == null) return;
            dof.active = !off && lastEnv.dof;
            motion.active = !off && Capturing && lastEnv.motionBlur > 0.01f;
        }

        /// The sky, the sun (or the moon) and the light of an hour of the
        /// day: 6 sunrise, 12 noon, 18.5 sunset, night after 20.
        static void TimeOfDay(EnvData e, Camera cam, Light sun)
        {
            float t = Mathf.Repeat(e.timeOfDay, 24f);
            float h = Mathf.Sin((t - 6f) / 12f * Mathf.PI);           // -1 midnight … 1 noon
            float elev = h * 75f;
            float az = -110f + (t - 6f) * 15f;
            bool night = h < -0.05f;
            float day = Mathf.Clamp01(h * 3f + 0.15f);                 // 0 night … 1 day
            float low = 1f - Mathf.Clamp01(Mathf.Abs(h) * 3.2f);       // near the horizon
            Color top = Color.Lerp(new Color(0.01f, 0.015f, 0.05f), new Color(0.12f, 0.38f, 0.9f), day);
            Color hor = Color.Lerp(new Color(0.04f, 0.06f, 0.13f), new Color(0.68f, 0.82f, 1f), day);
            hor = Color.Lerp(hor, new Color(1f, 0.5f, 0.25f), low * (night ? 0.25f : 0.85f));
            top = Color.Lerp(top, new Color(0.3f, 0.3f, 0.55f), low * 0.4f);
            if (cloudMat != null)
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                RenderSettings.skybox = cloudMat;
                cloudMat.SetColor("_TopColor", top);
                cloudMat.SetColor("_HorizonColor", hor);
                cloudMat.SetColor("_GroundColor", Color.Lerp(hor, new Color(0.08f, 0.08f, 0.08f), 0.6f));
                cloudMat.SetFloat("_Cover", e.cloudCover >= 0f ? e.cloudCover : 0.45f);
                cloudMat.SetFloat("_Exposure", Mathf.Lerp(0.35f, 1.1f, day));
            }
            else
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.Lerp(top, hor, 0.5f);
            }
            if (sun != null)
            {
                // At night the «sun» is the moon: high, cold and soft.
                float le = night ? Mathf.Max(20f, -elev * 0.8f) : Mathf.Max(2f, elev);
                sun.transform.rotation = Quaternion.Euler(le, night ? az + 180f : az, 0f);
                Color sc = night ? new Color(0.55f, 0.65f, 1f) : Color.Lerp(Mathf.CorrelatedColorTemperatureToRGB(2300f), Mathf.CorrelatedColorTemperatureToRGB(5800f), Mathf.Clamp01(h * 1.6f));
                sun.color = sc;
                sun.intensity = night ? 0.28f : Mathf.Lerp(0.5f, 1.45f, Mathf.Clamp01(h * 1.4f));
                if (cloudMat != null)
                {
                    cloudMat.SetColor("_SunColor", sc);
                    Vector3 sd = -sun.transform.forward;
                    cloudMat.SetVector("_SunDir", new Vector4(sd.x, sd.y, sd.z, 0f));
                }
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.06f, 0.08f, 0.16f), new Color(0.62f, 0.72f, 0.9f), day);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.03f, 0.04f, 0.08f), Color.Lerp(new Color(0.45f, 0.48f, 0.55f), new Color(0.7f, 0.45f, 0.35f), low), day);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.01f, 0.01f, 0.02f), new Color(0.22f, 0.2f, 0.18f), day);
            RenderSettings.fogColor = Color.Lerp(hor, top, 0.3f);
        }

        static void ApplyBloom(EnvData e)
        {
            if (bloom == null) return;
            float intensity = e.bloom * 2.5f, threshold = 0.95f;
            if (glowWanted && intensity < 1.1f)
            {
                // Only what glows (HDR light above the threshold) blooms.
                intensity = 1.1f;
                threshold = 1.5f;
            }
            bloom.active = intensity > 0.02f;
            bloom.intensity.Override(intensity);
            bloom.threshold.Override(threshold);
            bloom.scatter.Override(0.7f);
        }

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
            dof = profile.Add<URP.DepthOfField>(true);
            motion = profile.Add<URP.MotionBlur>(true);
            chroma = profile.Add<URP.ChromaticAberration>(true);
            split = profile.Add<URP.SplitToning>(true);
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
            // The renderer's full-screen pass uses this very asset.
            stylizeMat = Resources.Load<Material>("AtlasStylize");
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
                sun.shadows = e.shadowsOff ? LightShadows.None : LightShadows.Soft;
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
            if (e.timeOfDay >= 0f) TimeOfDay(e, cam, sun);
            else if (e.cloudCover >= 0f && cloudMat != null && RenderSettings.skybox == cloudMat) cloudMat.SetFloat("_Cover", e.cloudCover);
            if (stylizeMat != null)
            {
                bool own = e.style >= 0;
                stylizeMat.SetFloat("_Mode", own ? e.style : p.stylize);
                stylizeMat.SetFloat("_Amount", own ? Mathf.Clamp01(e.styleAmount) : 1f);
                stylizeMat.SetFloat("_Pixel", Mathf.Max(0f, e.pixel));
                stylizeMat.SetFloat("_Wide", Mathf.Clamp01(e.wideLens));
                stylizeMat.SetFloat("_Lens", Mathf.Clamp01(e.dirtyLens));
                stylizeMat.SetFloat("_Dust", Mathf.Clamp01(e.dust));
                stylizeMat.SetFloat("_Vintage", Mathf.Clamp01(e.vintage));
            }
            if (profile == null) return;
            lastEnv = e;
            ApplyBloom(e);
            var tm = e.tonemap == 0 ? URP.TonemappingMode.None : e.tonemap == 1 ? URP.TonemappingMode.Neutral : e.tonemap == 2 ? URP.TonemappingMode.ACES
                : (p.aces ? URP.TonemappingMode.ACES : URP.TonemappingMode.Neutral);
            tone.mode.Override(tm);
            // Colour grading («etalonaje»): tints for shadows and lights.
            float gSat = 0f, gCon = 0f;
            Color gSh = Color.gray, gHi = Color.gray, gFilter = p.filter;
            switch (e.grade ?? "")
            {
                case "warm": gSh = new Color(0.55f, 0.45f, 0.4f); gHi = new Color(0.75f, 0.55f, 0.35f); gFilter = p.filter * new Color(1f, 0.95f, 0.88f); break;
                case "cool": gSh = new Color(0.35f, 0.45f, 0.6f); gHi = new Color(0.5f, 0.58f, 0.7f); gFilter = p.filter * new Color(0.9f, 0.96f, 1.05f); break;
                case "teal": gSh = new Color(0.2f, 0.5f, 0.55f); gHi = new Color(0.8f, 0.55f, 0.3f); gCon = 12f; break;
                case "bw": gSat = -100f; gCon = 15f; break;
                case "dramatic": gSat = -30f; gCon = 35f; gSh = new Color(0.35f, 0.4f, 0.5f); break;
                case "dream": gSh = new Color(0.6f, 0.45f, 0.6f); gHi = new Color(0.75f, 0.6f, 0.7f); gCon = -15f; gSat = 10f; break;
            }
            split.active = gSh != Color.gray || gHi != Color.gray;
            split.shadows.Override(gSh);
            split.highlights.Override(gHi);
            split.balance.Override(0f);
            color.postExposure.Override(e.exposure);
            color.contrast.Override(Mathf.Clamp(e.contrast + gCon, -100f, 100f));
            color.saturation.Override(Mathf.Clamp(e.saturation + gSat, -100f, 100f));
            color.colorFilter.Override(gFilter);
            vignette.active = e.vignette > 0.01f;
            vignette.intensity.Override(e.vignette);
            vignette.smoothness.Override(0.45f);
            grain.active = e.grain > 0.01f;
            grain.type.Override(URP.FilmGrainLookup.Medium1);
            grain.intensity.Override(e.grain);
            // Film camera
            dof.active = e.dof;
            dof.mode.Override(URP.DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(Mathf.Max(0.1f, e.focus));
            dof.aperture.Override(Mathf.Lerp(16f, 1.2f, Mathf.Clamp01(e.dofBlur)));
            dof.focalLength.Override(Mathf.Lerp(35f, 120f, Mathf.Clamp01(e.dofBlur)));
            motion.active = Capturing && e.motionBlur > 0.01f;
            motion.intensity.Override(Mathf.Clamp01(e.motionBlur));
            chroma.active = e.chroma > 0.01f;
            chroma.intensity.Override(Mathf.Clamp01(e.chroma));
        }
    }
}
