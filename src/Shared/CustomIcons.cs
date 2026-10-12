using System;
using System.IO;
using UnityEngine;

namespace RRSOS.PCC.Shared
{
    /// <summary>
    /// Icons the mods make themselves (a stack of three crates, a terminal prompt) live in %LOCALAPPDATA%\RRSOS-PCC-Mods\CustomIcons\&lt;group id&gt;.png, apart from the
    /// game's own icons. A PNG already there wins over a freshly drawn one, so the icon can be replaced by simply dropping a file in; delete it to have it drawn again.
    /// </summary>
    internal static class CustomIcons
    {
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RRSOS-PCC-Mods", "CustomIcons");

        public static string PathFor(string groupId) => Path.Combine(Folder, groupId + ".png");

        /// <summary>The icon file for the group as a sprite, or null when there is none (or it cannot be read).</summary>
        public static Sprite Load(string groupId, float pixelsPerUnit)
        {
            try
            {
                var path = PathFor(groupId);
                if (!File.Exists(path))
                    return null;

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
                    return null;

                texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                sprite.name = groupId;
                return sprite;
            }
            catch (Exception e)
            {
                ModLog.Note("Could not load the custom icon of " + groupId + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Writes a drawn icon to the folder (the sprite's texture must be one the mod made, so that it can be read).</summary>
        public static void Save(string groupId, Sprite sprite)
        {
            try
            {
                var texture = sprite.texture;
                var png = texture.EncodeToPNG();
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(PathFor(groupId), png);
                ModLog.Note("Custom icon saved: " + PathFor(groupId));
            }
            catch (Exception e)
            {
                ModLog.Note("Could not save the custom icon of " + groupId + ": " + e.Message);
            }
        }
    }
}
