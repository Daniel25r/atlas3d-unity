// ATLAS 5D (Unity) - the name in the PC window's title bar. Unity writes the
// product name there, which stays «ATLAS 3D» so the projects folder keeps
// its place; the title is changed by hand on Windows.
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Atlas
{
    public static class WindowTitle
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetWindowTextW(IntPtr hWnd, string text);
#endif

        public static void Set(string title)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                var h = GetActiveWindow();
                if (h != IntPtr.Zero) SetWindowTextW(h, title);
            }
            catch (Exception e) { Debug.LogWarning("ATLAS: título de la ventana: " + e.Message); }
#endif
        }
    }
}
