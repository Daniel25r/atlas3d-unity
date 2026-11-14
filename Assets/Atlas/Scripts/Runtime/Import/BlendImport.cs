// ATLAS 3D (Unity) - Blender files (.blend) without Blender, ported from
// the Godot version: the file carries a description of its own structures
// («DNA»), which is read to find the objects, their meshes (vertices,
// faces, UVs, materials), the material colours and their image textures
// (packed in the file or next to it), and where each object is.
// Meshes of Blender 2.8 - 4.x. Modifiers (subdivision, mirror…), curves,
// text and armatures are not evaluated: the base meshes come in as they are.
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
            for (int bi = 0; bi < blocks.Count; bi++)
            {
                if (blocks[bi].code != "OB") continue;
                var ob = StructAt(bi, 0);
                if (!ob.ok || Int(ob, "type", 0) != 1) continue;   // OB_MESH
                int meBi = BlockOf(Ptr(ob, "data"));
                if (meBi < 0) continue;
                if ((Int(ob, "visibility_flag", 0) & 1) != 0) continue;   // hidden in the viewport
                var mesh = Mesh(meBi, ObjectMatrix(ob));
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
            int i;
            return p != 0 && byPtr.TryGetValue(p, out i) ? i : -1;
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

        MMesh Mesh(int bi, float[] m)
        {
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
            var ma = StructAt(bi, 0);
            // Blender's viewport colour (r, g, b) is the base colour in sRGB.
            var mm = new MMaterial
            {
                name = IdName(ma),
                color = new[] { Lin(Float(ma, "r", 0.8f)), Lin(Float(ma, "g", 0.8f)), Lin(Float(ma, "b", 0.8f)), Float(ma, "a", 1f) },
                roughness = Float(ma, "roughness", 0.5f),
                metallic = Float(ma, "metallic", 0f),
            };
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
                        var img = Image(StructAt(ib, 0));
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
