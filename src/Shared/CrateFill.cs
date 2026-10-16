using System;
using System.Collections;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Shared
{
    /// <summary>Filling a crate's empty slots with an item. Shared source, used by Resupply (at a rename, and on demand) and by Base (stocking the T0 crates).</summary>
    internal static class CrateFill
    {
        private const float WaitForItemSeconds = 3f;

        public sealed class Result
        {
            public int Empty = -1;   // empty slots there were (-1: the crate's inventory was not found)
            public int Added;        // items added
        }

        /// <summary>The crate's label, trimmed (empty when it has none).</summary>
        public static string Label(WorldObject crate) => (crate.GetText() ?? "").Trim();

        /// <summary>
        /// What a crate is meant to hold: the item its label names; failing that, its demand (the drone supply setting) when that is exactly one item. Null when neither says
        /// (no label, the label supplier or misc, a label that is not an item name, a demand of several groups or of a group that is not a single item), so nothing is guessed.
        /// </summary>
        public static GroupItem ItemFor(WorldObject crate, ItemLookup lookup)
        {
            var label = Label(crate);
            var plain = label.Trim();
            if (string.Equals(plain, "supplier", StringComparison.OrdinalIgnoreCase) || string.Equals(plain, "misc", StringComparison.OrdinalIgnoreCase))
                return null;   // a crate labelled supplier or misc is never filled (by the icon, the Resupply button, the auto top-up, a rename or the drone buttons)

            var item = label.Length == 0 ? null : lookup.Resolve(label);
            if (item != null)
                return item;

            var demand = crate.HasLinkedGroups() ? crate.GetLinkedGroups() : null;
            if (demand == null || demand.Count != 1)
                return null;

            return demand[0] as GroupItem;
        }

        /// <summary>
        /// Adds the item to each empty slot of the crate, one at a time through the game's own add-to-inventory call. What is in the crate already is never touched.
        /// Runs as a coroutine (yield it from one); the outcome is in the result.
        /// </summary>
        public static IEnumerator Fill(WorldObject crate, GroupItem item, Result result)
        {
            var inventory = InventoriesHandler.Instance.GetInventoryById(crate.GetLinkedInventoryId());
            if (inventory == null)
            {
                ModLog.Note("Crate #" + crate.GetId() + ": no inventory found; nothing filled.");
                yield break;
            }

            result.Empty = inventory.GetSize() - inventory.GetInsideWorldObjects().Count;
            for (var i = 0; i < result.Empty; i++)
            {
                var done = false;
                var ok = false;
                try
                {
                    InventoriesHandler.Instance.AddItemToInventory(item, inventory, (success, woId) =>
                    {
                        ok = success;
                        done = true;
                    });
                }
                catch (Exception e)
                {
                    ModLog.Note("Crate #" + crate.GetId() + ": adding failed: " + e.GetType().Name + ": " + e.Message);
                    break;
                }

                var until = Time.realtimeSinceStartup + WaitForItemSeconds;
                while (!done && Time.realtimeSinceStartup < until)
                    yield return null;

                if (!ok)
                    break;
                result.Added++;
            }

            ModLog.Note("Crate #" + crate.GetId() + " labelled \"" + Label(crate) + "\": added " + result.Added + " x " + item.GetId() + " (" + result.Empty + " empty slots).");
        }
    }
}
