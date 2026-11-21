// ATLAS 5D (Unity) - «Informar de un error»: a short form (what fails, the
// details, a few tags) plus, on its own, the app version and the device's
// data and a picture of the screen as it was. It can be opened as a report
// on the project's GitHub page, saved / shared as a file, or copied.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        const string BugRepo = "Daniel25r/atlas3d-unity";
        GameObject bugWindow;
        string bugShot;   // the screen as it was when the form was opened
        readonly HashSet<string> bugTags = new HashSet<string>();
        static readonly string[] BugTagNames = { "Error", "Se cierra", "Interfaz", "Huesos", "Animación", "Importar / exportar", "Render", "Lento", "Idea" };

        void ShowBugReport()
        {
            CloseMenus();
            StartCoroutine(BugReportAfterShot());
        }

        IEnumerator BugReportAfterShot()
        {
            // The picture first, before the form covers the screen.
            yield return new WaitForEndOfFrame();
            bugShot = null;
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex != null)
                {
                    string dir = Path.Combine(Application.temporaryCachePath, "informe");
                    Directory.CreateDirectory(dir);
                    bugShot = Path.Combine(dir, "pantalla.png");
                    File.WriteAllBytes(bugShot, tex.EncodeToPNG());
                    Destroy(tex);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("ATLAS: captura: " + e.Message); }
            BuildBugWindow();
        }

        void CloseBugReport()
        {
            if (bugWindow != null) Destroy(bugWindow);
            bugWindow = null;
        }

        void BuildBugWindow()
        {
            CloseBugReport();
            bugTags.Clear();
            var dim = AtlasTheme.Box(canvasRoot, "Informe", new Color(0f, 0f, 0f, 0.55f));
            AtlasTheme.Fill(dim.rectTransform);
            bugWindow = dim.gameObject;
            var card = AtlasTheme.Box(dim.transform, "Ventana", AtlasTheme.Panel2, 18);
            card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            var cr = card.rectTransform;
            cr.anchorMin = new Vector2(0.5f, 0.05f);
            cr.anchorMax = new Vector2(0.5f, 0.95f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            cr.sizeDelta = new Vector2(Mathf.Min(560f, canvasRoot.rect.width - 24f), 0f);
            cr.anchoredPosition = Vector2.zero;
            var title = AtlasTheme.Label(card.transform, "Informar de un error", 20, AtlasTheme.TextColor, true, TextAnchor.MiddleCenter);
            AtlasTheme.Pin(title.rectTransform, 0.5f, 1f, new Vector2(0f, -26f), new Vector2(320f, 34f));
            title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var x = AtlasTheme.IconButton(card.transform, "close", CloseBugReport, 44f, 22f);
            AtlasTheme.Pin((RectTransform)x.transform, 1f, 1f, new Vector2(-8f, -6f), new Vector2(44f, 44f));
            ((RectTransform)x.transform).pivot = new Vector2(1f, 1f);
            var area = AtlasTheme.Rect(card.transform, "Area");
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(10f, 10f);
            area.offsetMax = new Vector2(-10f, -56f);
            ScrollRect sr;
            var c = AtlasTheme.ScrollArea(area, out sr, 8f, new RectOffset(12, 12, 6, 12));
            float w = cr.sizeDelta.x - 44f;

            Hint(c, "Cuéntalo con tus palabras. Se añaden solos la versión de ATLAS, el modelo del teléfono y una captura de la pantalla de cuando abriste esto.");
            var l1 = AtlasTheme.Label(c, "Qué falla (en pocas palabras)", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(l1.gameObject, 24f);
            var summary = AtlasTheme.TextField(c, "", null, 46f);
            summary.characterLimit = 120;
            var l2 = AtlasTheme.Label(c, "Detalles: qué hiciste, qué esperabas y qué pasó", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(l2.gameObject, 24f);
            var details = AtlasTheme.TextField(c, "", null, 130f);
            details.characterLimit = 3000;
            details.lineType = InputField.LineType.MultiLineNewline;
            details.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            details.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            details.textComponent.alignment = TextAnchor.UpperLeft;
            details.textComponent.fontSize = 16;
            var l3 = AtlasTheme.Label(c, "Etiquetas", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(l3.gameObject, 24f);
            var g = AtlasTheme.Grid(c, 3, w, 34f, 4f);
            foreach (var tag in BugTagNames)
            {
                string tg = tag;
                Button chip = null;
                chip = AtlasTheme.Chip(g, tg, () =>
                {
                    if (!bugTags.Remove(tg)) bugTags.Add(tg);
                    AtlasTheme.SetActive(chip, bugTags.Contains(tg));
                });
            }
            bool withShot = bugShot != null;
            if (bugShot != null)
                AtlasTheme.Switch(c, "Adjuntar la captura de la pantalla", true, on => withShot = on);
            var send = AtlasTheme.TextButton(c, "export", "Enviar como reporte en GitHub", () =>
            {
                string body = BugText(summary.text, details.text, false);
                string url = "https://github.com/" + BugRepo + "/issues/new?title=" + System.Uri.EscapeDataString("[Error] " + BugTitle(summary.text)) +
                             "&body=" + System.Uri.EscapeDataString(body.Length > 6000 ? body.Substring(0, 6000) : body);
                if (withShot) SaveBugFiles(summary.text, details.text, true);
                Application.OpenURL(url);
                Toast(withShot ? "Se abre GitHub: la captura quedó guardada; arrástrala al reporte antes de enviarlo" : "Se abre GitHub con el reporte escrito: pulsa «Submit new issue»");
            }, true, 46f, 16);
            AtlasTheme.TextButton(c, "save", "Guardar o compartir (archivo)", () => SaveBugFiles(summary.text, details.text, withShot), false, 46f, 16);
            AtlasTheme.TextButton(c, "copy", "Copiar el informe", () =>
            {
                GUIUtility.systemCopyBuffer = BugText(summary.text, details.text, false);
                Toast("Informe copiado: pégalo en Discord, WhatsApp o donde quieras");
            }, false, 44f, 16);
            if (toastHolder != null) toastHolder.SetAsLastSibling();
        }

        static string BugTitle(string summary)
        {
            summary = (summary ?? "").Trim();
            return summary.Length > 0 ? summary : "Sin resumen";
        }

        string BugText(string summary, string details, bool forFile)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("## ").Append(BugTitle(summary)).Append("\n\n");
            if (!string.IsNullOrEmpty(details)) sb.Append(details.Trim()).Append("\n\n");
            if (bugTags.Count > 0) sb.Append("**Etiquetas:** ").Append(string.Join(", ", new List<string>(bugTags).ToArray())).Append("\n\n");
            sb.Append("---\n");
            sb.Append("- ATLAS 5D ").Append(Version).Append(Desktop ? " (PC)" : " (móvil)").Append('\n');
            sb.Append("- Aparato: ").Append(SystemInfo.deviceModel).Append('\n');
            sb.Append("- Sistema: ").Append(SystemInfo.operatingSystem).Append('\n');
            sb.Append("- Memoria: ").Append(SystemInfo.systemMemorySize).Append(" MB · Gráficos: ").Append(SystemInfo.graphicsDeviceName).Append(" (").Append(SystemInfo.graphicsDeviceType).Append(")\n");
            sb.Append("- Pantalla: ").Append(Screen.width).Append('×').Append(Screen.height).Append('\n');
            if (store != null) sb.Append("- Objetos en la escena: ").Append(items.Count).Append('\n');
            if (forFile) sb.Append("\n(La captura de la pantalla va en el mismo .zip)\n");
            return sb.ToString();
        }

        void SaveBugFiles(string summary, string details, bool withShot)
        {
            try
            {
                string zip = Path.Combine(Exporter.ExportDir, "informe_error_" + Stamp() + ".zip");
                if (File.Exists(zip)) File.Delete(zip);
                using (var fs = File.Create(zip))
                using (var z = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
                {
                    var e = z.CreateEntry("informe.md");
                    using (var w = new StreamWriter(e.Open())) w.Write(BugText(summary, details, withShot && bugShot != null));
                    if (withShot && bugShot != null && File.Exists(bugShot))
                    {
                        var p = z.CreateEntry("pantalla.png");
                        using (var o = p.Open())
                        {
                            var bytes = File.ReadAllBytes(bugShot);
                            o.Write(bytes, 0, bytes.Length);
                        }
                    }
                }
                Share(zip, "Informe de error");
            }
            catch (System.Exception e) { Toast("No se pudo guardar el informe: " + e.Message, true); }
        }
    }
}
