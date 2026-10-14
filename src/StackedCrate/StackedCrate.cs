using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.StackedCrate
{
    /// <summary>
    /// A "stacked crate" in the build menu: Container1's data copied under a new id (same ghost, same placement rules), costing 3 Iron, with an icon of three crates.
    /// When one is placed the game makes a single placeholder piece; this swaps it for three ordinary Container1 on top of each other, so a save only ever holds
    /// Container1. The group is added to the game's lookup lists by reflection (never through SetAllGroups, which would repoint Container1's prefab to the new group).
    /// </summary>
    internal sealed class StackedCrate : MonoBehaviour
    {
        private const string NewId = "StackedCrate";
        private const string SourceId = "Container1";
        private const string IronId = "Iron";
        private const int Levels = 3;
        private const float LevelHeight = 1.0f;

        private Group _group;
        private bool _busy;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(1f);

                if (!Plugin.Enabled.Value)
                    continue;

                try
                {
                    EnsureRegistered();
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("stackedcrate-register", "Could not add the stacked crate to the build menu: " + e.GetType().Name + ": " + e.Message);
                }

                if (_group == null || _busy)
                    continue;

                _busy = true;
                try
                {
                    ReplacePlaceholders();
                }
                catch (Exception e)
                {
                    ModLog.Note("Stacked crate: " + e.GetType().Name + ": " + e.Message);
                }

                _busy = false;
            }
        }

        // The game rebuilds its group lists when it reloads its static data, so check every second and put the group back if it is missing.
        private void EnsureRegistered()
        {
            var all = GroupsHandler.GetAllGroups();
            if (all == null)
                return;

            if (_group == null)
            {
                var source = GroupsHandler.GetGroupViaId(SourceId) as GroupConstructible;
                var iron = GroupsHandler.GetGroupViaId(IronId);
                if (source == null || iron == null || !(source.GetGroupData() is GroupDataConstructible sourceData) || !(iron.GetGroupData() is GroupDataItem ironData))
                    return;

                var data = UnityEngine.Object.Instantiate(sourceData);
                data.name = NewId;
                data.id = NewId;
                data.recipeIngredients = Enumerable.Repeat(ironData, Levels).ToList();
                var icon = CustomIcons.Load(NewId, sourceData.icon != null ? sourceData.icon.pixelsPerUnit : 100f);
                if (icon == null)
                {
                    icon = ComposeIcon(sourceData.icon);
                    if (icon != null)
                        CustomIcons.Save(NewId, icon);
                }

                data.icon = icon ?? sourceData.icon;

                var group = new GroupConstructible(data);
                group.SetRecipe(new Recipe(data.recipeIngredients));
                _group = group;
                ModLog.Note("Stacked crate: created the group (" + Levels + " x " + IronId + ", icon of " + Levels + " crates: " + (data.icon != sourceData.icon) + ").");
            }

            if (!all.Contains(_group))
                AddToGame(_group);
        }

        private static void AddToGame(Group group)
        {
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var type = typeof(GroupsHandler);
            ((List<Group>)type.GetField("_allGroups", flags).GetValue(null)).Add(group);
            ((List<GroupConstructible>)type.GetField("_groupsConstructible", flags).GetValue(null)).Add((GroupConstructible)group);
            ((Dictionary<int, Group>)type.GetField("_allGroupsByHash", flags).GetValue(null))[group.stableHashCode] = group;
            ((Dictionary<string, Group>)type.GetField("_allGroupsById", flags).GetValue(null))[group.id] = group;
            ModLog.Note("Stacked crate: added to the game's group lists (" + group.id + ").");
        }

        private void ReplacePlaceholders()
        {
            if (!WorldState.Ready(out var planetHash, out _))
                return;

            var handler = WorldObjectsHandler.Instance;
            var placeholders = handler.GetAllWorldObjects().Values.Where(w => w != null && w.GetGroup() == _group && w.GetPosition().sqrMagnitude > 0f).ToList();
            foreach (var placeholder in placeholders)
            {
                var pos = placeholder.GetPosition();
                var rot = placeholder.GetRotation();
                var hash = placeholder.GetPlanetHash() != 0 ? placeholder.GetPlanetHash() : planetHash;

                var created = new List<WorldObject>();
                try
                {
                    for (var level = 0; level < Levels; level++)
                        created.Add(Placement.PlaceAt(SourceId, pos + new Vector3(0f, level * LevelHeight, 0f), rot, hash));
                }
                catch (Exception e)
                {
                    foreach (var wo in created)
                        handler.DestroyWorldObject(wo, true);
                    created.Clear();
                    ModLog.Note("Stacked crate: could not place the stack (" + e.Message + "); placing one crate instead.");
                    try
                    {
                        created.Add(Placement.PlaceAt(SourceId, pos, rot, hash));
                    }
                    catch (Exception e2)
                    {
                        ModLog.Note("Stacked crate: could not place even one crate: " + e2.Message);
                    }

                    TerminalRegistration.Notice("The stacked crate could not be built, so I put a single crate where it was.");
                }

                handler.DestroyWorldObject(placeholder, true);
                if (created.Count > 0)
                {
                    ModLog.Note("Stacked crate: " + created.Count + " Container1 at " + pos.x.ToString("0.###") + ", " + pos.y.ToString("0.###") + ", " + pos.z.ToString("0.###") + ".");
                }
            }
        }

        // ---- the icon: three copies of Container1's icon, one above the other ---------------------------------------------------

        private static Sprite ComposeIcon(Sprite source)
        {
            try
            {
                if (source == null || source.texture == null)
                    return null;

                var rect = source.textureRect;
                var width = Mathf.Max(8, Mathf.RoundToInt(rect.width));
                var height = Mathf.Max(8, Mathf.RoundToInt(rect.height));
                var pixels = ReadPixels(source, width, height);

                var scale = 0.58f;
                var small = new Vector2(width * scale, height * scale);
                var canvas = new Color[width * height];
                for (var level = 0; level < Levels; level++)
                {
                    // bottom first, so the upper crates are drawn over the lower ones
                    var x0 = (width - small.x) / 2f;
                    var y0 = level * (height - small.y) / (Levels - 1);
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var u = (x - x0) / small.x;
                            var v = (y - y0) / small.y;
                            if (u < 0f || u >= 1f || v < 0f || v >= 1f)
                                continue;

                            var s = pixels[Mathf.Clamp((int)(v * height), 0, height - 1) * width + Mathf.Clamp((int)(u * width), 0, width - 1)];
                            var d = canvas[y * width + x];
                            var a = s.a + d.a * (1f - s.a);
                            canvas[y * width + x] = a <= 0f ? new Color(0, 0, 0, 0)
                                : new Color((s.r * s.a + d.r * d.a * (1f - s.a)) / a, (s.g * s.a + d.g * d.a * (1f - s.a)) / a, (s.b * s.a + d.b * d.a * (1f - s.a)) / a, a);
                        }
                    }
                }

                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.SetPixels(canvas);
                texture.Apply();
                texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                sprite.name = NewId;
                return sprite;
            }
            catch (Exception e)
            {
                ModLog.LogOnce("stackedcrate-icon", "Could not compose the three-crate icon; using the plain crate icon: " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        /// <summary>The sprite's rectangle as pixels, copied through a render texture since the game's textures are not always readable (as the Live plugin's icon export does).</summary>
        private static Color[] ReadPixels(Sprite sprite, int width, int height)
        {
            var rect = sprite.textureRect;
            var source = sprite.texture;
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
                UnityEngine.Object.Destroy(copy);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
