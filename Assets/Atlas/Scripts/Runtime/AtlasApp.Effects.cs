// ATLAS 3D (Unity) - particle effects and animated weather.
// Effects: rain, snow, dust, smoke, sparks, fire, explosion, bubbles,
// confetti and magic. Each one is computed for the timeline's time
// (ParticleSystem.Simulate with a fixed seed), so what you see when you move
// through the animation is exactly what comes out in the render.
// Weather: rain, storm with lightning, snow and dense fog, around the camera.
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        public static readonly string[] FxPresets = { "rain", "snow", "dust", "smoke", "sparks", "fire", "explosion", "bubbles", "confetti", "magic" };
        public static readonly string[] FxNames = { "Lluvia", "Nieve", "Polvo", "Humo", "Chispas", "Fuego", "Explosión", "Burbujas", "Confeti", "Magia" };

        readonly Dictionary<ParticleSystem, float> fxTime = new Dictionary<ParticleSystem, float>();
        Material particleTemplate;
        readonly Dictionary<string, Texture2D> fxTex = new Dictionary<string, Texture2D>();
        GameObject weatherGo;
        string weatherBuilt = "";

        // ------------------------------------------------------------------
        // Textures made here (no image files to ship)

        Texture2D FxTexture(string kind)
        {
            Texture2D t;
            if (fxTex.TryGetValue(kind, out t) && t != null) return t;
            int w = kind == "streak" ? 16 : 64, h = 64;
            t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            t.wrapMode = TextureWrapMode.Clamp;
            var rnd = new System.Random(7);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a;
                    switch (kind)
                    {
                        case "streak": a = Mathf.Clamp01(1f - Mathf.Abs(u)) * Mathf.Clamp01(1f - Mathf.Abs(v) * 0.9f); a *= a; break;
                        case "puff":
                            a = Mathf.Clamp01(1f - r);
                            a = a * a * (0.75f + 0.25f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f));
                            break;
                        case "star":
                            a = Mathf.Clamp01(1f - r * 1.6f) + Mathf.Clamp01(1f - Mathf.Abs(u) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(v)) + Mathf.Clamp01(1f - Mathf.Abs(v) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(u));
                            a = Mathf.Clamp01(a);
                            break;
                        case "square": a = Mathf.Abs(u) < 0.8f && Mathf.Abs(v) < 0.5f ? 1f : 0f; break;
                        case "bubble":
                            a = r < 1f ? Mathf.Clamp01((r - 0.78f) * 6f) * Mathf.Clamp01((1f - r) * 12f) + 0.12f : 0f;
                            float hl = Mathf.Clamp01(1f - new Vector2(u + 0.35f, v - 0.4f).magnitude * 5f);
                            a = Mathf.Clamp01(a + hl);
                            break;
                        default: a = Mathf.Clamp01(1f - r); a = a * a; break;   // soft dot
                    }
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply(true, false);
            fxTex[kind] = t;
            return t;
        }

        Material FxMaterial(string tex, bool additive)
        {
            if (particleTemplate == null) particleTemplate = Resources.Load<Material>("AtlasParticle");
            var m = new Material(particleTemplate != null ? particleTemplate : unlitTemplate);
            m.SetTexture("_BaseMap", FxTexture(tex));
            m.SetFloat("_SrcBlend", additive ? 1f : 5f);    // One : SrcAlpha
            m.SetFloat("_DstBlend", additive ? 1f : 10f);   // One : OneMinusSrcAlpha
            return m;
        }

        // ------------------------------------------------------------------
        // Building an effect

        GameObject MakeFx()
        {
            var go = new GameObject();
            // Something to tap and move: a small glowing marker (hidden in renders).
            var mark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mark.name = "Marca";
            Destroy(mark.GetComponent<Collider>());
            mark.transform.SetParent(go.transform, false);
            mark.transform.localScale = Vector3.one * 0.25f;
            mark.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return go;
        }

        /// Sets every module of the effect for its style and strength.
        void ConfigureFx(ParticleSystem ps, string preset, float amount, int seed, bool weather)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)Mathf.Max(1, seed);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 5f;
            main.startDelay = 0f;
            main.gravityModifier = 0f;
            main.startRotation = 0f;
            main.startSize3D = false;
            main.simulationSpace = weather || preset == "rain" || preset == "snow" ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission;
            em.enabled = true;
            em.SetBursts(new ParticleSystem.Burst[0]);
            var sh = ps.shape;
            sh.enabled = true;
            sh.rotation = Vector3.zero;
            sh.position = Vector3.zero;
            var vel = ps.velocityOverLifetime;
            vel.enabled = false;
            var noise = ps.noise;
            noise.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = false;
            var rot = ps.rotationOverLifetime;
            rot.enabled = false;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.minParticleSize = 0f;
            r.maxParticleSize = 2f;
            float k = Mathf.Clamp(amount, 0.05f, 4f);
            float area = weather ? 26f : 8f;
            switch (preset)
            {
                case "rain":
                    main.maxParticles = 6000;
                    main.startLifetime = 1.1f;
                    main.startSpeed = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.04f);
                    main.startColor = new Color(0.78f, 0.84f, 0.95f, 0.55f);
                    em.rateOverTime = (weather ? 2600f : 700f) * k;
                    sh.shapeType = ParticleSystemShapeType.Box;
                    sh.scale = new Vector3(area, 0.1f, area);
                    sh.position = new Vector3(0f, 9f, 0f);
                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.Local;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.6f, -0.6f);
                    vel.y = new ParticleSystem.MinMaxCurve(-17f, -17f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                    r.renderMode = ParticleSystemRenderMode.Stretch;
                    r.velocityScale = 0.035f;
                    r.lengthScale = 1f;
                    r.sharedMaterial = FxMaterial("streak", false);
                    break;
                case "snow":
                    main.maxParticles = 5000;
                    main.startLifetime = 7f;
                    main.startSpeed = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.11f);
                    main.startColor = new Color(1f, 1f, 1f, 0.9f);
                    em.rateOverTime = (weather ? 700f : 250f) * k;
                    sh.shapeType = ParticleSystemShapeType.Box;
                    sh.scale = new Vector3(area, 0.1f, area);
                    sh.position = new Vector3(0f, 7f, 0f);
                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.Local;
                    vel.x = new ParticleSystem.MinMaxCurve(0.1f, 0.1f);
                    vel.y = new ParticleSystem.MinMaxCurve(-1.1f, -1.1f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                    noise.enabled = true;
                    noise.strength = 0.5f;
                    noise.frequency = 0.4f;
                    r.sharedMaterial = FxMaterial("dot", false);
                    break;
                case "dust":
                    main.maxParticles = 800;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
                    main.startColor = new Color(1f, 0.93f, 0.78f, 0.6f);
                    em.rateOverTime = 45f * k;
                    sh.shapeType = ParticleSystemShapeType.Sphere;
                    sh.radius = 3f;
                    noise.enabled = true;
                    noise.strength = 0.15f;
                    noise.frequency = 0.3f;
                    FadeInOut(col, new Color(1f, 0.93f, 0.78f));
                    r.sharedMaterial = FxMaterial("dot", true);
                    break;
                case "smoke":
                    main.maxParticles = 300;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    main.startColor = new Color(0.55f, 0.55f, 0.58f, 0.55f);
                    em.rateOverTime = 14f * k;
                    sh.shapeType = ParticleSystemShapeType.Cone;
                    sh.angle = 12f;
                    sh.radius = 0.25f;
                    sh.rotation = new Vector3(-90f, 0f, 0f);
                    size.enabled = true;
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.2f));
                    rot.enabled = true;
                    rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
                    noise.enabled = true;
                    noise.strength = 0.35f;
                    noise.frequency = 0.25f;
                    FadeInOut(col, new Color(0.6f, 0.6f, 0.63f));
                    r.sharedMaterial = FxMaterial("puff", false);
                    break;
                case "sparks":
                    main.maxParticles = 800;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
                    main.gravityModifier = 0.9f;
                    main.startColor = new Color(1f, 0.75f, 0.3f, 1f);
                    em.rateOverTime = 70f * k;
                    sh.shapeType = ParticleSystemShapeType.Cone;
                    sh.angle = 28f;
                    sh.radius = 0.05f;
                    sh.rotation = new Vector3(-90f, 0f, 0f);
                    col.enabled = true;
                    col.color = Grad(new[] { new Color(1f, 0.95f, 0.7f), new Color(1f, 0.55f, 0.15f), new Color(0.9f, 0.2f, 0.05f) }, new[] { 1f, 1f, 0f });
                    r.renderMode = ParticleSystemRenderMode.Stretch;
                    r.velocityScale = 0.06f;
                    r.lengthScale = 1f;
                    r.sharedMaterial = FxMaterial("dot", true);
                    break;
                case "fire":
                    main.maxParticles = 400;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 1.9f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    main.startColor = Color.white;
                    em.rateOverTime = 55f * k;
                    sh.shapeType = ParticleSystemShapeType.Cone;
                    sh.angle = 6f;
                    sh.radius = 0.22f;
                    sh.rotation = new Vector3(-90f, 0f, 0f);
                    size.enabled = true;
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));
                    noise.enabled = true;
                    noise.strength = 0.4f;
                    noise.frequency = 1.2f;
                    col.enabled = true;
                    col.color = Grad(new[] { new Color(1f, 0.95f, 0.6f), new Color(1f, 0.55f, 0.1f), new Color(0.85f, 0.15f, 0.05f) }, new[] { 0.9f, 0.7f, 0f });
                    r.sharedMaterial = FxMaterial("puff", true);
                    break;
                case "explosion":
                    main.maxParticles = 600;
                    main.loop = false;
                    main.duration = 2f;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.6f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    main.startColor = Color.white;
                    em.rateOverTime = 0f;
                    em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(140 * k)), new ParticleSystem.Burst(0.08f, (short)(60 * k)) });
                    sh.shapeType = ParticleSystemShapeType.Sphere;
                    sh.radius = 0.4f;
                    var lim = ps.limitVelocityOverLifetime;
                    lim.enabled = true;
                    lim.limit = 2f;
                    lim.dampen = 0.25f;
                    size.enabled = true;
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 2.6f));
                    col.enabled = true;
                    col.color = Grad(new[] { new Color(1f, 1f, 0.85f), new Color(1f, 0.55f, 0.1f), new Color(0.25f, 0.22f, 0.2f) }, new[] { 1f, 0.9f, 0f });
                    r.sharedMaterial = FxMaterial("puff", false);
                    break;
                case "bubbles":
                    main.maxParticles = 400;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.38f);
                    main.startColor = new Color(0.85f, 0.95f, 1f, 0.85f);
                    em.rateOverTime = 12f * k;
                    sh.shapeType = ParticleSystemShapeType.Box;
                    sh.scale = new Vector3(3f, 0.1f, 3f);
                    sh.rotation = new Vector3(-90f, 0f, 0f);
                    noise.enabled = true;
                    noise.strength = 0.25f;
                    noise.frequency = 0.35f;
                    FadeInOut(col, new Color(0.85f, 0.95f, 1f));
                    r.sharedMaterial = FxMaterial("bubble", false);
                    break;
                case "confetti":
                    main.maxParticles = 1500;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
                    main.startSpeed = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    main.gravityModifier = 0.12f;
                    main.startColor = new ParticleSystem.MinMaxGradient(Grad(new[] { new Color(1f, 0.3f, 0.4f), new Color(1f, 0.85f, 0.2f), new Color(0.3f, 0.85f, 1f), new Color(0.6f, 1f, 0.4f), new Color(0.8f, 0.45f, 1f) }, new[] { 1f, 1f, 1f, 1f, 1f })) { mode = ParticleSystemGradientMode.RandomColor };
                    em.rateOverTime = 110f * k;
                    sh.shapeType = ParticleSystemShapeType.Box;
                    sh.scale = new Vector3(6f, 0.1f, 6f);
                    sh.position = new Vector3(0f, 4f, 0f);
                    rot.enabled = true;
                    rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
                    noise.enabled = true;
                    noise.strength = 0.6f;
                    noise.frequency = 0.8f;
                    r.sharedMaterial = FxMaterial("square", false);
                    break;
                default:   // magic
                    main.maxParticles = 600;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.45f, 1f), new Color(0.35f, 0.9f, 1f));
                    em.rateOverTime = 45f * k;
                    sh.shapeType = ParticleSystemShapeType.Sphere;
                    sh.radius = 0.6f;
                    noise.enabled = true;
                    noise.strength = 0.4f;
                    noise.frequency = 0.9f;
                    size.enabled = true;
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
                    r.sharedMaterial = FxMaterial("star", true);
                    break;
            }
            fxTime.Remove(ps);
        }

        static Gradient Grad(Color[] c, float[] a)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[c.Length];
            var ak = new GradientAlphaKey[a.Length];
            for (int i = 0; i < c.Length; i++) ck[i] = new GradientColorKey(c[i], c.Length == 1 ? 0f : i / (c.Length - 1f));
            for (int i = 0; i < a.Length; i++) ak[i] = new GradientAlphaKey(a[i], a.Length == 1 ? 0f : i / (a.Length - 1f));
            g.SetKeys(ck, ak);
            return g;
        }

        static void FadeInOut(ParticleSystem.ColorOverLifetimeModule col, Color c)
        {
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        // ------------------------------------------------------------------
        // Time

        /// The effect exactly as it is at time `t` (seconds): continued from
        /// the last time when going forward a little, computed again otherwise.
        void SimulateFx(ParticleSystem ps, float t)
        {
            if (ps == null) return;
            t = Mathf.Max(0f, t);
            float last;
            bool have = fxTime.TryGetValue(ps, out last);
            float dt = t - last;
            if (have && dt >= 0f && dt <= 0.25f)
            {
                if (dt > 0f) ps.Simulate(dt, true, false, true);
            }
            else ps.Simulate(t, true, true, true);
            fxTime[ps] = t;
        }

        void UpdateEffects()
        {
            float t = frame / Mathf.Max(1f, fps);
            foreach (var it in items)
            {
                if (it == null || it.kind != "fx") continue;
                var ps = it.GetComponent<ParticleSystem>();
                if (ps == null) continue;
                float local = it.preset == "explosion" ? t - it.fxStart / Mathf.Max(1f, fps) : t;
                if (local < 0f) { ps.Clear(true); fxTime.Remove(ps); continue; }
                SimulateFx(ps, local);
            }
            UpdateWeather(t);
        }

        // ------------------------------------------------------------------
        // Adding

        void AddFx(string preset)
        {
            CloseMenus();
            if (store == null) return;
            count++;
            int i = System.Array.IndexOf(FxPresets, preset);
            var d = new ItemData
            {
                v = 3,
                kind = "fx",
                name = i >= 0 ? FxNames[i] : "Efecto",
                position = new Vector3(target.position.x, preset == "rain" || preset == "snow" || preset == "confetti" ? 0f : 0.2f, target.position.z),
                scale = Vector3.one,
                color = Color.white,
                preset = preset,
                amount = 1f,
                seed = Random.Range(1, 99999),
                fxStart = Mathf.RoundToInt(frame),
            };
            Select(Create(d));
            Commit(true);
            Toast(preset == "explosion"
                ? "Explosión en el fotograma " + Mathf.RoundToInt(frame) + ": mueve la línea de tiempo para verla"
                : "Efecto añadido: muévelo como cualquier objeto; sale igual en el render");
        }

        void BuildFxSection(AtlasItem it)
        {
            var box = Sec("Efecto", "sparkle");
            var g = AtlasTheme.Grid(box, 3, InnerWidth, 38f);
            for (int i = 0; i < FxPresets.Length; i++)
            {
                string p = FxPresets[i];
                AtlasTheme.Chip(g, FxNames[i], () => { it.preset = p; ApplyLook(it); Commit(true); RefreshProps(); }, it.preset == p);
            }
            AtlasTheme.SliderRow(box, "Cantidad", 0.1f, 3f, it.amount, "0.0", x => it.amount = x, () => { ApplyLook(it); Commit(true); });
            if (it.preset == "explosion")
                AtlasTheme.TextButton(box, "key", "Explota en el fotograma actual (" + Mathf.RoundToInt(frame) + ")", () => { it.fxStart = Mathf.RoundToInt(frame); ApplyLook(it); Commit(true); RefreshProps(); }, true, 44f, 15);
            AtlasTheme.TextButton(box, "undo", "Otra variación", () => { it.seed = Random.Range(1, 99999); ApplyLook(it); Commit(true); }, false, 42f, 15);
            Hint(box, "Escálalo con Escalar para que cubra más o menos; gíralo para cambiar hacia dónde sale. Lo que ves al mover la línea de tiempo es justo lo que sale en el render.");
        }

        void ApplyFxLook(AtlasItem it)
        {
            var ps = it.GetComponent<ParticleSystem>();
            if (ps == null) return;
            ConfigureFx(ps, it.preset, it.amount, it.seed, false);
            var mark = it.transform.Find("Marca");
            if (mark != null)
            {
                var mr = mark.GetComponent<Renderer>();
                if (it.material == null) it.material = new Material(unlitTemplate != null ? unlitTemplate : litTemplate);
                it.material.SetColor("_BaseColor", new Color(1f, 0.75f, 0.3f, 1f));
                mr.sharedMaterial = it.material;
            }
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.enabled = it.visible;
            UpdateEffects();
        }

        // ------------------------------------------------------------------
        // Weather

        public static readonly string[] WeatherKinds = { "", "rain", "storm", "snow", "fog" };
        public static readonly string[] WeatherNames = { "Despejado", "Lluvia", "Tormenta con rayos", "Nieve", "Niebla densa" };

        void SetWeather(string w)
        {
            env.weather = w;
            switch (w)
            {
                case "rain": env.fog = Mathf.Max(env.fog, 0.025f); env.exposure = Mathf.Min(env.exposure, -0.2f); break;
                case "storm": env.fog = Mathf.Max(env.fog, 0.035f); env.exposure = Mathf.Min(env.exposure, -0.5f); break;
                case "snow": env.fog = Mathf.Max(env.fog, 0.02f); break;
                case "fog": env.fog = Mathf.Max(env.fog, 0.09f); break;
            }
            ApplyEnv();
            BuildWeather();
            Commit(true);
            RefreshProps();
            Toast(w == "" ? "Clima despejado" : "Clima: " + WeatherNames[System.Array.IndexOf(WeatherKinds, w)] + " (sale en el render)");
        }

        void BuildWeather()
        {
            string want = env != null ? (env.weather ?? "") : "";
            if (want == weatherBuilt && (want == "" || weatherGo != null)) return;
            weatherBuilt = want;
            if (weatherGo != null) Destroy(weatherGo);
            weatherGo = null;
            if (want == "" || want == "fog") return;
            weatherGo = new GameObject("Clima");
            var ps = weatherGo.AddComponent<ParticleSystem>();
            ConfigureFx(ps, want == "snow" ? "snow" : "rain", want == "storm" ? 1.8f : 1f, 4242, true);
        }

        /// Weather follows the camera; lightning flashes at fixed times.
        void UpdateWeather(float t)
        {
            BuildWeather();
            if (weatherGo != null)
            {
                var c = cam.transform.position;
                weatherGo.transform.position = new Vector3(c.x, c.y - 4f, c.z);
                SimulateFx(weatherGo.GetComponent<ParticleSystem>(), t);
            }
            float flash = env != null && env.weather == "storm" ? Lightning(t) : 0f;
            if (sunLight != null && env != null)
            {
                if (flash > 0.001f || lastFlash > 0.001f) sunLight.intensity = sunBase * (1f + flash * 5f);
                lastFlash = flash;
            }
        }

        float lastFlash, sunBase = 1.25f;

        /// Lightning: a few flashes every ~6 s, the same every time.
        static float Lightning(float t)
        {
            float slot = Mathf.Floor(t / 0.5f);
            float best = 0f;
            for (int k = 0; k < 4; k++)
            {
                float s = slot - k;
                if (s < 0f) continue;
                float h = Mathf.Abs(Mathf.Sin(s * 12.9898f + 4.1414f) * 43758.5453f) % 1f;
                if (h > 0.085f) continue;
                float dt = t - s * 0.5f;
                // Double flicker then fade.
                float f = Mathf.Exp(-dt * 6f) * (dt < 0.08f || (dt > 0.14f && dt < 0.22f) ? 1f : 0.35f);
                best = Mathf.Max(best, f);
            }
            return best;
        }

        void BuildWeatherSection()
        {
            var box = Sec("Clima (animado)", "sun", false);
            var g = AtlasTheme.Grid(box, 2, InnerWidth, 40f);
            string cur = env.weather ?? "";
            for (int i = 0; i < WeatherKinds.Length; i++)
            {
                string w = WeatherKinds[i];
                AtlasTheme.Chip(g, (i == 0 ? "Despejado (quitar clima)" : WeatherNames[i]), () => SetWeather(w), cur == w);
            }
            Hint(box, "La lluvia y la nieve van siempre alrededor de la cámara; la tormenta hace relámpagos. Todo se mueve con la línea de tiempo y sale en el render.");
        }
    }
}
