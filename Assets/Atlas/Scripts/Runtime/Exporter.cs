// ATLAS 3D (Unity) - «Exportar»: the scene's objects to six formats.
//   GLB  (one file, textures inside: the most compatible)
//   glTF (text + .bin + textures, in a .zip)
//   OBJ  (+ MTL + textures, in a .zip)
//   STL  (3D printing, no colour)
//   PLY  (vertex colours)
//   3MF  (3D printing with colours)
// The file is made in the app's folder and then the phone asks where to
// save it (Downloads, Drive…).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
            foreach (var it in items) if (it != null && it.kind != "light") objs.Add(it);
            if (objs.Count == 0) throw new Exception("no hay objetos que exportar");
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string baseName = Safe(name) + "_" + stamp;
            string dir = ExportDir;
            switch (format)
            {
                case "GLB":
                {
                    string path = Path.Combine(dir, baseName + ".glb");
                    await Gltf(objs, path, true);
                    return path;
                }
                case "glTF":
                {
                    string tmp = Path.Combine(dir, baseName + "_gltf");
                    Directory.CreateDirectory(tmp);
                    await Gltf(objs, Path.Combine(tmp, baseName + ".gltf"), false);
                    string zip = Path.Combine(dir, baseName + "_gltf.zip");
                    using (var z = new ZipArchive(File.Create(zip), ZipArchiveMode.Create))
                        foreach (var f in Directory.GetFiles(tmp))
                        {
                            var e = z.CreateEntry(Path.GetFileName(f), System.IO.Compression.CompressionLevel.Optimal);
                            using (var s = e.Open())
                            {
                                var data = File.ReadAllBytes(f);
                                s.Write(data, 0, data.Length);
                            }
                        }
                    Directory.Delete(tmp, true);
                    return zip;
                }
            }
            int skipped;
            var parts = MeshFormats.Collect(objs, out skipped);
            if (parts.Count == 0)
                throw new Exception(skipped > 0 ? "las mallas de los modelos importados no se pueden leer para este formato: usa GLB" : "no hay mallas que exportar");
            byte[] bytes;
            string ext;
            switch (format)
            {
                case "OBJ": bytes = MeshFormats.WriteObjZip(parts, Safe(name)); ext = "_obj.zip"; break;
                case "STL": bytes = MeshFormats.WriteStl(parts); ext = ".stl"; break;
                case "PLY": bytes = MeshFormats.WritePly(parts); ext = ".ply"; break;
                default: bytes = MeshFormats.Write3mf(parts); ext = ".3mf"; break;
            }
            string outPath = Path.Combine(dir, baseName + ext);
            File.WriteAllBytes(outPath, bytes);
            return outPath;
        }

        static async Task Gltf(List<AtlasItem> objs, string path, bool binary)
        {
            var settings = new GLTFast.Export.ExportSettings
            {
                Format = binary ? GLTFast.Export.GltfFormat.Binary : GLTFast.Export.GltfFormat.Json,
                FileConflictResolution = GLTFast.Export.FileConflictResolution.Overwrite,
            };
            var goSettings = new GLTFast.Export.GameObjectExportSettings { OnlyActiveInHierarchy = true };
            var export = new GLTFast.Export.GameObjectExport(settings, goSettings);
            var roots = new GameObject[objs.Count];
            for (int i = 0; i < objs.Count; i++) roots[i] = objs[i].gameObject;
            export.AddScene(roots, "ATLAS 3D");
            bool ok = await export.SaveToFileAndDispose(path);
            if (!ok) throw new Exception("glTFast no pudo escribir el archivo");
        }

        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "atlas";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }
    }
}
