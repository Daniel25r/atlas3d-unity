// ATLAS 3D - "Open with" / "Share" target: lets other apps (Prisma3D, the
// file manager, WhatsApp, Drive…) send models to ATLAS. The files are copied
// into the app's own folder (incoming/<time>/) and ATLAS is brought to the
// front; the app imports them as soon as it sees them.
package org.atlas3d;

import android.app.Activity;
import android.content.ClipData;
import android.content.Context;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;

public class AtlasOpenFile extends Activity {
    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        try {
            receive(getIntent());
        } catch (Throwable t) {
            // Nothing to import; ATLAS still opens.
        }
        Intent main = getPackageManager().getLaunchIntentForPackage(getPackageName());
        if (main != null) {
            main.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_REORDER_TO_FRONT);
            startActivity(main);
        }
        finish();
    }

    void receive(Intent intent) throws Exception {
        if (intent == null) return;
        ArrayList<Uri> uris = new ArrayList<Uri>();
        String action = intent.getAction();
        if (Intent.ACTION_VIEW.equals(action) && intent.getData() != null) uris.add(intent.getData());
        if (Intent.ACTION_SEND.equals(action)) {
            Uri u = intent.getParcelableExtra(Intent.EXTRA_STREAM);
            if (u != null) uris.add(u);
        }
        if (Intent.ACTION_SEND_MULTIPLE.equals(action)) {
            ArrayList<Uri> list = intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM);
            if (list != null) uris.addAll(list);
        }
        if (uris.isEmpty() && intent.getClipData() != null) {
            ClipData c = intent.getClipData();
            for (int i = 0; i < c.getItemCount(); i++) if (c.getItemAt(i).getUri() != null) uris.add(c.getItemAt(i).getUri());
        }
        if (uris.isEmpty()) return;
        File batch = new File(incomingDir(this), "tmp_" + System.currentTimeMillis());
        batch.mkdirs();
        int n = 0;
        for (Uri u : uris) {
            String name = displayName(u);
            if (name == null || name.length() == 0) name = "archivo_" + n;
            name = name.replace('/', '_').replace('\\', '_');
            File out = new File(batch, name);
            InputStream in = getContentResolver().openInputStream(u);
            if (in == null) continue;
            OutputStream o = new FileOutputStream(out);
            byte[] buf = new byte[65536];
            int r;
            while ((r = in.read(buf)) > 0) o.write(buf, 0, r);
            o.close();
            in.close();
            n++;
        }
        // Only finished batches are visible to the app.
        if (n > 0) batch.renameTo(new File(incomingDir(this), "b_" + System.currentTimeMillis()));
    }

    String displayName(Uri u) {
        String name = null;
        if ("content".equals(u.getScheme())) {
            Cursor c = null;
            try {
                c = getContentResolver().query(u, new String[] { OpenableColumns.DISPLAY_NAME }, null, null, null);
                if (c != null && c.moveToFirst()) name = c.getString(0);
            } catch (Throwable t) {
            } finally {
                if (c != null) c.close();
            }
        }
        if (name == null) name = u.getLastPathSegment();
        return name;
    }

    /// Where received files wait for the app (also read from Unity).
    public static File incomingDir(Context ctx) {
        File d = new File(ctx.getFilesDir(), "incoming");
        d.mkdirs();
        return d;
    }

    public static String incomingPath(Context ctx) {
        return incomingDir(ctx).getAbsolutePath();
    }
}
