// ATLAS 3D (Unity) - marks a scene object made by the user.
using System;
using UnityEngine;

namespace Atlas
{
    /// One object of the scene (on the root GameObject of each shape,
    /// light or imported model).
    public class AtlasItem : MonoBehaviour
    {
        public string id;
        public string kind;          // cube | sphere | cylinder | capsule | plane | light | model
        public Color color = Color.white;
        public string modelFile;     // models: file name inside the project's «models» folder
        public IDisposable importer; // keeps the imported model's resources alive

        void OnDestroy()
        {
            if (importer != null)
            {
                importer.Dispose();
                importer = null;
            }
        }
    }
}
