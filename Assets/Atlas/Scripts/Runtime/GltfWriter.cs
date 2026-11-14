// ATLAS 3D (Unity) - glTF 2.0 writer (GLB binary, or .gltf with the data
// inside), written by hand from the public specification so exporting does
// not depend on any package. One node + mesh per part, PBR materials with
// colour and (embedded PNG) texture. Takes the parts from
// MeshFormats.Collect: world space, already mirrored to right-handed and
// with the triangle order for that space.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Atlas
{
    public static class GltfWriter
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        class Bin
        {
            public readonly MemoryStream data = new MemoryStream();
            public readonly List<string> views = new List<string>();
            public readonly List<string> accessors = new List<string>();

            public int View(byte[] bytes, int target)
            {
                while (data.Length % 4 != 0) data.WriteByte(0);
                long offset = data.Length;
                data.Write(bytes, 0, bytes.Length);
                string t = target > 0 ? ",\"target\":" + target : "";
                views.Add("{\"buffer\":0,\"byteOffset\":" + offset + ",\"byteLength\":" + bytes.Length + t + "}");
                return views.Count - 1;
            }

            public int Accessor(int view, int componentType, int count, string type, string extra)
            {
                accessors.Add("{\"bufferView\":" + view + ",\"componentType\":" + componentType + ",\"count\":" + count + ",\"type\":\"" + type + "\"" + extra + "}");
                return accessors.Count - 1;
            }
        }

        static string N(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
            return v.ToString("0.######", Inv);
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "objeto";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static void Write(List<MeshFormats.Part> parts, string path, bool binary)
        {
            var bin = new Bin();
            var meshes = new List<string>();
            var nodes = new List<string>();
            var materials = new List<string>();
            var images = new List<string>();
            var textures = new List<string>();
            var texIndex = new Dictionary<Texture, int>();
            for (int p = 0; p < parts.Count; p++)
            {
                var part = parts[p];
                int n = part.verts.Length;
                if (n == 0 || part.tris.Length < 3) continue;
                // Positions (+ bounds, required by glTF)
                var pos = new byte[n * 12];
                Vector3 mn = part.verts[0], mx = part.verts[0];
                for (int i = 0; i < n; i++)
                {
                    var v = part.verts[i];
                    mn = Vector3.Min(mn, v);
                    mx = Vector3.Max(mx, v);
                    Buffer.BlockCopy(BitConverter.GetBytes(v.x), 0, pos, i * 12, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(v.y), 0, pos, i * 12 + 4, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(v.z), 0, pos, i * 12 + 8, 4);
                }
                int aPos = bin.Accessor(bin.View(pos, 34962), 5126, n, "VEC3",
                    ",\"min\":[" + N(mn.x) + "," + N(mn.y) + "," + N(mn.z) + "],\"max\":[" + N(mx.x) + "," + N(mx.y) + "," + N(mx.z) + "]");
                var nor = new byte[n * 12];
                for (int i = 0; i < n; i++)
                {
                    var v = part.normals[i].sqrMagnitude > 0f ? part.normals[i].normalized : Vector3.up;
                    Buffer.BlockCopy(BitConverter.GetBytes(v.x), 0, nor, i * 12, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(v.y), 0, nor, i * 12 + 4, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(v.z), 0, nor, i * 12 + 8, 4);
                }
                int aNor = bin.Accessor(bin.View(nor, 34962), 5126, n, "VEC3", "");
                string attrs = "\"POSITION\":" + aPos + ",\"NORMAL\":" + aNor;
                if (part.uvs != null && part.uvs.Length == n)
                {
                    var uv = new byte[n * 8];
                    for (int i = 0; i < n; i++)
                    {
                        // glTF puts the texture origin at the top.
                        Buffer.BlockCopy(BitConverter.GetBytes(part.uvs[i].x), 0, uv, i * 8, 4);
                        Buffer.BlockCopy(BitConverter.GetBytes(1f - part.uvs[i].y), 0, uv, i * 8 + 4, 4);
                    }
                    attrs += ",\"TEXCOORD_0\":" + bin.Accessor(bin.View(uv, 34962), 5126, n, "VEC2", "");
                }
                var idx = new byte[part.tris.Length * 4];
                Buffer.BlockCopy(part.tris, 0, idx, 0, idx.Length);
                int aIdx = bin.Accessor(bin.View(idx, 34963), 5125, part.tris.Length, "SCALAR", "");

                // Material
                Color c = part.color.linear;
                string texJson = "";
                if (part.texture != null && part.uvs != null)
                {
                    int ti;
                    if (!texIndex.TryGetValue(part.texture, out ti))
                    {
                        var png = MeshFormats.TexturePng(part.texture);
                        ti = -1;
                        if (png != null)
                        {
                            int view = bin.View(png, 0);
                            images.Add("{\"bufferView\":" + view + ",\"mimeType\":\"image/png\"}");
                            textures.Add("{\"source\":" + (images.Count - 1) + ",\"sampler\":0}");
                            ti = textures.Count - 1;
                        }
                        texIndex[part.texture] = ti;
                    }
                    if (ti >= 0) texJson = ",\"baseColorTexture\":{\"index\":" + ti + "}";
                }
                if (texJson != "") c = new Color(1f, 1f, 1f, part.color.a);
                string alpha = c.a < 0.999f ? ",\"alphaMode\":\"BLEND\"" : "";
                materials.Add("{\"name\":\"" + Esc(part.name) + "\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[" + N(c.r) + "," + N(c.g) + "," + N(c.b) + "," + N(c.a) + "],\"metallicFactor\":0,\"roughnessFactor\":0.6" + texJson + "}" + alpha + "}");
                meshes.Add("{\"name\":\"" + Esc(part.name) + "\",\"primitives\":[{\"attributes\":{" + attrs + "},\"indices\":" + aIdx + ",\"material\":" + (materials.Count - 1) + "}]}");
                nodes.Add("{\"name\":\"" + Esc(part.name) + "\",\"mesh\":" + (meshes.Count - 1) + "}");
            }
            if (nodes.Count == 0) throw new Exception("no hay mallas que exportar");
            while (bin.data.Length % 4 != 0) bin.data.WriteByte(0);
            byte[] buffer = bin.data.ToArray();

            var js = new StringBuilder();
            js.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"ATLAS 3D\"},\"scene\":0,\"scenes\":[{\"name\":\"ATLAS 3D\",\"nodes\":[");
            for (int i = 0; i < nodes.Count; i++) js.Append(i > 0 ? "," : "").Append(i);
            js.Append("]}],\"nodes\":[").Append(string.Join(",", nodes)).Append("]");
            js.Append(",\"meshes\":[").Append(string.Join(",", meshes)).Append("]");
            js.Append(",\"materials\":[").Append(string.Join(",", materials)).Append("]");
            if (textures.Count > 0)
            {
                js.Append(",\"samplers\":[{\"magFilter\":9729,\"minFilter\":9987,\"wrapS\":10497,\"wrapT\":10497}]");
                js.Append(",\"images\":[").Append(string.Join(",", images)).Append("]");
                js.Append(",\"textures\":[").Append(string.Join(",", textures)).Append("]");
            }
            js.Append(",\"accessors\":[").Append(string.Join(",", bin.accessors)).Append("]");
            js.Append(",\"bufferViews\":[").Append(string.Join(",", bin.views)).Append("]");
            if (binary)
                js.Append(",\"buffers\":[{\"byteLength\":").Append(buffer.Length).Append("}]}");
            else
                js.Append(",\"buffers\":[{\"byteLength\":").Append(buffer.Length).Append(",\"uri\":\"data:application/octet-stream;base64,").Append(Convert.ToBase64String(buffer)).Append("\"}]}");

            if (!binary)
            {
                File.WriteAllText(path, js.ToString(), new UTF8Encoding(false));
                return;
            }
            byte[] json = Encoding.UTF8.GetBytes(js.ToString());
            int jsonLen = (json.Length + 3) & ~3;
            int total = 12 + 8 + jsonLen + 8 + buffer.Length;
            using (var f = File.Create(path))
            using (var w = new BinaryWriter(f))
            {
                w.Write(0x46546C67u);   // "glTF"
                w.Write(2u);
                w.Write((uint)total);
                w.Write((uint)jsonLen);
                w.Write(0x4E4F534Au);   // "JSON"
                w.Write(json);
                for (int i = json.Length; i < jsonLen; i++) w.Write((byte)0x20);
                w.Write((uint)buffer.Length);
                w.Write(0x004E4942u);   // "BIN"
                w.Write(buffer);
            }
        }
    }
}
