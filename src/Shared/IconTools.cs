using UnityEngine;

namespace RRSOS.PCC.Shared
{
    /// <summary>Reading a sprite's pixels and turning drawn pixels back into a sprite, for the icons the mods draw themselves (see CustomIcons).</summary>
    internal static class IconTools
    {
        /// <summary>
        /// The sprite's rectangle as pixels, bottom row first. The game's textures are not always readable, so they are copied through a render texture first.
        /// </summary>
        public static Color[] ReadPixels(Sprite sprite, out int width, out int height)
        {
            var rect = sprite.textureRect;
            var source = sprite.texture;
            width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
            height = Mathf.Max(1, Mathf.RoundToInt(rect.height));

            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target,
                    new Vector2(rect.width / source.width, rect.height / source.height),
                    new Vector2(rect.x / source.width, rect.y / source.height));

                RenderTexture.active = target;
                var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply();
                var pixels = copy.GetPixels();
                Object.Destroy(copy);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        /// <summary>A sprite made of the given pixels (bottom row first). The texture is one the mod made, so it can be read and saved.</summary>
        public static Sprite ToSprite(Color[] pixels, int width, int height, float pixelsPerUnit, string name)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;

            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            sprite.name = name;
            return sprite;
        }
    }
}
