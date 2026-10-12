using System;
using System.IO;
using System.Linq;
using System.Text;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Commands
{
    /// <summary>
    /// Writes the structure of a few of the game's building prefabs (every child: name, position, size, its components, meshes and colliders) to prefabs.txt in this mod's
    /// data folder, once per start. The assets are not in the game's code, and this is how the terminal's model gets designed from facts instead of guesses.
    /// </summary>
    internal static class PrefabDump
    {
        public static void Write(params string[] groupIds)
        {
            try
            {
                var text = new StringBuilder();
                text.AppendLine("Prefab structure, written " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " by RRSOS PCC Commands " + Plugin.Version);
                foreach (var id in groupIds)
                {
                    var group = GroupsHandler.GetGroupViaId(id);
                    var prefab = group == null ? null : group.GetAssociatedGameObject();
                    text.AppendLine();
                    text.AppendLine("=== " + id + (prefab == null ? ": no prefab" : " (" + prefab.name + ")"));
                    if (prefab != null)
                        Dump(prefab.transform, 0, text);
                }

                var path = ModLog.FileIn("prefabs.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text.ToString());
                ModLog.Note("Prefab structure written to " + path);
            }
            catch (Exception e)
            {
                ModLog.Note("Could not write the prefab structure: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Dump(Transform t, int depth, StringBuilder text)
        {
            var pad = new string(' ', depth * 2);
            var rect = t as RectTransform;
            text.Append(pad).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [inactive]")
                .Append("  pos ").Append(Fmt(t.localPosition)).Append("  rot ").Append(Fmt(t.localEulerAngles)).Append("  scale ").Append(Fmt(t.localScale));
            if (rect != null)
                text.Append("  rect ").Append(rect.sizeDelta.x.ToString("0.##")).Append("x").Append(rect.sizeDelta.y.ToString("0.##"));
            text.AppendLine();

            var components = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).ToList();
            if (components.Count > 0)
                text.Append(pad).Append("  components: ").AppendLine(string.Join(", ", components.Select(c => c.GetType().Name).ToArray()));

            var filter = t.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                text.Append(pad).Append("  mesh ").Append(filter.sharedMesh.name).Append("  bounds center ").Append(Fmt(filter.sharedMesh.bounds.center)).Append(" size ").AppendLine(Fmt(filter.sharedMesh.bounds.size));

            var box = t.GetComponent<BoxCollider>();
            if (box != null)
                text.Append(pad).Append("  box collider center ").Append(Fmt(box.center)).Append(" size ").Append(Fmt(box.size)).AppendLine(box.isTrigger ? " (trigger)" : "");

            var label = t.GetComponent<TMPro.TMP_Text>();
            if (label != null)
                text.Append(pad).Append("  text \"").Append(label.text).Append("\"  font size ").Append(label.fontSize.ToString("0.##")).Append("  font ").AppendLine(label.font == null ? "?" : label.font.name);

            foreach (Transform child in t)
                Dump(child, depth + 1, text);
        }

        private static string Fmt(Vector3 v) => "(" + v.x.ToString("0.###") + ", " + v.y.ToString("0.###") + ", " + v.z.ToString("0.###") + ")";
    }
}
