using System;
using System.Collections;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.GiveItem
{
    /// <summary>
    /// /giveitem &lt;item&gt; [quantity] in the game's chat, and /giveitem add &lt;item id&gt; &lt;name&gt; to give an item another name. The item is the game's item id (or any whole name the item lookup knows, such as "water", from this mod's own aliases.txt); the quantity is 1 when left out.
    /// Items are added to the backpack one at a time through the game's own add-to-inventory call, so they land where the game puts them; when the backpack has less room than
    /// asked for, it takes what fits.
    /// </summary>
    internal sealed class GiveItemCommand : MonoBehaviour
    {
        private const float WaitForItemSeconds = 3f;

        private ItemLookup _lookup;   // made once the mod's data folder is known: this mod keeps its own aliases.txt, first made as a copy of the shared one
        private bool _busy;

        // The chat command is registered by the plugin (ChatHook.Add); this answers it in the chat.
        internal void OnCommand(string args)
        {
            Action<string> print = ChatHook.Say;
            try
            {
                if (_busy)
                {
                    print("Still busy with the last /giveitem.");
                    return;
                }

                if (!WorldState.Ready(out _, out _))
                {
                    print("The world is not ready yet.");
                    return;
                }

                _lookup = _lookup ?? new ItemLookup(ModLog.FileIn("aliases.txt"));

                var parts = (args ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                // /giveitem add <item id> <name>: one more name for an item, kept in this mod's own aliases.txt
                if (parts.Length > 0 && string.Equals(parts[0], "add", StringComparison.OrdinalIgnoreCase))
                {
                    AddName(parts.Skip(1).ToArray(), print);
                    return;
                }

                // /giveitem traits [count]: random colour A and colour B genetic traits
                if (parts.Length > 0 && string.Equals(parts[0], "traits", StringComparison.OrdinalIgnoreCase))
                {
                    var wanted = 20;
                    if (parts.Length > 1 && !int.TryParse(parts[1], out wanted))
                        wanted = 0;
                    if (wanted < 1 || wanted > Plugin.MaxQuantity.Value)
                    {
                        print("Usage: /giveitem traits [count], 1 to " + Plugin.MaxQuantity.Value + " (20 when left out).");
                        return;
                    }

                    StartCoroutine(GiveTraits(wanted, print));
                    return;
                }

                if (parts.Length == 0)
                {
                    print("Usage: /giveitem <item> [quantity], for example /giveitem Iron 20 (or /giveitem traits for 20 random colour traits). Another name for an item: /giveitem add <item id> <name>");
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

                if (quantity > Plugin.MaxQuantity.Value)
                {
                    print("At most " + Plugin.MaxQuantity.Value + " at a time.");
                    return;
                }

                var name = string.Join(" ", parts, 0, nameParts);
                var item = _lookup.Resolve(name);
                if (item == null)
                {
                    print("No item is called \"" + name + "\" (use the item's id, for example Iron).");
                    return;
                }

                StartCoroutine(Give(item, quantity, print));
            }
            catch (Exception e)
            {
                ModLog.Note("/giveitem failed: " + e.GetType().Name + ": " + e.Message);
                print("That did not work (" + e.Message + "). See the mod's log.");
            }
        }

        /// <summary>/giveitem add &lt;item id&gt; &lt;name&gt;: gives an item one more name, saved in this mod's own aliases.txt and used straight away.</summary>
        private void AddName(string[] parts, Action<string> say)
        {
            if (parts.Length < 2)
            {
                say("Usage: /giveitem add <item id> <name>, for example /giveitem add waterbottle1 water");
                return;
            }

            var name = string.Join(" ", parts, 1, parts.Length - 1).Trim();
            var item = _lookup.ById(parts[0]);
            if (item == null)
            {
                say("No item has the id \"" + parts[0] + "\".");
                return;
            }

            if (ItemLookup.Plain(name).Length == 0)
            {
                say("The name needs at least one letter or digit.");
                return;
            }

            var existing = _lookup.Existing(name);
            if (existing != null)
            {
                say("Name: " + name + " already exists" + (existing == item ? "." : " (it names " + Readable.GetGroupName(existing) + ")."));
                return;
            }

            _lookup.AddExplicit(name, item);
            ModLog.Append(ModLog.FileIn("aliases.txt"), item.GetId() + " = " + name);
            say("Name: " + name + " added.");
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

            ModLog.Note("/giveitem traits x" + count + ": gave " + given + " (" + a + " colour A, " + (given - a) + " colour B).");
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
            ModLog.Note("/giveitem " + label + " x" + quantity + ": gave " + given + ".");
            if (given == quantity)
                print("Gave " + given + " x " + label + ".");
            else if (given == 0)
                print("Backpack full: nothing given.");
            else
                print("Backpack full: gave " + given + " of " + quantity + " x " + label + ".");
        }
    }
}
