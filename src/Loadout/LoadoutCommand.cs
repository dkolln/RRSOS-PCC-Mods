using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Loadout
{
    /// <summary>
    /// /loadout: fits the top tier of every piece of personal gear, through the game's own equip steps (so the size and effect upgrades apply as when equipped by hand).
    /// A second call only does the difference: it adds what is missing and swaps a lower tier for its top tier.
    /// </summary>
    internal sealed class LoadoutCommand : MonoBehaviour
    {
        private static bool _running;

        internal void OnCommand(string args)
        {
            if (_running)
            {
                ChatHook.Say("The loadout is still being fitted.");
                return;
            }

            StartCoroutine(Routine());
        }

        /// <summary>The top tier of every personal equipment family in the game's data (not vehicle upgrades, skins or the story item), size and backpack upgrades first.</summary>
        private static List<GroupItem> LoadoutItems()
        {
            var families = new Dictionary<string, KeyValuePair<int, GroupItem>>();
            foreach (var group in GroupsHandler.GetAllGroups())
            {
                var item = group as GroupItem;
                if (item == null || item.GetItemCategory() != DataConfig.ItemCategory.Equipment)
                    continue;

                var id = item.GetId();
                if (id.StartsWith("Vehicle") || id.StartsWith("Skin-") || id.StartsWith("WardensChip"))
                    continue;

                var family = SlotOf(item);
                var tier = TierOf(id);
                if (!families.TryGetValue(family, out var best) || tier > best.Key)
                    families[family] = new KeyValuePair<int, GroupItem>(tier, item);
            }

            return families.Values.Select(v => v.Value)
                .OrderBy(i => i.GetId().StartsWith("EquipmentIncrease") ? 0 : i.GetId().StartsWith("Backpack") ? 1 : 2)
                .ThenBy(i => i.GetId()).ToList();
        }

        private static IEnumerator WaitUntil(Func<bool> done, float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < until)
                yield return new WaitForSecondsRealtime(0.1f);
        }

        /// <summary>What the game calls one equipment slot: the game allows one worn item per equip type (MultiDeconstruct and MultiToolDeconstruct3 share one), so the type is the family. Items with no equip type fall back to their name without the tier number.</summary>
        private static string SlotOf(GroupItem item) =>
            item.GetEquipableType() != DataConfig.EquipableType.Null ? item.GetEquipableType().ToString() : Regex.Match(item.GetId(), @"^(.*?)(\d*)$").Groups[1].Value;

        /// <summary>The game reads a tier from the last character of the id, and 1 when there is no number.</summary>
        private static int TierOf(string id) => int.TryParse(id.Substring(id.Length - 1), out var tier) ? tier : 1;

        private IEnumerator Routine()
        {
            _running = true;
            var fitted = new List<string>();
            var replaced = new List<string>();
            var already = new List<string>();
            var failed = new List<string>();
            var completed = false;
            try
            {
                var player = Managers.GetManager<PlayersManager>().GetActivePlayerController();
                var equipment = player.GetPlayerEquipment();
                var equipped = equipment.GetInventory();
                var backpack = player.GetPlayerBackpack().GetInventory();
                var items = LoadoutItems();
                ModLog.Note("Loadout: " + string.Join(", ", items.Select(i => i.GetId()).ToArray()));

                // A second /loadout only does the difference: pieces already worn at the top tier are left alone.
                var todo = items.Where(i => !equipped.GetInsideWorldObjects().Any(w => w.GetGroup().GetId() == i.GetId())).ToList();
                already.AddRange(items.Where(i => !todo.Contains(i)).Select(i => i.GetId()));
                if (todo.Count == 0)
                {
                    ChatHook.Say("Your loadout is already complete (" + already.Count + " pieces at the top tier).");
                    completed = true;
                    yield break;
                }

                ChatHook.Say("Fitting " + todo.Count + " pieces of gear...");
                foreach (var item in todo)
                {
                    var id = item.GetId();

                    // A lower tier of the same family is worn: take it off first (it goes to the backpack) and replace it.
                    var old = equipped.GetInsideWorldObjects().FirstOrDefault(w => w.GetGroup() is GroupItem g && SlotOf(g) == SlotOf(item));
                    if (old != null)
                    {
                        equipment.RemoveItemFromEquipment(old);
                        yield return WaitUntil(() => !equipped.GetInsideWorldObjects().Contains(old), 5f);
                        if (equipped.GetInsideWorldObjects().Contains(old))
                        {
                            failed.Add(id + " (could not take off " + old.GetGroup().GetId() + ")");
                            continue;
                        }
                    }

                    var done = false;
                    var ok = false;
                    var newId = 0;
                    InventoriesHandler.Instance.AddItemToInventory(item, backpack, (success, woId) =>
                    {
                        ok = success;
                        newId = woId;
                        done = true;
                    });
                    yield return WaitUntil(() => done, 5f);

                    var wo = ok ? WorldObjectsHandler.Instance.GetWorldObjectViaId(newId) : null;
                    if (wo != null)
                    {
                        equipment.AddItemInEquipment(wo);
                        yield return WaitUntil(() => equipped.GetInsideWorldObjects().Contains(wo), 5f);
                    }

                    if (wo == null || !equipped.GetInsideWorldObjects().Contains(wo))
                    {
                        failed.Add(id + (wo == null ? " (could not create it in the backpack)" : " (left in the backpack)") + (old == null ? "" : ", " + old.GetGroup().GetId() + " put back"));
                        if (old != null)
                        {
                            equipment.AddItemInEquipment(old);
                            yield return WaitUntil(() => equipped.GetInsideWorldObjects().Contains(old), 5f);
                        }

                        continue;
                    }

                    if (old == null)
                    {
                        fitted.Add(id);
                    }
                    else
                    {
                        replaced.Add(old.GetGroup().GetId() + " -> " + id);
                        try
                        {
                            InventoriesHandler.Instance.RemoveItemFromInventory(old, backpack, true);
                        }
                        catch (Exception e)
                        {
                            ModLog.Note("Loadout: could not remove the old " + old.GetGroup().GetId() + " from the backpack (left there): " + e.Message);
                        }
                    }

                    ModLog.Note("Loadout: fitted " + id + " #" + newId + (old == null ? "." : " in place of " + old.GetGroup().GetId() + "."));

                    // The size upgrades add their slots a moment later.
                    if (id.StartsWith("EquipmentIncrease") || id.StartsWith("Backpack"))
                        yield return new WaitForSecondsRealtime(1.5f);
                }

                completed = true;
            }
            finally
            {
                _running = false;
                if (!completed)
                    ChatHook.Say("The loadout stopped early (" + (fitted.Count + replaced.Count) + " done so far); see the mod's log.");
            }

            ChatHook.Say("Loadout: " + fitted.Count + " added, " + replaced.Count + " upgraded" + (replaced.Count == 0 ? "" : " (" + string.Join(", ", replaced.ToArray()) + ")")
                + ", " + already.Count + " already at the top tier" + (failed.Count == 0 ? "." : ", " + failed.Count + " failed: " + string.Join("; ", failed.ToArray())));
        }
    }
}
