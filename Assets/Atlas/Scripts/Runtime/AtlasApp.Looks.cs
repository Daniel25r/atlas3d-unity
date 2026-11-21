// ATLAS 5D (Unity) - «Estilo de la imagen»: the whole picture drawn in a
// style (Spider-Verse comic, anime, poster, painting, sketch, ink…) and
// effects on top (pixelated, wide lens, dirty lens, dust, vintage film),
// colour grading and tone mapping. All of it shows in the view and in
// renders (it is the AtlasStylize full-screen pass and URP's volume).
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        // Style numbers of the AtlasStylize shader (_Mode).
        static readonly string[] StyleNames = { "Del ambiente", "Ninguno", "Spider-Verse", "Anime", "Cómic", "Contorno de tinta", "Póster", "Pintura", "Boceto", "Retro", "VHS" };
        static readonly int[] StyleModes = { -1, 0, 4, 5, 2, 9, 6, 7, 8, 1, 3 };
        static readonly string[] GradeIds = { "", "warm", "cool", "teal", "bw", "dramatic", "dream" };
        static readonly string[] GradeNames = { "Natural", "Cálido", "Frío", "Cine", "Blanco y negro", "Dramático", "Sueño" };

        void BuildStyleSection()
        {
            bool any = env.style >= 0 || env.pixel > 0f || env.wideLens > 0f || env.dirtyLens > 0f || env.dust > 0f || env.vintage > 0f || !string.IsNullOrEmpty(env.grade) || env.tonemap >= 0;
            var box = Sec("Estilo de la imagen", "edit", any);
            Hint(box, "Toda la imagen con un estilo, en la vista y en el render. Se puede combinar con los efectos de abajo.");
            AtlasTheme.TextButton(box, "sun", "Aspecto realista (un toque)", MakeRealistic, true, 44f, 15);
            var g = AtlasTheme.Grid(box, 3, InnerWidth, 36f, 4f);
            for (int i = 0; i < StyleNames.Length; i++)
            {
                int mode = StyleModes[i];
                AtlasTheme.Chip(g, StyleNames[i], () =>
                {
                    env.style = mode;
                    if (mode > 0 && env.styleAmount <= 0.01f) env.styleAmount = 1f;
                    ApplyEnv();
                    Commit(true);
                    RefreshProps();
                }, env.style == mode);
            }
            if (env.style > 0)
                AtlasTheme.SliderRow(box, "Intensidad del estilo", 0f, 1f, env.styleAmount, "0.00", x => { env.styleAmount = x; ApplyEnv(); }, () => Commit(true));

            var t = AtlasTheme.Label(box, "Efectos encima", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t.gameObject, 26f);
            AtlasTheme.SliderRow(box, "Pixelado", 0f, 24f, env.pixel, "0", x => { env.pixel = Mathf.Round(x); ApplyEnv(); }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Gran angular", 0f, 1f, env.wideLens, "0.00", x => { env.wideLens = x; ApplyEnv(); }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Lente sucia", 0f, 1f, env.dirtyLens, "0.00", x => { env.dirtyLens = x; ApplyEnv(); }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Polvo en el aire", 0f, 1f, env.dust, "0.00", x => { env.dust = x; ApplyEnv(); }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Película antigua", 0f, 1f, env.vintage, "0.00", x => { env.vintage = x; ApplyEnv(); }, () => Commit(true));

            var t2 = AtlasTheme.Label(box, "Etalonaje (color de cine)", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t2.gameObject, 26f);
            var gg = AtlasTheme.Grid(box, 4, InnerWidth, 34f, 4f);
            for (int i = 0; i < GradeIds.Length; i++)
            {
                string id = GradeIds[i];
                AtlasTheme.Chip(gg, GradeNames[i], () => { env.grade = id; ApplyEnv(); Commit(true); RefreshProps(); }, (env.grade ?? "") == id);
            }
            var t3 = AtlasTheme.Label(box, "Mapeo de tonos", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t3.gameObject, 26f);
            var tg = AtlasTheme.Grid(box, 4, InnerWidth, 34f, 4f);
            string[] tn = { "Del ambiente", "Ninguno", "Neutro", "Cine (ACES)" };
            for (int i = 0; i < 4; i++)
            {
                int v = i - 1;
                AtlasTheme.Chip(tg, tn[i], () => { env.tonemap = v; ApplyEnv(); Commit(true); RefreshProps(); }, env.tonemap == v);
            }
            BuildFxKeyRow(box);
            AtlasTheme.TextButton(box, "undo", "Quitar estilo y efectos", () =>
            {
                env.style = -1; env.styleAmount = 1f;
                env.pixel = env.wideLens = env.dirtyLens = env.dust = env.vintage = 0f;
                env.grade = ""; env.tonemap = -1;
                ApplyEnv();
                Commit(true);
                RefreshProps();
            }, false, 40f, 15);
        }
    
        /// One tap towards a realistic picture: film colour (ACES), soft
        /// shadows in corners, shadows on, a touch of bloom and vignette, no
        /// drawing styles, and the models' own shine (their materials as
        /// they were made). The «Cine» render then adds lens blur and smooth
        /// edges.
        void MakeRealistic()
        {
            env.style = 0;
            env.styleAmount = 1f;
            env.pixel = env.wideLens = env.dust = env.vintage = 0f;
            env.dirtyLens = 0.08f;
            env.grade = "";
            env.tonemap = 2;
            env.ao = true;
            env.shadowsOff = false;
            env.exposure = 0.15f;
            env.contrast = 10f;
            env.saturation = 4f;
            env.bloom = 0.2f;
            env.vignette = 0.18f;
            env.grain = 0.05f;
            env.chroma = 0f;
            int models = 0;
            foreach (var it in items)
            {
                if (it == null) continue;
                if (it.toon) { it.toon = false; ApplyLook(it); }
                if (it.kind == "model") { it.modelGloss = -1f; ApplyLook(it); models++; }
            }
            if (!Prefs.viewShadows) { Prefs.viewShadows = true; Prefs.Save(); ApplyPrefs(); }
            ApplyEnv();
            Commit(true);
            RefreshProps();
            Toast("Aspecto realista puesto. Para la imagen final usa Render → calidad «Cine» (desenfoque de lente y bordes suaves)");
        }

        // ------------------------------------------------------------------
        // Keys of the image effects: exposure, colour, bloom, blur, style
        // strength, pixelated… change along the animation.

        void BuildFxKeyRow(Transform box)
        {
            int f = Mathf.RoundToInt(frame);
            var keys = env.fxKeys;
            bool here = keys != null && keys.Exists(k => k.frame == f);
            var t = AtlasTheme.Label(box, "Animar los efectos" + (keys != null && keys.Count > 0 ? " (" + keys.Count + " claves)" : ""), 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(t.gameObject, 26f);
            Hint(box, "Pon los valores, pulsa «Clave» en un fotograma, cámbialos y pon otra clave en otro: los efectos pasan de unos a otros al reproducir y en el render.");
            var g = AtlasTheme.Grid(box, 4, InnerWidth, 38f, 4f);
            AtlasTheme.TextButton(g, "key", here ? "Actualizar" : "Clave", () =>
            {
                if (env.fxKeys == null) env.fxKeys = new List<EnvKey>();
                env.fxKeys.RemoveAll(k => k.frame == f);
                env.fxKeys.Add(EnvKey.From(env, f));
                env.fxKeys.Sort((a, b) => a.frame.CompareTo(b.frame));
                Commit(true);
                RefreshProps();
                Toast("Clave de efectos en el fotograma " + f);
            }, true, 38f, 14);
            AtlasTheme.TextButton(g, "delete", "Quitar", () =>
            {
                if (env.fxKeys == null || env.fxKeys.RemoveAll(k => k.frame == f) == 0) { Toast("No hay clave de efectos en este fotograma"); return; }
                Commit(true);
                RefreshProps();
            }, false, 38f, 14);
            AtlasTheme.TextButton(g, "key_prev", "◀", () => JumpFxKey(-1), false, 38f, 14);
            AtlasTheme.TextButton(g, "key_next", "▶", () => JumpFxKey(1), false, 38f, 14);
            if (keys != null && keys.Count > 0)
                AtlasTheme.TextButton(box, "delete", "Quitar todas las claves de efectos", () => { env.fxKeys.Clear(); Commit(true); RefreshProps(); }, false, 38f, 14);
        }

        void JumpFxKey(int dir)
        {
            if (env.fxKeys == null || env.fxKeys.Count == 0) { Toast("Todavía no hay claves de efectos"); return; }
            int f = Mathf.RoundToInt(frame), best = -1;
            foreach (var k in env.fxKeys)
                if (dir > 0 ? k.frame > f && (best < 0 || k.frame < best) : k.frame < f && (best < 0 || k.frame > best)) best = k.frame;
            if (best < 0) { Toast(dir > 0 ? "No hay más claves después" : "No hay claves antes"); return; }
            SetFrame(best);
            RefreshProps();
        }

        /// Called by SetFrame: the effects as their keys say at this frame.
        void EvaluateFxKeys()
        {
            var keys = env.fxKeys;
            if (keys == null || keys.Count == 0) return;
            EnvKey a = keys[0], b = keys[keys.Count - 1];
            float t = 0f;
            if (frame <= a.frame) b = a;
            else if (frame >= b.frame) a = b;
            else
                for (int i = 0; i + 1 < keys.Count; i++)
                    if (frame >= keys[i].frame && frame <= keys[i + 1].frame)
                    {
                        a = keys[i]; b = keys[i + 1];
                        t = Interp.Shape(a.interp, Mathf.InverseLerp(a.frame, b.frame, frame));
                        break;
                    }
            EnvKey.Blend(a, b, t, env);
            Ambience.Apply(env, cam, sunLight);
        }
    }
}
