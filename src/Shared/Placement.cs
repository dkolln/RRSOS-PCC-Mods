using System;
using System.Collections.Generic;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Shared
{
    /// <summary>Places pieces in the running world.</summary>
    internal static class Placement
    {
        /// <summary>
        /// Brings one piece to life the way the game does when it loads a save: a world object with its position (and panels, colour, label, linked groups), then
        /// InstantiateWorldObject. The game saves it with everything else. Throws, after removing what it made, if the game refuses.
        /// </summary>
        public static WorldObject PlaceAt(string groupId, Vector3 pos, Quaternion rot, int planetHash, int[] panels = null, string color = null, string text = null, string linkedGroups = null)
        {
            var handler = WorldObjectsHandler.Instance;
            var group = GroupsHandler.GetGroupViaId(groupId);
            if (group == null)
                throw new Exception("the game has no group " + groupId);

            var wo = handler.CreateNewWorldObject(group, 0, null, true);
            try
            {
                wo.SetPositionAndRotation(pos, rot, planetHash);
                if (panels != null)
                    wo.SetPanelsId(new List<int>(panels));
                if (color != null)
                    wo.SetColor(DataTreatments.StringToColor(color));
                if (!string.IsNullOrEmpty(text))
                    wo.SetText(text);
                if (!string.IsNullOrEmpty(linkedGroups))
                    wo.SetLinkedGroups(GroupsHandler.GetGroupsViaString(linkedGroups) as List<Group>);

                var made = handler.InstantiateWorldObject(wo, true, 0uL);
                if (made == null)
                    throw new Exception("InstantiateWorldObject gave nothing back");
            }
            catch
            {
                handler.DestroyWorldObject(wo, true);
                throw;
            }

            return wo;
        }
    }
}
