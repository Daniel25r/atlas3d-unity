// ATLAS 3D (Unity) - «Animación 2D»: Artlas 2D (frame-by-frame drawing and
// animation, a web app) inside ATLAS. Its files travel in
// Resources/Atlas2D (fNN.bytes + files.txt), are copied once per version to
// the app's folder and opened:
//   Android  in its own screen (Atlas2DActivity, a WebView), back returns
//            to ATLAS;
//   PC       in the browser, served by a tiny local web server
//            (http://localhost, a secure origin: saving and video export
//            work).
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Atlas
{
    public partial class AtlasApp
    {
        static TcpListener web2d;
        static string web2dRoot;
        static int web2dPort;

        /// Copies the 2D app's files where they can be opened (again only
        /// when a new version of ATLAS brings other files). Returns the folder.
        static string Ensure2D()
        {
            var list = Resources.Load<TextAsset>("Atlas2D/files");
            if (list == null) throw new System.Exception("Faltan los archivos del modo 2D en esta versión");
            var lines = list.text.Replace("\r", "").Split('\n');
            string stamp = lines[0].Trim();
            string root = Path.Combine(Application.persistentDataPath, "Atlas2D");
            string stampFile = Path.Combine(root, ".version");
            if (File.Exists(stampFile) && File.ReadAllText(stampFile).Trim() == stamp) return root;
            Directory.CreateDirectory(root);
            for (int i = 1; i < lines.Length; i++)
            {
                string rel = lines[i].Trim();
                if (rel == "" || rel.Contains("..")) continue;
                var ta = Resources.Load<TextAsset>("Atlas2D/f" + (i - 1).ToString("00"));
                if (ta == null) throw new System.Exception("Falta un archivo del modo 2D: " + rel);
                string dst = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.WriteAllBytes(dst, ta.bytes);
                Resources.UnloadAsset(ta);
            }
            File.WriteAllText(stampFile, stamp);
            return root;
        }

        void Open2D()
        {
            CloseMenus();
            string root;
            try { root = Ensure2D(); }
            catch (System.Exception e) { Toast("No se pudo abrir el modo 2D: " + e.Message, true); return; }
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = new AndroidJavaObject("android.content.Intent"))
                {
                    string pkg = act.Call<string>("getPackageName");
                    intent.Call<AndroidJavaObject>("setClassName", pkg, "org.atlas3d.Atlas2DActivity").Dispose();
                    intent.Call<AndroidJavaObject>("putExtra", "root", root).Dispose();
                    act.Call("startActivity", intent);
                }
            }
            catch (System.Exception e) { Toast("No se pudo abrir el modo 2D: " + e.Message, true); }
#else
            try
            {
                int port = Start2DServer(root);
                Application.OpenURL("http://localhost:" + port + "/index.html");
                Toast("Animación 2D abierta en el navegador. Tus dibujos se guardan en este ordenador");
            }
            catch (System.Exception e) { Toast("No se pudo abrir el modo 2D: " + e.Message, true); }
#endif
        }

        // ------------------------------------------------------------------
        // PC: local web server (only this computer can reach it)

        /// Always the same port when it is free, so the browser keeps the
        /// drawings saved for that address.
        static int Start2DServer(string root)
        {
            web2dRoot = root;
            if (web2d != null) return web2dPort;
            for (int p = 47823; p < 47843; p++)
            {
                try
                {
                    var l = new TcpListener(IPAddress.Loopback, p);
                    l.Start();
                    web2d = l;
                    web2dPort = p;
                    var th = new Thread(Serve2D) { IsBackground = true, Name = "ATLAS 2D" };
                    th.Start();
                    return p;
                }
                catch (SocketException) { }
            }
            throw new System.Exception("no hay un puerto libre");
        }

        static void Serve2D()
        {
            while (web2d != null)
            {
                TcpClient c;
                try { c = web2d.AcceptTcpClient(); }
                catch (System.Exception) { return; }
                ThreadPool.QueueUserWorkItem(_ => Answer2D(c));
            }
        }

        static void Answer2D(TcpClient c)
        {
            try
            {
                using (c)
                using (var s = c.GetStream())
                {
                    s.ReadTimeout = 5000;
                    // Request line and headers (up to the blank line).
                    var head = new StringBuilder();
                    var one = new byte[1];
                    while (head.Length < 16384)
                    {
                        int n = s.Read(one, 0, 1);
                        if (n <= 0) break;
                        head.Append((char)one[0]);
                        if (head.Length >= 4 && head.ToString(head.Length - 4, 4) == "\r\n\r\n") break;
                    }
                    var first = head.ToString().Split('\n')[0].Trim().Split(' ');
                    if (first.Length < 2 || (first[0] != "GET" && first[0] != "HEAD")) { Reply(s, 405, "text/plain", Encoding.UTF8.GetBytes("Method Not Allowed"), false); return; }
                    string path = first[1];
                    int q = path.IndexOfAny(new[] { '?', '#' });
                    if (q >= 0) path = path.Substring(0, q);
                    path = WebUtilityDecode(path);
                    if (path == "" || path == "/") path = "/index.html";
                    string full = Path.GetFullPath(Path.Combine(web2dRoot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
                    string rootFull = Path.GetFullPath(web2dRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!full.StartsWith(rootFull) || !File.Exists(full)) { Reply(s, 404, "text/plain", Encoding.UTF8.GetBytes("Not Found"), false); return; }
                    Reply(s, 200, Mime2D(full), File.ReadAllBytes(full), first[0] == "HEAD");
                }
            }
            catch (System.Exception) { }
        }

        static string WebUtilityDecode(string p)
        {
            try { return System.Uri.UnescapeDataString(p); }
            catch (System.Exception) { return p; }
        }

        static void Reply(Stream s, int code, string mime, byte[] body, bool headOnly)
        {
            string status = code == 200 ? "OK" : code == 404 ? "Not Found" : "Method Not Allowed";
            var h = "HTTP/1.1 " + code + " " + status + "\r\n" +
                    "Content-Type: " + mime + "\r\n" +
                    "Content-Length: " + body.Length + "\r\n" +
                    "Cache-Control: no-cache\r\n" +
                    "Connection: close\r\n\r\n";
            var hb = Encoding.ASCII.GetBytes(h);
            s.Write(hb, 0, hb.Length);
            if (!headOnly) s.Write(body, 0, body.Length);
            s.Flush();
        }

        static string Mime2D(string p)
        {
            string l = p.ToLowerInvariant();
            if (l.EndsWith(".html")) return "text/html; charset=utf-8";
            if (l.EndsWith(".js")) return "application/javascript; charset=utf-8";
            if (l.EndsWith(".css")) return "text/css; charset=utf-8";
            if (l.EndsWith(".svg")) return "image/svg+xml";
            if (l.EndsWith(".png")) return "image/png";
            if (l.EndsWith(".json")) return "application/json";
            if (l.EndsWith(".txt")) return "text/plain; charset=utf-8";
            if (l.EndsWith(".woff2")) return "font/woff2";
            if (l.EndsWith(".woff")) return "font/woff";
            return "application/octet-stream";
        }

        void OnDisable2D()
        {
            var l = web2d;
            web2d = null;
            if (l != null) try { l.Stop(); } catch (System.Exception) { }
        }
    }
}
