using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SpaceCraft;
using Unity.Netcode;
using UnityEngine;

namespace RRSOS.PCC.Build
{
    /// <summary>
    /// Slash commands in the game's own chat: /build (place stage 1 of the base), /loadout (the top tier of every piece of personal gear) and /undo
    /// (take back what the last command placed). The game keeps its chat commands in a private dictionary on ChatHandler (/help, /list, /kick, ...); this adds
    /// ours to it every time a world creates a new ChatHandler. Host only, and only while Enabled is true in the config.
    /// </summary>
    internal sealed class ChatCommands : MonoBehaviour
    {
        private sealed class Record
        {
            public string Label;
            public List<int> Ids;
        }

        private static readonly List<Record> History = new List<Record>();
        private ChatHandler _registeredOn;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(1f);

                try
                {
                    var chat = ChatHandler.Instance;
                    if (!Plugin.Enabled.Value || chat == null || chat == _registeredOn)
                        continue;

                    var field = typeof(ChatHandler).GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic);
                    var commands = field == null ? null : field.GetValue(chat) as Dictionary<string, Action<string>>;
                    if (commands == null)
                    {
                        BaseBuilder.Note("Could not reach the chat's command list; /build, /undo and /loadout are not available (the game may have changed).");
                        _registeredOn = chat; // do not repeat every second
                        continue;
                    }

                    commands["/build"] = Guarded(Build);
                    commands["/undo"] = Guarded(Undo);
                    commands["/loadout"] = Guarded(LoadoutStart);

                    // The game's /help is a fixed text; add ours after it.
                    if (commands.TryGetValue("/help", out var gameHelp))
                        commands["/help"] = args =>
                        {
                            gameHelp(args);
                            chat.SendServiceMessage("RRSOS: /build (place the base), /undo (take back the last command), /loadout (top-tier gear)");
                        };
                    _registeredOn = chat;
                    BaseBuilder.Note("Chat commands ready: /build, /undo, /loadout.");
                }
                catch (Exception e)
                {
                    BaseBuilder.Note("Could not add the chat commands: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        // A command that throws must never break the chat.
        private static Action<string> Guarded(Action<string> command)
        {
            return args =>
            {
                try
                {
                    if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                    {
                        Say("Only the host can use this.");
                        return;
                    }

                    command(args ?? "");
                }
                catch (Exception e)
                {
                    BaseBuilder.Note("Command failed: " + e.GetType().Name + ": " + e.Message);
                    Say("That did not work (" + e.Message + "). See the build log.");
                }
            };
        }

        internal static void Say(string message)
        {
            BaseBuilder.Note("chat: " + message);
            if (ChatHandler.Instance != null)
                ChatHandler.Instance.SendServiceMessage(message);
        }

        /// <summary>Remembers what a command placed, so /undo can take it back.</summary>
        internal static void Remember(string label, IEnumerable<WorldObject> placed)
        {
            History.Add(new Record { Label = label, Ids = placed.Select(w => w.GetId()).ToList() });
        }

        // ---- /build ------------------------------------------------------------------------------------------------------------

        private static void Build(string args)
        {
            if (!BaseBuilder.WorldReady(out var planetHash, out _))
            {
                Say("The world is not ready yet.");
                return;
            }

            if (!BaseBuilder.OnTemplatePlanet())
            {
                Say("No template location is set for this planet.");
                return;
            }

            BaseBuilder.BuildRequested = true;
            Say("Building the base...");
        }

        // ---- /loadout ----------------------------------------------------------------------------------------------------------

        private static bool _loadoutRunning;

        private void LoadoutStart(string args)
        {
            if (_loadoutRunning)
            {
                Say("The loadout is still being fitted.");
                return;
            }

            StartCoroutine(LoadoutRoutine());
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

        private IEnumerator LoadoutRoutine()
        {
            _loadoutRunning = true;
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
                BaseBuilder.Note("Loadout: " + string.Join(", ", items.Select(i => i.GetId()).ToArray()));

                // A second /loadout only does the difference: pieces already worn at the top tier are left alone.
                var todo = items.Where(i => !equipped.GetInsideWorldObjects().Any(w => w.GetGroup().GetId() == i.GetId())).ToList();
                already.AddRange(items.Where(i => !todo.Contains(i)).Select(i => i.GetId()));
                if (todo.Count == 0)
                {
                    Say("Your loadout is already complete (" + already.Count + " pieces at the top tier).");
                    completed = true;
                    yield break;
                }

                Say("Fitting " + todo.Count + " pieces of gear...");
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
                            BaseBuilder.Note("Loadout: could not remove the old " + old.GetGroup().GetId() + " from the backpack (left there): " + e.Message);
                        }
                    }

                    BaseBuilder.Note("Loadout: fitted " + id + " #" + newId + (old == null ? "." : " in place of " + old.GetGroup().GetId() + "."));

                    // The size upgrades add their slots a moment later.
                    if (id.StartsWith("EquipmentIncrease") || id.StartsWith("Backpack"))
                        yield return new WaitForSecondsRealtime(1.5f);
                }

                completed = true;
            }
            finally
            {
                _loadoutRunning = false;
                if (!completed)
                    Say("The loadout stopped early (" + (fitted.Count + replaced.Count) + " done so far); see the build log.");
            }

            Say("Loadout: " + fitted.Count + " added, " + replaced.Count + " upgraded" + (replaced.Count == 0 ? "" : " (" + string.Join(", ", replaced.ToArray()) + ")")
                + ", " + already.Count + " already at the top tier" + (failed.Count == 0 ? "." : ", " + failed.Count + " failed: " + string.Join("; ", failed.ToArray())));
        }

        // ---- /undo -------------------------------------------------------------------------------------------------------------

        private static void Undo(string args)
        {
            if (History.Count == 0)
            {
                Say("Nothing to undo (this only remembers what commands placed since the game started).");
                return;
            }

            var last = History[History.Count - 1];
            History.RemoveAt(History.Count - 1);

            var handler = WorldObjectsHandler.Instance;
            var removed = 0;
            foreach (var id in last.Ids)
            {
                if (handler.GetWorldObjectViaId(id) != null)
                {
                    handler.DestroyWorldObject(id, true);
                    removed++;
                }
            }

            Say("Undid " + last.Label + " (" + removed + " pieces removed).");
        }
    }
}
