# ATLAS 5D (Unity) - notas para Claude

Léelo al empezar una sesión nueva: aquí está lo que hay que saber del proyecto.

## Cómo trabajamos
- Habla siempre en **español** con el dueño del proyecto.
- Primero arreglar errores, después añadir cosas. Si dice «solo haz eso», haz solo eso.
- Claude **sí tiene permiso para subir** al repositorio (el dueño se lo dio el
  21/11/2026). El ciclo es:
  1. Claude programa en una copia de trabajo, compila contra las DLL de Unity
     (ver «Comprobar sin Unity») y, si hay shaders, los comprueba con glslang.
  2. Claude sube los cambios directamente a `main` (los archivos de Unity siempre
     en `Assets/Atlas/...`; los workflows en `.github/workflows/`) y le cuenta al
     dueño qué subió.
  3. Claude vigila las compilaciones (Actions: Android y Windows) y da los
     enlaces (Artifacts ATLAS3D-Android / ATLAS3D-Windows).
  - Si el dueño prefiere subir él: Claude prepara un ZIP con la carpeta `Atlas`
    y el dueño la arrastra en https://github.com/Daniel25r/atlas3d-unity/upload/main/Assets
    (en la lista debe verse `Assets/Atlas/...`).
- Sube la versión en cada entrega: `Version` en `Scripts/Runtime/AtlasApp.cs`,
  `bundleVersion` en `Scripts/Editor/AtlasBuild.cs` y el texto `WhatsNew` en
  `AtlasApp.Home.cs` (y su traducción en `Resources/Lang.txt`).
- Textos nuevos de la interfaz: añádelos a `Resources/Lang.txt` (español§inglés§árabe).
- Privacidad: no subir los modelos ni archivos del dueño a servicios externos,
  no pedir contraseñas ni claves en el chat; la keystore de Android no va al repositorio.

## El proyecto
- Unity 6000.3.x con URP 17. Compila con GameCI (`AtlasBuild.BuildAndroid` / `BuildWindows`).
- `AtlasBuild.Prepare` crea los assets de URP, los materiales de `Resources` y los efectos de render.
- App de animación 3D para móvil y PC (interfaz de escritorio estilo Blender en PC).
- Código en `Assets/Atlas/Scripts/Runtime` (clase parcial `AtlasApp.*.cs`).
- Importadores en `Scripts/Runtime/Import`, sin tipos de Unity, para probarlos fuera:
  - Prisma3D: `PrismaImport`
  - Source: `SourceMdlImport` (MDL/VVD/VTX, VMT/VTF, GMA) y `PcfImport` (partículas .pcf, DMX binario 2–5)
  - Minecraft: `BlockbenchImport` (.bbmodel y .geo.json)
  - FBX: `FbxImport`
- Escenas: `SceneStore.cs`. `ItemData` se guarda con JsonUtility; los campos nuevos
  hay que copiarlos en `Create` (`AtlasApp.cs`) y en la foto de la escena (`SceneStore`).
- Efectos de partículas: `AtlasApp.Effects.cs` (efectos ya hechos, creador de
  partículas `FxCustom`) y `AtlasApp.Pcf.cs` (efectos .pcf como hijos del objeto).

## Comprobar sin Unity
- Los módulos de Unity activados son solo los de `Packages/manifest.json`
  (androidjni, animation, audio, imageconversion, imgui, jsonserialize,
  particlesystem, physics, ui, uielements, unitywebrequest, unitywebrequestaudio,
  video). No uses clases de otros módulos (por ejemplo `ScreenCapture`): la
  compilación de prueba no debe referenciarlos.
- Compilar contra las DLL de UnityEngine (paquete NuGet con las referencias de
  Unity) con un .csproj que incluya `Assets/Atlas/Scripts/Runtime/**/*.cs`.
- Los importadores se prueban en una app de consola .NET con los archivos de `Import/`.
