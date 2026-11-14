// ATLAS 3D (Unity) - FBX (binary and ASCII, rigs and animations) and
// OBJ + MTL through the native ufbx reader (Assets/Plugins/Atlas), turned
// into a model and then a GLB. Textures are taken from inside the FBX or
// looked for next to the file (also in its subfolders, by name).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Atlas.Import
{
    public static class FbxImport
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string Lib = "__Internal";
#else
        const string Lib = "atlas_ufbx";
#endif

        [DllImport(Lib)] static extern IntPtr atlas_fbx_load(byte[] pathUtf8, out int size);
        [DllImport(Lib)] static extern void atlas_fbx_free(IntPtr p);
        [DllImport(Lib)] static extern IntPtr atlas_fbx_error();

        /// Reads `path` into a model. Throws with a readable message.
        public static MModel Load(string path)
        {
            int size;
            IntPtr p;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(path + "\0");
                p = atlas_fbx_load(bytes, out size);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                throw new Exception("el lector de FBX no está disponible en este dispositivo");
            }
            if (p == IntPtr.Zero || size <= 0)
            {
                string err = Marshal.PtrToStringAnsi(atlas_fbx_error()) ?? "";
                throw new Exception("no se pudo leer el archivo" + (err != "" ? " (" + err.Split('\n')[0] + ")" : ""));
            }
            var data = new byte[size];
            Marshal.Copy(p, data, 0, size);
            atlas_fbx_free(p);
            return Parse(data, Path.GetDirectoryName(path));
        }

        class Reader
        {
            readonly byte[] d;
            public int at;
            public Reader(byte[] data) { d = data; }
            public int I() { int v = BitConverter.ToInt32(d, at); at += 4; return v; }
            public float F() { float v = BitConverter.ToSingle(d, at); at += 4; return v; }
            public string S() { int n = I(); string s = Encoding.UTF8.GetString(d, at, n); at += n; return s; }
            public byte[] B(int n) { var b = new byte[n]; Buffer.BlockCopy(d, at, b, 0, n); at += n; return b; }
            public float[] Fs(int n) { var f = new float[n]; Buffer.BlockCopy(d, at, f, 0, n * 4); at += n * 4; return f; }
            public uint[] Us(int n) { var u = new uint[n]; Buffer.BlockCopy(d, at, u, 0, n * 4); at += n * 4; return u; }
            public ushort[] Hs(int n) { var u = new ushort[n]; Buffer.BlockCopy(d, at, u, 0, n * 2); at += n * 2; return u; }
        }

        static MModel Parse(byte[] data, string dir)
        {
            var r = new Reader(data);
            if (Encoding.ASCII.GetString(data, 0, 4) != "AFBX") throw new Exception("respuesta del lector no válida");
            r.at = 4;
            r.I();   // version
            var m = new MModel();
            int nn = r.I();
            for (int i = 0; i < nn; i++)
            {
                var n = new MNode { name = r.S(), parent = r.I(), t = r.Fs(3), r = r.Fs(4), s = r.Fs(3), mesh = r.I() };
                r.I();   // is bone
                m.nodes.Add(n);
            }
            int nm = r.I();
            for (int i = 0; i < nm; i++)
            {
                var mesh = new MMesh { name = r.S() };
                int nv = r.I();
                bool hasUv = r.I() != 0, skinned = r.I() != 0;
                mesh.pos = r.Fs(nv * 3);
                mesh.nor = r.Fs(nv * 3);
                if (hasUv) mesh.uv = r.Fs(nv * 2);
                if (skinned)
                {
                    mesh.joints = r.Hs(nv * 4);
                    mesh.weights = r.Fs(nv * 4);
                }
                int np = r.I();
                for (int k = 0; k < np; k++)
                {
                    int mat = r.I();
                    int c = r.I();
                    mesh.parts.Add(new MPart { material = mat, indices = r.Us(c) });
                }
                if (skinned)
                {
                    int nj = r.I();
                    mesh.skinJoints = new int[nj];
                    mesh.skinIbm = new float[nj * 16];
                    for (int j = 0; j < nj; j++)
                    {
                        mesh.skinJoints[j] = r.I();
                        Array.Copy(r.Fs(16), 0, mesh.skinIbm, j * 16, 16);
                    }
                    if (nj == 0) { mesh.joints = null; mesh.weights = null; mesh.skinJoints = null; }
                }
                m.meshes.Add(mesh);
            }
            int nt = r.I();
            for (int i = 0; i < nt; i++)
            {
                string file = r.S(), rel = r.S();
                int len = r.I();
                byte[] content = len > 0 ? r.B(len) : null;
                var tex = new MTexture();
                byte[] img = content != null ? ImageConv.ToWebImage(content, file) : null;
                if (img == null) img = FindImage(file, rel, dir);
                if (img != null) { tex.data = img; tex.mime = MModel.ImageMime(img); }
                m.textures.Add(tex);
            }
            int nmat = r.I();
            for (int i = 0; i < nmat; i++)
            {
                var mat = new MMaterial { name = r.S(), color = r.Fs(4), metallic = r.F(), roughness = r.F(), emission = r.Fs(3), texture = r.I(), normalTexture = r.I() };
                // A texture shows its own colours: no dark tint on top of it.
                if (mat.texture >= 0 && mat.texture < m.textures.Count && m.textures[mat.texture].data != null)
                {
                    float mx = Math.Max(mat.color[0], Math.Max(mat.color[1], mat.color[2]));
                    if (mx < 0.05f) { mat.color[0] = mat.color[1] = mat.color[2] = 1f; }
                }
                m.materials.Add(mat);
            }
            int na = r.I();
            for (int i = 0; i < na; i++)
            {
                var a = new MAnimation { name = r.S(), frames = r.I(), fps = r.F() };
                a.trs = r.Fs(a.frames * nn * 10);
                m.animations.Add(a);
            }
            return m;
        }

        /// The image a texture names: the path as written, next to the
        /// model, or anywhere below the model's folder with the same name.
        public static byte[] FindImage(string file, string rel, string dir)
        {
            var tries = new List<string>();
            if (!string.IsNullOrEmpty(file)) tries.Add(file);
            if (!string.IsNullOrEmpty(rel) && dir != null) tries.Add(Path.Combine(dir, rel));
            string name = BaseName(!string.IsNullOrEmpty(file) ? file : rel);
            if (name != "" && dir != null) tries.Add(Path.Combine(dir, name));
            foreach (var t in tries)
            {
                try
                {
                    if (File.Exists(t))
                    {
                        var b = ImageConv.ToWebImage(File.ReadAllBytes(t), t);
                        if (b != null) return b;
                    }
                }
                catch (Exception) { }
            }
            if (name == "" || dir == null || !Directory.Exists(dir)) return null;
            // By name, ignoring case and the extension (a .tga often has a .png twin).
            string stem = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    string fn = Path.GetFileName(f).ToLowerInvariant();
                    if (fn != name.ToLowerInvariant() && Path.GetFileNameWithoutExtension(fn) != stem) continue;
                    var b = ImageConv.ToWebImage(File.ReadAllBytes(f), f);
                    if (b != null) return b;
                }
            }
            catch (Exception) { }
            return null;
        }

        static string BaseName(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            int i = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
            return i >= 0 ? p.Substring(i + 1) : p;
        }
    }
}
