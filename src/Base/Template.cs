using System;
using System.IO;
using UnityEngine;

namespace RRSOS.PCC.Base
{
    // Unity fills these fields itself when it reads T0.json, so the compiler never sees them assigned.
#pragma warning disable CS0649
    [Serializable]
    internal sealed class TemplatePiece
    {
        public string g;        // the game's group id
        public float x, y, z;   // metres from the anchor foundation, in world axes (north is +x)
        public string rot;      // the game's own quaternion text, "x,y,z,w"
        public string text;     // a crate's label
        public string groups;   // the groups the game linked to the object
        public string panels;   // a pod's panel types
        public string color;    // the game's own colour text
    }

    [Serializable]
    internal sealed class Template
    {
        public string name;
        public float[] anchor;  // where the anchor foundation stood in the save it was captured from (for the record: the build uses the planet's own anchor)
        public TemplatePiece[] pieces;
    }
#pragma warning restore CS0649

    /// <summary>The T0 base, captured from the owner's save by tools/capture-t0.py and built into this DLL.</summary>
    internal static class T0
    {
        private static Template _template;

        public static Template Get()
        {
            if (_template != null)
                return _template;

            using (var stream = typeof(T0).Assembly.GetManifestResourceStream("T0.json"))
            {
                if (stream == null)
                    throw new InvalidOperationException("T0.json is not built into this DLL.");

                using (var reader = new StreamReader(stream))
                    _template = JsonUtility.FromJson<Template>(reader.ReadToEnd());
            }

            if (_template == null || _template.pieces == null || _template.pieces.Length == 0)
                throw new InvalidOperationException("T0.json holds no pieces.");

            return _template;
        }
    }
}
