using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RRSOS.PCC.Shared;
using SpaceCraft;

namespace RRSOS.PCC.Resupply
{
    /// <summary>
    /// Purge Extractors: an ore extractor standing on a vein is set to that vein's ore (its linked groups). Whatever else it holds (iron, cobalt, ... mined before, or put in)
    /// is deleted, so only the vein's ore is left in it. An extractor with no ore set (not on a vein) is left alone.
    /// </summary>
    internal static class ExtractorPurge
    {
        private static readonly Regex Extractor = new Regex(@"^OreExtractor\d*$", RegexOptions.Compiled);

        public sealed class Outcome
        {
            public int Purged, Items, Clean, NoVein;
        }

        public static Outcome Run(int planetHash)
        {
            var outcome = new Outcome();
            foreach (var machine in WorldObjectsHandler.Instance.GetAllWorldObjects().Values
                .Where(w => w != null && w.GetGroup() != null && Extractor.IsMatch(w.GetGroup().GetId()) && w.GetLinkedInventoryId() != 0 && w.GetPlanetHash() == planetHash && w.GetPosition().sqrMagnitude > 0f).ToList())
            {
                // the ore it is set to: what its linked groups list (a T2 lists its ore once for each slot)
                var ore = new HashSet<string>(machine.HasLinkedGroups() ? machine.GetLinkedGroups().Where(g => g != null).Select(g => g.GetId()) : new string[0]);
                if (ore.Count == 0)
                {
                    outcome.NoVein++;
                    continue;
                }

                var inventory = InventoriesHandler.Instance.GetInventoryById(machine.GetLinkedInventoryId());
                if (inventory == null)
                    continue;

                var strangers = inventory.GetInsideWorldObjects().Where(i => i != null && i.GetGroup() != null && !ore.Contains(i.GetGroup().GetId())).ToList();
                if (strangers.Count == 0)
                {
                    outcome.Clean++;
                    continue;
                }

                var removed = 0;
                foreach (var item in strangers)
                {
                    var id = item.GetId();
                    try
                    {
                        InventoriesHandler.Instance.RemoveItemFromInventory(item, inventory, true);
                        if (WorldObjectsHandler.Instance.GetWorldObjectViaId(id) != null)
                            WorldObjectsHandler.Instance.DestroyWorldObject(id, true);
                        removed++;
                    }
                    catch (System.Exception e)
                    {
                        ModLog.Note("Purge: could not remove " + item.GetGroup().GetId() + " #" + id + " from " + machine.GetGroup().GetId() + " #" + machine.GetId() + ": " + e.GetType().Name + ": " + e.Message);
                    }
                }

                if (removed > 0)
                {
                    outcome.Purged++;
                    outcome.Items += removed;
                    ModLog.Note("Purge: " + machine.GetGroup().GetId() + " #" + machine.GetId() + " keeps " + string.Join(", ", ore.ToArray()) + "; removed " + removed + " other items.");
                }
            }

            ModLog.Note("Purge extractors: " + outcome.Items + " items from " + outcome.Purged + " extractors, " + outcome.Clean + " already clean, " + outcome.NoVein + " not on a vein.");
            return outcome;
        }
    }
}
