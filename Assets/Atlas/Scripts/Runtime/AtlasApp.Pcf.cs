// ATLAS 3D (Unity) - Source / SFM particles (.pcf) in the scene.
// A .pcf system (and its children) becomes one «fx» item; each system
// inside it is a child particle system made from the numbers PcfImport
// read (lifetime, size, colours, fades, speed, gravity, drag, pull, swirl).
// It runs on the timeline like the other effects and starts at the frame
// where it was added (or the one you choose).
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        void ImportPcf(string path)
        {
            CloseMenus();
            if (store == null) return;
            List<KeyValuePair<string, List<int>>> groups;
            List<Import.PcfFx> list;
            try { list = Import.PcfImport.Load(path, out groups); }
            catch (System.Exception e) { Toast("No se pudo importar: " + e.Message, true); return; }

            AtlasItem first = null;
            int n = 0;
            float off = 0f;
            foreach (var g in groups)
            {
                var systems = new List<Import.PcfFx>();
                foreach (int i in g.Value) systems.Add(list[i].Copy());
                if (systems.Count == 0) continue;
                count++;
                string nm = string.IsNullOrEmpty(g.Key) ? Path.GetFileNameWithoutExtension(path) : g.Key.Trim('_', ' ');
                if (nm.Length > 40) nm = nm.Substring(0, 40);
                var d = new ItemData
                {
                    v = 3,
                    kind = "fx",
                    name = nm,
                    position = new Vector3(target.position.x + off, 1f, target.position.z),
                    scale = Vector3.one,
                    color = Color.white,
                    preset = "pcf",
                    amount = 1f,
                    seed = Random.Range(1, 99999),
                    fxStart = Mathf.RoundToInt(frame),
                    pcf = systems,
                };
                var it = Create(d);
                if (first == null) first = it;
                n += systems.Count;
                off += 1.5f;
            }
            if (first == null) { Toast("Este .pcf no emite partículas", true); return; }
            Select(first);
            Commit(true);
            Toast("Partículas de SFM: " + groups.Count + (groups.Count == 1 ? " efecto" : " efectos") + " (" + n + " sistemas). Empiezan en el fotograma " + Mathf.RoundToInt(frame) + "; el aspecto de cada partícula es aproximado (el .pcf no trae sus imágenes)");
        }

        /// The item's own particle system only holds the .pcf's systems.
        void ApplyPcf(AtlasItem it, ParticleSystem ps)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 600f;
            main.maxParticles = 1;
            var em = ps.emission;
            em.enabled = false;
            var sh = ps.shape;
            sh.enabled = false;
            var rr = ps.GetComponent<ParticleSystemRenderer>();
            if (rr != null) rr.enabled = false;

            var list = it.pcf ?? new List<Import.PcfFx>();
            for (int i = 0; i < list.Count; i++)
            {
                string cn = "pcf" + i;
                var t = it.transform.Find(cn);
                ParticleSystem cps;
                if (t == null)
                {
                    var go = new GameObject(cn);
                    go.transform.SetParent(it.transform, false);
                    cps = go.AddComponent<ParticleSystem>();
                    var cm = cps.main;
                    cm.playOnAwake = false;
                }
                else cps = t.GetComponent<ParticleSystem>();
                ConfigurePcf(cps, list[i], it.amount, it.seed + i * 7919);
                if (it.fx != null && it.fx.on) ApplyFxCustom(cps, it.fx);
            }
            RemovePcfChildren(it, list.Count);
            fxTime.Clear();
        }

        void RemovePcfChildren(AtlasItem it, int keep)
        {
            for (int i = keep; i < 64; i++)
            {
                var t = it.transform.Find("pcf" + i);
                if (t == null) break;
                t.SetParent(null);
                Destroy(t.gameObject);
            }
        }

        static Color PcfColor(float[] c)
        {
            return c != null && c.Length >= 4 ? new Color(c[0], c[1], c[2], c[3]) : Color.white;
        }

        void ConfigurePcf(ParticleSystem ps, Import.PcfFx f, float amount, int seed)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)Mathf.Max(1, seed);
            float k = Mathf.Clamp(amount, 0.05f, 4f);
            float lifeAvg = (f.lifeMin + f.lifeMax) * 0.5f;

            var main = ps.main;
            main.playOnAwake = false;
            float emitEnd = f.rateDuration > 0f ? f.rateStart + f.rateDuration : 0f;
            main.loop = !f.oneShot;
            main.duration = f.oneShot ? Mathf.Max(0.05f, Mathf.Max(emitEnd, f.burstTime + 0.02f)) : 600f;
            main.startDelay = Mathf.Max(0f, f.delay);
            main.startLifetime = new ParticleSystem.MinMaxCurve(f.lifeMin, f.lifeMax);
            main.startSpeed = f.speedMax > 0f ? new ParticleSystem.MinMaxCurve(f.speedMin, f.speedMax) : new ParticleSystem.MinMaxCurve(0f);
            main.startSize3D = false;
            main.startSize = new ParticleSystem.MinMaxCurve(Mathf.Max(0.001f, f.sizeMin), Mathf.Max(0.001f, f.sizeMax));
            main.startRotation = f.randomRotation ? new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f) : new ParticleSystem.MinMaxCurve(0f);
            main.startColor = new ParticleSystem.MinMaxGradient(PcfColor(f.color1), PcfColor(f.color2));
            main.gravityModifier = 0f;
            main.simulationSpace = f.local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = Mathf.Clamp(Mathf.RoundToInt(f.maxParticles * Mathf.Max(1f, k)), 1, 5000);

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = f.rate * k;
            em.rateOverDistance = 0f;
            em.SetBursts(f.burst > 0
                ? new[] { new ParticleSystem.Burst(Mathf.Max(0f, f.burstTime), (short)Mathf.Clamp(Mathf.RoundToInt(f.burst * k), 1, 5000)) }
                : new ParticleSystem.Burst[0]);

            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.position = Vector3.zero;
            sh.rotation = Vector3.zero;
            sh.scale = Vector3.one;
            sh.radius = Mathf.Max(0.0001f, f.spawnMax);
            sh.radiusThickness = f.spawnMax > 0.0001f ? Mathf.Clamp01(1f - f.spawnMin / f.spawnMax) : 1f;
            sh.randomDirectionAmount = 0f;

            // Initial speeds (per axis, as in SFM), pull to the centre, swirl.
            var vel = ps.velocityOverLifetime;
            bool hasVel = false;
            for (int i = 0; i < 3; i++) if (Mathf.Abs(f.velMin[i]) + Mathf.Abs(f.velMax[i]) > 1e-4f) hasVel = true;
            vel.enabled = hasVel || f.pull > 0f || f.twist > 0f;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(f.velMin[0], f.velMax[0]);
            vel.y = new ParticleSystem.MinMaxCurve(f.velMin[1], f.velMax[1]);
            vel.z = new ParticleSystem.MinMaxCurve(f.velMin[2], f.velMax[2]);
            float pullV = Mathf.Min(40f, f.pull * lifeAvg);
            vel.radial = pullV > 0f ? new ParticleSystem.MinMaxCurve(pullV, AnimationCurve.Linear(0f, 0f, 1f, -1f)) : new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalX = 0f;
            vel.orbitalY = Mathf.Clamp(f.twist, 0f, 8f);
            vel.orbitalZ = 0f;
            // Drag slows those speeds down as the particle ages.
            if (f.drag > 0f && hasVel)
            {
                var c = new AnimationCurve();
                for (int i = 0; i <= 6; i++) { float x = i / 6f; c.AddKey(x, Mathf.Exp(-f.drag * lifeAvg * x)); }
                vel.speedModifier = new ParticleSystem.MinMaxCurve(1f, c);
            }
            else vel.speedModifier = 1f;

            var force = ps.forceOverLifetime;
            bool grav = Mathf.Abs(f.gravity[0]) + Mathf.Abs(f.gravity[1]) + Mathf.Abs(f.gravity[2]) > 1e-4f;
            force.enabled = grav;
            force.space = ParticleSystemSimulationSpace.World;
            force.x = f.gravity[0]; force.y = f.gravity[1]; force.z = f.gravity[2];

            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = f.drag > 0f && (grav || f.speedMax > 0f);
            lim.limit = 1000f;
            lim.dampen = 0f;
            lim.drag = f.drag;

            var noise = ps.noise;
            noise.enabled = f.noise > 0.01f;
            noise.strength = Mathf.Clamp(f.noise, 0f, 10f);
            noise.frequency = 0.8f;
            noise.scrollSpeed = 0.5f;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            // Colour fade and alpha fade in / out.
            var col = ps.colorOverLifetime;
            var ck = new List<GradientColorKey> { new GradientColorKey(Color.white, 0f) };
            if (f.hasColorFade)
            {
                Color a = PcfColor(f.color1), b = PcfColor(f.color2), to = PcfColor(f.colorFade);
                Color avg = (a + b) * 0.5f;
                var tgt = new Color(
                    avg.r > 0.02f ? Mathf.Clamp01(to.r / avg.r) : 1f,
                    avg.g > 0.02f ? Mathf.Clamp01(to.g / avg.g) : 1f,
                    avg.b > 0.02f ? Mathf.Clamp01(to.b / avg.b) : 1f);
                if (f.colorFadeStart > 0.001f) ck.Add(new GradientColorKey(Color.white, f.colorFadeStart));
                ck.Add(new GradientColorKey(tgt, Mathf.Max(f.colorFadeStart + 0.001f, f.colorFadeEnd)));
            }
            else ck.Add(new GradientColorKey(Color.white, 1f));
            var ak = new List<GradientAlphaKey>();
            if (f.fadeIn > 0.001f) { ak.Add(new GradientAlphaKey(0f, 0f)); ak.Add(new GradientAlphaKey(1f, f.fadeIn)); }
            else ak.Add(new GradientAlphaKey(1f, 0f));
            if (f.fadeOut > 0.001f) { ak.Add(new GradientAlphaKey(1f, Mathf.Max(f.fadeIn + 0.001f, 1f - f.fadeOut))); ak.Add(new GradientAlphaKey(0f, 1f)); }
            else ak.Add(new GradientAlphaKey(1f, 1f));
            var grad = new Gradient();
            grad.SetKeys(ck.ToArray(), ak.ToArray());
            col.enabled = f.hasColorFade || f.fadeIn > 0.001f || f.fadeOut > 0.001f;
            col.color = grad;

            var size = ps.sizeOverLifetime;
            bool scales = Mathf.Abs(f.sizeStart - 1f) > 0.01f || Mathf.Abs(f.sizeEnd - 1f) > 0.01f;
            size.enabled = scales;
            if (scales)
            {
                var c = new AnimationCurve();
                c.AddKey(new Keyframe(0f, f.sizeStart));
                c.AddKey(new Keyframe(Mathf.Clamp(f.sizeEndTime, 0.02f, 1f), f.sizeEnd));
                if (f.sizeEndTime < 0.99f) c.AddKey(new Keyframe(1f, f.sizeEnd));
                for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
                size.size = new ParticleSystem.MinMaxCurve(1f, c);
            }

            var rot = ps.rotationOverLifetime;
            bool spin = Mathf.Abs(f.spinMin) + Mathf.Abs(f.spinMax) > 0.01f;
            rot.enabled = spin;
            rot.z = new ParticleSystem.MinMaxCurve(f.spinMin * Mathf.Deg2Rad, f.spinMax * Mathf.Deg2Rad);

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.minParticleSize = 0f;
            r.maxParticleSize = 3f;
            if (f.rope)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.05f;
                r.lengthScale = 2f;
            }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = FxMaterial(string.IsNullOrEmpty(f.tex) ? "dot" : f.tex, f.additive);
        }

        void BuildPcfSection(AtlasItem it, Transform box)
        {
            var list = it.pcf ?? new List<Import.PcfFx>();
            var names = new List<string>();
            foreach (var f in list) names.Add(f.name);
            Hint(box, "Partículas de SFM (.pcf): " + string.Join(", ", names.ToArray()));
            AtlasTheme.TextButton(box, "key", "Empieza en el fotograma actual (" + Mathf.RoundToInt(frame) + ")", () => { it.fxStart = Mathf.RoundToInt(frame); ApplyLook(it); Commit(true); RefreshProps(); }, true, 44f, 15);
        }
    }
}
