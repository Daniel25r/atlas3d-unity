#if UNITY_ANDROID
// ATLAS 3D (Unity) - puts ATLAS in Android's "Open with" and "Share" lists
// (Prisma3D, the file manager, Drive, WhatsApp…): adds the AtlasOpenFile
// activity (Assets/Plugins/Android/AtlasOpenFile.java) to the generated
// Gradle project's manifest.
using System.IO;
using UnityEditor.Android;
using UnityEngine;

public class AtlasManifest : IPostGenerateGradleAndroidProject
{
    public int callbackOrder { get { return 10; } }

    const string Activity = @"
        <activity android:name=""org.atlas3d.AtlasOpenFile""
            android:exported=""true""
            android:label=""ATLAS 3D""
            android:theme=""@android:style/Theme.Translucent.NoTitleBar""
            android:excludeFromRecents=""true""
            android:noHistory=""true"">
            <intent-filter>
                <action android:name=""android.intent.action.SEND"" />
                <action android:name=""android.intent.action.SEND_MULTIPLE"" />
                <category android:name=""android.intent.category.DEFAULT"" />
                <data android:mimeType=""application/*"" />
                <data android:mimeType=""model/*"" />
                <data android:mimeType=""*/*"" />
            </intent-filter>
            <intent-filter>
                <action android:name=""android.intent.action.VIEW"" />
                <category android:name=""android.intent.category.DEFAULT"" />
                <category android:name=""android.intent.category.BROWSABLE"" />
                <data android:scheme=""content"" />
                <data android:scheme=""file"" />
                <data android:mimeType=""application/*"" />
                <data android:mimeType=""model/*"" />
            </intent-filter>
        </activity>
";

    // «Animación 2D» (Artlas 2D in a WebView), landscape like the app.
    const string Activity2D = @"
        <activity android:name=""org.atlas3d.Atlas2DActivity""
            android:exported=""false""
            android:label=""ATLAS 2D""
            android:theme=""@android:style/Theme.Black.NoTitleBar.Fullscreen""
            android:hardwareAccelerated=""true""
            android:screenOrientation=""sensorLandscape""
            android:windowSoftInputMode=""adjustResize""
            android:configChanges=""orientation|screenSize|screenLayout|smallestScreenSize|keyboardHidden|keyboard|navigation|uiMode|density"" />
";

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifest))
        {
            Debug.LogWarning("ATLAS: no se encontró " + manifest);
            return;
        }
        string xml = File.ReadAllText(manifest);
        if (!xml.Contains("org.atlas3d.Atlas2DActivity"))
        {
            int at2 = xml.LastIndexOf("</application>");
            if (at2 >= 0)
            {
                xml = xml.Insert(at2, Activity2D);
                File.WriteAllText(manifest, xml);
                Debug.Log("ATLAS: modo 2D añadido al manifiesto");
            }
        }
        if (xml.Contains("org.atlas3d.AtlasOpenFile")) return;
        int at = xml.LastIndexOf("</application>");
        if (at < 0)
        {
            Debug.LogWarning("ATLAS: el manifiesto no tiene <application>");
            return;
        }
        if (!xml.Contains("xmlns:android")) Debug.LogWarning("ATLAS: el manifiesto no declara xmlns:android");
        xml = xml.Insert(at, Activity);
        // Uninstalling asks whether to keep the projects.
        int app = xml.IndexOf("<application");
        if (app >= 0 && !xml.Contains("hasFragileUserData"))
            xml = xml.Insert(app + "<application".Length, " android:hasFragileUserData=\"true\"");
        File.WriteAllText(manifest, xml);
        Debug.Log("ATLAS: «Abrir con / Compartir» añadido al manifiesto");
    }
}
#endif
