// ATLAS 3D (Unity) - simple model formats read and written here (glTF /
// GLB go through glTFast): OBJ (+ MTL colours and textures), STL (binary
// and text), PLY (text and binary, vertex colours) and zip packages
// (Sketchfab downloads: scene.gltf + scene.bin + textures). Export: OBJ
// (+ MTL + textures, zipped), STL, PLY and 3MF.
// Unity is left-handed and these files right-handed: X is mirrored and the
// triangle order reversed both ways, so models keep their look and facing.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas
{
    public static class MeshFormats
    {
        public static readonly string[] Importable = { ".glb", ".gltf", ".obj", ".stl", ".ply", ".zip" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------
        // Recognising files (the phone's picker may give names without
        // extension)

        public static string Sniff(string path)
        {
            try
            {
                using (var f = File.OpenRead(path))
                {
                    var head = new byte[512];
                    int n = f.Read(head, 0, head.Length);
                    if (n >= 4 && head[0] == (byte)'g' && head[1] == (byte)'l' && head[2] == (byte)'T' && head[3] == (byte)'F') return ".glb";
                    if (n >= 4 && head[0] == 0x50 && head[1] == 0x4B && head[2] == 3 && head[3] == 4) return ".zip";
                    string text = Encoding.ASCII.GetString(head, 0, n);
                    if (text.StartsWith("ply")) return ".ply";
                    if (text.TrimStart().StartsWith("{")) return ".gltf";
                    if (text.TrimStart().StartsWith("solid") && text.Contains("facet")) return ".stl";
                    if (text.Contains("\nv ") || text.StartsWith("v ") || text.Contains("mtllib") || text.Contains("\no ")) return ".obj";
                    if (f.Length >= 84)
                    {
                        f.Seek(80, SeekOrigin.Begin);
                        var c = new byte[4];
                        f.Read(c, 0, 4);
                        uint tris = BitConverter.ToUInt32(c, 0);
                        if (84 + (long)tris * 50 == f.Length) return ".stl";
                    }
                }
            }
            catch (Exception) { }
            return ".glb";
        }

        /// Unpacks a zip into its own folder; returns the main model inside
        /// (relative to `modelsDir`).
        public static string ExtractPackage(string zipPath, string modelsDir)
        {
            string folder = "paquete_" + SceneStore.NewId().Substring(0, 8);
            string dest = Path.Combine(modelsDir, folder);
            Directory.CreateDirectory(dest);
            string best = null;
            int bestRank = 99;
            string[] order = { ".glb", ".gltf", ".obj", ".ply", ".stl" };
            using (var zip = new ZipArchive(File.OpenRead(zipPath), ZipArchiveMode.Read))
            {
                foreach (var e in zip.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;
                    string rel = e.FullName.Replace('\\', '/');
                    if (rel.Contains("..")) continue;
                    string outPath = Path.Combine(dest, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                    using (var src = e.Open())
                    using (var dst = File.Create(outPath))
                        src.CopyTo(dst);
                    int rank = Array.IndexOf(order, Path.GetExtension(rel).ToLowerInvariant());
                    if (rank >= 0 && rank < bestRank)
                    {
                        bestRank = rank;
                        best = folder + "/" + rel;
                    }
                }
            }
            if (best == null) throw new Exception("el .zip no contiene ningún modelo (GLB, glTF, OBJ, PLY o STL)");
            return best;
        }

        // ------------------------------------------------------------------
        // Import

        /// Builds a GameObject for an OBJ / STL / PLY file. Returns null and
        /// an error on failure.
        public static GameObject Load(string path, Material lit, out string error)
        {
            error = "";
            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (Array.IndexOf(Importable, ext) < 0) ext = Sniff(path);
                switch (ext)
                {
                    case ".obj": return LoadObj(path, lit);
                    case ".stl": return LoadStl(path, lit);
                    case ".ply": return LoadPly(path, lit);
                }
                error = "Formato no compatible.";
            }
            catch (Exception e)
            {
                error = "No se pudo leer el modelo: " + e.Message;
            }
            return null;
        }

        static float F(string s)
        {
            float v;
            float.TryParse(s, NumberStyles.Float, Inv, out v);
            return v;
        }

        static GameObject MakeObject(string name, Mesh mesh, Material[] mats)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        static Material Mat(Material lit, Color c, Texture2D tex = null)
        {
            var m = new Material(lit);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.3f);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            return m;
        }

        static Texture2D LoadTexture(string dir, string rel)
        {
            if (string.IsNullOrEmpty(rel)) return null;
            rel = rel.Replace('\\', '/').Trim();
            string[] tries = { Path.Combine(dir, rel), Path.Combine(dir, Path.GetFileName(rel)) };
            foreach (var p in tries)
            {
                if (!File.Exists(p)) continue;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (t.LoadImage(File.ReadAllBytes(p))) return t;
            }
            return null;
        }

        static GameObject LoadObj(string path, Material lit)
        {
            string dir = Path.GetDirectoryName(path);
            var pos = new List<Vector3>();
            var uvs = new List<Vector2>();
            var nrm = new List<Vector3>();
            var outPos = new List<Vector3>();
            var outUv = new List<Vector2>();
            var outN = new List<Vector3>();
            var subs = new List<List<int>>();
            var subMat = new List<string>();
            var mtl = new Dictionary<string, Material>();
            var corner = new Dictionary<string, int>();
            int cur = -1;
            bool hasN = false;
            foreach (var raw in File.ReadAllLines(path))
            {
                string l = raw.Trim();
                if (l.Length < 2 || l[0] == '#') continue;
                string[] p = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (p[0])
                {
                    case "v": if (p.Length >= 4) pos.Add(new Vector3(-F(p[1]), F(p[2]), F(p[3]))); break;
                    case "vt": if (p.Length >= 3) uvs.Add(new Vector2(F(p[1]), F(p[2]))); break;
                    case "vn": if (p.Length >= 4) nrm.Add(new Vector3(-F(p[1]), F(p[2]), F(p[3]))); break;
                    case "mtllib": ReadMtl(Path.Combine(dir, l.Substring(7).Trim()), dir, lit, mtl); break;
                    case "usemtl":
                        string mname = l.Substring(7).Trim();
                        cur = subMat.IndexOf(mname);
                        if (cur < 0) { subMat.Add(mname); subs.Add(new List<int>()); cur = subs.Count - 1; }
                        break;
                    case "f":
                        if (cur < 0) { subMat.Add(""); subs.Add(new List<int>()); cur = 0; }
                        var idx = new List<int>();
                        for (int i = 1; i < p.Length; i++)
                        {
                            int c;
                            if (!corner.TryGetValue(p[i], out c))
                            {
                                string[] q = p[i].Split('/');
                                int vi = Index(q[0], pos.Count);
                                if (vi < 0) continue;
                                c = outPos.Count;
                                outPos.Add(pos[vi]);
                                int ti = q.Length > 1 ? Index(q[1], uvs.Count) : -1;
                                outUv.Add(ti >= 0 ? uvs[ti] : Vector2.zero);
                                int ni = q.Length > 2 ? Index(q[2], nrm.Count) : -1;
                                outN.Add(ni >= 0 ? nrm[ni] : Vector3.zero);
                                if (ni >= 0) hasN = true;
                                corner[p[i]] = c;
                            }
                            idx.Add(c);
                        }
                        // Mirrored on X: the winding flips, so each triangle is reversed.
                        for (int k = 1; k + 1 < idx.Count; k++)
                        {
                            subs[cur].Add(idx[0]);
                            subs[cur].Add(idx[k + 1]);
                            subs[cur].Add(idx[k]);
                        }
                        break;
                }
            }
            if (outPos.Count == 0) throw new Exception("el OBJ no tiene caras");
            var mesh = new Mesh();
            if (outPos.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(outPos);
            mesh.SetUVs(0, outUv);
            mesh.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
            if (hasN) mesh.SetNormals(outN); else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var mats = new Material[subs.Count];
            for (int s = 0; s < subs.Count; s++)
            {
                Material m;
                mats[s] = mtl.TryGetValue(subMat[s], out m) ? m : Mat(lit, new Color(0.8f, 0.8f, 0.82f));
            }
            return MakeObject(Path.GetFileNameWithoutExtension(path), mesh, mats);
        }

        static int Index(string s, int count)
        {
            int i;
            if (!int.TryParse(s, NumberStyles.Integer, Inv, out i)) return -1;
            i = i > 0 ? i - 1 : count + i;
            return i >= 0 && i < count ? i : -1;
        }

        static void ReadMtl(string path, string dir, Material lit, Dictionary<string, Material> into)
        {
            if (!File.Exists(path))
            {
                // Renamed downloads: any .mtl next to it.
                var found = Directory.GetFiles(dir, "*.mtl");
                if (found.Length == 0) return;
                path = found[0];
            }
            Material cur = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                string l = raw.Trim();
                string[] p = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                if (p[0] == "newmtl" && p.Length > 1)
                {
                    cur = Mat(lit, new Color(0.8f, 0.8f, 0.8f));
                    into[l.Substring(7).Trim()] = cur;
                }
                else if (cur == null) continue;
                else if (p[0] == "Kd" && p.Length >= 4)
                {
                    Color c = cur.GetColor("_BaseColor");
                    cur.SetColor("_BaseColor", new Color(F(p[1]), F(p[2]), F(p[3]), c.a));
                }
                else if (p[0] == "map_Kd" && p.Length >= 2)
                {
                    var t = LoadTexture(dir, p[p.Length - 1]);
                    if (t != null)
                    {
                        cur.SetTexture("_BaseMap", t);
                        Color c = cur.GetColor("_BaseColor");
                        cur.SetColor("_BaseColor", new Color(1f, 1f, 1f, c.a));
                    }
                }
            }
        }

        static GameObject LoadStl(string path, Material lit)
        {
            byte[] data = File.ReadAllBytes(path);
            var verts = new List<Vector3>();
            bool binary = data.Length >= 84 && 84 + (long)BitConverter.ToUInt32(data, 80) * 50 == data.Length;
            if (binary)
            {
                uint n = BitConverter.ToUInt32(data, 80);
                for (uint t = 0; t < n; t++)
                {
                    int o = 84 + (int)t * 50 + 12;
                    for (int k = 0; k < 3; k++)
                    {
                        int q = o + k * 12;
                        verts.Add(new Vector3(-BitConverter.ToSingle(data, q), BitConverter.ToSingle(data, q + 8), BitConverter.ToSingle(data, q + 4)));
                    }
                }
            }
            else
            {
                foreach (var raw in Encoding.ASCII.GetString(data).Split('\n'))
                {
                    string[] p = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 4 && p[0] == "vertex")
                        verts.Add(new Vector3(-F(p[1]), F(p[3]), F(p[2])));
                }
            }
            if (verts.Count < 3) throw new Exception("el STL no tiene triángulos");
            // STL is Z-up: turned to Y-up above (and mirrored): reverse winding.
            var tris = new int[verts.Count - verts.Count % 3];
            for (int i = 0; i < tris.Length; i += 3) { tris[i] = i; tris[i + 1] = i + 1; tris[i + 2] = i + 2; }
            var mesh = new Mesh();
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return MakeObject(Path.GetFileNameWithoutExtension(path), mesh, new[] { Mat(lit, new Color(0.78f, 0.79f, 0.83f)) });
        }

        static GameObject LoadPly(string path, Material lit)
        {
            byte[] data = File.ReadAllBytes(path);
            int headerEnd = IndexOf(data, Encoding.ASCII.GetBytes("end_header"));
            if (headerEnd < 0) throw new Exception("cabecera PLY no válida");
            int bodyStart = headerEnd + 10;
            while (bodyStart < data.Length && (data[bodyStart] == '\r' || data[bodyStart] == '\n')) bodyStart++;
            string header = Encoding.ASCII.GetString(data, 0, headerEnd);
            bool ascii = header.Contains("format ascii");
            bool bigEndian = header.Contains("binary_big_endian");
            if (bigEndian) throw new Exception("PLY binario big-endian no compatible");
            int nv = 0, nf = 0;
            var vprops = new List<string[]>();   // [type, name]
            string faceCountType = "uchar", faceIndexType = "int";
            string section = "";
            foreach (var raw in header.Split('\n'))
            {
                string[] p = raw.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2) continue;
                if (p[0] == "element") { section = p[1]; if (p[1] == "vertex") nv = int.Parse(p[2], Inv); else if (p[1] == "face") nf = int.Parse(p[2], Inv); }
                else if (p[0] == "property" && section == "vertex" && p.Length >= 3) vprops.Add(new[] { p[1], p[2] });
                else if (p[0] == "property" && section == "face" && p[1] == "list" && p.Length >= 5) { faceCountType = p[2]; faceIndexType = p[3]; }
            }
            var verts = new Vector3[nv];
            var cols = new Color[nv];
            bool hasColor = false;
            var tris = new List<int>();
            if (ascii)
            {
                string[] tokens = Encoding.ASCII.GetString(data, bodyStart, data.Length - bodyStart).Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                int t = 0;
                for (int v = 0; v < nv; v++)
                {
                    float x = 0, y = 0, z = 0, r = 1, g = 1, b = 1;
                    foreach (var pr in vprops)
                    {
                        float val = F(tokens[t++]);
                        Prop(pr, val, ref x, ref y, ref z, ref r, ref g, ref b, ref hasColor);
                    }
                    verts[v] = new Vector3(-x, y, z);
                    cols[v] = new Color(r, g, b);
                }
                for (int f = 0; f < nf && t < tokens.Length; f++)
                {
                    int n = (int)F(tokens[t++]);
                    var idx = new int[n];
                    for (int k = 0; k < n; k++) idx[k] = (int)F(tokens[t++]);
                    Fan(idx, tris);
                }
            }
            else
            {
                int o = bodyStart;
                for (int v = 0; v < nv; v++)
                {
                    float x = 0, y = 0, z = 0, r = 1, g = 1, b = 1;
                    foreach (var pr in vprops)
                    {
                        float val = ReadBin(data, ref o, pr[0]);
                        Prop(pr, val, ref x, ref y, ref z, ref r, ref g, ref b, ref hasColor);
                    }
                    verts[v] = new Vector3(-x, y, z);
                    cols[v] = new Color(r, g, b);
                }
                for (int f = 0; f < nf && o < data.Length; f++)
                {
                    int n = (int)ReadBin(data, ref o, faceCountType);
                    var idx = new int[n];
                    for (int k = 0; k < n; k++) idx[k] = (int)ReadBin(data, ref o, faceIndexType);
                    Fan(idx, tris);
                }
            }
            var mesh = new Mesh();
            if (nv > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = verts;
            if (hasColor) mesh.colors = cols;
            if (tris.Count > 0) mesh.SetTriangles(tris, 0);
            else mesh.SetIndices(Range(nv), MeshTopology.Points, 0);   // point cloud
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Color avg = Color.white;
            if (hasColor && nv > 0)
            {
                Color sum = Color.clear;
                int step = Mathf.Max(1, nv / 2000), cnt = 0;
                for (int v = 0; v < nv; v += step) { sum += cols[v]; cnt++; }
                avg = sum / Mathf.Max(1, cnt);
                avg.a = 1f;
            }
            return MakeObject(Path.GetFileNameWithoutExtension(path), mesh, new[] { Mat(lit, hasColor ? avg : new Color(0.78f, 0.79f, 0.83f)) });
        }

        static int[] Range(int n)
        {
            var a = new int[n];
            for (int i = 0; i < n; i++) a[i] = i;
            return a;
        }

        static void Fan(int[] idx, List<int> tris)
        {
            for (int k = 1; k + 1 < idx.Length; k++)
            {
                tris.Add(idx[0]);
                tris.Add(idx[k + 1]);
                tris.Add(idx[k]);
            }
        }

        static void Prop(string[] pr, float val, ref float x, ref float y, ref float z, ref float r, ref float g, ref float b, ref bool hasColor)
        {
            bool byteType = pr[0] == "uchar" || pr[0] == "uint8";
            switch (pr[1])
            {
                case "x": x = val; break;
                case "y": y = val; break;
                case "z": z = val; break;
                case "red": r = byteType ? val / 255f : val; hasColor = true; break;
                case "green": g = byteType ? val / 255f : val; hasColor = true; break;
                case "blue": b = byteType ? val / 255f : val; hasColor = true; break;
            }
        }

        static float ReadBin(byte[] d, ref int o, string type)
        {
            float v;
            switch (type)
            {
                case "char": case "int8": v = (sbyte)d[o]; o += 1; break;
                case "uchar": case "uint8": v = d[o]; o += 1; break;
                case "short": case "int16": v = BitConverter.ToInt16(d, o); o += 2; break;
                case "ushort": case "uint16": v = BitConverter.ToUInt16(d, o); o += 2; break;
                case "int": case "int32": v = BitConverter.ToInt32(d, o); o += 4; break;
                case "uint": case "uint32": v = BitConverter.ToUInt32(d, o); o += 4; break;
                case "double": case "float64": v = (float)BitConverter.ToDouble(d, o); o += 8; break;
                default: v = BitConverter.ToSingle(d, o); o += 4; break;
            }
            return v;
        }

        static int IndexOf(byte[] hay, byte[] needle)
        {
            int limit = Math.Min(hay.Length - needle.Length, 1 << 16);
            for (int i = 0; i <= limit; i++)
            {
                int k = 0;
                while (k < needle.Length && hay[i + k] == needle[k]) k++;
                if (k == needle.Length) return i;
            }
            return -1;
        }

        // ------------------------------------------------------------------
        // Export (the scene's meshes in world space)

        public class Part
        {
            public string name;
            public Vector3[] verts;
            public Vector3[] normals;
            public Vector2[] uvs;
            public int[] tris;          // already in the file's (mirrored) order
            public Color color = Color.white;
            public Texture texture;
        }

        /// Every visible mesh of the given objects, baked to world space.
        /// `skipped` counts meshes that could not be read.
        public static List<Part> Collect(IEnumerable<AtlasItem> items, out int skipped)
        {
            skipped = 0;
            var parts = new List<Part>();
            foreach (var it in items)
            {
                if (it == null || it.IsLight) continue;
                foreach (var r in it.GetComponentsInChildren<Renderer>())
                {
                    Mesh mesh = null;
                    Matrix4x4 m;
                    var smr = r as SkinnedMeshRenderer;
                    if (smr != null)
                    {
                        if (smr.sharedMesh == null) continue;
                        mesh = new Mesh();
                        smr.BakeMesh(mesh, true);
                        m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    }
                    else
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        mesh = mf.sharedMesh;
                        m = r.transform.localToWorldMatrix;
                    }
                    if (!mesh.isReadable) { skipped++; continue; }
                    var v = mesh.vertices;
                    var n = mesh.normals;
                    var uv = mesh.uv;
                    var wv = new Vector3[v.Length];
                    var wn = new Vector3[v.Length];
                    for (int i = 0; i < v.Length; i++)
                    {
                        Vector3 p = m.MultiplyPoint3x4(v[i]);
                        wv[i] = new Vector3(-p.x, p.y, p.z);
                        Vector3 q = i < n.Length ? m.MultiplyVector(n[i]).normalized : Vector3.up;
                        wn[i] = new Vector3(-q.x, q.y, q.z);
                    }
                    var mats = r.sharedMaterials;
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                        var t = mesh.GetTriangles(s);
                        for (int k = 0; k + 2 < t.Length; k += 3)
                        {
                            int tmp = t[k + 1];
                            t[k + 1] = t[k + 2];
                            t[k + 2] = tmp;
                        }
                        var part = new Part { name = it.gameObject.name, verts = wv, normals = wn, uvs = uv.Length == v.Length ? uv : null, tris = t };
                        var mat = s < mats.Length ? mats[s] : null;
                        if (mat != null)
                        {
                            if (mat.HasProperty("_BaseColor")) part.color = mat.GetColor("_BaseColor");
                            else if (mat.HasProperty("baseColorFactor")) part.color = mat.GetColor("baseColorFactor");
                            if (mat.HasProperty("_BaseMap")) part.texture = mat.GetTexture("_BaseMap");
                            if (part.texture == null && mat.HasProperty("baseColorTexture")) part.texture = mat.GetTexture("baseColorTexture");
                        }
                        parts.Add(part);
                    }
                }
            }
            return parts;
        }

        public static byte[] WriteStl(List<Part> parts)
        {
            int n = 0;
            foreach (var p in parts) n += p.tris.Length / 3;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                var header = new byte[80];
                Encoding.ASCII.GetBytes("ATLAS 3D").CopyTo(header, 0);
                w.Write(header);
                w.Write((uint)n);
                foreach (var p in parts)
                    for (int k = 0; k + 2 < p.tris.Length; k += 3)
                    {
                        // Back to Z-up for STL.
                        Vector3 a = ZUp(p.verts[p.tris[k]]), b = ZUp(p.verts[p.tris[k + 1]]), c = ZUp(p.verts[p.tris[k + 2]]);
                        Vector3 nn = Vector3.Cross(b - a, c - a).normalized;
                        foreach (var v in new[] { nn, a, b, c }) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
                        w.Write((ushort)0);
                    }
                w.Flush();
                return ms.ToArray();
            }
        }

        static Vector3 ZUp(Vector3 v)
        {
            return new Vector3(v.x, -v.z, v.y);
        }

        public static byte[] WritePly(List<Part> parts)
        {
            int nv = 0, nf = 0;
            foreach (var p in parts) { nv += p.verts.Length; nf += p.tris.Length / 3; }
            using (var ms = new MemoryStream())
            {
                string header = "ply\nformat binary_little_endian 1.0\ncomment ATLAS 3D\nelement vertex " + nv +
                    "\nproperty float x\nproperty float y\nproperty float z\nproperty float nx\nproperty float ny\nproperty float nz\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nelement face " + nf +
                    "\nproperty list uchar int vertex_indices\nend_header\n";
                var hb = Encoding.ASCII.GetBytes(header);
                ms.Write(hb, 0, hb.Length);
                var w = new BinaryWriter(ms);
                foreach (var p in parts)
                {
                    Color32 c = p.color;
                    for (int i = 0; i < p.verts.Length; i++)
                    {
                        var v = p.verts[i];
                        var n = p.normals[i];
                        w.Write(v.x); w.Write(v.y); w.Write(v.z);
                        w.Write(n.x); w.Write(n.y); w.Write(n.z);
                        w.Write(c.r); w.Write(c.g); w.Write(c.b);
                    }
                }
                int baseIndex = 0;
                foreach (var p in parts)
                {
                    for (int k = 0; k + 2 < p.tris.Length; k += 3)
                    {
                        w.Write((byte)3);
                        w.Write(baseIndex + p.tris[k]);
                        w.Write(baseIndex + p.tris[k + 1]);
                        w.Write(baseIndex + p.tris[k + 2]);
                    }
                    baseIndex += p.verts.Length;
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        /// OBJ + MTL + textures in one zip.
        public static byte[] WriteObjZip(List<Part> parts, string name)
        {
            var obj = new StringBuilder();
            var mtl = new StringBuilder();
            obj.Append("# ATLAS 3D\nmtllib ").Append(name).Append(".mtl\n");
            var textures = new Dictionary<Texture, string>();
            int baseIndex = 1, mi = 0;
            foreach (var p in parts)
            {
                string mat = "material_" + (mi++);
                obj.Append("o ").Append(Clean(p.name)).Append('\n');
                foreach (var v in p.verts) obj.Append("v ").Append(v.x.ToString("0.######", Inv)).Append(' ').Append(v.y.ToString("0.######", Inv)).Append(' ').Append(v.z.ToString("0.######", Inv)).Append('\n');
                foreach (var n in p.normals) obj.Append("vn ").Append(n.x.ToString("0.####", Inv)).Append(' ').Append(n.y.ToString("0.####", Inv)).Append(' ').Append(n.z.ToString("0.####", Inv)).Append('\n');
                bool uv = p.uvs != null;
                if (uv) foreach (var t in p.uvs) obj.Append("vt ").Append(t.x.ToString("0.######", Inv)).Append(' ').Append(t.y.ToString("0.######", Inv)).Append('\n');
                obj.Append("usemtl ").Append(mat).Append('\n');
                for (int k = 0; k + 2 < p.tris.Length; k += 3)
                {
                    obj.Append('f');
                    for (int j = 0; j < 3; j++)
                    {
                        int i = baseIndex + p.tris[k + j];
                        obj.Append(' ').Append(i).Append('/');
                        if (uv) obj.Append(i);
                        obj.Append('/').Append(i);
                    }
                    obj.Append('\n');
                }
                baseIndex += p.verts.Length;
                mtl.Append("newmtl ").Append(mat).Append('\n');
                Color c = p.texture != null ? Color.white : p.color;
                mtl.Append("Kd ").Append(c.r.ToString("0.###", Inv)).Append(' ').Append(c.g.ToString("0.###", Inv)).Append(' ').Append(c.b.ToString("0.###", Inv)).Append('\n');
                if (p.texture != null)
                {
                    string tn;
                    if (!textures.TryGetValue(p.texture, out tn))
                    {
                        tn = "textura_" + textures.Count + ".png";
                        textures[p.texture] = tn;
                    }
                    mtl.Append("map_Kd ").Append(tn).Append('\n');
                }
            }
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    AddEntry(zip, name + ".obj", Encoding.UTF8.GetBytes(obj.ToString()));
                    AddEntry(zip, name + ".mtl", Encoding.UTF8.GetBytes(mtl.ToString()));
                    foreach (var kv in textures)
                    {
                        var png = TexturePng(kv.Key);
                        if (png != null) AddEntry(zip, kv.Value, png);
                    }
                }
                return ms.ToArray();
            }
        }

        static string Xml(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        static string Clean(string s)
        {
            return string.IsNullOrEmpty(s) ? "objeto" : s.Replace(' ', '_');
        }

        static void AddEntry(ZipArchive zip, string name, byte[] data)
        {
            var e = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
            using (var s = e.Open()) s.Write(data, 0, data.Length);
        }

        /// PNG of any texture (also the ones the GPU keeps unreadable).
        public static byte[] TexturePng(Texture tex)
        {
            if (tex == null) return null;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new UnityEngine.Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            byte[] png = copy.EncodeToPNG();
            UnityEngine.Object.Destroy(copy);
            return png;
        }

        /// 3D printing package (3MF): one object per part, with its colour.
        public static byte[] Write3mf(List<Part> parts)
        {
            var model = new StringBuilder();
            model.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            model.Append("<model unit=\"meter\" xml:lang=\"es-ES\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\">\n<resources>\n");
            model.Append("<basematerials id=\"1\">\n");
            foreach (var p in parts)
                model.Append("<base name=\"").Append(Xml(Clean(p.name))).Append("\" displaycolor=\"#").Append(ColorUtility.ToHtmlStringRGB(p.color)).Append("\"/>\n");
            model.Append("</basematerials>\n");
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                model.Append("<object id=\"").Append(i + 2).Append("\" type=\"model\" pid=\"1\" pindex=\"").Append(i).Append("\"><mesh><vertices>\n");
                foreach (var v0 in p.verts)
                {
                    var v = ZUp(v0);
                    model.Append("<vertex x=\"").Append(v.x.ToString("0.######", Inv)).Append("\" y=\"").Append(v.y.ToString("0.######", Inv)).Append("\" z=\"").Append(v.z.ToString("0.######", Inv)).Append("\"/>\n");
                }
                model.Append("</vertices><triangles>\n");
                for (int k = 0; k + 2 < p.tris.Length; k += 3)
                    model.Append("<triangle v1=\"").Append(p.tris[k]).Append("\" v2=\"").Append(p.tris[k + 1]).Append("\" v3=\"").Append(p.tris[k + 2]).Append("\"/>\n");
                model.Append("</triangles></mesh></object>\n");
            }
            model.Append("</resources>\n<build>\n");
            for (int i = 0; i < parts.Count; i++) model.Append("<item objectid=\"").Append(i + 2).Append("\"/>\n");
            model.Append("</build>\n</model>\n");
            string types = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\"/></Types>";
            string rels = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Target=\"/3D/3dmodel.model\" Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/></Relationships>";
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    AddEntry(zip, "[Content_Types].xml", Encoding.UTF8.GetBytes(types));
                    AddEntry(zip, "_rels/.rels", Encoding.UTF8.GetBytes(rels));
                    AddEntry(zip, "3D/3dmodel.model", Encoding.UTF8.GetBytes(model.ToString()));
                }
                return ms.ToArray();
            }
        }
    }
}
