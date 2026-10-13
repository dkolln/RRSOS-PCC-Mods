using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Shared
{
    /// <summary>
    /// Makes a new build-menu item out of an existing one: its game data is copied under a new id (so it has the same model, icon and placement rules) with a
    /// recipe of your choice, and the new group is added to the game's lookup lists by reflection. It never calls GroupsHandler.SetAllGroups, which would also
    /// repoint the "which group does this prefab belong to" lookup from the original to the copy.
    /// </summary>
    internal static class GroupCloner
    {
        /// <summary>The copy, or null while the game's groups are not loaded yet (call again later). The copy is unlocked from the start.</summary>
        public static GroupConstructible Clone(string sourceId, string newId, IList<string> ingredientIds, string iconFromId = null, Func<Sprite, Sprite> paintIcon = null)
        {
            var source = GroupsHandler.GetGroupViaId(sourceId) as GroupConstructible;
            if (source == null || !(source.GetGroupData() is GroupDataConstructible sourceData))
                return null;

            var ingredients = new List<GroupDataItem>();
            foreach (var id in ingredientIds)
            {
                var group = GroupsHandler.GetGroupViaId(id);
                if (group == null || !(group.GetGroupData() is GroupDataItem data))
                    return null;
                ingredients.Add(data);
            }

            var copy = UnityEngine.Object.Instantiate(sourceData);
            copy.name = newId;
            copy.id = newId;
            copy.recipeIngredients = ingredients;
            copy.unlockingWorldUnit = DataConfig.WorldUnitType.Terraformation;
            copy.unlockingValue = 0f;
            copy.terraformStageUnlock = null;
            copy.unlockInPlanets = new List<PlanetData>();

            // The icon: a PNG in CustomIcons wins; else another group's icon; else the source's own.
            var ownIcon = CustomIcons.Load(newId, sourceData.icon != null ? sourceData.icon.pixelsPerUnit : 100f);
            if (ownIcon != null)
                copy.icon = ownIcon;
            else if (iconFromId != null && GroupsHandler.GetGroupViaId(iconFromId) is Group iconGroup && iconGroup.GetImage() != null)
            {
                // Another group's icon, optionally painted over; a painted icon is saved to CustomIcons so it can be replaced by dropping a PNG in.
                var icon = iconGroup.GetImage();
                if (paintIcon != null)
                {
                    var painted = paintIcon(icon);
                    if (painted != null)
                    {
                        CustomIcons.Save(newId, painted);
                        icon = painted;
                    }
                }

                copy.icon = icon;
            }

            var created = new GroupConstructible(copy);
            created.SetRecipe(new Recipe(ingredients));
            Recipes[created] = ingredientIds.ToArray();
            return created;
        }

        /// <summary>True when the group is in the game's lists. The game rebuilds them when it reloads its static data, so callers check again from time to time.</summary>
        public static bool IsInGame(Group group)
        {
            var all = GroupsHandler.GetAllGroups();
            var inGame = all != null && all.Contains(group);
            if (inGame)
                RefreshRecipe(group);
            return inGame;
        }

        // The ingredient ids of each clone. A Recipe holds the Group objects its ingredients were when it was made, and the game makes all its groups again when a world loads: the
        // inventory is then full of the new objects and the old ones in the recipe match nothing, so the ingredients show as missing (free build never checks, which hid it).
        private static readonly Dictionary<Group, string[]> Recipes = new Dictionary<Group, string[]>();

        /// <summary>Makes the clone's recipe from the game's current ingredient groups, when it holds out-of-date ones.</summary>
        public static void RefreshRecipe(Group group)
        {
            if (!Recipes.TryGetValue(group, out var ids))
                return;

            try
            {
                var current = group.GetRecipe() == null ? null : group.GetRecipe().GetIngredientsGroupInRecipe();
                var stale = current == null || current.Count != ids.Length;
                for (var i = 0; !stale && i < ids.Length; i++)
                    stale = !ReferenceEquals(current[i], GroupsHandler.GetGroupViaId(ids[i]));
                if (!stale)
                    return;

                var data = new List<GroupDataItem>();
                foreach (var id in ids)
                {
                    var item = GroupsHandler.GetGroupViaId(id);
                    if (item == null || !(item.GetGroupData() is GroupDataItem itemData))
                        return;
                    data.Add(itemData);
                }

                ((GroupConstructible)group).SetRecipe(new Recipe(data));
                ModLog.Note("Recipe of " + group.GetId() + " made again from the game's current ingredients (" + string.Join(", ", ids) + ").");
            }
            catch (Exception e)
            {
                ModLog.LogOnce("recipe-" + group.GetId(), "Could not refresh the recipe of " + group.GetId() + ": " + e.GetType().Name + ": " + e.Message);
            }
        }

        // Groups kept in the game: the game rebuilds its group lists whenever it loads its static data (for example when a world loads), and a saved building whose group is
        // missing at that moment is dropped from the world. So the lists are patched to put these groups back the instant the game has rebuilt them, before the world's objects
        // are made, instead of waiting for a check a moment later.
        private static readonly List<Group> Kept = new List<Group>();
        private static bool _patched;

        private static void Keep(Group group)
        {
            if (!Kept.Contains(group))
                Kept.Add(group);

            if (_patched)
                return;

            _patched = true;
            try
            {
                var harmony = new HarmonyLib.Harmony("com.rrsos.pcc.keepgroups." + typeof(GroupCloner).Assembly.GetName().Name);
                harmony.Patch(HarmonyLib.AccessTools.Method(typeof(GroupsHandler), "SetAllGroups"), postfix: new HarmonyLib.HarmonyMethod(typeof(GroupCloner), nameof(AfterGroupsRebuilt)));
            }
            catch (Exception e)
            {
                ModLog.Note("Could not keep the added groups in the game's lists: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void AfterGroupsRebuilt()
        {
            foreach (var group in Kept)
            {
                if (!IsInGame(group))
                    AddToGame(group);
                RefreshRecipe(group);
            }
        }

        public static void AddToGame(Group group)
        {
            Keep(group);

            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var type = typeof(GroupsHandler);
            ((List<Group>)type.GetField("_allGroups", flags).GetValue(null)).Add(group);
            ((List<GroupConstructible>)type.GetField("_groupsConstructible", flags).GetValue(null)).Add((GroupConstructible)group);
            ((Dictionary<int, Group>)type.GetField("_allGroupsByHash", flags).GetValue(null))[group.stableHashCode] = group;
            ((Dictionary<string, Group>)type.GetField("_allGroupsById", flags).GetValue(null))[group.id] = group;
        }
    }
}
