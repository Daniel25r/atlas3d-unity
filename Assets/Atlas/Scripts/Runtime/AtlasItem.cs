// ATLAS 3D (Unity) - marks a scene object made by the user.
using System;
using System.Collections.Generic;
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
        /// Animation: keys sorted by frame (position, rotation, scale).
        public List<KeyData> keys = new List<KeyData>();

        public bool Animated { get { return keys.Count > 0; } }

        /// Stores the object's current transform as a key at `frame`.
        public void SetKey(int frame)
        {
            var k = new KeyData { frame = frame, position = transform.position, rotation = transform.rotation, scale = transform.localScale };
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].frame == frame) { keys[i] = k; return; }
                if (keys[i].frame > frame) { keys.Insert(i, k); return; }
            }
            keys.Add(k);
        }

        public bool RemoveKey(int frame)
        {
            return keys.RemoveAll(k => k.frame == frame) > 0;
        }

        public bool HasKey(int frame)
        {
            foreach (var k in keys) if (k.frame == frame) return true;
            return false;
        }

        /// Puts the object where its keys say at `frame` (smooth in and out
        /// between keys, held before the first and after the last).
        public void Evaluate(float frame)
        {
            if (keys.Count == 0) return;
            KeyData a = keys[0], b = keys[keys.Count - 1];
            if (frame <= a.frame) { Apply(a); return; }
            if (frame >= b.frame) { Apply(b); return; }
            for (int i = 0; i < keys.Count - 1; i++)
            {
                if (frame >= keys[i].frame && frame <= keys[i + 1].frame)
                {
                    a = keys[i];
                    b = keys[i + 1];
                    break;
                }
            }
            float t = Mathf.InverseLerp(a.frame, b.frame, frame);
            t = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(a.position, b.position, t);
            transform.rotation = Quaternion.Slerp(a.rotation, b.rotation, t);
            transform.localScale = Vector3.Lerp(a.scale, b.scale, t);
        }

        void Apply(KeyData k)
        {
            transform.position = k.position;
            transform.rotation = k.rotation;
            transform.localScale = k.scale;
        }

        void OnDestroy()
        {
            if (importer != null)
            {
                importer.Dispose();
                importer = null;
            }
        }
    }

    [Serializable]
    public class KeyData
    {
        public int frame;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 scale = Vector3.one;
    }
}
