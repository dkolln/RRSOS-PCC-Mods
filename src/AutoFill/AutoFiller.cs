using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.AutoFill
{
    /// <summary>
    /// When a crate is given a label, looks the label up: if it names an item (its group id, its display name, or an alias from aliases.txt) the crate's EMPTY slots are
    /// filled with that item. Only at a rename; what is already in the crate stays; a crate that already had its label when the world loaded is never touched.
    /// </summary>
    internal sealed class AutoFiller : MonoBehaviour
    {
        private const float PollSeconds = 0.5f;
        private const float WaitForItemSeconds = 3f;

        // crate id -> the label we have already dealt with
        private readonly Dictionary<int, string> _handled = new Dictionary<int, string>();

        // crate id -> a label that changed and has not settled yet (the game writes the label while it is typed, letter by letter)
        private readonly Dictionary<int, KeyValuePair<string, float>> _pending = new Dictionary<int, KeyValuePair<string, float>>();

        private string _worldKey;
        private bool _baselineDone;
        private bool _busy;

        // The lookup, built once the game's groups are there: lower-case item id -> item; plain name -> the items that go by it; plain name -> item from the user's file.
        private Dictionary<string, GroupItem> _byId;
        private Dictionary<string, HashSet<GroupItem>> _byName;
        private Dictionary<string, GroupItem> _explicit;
        private readonly HashSet<string> _toldAmbiguous = new HashSet<string>();
        private int _lookupBuiltFor;

        private static string AliasFile => ModLog.FileIn("aliases.txt");

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(PollSeconds);

                if (_busy || !Plugin.Enabled.Value)
                    continue;

                string key;
                try
                {
                    if (!WorldState.Ready(out _, out key))
                        continue;
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("autofill-ready", "Could not tell whether the world is ready: " + e.Message);
                    continue;
                }

                if (key != _worldKey)
                {
                    _worldKey = key;
                    _handled.Clear();
                    _pending.Clear();
                    _baselineDone = false;
                }

                _busy = true;
                List<KeyValuePair<WorldObject, GroupItem>> work = null;
                try
                {
                    work = Scan();
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("autofill-scan", "Could not scan the crates: " + e.GetType().Name + ": " + e.Message);
                }

                if (work != null)
                {
                    foreach (var job in work)
                        yield return Fill(job.Key, job.Value);
                }

                _busy = false;
            }
        }

        /// <summary>Notes every crate's label. Returns the crates whose label has just been settled on an item name.</summary>
        private List<KeyValuePair<WorldObject, GroupItem>> Scan()
        {
            var crateGroups = new HashSet<string>(Plugin.CrateGroups.Value.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0));
            var crates = WorldObjectsHandler.Instance.GetAllWorldObjects().Values
                .Where(w => w != null && w.GetGroup() != null && crateGroups.Contains(w.GetGroup().GetId()) && w.GetLinkedInventoryId() != 0).ToList();

            if (!_baselineDone)
            {
                // The labels that are there when the world loads are not renames.
                foreach (var crate in crates)
                    _handled[crate.GetId()] = Label(crate);
                _baselineDone = true;
                return null;
            }

            var settleSeconds = Plugin.SettleSeconds.Value;
            var windowOpen = Managers.GetManager<WindowsHandler>().GetHasUiOpen();
            var jobs = new List<KeyValuePair<WorldObject, GroupItem>>();
            foreach (var crate in crates)
            {
                var id = crate.GetId();
                var label = Label(crate);
                if (!_handled.TryGetValue(id, out var before))
                {
                    _handled[id] = label; // a crate placed since the last look: the label it starts with is its baseline
                    continue;
                }

                if (label == before)
                {
                    _pending.Remove(id);
                    continue;
                }

                var now = Time.realtimeSinceStartup;
                if (!_pending.TryGetValue(id, out var pending) || pending.Key != label)
                {
                    _pending[id] = new KeyValuePair<string, float>(label, now);
                    continue;
                }

                // Settled: unchanged for a while, and the naming window is closed.
                if (now - pending.Value < settleSeconds || windowOpen)
                    continue;

                _pending.Remove(id);
                _handled[id] = label;
                if (label.Length == 0)
                    continue;

                var item = Resolve(label);
                if (item == null)
                {
                    ModLog.Note("Crate #" + id + " labelled \"" + label + "\": not an item name, nothing to fill.");
                    continue;
                }

                jobs.Add(new KeyValuePair<WorldObject, GroupItem>(crate, item));
            }

            return jobs;
        }

        private static string Label(WorldObject crate) => (crate.GetText() ?? "").Trim();

        // ---- the lookup ------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Whole-name matches only, never part of a name. A label matches an item when, ignoring case, spaces and punctuation, it equals the item's id, its display name in the
        /// game, one of its extra names in DefaultNames.txt, or one of the names in the user's aliases.txt (which win). A name that two items go by is not guessed at.
        /// </summary>
        private GroupItem Resolve(string label)
        {
            BuildLookup();
            var plain = Plain(label);
            if (plain.Length == 0)
                return null;

            if (_explicit.TryGetValue(plain, out var chosen))
                return chosen;

            if (_byId.TryGetValue(label.Trim().ToLowerInvariant(), out var viaId))
                return viaId;

            if (_byName.TryGetValue(plain, out var items))
            {
                if (items.Count == 1)
                    return items.First();

                if (_toldAmbiguous.Add(plain))
                    ModLog.Note("The label \"" + label + "\" fits several items (" + string.Join(", ", items.Select(i => i.GetId()).OrderBy(i => i).ToArray()) + "); nothing filled. Name one in aliases.txt to pick.");
            }

            return null;
        }

        /// <summary>The one item a name already goes to, or null when it goes to none or to several.</summary>
        private GroupItem ExistingItem(string label)
        {
            var plain = Plain(label);
            if (_explicit.TryGetValue(plain, out var chosen))
                return chosen;
            if (_byId.TryGetValue(label.Trim().ToLowerInvariant(), out var viaId))
                return viaId;
            if (_byName.TryGetValue(plain, out var items) && items.Count == 1)
                return items.First();
            return null;
        }

        /// <summary>/addname &lt;item id&gt; &lt;name&gt;: gives an item one more name, saved in aliases.txt and used straight away.</summary>
        internal void OnAddName(string args)
        {
            if (GroupsHandler.GetAllGroups() == null)
            {
                ChatHook.Say("The game is not ready yet.");
                return;
            }

            BuildLookup();
            var parts = (args ?? "").Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                ChatHook.Say("Usage: /addname <item id> <name>, for example /addname waterbottle1 water");
                return;
            }

            var name = parts[1].Trim();
            if (!_byId.TryGetValue(parts[0].ToLowerInvariant(), out var item))
            {
                ChatHook.Say("No item has the id \"" + parts[0] + "\".");
                return;
            }

            if (Plain(name).Length == 0)
            {
                ChatHook.Say("The name needs at least one letter or digit.");
                return;
            }

            var existing = ExistingItem(name);
            if (existing != null)
            {
                ChatHook.Say("Name: " + name + " already exists" + (existing == item ? "." : " (it names " + Readable.GetGroupName(existing) + ")."));
                return;
            }

            _explicit[Plain(name)] = item;
            ModLog.Append(AliasFile, item.GetId() + " = " + name);
            ChatHook.Say("Name: " + name + " added.");
        }

        /// <summary>A name with only its letters and digits, lower case, so "Oxygen capsule", "oxygen-capsule" and "OXYGEN CAPSULE" are one name.</summary>
        private static string Plain(string text) => new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        private void BuildLookup()
        {
            var all = GroupsHandler.GetAllGroups();
            if (_byId != null && _lookupBuiltFor == all.Count)
                return;

            _byId = new Dictionary<string, GroupItem>();
            _byName = new Dictionary<string, HashSet<GroupItem>>();
            _explicit = new Dictionary<string, GroupItem>();
            _toldAmbiguous.Clear();

            foreach (var group in all)
            {
                var item = group as GroupItem;
                if (item == null)
                    continue;

                _byId[item.GetId().ToLowerInvariant()] = item;
                AddName(Plain(item.GetId()), item);
                try
                {
                    AddName(Plain(Readable.GetGroupName(item) ?? ""), item);
                }
                catch (Exception)
                {
                }
            }

            var extra = 0;
            foreach (var pair in ReadNames(ReadDefaultNames()))
            {
                if (_byId.TryGetValue(pair.Key.ToLowerInvariant(), out var item))
                {
                    foreach (var name in pair.Value)
                    {
                        AddName(Plain(name), item);
                        extra++;
                    }
                }
            }

            var mine = 0;
            foreach (var pair in ReadNames(ReadUserNames()))
            {
                if (_byId.TryGetValue(pair.Key.ToLowerInvariant(), out var item))
                {
                    foreach (var name in pair.Value)
                    {
                        _explicit[Plain(name)] = item;
                        mine++;
                    }
                }
                else
                {
                    ModLog.Note("aliases.txt: \"" + pair.Key + "\" is not an item id; its names are ignored.");
                }
            }

            _lookupBuiltFor = all.Count;
            ModLog.Note("Item lookup built: " + _byId.Count + " item ids, " + _byName.Count + " names (" + extra + " extra names from the default table), " + mine + " names from aliases.txt.");
        }

        private void AddName(string plain, GroupItem item)
        {
            if (plain.Length == 0)
                return;

            if (!_byName.TryGetValue(plain, out var set))
                _byName[plain] = set = new HashSet<GroupItem>();
            set.Add(item);
        }

        /// <summary>Lines of "item id = name, name, ..." ("#" starts a comment).</summary>
        private static Dictionary<string, List<string>> ReadNames(IEnumerable<string> lines)
        {
            var result = new Dictionary<string, List<string>>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var at = trimmed.IndexOf('=');
                if (trimmed.StartsWith("#") || at <= 0)
                    continue;

                var names = trimmed.Substring(at + 1).Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
                var id = trimmed.Substring(0, at).Trim();
                if (result.TryGetValue(id, out var existing))
                    existing.AddRange(names);
                else
                    result[id] = names;
            }

            return result;
        }

        private static IEnumerable<string> ReadDefaultNames()
        {
            using (var stream = typeof(AutoFiller).Assembly.GetManifestResourceStream("DefaultNames.txt"))
            {
                if (stream == null)
                    return new string[0];

                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd().Split('\n');
            }
        }

        /// <summary>aliases.txt in this mod's data folder: "item id = name, name, ...", the same format as the default table. Made with a few comment lines the first time.</summary>
        private static IEnumerable<string> ReadUserNames()
        {
            try
            {
                if (!File.Exists(AliasFile))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(AliasFile));
                    File.WriteAllText(AliasFile,
                        "# Your own names for items: item id = name, name, ...\r\n"
                        + "# A crate label must equal a whole name (case, spaces and punctuation do not matter). Names here win over the built-in ones.\r\n"
                        + "# Example (remove the #):\r\n"
                        + "# OxygenCapsule1 = oxygen capsule, oxygen bottle, oxygen, O2\r\n");
                    return new string[0];
                }

                return File.ReadAllLines(AliasFile);
            }
            catch (Exception e)
            {
                ModLog.Note("Could not read aliases.txt: " + e.Message);
                return new string[0];
            }
        }

        // ---- the fill --------------------------------------------------------------------------------------------------------

        /// <summary>Adds the item to each empty slot of the crate, one at a time through the game's own add-to-inventory call. What is in the crate already is never touched.</summary>
        private IEnumerator Fill(WorldObject crate, GroupItem item)
        {
            var name = Readable.GetGroupName(item);
            var label = Label(crate);
            var inventory = InventoriesHandler.Instance.GetInventoryById(crate.GetLinkedInventoryId());
            if (inventory == null)
            {
                ModLog.Note("Crate #" + crate.GetId() + ": no inventory found; nothing filled.");
                yield break;
            }

            var empty = inventory.GetSize() - inventory.GetInsideWorldObjects().Count;
            if (empty <= 0)
            {
                ChatHook.Say("\"" + label + "\" is " + name + ", but the crate has no empty slots.");
                yield break;
            }

            var added = 0;
            for (var i = 0; i < empty; i++)
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
                added++;
            }

            ModLog.Note("Crate #" + crate.GetId() + " labelled \"" + label + "\": added " + added + " x " + item.GetId() + " (" + empty + " empty slots).");
            ChatHook.Say("Filled " + added + " empty slot" + (added == 1 ? "" : "s") + " with " + name + ".");
        }
    }
}
