// ATLAS 3D (Unity) - turns other 3D formats into a self-contained GLB at
// import time (like the Godot version), so the project keeps one model
// format with bones, materials and textures inside:
//   FBX  (binary / ASCII, rigs and animations)  - ufbx
//   OBJ  (+ MTL and its textures)               - ufbx (simple reader if it fails)
//   3DS  (3D Studio)                            - ThreeDsImport
//   BLEND (Blender 2.8 - 4.x, base meshes)      - BlendImport
using System;
using System.IO;

namespace Atlas.Import
{
    public static class ModelConverter
    {
        public static readonly string[] Convertible = { ".fbx", ".obj", ".3ds", ".blend" };

        /// Converts `path` (inside the project) to a .glb next to it and
        /// returns the GLB's path, or null to keep the file as it is.
        public static string ToGlb(string path, string ext)
        {
            MModel m;
            switch (ext)
            {
                case ".fbx":
                    m = FbxImport.Load(path);
                    break;
                case ".obj":
                    try { m = FbxImport.Load(path); }
                    catch (Exception) { return null; }   // the simple OBJ reader takes it
                    break;
                case ".3ds":
                    m = ThreeDsImport.Load(path);
                    break;
                case ".blend":
                    m = BlendImport.Load(path);
                    break;
                default:
                    return null;
            }
            string glb = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ".glb");
            int i = 2;
            while (File.Exists(glb)) glb = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_" + i++ + ".glb");
            m.WriteGlb(glb);
            return glb;
        }

        /// Formats that are recognised but cannot be read on a phone, with
        /// what to do instead; "" when the file is not one of them.
        public static string Unsupported(string ext)
        {
            switch (ext)
            {
                case ".usd":
                case ".usda":
                case ".usdc":
                case ".usdz": return "USD / USDZ todavía no se puede abrir: exporta el modelo como GLB o FBX (Blender, Reality Converter o Sketchfab lo hacen).";
                case ".abc": return "Alembic (.abc) guarda cachés de animación de programas de escritorio; no se puede abrir en el móvil. Exporta la animación como FBX o GLB.";
                case ".vdb": return "VDB son volúmenes (humo, nubes) para renderizadores de escritorio; no es un modelo con caras. ATLAS no puede abrirlo.";
                case ".max": return "Los .max son archivos internos de 3ds Max (formato cerrado). En 3ds Max usa Exportar → FBX.";
                case ".c4d": return "Los .c4d son archivos internos de Cinema 4D (formato cerrado). En Cinema 4D usa Exportar → FBX o glTF.";
                case ".ma":
                case ".mb": return "Los archivos de Maya (.ma / .mb) son internos. En Maya usa Exportar → FBX.";
                case ".skp": return "Los .skp de SketchUp son internos: expórtalo como FBX, OBJ o glTF.";
            }
            return "";
        }

        /// Recognises FBX and 3DS by their first bytes (files without the
        /// right extension, as some phones save them).
        public static string Sniff(byte[] head, int n)
        {
            if (n >= 18 && System.Text.Encoding.ASCII.GetString(head, 0, 18) == "Kaydara FBX Binary") return ".fbx";
            string text = System.Text.Encoding.ASCII.GetString(head, 0, Math.Min(n, 512));
            if (text.Contains("FBXHeaderExtension") || text.StartsWith("; FBX")) return ".fbx";
            if (n >= 10 && ThreeDsImport.Is3ds(head)) return ".3ds";
            if (BlendImport.IsBlend(head)) return ".blend";
            return "";
        }
    }
}
