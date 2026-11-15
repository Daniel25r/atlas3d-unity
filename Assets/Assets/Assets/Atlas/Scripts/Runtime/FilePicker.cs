// ATLAS 3D (Unity) - choosing and saving files on every platform. Phones
// use the NativeFilePicker plugin; Windows uses its own «Open» and «Save
// as» windows (comdlg32). The rest of the app calls it the same way.
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Atlas
{
    public static class NativeFilePicker
    {
        public delegate void FilePickedCallback(string path);
        public delegate void MultipleFilesPickedCallback(string[] paths);
        public delegate void FilesExportedCallback(bool success);

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        public static bool IsFilePickerBusy() { return busy; }
        public static bool CanPickMultipleFiles() { return true; }

        public static void PickFile(FilePickedCallback cb, params string[] types)
        {
            var r = Open(types, false);
            if (cb != null) cb(r != null && r.Length > 0 ? r[0] : null);
        }

        public static void PickMultipleFiles(MultipleFilesPickedCallback cb, params string[] types)
        {
            var r = Open(types, true);
            if (cb != null) cb(r);
        }

        /// «Save as…» with the file's name; the file is copied there.
        public static void ExportFile(string path, FilesExportedCallback cb)
        {
            bool ok = false;
            try
            {
                string ext = Path.GetExtension(path);
                string dest = Save(Path.GetFileName(path), ext);
                if (!string.IsNullOrEmpty(dest))
                {
                    if (!string.Equals(Path.GetFullPath(dest), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) File.Copy(path, dest, true);
                    ok = true;
                }
            }
            catch (Exception e) { Debug.LogWarning("ATLAS: guardar: " + e.Message); }
            if (cb != null) cb(ok);
        }

        static bool busy;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        class OpenFileName
        {
            public int lStructSize = Marshal.SizeOf(typeof(OpenFileName));
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetOpenFileNameW([In, Out] OpenFileName ofn);
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetSaveFileNameW([In, Out] OpenFileName ofn);
        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        const int OFN_OVERWRITEPROMPT = 0x2, OFN_NOCHANGEDIR = 0x8, OFN_ALLOWMULTISELECT = 0x200, OFN_PATHMUSTEXIST = 0x800, OFN_FILEMUSTEXIST = 0x1000, OFN_EXPLORER = 0x80000;
        const int BufChars = 1 << 16;

        static string Filter(string[] types)
        {
            string t = types != null && types.Length > 0 ? types[0] : "*/*";
            if (t.StartsWith("image")) return Lang.T("Imágenes") + "\0*.png;*.jpg;*.jpeg;*.tga;*.bmp\0" + Lang.T("Todos los archivos") + "\0*.*\0\0";
            if (t.StartsWith("audio")) return "Audio\0*.mp3;*.ogg;*.wav\0" + Lang.T("Todos los archivos") + "\0*.*\0\0";
            if (t.StartsWith("video")) return Lang.T("Vídeo") + "\0*.mp4;*.webm;*.mov\0" + Lang.T("Todos los archivos") + "\0*.*\0\0";
            return Lang.T("Modelos y proyectos") + "\0*.glb;*.gltf;*.fbx;*.obj;*.blend;*.3ds;*.stl;*.ply;*.zip;*.prisma;*.proj;*.pobject\0" + Lang.T("Todos los archivos") + "\0*.*\0\0";
        }

        static string[] Open(string[] types, bool multi)
        {
            busy = true;
            IntPtr buf = Marshal.AllocHGlobal(BufChars * 2);
            try
            {
                for (int i = 0; i < BufChars * 2; i += 8) Marshal.WriteInt64(buf, i, 0);
                var o = new OpenFileName
                {
                    hwndOwner = GetActiveWindow(),
                    lpstrFilter = Filter(types),
                    lpstrFile = buf,
                    nMaxFile = BufChars,
                    lpstrTitle = Lang.T("Abrir"),
                    Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | (multi ? OFN_ALLOWMULTISELECT : 0),
                };
                if (!GetOpenFileNameW(o)) return null;
                // «folder\0file1\0file2\0\0» (several) or «full path\0\0» (one).
                var parts = new System.Collections.Generic.List<string>();
                int at = 0;
                while (true)
                {
                    string s = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + at * 2));
                    if (string.IsNullOrEmpty(s)) break;
                    parts.Add(s);
                    at += s.Length + 1;
                }
                if (parts.Count == 0) return null;
                if (parts.Count == 1) return new[] { parts[0] };
                var r = new string[parts.Count - 1];
                for (int i = 1; i < parts.Count; i++) r[i - 1] = Path.Combine(parts[0], parts[i]);
                return r;
            }
            catch (Exception e)
            {
                Debug.LogWarning("ATLAS: abrir: " + e.Message);
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
                busy = false;
            }
        }

        static string Save(string name, string ext)
        {
            busy = true;
            IntPtr buf = Marshal.AllocHGlobal(BufChars * 2);
            try
            {
                for (int i = 0; i < BufChars * 2; i += 8) Marshal.WriteInt64(buf, i, 0);
                var chars = (name ?? "archivo").ToCharArray();
                Marshal.Copy(chars, 0, buf, Math.Min(chars.Length, BufChars - 1));
                string e = string.IsNullOrEmpty(ext) ? "*" : ext.TrimStart('.');
                var o = new OpenFileName
                {
                    hwndOwner = GetActiveWindow(),
                    lpstrFilter = e.ToUpperInvariant() + "\0*." + e + "\0" + Lang.T("Todos los archivos") + "\0*.*\0\0",
                    lpstrFile = buf,
                    nMaxFile = BufChars,
                    lpstrDefExt = e,
                    lpstrTitle = Lang.T("Guardar"),
                    lpstrInitialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Flags = OFN_EXPLORER | OFN_OVERWRITEPROMPT | OFN_NOCHANGEDIR | OFN_PATHMUSTEXIST,
                };
                if (!GetSaveFileNameW(o)) return null;
                return Marshal.PtrToStringUni(buf);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("ATLAS: guardar: " + ex.Message);
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
                busy = false;
            }
        }
#else
        public static bool IsFilePickerBusy() { return global::NativeFilePicker.IsFilePickerBusy(); }
        public static bool CanPickMultipleFiles() { return global::NativeFilePicker.CanPickMultipleFiles(); }

        public static void PickFile(FilePickedCallback cb, params string[] types)
        {
            global::NativeFilePicker.PickFile(p => { if (cb != null) cb(p); }, types);
        }

        public static void PickMultipleFiles(MultipleFilesPickedCallback cb, params string[] types)
        {
            global::NativeFilePicker.PickMultipleFiles(p => { if (cb != null) cb(p); }, types);
        }

        public static void ExportFile(string path, FilesExportedCallback cb)
        {
            global::NativeFilePicker.ExportFile(path, ok => { if (cb != null) cb(ok); });
        }
#endif
    }
}
