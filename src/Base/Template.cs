using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

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
        public string fill;     // what the piece holds when it is made (the seeds of a vegetube): group ids separated by commas
        public string from;     // growth: the panels the piece must still have for the change to be made
        public string needs;    // growth: the group that must be unlocked first
        public string op;       // growth: "add" (make the piece), "remove" (take the piece away), empty = change a pod's panels
        public int count;       // growth, add: how many of the piece stand on the spot when done (a stack); 0 or 1 = one
        public bool all;        // growth, remove: take every piece of the group(s) on the spot, not just one (a stack)
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
                    _template = Parse(reader.ReadToEnd());
            }

            if (_template == null || _template.pieces == null || _template.pieces.Length == 0)
                throw new InvalidOperationException("T0.json holds no pieces.");

            return _template;
        }

        // The file is flat (a list of objects of strings and numbers), so it is read directly: Unity's JsonUtility returned no pieces for it in the game.
        private static readonly Regex PieceText = new Regex(@"\{[^{}]*\}", RegexOptions.Singleline);
        private static readonly Regex Pair = new Regex(@"""(\w+)""\s*:\s*(""(?:[^""\\]|\\.)*""|-?[0-9.eE+\-]+|true|false)");

        internal static Template Parse(string json)
        {
            var template = new Template { name = "T0" };
            var start = json.IndexOf("\"pieces\"", StringComparison.Ordinal);
            var pieces = new List<TemplatePiece>();
            if (start >= 0)
            {
                foreach (Match m in PieceText.Matches(json.Substring(start)))
                {
                    var piece = new TemplatePiece();
                    foreach (Match pair in Pair.Matches(m.Value))
                    {
                        var value = pair.Groups[2].Value;
                        var text = value.StartsWith("\"") ? Unescape(value.Substring(1, value.Length - 2)) : null;
                        switch (pair.Groups[1].Value)
                        {
                            case "g": piece.g = text; break;
                            case "rot": piece.rot = text; break;
                            case "text": piece.text = text; break;
                            case "groups": piece.groups = text; break;
                            case "panels": piece.panels = text; break;
                            case "color": piece.color = text; break;
                            case "fill": piece.fill = text; break;
                            case "from": piece.from = text; break;
                            case "needs": piece.needs = text; break;
                            case "op": piece.op = text; break;
                            case "count": piece.count = int.Parse(value, CultureInfo.InvariantCulture); break;
                            case "all": piece.all = value == "true"; break;
                            case "x": piece.x = float.Parse(value, CultureInfo.InvariantCulture); break;
                            case "y": piece.y = float.Parse(value, CultureInfo.InvariantCulture); break;
                            case "z": piece.z = float.Parse(value, CultureInfo.InvariantCulture); break;
                        }
                    }

                    if (!string.IsNullOrEmpty(piece.g))
                        pieces.Add(piece);
                }
            }

            template.pieces = pieces.ToArray();
            return template;
        }

        private static string Unescape(string s)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(s[i]);
                    continue;
                }

                var c = s[++i];
                if (c == 'u' && i + 4 < s.Length)
                {
                    sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16));
                    i += 4;
                }
                else
                {
                    sb.Append(c == 'n' ? (char)10 : c == 't' ? (char)9 : c);
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>What was added to the base after T0 (tools/capture-growth.ps1): entries that are made when the group they need is unlocked. May be empty.</summary>
    internal static class Growth
    {
        private static Template _template;

        public static Template Get()
        {
            if (_template != null)
                return _template;

            using (var stream = typeof(Growth).Assembly.GetManifestResourceStream("Growth.json"))
            {
                if (stream == null)
                    throw new InvalidOperationException("Growth.json is not built into this DLL.");

                using (var reader = new StreamReader(stream))
                    _template = T0.Parse(reader.ReadToEnd());
            }

            return _template;
        }
    }
}
