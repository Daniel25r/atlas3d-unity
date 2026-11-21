// ATLAS 3D (Unity) - Source engine models (Source Filmmaker, Garry's Mod,
// TF2, HL2…): .mdl (+ .vvd vertices, .dx90.vtx triangles) with the
// skeleton, skin weights and the materials (.vmt → .vtf textures, DXT1/3/5
// and plain formats). Also Garry's Mod addons (.gma), unpacked first.
// Written from the public file layouts (studio.h, optimize.h,
// vtf.h); MDL versions 44 to 49.
// Source: Z up, X forward, inches. glTF: Y up, Z forward, metres.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Atlas.Import
{
    public static class SourceMdlImport
    {
        const float Inch = 0.0254f;

        static int I32(byte[] b, int o) { return BitConverter.ToInt32(b, o); }
        static int U16(byte[] b, int o) { return BitConverter.ToUInt16(b, o); }
        static short S16(byte[] b, int o) { return BitConverter.ToInt16(b, o); }
        static float F(byte[] b, int o) { return BitConverter.ToSingle(b, o); }

        static string CStr(byte[] b, int o)
        {
            if (o < 0 || o >= b.Length) return "";
            int e = o;
            while (e < b.Length && b[e] != 0 && e - o < 512) e++;
            return Encoding.ASCII.GetString(b, o, e - o);
        }

        static void Need(byte[] b, int o, int n, string what)
        {
            if (o < 0 || n < 0 || (long)o + n > b.Length) throw new Exception("el modelo de Source está incompleto o dañado (" + what + ")");
        }

        // Source (x fwd, y left, z up) → glTF (x, y up, z fwd): a rotation.
        static void P(float x, float y, float z, float[] o, int i, float s)
        {
            o[i] = y * s; o[i + 1] = z * s; o[i + 2] = x * s;
        }

        // ------------------------------------------------------------------

        public static MModel Load(string mdlPath)
        {
            var b = File.ReadAllBytes(mdlPath);
            if (b.Length < 408 || Encoding.ASCII.GetString(b, 0, 4) != "IDST")
                throw new Exception("no es un modelo de Source (.mdl)");
            int version = I32(b, 4);
            if (version < 44 || version > 49)
                throw new Exception("este .mdl es de la versión " + version + " del motor Source, que ATLAS todavía no lee (sí lee SFM, Garry's Mod, TF2 y HL2: versiones 44-49)");
            string dir = Path.GetDirectoryName(mdlPath);
            string baseName = Path.GetFileNameWithoutExtension(mdlPath);
            string vvdPath = Sibling(dir, baseName, ".vvd");
            string vtxPath = Sibling(dir, baseName, ".dx90.vtx") ?? Sibling(dir, baseName, ".dx80.vtx") ?? Sibling(dir, baseName, ".sw.vtx") ?? Sibling(dir, baseName, ".vtx");
            if (vvdPath == null || vtxPath == null)
                throw new Exception("al modelo de Source le faltan sus archivos " + (vvdPath == null ? baseName + ".vvd" : "") + (vvdPath == null && vtxPath == null ? " y " : "") + (vtxPath == null ? baseName + ".dx90.vtx" : "") + ": elígelos junto al .mdl o importa el .zip / .gma completo");
            var vvd = File.ReadAllBytes(vvdPath);
            var vtx = File.ReadAllBytes(vtxPath);

            var model = new MModel();
            string mdlName = CStr(b, 12);
            model.nodes.Add(new MNode { name = string.IsNullOrEmpty(mdlName) ? baseName : Path.GetFileNameWithoutExtension(mdlName) });

            // --- Bones
            int numBones = I32(b, 156), boneIndex = I32(b, 160);
            const int BoneSize = 216;
            Need(b, boneIndex, numBones * BoneSize, "huesos");
            var boneNode = new int[numBones];
            var world = new float[numBones][];
            for (int i = 0; i < numBones; i++)
            {
                int o = boneIndex + i * BoneSize;
                string name = CStr(b, o + I32(b, o));
                int parent = I32(b, o + 4);
                var n = new MNode { name = string.IsNullOrEmpty(name) ? "hueso" + i : name };
                P(F(b, o + 32), F(b, o + 36), F(b, o + 40), n.t, 0, Inch);
                // Quaternion: its axis turns like the positions.
                float qx = F(b, o + 44), qy = F(b, o + 48), qz = F(b, o + 52), qw = F(b, o + 56);
                n.r = new[] { qy, qz, qx, qw };
                n.parent = parent >= 0 && parent < i ? boneNode[parent] : 0;
                model.nodes.Add(n);
                boneNode[i] = model.nodes.Count - 1;
                var local = Trs(n.t, n.r);
                world[i] = parent >= 0 && parent < i ? Mul(world[parent], local) : local;
            }

            // The skin was made on the skeleton stored in each bone's
            // poseToBone matrix. The bones' default pose (pos/quat) usually
            // matches it, but not in many Garry's Mod / SFM characters
            // («proportion trick», $definebone): their joints sit away from
            // the mesh, so turning or moving a bone split and smeared the
            // skin (heads that looked doubled). Rest on the real skeleton.
            UseBindPose(b, boneIndex, BoneSize, numBones, model, boneNode, world);

            // --- Vertices (.vvd), LOD 0 with its fix-ups
            if (vvd.Length < 64 || Encoding.ASCII.GetString(vvd, 0, 4) != "IDSV") throw new Exception("el .vvd del modelo está dañado");
            int lod0Count = I32(vvd, 16);
            int numFixups = I32(vvd, 48), fixupStart = I32(vvd, 52), vertStart = I32(vvd, 56);
            var order = new List<int>();
            if (numFixups > 0)
            {
                Need(vvd, fixupStart, numFixups * 12, "vvd");
                for (int f = 0; f < numFixups; f++)
                {
                    int o = fixupStart + f * 12;
                    if (I32(vvd, o) < 0) continue;
                    int src = I32(vvd, o + 4), cnt = I32(vvd, o + 8);
                    for (int k = 0; k < cnt; k++) order.Add(src + k);
                }
            }
            else for (int k = 0; k < lod0Count; k++) order.Add(k);

            // --- Materials
            var textures = new List<string>();
            int numTex = I32(b, 204), texIndex = I32(b, 208);
            for (int i = 0; i < numTex; i++)
            {
                int o = texIndex + i * 64;
                if (o + 64 > b.Length) break;
                textures.Add(CStr(b, o + I32(b, o)).Replace('\\', '/'));
            }
            var cdPaths = new List<string>();
            int numCd = I32(b, 212), cdIndex = I32(b, 216);
            for (int i = 0; i < numCd && cdIndex + i * 4 + 4 <= b.Length; i++)
                cdPaths.Add(CStr(b, I32(b, cdIndex + i * 4)).Replace('\\', '/'));
            int numSkinRef = I32(b, 220), numSkinFam = I32(b, 224), skinIndex = I32(b, 228);
            var skin = new int[Math.Max(numSkinRef, 0)];
            for (int i = 0; i < skin.Length; i++)
                skin[i] = skinIndex + i * 2 + 2 <= b.Length && numSkinFam > 0 ? S16(b, skinIndex + i * 2) : i;
            var mats = new Materials(mdlPath, textures, cdPaths, model);

            // --- Body parts: the first (default) model of each
            int numBodyParts = I32(b, 232), bodyIndex = I32(b, 236);
            int vtxNumBody = I32(vtx, 28), vtxBodyOff = I32(vtx, 32);
            if (I32(vtx, 0) != 7) throw new Exception("el .vtx del modelo es de una versión que ATLAS no lee");
            int stride = -1;
            for (int bp = 0; bp < numBodyParts && bp < vtxNumBody; bp++)
            {
                int bo = bodyIndex + bp * 16;
                Need(b, bo, 16, "partes");
                int numModels = I32(b, bo + 4), modelIndex = I32(b, bo + 12);
                int vbo = vtxBodyOff + bp * 8;
                Need(vtx, vbo, 8, "vtx");
                int vNumModels = I32(vtx, vbo), vModelOff = I32(vtx, vbo + 4);
                for (int mi = 0; mi < numModels && mi < vNumModels; mi++)
                {
                    int mo = bo + modelIndex + mi * 148;
                    Need(b, mo, 148, "modelo");
                    int numMeshes = I32(b, mo + 72), meshIndex = I32(b, mo + 76), vertexIndex = I32(b, mo + 84);
                    if (numMeshes <= 0) continue;   // an empty body group («none»)
                    int vmo = vbo + vModelOff + mi * 8;
                    int vLods = I32(vtx, vmo), vLodOff = I32(vtx, vmo + 4);
                    if (vLods <= 0) continue;
                    int vlo = vmo + vLodOff;
                    int vNumMeshes = I32(vtx, vlo), vMeshOff = I32(vtx, vlo + 4);
                    var mesh = BuildModelMesh(b, vvd, vtx, vertStart, order, mo, meshIndex, numMeshes, vertexIndex / 48, vlo + vMeshOff, Math.Min(vNumMeshes, numMeshes), skin, mats, numBones, ref stride);
                    if (mesh == null) continue;
                    mesh.name = CStr(b, mo);
                    if (numBones > 1)
                    {
                        mesh.skinJoints = new int[numBones];
                        mesh.skinIbm = new float[numBones * 16];
                        for (int j = 0; j < numBones; j++)
                        {
                            mesh.skinJoints[j] = boneNode[j];
                            Array.Copy(InverseRigid(world[j]), 0, mesh.skinIbm, j * 16, 16);
                        }
                    }
                    else { mesh.joints = null; mesh.weights = null; }
                    model.meshes.Add(mesh);
                    model.nodes.Add(new MNode { name = mesh.name, parent = 0, mesh = model.meshes.Count - 1 });
                    break;   // default body group only
                }
            }
            if (model.meshes.Count == 0) throw new Exception("el modelo de Source no tiene mallas que ATLAS pueda leer");
            return model;
        }

        static string Sibling(string dir, string baseName, string ext)
        {
            string p = Path.Combine(dir, baseName + ext);
            if (File.Exists(p)) return p;
            // Phones keep file names but not always their case.
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                    if (string.Equals(Path.GetFileName(f), baseName + ext, StringComparison.OrdinalIgnoreCase)) return f;
            }
            catch (Exception) { }
            return null;
        }

        static MMesh BuildModelMesh(byte[] b, byte[] vvd, byte[] vtx, int vertStart, List<int> order, int mo, int meshIndex, int numMeshes,
            int modelFirst, int vMeshBase, int vNumMeshes, int[] skin, Materials mats, int numBones, ref int stride)
        {
            var remap = new Dictionary<int, uint>();
            var pos = new List<float>();
            var nor = new List<float>();
            var uv = new List<float>();
            var joints = new List<ushort>();
            var weights = new List<float>();
            var mesh = new MMesh();
            for (int m = 0; m < vNumMeshes; m++)
            {
                int me = mo + meshIndex + m * 116;
                Need(b, me, 116, "malla");
                int material = I32(b, me), vertexOffset = I32(b, me + 12);
                int vm = vMeshBase + m * 9;
                Need(vtx, vm, 9, "vtx malla");
                int numSG = I32(vtx, vm), sgOff = I32(vtx, vm + 4);
                if (numSG <= 0) continue;
                int sgBase = vm + sgOff;
                if (stride < 0) stride = GuessStride(vtx, sgBase);
                var tris = new List<uint>();
                for (int g = 0; g < numSG; g++)
                {
                    int sg = sgBase + g * stride;
                    Need(vtx, sg, 25, "vtx grupo");
                    int numVerts = I32(vtx, sg), vertOff = I32(vtx, sg + 4), numIdx = I32(vtx, sg + 8), idxOff = I32(vtx, sg + 12);
                    Need(vtx, sg + vertOff, numVerts * 9, "vtx vértices");
                    Need(vtx, sg + idxOff, numIdx * 2, "vtx índices");
                    // The strips say how the indices make triangles: lists
                    // (three each) or strips (each new index one more).
                    int numStrips = I32(vtx, sg + 16), stripOff = I32(vtx, sg + 20);
                    int stripSize = stride == 33 ? 35 : 27;
                    var runs = new List<int[]>();   // start, count, isStrip
                    if (numStrips > 0 && sg + stripOff + numStrips * stripSize <= vtx.Length)
                        for (int st = 0; st < numStrips; st++)
                        {
                            int so = sg + stripOff + st * stripSize;
                            int n = I32(vtx, so), io = I32(vtx, so + 4);
                            if (n <= 0 || io < 0 || io + n > numIdx) continue;
                            runs.Add(new[] { io, n, (vtx[so + 18] & 2) != 0 ? 1 : 0 });
                        }
                    if (runs.Count == 0) runs.Add(new[] { 0, numIdx, 0 });
                    System.Func<int, int> corner = k =>
                    {
                        int li = U16(vtx, sg + idxOff + k * 2);
                        if (li >= numVerts) return -1;
                        int orig = U16(vtx, sg + vertOff + li * 9 + 4);
                        int gi = modelFirst + vertexOffset + orig;
                        int vi = gi < order.Count ? order[gi] : gi;
                        if (vi < 0 || vertStart + (vi + 1) * 48 > vvd.Length) return -1;
                        uint idx;
                        if (!remap.TryGetValue(gi, out idx))
                        {
                            idx = (uint)(pos.Count / 3);
                            remap[gi] = idx;
                            AddVertex(vvd, vertStart, vi, pos, nor, uv, joints, weights, numBones);
                        }
                        return (int)idx;
                    };
                    foreach (var r in runs)
                    {
                        int count = r[2] == 1 ? r[1] - 2 : r[1] / 3;
                        for (int t = 0; t < count; t++)
                        {
                            int k0, k1, k2;
                            if (r[2] == 1) { k0 = r[0] + t; k1 = r[0] + t + ((t & 1) == 0 ? 1 : 2); k2 = r[0] + t + ((t & 1) == 0 ? 2 : 1); }
                            else { k0 = r[0] + t * 3; k1 = k0 + 1; k2 = k0 + 2; }
                            int a0 = corner(k0), a1 = corner(k1), a2 = corner(k2);
                            if (a0 < 0 || a1 < 0 || a2 < 0 || a0 == a1 || a1 == a2 || a0 == a2) continue;   // broken or empty
                            tris.Add((uint)a0); tris.Add((uint)a1); tris.Add((uint)a2);
                        }
                    }
                }
                if (tris.Count == 0) continue;
                int tex = material >= 0 && material < skin.Length ? skin[material] : material;
                mesh.parts.Add(new MPart { material = mats.Get(tex), indices = tris.ToArray() });
            }
            if (mesh.parts.Count == 0) return null;
            DropStretched(mesh.parts, pos);
            mesh.parts.RemoveAll(p => p.indices.Length == 0);
            if (mesh.parts.Count == 0) return null;
            mesh.pos = pos.ToArray();
            mesh.nor = nor.ToArray();
            mesh.uv = uv.ToArray();
            if (numBones > 1) { mesh.joints = joints.ToArray(); mesh.weights = weights.ToArray(); }
            foreach (var part in mesh.parts) MMesh.FixWinding(mesh.pos, mesh.nor, part.indices);
            return mesh;
        }

        /// Triangles far bigger than the model's normal ones are reading
        /// mistakes (long dark slivers across the model): they go.
        static void DropStretched(List<MPart> parts, List<float> pos)
        {
            var edges = new List<float>();
            System.Func<uint, uint, float> len = (a, b) =>
            {
                float dx = pos[(int)a * 3] - pos[(int)b * 3], dy = pos[(int)a * 3 + 1] - pos[(int)b * 3 + 1], dz = pos[(int)a * 3 + 2] - pos[(int)b * 3 + 2];
                return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            };
            foreach (var p in parts)
                for (int i = 0; i + 2 < p.indices.Length; i += 3 * Math.Max(1, p.indices.Length / 30000))
                    edges.Add(Math.Max(len(p.indices[i], p.indices[i + 1]), Math.Max(len(p.indices[i + 1], p.indices[i + 2]), len(p.indices[i], p.indices[i + 2]))));
            if (edges.Count < 20) return;
            edges.Sort();
            float limit = Math.Max(edges[edges.Count / 2] * 30f, edges[(int)(edges.Count * 0.99f)] * 3f);
            foreach (var p in parts)
            {
                var keep = new List<uint>(p.indices.Length);
                for (int i = 0; i + 2 < p.indices.Length; i += 3)
                {
                    uint a = p.indices[i], b = p.indices[i + 1], c = p.indices[i + 2];
                    if (Math.Max(len(a, b), Math.Max(len(b, c), len(a, c))) > limit) continue;
                    keep.Add(a); keep.Add(b); keep.Add(c);
                }
                p.indices = keep.ToArray();
            }
        }

        /// The SDK's strip group header has 25 bytes; newer branches (CS:GO,
        /// newer SFM) 33. Picks the one whose offsets make sense.
        static int GuessStride(byte[] vtx, int sg)
        {
            foreach (int s in new[] { 25, 33 })
            {
                int o = sg + s;   // the second strip group (if any) or garbage
                bool ok = true;
                // The first group's own data must lie in the file.
                int nv = I32(vtx, sg), vo = I32(vtx, sg + 4), ni = I32(vtx, sg + 8), io = I32(vtx, sg + 12);
                if (nv < 0 || ni < 0 || sg + vo < 0 || sg + io < 0 || sg + vo + nv * 9 > vtx.Length || sg + io + ni * 2 > vtx.Length) ok = false;
                if (ok && s == 25 && o + 16 <= vtx.Length)
                {
                    // With 33-byte headers the «next» group read at +25 is nonsense.
                    int nv2 = I32(vtx, o), ni2 = I32(vtx, o + 8);
                    if (nv2 < 0 || ni2 < 0 || ni2 % 3 != 0 || nv2 > 65536) ok = s != 25;
                }
                if (ok) return s;
            }
            return 25;
        }

        static void AddVertex(byte[] vvd, int vertStart, int vi, List<float> pos, List<float> nor, List<float> uv, List<ushort> joints, List<float> weights, int numBones)
        {
            int o = vertStart + vi * 48;
            Need(vvd, o, 48, "vértice");
            var tmp = new float[3];
            P(F(vvd, o + 16), F(vvd, o + 20), F(vvd, o + 24), tmp, 0, Inch);
            pos.Add(tmp[0]); pos.Add(tmp[1]); pos.Add(tmp[2]);
            P(F(vvd, o + 28), F(vvd, o + 32), F(vvd, o + 36), tmp, 0, 1f);
            nor.Add(tmp[0]); nor.Add(tmp[1]); nor.Add(tmp[2]);
            uv.Add(F(vvd, o + 40)); uv.Add(F(vvd, o + 44));
            int nb = vvd[o + 15];
            float sum = 0f;
            var w = new float[4];
            var j = new ushort[4];
            for (int k = 0; k < 3; k++)
            {
                if (k >= nb) continue;
                int bone = vvd[o + 12 + k];
                if (bone >= numBones) continue;
                w[k] = F(vvd, o + k * 4);
                j[k] = (ushort)bone;
                sum += w[k];
            }
            if (sum <= 1e-6f) { w[0] = 1f; sum = 1f; }
            for (int k = 0; k < 4; k++) { joints.Add(j[k]); weights.Add(w[k] / sum); }
        }

        // ------------------------------------------------------------------
        // Matrices (column-major, like glTF)
        static void UseBindPose(byte[] b, int boneIndex, int boneSize, int numBones, MModel model, int[] boneNode, float[][] world)
        {
            if (numBones <= 0) return;
            var bind = new float[numBones][];
            bool differs = false;
            for (int i = 0; i < numBones; i++)
            {
                int o = boneIndex + i * boneSize + 96;   // matrix3x4_t poseToBone, rows
                var m = new float[12];
                for (int k = 0; k < 12; k++)
                {
                    m[k] = F(b, o + k * 4);
                    if (float.IsNaN(m[k]) || float.IsInfinity(m[k])) return;
                }
                // Rotation rows must be unit and at right angles (else: not stored).
                for (int r = 0; r < 3; r++)
                    for (int c = r; c < 3; c++)
                    {
                        float d = m[r * 4] * m[c * 4] + m[r * 4 + 1] * m[c * 4 + 1] + m[r * 4 + 2] * m[c * 4 + 2];
                        if (Math.Abs(d - (r == c ? 1f : 0f)) > 0.02f) return;
                    }
                // poseToBone: model → bone. Its inverse is the bone in the model:
                // rotation Rᵀ, position -Rᵀ·t (Source axes).
                var rot = new float[9];   // [row*3+col], bone → model
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 3; c++) rot[r * 3 + c] = m[c * 4 + r];
                var t = new float[3];
                for (int r = 0; r < 3; r++) t[r] = -(rot[r * 3] * m[3] + rot[r * 3 + 1] * m[7] + rot[r * 3 + 2] * m[11]);
                // To ATLAS axes: new = C·old, C maps (x, y, z) → (y, z, x).
                int[] map = { 1, 2, 0 };   // new axis a reads old axis map[a]
                var w = new float[16];
                for (int a = 0; a < 3; a++)
                    for (int c = 0; c < 3; c++) w[c * 4 + a] = rot[map[a] * 3 + map[c]];
                for (int a = 0; a < 3; a++) w[12 + a] = t[map[a]] * Inch;
                w[15] = 1f;
                bind[i] = w;
                for (int k = 0; k < 16 && !differs; k++)
                    if (Math.Abs(w[k] - world[i][k]) > (k >= 12 ? 0.002f : 0.01f)) differs = true;
            }
            if (!differs) return;
            for (int i = 0; i < numBones; i++)
            {
                int parent = I32(b, boneIndex + i * boneSize + 4);
                var local = parent >= 0 && parent < i ? Mul(InverseRigid(bind[parent]), bind[i]) : bind[i];
                var n = model.nodes[boneNode[i]];
                n.t = new[] { local[12], local[13], local[14] };
                n.r = Quat(local);
                world[i] = bind[i];
            }
        }

        static float[] Quat(float[] m)
        {
            float m00 = m[0], m11 = m[5], m22 = m[10];
            float m01 = m[4], m02 = m[8], m10 = m[1], m12 = m[9], m20 = m[2], m21 = m[6];
            float tr = m00 + m11 + m22, x, y, z, w;
            if (tr > 0f)
            {
                float s = (float)Math.Sqrt(tr + 1f) * 2f;
                w = 0.25f * s; x = (m21 - m12) / s; y = (m02 - m20) / s; z = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                w = (m21 - m12) / s; x = 0.25f * s; y = (m01 + m10) / s; z = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                float s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                w = (m02 - m20) / s; x = (m01 + m10) / s; y = 0.25f * s; z = (m12 + m21) / s;
            }
            else
            {
                float s = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
                w = (m10 - m01) / s; x = (m02 + m20) / s; y = (m12 + m21) / s; z = 0.25f * s;
            }
            return new[] { x, y, z, w };
        }


        static float[] Trs(float[] t, float[] q)
        {
            float x = q[0], y = q[1], z = q[2], w = q[3];
            float n = (float)Math.Sqrt(x * x + y * y + z * z + w * w);
            if (n > 1e-8f) { x /= n; y /= n; z /= n; w /= n; }
            return new float[]
            {
                1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w), 0,
                2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w), 0,
                2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y), 0,
                t[0], t[1], t[2], 1,
            };
        }

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

        /// Inverse of rotation + translation (the bones have no scale).
        static float[] InverseRigid(float[] m)
        {
            var r = new float[16];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++) r[j * 4 + i] = m[i * 4 + j];
            for (int i = 0; i < 3; i++) r[12 + i] = -(r[i] * m[12] + r[4 + i] * m[13] + r[8 + i] * m[14]);
            r[15] = 1f;
            return r;
        }

        // ------------------------------------------------------------------
        // Materials: .vmt → $basetexture → .vtf

        class Materials
        {
            readonly List<string> textures, cdPaths;
            readonly MModel model;
            readonly Dictionary<int, int> done = new Dictionary<int, int>();
            readonly Dictionary<string, string> files = new Dictionary<string, string>();   // lower-case path under materials/ → real path

            public Materials(string mdlPath, List<string> textures, List<string> cdPaths, MModel model)
            {
                this.textures = textures;
                this.cdPaths = cdPaths;
                this.model = model;
                foreach (var root in Roots(mdlPath)) Index(root);
            }

            /// «materials» folders above the model (addon / SFM layout), and
            /// the folder the model came from (saved next to it on import).
            static IEnumerable<string> Roots(string mdlPath)
            {
                var seen = new HashSet<string>();
                var starts = new List<string> { Path.GetDirectoryName(mdlPath) };
                try
                {
                    string hint = Path.Combine(Path.GetDirectoryName(mdlPath), Path.GetFileNameWithoutExtension(mdlPath) + ".srcroot");
                    if (File.Exists(hint)) starts.Add(File.ReadAllText(hint).Trim());
                }
                catch (Exception) { }
                foreach (var s0 in starts)
                {
                    string d = s0;
                    for (int up = 0; up < 8 && !string.IsNullOrEmpty(d); up++)
                    {
                        string m = null;
                        try
                        {
                            foreach (var sub in Directory.GetDirectories(d))
                                if (string.Equals(Path.GetFileName(sub), "materials", StringComparison.OrdinalIgnoreCase)) { m = sub; break; }
                        }
                        catch (Exception) { }
                        if (m != null && seen.Add(m)) yield return m;
                        d = Path.GetDirectoryName(d);
                    }
                }
                // Loose files next to the model count too.
                string here = Path.GetDirectoryName(mdlPath);
                if (seen.Add(here)) yield return here;
            }

            void Index(string root)
            {
                try
                {
                    int n = 0;
                    foreach (var f in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
                    {
                        string e = Path.GetExtension(f).ToLowerInvariant();
                        if (e != ".vmt" && e != ".vtf") continue;
                        string rel = f.Substring(root.Length).Replace('\\', '/').TrimStart('/').ToLowerInvariant();
                        if (!files.ContainsKey(rel)) files[rel] = f;
                        // Also by bare file name (loose files).
                        string bare = Path.GetFileName(rel);
                        if (!files.ContainsKey("@" + bare)) files["@" + bare] = f;
                        if (++n > 20000) break;
                    }
                }
                catch (Exception) { }
            }

            string Find(string rel)
            {
                rel = rel.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
                string p;
                if (files.TryGetValue(rel, out p)) return p;
                if (files.TryGetValue("@" + Path.GetFileName(rel), out p)) return p;
                return null;
            }

            public int Get(int tex)
            {
                int mi;
                if (done.TryGetValue(tex, out mi)) return mi;
                string name = tex >= 0 && tex < textures.Count ? textures[tex] : "material";
                var mat = new MMaterial { name = Path.GetFileName(name), color = new[] { 1f, 1f, 1f, 1f }, roughness = 0.75f };
                string vmt = null;
                foreach (var cd in cdPaths)
                {
                    vmt = Find(cd.TrimEnd('/') + "/" + name + ".vmt");
                    if (vmt != null) break;
                }
                if (vmt == null) vmt = Find(name + ".vmt");
                string baseTex = null, bumpTex = null;
                if (vmt != null)
                {
                    var kv = ReadVmt(vmt);
                    string v;
                    if (kv.TryGetValue("$basetexture", out v)) baseTex = v;
                    // Relief: $bumpmap / $normalmap (not «self-shadowed» ssbump,
                    // which is a different kind of picture).
                    bool ssbump = kv.TryGetValue("$ssbump", out v) && v != "0";
                    if (!ssbump && (kv.TryGetValue("$bumpmap", out v) || kv.TryGetValue("$normalmap", out v))) bumpTex = v;
                    if (kv.ContainsKey("$alphatest") && kv["$alphatest"] != "0") mat.alphaMask = true;
                    if (kv.ContainsKey("$translucent") && kv["$translucent"] != "0") mat.alphaMask = true;
                    if (kv.ContainsKey("$nocull") && kv["$nocull"] != "0") mat.doubleSided = true;
                    if (kv.ContainsKey("$color2")) { var c = Vec(kv["$color2"]); if (c != null) mat.color = new[] { c[0], c[1], c[2], 1f }; }
                }
                string vtf = baseTex != null ? Find(baseTex + ".vtf") : null;
                if (vtf == null)
                    foreach (var cd in cdPaths) { vtf = Find(cd.TrimEnd('/') + "/" + name + ".vtf"); if (vtf != null) break; }
                if (vtf != null)
                {
                    try
                    {
                        var png = Vtf.ToPng(File.ReadAllBytes(vtf));
                        if (png != null)
                        {
                            model.textures.Add(new MTexture { data = png, mime = "image/png" });
                            mat.texture = model.textures.Count - 1;
                        }
                    }
                    catch (Exception e) { UnityEngine.Debug.LogWarning("ATLAS: textura de Source: " + e.Message); }
                }
                string bvtf = bumpTex != null ? Find(bumpTex + ".vtf") : null;
                if (bvtf != null && mat.texture >= 0)
                {
                    try
                    {
                        int bw, bh;
                        var rgba = Vtf.ToRgba(File.ReadAllBytes(bvtf), out bw, out bh);
                        if (rgba != null)
                        {
                            // Source draws relief «DirectX style» (green down)
                            // and keeps a shine mask in the alpha: glTF wants
                            // green up and nothing in the alpha.
                            for (int i = 0; i < bw * bh; i++) { rgba[i * 4 + 1] = (byte)(255 - rgba[i * 4 + 1]); rgba[i * 4 + 3] = 255; }
                            model.textures.Add(new MTexture { data = ImageConv.Png(rgba, bw, bh), mime = "image/png" });
                            mat.normalTexture = model.textures.Count - 1;
                        }
                    }
                    catch (Exception e) { UnityEngine.Debug.LogWarning("ATLAS: relieve de Source: " + e.Message); }
                }
                model.materials.Add(mat);
                mi = model.materials.Count - 1;
                done[tex] = mi;
                return mi;
            }

            static float[] Vec(string s)
            {
                var parts = s.Trim('[', ']', '{', '}', ' ').Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) return null;
                var r = new float[3];
                for (int i = 0; i < 3; i++)
                    if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r[i])) return null;
                return r;
            }

            /// "key" "value" pairs of a .vmt (lower-case keys).
            static Dictionary<string, string> ReadVmt(string path)
            {
                var d = new Dictionary<string, string>();
                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw;
                    int cm = line.IndexOf("//");
                    if (cm >= 0) line = line.Substring(0, cm);
                    var tok = new List<string>();
                    int i = 0;
                    while (i < line.Length)
                    {
                        char c = line[i];
                        if (char.IsWhiteSpace(c) || c == '{' || c == '}') { i++; continue; }
                        if (c == '"')
                        {
                            int e = line.IndexOf('"', i + 1);
                            if (e < 0) e = line.Length;
                            tok.Add(line.Substring(i + 1, e - i - 1));
                            i = e + 1;
                        }
                        else
                        {
                            int s = i;
                            while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != '"') i++;
                            tok.Add(line.Substring(s, i - s));
                        }
                    }
                    if (tok.Count >= 2 && tok[0].StartsWith("$")) d[tok[0].ToLowerInvariant()] = tok[1];
                }
                return d;
            }
        }

        // ------------------------------------------------------------------
        // Garry's Mod addons (.gma): unpacked into `dest`.

        public static bool IsGma(byte[] head) { return head != null && head.Length >= 4 && head[0] == 'G' && head[1] == 'M' && head[2] == 'A' && head[3] == 'D'; }

        public static void ExtractGma(string gma, string dest)
        {
            var b = File.ReadAllBytes(gma);
            if (!IsGma(b)) throw new Exception("no es un addon de Garry's Mod (.gma)");
            int p = 4;
            int ver = b[p++];
            p += 8 + 8;   // steam id, time stamp
            if (ver > 1)
            {
                while (p < b.Length && b[p] != 0) { while (p < b.Length && b[p] != 0) p++; p++; }
                p++;   // end of the required-content list
            }
            for (int s = 0; s < 3; s++) { while (p < b.Length && b[p] != 0) p++; p++; }   // name, description, author
            p += 4;   // addon version
            var names = new List<string>();
            var sizes = new List<long>();
            while (p + 4 <= b.Length)
            {
                int num = I32(b, p); p += 4;
                if (num == 0) break;
                int e = p;
                while (e < b.Length && b[e] != 0) e++;
                names.Add(Encoding.UTF8.GetString(b, p, e - p));
                p = e + 1;
                sizes.Add(BitConverter.ToInt64(b, p)); p += 8;
                p += 4;   // crc
            }
            for (int i = 0; i < names.Count; i++)
            {
                long size = sizes[i];
                if (size < 0 || p + size > b.Length) break;
                string rel = names[i].Replace('\\', '/');
                if (!rel.Contains("..") && !rel.StartsWith("/"))
                {
                    string outPath = Path.Combine(dest, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    using (var f = File.Create(outPath)) f.Write(b, p, (int)size);
                }
                p += (int)size;
            }
        }

        /// The main model of an unpacked addon or zip: the biggest .mdl
        /// (characters are bigger than their arms, gibs or props).
        public static string MainMdl(string dir)
        {
            string best = null;
            long size = -1;
            foreach (var f in Directory.GetFiles(dir, "*.mdl", SearchOption.AllDirectories))
            {
                string n = Path.GetFileName(f).ToLowerInvariant();
                long s = new FileInfo(f).Length;
                if (n.Contains("_arms") || n.StartsWith("c_") || n.Contains("gib")) s /= 4;
                if (s > size) { size = s; best = f; }
            }
            return best;
        }
    }

    // ----------------------------------------------------------------------
    // Valve Texture Format → PNG (largest mip, first frame).

    public static class Vtf
    {
        public static byte[] ToPng(byte[] b)
        {
            int w, h;
            var rgba = ToRgba(b, out w, out h);
            return rgba != null ? ImageConv.Png(rgba, w, h) : null;
        }

        /// The largest picture as RGBA bytes (null if it cannot be read).
        public static byte[] ToRgba(byte[] b, out int w, out int h)
        {
            w = h = 0;
            if (b.Length < 64 || b[0] != 'V' || b[1] != 'T' || b[2] != 'F') return null;
            int major = BitConverter.ToInt32(b, 4), minor = BitConverter.ToInt32(b, 8);
            int headerSize = BitConverter.ToInt32(b, 12);
            w = BitConverter.ToUInt16(b, 16); h = BitConverter.ToUInt16(b, 18);
            uint flags = BitConverter.ToUInt32(b, 20);
            int frames = Math.Max(1, (int)BitConverter.ToUInt16(b, 24));
            int fmt = BitConverter.ToInt32(b, 52);
            int mips = Math.Max(1, (int)b[56]);
            int lowFmt = BitConverter.ToInt32(b, 57);
            int lowW = b[61], lowH = b[62];
            int depth = major == 7 && minor >= 2 ? Math.Max(1, (int)BitConverter.ToUInt16(b, 63)) : 1;
            int faces = (flags & 0x4000) != 0 ? 6 : 1;
            if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return null;
            int dataOff = -1;
            if (major == 7 && minor >= 3 && b.Length >= 80)
            {
                int numRes = BitConverter.ToInt32(b, 68);
                for (int i = 0; i < numRes && 80 + i * 8 + 8 <= b.Length; i++)
                {
                    int o = 80 + i * 8;
                    if (b[o] == 0x30 && b[o + 1] == 0 && b[o + 2] == 0) dataOff = BitConverter.ToInt32(b, o + 4);
                }
            }
            if (dataOff < 0) dataOff = headerSize + (lowFmt >= 0 && lowW > 0 && lowH > 0 ? Size(lowFmt, lowW, lowH) : 0);
            // Mips go from the smallest to the largest: skip the small ones.
            long off = dataOff;
            for (int m = mips - 1; m >= 1; m--)
            {
                int mw = Math.Max(1, w >> m), mh = Math.Max(1, h >> m);
                off += (long)Size(fmt, mw, mh) * frames * faces * Math.Max(1, depth >> m);
            }
            int size = Size(fmt, w, h);
            if (size <= 0 || off + size > b.Length) return null;
            return Decode(b, (int)off, fmt, w, h);
        }

        static int Size(int fmt, int w, int h)
        {
            int bw = (w + 3) / 4, bh = (h + 3) / 4;
            switch (fmt)
            {
                case 13: case 20: return bw * bh * 8;           // DXT1
                case 14: case 15: return bw * bh * 16;          // DXT3, DXT5
                case 0: case 1: case 11: case 12: case 16: return w * h * 4;   // RGBA8888, ABGR8888, ARGB8888, BGRA8888, BGRX8888
                case 2: case 3: return w * h * 3;               // RGB888, BGR888
                case 4: case 17: case 21: case 6: return w * h * 2;   // RGB565, BGR565, BGRA5551, IA88
                case 5: case 8: return w * h;                    // I8, A8
                case 24: return w * h * 8;                        // RGBA16161616F
            }
            return -1;
        }

        static byte[] Decode(byte[] b, int o, int fmt, int w, int h)
        {
            var px = new byte[w * h * 4];
            switch (fmt)
            {
                case 13: case 14: case 15: Dxt(b, o, fmt, w, h, px); return px;
                case 20: Dxt(b, o, 13, w, h, px); return px;
                case 0: for (int i = 0; i < w * h; i++) { px[i * 4] = b[o + i * 4]; px[i * 4 + 1] = b[o + i * 4 + 1]; px[i * 4 + 2] = b[o + i * 4 + 2]; px[i * 4 + 3] = b[o + i * 4 + 3]; } return px;
                case 1: for (int i = 0; i < w * h; i++) { px[i * 4] = b[o + i * 4 + 3]; px[i * 4 + 1] = b[o + i * 4 + 2]; px[i * 4 + 2] = b[o + i * 4 + 1]; px[i * 4 + 3] = b[o + i * 4]; } return px;
                case 11: for (int i = 0; i < w * h; i++) { px[i * 4] = b[o + i * 4 + 1]; px[i * 4 + 1] = b[o + i * 4 + 2]; px[i * 4 + 2] = b[o + i * 4 + 3]; px[i * 4 + 3] = b[o + i * 4]; } return px;
                case 12: for (int i = 0; i < w * h; i++) { px[i * 4] = b[o + i * 4 + 2]; px[i * 4 + 1] = b[o + i * 4 + 1]; px[i * 4 + 2] = b[o + i * 4]; px[i * 4 + 3] = b[o + i * 4 + 3]; } return px;
                case 16: for (int i = 0; i < w * h; i++) { px[i * 4] = b[o + i * 4 + 2]; px[i * 4 + 1] = b[o + i * 4 + 1]; px[i * 4 + 2] = b[o + i * 4]; px[i * 4 + 3] = 255; } return px;
                case 2: for (int i = 0; i < w * h; i++) { px[i * 4] = b[o + i * 3]; px[i * 4 + 1] = b[o + i * 3 + 1]; px[i * 4 + 2] = b[o + i * 3 + 2]; px[i * 4 + 3] = 255; } return px;
                case 3: for (int i = 0; i < w * h; i++) { px[i * 4] = b[o + i * 3 + 2]; px[i * 4 + 1] = b[o + i * 3 + 1]; px[i * 4 + 2] = b[o + i * 3]; px[i * 4 + 3] = 255; } return px;
                case 5: for (int i = 0; i < w * h; i++) { byte v = b[o + i]; px[i * 4] = v; px[i * 4 + 1] = v; px[i * 4 + 2] = v; px[i * 4 + 3] = 255; } return px;
                case 8: for (int i = 0; i < w * h; i++) { px[i * 4] = 255; px[i * 4 + 1] = 255; px[i * 4 + 2] = 255; px[i * 4 + 3] = b[o + i]; } return px;
                case 6: for (int i = 0; i < w * h; i++) { byte v = b[o + i * 2]; px[i * 4] = v; px[i * 4 + 1] = v; px[i * 4 + 2] = v; px[i * 4 + 3] = b[o + i * 2 + 1]; } return px;
                case 4: case 17:
                    for (int i = 0; i < w * h; i++)
                    {
                        int v = b[o + i * 2] | (b[o + i * 2 + 1] << 8);
                        byte r5 = (byte)(((v >> 11) & 31) * 255 / 31), g6 = (byte)(((v >> 5) & 63) * 255 / 63), b5 = (byte)((v & 31) * 255 / 31);
                        bool rgb = fmt == 4;
                        px[i * 4] = rgb ? r5 : b5; px[i * 4 + 1] = g6; px[i * 4 + 2] = rgb ? b5 : r5; px[i * 4 + 3] = 255;
                    }
                    return px;
            }
            return null;
        }

        static void Color565(int c, byte[] o, int i)
        {
            o[i] = (byte)(((c >> 11) & 31) * 255 / 31);
            o[i + 1] = (byte)(((c >> 5) & 63) * 255 / 63);
            o[i + 2] = (byte)((c & 31) * 255 / 31);
            o[i + 3] = 255;
        }

        static void Dxt(byte[] b, int o, int fmt, int w, int h, byte[] px)
        {
            int bw = (w + 3) / 4, bh = (h + 3) / 4;
            int blockSize = fmt == 13 ? 8 : 16;
            var pal = new byte[16];
            var alpha = new byte[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int p = o + (by * bw + bx) * blockSize;
                    int cp = fmt == 13 ? p : p + 8;
                    // Alpha
                    for (int k = 0; k < 16; k++) alpha[k] = 255;
                    if (fmt == 14)
                        for (int k = 0; k < 16; k++) { int v = (b[p + k / 2] >> ((k & 1) * 4)) & 15; alpha[k] = (byte)(v * 17); }
                    else if (fmt == 15)
                    {
                        int a0 = b[p], a1 = b[p + 1];
                        ulong bits = 0;
                        for (int k = 0; k < 6; k++) bits |= (ulong)b[p + 2 + k] << (8 * k);
                        var av = new int[8];
                        av[0] = a0; av[1] = a1;
                        if (a0 > a1) for (int k = 1; k < 7; k++) av[k + 1] = ((7 - k) * a0 + k * a1) / 7;
                        else { for (int k = 1; k < 5; k++) av[k + 1] = ((5 - k) * a0 + k * a1) / 5; av[6] = 0; av[7] = 255; }
                        for (int k = 0; k < 16; k++) alpha[k] = (byte)av[(int)((bits >> (3 * k)) & 7)];
                    }
                    // Colour
                    int c0 = b[cp] | (b[cp + 1] << 8), c1 = b[cp + 2] | (b[cp + 3] << 8);
                    Color565(c0, pal, 0);
                    Color565(c1, pal, 4);
                    if (c0 > c1 || fmt != 13)
                    {
                        for (int k = 0; k < 3; k++) { pal[8 + k] = (byte)((2 * pal[k] + pal[4 + k]) / 3); pal[12 + k] = (byte)((pal[k] + 2 * pal[4 + k]) / 3); }
                        pal[11] = 255; pal[15] = 255;
                    }
                    else
                    {
                        for (int k = 0; k < 3; k++) { pal[8 + k] = (byte)((pal[k] + pal[4 + k]) / 2); pal[12 + k] = 0; }
                        pal[11] = 255; pal[15] = 0;   // transparent black
                    }
                    uint idx = (uint)(b[cp + 4] | (b[cp + 5] << 8) | (b[cp + 6] << 16) | (b[cp + 7] << 24));
                    for (int k = 0; k < 16; k++)
                    {
                        int x = bx * 4 + (k & 3), y = by * 4 + (k >> 2);
                        if (x >= w || y >= h) continue;
                        int ci = (int)((idx >> (2 * k)) & 3) * 4;
                        int d = (y * w + x) * 4;
                        px[d] = pal[ci]; px[d + 1] = pal[ci + 1]; px[d + 2] = pal[ci + 2];
                        px[d + 3] = fmt == 13 ? pal[ci + 3] : alpha[k];
                    }
                }
        }
    }
}
