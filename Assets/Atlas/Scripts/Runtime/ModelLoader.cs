// ATLAS 3D (Unity) - imported models (GLB / glTF) through glTFast.
// The model is placed on the ground, gets a box for touch selection, and
// any material whose shader is missing on this phone is replaced by a
// standard URP one with the same colour and texture (never pink).
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Atlas
{
    public static class ModelLoader
    {
        static readonly string[] TexProps = { "baseColorTexture", "_BaseMap", "_MainTex", "_BaseColorMap" };
        static readonly string[] ColorProps = { "baseColorFactor", "_BaseColor", "_Color" };

        /// Loads `path` under `root`. Returns "" or an error message.
        public static async Task<string> Load(string path, AtlasItem root, Material litTemplate, bool placeOnGround)
        {
            if (!File.Exists(path)) return "No se encuentra el archivo del modelo.";
            var gltf = new GLTFast.GltfImport();
            bool ok;
            try
            {
                var uri = new Uri("file://" + path);
                if (path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                    ok = await gltf.LoadGltfBinary(File.ReadAllBytes(path), uri);
                else
                    ok = await gltf.Load(path);
                if (ok && root != null)
                {
                    var holder = new GameObject("Modelo");
                    holder.transform.SetParent(root.transform, false);
                    ok = await gltf.InstantiateMainSceneAsync(holder.transform);
                }
            }
            catch (Exception e)
            {
                gltf.Dispose();
                return "No se pudo abrir el modelo: " + e.Message;
            }
            if (!ok || root == null)
            {
                gltf.Dispose();
                return "No se pudo abrir el modelo (¿es un GLB / glTF válido?).";
            }
            root.importer = gltf;
            FixMaterials(root.gameObject, litTemplate);
            Fit(root, placeOnGround);
            return "";
        }

        /// Box collider around the model (selection) and, for a new model,
        /// its feet on the ground and a sensible size.
        static void Fit(AtlasItem root, bool placeOnGround)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Transform holder = root.transform.Find("Modelo");
            if (placeOnGround && holder != null)
            {
                Bounds wb = WorldBounds(renderers);
                float size = Mathf.Max(wb.size.x, Mathf.Max(wb.size.y, wb.size.z));
                // Huge exports (centimetres) or tiny ones: about 2 m tall.
                if (size > 50f || size < 0.05f)
                    holder.localScale *= 2f / Mathf.Max(0.0001f, size);
                wb = WorldBounds(renderers);
                Vector3 shift = new Vector3(root.transform.position.x - wb.center.x, root.transform.position.y - wb.min.y, root.transform.position.z - wb.center.z);
                holder.position += shift;
            }
            Bounds b = WorldBounds(renderers);
            var box = root.gameObject.GetComponent<BoxCollider>();
            if (box == null) box = root.gameObject.AddComponent<BoxCollider>();
            Vector3 lossy = root.transform.lossyScale;
            box.center = root.transform.InverseTransformPoint(b.center);
            box.size = new Vector3(b.size.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)), b.size.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)), b.size.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
        }

        static Bounds WorldBounds(Renderer[] rs)
        {
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        static void FixMaterials(GameObject go, Material litTemplate)
        {
            if (litTemplate == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m != null && m.shader != null && m.shader.isSupported && m.shader.name != "Hidden/InternalErrorShader") continue;
                    var n = new Material(litTemplate);
                    if (m != null)
                    {
                        foreach (var p in TexProps)
                            if (m.HasProperty(p) && m.GetTexture(p) != null) { n.SetTexture("_BaseMap", m.GetTexture(p)); break; }
                        foreach (var p in ColorProps)
                            if (m.HasProperty(p)) { n.SetColor("_BaseColor", m.GetColor(p)); break; }
                    }
                    mats[i] = n;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
