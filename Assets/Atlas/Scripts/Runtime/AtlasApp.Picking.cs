// ATLAS 3D (Unity) - picking by pixel, like the Godot version (pick_px):
// on a tap every visible object is drawn once, off screen, in its own flat
// colour, and the colour under the finger says which object it was. It
// hits exactly what is on screen (any pose, animation or shape, thin parts
// included) and never what is hidden behind.
using System.Collections.Generic;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        readonly List<Material> pickMats = new List<Material>();

        /// Flat colour of the i-th object (sRGB; levels far apart so the
        /// colour read back is never ambiguous).
        static Color PickColor(int i)
        {
            float[] lv = { 0.22f, 0.42f, 0.62f, 0.82f, 1f };
            int n = i + 1;   // black = nothing
            return new Color(lv[n % 5], lv[(n / 5) % 5], lv[(n / 25) % 5], 1f) * (n % 5 == 0 && (n / 5) % 5 == 0 && (n / 25) % 5 == 0 ? 0.5f : 1f);
        }

        Material PickMat(int i)
        {
            while (pickMats.Count <= i)
            {
                var m = new Material(unlitTemplate);
                MatUtil.SetTransparent(m, false);
                m.SetColor("_BaseColor", PickColor(pickMats.Count));
                pickMats.Add(m);
            }
            return pickMats[i];
        }

        AtlasItem PickItem(Vector2 screen)
        {
            AtlasItem hit;
            if (PickByColor(screen, out hit)) return hit;
            return PickItemTriangles(screen);
        }

        /// false only if the pixel render could not run (then the triangle
        /// test is used).
        bool PickByColor(Vector2 screen, out AtlasItem hit)
        {
            hit = null;
            var cands = new List<AtlasItem>();
            foreach (var it in items)
                if (it != null && it.visible && !it.locked) cands.Add(it);
            if (cands.Count == 0) return true;
            if (cands.Count > 120) return false;
            var swapped = new List<KeyValuePair<Renderer, Material[]>>();
            var hiddenOther = new List<Renderer>();
            bool groundOn = ground.activeSelf, gizmoOn = gizmo.gameObject.activeSelf, fogOn = RenderSettings.fog;
            var clear = cam.clearFlags;
            Color bg = cam.backgroundColor;
            bool post = false;
            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data = null;
            RenderTexture rt = null;
            var prevTarget = cam.targetTexture;
            try
            {
                // Objects that cannot be picked (locked / hidden) still hide
                // what is behind them: drawn black.
                var black = PickBlack();
                foreach (var it in items)
                {
                    if (it == null) continue;
                    int ci = cands.IndexOf(it);
                    foreach (var r in it.GetComponentsInChildren<Renderer>())
                    {
                        if (r.gameObject.layer == Gizmo.Layer || !r.enabled) continue;
                        var old = r.sharedMaterials;
                        swapped.Add(new KeyValuePair<Renderer, Material[]>(r, old));
                        var m = ci >= 0 ? PickMat(ci) : black;
                        var arr = new Material[Mathf.Max(1, old.Length)];
                        for (int k = 0; k < arr.Length; k++) arr[k] = m;
                        r.sharedMaterials = arr;
                    }
                }
                ground.SetActive(false);
                gizmo.gameObject.SetActive(false);
                highlighter.SetVisible(false);
                RenderSettings.fog = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
                post = data.renderPostProcessing;
                data.renderPostProcessing = false;
                int w = Mathf.Max(64, Screen.width / 2), h = Mathf.Max(64, Screen.height / 2);
                rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear, 1);
                rt.filterMode = FilterMode.Point;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prevTarget;
                // Read a small square around the finger; the centre wins,
                // then the nearest coloured pixel.
                int px = Mathf.Clamp(Mathf.RoundToInt(screen.x * w / Screen.width), 0, w - 1);
                int py = Mathf.Clamp(Mathf.RoundToInt(screen.y * h / Screen.height), 0, h - 1);
                int rad = Mathf.Clamp(Mathf.RoundToInt((Screen.dpi > 0 ? Screen.dpi : 320f) * 0.03f * w / Screen.width), 1, 12);
                int x0 = Mathf.Max(0, px - rad), y0 = Mathf.Max(0, py - rad);
                int x1 = Mathf.Min(w - 1, px + rad), y1 = Mathf.Min(h - 1, py + rad);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(x1 - x0 + 1, y1 - y0 + 1, TextureFormat.RGBA32, false, true);
                tex.ReadPixels(new UnityEngine.Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                var px32 = tex.GetPixels();
                int tw = tex.width;
                Destroy(tex);
                float bestD = float.MaxValue;
                for (int i = 0; i < px32.Length; i++)
                {
                    Color c = px32[i];
                    if (c.r + c.g + c.b < 0.03f) continue;
                    int ix = x0 + i % tw, iy = y0 + i / tw;
                    float d = (ix - px) * (ix - px) + (iy - py) * (iy - py);
                    if (d >= bestD) continue;
                    int found = Decode(c, cands.Count);
                    if (found < 0) continue;
                    bestD = d;
                    hit = cands[found];
                }
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("ATLAS: selección por píxel no disponible: " + e.Message);
                return false;
            }
            finally
            {
                foreach (var kv in swapped) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
                cam.targetTexture = prevTarget;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                ground.SetActive(groundOn);
                gizmo.gameObject.SetActive(gizmoOn);
                highlighter.SetVisible(true);
                RenderSettings.fog = fogOn;
                cam.clearFlags = clear;
                cam.backgroundColor = bg;
                if (data != null) data.renderPostProcessing = post;
            }
        }

        Material pickBlack;

        Material PickBlack()
        {
            if (pickBlack == null)
            {
                pickBlack = new Material(unlitTemplate);
                MatUtil.SetTransparent(pickBlack, false);
                pickBlack.SetColor("_BaseColor", Color.black);
            }
            return pickBlack;
        }

        /// Which object's colour is this (read from a linear texture)?
        static int Decode(Color c, int count)
        {
            int best = -1;
            float bestD = 0.02f;
            for (int i = 0; i < count; i++)
            {
                Color e = PickColor(i).linear;
                float d = (e.r - c.r) * (e.r - c.r) + (e.g - c.g) * (e.g - c.g) + (e.b - c.b) * (e.b - c.b);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }
    }
}
