// ATLAS 3D (Unity) - Source / SFM particle files (.pcf).
// A .pcf is a binary DMX file (encodings 2 to 5): a table of strings, a
// list of elements and their attributes. Each «particle system definition»
// has emitters, initializers, operators and renderers; here the common ones
// are read and turned into numbers for ATLAS's own particle effects
// (lifetime, size, colour, fades, speed, gravity, pull, swirl…).
// The sprites (.vmt/.vtf) are not in the file, so the look of each
// particle is guessed from its material's name (glow, smoke, beam…).
// Units: Source inches → metres, Z up → Y up.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Atlas.Import
{
    /// One particle system of a .pcf, ready for ATLAS (metres, Y up, seconds).
    [Serializable]
    public class PcfFx
    {
        public string name = "";
        public string material = "";
        public string tex = "dot";
        public bool additive = true;
        public bool rope;            // drawn as stretched streaks
        public int maxParticles = 1000;
        public float rate;           // per second (0 = none)
        public float rateStart, rateDuration;   // 0 duration = forever
        public int burst;            // particles at once
        public float burstTime;
        public float delay;          // child delay
        public float lifeMin = 1f, lifeMax = 1f;
        public float sizeMin = 0.25f, sizeMax = 0.25f;   // diameter in metres
        public float sizeStart = 1f, sizeEnd = 1f, sizeEndTime = 1f;
        public float[] color1 = { 1, 1, 1, 1 }, color2 = { 1, 1, 1, 1 };
        public float[] colorFade = { 1, 1, 1, 1 };
        public bool hasColorFade;
        public float colorFadeStart, colorFadeEnd = 1f;
        public float fadeIn, fadeOut;            // fractions of the lifetime
        public float spawnMin, spawnMax;         // sphere distance, metres
        public float speedMin, speedMax;         // outwards, m/s
        public float[] velMin = { 0, 0, 0 }, velMax = { 0, 0, 0 };   // m/s
        public float[] gravity = { 0, 0, 0 };    // m/s²
        public float drag;                       // 1/s
        public float pull;                       // m/s² towards the centre
        public float twist;                      // rad/s around Y
        public float noise;                      // random force, m/s
        public bool randomRotation;
        public float spinMin, spinMax;           // degrees per second
        public bool local;                       // locked to its control point
        public bool distortion;                  // a refraction effect: very faint
        public bool oneShot;                     // stops emitting by itself

        public PcfFx Copy()
        {
            var c = (PcfFx)MemberwiseClone();
            c.color1 = (float[])color1.Clone(); c.color2 = (float[])color2.Clone(); c.colorFade = (float[])colorFade.Clone();
            c.velMin = (float[])velMin.Clone(); c.velMax = (float[])velMax.Clone(); c.gravity = (float[])gravity.Clone();
            return c;
        }
    }

    public static class PcfImport
    {
        const float Inch = 0.0254f;

        public static List<PcfFx> CopyList(List<PcfFx> l)
        {
            var o = new List<PcfFx>();
            if (l != null) foreach (var f in l) if (f != null) o.Add(f.Copy());
            return o;
        }

        public static bool IsPcf(string path)
        {
            return path != null && path.EndsWith(".pcf", StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // DMX binary

        class El
        {
            public string type, name;
            public readonly Dictionary<string, object> a = new Dictionary<string, object>();
            public object Get(string k) { object v; return a.TryGetValue(k, out v) ? v : null; }
        }

        sealed class ElRef { public int i; public ElRef(int i) { this.i = i; } }

        class Reader
        {
            readonly byte[] b; public int p;
            public Reader(byte[] b, int p) { this.b = b; this.p = p; }
            public void Need(int n) { if (p + n > b.Length || n < 0) throw new Exception("El archivo .pcf está incompleto o dañado"); }
            public int Int() { Need(4); int v = BitConverter.ToInt32(b, p); p += 4; return v; }
            public short Short() { Need(2); short v = BitConverter.ToInt16(b, p); p += 2; return v; }
            public float Float() { Need(4); float v = BitConverter.ToSingle(b, p); p += 4; return v; }
            public byte Byte() { Need(1); return b[p++]; }
            public string CStr()
            {
                int e = Array.IndexOf(b, (byte)0, p);
                if (e < 0) throw new Exception("El archivo .pcf está incompleto o dañado");
                string s = Encoding.UTF8.GetString(b, p, e - p);
                p = e + 1;
                return s;
            }
            public void Skip(int n) { Need(n); p += n; }
        }

        static List<El> ParseDmx(byte[] b, out El root)
        {
            int nl = Array.IndexOf(b, (byte)'\n');
            if (nl < 0 || nl > 200) throw new Exception("No es un archivo de partículas .pcf");
            string header = Encoding.ASCII.GetString(b, 0, nl);
            if (header.IndexOf("binary", StringComparison.Ordinal) < 0)
                throw new Exception("Este .pcf está guardado como texto; guárdalo en binario desde el editor de partículas");
            int ver = 0;
            var m = System.Text.RegularExpressions.Regex.Match(header, @"binary\s+(\d+)");
            if (m.Success) ver = int.Parse(m.Groups[1].Value);
            if (ver < 2 || ver > 5) throw new Exception("Versión de .pcf no compatible (binary " + ver + ")");
            var r = new Reader(b, nl + 1);
            if (r.p < b.Length && b[r.p] == 0) r.p++;

            int ns = ver >= 4 ? r.Int() : r.Short();
            if (ns < 0 || ns > 1000000) throw new Exception("El archivo .pcf está dañado");
            var strs = new string[ns];
            for (int i = 0; i < ns; i++) strs[i] = r.CStr();
            Func<string> sym = () =>
            {
                int i = ver >= 5 ? r.Int() : r.Short();
                return i >= 0 && i < strs.Length ? strs[i] : "";
            };

            int ne = r.Int();
            if (ne < 0 || ne > 1000000) throw new Exception("El archivo .pcf está dañado");
            var els = new List<El>(ne);
            for (int i = 0; i < ne; i++)
            {
                var e = new El { type = sym() };
                e.name = ver >= 4 ? sym() : r.CStr();
                r.Skip(16);   // GUID
                els.Add(e);
            }
            foreach (var e in els)
            {
                int na = r.Int();
                for (int k = 0; k < na; k++)
                {
                    string an = sym();
                    int t = r.Byte();
                    e.a[an] = Value(r, t, ver, sym, false);
                }
            }
            root = els.Count > 0 ? els[0] : null;
            return els;
        }

        static object Value(Reader r, int t, int ver, Func<string> sym, bool inArray)
        {
            switch (t)
            {
                case 1:
                {
                    int i = r.Int();
                    if (i == -2) { if (ver >= 5) r.Int(); else r.CStr(); return null; }   // reference by id (another file)
                    return i >= 0 ? new ElRef(i) : null;
                }
                case 2: return r.Int();
                case 3: return r.Float();
                case 4: return r.Byte() != 0;
                case 5: return ver >= 4 && !inArray ? sym() : r.CStr();
                case 6: { int n = r.Int(); r.Skip(n); return null; }
                case 7: return r.Int() / 10000f;
                case 8: { r.Need(4); var c = new float[] { r.Byte() / 255f, r.Byte() / 255f, r.Byte() / 255f, r.Byte() / 255f }; return c; }
                case 9: return new[] { r.Float(), r.Float() };
                case 10: return new[] { r.Float(), r.Float(), r.Float() };
                case 11: case 13: return new[] { r.Float(), r.Float(), r.Float(), r.Float() };
                case 12: return new[] { r.Float(), r.Float(), r.Float() };
                case 14: r.Skip(64); return null;
            }
            if (t >= 15 && t <= 28)
            {
                int n = r.Int();
                if (n < 0 || n > 10000000) throw new Exception("El archivo .pcf está dañado");
                var list = new List<object>(Math.Min(n, 4096));
                for (int i = 0; i < n; i++) list.Add(Value(r, t - 14, ver, sym, true));
                return list;
            }
            throw new Exception("El archivo .pcf tiene un dato desconocido (" + t + ")");
        }

        // ------------------------------------------------------------------
        // Particle systems → ATLAS effects

        /// Reads every particle system of the file. `groups` gets the name of
        /// each top system (the one you would pick in SFM) and the indices of
        /// its effects in the returned list.
        public static List<PcfFx> Load(string path, out List<KeyValuePair<string, List<int>>> groups)
        {
            return Load(File.ReadAllBytes(path), out groups);
        }

        public static List<PcfFx> Load(byte[] data, out List<KeyValuePair<string, List<int>>> groups)
        {
            El root;
            var els = ParseDmx(data, out root);
            var defs = new List<int>();
            var list = root != null ? root.Get("particleSystemDefinitions") as List<object> : null;
            if (list != null) foreach (var o in list) { var rf = o as ElRef; if (rf != null && rf.i < els.Count) defs.Add(rf.i); }
            if (defs.Count == 0)
                for (int i = 0; i < els.Count; i++) if (els[i].type == "DmeParticleSystemDefinition") defs.Add(i);
            if (defs.Count == 0) throw new Exception("Este .pcf no tiene sistemas de partículas");

            // Systems that are someone's child are not shown on their own.
            var isChild = new HashSet<int>();
            foreach (int d in defs)
                foreach (var c in Children(els, els[d])) isChild.Add(c.Key);

            var outFx = new List<PcfFx>();
            var made = new Dictionary<string, int>();
            groups = new List<KeyValuePair<string, List<int>>>();
            foreach (int d in defs)
            {
                if (isChild.Contains(d)) continue;
                var idx = new List<int>();
                Walk(els, d, 0f, 0, outFx, idx, new HashSet<int>(), new HashSet<int>());
                if (idx.Count > 0) groups.Add(new KeyValuePair<string, List<int>>(els[d].name, idx));
            }
            if (outFx.Count == 0) throw new Exception("Este .pcf no emite partículas");
            return outFx;
        }

        static IEnumerable<KeyValuePair<int, float>> Children(List<El> els, El def)
        {
            var ch = def.Get("children") as List<object>;
            if (ch == null) yield break;
            foreach (var o in ch)
            {
                var rf = o as ElRef;
                if (rf == null || rf.i >= els.Count) continue;
                var c = els[rf.i];
                if (c.type == "DmeParticleSystemDefinition") { yield return new KeyValuePair<int, float>(rf.i, 0f); continue; }
                var cr = c.Get("child") as ElRef;
                if (cr != null && cr.i < els.Count) yield return new KeyValuePair<int, float>(cr.i, F(c.Get("delay"), 0f));
            }
        }

        static void Walk(List<El> els, int d, float delay, int depth, List<PcfFx> outFx, List<int> idx, HashSet<int> path, HashSet<int> done)
        {
            if (depth > 12 || path.Contains(d) || outFx.Count >= 48) return;
            path.Add(d);
            // The same system used several times (at other control points in
            // SFM) would sit on top of itself here: once is enough.
            var fx = done.Add(d) ? Convert(els, els[d]) : null;
            if (fx != null)
            {
                fx.delay = delay;
                idx.Add(outFx.Count);
                outFx.Add(fx);
            }
            foreach (var c in Children(els, els[d])) Walk(els, c.Key, delay + c.Value, depth + 1, outFx, idx, path, done);
            path.Remove(d);
        }

        static IEnumerable<El> Ops(List<El> els, El def, string list)
        {
            var l = def.Get(list) as List<object>;
            if (l == null) yield break;
            foreach (var o in l) { var rf = o as ElRef; if (rf != null && rf.i < els.Count) yield return els[rf.i]; }
        }

        static float F(object o, float def)
        {
            if (o is float) return (float)o;
            if (o is int) return (int)o;
            return def;
        }

        static float[] V3(object o, float x, float y, float z)
        {
            var v = o as float[];
            return v != null && v.Length >= 3 ? v : new[] { x, y, z };
        }

        static float[] Col(object o, float[] def)
        {
            var v = o as float[];
            return v != null && v.Length >= 4 ? (float[])v.Clone() : (float[])def.Clone();
        }

        /// Source (X fwd, Y left, Z up, inches) → Unity (metres, Y up).
        static float[] ToUnity(float[] v) { return new[] { v[0] * Inch, v[2] * Inch, v[1] * Inch }; }

        static PcfFx Convert(List<El> els, El def)
        {
            bool emits = false;
            foreach (var _ in Ops(els, def, "emitters")) { emits = true; break; }
            if (!emits) return null;
            // Without a renderer it only guides other systems: nothing to see.
            bool draws = false;
            foreach (var _ in Ops(els, def, "renderers")) { draws = true; break; }
            if (!draws) return null;

            var fx = new PcfFx { name = def.name };
            fx.material = def.Get("material") as string ?? "";
            fx.maxParticles = Math.Max(1, Math.Min(5000, (int)F(def.Get("max_particles"), 1000)));
            float baseRadius = F(def.Get("radius"), 5f);
            fx.sizeMin = fx.sizeMax = baseRadius * 2f * Inch;
            var baseCol = Col(def.Get("color"), new float[] { 1, 1, 1, 1 });
            fx.color1 = (float[])baseCol.Clone();
            fx.color2 = (float[])baseCol.Clone();
            float alphaMin = 1f, alphaMax = 1f;
            bool hasLife = false;

            foreach (var e in Ops(els, def, "emitters"))
            {
                switch (e.Get("functionName") as string ?? "")
                {
                    case "emit_continuously":
                        fx.rate += F(e.Get("emission_rate"), 100f);
                        fx.rateStart = F(e.Get("emission_start_time"), 0f);
                        fx.rateDuration = F(e.Get("emission_duration"), 0f);
                        break;
                    case "emit noise":
                        fx.rate += (F(e.Get("emission minimum"), 0f) + F(e.Get("emission maximum"), 100f)) * 0.5f;
                        break;
                    case "emit_instantaneously":
                        fx.burst += (int)F(e.Get("num_to_emit"), 100f);
                        fx.burstTime = F(e.Get("emission_start_time"), 0f);
                        break;
                    default:
                        fx.rate += 20f;
                        break;
                }
            }
            fx.oneShot = fx.rate <= 0f || fx.rateDuration > 0f;

            foreach (var e in Ops(els, def, "initializers"))
            {
                switch (e.Get("functionName") as string ?? "")
                {
                    case "Lifetime Random":
                    {
                        float a = F(e.Get("lifetime_min"), 0f), b = F(e.Get("lifetime_max"), 0f);
                        fx.lifeMin = Math.Min(a, b); fx.lifeMax = Math.Max(a, b);
                        hasLife = true;
                        break;
                    }
                    case "Radius Random":
                    {
                        float a = F(e.Get("radius_min"), 1f), b = F(e.Get("radius_max"), 1f);
                        fx.sizeMin = Math.Min(a, b) * 2f * Inch; fx.sizeMax = Math.Max(a, b) * 2f * Inch;
                        break;
                    }
                    case "Color Random":
                        fx.color1 = Col(e.Get("color1"), new float[] { 1, 1, 1, 1 });
                        fx.color2 = Col(e.Get("color2"), new float[] { 1, 1, 1, 1 });
                        break;
                    case "Alpha Random":
                    {
                        float a = F(e.Get("alpha_min"), 255f) / 255f, b = F(e.Get("alpha_max"), 255f) / 255f;
                        alphaMin = Math.Min(a, b); alphaMax = Math.Max(a, b);
                        break;
                    }
                    case "Position Within Sphere Random":
                    {
                        float a = F(e.Get("distance_min"), 0f), b = F(e.Get("distance_max"), 0f);
                        fx.spawnMin = Math.Min(a, b) * Inch; fx.spawnMax = Math.Max(a, b) * Inch;
                        float s0 = F(e.Get("speed_min"), 0f), s1 = F(e.Get("speed_max"), 0f);
                        fx.speedMin = Math.Min(s0, s1) * Inch; fx.speedMax = Math.Max(s0, s1) * Inch;
                        AddVel(fx, e.Get("speed_in_local_coordinate_system_min"), e.Get("speed_in_local_coordinate_system_max"));
                        break;
                    }
                    case "Velocity Random":
                    {
                        AddVel(fx, e.Get("speed_in_local_coordinate_system_min"), e.Get("speed_in_local_coordinate_system_max"));
                        float s0 = F(e.Get("random_speed_min"), 0f), s1 = F(e.Get("random_speed_max"), 0f);
                        fx.speedMin += Math.Min(s0, s1) * Inch; fx.speedMax += Math.Max(s0, s1) * Inch;
                        break;
                    }
                    case "Position Modify Offset Random":
                    {
                        var a = V3(e.Get("offset min"), 0, 0, 0); var b = V3(e.Get("offset max"), 0, 0, 0);
                        float spread = 0f;
                        for (int k = 0; k < 3; k++) spread = Math.Max(spread, Math.Abs(b[k] - a[k]) * 0.5f);
                        fx.spawnMax = Math.Max(fx.spawnMax, spread * Inch);
                        break;
                    }
                    case "Rotation Random":
                        fx.randomRotation = true;
                        break;
                    case "Rotation Speed Random":
                    {
                        float a = F(e.Get("rotation_speed_random_min"), 0f), b = F(e.Get("rotation_speed_random_max"), 360f);
                        fx.spinMin = Math.Min(a, b); fx.spinMax = Math.Max(a, b);
                        break;
                    }
                }
            }
            fx.color1[3] *= alphaMin;
            fx.color2[3] *= alphaMax;
            if (!hasLife) { fx.lifeMin = 0.8f; fx.lifeMax = 1.2f; }
            if (fx.lifeMax < 0.05f) fx.lifeMax = 0.05f;
            if (fx.lifeMin < 0.05f) fx.lifeMin = Math.Min(0.05f, fx.lifeMax);
            fx.lifeMax = Math.Min(fx.lifeMax, 30f);
            fx.lifeMin = Math.Min(fx.lifeMin, fx.lifeMax);

            foreach (var e in Ops(els, def, "operators"))
            {
                switch (e.Get("functionName") as string ?? "")
                {
                    case "Movement Basic":
                        fx.gravity = ToUnity(V3(e.Get("gravity"), 0, 0, 0));
                        // Source's drag takes this share of the speed away each 1/30 s.
                        float dr = Math.Max(0f, Math.Min(0.99f, F(e.Get("drag"), 0f)));
                        fx.drag = dr > 0f ? -(float)Math.Log(1f - dr) * 30f : 0f;
                        break;
                    case "Movement Lock to Control Point":
                        fx.local = true;
                        break;
                    case "Radius Scale":
                        fx.sizeStart = F(e.Get("radius_start_scale"), 1f);
                        fx.sizeEnd = F(e.Get("radius_end_scale"), 1f);
                        fx.sizeEndTime = Clamp01(F(e.Get("end_time"), 1f));
                        if (fx.sizeEndTime < 0.02f) fx.sizeEndTime = 1f;
                        break;
                    case "Color Fade":
                        fx.colorFade = Col(e.Get("color_fade"), new float[] { 1, 1, 1, 1 });
                        fx.colorFadeStart = Clamp01(F(e.Get("fade_start_time"), 0f));
                        fx.colorFadeEnd = Clamp01(F(e.Get("fade_end_time"), 1f));
                        if (fx.colorFadeEnd <= fx.colorFadeStart) fx.colorFadeEnd = Math.Min(1f, fx.colorFadeStart + 0.01f);
                        fx.hasColorFade = true;
                        break;
                    case "Alpha Fade In Random":
                        fx.fadeIn = Clamp01((F(e.Get("fade in time min"), 0.25f) + F(e.Get("fade in time max"), 0.25f)) * 0.5f);
                        break;
                    case "Alpha Fade Out Random":
                        fx.fadeOut = Clamp01((F(e.Get("fade out time min"), 0.25f) + F(e.Get("fade out time max"), 0.25f)) * 0.5f);
                        break;
                    case "Alpha Fade and Decay":
                    {
                        float fi = F(e.Get("end_fade_in_time"), 0.5f) - F(e.Get("start_fade_in_time"), 0f);
                        float fo = F(e.Get("end_fade_out_time"), 1f) - F(e.Get("start_fade_out_time"), 0.5f);
                        fx.fadeIn = Clamp01(fi); fx.fadeOut = Clamp01(fo);
                        break;
                    }
                    case "Pull towards control point":
                        fx.pull += F(e.Get("amount of force"), 100f) * Inch;
                        break;
                    case "twist around axis":
                        fx.twist += F(e.Get("amount of force"), 100f) / 100f;
                        break;
                    case "random force":
                    {
                        var a = V3(e.Get("min force"), 0, 0, 0); var b = V3(e.Get("max force"), 0, 0, 0);
                        float mag = 0f;
                        for (int k = 0; k < 3; k++) mag = Math.Max(mag, Math.Abs(b[k] - a[k]) * 0.5f);
                        fx.noise = Math.Max(fx.noise, mag * Inch * 0.15f);
                        // A force that only pushes one way acts like extra gravity.
                        for (int k = 0; k < 3; k++)
                            if (Math.Sign(a[k]) == Math.Sign(b[k]) && a[k] != 0f)
                            {
                                var g = new float[] { 0, 0, 0 };
                                g[k] = (a[k] + b[k]) * 0.5f;
                                var gu = ToUnity(g);
                                for (int q = 0; q < 3; q++) fx.gravity[q] += gu[q];
                            }
                        break;
                    }
                    case "Rotation Spin Roll":
                    {
                        float s = F(e.Get("spin_rate_degrees"), 0f);
                        fx.spinMin += s; fx.spinMax += s;
                        break;
                    }
                }
            }
            if (fx.fadeIn + fx.fadeOut > 1f) { float k = 1f / (fx.fadeIn + fx.fadeOut); fx.fadeIn *= k; fx.fadeOut *= k; }

            foreach (var e in Ops(els, def, "renderers"))
            {
                string fn = e.Get("functionName") as string ?? "";
                if (fn == "render_rope" || fn == "render_sprite_trail") fx.rope = true;
            }
            GuessLook(fx);
            return fx;
        }

        static void AddVel(PcfFx fx, object min, object max)
        {
            var a = ToUnity(V3(min, 0, 0, 0)); var b = ToUnity(V3(max, 0, 0, 0));
            for (int k = 0; k < 3; k++)
            {
                fx.velMin[k] += Math.Min(a[k], b[k]);
                fx.velMax[k] += Math.Max(a[k], b[k]);
            }
        }

        static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }

        static bool Has(string s, params string[] words)
        {
            foreach (var w in words) if (s.Contains(w)) return true;
            return false;
        }

        /// No sprite comes with the file: pick ATLAS's closest one by the
        /// material's name.
        static void GuessLook(PcfFx fx)
        {
            string m = (fx.material + " " + fx.name).ToLowerInvariant().Replace('\\', '/');
            bool smoke = Has(m, "smoke", "dust", "sand", "cloud", "fog", "steam", "leftover", "puff", "dirt", "debris", "mist", "vapor");
            fx.distortion = Has(m, "distort", "refract", "warp", "heatwave", "heat_haze", "anomaly");
            if (fx.rope || Has(m, "beam", "laser", "trail", "streak", "tracer", "rope")) { fx.tex = "streak"; fx.rope = true; }
            else if (smoke) fx.tex = "puff";
            else if (Has(m, "circle", "ring", "swirl", "shockwave", "wave", "portal", "vortex")) fx.tex = "ring";
            else if (Has(m, "electric", "lightning", "spark", "blazing", "zap", "flare", "star", "shine")) fx.tex = "star";
            else if (Has(m, "bubble")) fx.tex = "bubble";
            else fx.tex = "dot";
            fx.additive = !smoke || Has(m, "_add", "additive", "glow", "fire", "flame", "ember");
            if (fx.distortion)
            {
                fx.tex = "puff";
                fx.additive = true;
                fx.color1[3] *= 0.12f;
                fx.color2[3] *= 0.12f;
            }
        }
    }
}
