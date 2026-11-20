// ATLAS 3D (Unity) - the start screen to your taste: your own picture as
// the background (darkened as much as you like), the logo in the colour
// chosen for the interface, and «3D | 2D» next to the logo to jump to
// Artlas 2D, as one app.
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        RawImage homeBgImage;
        Image homeBgDim;
        Texture2D homeBgTex, logoGray;

        static string HomeBgPath { get { return Path.Combine(Application.persistentDataPath, "fondo_inicio.img"); } }

        /// The background picture (and its darkening) behind the start screen.
        void ApplyHomeBackground()
        {
            if (home == null) return;
            if (homeBgImage == null)
            {
                homeBgImage = new GameObject("Fondo").AddComponent<RawImage>();
                homeBgImage.transform.SetParent(home.transform, false);
                homeBgImage.transform.SetAsFirstSibling();
                homeBgImage.raycastTarget = false;
                AtlasTheme.Fill(homeBgImage.rectTransform);
                homeBgImage.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                homeBgDim = AtlasTheme.Box(home.transform, "Oscurecer", new Color(0f, 0f, 0f, 0.4f));
                homeBgDim.raycastTarget = false;
                AtlasTheme.Fill(homeBgDim.rectTransform);
                homeBgDim.transform.SetSiblingIndex(1);
            }
            bool has = File.Exists(HomeBgPath);
            if (has && homeBgTex == null)
            {
                homeBgTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!homeBgTex.LoadImage(File.ReadAllBytes(HomeBgPath))) { Destroy(homeBgTex); homeBgTex = null; has = false; }
            }
            if (!has && homeBgTex != null) { Destroy(homeBgTex); homeBgTex = null; }
            homeBgImage.texture = homeBgTex;
            homeBgImage.gameObject.SetActive(homeBgTex != null);
            if (homeBgTex != null)
                homeBgImage.GetComponent<AspectRatioFitter>().aspectRatio = (float)homeBgTex.width / Mathf.Max(1, homeBgTex.height);
            homeBgDim.gameObject.SetActive(homeBgTex != null);
            homeBgDim.color = new Color(0f, 0f, 0f, Mathf.Clamp01(Prefs.homeDim));
        }

        void PickHomeBackground()
        {
            if (NativeFilePicker.IsFilePickerBusy()) return;
            NativeFilePicker.PickFile(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    // Checked, made smaller if huge (a phone photo is 12 MP), and kept.
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!t.LoadImage(File.ReadAllBytes(path))) { Destroy(t); Toast("Esa imagen no se puede leer (usa PNG o JPG)", true); return; }
                    byte[] png;
                    if (t.width > 2048 || t.height > 2048)
                    {
                        float k = 2048f / Mathf.Max(t.width, t.height);
                        var rt = RenderTexture.GetTemporary(Mathf.RoundToInt(t.width * k), Mathf.RoundToInt(t.height * k));
                        Graphics.Blit(t, rt);
                        var prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        var small = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                        small.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0);
                        small.Apply();
                        RenderTexture.active = prev;
                        RenderTexture.ReleaseTemporary(rt);
                        png = small.EncodeToJPG(90);
                        Destroy(small);
                    }
                    else png = File.ReadAllBytes(path);
                    Destroy(t);
                    File.WriteAllBytes(HomeBgPath, png);
                    if (homeBgTex != null) { Destroy(homeBgTex); homeBgTex = null; }
                    ApplyHomeBackground();
                    Toast("Fondo de inicio puesto");
                }
                catch (System.Exception e) { Toast("No se pudo poner el fondo: " + e.Message, true); }
            }, new string[] { "image/*" });
        }

        void RemoveHomeBackground()
        {
            try { if (File.Exists(HomeBgPath)) File.Delete(HomeBgPath); } catch (System.Exception) { }
            if (homeBgTex != null) { Destroy(homeBgTex); homeBgTex = null; }
            ApplyHomeBackground();
            Toast("Fondo de inicio quitado");
        }

        /// The logo in the interface's colour (a grey copy tinted).
        Texture LogoTexture(RawImage target)
        {
            var src = Resources.Load<Texture2D>("AtlasLogo");
            if (src == null) return null;
            if (Prefs.accent == 0) { target.color = Color.white; return src; }   // the original colours
            if (logoGray == null)
            {
                try
                {
                    var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(src, rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                    t.ReadPixels(new UnityEngine.Rect(0, 0, src.width, src.height), 0, 0);
                    t.Apply();
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    var px = t.GetPixels32();
                    for (int i = 0; i < px.Length; i++)
                    {
                        int g = Mathf.Min(255, (int)((px[i].r * 0.35f + px[i].g * 0.3f + px[i].b * 0.45f) * 1.25f));
                        px[i] = new Color32((byte)g, (byte)g, (byte)g, px[i].a);
                    }
                    t.SetPixels32(px);
                    t.Apply();
                    logoGray = t;
                }
                catch (System.Exception) { target.color = Color.white; return src; }
            }
            target.color = AtlasTheme.AccentLight;
            return logoGray;
        }

        /// «3D | 2D» under the name: ATLAS and Artlas 2D in one app.
        void Build3D2DSwitch(Transform parent)
        {
            var row = AtlasTheme.Rect(parent, "Modos").gameObject;
            AtlasTheme.Row(row, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft);
            AtlasTheme.Le(row, 30f);
            var b3 = AtlasTheme.Chip(row.transform, "3D", () => { }, true);
            AtlasTheme.Le(b3.gameObject, 30f, 64f);
            var b2 = AtlasTheme.Chip(row.transform, "2D", Open2D, false);
            AtlasTheme.Le(b2.gameObject, 30f, 64f);
        }
    }
}
