// ATLAS 3D (Unity) - «Exportar»: the scene's objects to six formats.
//   GLB  (one file, textures inside: the most compatible)
//   glTF (one text file with the data inside)
//   OBJ  (+ MTL + textures, in a .zip)
//   STL  (3D printing, no colour)
//   PLY  (vertex colours)
//   3MF  (3D printing with colours)
// The file is made in the app's folder and then the phone asks where to
// save it (Downloads, Drive…).
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Atlas
{
    public static class Exporter
    {
        public static readonly string[] Formats = { "GLB", "glTF", "OBJ", "STL", "PLY", "3MF" };

        public static string ExportDir
        {
            get
            {
                string d = Path.Combine(Application.persistentDataPath, "Exportados");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        /// Writes the file. Returns its path, or throws with a readable message.
        public static async Task<string> Export(string format, List<AtlasItem> items, string name)
        {
            var objs = new List<AtlasItem>();
            foreach (var it in items) if (it != null && !it.IsLight) objs.Add(it);
            if (objs.Count == 0) throw new Exception("no hay objetos que exportar");
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string baseName = Safe(name) + "_" + stamp;
            string dir = ExportDir;
            int skipped;
            var parts = MeshFormats.Collect(objs, out skipped);
            if (parts.Count == 0)
                throw new Exception(skipped > 0 ? "las mallas de los modelos importados no se pueden leer" : "no hay mallas que exportar");
            await Task.Yield();
            string outPath;
            switch (format)
            {
                case "GLB":
                    outPath = Path.Combine(dir, baseName + ".glb");
                    GltfWriter.Write(parts, outPath, true);
                    return outPath;
                case "glTF":
                    outPath = Path.Combine(dir, baseName + ".gltf");
                    GltfWriter.Write(parts, outPath, false);
                    return outPath;
            }
            byte[] bytes;
            string ext;
            switch (format)
            {
                case "OBJ": bytes = MeshFormats.WriteObjZip(parts, Safe(name)); ext = "_obj.zip"; break;
                case "STL": bytes = MeshFormats.WriteStl(parts); ext = ".stl"; break;
                case "PLY": bytes = MeshFormats.WritePly(parts); ext = ".ply"; break;
                default: bytes = MeshFormats.Write3mf(parts); ext = ".3mf"; break;
            }
            outPath = Path.Combine(dir, baseName + ext);
            File.WriteAllBytes(outPath, bytes);
            return outPath;
        }

        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "atlas";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }
    }
}
