// ATLAS 3D (Unity) - texture layout (UV) for parts that have none, or whose
// UVs are all the same point (some Prisma3D / OBJ / STL parts): a box
// projection from the part's own size, so a picture covers it instead of
// showing a single pixel of it.
using UnityEngine;

namespace Atlas
{
    public static class UvFix
    {
        public static void Ensure(Renderer r)
        {
            Mesh mesh = null;
            var smr = r as SkinnedMeshRenderer;
            if (smr != null) mesh = smr.sharedMesh;
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) mesh = mf.sharedMesh;
            }
            if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0) return;
            var uv = mesh.uv;
            if (uv.Length == mesh.vertexCount)
            {
                Vector2 mn = uv[0], mx = uv[0];
                foreach (var u in uv) { mn = Vector2.Min(mn, u); mx = Vector2.Max(mx, u); }
                if ((mx - mn).sqrMagnitude > 1e-8f) return;   // it has a layout
            }
            var v = mesh.vertices;
            var n = mesh.normals;
            var b = mesh.bounds;
            Vector3 size = b.size;
            for (int k = 0; k < 3; k++) if (size[k] < 1e-6f) size[k] = 1e-6f;
            var o = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = v[i] - b.min;
                Vector3 nn = i < n.Length ? n[i] : Vector3.forward;
                float ax = Mathf.Abs(nn.x), ay = Mathf.Abs(nn.y), az = Mathf.Abs(nn.z);
                if (az >= ax && az >= ay) o[i] = new Vector2((nn.z >= 0f ? size.x - p.x : p.x) / size.x, p.y / size.y);
                else if (ax >= ay) o[i] = new Vector2((nn.x >= 0f ? p.z : size.z - p.z) / size.z, p.y / size.y);
                else o[i] = new Vector2(p.x / size.x, (nn.y >= 0f ? size.z - p.z : p.z) / size.z);
            }
            mesh.uv = o;
        }
    }
}
