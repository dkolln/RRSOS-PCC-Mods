using System;
using System.Collections;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Shared
{
    /// <summary>Filling a crate's empty slots with an item. Shared source, used by Auto Fill (at a rename) and by the terminal's /resupply.</summary>
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
