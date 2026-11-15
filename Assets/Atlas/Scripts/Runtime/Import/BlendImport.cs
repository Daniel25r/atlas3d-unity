// ATLAS 3D (Unity) - Blender files (.blend) without Blender, ported from
// the Godot version: the file carries a description of its own structures
// («DNA»), which is read to find the objects, their meshes (vertices,
// faces, UVs, materials), the material colours and their image textures
// (packed in the file or next to it), and where each object is.
// Meshes of Blender 2.8 - 4.x, with their Mirror and Subdivision Surface
// modifiers applied; only the objects the scene shows (hidden objects and
// excluded collections are left out). Curves, text and armatures are not
// evaluated.
// Blender (z up) -> glTF (y up): (x, y, z) -> (x, z, -y).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Atlas.Import
{
    public class BlendImport
    {
        const int CD_MVERT = 0, CD_MLOOPUV = 16, CD_MPOLY = 25, CD_MLOOP = 26, CD_PROP_INT32 = 11, CD_PROP_BOOL = 50, CD_PROP_FLOAT2 = 49, CD_PROP_FLOAT3 = 48;

        class Field { public int off; public string type; public bool ptr; public int size; }
        class StructDef { public int size; public Dictionary<string, Field> fields = new Dictionary<string, Field>(); }
        class Block { public string code; public int len; public ulong old; public int sdna, nr, off; }
        struct View { public string type; public int off; public bool ok; }

        byte[] b;
        int ptr = 8;
        readonly List<string> names = new List<string>();
        readonly List<string> types = new List<string>();
        readonly List<int> tlen = new List<int>();
        readonly Dictionary<string, StructDef> structs = new Dictionary<string, StructDef>();
        readonly List<string> sdnaStruct = new List<string>();
        readonly List<Block> blocks = new List<Block>();
        readonly Dictionary<ulong, int> byPtr = new Dictionary<ulong, int>();
        // Blender 3.x writes some mesh data from temporary arrays, so one old
        // address can name blocks of different meshes: a pointer belongs to
        // the first such block after the data-block that is being read (ctx).
        readonly Dictionary<ulong, List<int>> byPtrAll = new Dictionary<ulong, List<int>>();
        int ctx = -1;
        string dir = "";
        MModel model;
        readonly Dictionary<int, int> matCache = new Dictionary<int, int>();

        public static bool IsBlend(byte[] h)
        {
            return h != null && h.Length >= 7 && Encoding.ASCII.GetString(h, 0, 7) == "BLENDER";
        }

        public static MModel Load(string path)
        {
            return new BlendImport().Build(path);
        }

        MModel Build(string path)
        {
            dir = Path.GetDirectoryName(path);
            b = File.ReadAllBytes(path);
            if (b.Length >= 4 && b[0] == 0x28 && b[1] == 0xB5 && b[2] == 0x2F && b[3] == 0xFD)
                throw new Exception("este .blend está guardado comprimido; en Blender desactiva «Comprimir» al guardar (Archivo → Guardar como → ⚙) o exporta a glTF / FBX");
            if (b.Length >= 2 && b[0] == 0x1F && b[1] == 0x8B)
            {
                using (var gz = new GZipStream(new MemoryStream(b), CompressionMode.Decompress))
                using (var ms = new MemoryStream())
                {
                    gz.CopyTo(ms);
                    b = ms.ToArray();
                }
            }
            if (!IsBlend(b)) throw new Exception("no es un archivo de Blender (.blend) o está dañado");
            if (b[7] != 0x2D && b[7] != 0x5F)
                throw new Exception("este .blend es de una versión de Blender muy nueva que ATLAS todavía no lee; en Blender: Archivo → Exportar → glTF (.glb)");
            ptr = b[7] == 0x2D ? 8 : 4;
            if (b[8] != 0x76) throw new Exception("este .blend se guardó en un ordenador «big endian» muy antiguo");
            ReadBlocks();
            ReadSdna();
            model = new MModel();
            model.nodes.Add(new MNode { name = Path.GetFileNameWithoutExtension(path) });
            // The objects the scene shows; every object block if that fails.
            var shown = SceneObjects();
            for (int bi = 0; bi < blocks.Count; bi++)
            {
                if (blocks[bi].code != "OB") continue;
                if (shown != null && !shown.Contains(bi)) continue;
                var ob = StructAt(bi, 0);
                if (!ob.ok || Int(ob, "type", 0) != 1) continue;   // OB_MESH
                ctx = bi;
                int meBi = BlockOf(Ptr(ob, "data"));
                if (meBi < 0) continue;
                // restrictflag: OB_HIDE_VIEWPORT (1), OB_HIDE_RENDER (4);
                // visibility_flag: OB_HIDE_CAMERA (1), invisible to the camera.
                long restrict = Int(ob, "restrictflag", 0);
                if ((restrict & 1) != 0 || (restrict & 4) != 0 || (Int(ob, "visibility_flag", 0) & 1) != 0) continue;
                var mesh = Mesh(meBi, ObjectMatrix(ob), ob);
                if (mesh == null) continue;
                mesh.name = IdName(ob);
                model.meshes.Add(mesh);
                model.nodes.Add(new MNode { name = mesh.name, parent = 0, mesh = model.meshes.Count - 1 });
            }
            if (model.meshes.Count == 0)
                throw new Exception("el .blend no tiene mallas que ATLAS pueda leer (curvas, textos o metabolas no se leen); en Blender: Archivo → Exportar → glTF (.glb)");
            return model;
        }

        // ------------------------------------------------------------------
        // File blocks and DNA

        int S32(int o) { return BitConverter.ToInt32(b, o); }
        int U16(int o) { return BitConverter.ToUInt16(b, o); }
        float F32(int o) { return BitConverter.ToSingle(b, o); }
        ulong PtrAt(int o) { return ptr == 8 ? BitConverter.ToUInt64(b, o) : BitConverter.ToUInt32(b, o); }

        string CStrAt(int o, int max, out int end)
        {
            int e = o;
            while (e < b.Length && e < o + max && b[e] != 0) e++;
            end = e;
            return Encoding.UTF8.GetString(b, o, e - o);
        }

        void ReadBlocks()
        {
            int p = 12;
            int hsize = 16 + ptr;
            while (p + hsize <= b.Length)
            {
                string code = Encoding.ASCII.GetString(b, p, 4).TrimEnd('\0');
                int length = S32(p + 4);
                ulong old = PtrAt(p + 8);
                int sdna = S32(p + 8 + ptr);
                int nr = S32(p + 12 + ptr);
                if (code == "ENDB") break;
                if (length < 0 || p + hsize + length > b.Length) throw new Exception("el .blend está cortado");
                blocks.Add(new Block { code = code, len = length, old = old, sdna = sdna, nr = nr, off = p + hsize });
                byPtr[old] = blocks.Count - 1;
                List<int> all;
                if (!byPtrAll.TryGetValue(old, out all)) { all = new List<int>(); byPtrAll[old] = all; }
                all.Add(blocks.Count - 1);
                p += hsize + length;
            }
        }

        int sp;

        bool Expect(string tag)
        {
            sp = (sp + 3) & ~3;
            if (sp + 4 > b.Length || Encoding.ASCII.GetString(b, sp, 4) != tag) return false;
            sp += 4;
            return true;
        }

        void ReadSdna()
        {
            int dna = -1;
            foreach (var bl in blocks) if (bl.code == "DNA1") { dna = bl.off; break; }
            if (dna < 0) throw new Exception("el .blend no describe su contenido (sin DNA1)");
            sp = dna + 4;
            if (Encoding.ASCII.GetString(b, dna, 4) != "SDNA" || !Expect("NAME")) throw new Exception("descripción del .blend no válida");
            int n = S32(sp);
            sp += 4;
            for (int i = 0; i < n; i++) { int e; names.Add(CStrAt(sp, 4096, out e)); sp = e + 1; }
            if (!Expect("TYPE")) throw new Exception("descripción del .blend no válida (TYPE)");
            n = S32(sp);
            sp += 4;
            for (int i = 0; i < n; i++) { int e; types.Add(CStrAt(sp, 4096, out e)); sp = e + 1; }
            if (!Expect("TLEN")) throw new Exception("descripción del .blend no válida (TLEN)");
            for (int i = 0; i < n; i++) { tlen.Add(U16(sp)); sp += 2; }
            if (!Expect("STRC")) throw new Exception("descripción del .blend no válida (STRC)");
            int ns = S32(sp);
            sp += 4;
            for (int s = 0; s < ns; s++)
            {
                int t = U16(sp), nf = U16(sp + 2);
                sp += 4;
                var def = new StructDef { size = tlen[t] };
                int off = 0;
                for (int f = 0; f < nf; f++)
                {
                    int ft = U16(sp);
                    string fname = names[U16(sp + 2)];
                    sp += 4;
                    bool isPtr = fname.StartsWith("*") || fname.StartsWith("(*");
                    string bas = fname;
                    int count = 1;
                    int br = fname.IndexOf('[');
                    if (br >= 0)
                    {
                        bas = fname.Substring(0, br);
                        foreach (var part in fname.Substring(br).Split(new[] { '[' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            int d;
                            if (int.TryParse(part.TrimEnd(']'), out d)) count *= d;
                        }
                    }
                    bas = bas.Replace("*", "").Replace("(", "").Replace(")", "");
                    int size = (isPtr ? ptr : tlen[ft]) * count;
                    def.fields[bas] = new Field { off = off, type = types[ft], ptr = isPtr, size = size };
                    off += size;
                }
                structs[types[t]] = def;
                sdnaStruct.Add(types[t]);
            }
        }

        int BlockOf(ulong p)
        {
            if (p == 0) return -1;
            List<int> all;
            if (!byPtrAll.TryGetValue(p, out all)) return -1;
            if (all.Count == 1 || ctx < 0) return all[all.Count - 1];
            foreach (int i in all) if (i > ctx) return i;
            return all[all.Count - 1];
        }

        View StructAt(int bi, int idx)
        {
            var bl = blocks[bi];
            if (bl.sdna < 0 || bl.sdna >= sdnaStruct.Count) return new View();
            string t = sdnaStruct[bl.sdna];
            return new View { type = t, off = bl.off + idx * structs[t].size, ok = true };
        }

        Field FieldOf(View st, string name)
        {
            StructDef s;
            Field f;
            if (!st.ok || !structs.TryGetValue(st.type, out s) || !s.fields.TryGetValue(name, out f)) return null;
            return f;
        }

        ulong Ptr(View st, string name)
        {
            var f = FieldOf(st, name);
            return f != null && f.ptr ? PtrAt(st.off + f.off) : 0;
        }

        long Int(View st, string name, long def)
        {
            var f = FieldOf(st, name);
            if (f == null) return def;
            int o = st.off + f.off;
            if (f.ptr) return (long)PtrAt(o);
            switch (f.type)
            {
                case "char": case "uchar": case "int8_t": case "uint8_t": return b[o];
                case "short": return BitConverter.ToInt16(b, o);
                case "ushort": case "uint16_t": return BitConverter.ToUInt16(b, o);
                case "int": case "int32_t": return S32(o);
                case "uint": case "uint32_t": return BitConverter.ToUInt32(b, o);
                case "int64_t": case "uint64_t": return BitConverter.ToInt64(b, o);
                case "float": return (long)F32(o);
            }
            return def;
        }

        float Float(View st, string name, float def)
        {
            var f = FieldOf(st, name);
            return f != null && f.type == "float" ? F32(st.off + f.off) : def;
        }

        float[] Floats(View st, string name)
        {
            var f = FieldOf(st, name);
            if (f == null || f.type != "float") return new float[0];
            var o = new float[f.size / 4];
            for (int i = 0; i < o.Length; i++) o[i] = F32(st.off + f.off + i * 4);
            return o;
        }

        string Str(View st, string name)
        {
            var f = FieldOf(st, name);
            if (f == null) return "";
            int e;
            return CStrAt(st.off + f.off, f.size, out e);
        }

        View Sub(View st, string name)
        {
            var f = FieldOf(st, name);
            return f == null ? new View() : new View { type = f.type, off = st.off + f.off, ok = true };
        }

        string First(View st, params string[] candidates)
        {
            foreach (var c in candidates) if (FieldOf(st, c) != null) return c;
            return "";
        }

        string IdName(View st)
        {
            string n = Str(Sub(st, "id"), "name");
            return n.Length > 2 ? n.Substring(2) : n;
        }

        // ------------------------------------------------------------------
        // Objects (4x4 matrices, column-major as Blender stores them)

        static float[] Mul(float[] a, float[] c)
        {
            var r = new float[16];
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                {
                    float s = 0f;
                    for (int k = 0; k < 4; k++) s += a[k * 4 + row] * c[col * 4 + k];
                    r[col * 4 + row] = s;
                }
            return r;
        }

        static float[] Identity() { return new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 }; }

        float[] Mat4(View st, string name)
        {
            var f = Floats(st, name);
            return f.Length >= 16 ? f : Identity();
        }

        float[] LocalMatrix(View ob)
        {
            var loc = Floats(ob, "loc");
            var rot = Floats(ob, "rot");
            var size = Floats(ob, "scale");
            if (size.Length < 3) size = Floats(ob, "size");
            float rx = rot.Length >= 3 ? rot[0] : 0f, ry = rot.Length >= 3 ? rot[1] : 0f, rz = rot.Length >= 3 ? rot[2] : 0f;
            // Euler XYZ: R = Rz * Ry * Rx
            float cx = (float)Math.Cos(rx), sx = (float)Math.Sin(rx), cy = (float)Math.Cos(ry), sy = (float)Math.Sin(ry), cz = (float)Math.Cos(rz), sz = (float)Math.Sin(rz);
            float[] r =
            {
                cy * cz, cy * sz, -sy, 0,
                sx * sy * cz - cx * sz, sx * sy * sz + cx * cz, sx * cy, 0,
                cx * sy * cz + sx * sz, cx * sy * sz - sx * cz, cx * cy, 0,
                0, 0, 0, 1,
            };
            float s0 = size.Length >= 3 ? size[0] : 1f, s1 = size.Length >= 3 ? size[1] : 1f, s2 = size.Length >= 3 ? size[2] : 1f;
            for (int i = 0; i < 3; i++) { r[i] *= s0; r[4 + i] *= s1; r[8 + i] *= s2; }
            if (loc.Length >= 3) { r[12] = loc[0]; r[13] = loc[1]; r[14] = loc[2]; }
            return r;
        }

        float[] ObjectMatrix(View ob)
        {
            string key = First(ob, "object_to_world", "obmat");
            if (key != "")
            {
                var f = Floats(ob, key);
                if (f.Length >= 16) return f;
            }
            // Newer files keep only the local values: parent × parent-inverse × local.
            var m = LocalMatrix(ob);
            var cur = ob;
            int parent = BlockOf(Ptr(ob, "parent"));
            for (int guard = 0; parent >= 0 && guard < 32; guard++)
            {
                var pinv = Mat4(cur, "parentinv");
                var po = StructAt(parent, 0);
                m = Mul(LocalMatrix(po), Mul(pinv, m));
                cur = po;
                parent = BlockOf(Ptr(po, "parent"));
            }
            return m;
        }

        // ------------------------------------------------------------------
        // Meshes

        class Layer { public int type; public string name; public ulong data; }

        List<Layer> Layers(View me, params string[] keys)
        {
            var o = new List<Layer>();
            string key = First(me, keys);
            if (key == "") return o;
            var cd = Sub(me, key);
            long n = Int(cd, "totlayer", 0);
            int lb = BlockOf(Ptr(cd, "layers"));
            if (lb < 0) return o;
            for (int i = 0; i < n; i++)
            {
                var l = StructAt(lb, i);
                if (!l.ok || l.off + structs[l.type].size > b.Length) break;
                o.Add(new Layer { type = (int)Int(l, "type", -1), name = Str(l, "name"), data = Ptr(l, "data") });
            }
            return o;
        }

        static Layer Find(List<Layer> ls, int type, string name = "")
        {
            foreach (var l in ls) if (l.type == type && (name == "" || l.name == name)) return l;
            return null;
        }

        /// Object blocks in the first scene's collections, skipping
        /// collections that are hidden or excluded; null when not found.
        HashSet<int> SceneObjects()
        {
            try
            {
                int sc = -1;
                for (int i = 0; i < blocks.Count; i++) if (blocks[i].code == "SC") { sc = i; break; }
                if (sc < 0) return null;
                var scene = StructAt(sc, 0);
                int master = BlockOf(Ptr(scene, "master_collection"));
                if (master < 0) return null;
                // Collections excluded or hidden in the view layer.
                var excluded = new HashSet<int>();
                var layers = Sub(scene, "view_layers");
                int vlb = BlockOf(Ptr(layers, "first"));
                if (vlb >= 0)
                {
                    var vl = StructAt(vlb, 0);
                    var stack = new List<int>();
                    var lcs = Sub(vl, "layer_collections");
                    int lc = BlockOf(Ptr(lcs, "first"));
                    if (lc >= 0) stack.Add(lc);
                    for (int guard = 0; stack.Count > 0 && guard < 20000; guard++)
                    {
                        int cur = stack[stack.Count - 1];
                        stack.RemoveAt(stack.Count - 1);
                        var l = StructAt(cur, 0);
                        long flag = Int(l, "flag", 0);
                        int col = BlockOf(Ptr(l, "collection"));
                        // LAYER_COLLECTION_EXCLUDE (1 << 4), LAYER_COLLECTION_HIDE (1 << 6)
                        if (col >= 0 && ((flag & (1 << 4)) != 0 || (flag & (1 << 6)) != 0)) excluded.Add(col);
                        int nx = BlockOf(Ptr(l, "next"));
                        if (nx >= 0) stack.Add(nx);
                        int ch = BlockOf(Ptr(Sub(l, "layer_collections"), "first"));
                        if (ch >= 0) stack.Add(ch);
                    }
                }
                var found = new HashSet<int>();
                var seen = new HashSet<int>();
                var todo = new List<int> { master };
                while (todo.Count > 0 && seen.Count < 10000)
                {
                    int c = todo[todo.Count - 1];
                    todo.RemoveAt(todo.Count - 1);
                    if (!seen.Add(c) || excluded.Contains(c)) continue;
                    var col = StructAt(c, 0);
                    long cflag = Int(col, "flag", 0);
                    // COLLECTION_HIDE_VIEWPORT (1 << 0), COLLECTION_HIDE_RENDER (1 << 3)
                    if (c != master && ((cflag & 1) != 0 || (cflag & 8) != 0)) continue;
                    int go = BlockOf(Ptr(Sub(col, "gobject"), "first"));
                    for (int guard = 0; go >= 0 && guard < 100000; guard++)
                    {
                        var co = StructAt(go, 0);
                        int ob = BlockOf(Ptr(co, "ob"));
                        if (ob >= 0) found.Add(ob);
                        go = BlockOf(Ptr(co, "next"));
                    }
                    int ch = BlockOf(Ptr(Sub(col, "children"), "first"));
                    for (int guard = 0; ch >= 0 && guard < 10000; guard++)
                    {
                        var cc = StructAt(ch, 0);
                        int sub = BlockOf(Ptr(cc, "collection"));
                        if (sub >= 0) todo.Add(sub);
                        ch = BlockOf(Ptr(cc, "next"));
                    }
                }
                return found.Count > 0 ? found : null;
            }
            catch (Exception) { return null; }
        }

        MMesh Mesh(int bi, float[] m, View ob)
        {
            int keep = ctx;
            try { return MeshIn(bi, m, ob); }
            finally { ctx = keep; }
        }

        MMesh MeshIn(int bi, float[] m, View ob)
        {
            // Modifiers are data of the object block (read with its context).
            int obCtx = ctx;
            ctx = bi;
            var me = StructAt(bi, 0);
            long nv = Int(me, First(me, "verts_num", "totvert"), 0);
            long nf = Int(me, First(me, "faces_num", "totpoly"), 0);
            long nl = Int(me, First(me, "corners_num", "totloop"), 0);
            if (nv <= 0 || nf <= 0 || nl <= 0) return null;
            var vl = Layers(me, "vert_data", "vdata");
            var fl = Layers(me, "face_data", "pdata");
            var ll = Layers(me, "corner_data", "ldata");
            // Positions
            var pos = new float[nv * 3];
            var lp = Find(vl, CD_PROP_FLOAT3, "position");
            int pb = lp != null ? BlockOf(lp.data) : -1;
            if (pb >= 0)
            {
                int o = blocks[pb].off;
                for (int i = 0; i < nv * 3; i++) pos[i] = F32(o + i * 4);
            }
            else
            {
                int mv = BlockOf(Ptr(me, "mvert"));
                if (mv < 0) { var lm = Find(vl, CD_MVERT); mv = lm != null ? BlockOf(lm.data) : -1; }
                if (mv < 0) return null;
                for (int i = 0; i < nv; i++)
                {
                    var co = Floats(StructAt(mv, i), "co");
                    if (co.Length >= 3) { pos[i * 3] = co[0]; pos[i * 3 + 1] = co[1]; pos[i * 3 + 2] = co[2]; }
                }
            }
            // Faces: offsets into the corners
            var starts = new int[nf];
            var counts = new int[nf];
            var matOf = new int[nf];
            var smooth = new bool[nf];
            int offB = BlockOf(Ptr(me, "poly_offset_indices"));
            if (offB >= 0)
            {
                int o = blocks[offB].off;
                for (int i = 0; i < nf; i++) { starts[i] = S32(o + i * 4); counts[i] = S32(o + (i + 1) * 4) - starts[i]; }
                var mil = Find(fl, CD_PROP_INT32, "material_index");
                int mb = mil != null ? BlockOf(mil.data) : -1;
                if (mb >= 0) for (int i = 0; i < nf; i++) matOf[i] = S32(blocks[mb].off + i * 4);
                var sh = Find(fl, CD_PROP_BOOL, "sharp_face");
                int sb = sh != null ? BlockOf(sh.data) : -1;
                for (int i = 0; i < nf; i++) smooth[i] = sb < 0 || b[blocks[sb].off + i] == 0;
            }
            else
            {
                int mp = BlockOf(Ptr(me, "mpoly"));
                if (mp < 0) { var lmp = Find(fl, CD_MPOLY); mp = lmp != null ? BlockOf(lmp.data) : -1; }
                if (mp < 0) return null;
                for (int i = 0; i < nf; i++)
                {
                    var p = StructAt(mp, i);
                    starts[i] = (int)Int(p, "loopstart", 0);
                    counts[i] = (int)Int(p, "totloop", 0);
                    matOf[i] = (int)Int(p, "mat_nr", 0);
                    smooth[i] = (Int(p, "flag", 0) & 1) != 0;
                }
            }
            // Corners -> vertex
            var cornerVert = new int[nl];
            var cv = Find(ll, CD_PROP_INT32, ".corner_vert");
            int cvb = cv != null ? BlockOf(cv.data) : -1;
            if (cvb >= 0) for (int i = 0; i < nl; i++) cornerVert[i] = S32(blocks[cvb].off + i * 4);
            else
            {
                int ml = BlockOf(Ptr(me, "mloop"));
                if (ml < 0) { var lml = Find(ll, CD_MLOOP); ml = lml != null ? BlockOf(lml.data) : -1; }
                if (ml < 0) return null;
                for (int i = 0; i < nl; i++) cornerVert[i] = (int)Int(StructAt(ml, i), "v", 0);
            }
            // UVs (first map)
            float[] uv = null;
            Layer uvl = null;
            foreach (var l in ll) if (l.type == CD_PROP_FLOAT2 && !l.name.StartsWith(".")) { uvl = l; break; }
            int ub = uvl != null ? BlockOf(uvl.data) : -1;
            if (ub >= 0)
            {
                uv = new float[nl * 2];
                for (int i = 0; i < nl * 2; i++) uv[i] = F32(blocks[ub].off + i * 4);
            }
            else
            {
                var lu = Find(ll, CD_MLOOPUV);
                int lub = lu != null ? BlockOf(lu.data) : -1;
                if (lub >= 0)
                {
                    uv = new float[nl * 2];
                    for (int i = 0; i < nl; i++)
                    {
                        var u = Floats(StructAt(lub, i), "uv");
                        if (u.Length >= 2) { uv[i * 2] = u[0]; uv[i * 2 + 1] = u[1]; }
                    }
                }
            }
            // Modifiers that change the shape the most: Mirror and Subdivision.
            var poly = new Poly { pos = pos, starts = starts, counts = counts, corner = cornerVert, uv = uv, mat = matOf, smooth = smooth };
            ctx = obCtx;
            ApplyModifiers(ob, poly);
            ctx = bi;
            pos = poly.pos; starts = poly.starts; counts = poly.counts; cornerVert = poly.corner; uv = poly.uv; matOf = poly.mat; smooth = poly.smooth;
            nv = pos.Length / 3; nf = starts.Length; nl = cornerVert.Length;
            // Face and smooth vertex normals (Newell), in Blender space
            var fn = new float[nf * 3];
            var vn = new float[nv * 3];
            for (int i = 0; i < nf; i++)
            {
                float x = 0, y = 0, z = 0;
                int s = starts[i];
                for (int k = 0; k < counts[i]; k++)
                {
                    int ia = cornerVert[s + k], ib = cornerVert[s + (k + 1) % counts[i]];
                    if (ia >= nv || ib >= nv || ia < 0 || ib < 0) continue;
                    float ax = pos[ia * 3], ay = pos[ia * 3 + 1], az = pos[ia * 3 + 2];
                    float bx = pos[ib * 3], by = pos[ib * 3 + 1], bz = pos[ib * 3 + 2];
                    x += (ay - by) * (az + bz); y += (az - bz) * (ax + bx); z += (ax - bx) * (ay + by);
                }
                float len = (float)Math.Sqrt(x * x + y * y + z * z);
                if (len > 1e-20f) { x /= len; y /= len; z /= len; }
                fn[i * 3] = x; fn[i * 3 + 1] = y; fn[i * 3 + 2] = z;
                if (smooth[i])
                    for (int k = 0; k < counts[i]; k++)
                    {
                        int vi = cornerVert[s + k];
                        if (vi < 0 || vi >= nv) continue;
                        vn[vi * 3] += x; vn[vi * 3 + 1] += y; vn[vi * 3 + 2] += z;
                    }
            }
            // Materials of the mesh
            var mats = new List<int>();
            long totcol = Int(me, "totcol", 0);
            int matArr = BlockOf(Ptr(me, "mat"));
            for (int i = 0; i < totcol; i++)
            {
                ulong mp = matArr >= 0 ? PtrAt(blocks[matArr].off + i * ptr) : 0;
                mats.Add(Material(BlockOf(mp)));
            }
            // World matrix (Blender) then z-up -> y-up; normals by the same
            // rotation (non-uniform scale is rare in exported models).
            bool flip = Det3(m) < 0f;
            var outPos = new List<float>();
            var outNor = new List<float>();
            var outUv = new List<float>();
            var byMat = new Dictionary<int, List<uint>>();
            for (int i = 0; i < nf; i++)
            {
                int mi = mats.Count > 0 ? Math.Max(0, Math.Min(matOf[i], mats.Count - 1)) : 0;
                List<uint> idx;
                if (!byMat.TryGetValue(mi, out idx)) { idx = new List<uint>(); byMat[mi] = idx; }
                int s = starts[i];
                for (int k = 1; k < counts[i] - 1; k++)
                {
                    int[] corners = flip ? new[] { s, s + k + 1, s + k } : new[] { s, s + k, s + k + 1 };
                    foreach (int c in corners)
                    {
                        if (c >= nl) continue;
                        int vi = cornerVert[c];
                        if (vi < 0 || vi >= nv) vi = 0;
                        float px = pos[vi * 3], py = pos[vi * 3 + 1], pz = pos[vi * 3 + 2];
                        float wx = m[0] * px + m[4] * py + m[8] * pz + m[12];
                        float wy = m[1] * px + m[5] * py + m[9] * pz + m[13];
                        float wz = m[2] * px + m[6] * py + m[10] * pz + m[14];
                        float nx, ny, nz;
                        float vl2 = vn[vi * 3] * vn[vi * 3] + vn[vi * 3 + 1] * vn[vi * 3 + 1] + vn[vi * 3 + 2] * vn[vi * 3 + 2];
                        if (smooth[i] && vl2 > 1e-20f) { nx = vn[vi * 3]; ny = vn[vi * 3 + 1]; nz = vn[vi * 3 + 2]; }
                        else { nx = fn[i * 3]; ny = fn[i * 3 + 1]; nz = fn[i * 3 + 2]; }
                        float tx = m[0] * nx + m[4] * ny + m[8] * nz, ty = m[1] * nx + m[5] * ny + m[9] * nz, tz = m[2] * nx + m[6] * ny + m[10] * nz;
                        float tl = (float)Math.Sqrt(tx * tx + ty * ty + tz * tz);
                        if (tl > 1e-20f) { tx /= tl; ty /= tl; tz /= tl; }
                        if (flip) { tx = -tx; ty = -ty; tz = -tz; }
                        idx.Add((uint)(outPos.Count / 3));
                        outPos.Add(wx); outPos.Add(wz); outPos.Add(-wy);
                        outNor.Add(tx); outNor.Add(tz); outNor.Add(-ty);
                        if (uv != null) { outUv.Add(uv[c * 2]); outUv.Add(1f - uv[c * 2 + 1]); }
                    }
                }
            }
            if (outPos.Count == 0) return null;
            var mesh = new MMesh { pos = outPos.ToArray(), nor = outNor.ToArray(), uv = uv != null ? outUv.ToArray() : null };
            foreach (var kv in byMat)
                if (kv.Value.Count > 0) mesh.parts.Add(new MPart { material = kv.Key < mats.Count ? mats[kv.Key] : -1, indices = kv.Value.ToArray() });
            return mesh;
        }

        // ------------------------------------------------------------------
        // Modifiers

        class Poly
        {
            public float[] pos, uv;
            public int[] starts, counts, corner, mat;
            public bool[] smooth;
        }

        void ApplyModifiers(View ob, Poly p)
        {
            int md = BlockOf(Ptr(Sub(ob, "modifiers"), "first"));
            for (int guard = 0; md >= 0 && guard < 64; guard++)
            {
                var mod = StructAt(md, 0);
                var head = Sub(mod, "modifier");
                long mode = Int(head, "mode", 3);
                bool on = (mode & 2) != 0 || (mode & 1) != 0;   // render or viewport
                try
                {
                    if (on && mod.type == "MirrorModifierData" && Ptr(mod, "mirror_ob") == 0) Mirror(p, (int)Int(mod, "flag", 8), Float(mod, "tolerance", 0.001f));
                    else if (on && mod.type == "ArrayModifierData" && Int(mod, "fit_type", 0) == 0)
                    {
                        long flags = Int(mod, "offset_type", 2);
                        var cst = Floats(mod, "offset");
                        var rel = Floats(mod, "scale");
                        ArrayCopies(p, (int)Int(mod, "count", 2), (flags & 1) != 0 && cst.Length >= 3 ? cst : null, (flags & 2) != 0 && rel.Length >= 3 ? rel : null);
                    }
                    else if (on && mod.type == "SubsurfModifierData")
                    {
                        int lv = (int)Math.Max(Int(mod, "levels", 1), Int(mod, "renderLevels", 1));
                        bool simple = Int(mod, "subdivType", 0) == 1;
                        for (int i = 0; i < Math.Min(lv, 3); i++)
                        {
                            if (p.corner.Length * 4 > 1500000) break;   // keep phones fast
                            Subdivide(p, simple);
                        }
                    }
                }
                catch (Exception) { }
                md = BlockOf(Ptr(head, "next"));
            }
        }

        /// Array modifier (fixed count): copies moved by a constant and/or a
        /// relative offset (times the mesh's size).
        static void ArrayCopies(Poly p, int count, float[] constant, float[] relative)
        {
            int nv = p.pos.Length / 3, nf = p.starts.Length, nl = p.corner.Length;
            if (count <= 1 || nv == 0) return;
            count = Math.Min(count, Math.Max(1, 2000000 / Math.Max(1, nl)));   // phones: a sane size
            var min = new float[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var max = new float[] { float.MinValue, float.MinValue, float.MinValue };
            for (int v = 0; v < nv; v++)
                for (int c = 0; c < 3; c++) { min[c] = Math.Min(min[c], p.pos[v * 3 + c]); max[c] = Math.Max(max[c], p.pos[v * 3 + c]); }
            var step = new float[3];
            for (int c = 0; c < 3; c++)
                step[c] = (constant != null ? constant[c] : 0f) + (relative != null ? relative[c] * (max[c] - min[c]) : 0f);
            var pos = new float[nv * 3 * count];
            var starts = new int[nf * count]; var counts = new int[nf * count]; var mat = new int[nf * count]; var smooth = new bool[nf * count];
            var corner = new int[nl * count];
            var uv = p.uv != null ? new float[nl * 2 * count] : null;
            for (int k = 0; k < count; k++)
            {
                for (int v = 0; v < nv; v++)
                    for (int c = 0; c < 3; c++) pos[(k * nv + v) * 3 + c] = p.pos[v * 3 + c] + step[c] * k;
                for (int f = 0; f < nf; f++)
                {
                    starts[k * nf + f] = p.starts[f] + k * nl; counts[k * nf + f] = p.counts[f];
                    mat[k * nf + f] = p.mat[f]; smooth[k * nf + f] = p.smooth[f];
                }
                for (int l = 0; l < nl; l++) corner[k * nl + l] = p.corner[l] + k * nv;
                if (uv != null) Array.Copy(p.uv, 0, uv, k * nl * 2, nl * 2);
            }
            p.pos = pos; p.starts = starts; p.counts = counts; p.corner = corner; p.uv = uv; p.mat = mat; p.smooth = smooth;
        }

        /// Mirror modifier: copies mirrored on each chosen axis, the
        /// vertices on the mirror plane merged.
        static void Mirror(Poly p, int flag, float tol)
        {
            bool merge = (flag & (1 << 7)) == 0;
            for (int axis = 0; axis < 3; axis++)
            {
                if ((flag & (1 << (3 + axis))) == 0) continue;
                int nv = p.pos.Length / 3, nf = p.starts.Length, nl = p.corner.Length;
                var pos = new List<float>(p.pos);
                var map = new int[nv];
                for (int v = 0; v < nv; v++)
                {
                    float c = p.pos[v * 3 + axis];
                    if (merge && Math.Abs(c) <= Math.Max(tol, 1e-6f)) { map[v] = v; continue; }
                    map[v] = pos.Count / 3;
                    pos.Add(p.pos[v * 3]); pos.Add(p.pos[v * 3 + 1]); pos.Add(p.pos[v * 3 + 2]);
                    pos[pos.Count - 3 + axis] = -c;
                }
                var starts = new int[nf * 2];
                var counts = new int[nf * 2];
                var corner = new int[nl * 2];
                var uv = p.uv != null ? new float[nl * 4] : null;
                var mat = new int[nf * 2];
                var smooth = new bool[nf * 2];
                Array.Copy(p.starts, starts, nf); Array.Copy(p.counts, counts, nf); Array.Copy(p.corner, corner, nl);
                Array.Copy(p.mat, mat, nf); Array.Copy(p.smooth, smooth, nf);
                if (uv != null) Array.Copy(p.uv, uv, nl * 2);
                int at = nl;
                for (int f = 0; f < nf; f++)
                {
                    int n = p.counts[f], s = p.starts[f];
                    starts[nf + f] = at; counts[nf + f] = n; mat[nf + f] = p.mat[f]; smooth[nf + f] = p.smooth[f];
                    // Reversed so the copy faces outwards.
                    for (int k = 0; k < n; k++)
                    {
                        int src = s + (n - k) % n;
                        corner[at + k] = map[p.corner[src]];
                        if (uv != null) { uv[(at + k) * 2] = p.uv[src * 2]; uv[(at + k) * 2 + 1] = p.uv[src * 2 + 1]; }
                    }
                    at += n;
                }
                p.pos = pos.ToArray(); p.starts = starts; p.counts = counts; p.corner = corner; p.uv = uv; p.mat = mat; p.smooth = smooth;
            }
        }

        /// One level of Catmull-Clark (or simple) subdivision; UVs are
        /// split linearly.
        static void Subdivide(Poly p, bool simple)
        {
            int nv = p.pos.Length / 3, nf = p.starts.Length;
            var edges = new Dictionary<long, int>();
            var eA = new List<int>(); var eB = new List<int>(); var eFaces = new List<int>(); var eSum = new List<float[]>();
            var fp = new float[nf * 3];
            for (int f = 0; f < nf; f++)
            {
                int n = p.counts[f], s = p.starts[f];
                float x = 0, y = 0, z = 0;
                for (int k = 0; k < n; k++) { int v = p.corner[s + k]; x += p.pos[v * 3]; y += p.pos[v * 3 + 1]; z += p.pos[v * 3 + 2]; }
                fp[f * 3] = x / n; fp[f * 3 + 1] = y / n; fp[f * 3 + 2] = z / n;
                for (int k = 0; k < n; k++)
                {
                    int a = p.corner[s + k], b2 = p.corner[s + (k + 1) % n];
                    long key = a < b2 ? ((long)a << 32) | (uint)b2 : ((long)b2 << 32) | (uint)a;
                    int e;
                    if (!edges.TryGetValue(key, out e)) { e = eA.Count; edges[key] = e; eA.Add(a); eB.Add(b2); eFaces.Add(0); eSum.Add(new float[3]); }
                    eFaces[e]++;
                    eSum[e][0] += fp[f * 3]; eSum[e][1] += fp[f * 3 + 1]; eSum[e][2] += fp[f * 3 + 2];
                }
            }
            int ne = eA.Count;
            // Edge points
            var ep = new float[ne * 3];
            for (int e = 0; e < ne; e++)
                for (int c = 0; c < 3; c++)
                {
                    float mid = (p.pos[eA[e] * 3 + c] + p.pos[eB[e] * 3 + c]) * 0.5f;
                    ep[e * 3 + c] = simple || eFaces[e] != 2 ? mid : (p.pos[eA[e] * 3 + c] + p.pos[eB[e] * 3 + c] + eSum[e][c] / eFaces[e] * 2f) / 4f;
                }
            // Vertex points
            var vp = new float[nv * 3];
            Array.Copy(p.pos, vp, nv * 3);
            if (!simple)
            {
                var fSum = new float[nv * 3]; var fCnt = new int[nv];
                var rSum = new float[nv * 3]; var rCnt = new int[nv];
                var bSum = new float[nv * 3]; var bCnt = new int[nv];
                for (int f = 0; f < nf; f++)
                    for (int k = 0; k < p.counts[f]; k++)
                    {
                        int v = p.corner[p.starts[f] + k];
                        fSum[v * 3] += fp[f * 3]; fSum[v * 3 + 1] += fp[f * 3 + 1]; fSum[v * 3 + 2] += fp[f * 3 + 2]; fCnt[v]++;
                    }
                for (int e = 0; e < ne; e++)
                {
                    foreach (int v in new[] { eA[e], eB[e] })
                        for (int c = 0; c < 3; c++)
                        {
                            float mid = (p.pos[eA[e] * 3 + c] + p.pos[eB[e] * 3 + c]) * 0.5f;
                            rSum[v * 3 + c] += mid;
                            if (eFaces[e] == 1) bSum[v * 3 + c] += mid;
                        }
                    rCnt[eA[e]]++; rCnt[eB[e]]++;
                    if (eFaces[e] == 1) { bCnt[eA[e]]++; bCnt[eB[e]]++; }
                }
                for (int v = 0; v < nv; v++)
                {
                    if (fCnt[v] == 0) continue;
                    for (int c = 0; c < 3; c++)
                    {
                        float P = p.pos[v * 3 + c];
                        if (bCnt[v] >= 2)
                            vp[v * 3 + c] = (bSum[v * 3 + c] / bCnt[v] + P) * 0.5f;   // boundary: (P + mid avg) / 2 = (6P + a + b) / 8
                        else if (bCnt[v] == 0 && fCnt[v] >= 3)
                        {
                            float n = fCnt[v];
                            vp[v * 3 + c] = (fSum[v * 3 + c] / n + 2f * rSum[v * 3 + c] / rCnt[v] + (n - 3f) * P) / n;
                        }
                    }
                }
            }
            // New mesh: vertices = vertex points + edge points + face points.
            var pos = new float[(nv + ne + nf) * 3];
            Array.Copy(vp, 0, pos, 0, nv * 3);
            Array.Copy(ep, 0, pos, nv * 3, ne * 3);
            Array.Copy(fp, 0, pos, (nv + ne) * 3, nf * 3);
            int total = 0;
            for (int f = 0; f < nf; f++) total += p.counts[f];
            var starts = new int[total]; var counts = new int[total]; var mat = new int[total]; var smooth = new bool[total];
            var corner = new int[total * 4];
            var uv = p.uv != null ? new float[total * 8] : null;
            int q = 0;
            for (int f = 0; f < nf; f++)
            {
                int n = p.counts[f], s = p.starts[f];
                float cu = 0, cv = 0;
                if (uv != null) for (int k = 0; k < n; k++) { cu += p.uv[(s + k) * 2]; cv += p.uv[(s + k) * 2 + 1]; }
                cu /= n; cv /= n;
                for (int k = 0; k < n; k++)
                {
                    int prev = (k + n - 1) % n, next = (k + 1) % n;
                    int v = p.corner[s + k];
                    int eNext = edges[Key(v, p.corner[s + next])], ePrev = edges[Key(p.corner[s + prev], v)];
                    starts[q] = q * 4; counts[q] = 4; mat[q] = p.mat[f]; smooth[q] = p.smooth[f];
                    corner[q * 4] = v;
                    corner[q * 4 + 1] = nv + eNext;
                    corner[q * 4 + 2] = nv + ne + f;
                    corner[q * 4 + 3] = nv + ePrev;
                    if (uv != null)
                    {
                        float u0 = p.uv[(s + k) * 2], v0 = p.uv[(s + k) * 2 + 1];
                        float un = p.uv[(s + next) * 2], vn = p.uv[(s + next) * 2 + 1];
                        float up = p.uv[(s + prev) * 2], vpv = p.uv[(s + prev) * 2 + 1];
                        uv[q * 8] = u0; uv[q * 8 + 1] = v0;
                        uv[q * 8 + 2] = (u0 + un) * 0.5f; uv[q * 8 + 3] = (v0 + vn) * 0.5f;
                        uv[q * 8 + 4] = cu; uv[q * 8 + 5] = cv;
                        uv[q * 8 + 6] = (u0 + up) * 0.5f; uv[q * 8 + 7] = (v0 + vpv) * 0.5f;
                    }
                    q++;
                }
            }
            p.pos = pos; p.starts = starts; p.counts = counts; p.corner = corner; p.uv = uv; p.mat = mat; p.smooth = smooth;
        }

        static long Key(int a, int b)
        {
            return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        static float Det3(float[] m)
        {
            return m[0] * (m[5] * m[10] - m[9] * m[6]) - m[4] * (m[1] * m[10] - m[9] * m[2]) + m[8] * (m[1] * m[6] - m[5] * m[2]);
        }

        // ------------------------------------------------------------------
        // Materials and images

        static float Lin(float c)
        {
            return c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        int Material(int bi)
        {
            if (bi < 0) return -1;
            int cached;
            if (matCache.TryGetValue(bi, out cached)) return cached;
            int keep = ctx;
            ctx = bi;
            try { return MaterialIn(bi); }
            finally { ctx = keep; }
        }

        int MaterialIn(int bi)
        {
            var ma = StructAt(bi, 0);
            // Blender's viewport colour (r, g, b) is the base colour in sRGB.
            var mm = new MMaterial
            {
                name = IdName(ma),
                color = new[] { Lin(Float(ma, "r", 0.8f)), Lin(Float(ma, "g", 0.8f)), Lin(Float(ma, "b", 0.8f)), Float(ma, "a", 1f) },
                roughness = Float(ma, "roughness", 0.5f),
                metallic = Float(ma, "metallic", 0f),
            };
            // The node material's own values (Principled BSDF) rather than
            // the viewport colour, which is often left white.
            float[] baseCol;
            float rough, metal;
            if (Principled(ma, out baseCol, out rough, out metal))
            {
                if (baseCol != null) mm.color = new[] { baseCol[0], baseCol[1], baseCol[2], baseCol[3] };
                if (rough >= 0f) mm.roughness = rough;
                if (metal >= 0f) mm.metallic = metal;
            }
            var img = MaterialImage(ma);
            if (img != null)
            {
                model.textures.Add(new MTexture { data = img, mime = MModel.ImageMime(img) });
                mm.texture = model.textures.Count - 1;
                mm.color = new[] { 1f, 1f, 1f, mm.color[3] };
            }
            model.materials.Add(mm);
            matCache[bi] = model.materials.Count - 1;
            return model.materials.Count - 1;
        }

        /// Base Color / Roughness / Metallic of the material's Principled
        /// BSDF node (socket default values, already linear).
        bool Principled(View ma, out float[] color, out float rough, out float metal)
        {
            color = null; rough = -1f; metal = -1f;
            try
            {
                int nt = BlockOf(Ptr(ma, "nodetree"));
                if (nt < 0) return false;
                int nb = BlockOf(Ptr(Sub(StructAt(nt, 0), "nodes"), "first"));
                for (int guard = 0; nb >= 0 && guard < 1000; guard++)
                {
                    var node = StructAt(nb, 0);
                    if (Str(node, "idname") == "ShaderNodeBsdfPrincipled")
                    {
                        int sb = BlockOf(Ptr(Sub(node, "inputs"), "first"));
                        for (int g2 = 0; sb >= 0 && g2 < 200; g2++)
                        {
                            var sock = StructAt(sb, 0);
                            string id = Str(sock, "identifier");
                            if (id == "") id = Str(sock, "name");
                            int dv = BlockOf(Ptr(sock, "default_value"));
                            if (dv >= 0)
                            {
                                var val = Floats(StructAt(dv, 0), "value");
                                if (id == "Base Color" && val.Length >= 4) color = new[] { val[0], val[1], val[2], val[3] };
                                else if (id == "Roughness" && val.Length >= 1) rough = val[0];
                                else if (id == "Metallic" && val.Length >= 1) metal = val[0];
                            }
                            sb = BlockOf(Ptr(sock, "next"));
                        }
                        return true;
                    }
                    nb = BlockOf(Ptr(node, "next"));
                }
            }
            catch (Exception) { }
            return false;
        }

        /// The image of the first image-texture node of the material.
        byte[] MaterialImage(View ma)
        {
            int nt = BlockOf(Ptr(ma, "nodetree"));
            if (nt < 0) return null;
            var tree = StructAt(nt, 0);
            var nodes = Sub(tree, "nodes");
            int nb = BlockOf(Ptr(nodes, "first"));
            for (int guard = 0; nb >= 0 && guard < 1000; guard++)
            {
                var node = StructAt(nb, 0);
                if (Str(node, "idname") == "ShaderNodeTexImage")
                {
                    int ib = BlockOf(Ptr(node, "id"));
                    if (ib >= 0)
                    {
                        int keep = ctx;
                        ctx = ib;
                        byte[] img;
                        try { img = Image(StructAt(ib, 0)); }
                        finally { ctx = keep; }
                        if (img != null) return img;
                    }
                }
                nb = BlockOf(Ptr(node, "next"));
            }
            return null;
        }

        byte[] Image(View im)
        {
            int pf = BlockOf(Ptr(im, "packedfile"));
            if (pf < 0)
            {
                var lst = Sub(im, "packedfiles");
                int first = BlockOf(Ptr(lst, "first"));
                if (first >= 0) pf = BlockOf(Ptr(StructAt(first, 0), "packedfile"));
            }
            if (pf >= 0)
            {
                var pst = StructAt(pf, 0);
                long size = Int(pst, "size", 0);
                int db = BlockOf(Ptr(pst, "data"));
                if (db >= 0 && size > 0 && blocks[db].off + size <= b.Length)
                {
                    var bytes = new byte[size];
                    Buffer.BlockCopy(b, blocks[db].off, bytes, 0, (int)size);
                    return ImageConv.ToWebImage(bytes, Str(im, "name"));
                }
                return null;
            }
            string p = Str(im, "filepath");
            if (p == "") p = Str(im, "name");
            p = p.Replace('\\', '/');
            if (p.StartsWith("//")) p = p.Substring(2);
            return FbxImport.FindImage(Path.Combine(dir, p), p, dir);
        }
    }
}
