using System;
using System.Collections;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Resupply
{
    /// <summary>
    /// /duplicate trait, /duplicate dna or /duplicate &lt;item&gt;: every such item in the backpack (a genetic trait keeps its kind, colour and value) gets a second copy in the backpack. Only what fits is
    /// duplicated; the copies are made through the game's own calls, the way the game makes a trait.
    /// </summary>
    internal static class DnaDuplicate
    {
        private const float WaitSeconds = 3f;

        public static IEnumerator Run(Action<string> say, Func<string, bool> matches, string label)
        {
            var made = 0;
            var wanted = 0;
            var failed = false;
            Inventory backpack = null;
            try
            {
                backpack = Managers.GetManager<PlayersManager>().GetActivePlayerController().GetPlayerBackpack().GetInventory();
            }
            catch (Exception e)
            {
                ModLog.Note("Duplicate DNA: no backpack: " + e.Message);
            }

            if (backpack == null)
            {
                say("The backpack is not ready yet.");
                yield break;
            }

            // a snapshot: the copies added below are not copied again
            var originals = backpack.GetInsideWorldObjects().Where(w => w != null && w.GetGroup() != null && matches(w.GetGroup().GetId())).ToList();
            wanted = originals.Count;
            if (wanted == 0)
            {
                say("There is no " + label + " in the backpack.");
                yield break;
            }

            foreach (var original in originals)
            {
                if (backpack.GetInsideWorldObjects().Count >= backpack.GetSize())
                    break;

                var done = false;
                var ok = false;
                WorldObject copy = null;
                try
                {
                    if (original.GetGroup().GetId() == "GeneticTrait" && GeneticTraitHandler.Instance != null)
                    {
                        copy = GeneticTraitHandler.Instance.CreateNewTraitWorldObject(new GeneticTraitData.SerializedData
                        {
                            traitType = original.GetGeneticTraitType(),
                            traitColor = original.GetColor(),
                            traitValue = original.GetGeneticTraitValue()
                        }, true);
                        InventoriesHandler.Instance.AddItemToInventory(copy, backpack.GetId(), true, success =>
                        {
                            ok = success;
                            done = true;
                        });
                    }
                    else
                    {
                        InventoriesHandler.Instance.AddItemToInventory(original.GetGroup(), backpack, (success, id) =>
                        {
                            ok = success;
                            done = true;
                        });
                    }
                }
                catch (Exception e)
                {
                    ModLog.Note("Duplicate DNA: " + e.GetType().Name + ": " + e.Message);
                    failed = true;
                    break;
                }

                var until = Time.realtimeSinceStartup + WaitSeconds;
                while (!done && Time.realtimeSinceStartup < until)
                    yield return null;

                if (!ok)
                {
                    if (copy != null)
                        WorldObjectsHandler.Instance.DestroyWorldObject(copy);
                    break;
                }

                made++;
            }

            ModLog.Note("Duplicate " + label + ": " + made + " of " + wanted + " copied.");
            say(failed ? "That did not work; see the mod's log." : made == wanted ? "Copied " + made + " x " + label + "." : "Backpack full: copied " + made + " of " + wanted + " x " + label + ".");
        }

    }
}
