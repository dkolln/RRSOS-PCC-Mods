using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.AutoFill
{
    /// <summary>
    /// When a crate is given a label, looks the label up: if it names an item (its group id, its display name, or an extra name) the crate's EMPTY slots are filled with that
    /// item. Only at a rename; what is already in the crate stays; a crate that already had its label when the world loaded is never touched.
    /// /addname works in the chat and, when a terminal is installed, on the terminal. (/resupply and the per-crate resupply icon are the Resupply mod.)
    /// </summary>
    internal sealed class AutoFiller : MonoBehaviour
    {
        private const float PollSeconds = 0.5f;

        private readonly ItemLookup _lookup = new ItemLookup();

        // crate id -> the label we have already dealt with
        private readonly Dictionary<int, string> _handled = new Dictionary<int, string>();

        // crate id -> a label that changed and has not settled yet (the game writes the label while it is typed, letter by letter)
        private readonly Dictionary<int, KeyValuePair<string, float>> _pending = new Dictionary<int, KeyValuePair<string, float>>();

        private string _worldKey;
        private bool _baselineDone;
        private bool _busy;
        private bool _terminalChecked;

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(PollSeconds);

                if (!Plugin.Enabled.Value)
                    continue;

                if (!_terminalChecked)
                    RegisterWithTerminal();

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

        private HashSet<string> CrateGroupIds() =>
            new HashSet<string>(Plugin.CrateGroups.Value.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0));

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

                var item = _lookup.Resolve(label);
                if (item == null)
                {
                    ModLog.Note("Crate #" + id + " labelled \"" + label + "\": not an item name, nothing to fill.");
                    continue;
                }

                jobs.Add(new KeyValuePair<WorldObject, GroupItem>(crate, item));
            }

            return jobs;
        }

        // ---- the rename fill --------------------------------------------------------------------------------------------------

        /// <summary>The fill after a rename, with its answer in the chat.</summary>
        private IEnumerator Fill(WorldObject crate, GroupItem item)
        {
            var result = new CrateFill.Result();
            yield return CrateFill.Fill(crate, item, result);

            if (result.Empty < 0)
                yield break;

            var name = Readable.GetGroupName(item);
            if (result.Empty == 0)
                ChatHook.Say("\"" + CrateFill.Label(crate) + "\" is " + name + ", but the crate has no empty slots.");
            else
                ChatHook.Say("Filled " + result.Added + " empty slot" + (result.Added == 1 ? "" : "s") + " with " + name + ".");
        }

        // ---- /addname ---------------------------------------------------------------------------------------------------------

        /// <summary>/addname &lt;item id&gt; &lt;name&gt;: gives an item one more name, saved in aliases.txt and used straight away.</summary>
        internal void OnAddName(string args) => AddName(args, ChatHook.Say);

        /// <summary>The same, answering through the given function: the chat's reply, or a line on a terminal's screen.</summary>
        private void AddName(string args, Action<string> say)
        {
            if (GroupsHandler.GetAllGroups() == null)
            {
                say("The game is not ready yet.");
                return;
            }

            var parts = (args ?? "").Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                say("Usage: /addname <item id> <name>, for example /addname waterbottle1 water");
                return;
            }

            var name = parts[1].Trim();
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
            ModLog.Append(ItemLookup.AliasFile, item.GetId() + " = " + name);
            say("Name: " + name + " added.");
        }

        // ---- /addname (on the terminal) -----------------------------------------------------------------------------------------

        // The terminal is optional. When its mod is loaded this puts /addname on it; otherwise there is nothing to do and the mod works as before.
        private void RegisterWithTerminal()
        {
            try
            {
                if (TerminalRegistration.TryRegister("/addname", "/addname <item id> <name>", "give an item another name", (args, print) => AddName(args, print)))
                {
                    _terminalChecked = true;
                    ModLog.Note("The terminal is installed: /addname is on it.");
                }
            }
            catch (Exception e)
            {
                _terminalChecked = true; // do not try again every half second
                ModLog.Note("Could not register /addname with the terminal: " + e.Message);
            }
        }
    }
}
