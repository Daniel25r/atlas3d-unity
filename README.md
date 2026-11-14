# ATLAS 3D (Unity)

Versión de ATLAS 3D hecha con Unity 6 (URP), empezando por Android.

- `Assets/Atlas/Scripts/Runtime`: la app.
- `Assets/Atlas/Scripts/Editor/AtlasBuild.cs`: prepara el proyecto (render URP,
  materiales, escena, ajustes de Android) y crea el APK.
- `.github/workflows/android.yml`: crea el APK en la nube (GameCI) en cada
  cambio de `main`. Necesita los secretos del repositorio `UNITY_LICENSE`,
  `UNITY_EMAIL` y `UNITY_PASSWORD`. El APK aparece en la pestaña *Actions*,
  en la ejecución, como artefacto `ATLAS3D-Android`.

En Unity: menú **ATLAS → Build Android APK**.
