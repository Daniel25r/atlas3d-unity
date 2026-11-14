// ATLAS 3D (Unity) - cloud build entry point.
// GameCI calls AtlasBuild.BuildAndroid in batch mode. Everything the build
// needs (render pipeline asset, materials, the main scene, player settings)
// is created here from code, so the repository only holds scripts and the
// build is the same on every machine.
using System;
using System.IO;
using UnityEditor;
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
}
