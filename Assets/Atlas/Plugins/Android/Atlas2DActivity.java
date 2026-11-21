package org.atlas3d;

import android.Manifest;
import android.app.Activity;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.media.MediaScannerConnection;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.speech.tts.TextToSpeech;
import android.speech.tts.UtteranceProgressListener;
import android.webkit.PermissionRequest;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import android.os.Environment;
import android.provider.MediaStore;
import android.text.TextUtils;
import android.util.Base64;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.webkit.JavascriptInterface;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;

/**
 * ATLAS 3D - modo 2D (Artlas 2D) dentro de la app. Adaptado del contenedor
 * de Artlas 2D: los archivos web los copia ATLAS a una carpeta de la app
 * (extra "root") y se sirven desde ahí.
 *
 * Artlas 2D: contenedor nativo. La interfaz y el motor de dibujo viven en assets/web
 * y se sirven desde un origen seguro local (https://app.trazo.local/) para que
 * funcionen IndexedDB, WebCodecs y Web Audio sin conexión.
 */
public class Atlas2DActivity extends Activity {
    private static final String HOST = "app.trazo.local";
    private static final int REQ_FILE = 41;

    private WebView web;
    private ValueCallback<Uri[]> fileCallback;
    private File root;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        String r = getIntent() != null ? getIntent().getStringExtra("root") : null;
        if (r == null) { finish(); return; }
        root = new File(r);
        Window w = getWindow();
        w.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        w.setStatusBarColor(Color.parseColor("#0B0714"));
        w.setNavigationBarColor(Color.parseColor("#0B0714"));

        web = new WebView(this);
        web.setBackgroundColor(Color.BLACK);
        setContentView(web);

        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setDatabaseEnabled(true);
        s.setMediaPlaybackRequiresUserGesture(false);
        s.setAllowFileAccess(false);
        s.setAllowContentAccess(true);
        s.setSupportZoom(false);
        s.setBuiltInZoomControls(false);
        s.setTextZoom(100);
        s.setCacheMode(WebSettings.LOAD_NO_CACHE);

        web.setWebViewClient(new LocalClient(root));
        web.setWebChromeClient(new Chrome(this));

        web.addJavascriptInterface(new Bridge(this), "AndroidBridge");
        voice = new Voice(this);
        web.addJavascriptInterface(voice, "AtlasVoice");

        if (savedInstanceState != null) web.restoreState(savedInstanceState);
        else {
            String imp = getIntent().getStringExtra("import");
            web.loadUrl("https://" + HOST + "/index.html" + (imp != null && imp.matches("[A-Za-z0-9_]{1,40}") ? "?import=" + imp : ""));
        }
        hideSystemBars();
    }

    private static WebResourceResponse notFound() {
        return new WebResourceResponse("text/plain", "utf-8", 404, "Not Found", null, null);
    }

    private static String mime(String p) {
        String l = p.toLowerCase();
        if (l.endsWith(".html")) return "text/html";
        if (l.endsWith(".js") || l.endsWith(".mjs")) return "application/javascript";
        if (l.endsWith(".css")) return "text/css";
        if (l.endsWith(".png")) return "image/png";
        if (l.endsWith(".svg")) return "image/svg+xml";
        if (l.endsWith(".json")) return "application/json";
        if (l.endsWith(".txt")) return "text/plain";
        if (l.endsWith(".woff2")) return "font/woff2";
        if (l.endsWith(".woff")) return "font/woff";
        return "application/octet-stream";
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == REQ_FILE && fileCallback != null) {
            Uri[] result = null;
            if (resultCode == RESULT_OK && data != null && data.getData() != null) result = new Uri[]{data.getData()};
            fileCallback.onReceiveValue(result);
            fileCallback = null;
        }
    }

    @Override
    public void onBackPressed() {
        if (web == null) { finish(); return; }
        web.evaluateJavascript("(window.trazoBack && window.trazoBack()) ? '1' : '0'", new BackResult(this));
    }

    @Override
    protected void onPause() {
        if (web != null) web.evaluateJavascript("window.trazoFlush && window.trazoFlush()", null);
        super.onPause();
    }

    @Override
    protected void onResume() {
        super.onResume();
        hideSystemBars();
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) hideSystemBars();
    }

    @Override
    protected void onSaveInstanceState(Bundle out) {
        super.onSaveInstanceState(out);
        if (web != null) web.saveState(out);
    }

    // ---- Micrófono y cámara para la página (grabar voz, hacer fotos y vídeos) ----
    private PermissionRequest pendingPerm;
    private Voice voice;
    private static final int REQ_MEDIA = 43;

    void askMedia(PermissionRequest req) {
        List<String> need = new ArrayList<>();
        for (String r : req.getResources()) {
            if (PermissionRequest.RESOURCE_AUDIO_CAPTURE.equals(r)) need.add(Manifest.permission.RECORD_AUDIO);
            if (PermissionRequest.RESOURCE_VIDEO_CAPTURE.equals(r)) need.add(Manifest.permission.CAMERA);
        }
        List<String> missing = new ArrayList<>();
        for (String n : need) if (checkSelfPermission(n) != PackageManager.PERMISSION_GRANTED) missing.add(n);
        if (missing.isEmpty()) { grantAllowed(req); return; }
        if (pendingPerm != null) pendingPerm.deny();
        pendingPerm = req;
        requestPermissions(missing.toArray(new String[0]), REQ_MEDIA);
    }

    private void grantAllowed(PermissionRequest req) {
        List<String> ok = new ArrayList<>();
        for (String r : req.getResources()) {
            if (PermissionRequest.RESOURCE_AUDIO_CAPTURE.equals(r) && checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) ok.add(r);
            if (PermissionRequest.RESOURCE_VIDEO_CAPTURE.equals(r) && checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) ok.add(r);
        }
        if (ok.isEmpty()) req.deny(); else req.grant(ok.toArray(new String[0]));
    }

    @Override
    public void onRequestPermissionsResult(int code, String[] perms, int[] results) {
        super.onRequestPermissionsResult(code, perms, results);
        if (code == REQ_MEDIA && pendingPerm != null) { grantAllowed(pendingPerm); pendingPerm = null; }
    }

    // ---- Texto a voz: la voz de Android se guarda como audio para la animación ----
    static final class Voice {
        private final Atlas2DActivity act;
        private TextToSpeech tts;
        private boolean ready;

        Voice(Atlas2DActivity act) {
            this.act = act;
            tts = new TextToSpeech(act, status -> ready = status == TextToSpeech.SUCCESS);
            tts.setOnUtteranceProgressListener(new UtteranceProgressListener() {
                @Override public void onStart(String id) { }
                @Override public void onDone(String id) { send(id, true); }
                @Override public void onError(String id) { send(id, false); }
            });
        }

        @JavascriptInterface
        public boolean available() { return ready; }

        @JavascriptInterface
        public boolean speak(String id, String text, float pitch, float rate, String lang) {
            if (!ready || id == null || !id.matches("[A-Za-z0-9_]{1,40}")) return false;
            try {
                Locale loc = lang != null && lang.length() > 0 ? Locale.forLanguageTag(lang) : Locale.getDefault();
                if (tts.isLanguageAvailable(loc) >= TextToSpeech.LANG_AVAILABLE) tts.setLanguage(loc);
                tts.setPitch(Math.max(0.3f, Math.min(pitch, 3f)));
                tts.setSpeechRate(Math.max(0.3f, Math.min(rate, 3f)));
                File out = new File(act.getCacheDir(), "voz_" + id + ".wav");
                Bundle b = new Bundle();
                return tts.synthesizeToFile(text, b, out, id) == TextToSpeech.SUCCESS;
            } catch (Exception e) {
                return false;
            }
        }

        private void send(String id, boolean ok) {
            String b64 = "";
            File f = new File(act.getCacheDir(), "voz_" + id + ".wav");
            if (ok) {
                try {
                    byte[] data = new byte[(int) f.length()];
                    try (java.io.FileInputStream in = new java.io.FileInputStream(f)) {
                        int n = 0;
                        while (n < data.length) { int r = in.read(data, n, data.length - n); if (r < 0) break; n += r; }
                    }
                    b64 = Base64.encodeToString(data, Base64.NO_WRAP);
                } catch (Exception e) { b64 = ""; }
            }
            if (f.exists() && !f.delete()) f.deleteOnExit();
            final String js = "window.__voiceDone && window.__voiceDone('" + id + "','" + b64 + "')";
            act.runOnUiThread(() -> { if (act.web != null) act.web.evaluateJavascript(js, null); });
        }

        void shutdown() { try { tts.shutdown(); } catch (Exception ignored) { } }
    }

    @Override
    protected void onDestroy() {
        if (web != null) {
            web.removeJavascriptInterface("AtlasVoice");
            if (voice != null) voice.shutdown();
            web.removeJavascriptInterface("AndroidBridge");
            web.destroy();
            web = null;
        }
        super.onDestroy();
    }

    @SuppressWarnings("deprecation")
    private void hideSystemBars() {
        View d = getWindow().getDecorView();
        d.setSystemUiVisibility(View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                | View.SYSTEM_UI_FLAG_FULLSCREEN
                | View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
    }

    /** Abre el selector de audio del sistema para la página. */
    boolean openAudioPicker(ValueCallback<Uri[]> cb) {
        return openPicker(cb, "audio/*");
    }

    /** Abre el selector de archivos del sistema con el tipo pedido por la página. */
    boolean openPicker(ValueCallback<Uri[]> cb, String type) {
        if (fileCallback != null) fileCallback.onReceiveValue(null);
        fileCallback = cb;
        try {
            Intent i = new Intent(Intent.ACTION_GET_CONTENT);
            i.addCategory(Intent.CATEGORY_OPENABLE);
            i.setType(type);
            String title = type.startsWith("image") ? "Elegir imagen" : type.startsWith("audio") ? "Elegir audio" : "Elegir archivo";
            startActivityForResult(Intent.createChooser(i, title), REQ_FILE);
            return true;
        } catch (Exception e) {
            fileCallback = null;
            return false;
        }
    }

    /** Sirve la interfaz desde la carpeta copiada por ATLAS, en un origen seguro local. */
    static final class LocalClient extends WebViewClient {
        private final File root;

        LocalClient(File root) {
            this.root = root;
        }

        @Override
        public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest req) {
            Uri u = req.getUrl();
            if (!HOST.equals(u.getHost())) return null;
            String path = u.getPath();
            if (path == null || path.isEmpty() || path.equals("/")) path = "/index.html";
            if (path.contains("..")) return notFound();
            try {
                File f = new File(root, path.substring(1));
                if (!f.getCanonicalPath().startsWith(root.getCanonicalPath() + File.separator) || !f.isFile()) return notFound();
                InputStream in = new java.io.FileInputStream(f);
                WebResourceResponse r = new WebResourceResponse(mime(path), "utf-8", in);
                Map<String, String> h = new HashMap<>();
                h.put("Cache-Control", "no-cache");
                r.setResponseHeaders(h);
                return r;
            } catch (IOException e) {
                return notFound();
            }
        }

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest req) {
            return !HOST.equals(req.getUrl().getHost());
        }
    }

    static final class Chrome extends WebChromeClient {
        private final Atlas2DActivity act;

        Chrome(Atlas2DActivity act) {
            this.act = act;
        }

        @Override
        public void onPermissionRequest(PermissionRequest request) {
            act.runOnUiThread(() -> act.askMedia(request));
        }

        @Override
        public boolean onShowFileChooser(WebView v, ValueCallback<Uri[]> cb, FileChooserParams params) {
            // Elige el tipo de archivo según lo que pide la página (accept="...")
            String type = "*/*";
            String[] acc = params != null ? params.getAcceptTypes() : null;
            String joined = acc == null ? "" : TextUtils.join(",", acc);
            if (joined.contains("image/")) type = "image/*";
            else if (joined.contains("audio/")) type = "audio/*";
            else if (joined.contains("video/")) type = "video/*";
            return act.openPicker(cb, type);
        }
    }

    static final class BackResult implements ValueCallback<String> {
        private final Activity act;

        BackResult(Activity act) {
            this.act = act;
        }

        @Override
        public void onReceiveValue(String value) {
            if (!"\"1\"".equals(value)) act.finish();
        }
    }

    private static final class Pending {
        Uri uri;          // Android 10+: entrada de MediaStore
        File file;        // Android 7-9: archivo en la carpeta pública
        OutputStream os;
        String where;
    }

    /**
     * Guardado de archivos exportados en la galería / Descargas.
     * Android 10+: MediaStore (sin permisos). Android 7-9: carpeta pública con permiso de almacenamiento.
     */
    static final class Bridge {
        private final Activity act;

        Bridge(Activity act) {
            this.act = act;
        }

        private final Map<String, Pending> files = new HashMap<>();

        private static String folderFor(String mime) {
            return mime.startsWith("video/") ? Environment.DIRECTORY_MOVIES
                    : mime.startsWith("image/") ? Environment.DIRECTORY_PICTURES
                    : Environment.DIRECTORY_DOWNLOADS;
        }

        @JavascriptInterface
        public synchronized String beginFile(String name, String mime) {
            try {
                Pending p = new Pending();
                String base = folderFor(mime);
                if (Build.VERSION.SDK_INT >= 29) {
                    ContentResolver cr = act.getContentResolver();
                    ContentValues v = new ContentValues();
                    Uri collection;
                    if (mime.startsWith("video/")) collection = MediaStore.Video.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY);
                    else if (mime.startsWith("image/")) collection = MediaStore.Images.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY);
                    else collection = MediaStore.Downloads.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY);
                    v.put(MediaStore.MediaColumns.DISPLAY_NAME, name);
                    v.put(MediaStore.MediaColumns.MIME_TYPE, mime);
                    v.put(MediaStore.MediaColumns.RELATIVE_PATH, base + "/Artlas 2D");
                    v.put(MediaStore.MediaColumns.IS_PENDING, 1);
                    Uri uri = cr.insert(collection, v);
                    if (uri == null) return "ERROR: no se pudo crear el archivo";
                    OutputStream os = cr.openOutputStream(uri);
                    if (os == null) return "ERROR: no se pudo abrir el archivo";
                    p.uri = uri;
                    p.os = os;
                } else {
                    if (act.checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) != PackageManager.PERMISSION_GRANTED) {
                        act.runOnUiThread(new PermissionAsk(act));
                        return "ERROR: Artlas 2D necesita permiso de almacenamiento para guardar. Acéptalo y vuelve a exportar.";
                    }
                    File dir = new File(Environment.getExternalStoragePublicDirectory(base), "Artlas 2D");
                    if (!dir.isDirectory() && !dir.mkdirs()) return "ERROR: no se pudo crear la carpeta " + dir.getPath();
                    File f = new File(dir, name);
                    int n = 1;
                    String stem = name.contains(".") ? name.substring(0, name.lastIndexOf('.')) : name;
                    String ext = name.contains(".") ? name.substring(name.lastIndexOf('.')) : "";
                    while (f.exists()) f = new File(dir, stem + " (" + (n++) + ")" + ext);
                    p.file = f;
                    p.os = new FileOutputStream(f);
                    name = f.getName();
                }
                p.where = base + "/Artlas 2D/" + name;
                String tok = UUID.randomUUID().toString();
                files.put(tok, p);
                return tok;
            } catch (Exception e) {
                return "ERROR: " + e.getMessage();
            }
        }

        @JavascriptInterface
        public synchronized boolean appendFile(String tok, String b64) {
            Pending p = files.get(tok);
            if (p == null) return false;
            try {
                p.os.write(Base64.decode(b64, Base64.DEFAULT));
                return true;
            } catch (Exception e) {
                return false;
            }
        }

        @JavascriptInterface
        public synchronized String finishFile(String tok) {
            Pending p = files.remove(tok);
            if (p == null) return "ERROR: archivo desconocido";
            try {
                p.os.flush();
                p.os.close();
                if (p.uri != null) {
                    ContentValues v = new ContentValues();
                    v.put(MediaStore.MediaColumns.IS_PENDING, 0);
                    act.getContentResolver().update(p.uri, v, null, null);
                } else if (p.file != null) {
                    // que aparezca enseguida en la galería
                    MediaScannerConnection.scanFile(act, new String[]{p.file.getAbsolutePath()}, null, null);
                }
                return "Guardado en " + p.where;
            } catch (Exception e) {
                discard(p);
                return "ERROR: " + e.getMessage();
            }
        }

        @JavascriptInterface
        public synchronized void abortFile(String tok) {
            Pending p = files.remove(tok);
            if (p == null) return;
            try { p.os.close(); } catch (Exception ignored) { }
            discard(p);
        }

        private void discard(Pending p) {
            try {
                if (p.uri != null) act.getContentResolver().delete(p.uri, null, null);
                else if (p.file != null && p.file.exists() && !p.file.delete()) p.file.deleteOnExit();
            } catch (Exception ignored) { }
        }
    }

    static final class PermissionAsk implements Runnable {
        private final Activity act;

        PermissionAsk(Activity act) {
            this.act = act;
        }

        @Override
        public void run() {
            act.requestPermissions(new String[]{Manifest.permission.WRITE_EXTERNAL_STORAGE}, 7);
        }
    }
}
