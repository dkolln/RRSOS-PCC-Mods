using System;
using System.Collections;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Resupply
{
    /// <summary>
    /// /give &lt;item&gt; [quantity] in the game's chat (also /giveitem), and /give traits [count]. The item is the game's item id (or any whole name the item lookup knows, such as "water", from the shared aliases.txt; /add gives an item another name); the quantity is 1 when left out.
    /// Items are added to the backpack one at a time through the game's own add-to-inventory call, so they land where the game puts them; when the backpack has less room than
    /// asked for, it takes what fits.
    /// </summary>
    internal sealed class GiveCommand : MonoBehaviour
    {
        private const float WaitForItemSeconds = 3f;

        private static ItemLookup Lookup => Resupplier.Instance.Lookup;
        private bool _busy;

        // The chat command is registered by the plugin (ChatHook.Add); this answers it in the chat.
        internal void OnCommand(string args)
        {
            Action<string> print = ChatHook.Say;
            try
            {
                if (_busy)
                {
                    print("Still busy with the last /give.");
                    return;
                }

                if (!WorldState.Ready(out _, out _))
                {
                    print("The world is not ready yet.");
                    return;
                }

                if (Resupplier.Instance == null)
                {
                    print("The game is not ready yet.");
                    return;
                }

                var parts = (args ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                // /give traits [count]: random colour A and colour B genetic traits
                if (parts.Length > 0 && string.Equals(parts[0], "traits", StringComparison.OrdinalIgnoreCase))
                {
                    var wanted = 20;
                    if (parts.Length > 1 && !int.TryParse(parts[1], out wanted))
                        wanted = 0;
                    if (wanted < 1 || wanted > Plugin.MaxGive.Value)
                    {
                        print("Usage: /give traits [count], 1 to " + Plugin.MaxGive.Value + " (20 when left out).");
                        return;
                    }

                    StartCoroutine(GiveTraits(wanted, print));
                    return;
                }

                if (parts.Length == 0)
                {
                    print("Usage: /give <item> [quantity], for example /give Iron 20 (or /give traits for 20 random colour traits). Another name for an item: /add <item id> <name>");
                    return;
                }

                // A last word that is a number is the quantity; everything before it is the item (so a name of several words works).
                var quantity = 1;
                var nameParts = parts.Length;
                if (parts.Length > 1 && int.TryParse(parts[parts.Length - 1], out var asked))
                {
                    quantity = asked;
                    nameParts = parts.Length - 1;
                }

                if (quantity < 1)
                {
                    print("The quantity must be 1 or more.");
                    return;
                }

                if (quantity > Plugin.MaxGive.Value)
                {
                    print("At most " + Plugin.MaxGive.Value + " at a time.");
                    return;
                }

                var name = string.Join(" ", parts, 0, nameParts);
                var item = Lookup.Resolve(name);
                if (item == null)
                {
                    print("No item is called \"" + name + "\" (use the item's id, for example Iron).");
                    return;
                }

                StartCoroutine(Give(item, quantity, print));
            }
            catch (Exception e)
            {
                ModLog.Note("/give failed: " + e.GetType().Name + ": " + e.Message);
                print("That did not work (" + e.Message + "). See the mod's log.");
            }
        }

        /// <summary>Random colour A and colour B genetic traits, made the way the game makes a looted one and put in the backpack.</summary>
        private IEnumerator GiveTraits(int count, Action<string> print)
        {
            _busy = true;
            var given = 0;
            var a = 0;
            try
            {
                var handler = GeneticTraitHandler.Instance;
                var pool = handler == null || handler.GetAllAvailableTraits() == null ? new System.Collections.Generic.List<GeneticTraitData>()
                    : handler.GetAllAvailableTraits().Where(t => t != null && (t.GetGeneticTraitType() == DataConfig.GeneticTraitType.ColorA || t.GetGeneticTraitType() == DataConfig.GeneticTraitType.ColorB)).ToList();
                if (pool.Count == 0)
                {
                    print("The game has no colour traits to give yet.");
                    yield break;
                }

                var backpack = Managers.GetManager<PlayersManager>().GetActivePlayerController().GetPlayerBackpack().GetInventory();
                var room = Mathf.Max(backpack.GetSize() - backpack.GetInsideWorldObjects().Count, 0);
                for (var i = 0; i < Mathf.Min(count, room); i++)
                {
                    var data = pool[UnityEngine.Random.Range(0, pool.Count)];
                    var trait = handler.CreateNewTraitWorldObject(data, true);
                    var done = false;
                    var ok = false;
                    InventoriesHandler.Instance.AddItemToInventory(trait, backpack.GetId(), true, success =>
                    {
                        ok = success;
                        done = true;
                    });

                    var until = Time.realtimeSinceStartup + WaitForItemSeconds;
                    while (!done && Time.realtimeSinceStartup < until)
                        yield return null;

                    if (!ok)
                    {
                        WorldObjectsHandler.Instance.DestroyWorldObject(trait);
                        break;
                    }

                    given++;
                    if (data.GetGeneticTraitType() == DataConfig.GeneticTraitType.ColorA)
                        a++;
                }
            }
            finally
            {
                _busy = false;
            }

            ModLog.Note("/give traits x" + count + ": gave " + given + " (" + a + " colour A, " + (given - a) + " colour B).");
            print(given == count ? "Gave " + given + " random colour traits (" + a + " colour A, " + (given - a) + " colour B)." : "Backpack full: gave " + given + " of " + count + " colour traits.");
        }

        private IEnumerator Give(GroupItem item, int quantity, Action<string> print)
        {
            _busy = true;
            var given = 0;
            try
            {
                var backpack = Managers.GetManager<PlayersManager>().GetActivePlayerController().GetPlayerBackpack().GetInventory();
                var room = backpack.GetSize() - backpack.GetInsideWorldObjects().Count;
                var count = Mathf.Min(quantity, Mathf.Max(room, 0));

                for (var i = 0; i < count; i++)
                {
                    var done = false;
                    var ok = false;
                    InventoriesHandler.Instance.AddItemToInventory(item, backpack, (success, woId) =>
                    {
                        ok = success;
                        done = true;
                    });

                    var until = Time.realtimeSinceStartup + WaitForItemSeconds;
                    while (!done && Time.realtimeSinceStartup < until)
                        yield return null;

                    if (!ok)
                        break;
                    given++;
                }
            }
            finally
            {
                _busy = false;
            }

            var label = item.GetId();
            ModLog.Note("/give " + label + " x" + quantity + ": gave " + given + ".");
            if (given == quantity)
                print("Gave " + given + " x " + label + ".");
            else if (given == 0)
                print("Backpack full: nothing given.");
            else
                print("Backpack full: gave " + given + " of " + quantity + " x " + label + ".");
        }
    }
}
