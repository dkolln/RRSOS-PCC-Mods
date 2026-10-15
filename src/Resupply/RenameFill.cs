using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Resupply
{
    /// <summary>
    /// When a crate is given a label, looks the label up: if it names an item (its group id, its display name, or an extra name) the crate's EMPTY slots are filled with that
    /// item. Only at a rename; what is already in the crate stays; a crate that already had its label when the world loaded is never touched. Switched by FillOnRename.
    /// Also the /resupply add chat command, which gives an item one more name for labels (saved in the shared aliases.txt and used at once). This used to be the Auto Fill mod.
    /// </summary>
    internal sealed class RenameFill : MonoBehaviour
    {
        private const float PollSeconds = 0.5f;

        // crate id -> the label we have already dealt with
        private readonly Dictionary<int, string> _handled = new Dictionary<int, string>();

        // crate id -> a label that changed and has not settled yet (the game writes the label while it is typed, letter by letter)
        private readonly Dictionary<int, KeyValuePair<string, float>> _pending = new Dictionary<int, KeyValuePair<string, float>>();

        private string _worldKey;
        private bool _baselineDone;
        private bool _busy;

        // one lookup for the whole mod, so a name added with /resupply add is known to the icon, the Supply Manager and this at once
        private ItemLookup Lookup => Resupplier.Instance.Lookup;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(PollSeconds);

                if (!Plugin.Enabled.Value || !Plugin.FillOnRename.Value || Resupplier.Instance == null)
                    continue;

                if (_busy)
                    continue;

                string key;
                try
                {
                    if (!WorldState.Ready(out _, out key))
                        continue;
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("rename-ready", "Could not tell whether the world is ready: " + e.Message);
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
                    ModLog.LogOnce("rename-scan", "Could not scan the crates: " + e.GetType().Name + ": " + e.Message);
                }

                if (work != null)
                {
                    foreach (var job in work)
                        yield return Fill(job.Key, job.Value);
                }

                _busy = false;
            }
        }

        private HashSet<string> CrateGroupIds() =>
            new HashSet<string>(Plugin.Groups.Value.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0));

        /// <summary>Notes every crate's label. Returns the crates whose label has just been settled on an item name.</summary>
        private List<KeyValuePair<WorldObject, GroupItem>> Scan()
        {
            var crateGroups = CrateGroupIds();
            var crates = WorldObjectsHandler.Instance.GetAllWorldObjects().Values
                .Where(w => w != null && w.GetGroup() != null && crateGroups.Contains(w.GetGroup().GetId()) && w.GetLinkedInventoryId() != 0).ToList();

            if (!_baselineDone)
            {
                // The labels that are there when the world loads are not renames.
                foreach (var crate in crates)
                    _handled[crate.GetId()] = CrateFill.Label(crate);
                _baselineDone = true;
                return null;
            }

            var settleSeconds = Plugin.SettleSeconds.Value;
            var windowOpen = Managers.GetManager<WindowsHandler>().GetHasUiOpen();
            var jobs = new List<KeyValuePair<WorldObject, GroupItem>>();
            foreach (var crate in crates)
            {
                var id = crate.GetId();
                var label = CrateFill.Label(crate);
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

                var item = Lookup.Resolve(label);
                if (item == null)
                {
                    ModLog.Note("Crate #" + id + " labelled \"" + label + "\": not an item name, nothing to fill.");
                    continue;
                }

                jobs.Add(new KeyValuePair<WorldObject, GroupItem>(crate, item));
            }

            return jobs;
        }

        /// <summary>The fill after a rename, with its answer in the chat.</summary>
        private IEnumerator Fill(WorldObject crate, GroupItem item)
        {
            var result = new CrateFill.Result();
            yield return CrateFill.Fill(crate, item, result);

            if (result.Empty < 0)
                yield break;

            var name = Readable.GetGroupName(item);
            if (result.Empty == 0)
                TerminalRegistration.Notice("\"" + CrateFill.Label(crate) + "\" is " + name + ", but the crate has no empty slots.");
            else
                TerminalRegistration.Notice("Filled " + result.Added + " empty slot" + (result.Added == 1 ? "" : "s") + " with " + name + ".");
        }

        // ---- /resupply add ---------------------------------------------------------------------------------------------------------

        /// <summary>/resupply on its own fills every crate. (/resupply add and /resupply duplicate dna still work: they are /add and /duplicate now.)</summary>
        internal void OnResupplyCommand(string args)
        {
            var words = (args ?? "").Trim().Split(new[] { ' ', '	' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                ResupplyAllNow();
            else if (string.Equals(words[0], "add", StringComparison.OrdinalIgnoreCase))
                AddName(words.Length > 1 ? words[1] : "");
            else if (string.Equals(words[0], "duplicate", StringComparison.OrdinalIgnoreCase))
                OnDuplicateCommand(words.Length > 1 ? words[1] : "");
            else
                ChatHook.Say("Usage: /resupply (fill every crate). See also /add <item id> <name> and /duplicate <trait | dna | item>.");
        }

        /// <summary>/add &lt;item id&gt; &lt;name&gt;: another name for an item.</summary>
        internal void OnAddCommand(string args) => AddName(args);

        /// <summary>/duplicate trait | dna | &lt;item&gt;: a second copy of every such item in the backpack.</summary>
        internal void OnDuplicateCommand(string args)
        {
            var what = (args ?? "").Trim();
            if (what.Length == 0)
            {
                ChatHook.Say("Usage: /duplicate trait, /duplicate dna, or /duplicate <item> (a second copy of every one of them in the backpack).");
                return;
            }

            var key = ItemLookup.Plain(what);
            if (key == "trait" || key == "traits")
                StartCoroutine(DnaDuplicate.Run(ChatHook.Say, id => id == "GeneticTrait", "trait"));
            else if (key == "dna")
                StartCoroutine(DnaDuplicate.Run(ChatHook.Say, id => id == "GeneticTrait" || id == "DNASequence", "DNA"));
            else
            {
                var item = Resupplier.Instance == null ? null : Lookup.Resolve(what);
                if (item == null)
                    ChatHook.Say("No item is called \"" + what + "\".");
                else
                    StartCoroutine(DnaDuplicate.Run(ChatHook.Say, id => id == item.GetId(), Readable.GetGroupName(item)));
            }
        }

        /// <summary>/resupply on its own: every crate with an item is filled, as the Supply Manager's Resupply button does. One line in the chat when done.</summary>
        private void ResupplyAllNow()
        {
            var resupplier = Resupplier.Instance;
            if (resupplier == null || resupplier.Busy)
            {
                ChatHook.Say("Still busy filling crates; try again in a moment.");
                return;
            }

            StartCoroutine(RunResupplyAll(resupplier));
        }

        private IEnumerator RunResupplyAll(Resupplier resupplier)
        {
            var lines = new List<string>();
            yield return resupplier.ResupplyAll(line => lines.Add(line));
            ChatHook.Say(lines.LastOrDefault(l => l.StartsWith("Done:")) ?? lines.LastOrDefault() ?? "Done.");
        }

        private void AddName(string args)
        {
            var say = (Action<string>)ChatHook.Say;
            if (GroupsHandler.GetAllGroups() == null || Resupplier.Instance == null)
            {
                say("The game is not ready yet.");
                return;
            }

            var parts = (args ?? "").Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                say("Usage: /add <item id> <name>, for example /add waterbottle1 water");
                return;
            }

            var name = parts[1].Trim();
            var item = Lookup.ById(parts[0]);
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

            var existing = Lookup.Existing(name);
            if (existing != null)
            {
                say("Name: " + name + " already exists" + (existing == item ? "." : " (it names " + Readable.GetGroupName(existing) + ")."));
                return;
            }

            Lookup.AddExplicit(name, item);
            ModLog.Append(ItemLookup.AliasFile, item.GetId() + " = " + name);
            say("Name: " + name + " added.");
        }
    }
}
