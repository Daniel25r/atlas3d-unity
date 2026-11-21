// ATLAS 5D (Unity) - an editable mesh straight from faces (quads and
// n-gons), for the shapes made by ShapeGen: Modelar shows them as clean
// quads and one-face lids, like Blender.
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public partial class EditMesh
    {
        public static EditMesh FromPolys(Vector3[] v, Vector2[] uv, List<int[]> polys)
        {
            var e = new EditMesh();
            var map = new int[v.Length];
            var weld = new Dictionary<long, List<int>>();
            for (int i = 0; i < v.Length; i++)
            {
                long k = Key(v[i]);
                List<int> cands;
                int found = -1;
                if (weld.TryGetValue(k, out cands))
                    foreach (int c in cands) if ((e.verts[c] - v[i]).sqrMagnitude < 1e-10f) { found = c; break; }
                if (found < 0)
                {
                    found = e.verts.Count;
                    e.verts.Add(v[i]);
                    if (cands == null) { cands = new List<int>(); weld[k] = cands; }
                    cands.Add(found);
                }
                map[i] = found;
            }
            foreach (var f in polys)
            {
                // Corners that ended up on the same point (a pole) are dropped.
                var keep = new List<int>();
                for (int k = 0; k < f.Length; k++)
                {
                    int a = map[f[k]];
                    bool dup = false;
                    foreach (int q in keep) if (map[q] == a) dup = true;
                    if (!dup) keep.Add(f[k]);
                }
                if (keep.Count >= 3) e.AddFace(keep.ToArray(), map, uv);
            }
            return e;
        }
    }
}
