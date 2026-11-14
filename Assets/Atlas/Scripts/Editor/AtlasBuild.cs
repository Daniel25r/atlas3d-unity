// ATLAS 3D (Unity) - cloud build entry point.
// GameCI calls AtlasBuild.BuildAndroid in batch mode. Everything the build
// needs (render pipeline asset, materials, the main scene, player settings)
// is created here from code, so the repository only holds scripts and the
// build is the same on every machine.
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class AtlasBuild
{
    const string SettingsDir = "Assets/Atlas/Settings";
    const string ResourcesDir = "Assets/Atlas/Resources";
    const string ScenePath = "Assets/Atlas/Scenes/Main.unity";

    [MenuItem("ATLAS/Build Android APK")]
    public static void BuildAndroid()
    {
        try
        {
            Prepare();
            string path = Arg("-customBuildPath") ?? "build/Android/ATLAS3D.apk";
            if (!path.EndsWith(".apk")) path = Path.Combine(path, "ATLAS3D.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log("ATLAS build: " + report.summary.result + " -> " + path + " (" + report.summary.totalSize + " bytes)");
            if (report.summary.result != BuildResult.Succeeded)
                Exit(1);
            else
                Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("ATLAS build failed: " + e);
            Exit(1);
        }
    }

    static void Exit(int code)
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(code);
    }

    static string Arg(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    /// Render pipeline, materials, scene and player settings.
    public static void Prepare()
    {
        Directory.CreateDirectory(SettingsDir);
        Directory.CreateDirectory(ResourcesDir);
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        AssetDatabase.Refresh();

        // --- Universal Render Pipeline: the «looks good on phones» part.
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(SettingsDir + "/AtlasRenderer.asset");
        if (rendererData == null)
        {
            rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, SettingsDir + "/AtlasRenderer.asset");
        }
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(SettingsDir + "/AtlasURP.asset");
        if (urp == null)
        {
            urp = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(urp, SettingsDir + "/AtlasURP.asset");
        }
        urp.msaaSampleCount = 4;        // smooth edges
        urp.supportsHDR = true;
        urp.renderScale = 1f;           // never pixelated
        urp.shadowDistance = 60f;
        EditorUtility.SetDirty(urp);
        GraphicsSettings.defaultRenderPipeline = urp;
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }
        QualitySettings.SetQualityLevel(current, false);

        // --- Materials the app instantiates at run time (in Resources, so
        // their shaders are always part of the build).
        MakeMaterial("AtlasLit", "Universal Render Pipeline/Lit");
        MakeMaterial("AtlasUnlit", "Universal Render Pipeline/Unlit");

        // --- The one scene: an empty one with the app object.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("ATLAS").AddComponent<Atlas.AtlasApp>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        // --- Android player settings.
        PlayerSettings.companyName = "ATLAS 3D";
        PlayerSettings.productName = "ATLAS 3D";
        // Separate id: installs next to the Godot version while we test.
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "org.atlas3d.unity");
        PlayerSettings.bundleVersion = "3.0.0";
        int run;
        if (int.TryParse(Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER"), out run))
            PlayerSettings.Android.bundleVersionCode = run;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = true;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        EditorUserBuildSettings.buildAppBundle = false;
        Branding();
        CrispIcons();
        IncludeGltfShaders();
        AssetDatabase.SaveAssets();
    }

    static void MakeMaterial(string name, string shaderName)
    {
        string path = ResourcesDir + "/" + name + ".mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        Shader shader = Shader.Find(shaderName);
        if (shader == null) throw new Exception("Shader not found: " + shaderName);
        var m = new Material(shader);
        if (name == "AtlasLit")
        {
            // Keep the emission variant in the build (selection highlight):
            // variants no material uses are stripped from the APK.
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        AssetDatabase.CreateAsset(m, path);
    }

    const string BrandDir = "Assets/Atlas/Branding";

    static Texture2D Tex(string path, bool sprite)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            bool dirty = false;
            var want = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (imp.textureType != want) { imp.textureType = want; dirty = true; }
            if (imp.textureCompression != TextureImporterCompression.Uncompressed) { imp.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
            if (!imp.alphaIsTransparency) { imp.alphaIsTransparency = true; dirty = true; }
            if (dirty) imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// ATLAS's own logo: launcher icon (classic and adaptive) and the
    /// opening screen.
    static void Branding()
    {
        try
        {
            var icon = Tex(BrandDir + "/AppIcon.png", false);
            var fg = Tex(BrandDir + "/AdaptiveForeground.png", false);
            var bg = Tex(BrandDir + "/AdaptiveBackground.png", false);
            if (icon != null)
            {
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
                SetAndroidIcons(AndroidPlatformIconKind.Legacy, new[] { icon });
                SetAndroidIcons(AndroidPlatformIconKind.Round, new[] { icon });
            }
            if (fg != null && bg != null)
                SetAndroidIcons(AndroidPlatformIconKind.Adaptive, new[] { bg, fg });
        }
        catch (Exception e)
        {
            Debug.LogWarning("ATLAS: icono no aplicado: " + e.Message);
        }
        try
        {
            Tex(BrandDir + "/Splash.png", true);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BrandDir + "/Splash.png");
            if (sprite != null)
            {
                PlayerSettings.SplashScreen.show = true;
                PlayerSettings.SplashScreen.backgroundColor = Color.black;
                PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
                PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(1.6f, sprite) };
                PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("ATLAS: pantalla de inicio no aplicada: " + e.Message);
        }
    }

    static void SetAndroidIcons(PlatformIconKind kind, Texture2D[] layers)
    {
        var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
        foreach (var ic in icons)
            ic.SetTextures(layers);
        PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
    }

    /// Interface icons: uncompressed with mipmaps (sharp at any size).
    static void CrispIcons()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Atlas/Resources/Icons" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) continue;
            if (imp.textureCompression == TextureImporterCompression.Uncompressed && imp.mipmapEnabled && imp.alphaIsTransparency) continue;
            imp.textureType = TextureImporterType.Default;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.mipmapEnabled = true;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
    }

    /// glTFast draws imported models with its own shaders: their variant
    /// collections go into the preloaded shaders so they are in the APK.
    static void IncludeGltfShaders()
    {
        try
        {
            var found = new List<ShaderVariantCollection>();
            foreach (string guid in AssetDatabase.FindAssets("t:ShaderVariantCollection", new[] { "Packages/com.unity.cloud.gltfast" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.ToLowerInvariant().Contains("built-in") || path.ToLowerInvariant().Contains("builtin") || path.ToLowerInvariant().Contains("hdrp")) continue;
                var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(path);
                if (svc != null) { found.Add(svc); Debug.Log("ATLAS: shaders de glTF incluidos: " + path); }
            }
            if (found.Count > 0)
            {
                var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
                var so = new SerializedObject(gs);
                var list = so.FindProperty("m_PreloadedShaders");
                foreach (var f in found)
                {
                    bool already = false;
                    for (int i = 0; i < list.arraySize; i++)
                        if (list.GetArrayElementAtIndex(i).objectReferenceValue == f) already = true;
                    if (already) continue;
                    list.InsertArrayElementAtIndex(list.arraySize);
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = f;
                }
                so.ApplyModifiedProperties();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("ATLAS: shaders de glTF no incluidos: " + e.Message);
        }
    }
}
