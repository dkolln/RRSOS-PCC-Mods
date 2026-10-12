using System;
using System.Collections;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.GiveItem
{
    /// <summary>
    /// /giveitem &lt;item&gt; [quantity] on the terminal. The item is the game's item id (or any whole name the item lookup knows, such as "water"); the quantity is 1 when left out.
    /// Items are added to the backpack one at a time through the game's own add-to-inventory call, so they land where the game puts them; when the backpack has less room than
    /// asked for, it takes what fits.
    /// </summary>
    internal sealed class GiveItemCommand : MonoBehaviour
    {
        private const float WaitForItemSeconds = 3f;

        private readonly ItemLookup _lookup = new ItemLookup();
        private bool _registered;
        private bool _busy;

        private IEnumerator Start()
        {
            while (!_registered)
            {
                yield return new WaitForSecondsRealtime(0.5f);

                if (!Plugin.Enabled.Value)
                    continue;

                try
                {
                    // The terminal is the only place this command lives. Without it the mod does nothing.
                    if (TerminalRegistration.TryRegister("/giveitem", "/giveitem <item id> [quantity]", "put an item into your backpack", OnCommand))
                    {
                        _registered = true;
                        ModLog.Note("The terminal is installed: /giveitem is on it.");
                    }
                }
                catch (Exception e)
                {
                    _registered = true; // do not try again every half second
                    ModLog.Note("Could not register /giveitem with the terminal: " + e.Message);
                }
            }
        }

        private void OnCommand(string args, Action<string> print)
        {
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

                var parts = (args ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    print("Usage: /giveitem <item id> [quantity]");
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
