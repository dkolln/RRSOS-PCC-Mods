using System.Collections.Generic;
using System.Reflection;
using SpaceCraft;

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
        public static GroupConstructible Clone(string sourceId, string newId, IList<string> ingredientIds, string iconFromId = null)
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
                copy.icon = iconGroup.GetImage();

            var created = new GroupConstructible(copy);
            created.SetRecipe(new Recipe(ingredients));
            return created;
        }

        /// <summary>True when the group is in the game's lists. The game rebuilds them when it reloads its static data, so callers check again from time to time.</summary>
        public static bool IsInGame(Group group)
        {
            var all = GroupsHandler.GetAllGroups();
            return all != null && all.Contains(group);
        }

        public static void AddToGame(Group group)
        {
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var type = typeof(GroupsHandler);
            ((List<Group>)type.GetField("_allGroups", flags).GetValue(null)).Add(group);
            ((List<GroupConstructible>)type.GetField("_groupsConstructible", flags).GetValue(null)).Add((GroupConstructible)group);
            ((Dictionary<int, Group>)type.GetField("_allGroupsByHash", flags).GetValue(null))[group.stableHashCode] = group;
            ((Dictionary<string, Group>)type.GetField("_allGroupsById", flags).GetValue(null))[group.id] = group;
        }
    }
}
