using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Base
{
    /// <summary>
    /// Keeps buildings up to date while the game is running, by a list of upgrade chains (upgrades.txt, one chain per line, for example
    /// "EnergyGenerator1 > EnergyGenerator2 > EnergyGenerator3"). As soon as a later step of a chain is unlocked, every building of an earlier step is swapped, in the same spot,
    /// for the highest step that is unlocked. Anything stored inside the old building (the seeds in a vegetube) is moved into the new one. The new building is placed before the
    /// old one is removed, and the old one is kept if the new one cannot be placed or its contents cannot be moved, so nothing is lost.
    /// With the scope setting on World this applies to every such building on the planet; on Base only to the pieces this mod placed.
    /// </summary>
    internal sealed class BaseUpgrades : MonoBehaviour
    {
        private const string DefaultRules = @"# Upgrade chains: when a later step is unlocked, the earlier steps in the world are swapped for it (in the same spot).
# One chain per line, steps separated by >. A building that is not at the start of a chain is upgraded from wherever it stands in it.
# Anything inside the old building (seeds in a vegetube) is moved to the new one. Lines starting with # are notes.
# After the steps a line may add ; label=Text ; color=green ; only=1 : the last step is then made with that label and colour,
# and only=N means only when exactly N buildings of the swapped kind stand in the world (the swap itself happens either way).
# Colours: green red blue yellow orange purple cyan white.

# Wind Turbine > T1 Solar Panel > T2 Solar Panel
EnergyGenerator1 > EnergyGenerator2 > EnergyGenerator3

# Heater T1 > T2 > T3
Heater1 > Heater2 > Heater3

# Heater T4 > T5
Heater4 > Heater5

# Vegetube T1 > T2 (the seeds go with it)
Vegetube1 > Vegetube2

# Drill T1 > T2 > T3 > T4 > T5
Drill0 > Drill1 > Drill2 > Drill3 > Drill4

# Outdoor lamp > Beacon; when there is only one lamp (the base's) it becomes the beacon ""Base"" in green
OutsideLamp1 > Beacon ; label=Base ; color=green ; only=1
";

        private const int PiecesPerFrame = 4;
        private const float WaitSeconds = 4f;

        private static string RulesFile => ModLog.FileIn("upgrades.txt");

        /// <summary>One line of upgrades.txt: the steps, and what the last step gets when it is made (a label and a colour, optionally only when exactly N of the first kind exist).</summary>
        private sealed class Chain
        {
            public string[] Steps;
            public string Label;
            public string Color;     // the game's own colour text
            public int OnlyIf;       // 0 = always; N = only when exactly N buildings of the swapped kind stand in the world
        }

        // colour names that can be written in upgrades.txt
        private static readonly Dictionary<string, Color> ColorNames = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "green", new Color(0.1f, 0.9f, 0.1f, 1f) }, { "red", new Color(0.95f, 0.1f, 0.1f, 1f) }, { "blue", new Color(0.15f, 0.35f, 1f, 1f) },
            { "yellow", new Color(1f, 0.9f, 0.1f, 1f) }, { "orange", new Color(1f, 0.55f, 0.05f, 1f) }, { "purple", new Color(0.6f, 0.2f, 0.9f, 1f) },
            { "cyan", new Color(0.1f, 0.9f, 0.9f, 1f) }, { "white", Color.white },
        };

        private List<Chain> _chains = new List<Chain>();
        private DateTime _rulesRead = DateTime.MinValue;

        // ---- the loop ---------------------------------------------------------------------------------------------------------

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(3f);

                if (!Plugin.Enabled.Value || !Plugin.AutoUpgrade.Value || BaseBuilder.Busy)
                    continue;

                string key = null;
                var planetHash = 0;
                var ready = false;
                try
                {
                    ready = WorldState.Ready(out planetHash, out key);
                    if (ready)
                        LoadRules();
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("upgrades-ready", "Upgrades: " + e.GetType().Name + ": " + e.Message);
                }

                if (!ready || _chains.Count == 0)
                    continue;

                BaseBuilder.Busy = true;
                try
                {
                    yield return Run(key, planetHash);
                    yield return RunGrowth(key, planetHash);
                    yield return RunStages(key, planetHash);
                }
                finally
                {
                    BaseBuilder.Busy = false;
                }
            }
        }

        // ---- the rules --------------------------------------------------------------------------------------------------------

        private void LoadRules()
        {
            if (!File.Exists(RulesFile))
                File.WriteAllText(RulesFile, DefaultRules.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));

            var written = File.GetLastWriteTimeUtc(RulesFile);
            if (written == _rulesRead)
                return;

            _rulesRead = written;
            _chains = new List<Chain>();
            foreach (var raw in File.ReadAllLines(RulesFile))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                var sections = line.Split(';');
                var steps = sections[0].Split('>').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
                var unknown = steps.Where(s => GroupsHandler.GetGroupViaId(s) == null).ToArray();
                if (steps.Length < 2 || unknown.Length > 0)
                {
                    ModLog.Note("upgrades.txt: skipped \"" + line + "\"" + (unknown.Length > 0 ? " (the game has no group " + string.Join(", ", unknown) + ")" : " (a chain needs at least two steps)") + ".");
                    continue;
                }

                var chain = new Chain { Steps = steps };
                foreach (var option in sections.Skip(1))
                {
                    var pair = option.Split(new[] { '=' }, 2);
                    var name = pair[0].Trim().ToLowerInvariant();
                    var value = pair.Length > 1 ? pair[1].Trim() : "";
                    if (name == "label")
                        chain.Label = value;
                    else if (name == "color" && ColorNames.TryGetValue(value, out var colour))
                        chain.Color = DataTreatments.ColorToString(colour);
                    else if (name == "only" && int.TryParse(value, out var only))
                        chain.OnlyIf = only;
                    else
                        ModLog.Note("upgrades.txt: did not understand \"" + option.Trim() + "\" in \"" + line + "\".");
                }

                _chains.Add(chain);
            }

            ModLog.Note("upgrades.txt: " + _chains.Count + " chains: " + string.Join("; ", _chains.Select(c => string.Join(" > ", c.Steps)).ToArray()));
        }

        private static bool Unlocked(string groupId)
        {
            var group = GroupsHandler.GetGroupViaId(groupId);
            if (group == null)
                return false;

            try
            {
                return group.GetUnlockingInfos().GetIsUnlocked(checkIfAvailableOnCurrentPlanet: true) || group.GetIsGloballyUnlocked();
            }
            catch (Exception e)
            {
                ModLog.LogOnce("upgrades-unlock-" + groupId, "Could not read whether " + groupId + " is unlocked: " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        /// <summary>For every building group that has a better, unlocked step in some chain: the group to swap it for (the highest unlocked step after it).</summary>
        private Dictionary<string, Chain> _chainOf = new Dictionary<string, Chain>();

        private Dictionary<string, string> Targets()
        {
            var targets = new Dictionary<string, string>();
            _chainOf = new Dictionary<string, Chain>();
            foreach (var chain in _chains)
            {
                // the highest unlocked step, and every step before it swaps to it
                var top = -1;
                for (var i = chain.Steps.Length - 1; i > 0; i--)
                    if (Unlocked(chain.Steps[i]))
                    {
                        top = i;
                        break;
                    }

                for (var i = 0; i < top; i++)
                {
                    targets[chain.Steps[i]] = chain.Steps[top];
                    _chainOf[chain.Steps[i]] = chain;
                }
            }

            return targets;
        }

        // ---- the swap ---------------------------------------------------------------------------------------------------------

        // On the terminal's screens; in the chat only when no terminal is installed.
        private static void Tell(string message)
        {
            try
            {
                if (TerminalRegistration.TryNotify(message))
                    return;
            }
            catch (Exception e)
            {
                ModLog.LogOnce("upgrades-terminal", "Could not post on the terminal: " + e.GetType().Name + ": " + e.Message);
            }

            ChatHook.Say(message);
        }

        private IEnumerator Run(string key, int planetHash)
        {
            var targets = Targets();
            if (targets.Count == 0)
                yield break;

            var handler = WorldObjectsHandler.Instance;
            var recorded = ModLog.ReadLines(BaseBuilder.UndoFile).Where(l => l.StartsWith(key + "|")).ToList();
            var recordedIds = new HashSet<int>(recorded.Select(l => l.Split('|')).Where(p => p.Length >= 3 && int.TryParse(p[p.Length - 2], out _)).Select(p => int.Parse(p[p.Length - 2])));
            var baseOnly = Plugin.UpgradeScope.Value.Trim().Equals("Base", StringComparison.OrdinalIgnoreCase);

            var todo = new List<WorldObject>();
            foreach (var wo in handler.GetAllWorldObjects().Values.ToList())
            {
                if (wo == null || wo.GetGroup() == null || !targets.ContainsKey(wo.GetGroup().GetId()))
                    continue;
                if (wo.GetPosition().sqrMagnitude <= 0f || wo.GetPlanetHash() != planetHash)
                    continue; // inside an inventory, or on another planet
                if (baseOnly && !recordedIds.Contains(wo.GetId()))
                    continue;
                todo.Add(wo);
            }

            if (todo.Count == 0)
                yield break;

            ModLog.Note("Upgrades: " + todo.Count + " buildings to swap (" + string.Join(", ", todo.GroupBy(w => w.GetGroup().GetId() + " > " + targets[w.GetGroup().GetId()]).Select(g => g.Count() + " x " + g.Key).ToArray()) + ").");

            var swapped = new Dictionary<string, int>();   // "from > to" -> how many
            var failed = new Dictionary<string, int>();
            var newIds = new Dictionary<int, KeyValuePair<int, string>>(); // old id -> new id and group, for the undo list
            var sinceFrame = 0;
            foreach (var old in todo)
            {
                var from = old.GetGroup().GetId();
                var to = targets[from];
                var pair = from + " > " + to;

                // The label and colour of a chain go to its last step, and only when the chain says so (for example only when there is just one such building).
                string label = null, color = null;
                var chain = _chainOf[from];
                if (to == chain.Steps[chain.Steps.Length - 1] && (chain.OnlyIf == 0 || todo.Count(w => w.GetGroup().GetId() == from) == chain.OnlyIf))
                {
                    label = chain.Label;
                    color = chain.Color;
                }

                var result = new SwapResult();
                yield return Swap(old, to, planetHash, result, null, color, label);
                if (result.Made != null)
                {
                    swapped[pair] = (swapped.TryGetValue(pair, out var n) ? n : 0) + 1;
                    newIds[result.OldId] = new KeyValuePair<int, string>(result.Made.GetId(), to);
                }
                else
                {
                    failed[pair] = (failed.TryGetValue(pair, out var m) ? m : 0) + 1;
                }

                if (++sinceFrame >= PiecesPerFrame)
                {
                    sinceFrame = 0;
                    yield return null;
                }
            }

            try
            {
                RewriteUndo(key, newIds);

                var energy = Managers.GetManager<RequireEnergyHandler>();
                if (energy != null)
                    energy.UpdateAllEnergyRequester();
            }
            catch (Exception e)
            {
                ModLog.Note("Upgrades: could not finish the bookkeeping: " + e.GetType().Name + ": " + e.Message);
            }

            foreach (var entry in swapped)
            {
                var ids = entry.Key.Split(new[] { " > " }, StringSplitOptions.None);
                var fromName = Readable.GetGroupName(GroupsHandler.GetGroupViaId(ids[0]));
                var toName = Readable.GetGroupName(GroupsHandler.GetGroupViaId(ids[1]));
                failed.TryGetValue(entry.Key, out var notSwapped);
                Tell(toName + " unlocked: " + entry.Value + " x " + fromName + " swapped for " + toName + (notSwapped == 0 ? "." : " (" + notSwapped + " could not be swapped)."));
            }

            foreach (var entry in failed.Where(f => !swapped.ContainsKey(f.Key)))
                Tell("Could not swap " + entry.Value + " x " + entry.Key.Replace(" > ", " for ") + " (see the Base log).");
        }

        /// <summary>The base pieces this mod placed keep their place in the undo list, as the new version.</summary>
        private static void RewriteUndo(string key, Dictionary<int, KeyValuePair<int, string>> newIds)
        {
            if (newIds.Count == 0)
                return;

            var all = ModLog.ReadLines(BaseBuilder.UndoFile);
            for (var i = 0; i < all.Count; i++)
            {
                var parts = all[i].Split('|');
                if (parts.Length >= 3 && all[i].StartsWith(key + "|") && int.TryParse(parts[parts.Length - 2], out var id) && newIds.TryGetValue(id, out var made))
                    all[i] = key + "|" + made.Key + "|" + made.Value;
            }

            File.WriteAllLines(BaseBuilder.UndoFile, all.ToArray());
        }

        // ---- growth: what was added to the base after T0 ----------------------------------------------------------------------

        private const float SameSpot = 0.5f;

        /// <summary>
        /// The entries of Growth.json whose group is unlocked. A pod entry (a window is a panel type on a pod) changes the pod of T0 at that spot: it is swapped for the same pod
        /// with the new panels, but only while it still has its T0 panels, so a pod the player changed is left alone. A pod that is not there is not made.
        /// </summary>
        private IEnumerator RunGrowth(string key, int planetHash)
        {
            Template growth = null;
            try
            {
                growth = Growth.Get();
            }
            catch (Exception e)
            {
                ModLog.LogOnce("growth-read", "Could not read the growth template: " + e.Message);
            }

            if (growth == null || growth.pieces == null || growth.pieces.Length == 0)
                yield break;

            var due = growth.pieces.Where(p => string.IsNullOrEmpty(p.op) && !string.IsNullOrEmpty(p.needs) && Unlocked(p.needs)).ToList();
            if (due.Count == 0)
                yield break;

            var handler = WorldObjectsHandler.Instance;
            var existing = handler.GetAllWorldObjects().Values.Where(w => w != null && w.GetGroup() != null && w.GetPlanetHash() == planetHash && w.GetPosition().sqrMagnitude > 0f).ToList();
            var newIds = new Dictionary<int, KeyValuePair<int, string>>();
            var done = new Dictionary<string, int>(); // needed group -> pieces updated
            foreach (var entry in due)
            {
                var at = BaseBuilder.Anchor + new Vector3(entry.x, entry.y, entry.z);
                var old = existing.FirstOrDefault(w => w.GetGroup().GetId() == entry.g && (w.GetPosition() - at).sqrMagnitude < SameSpot * SameSpot);
                if (old == null || string.IsNullOrEmpty(entry.panels))
                    continue;

                var current = string.Join(",", old.GetPanelsId().Select(i => i.ToString()).ToArray());
                if (current == entry.panels || current != entry.from)
                    continue; // already done, or the player has changed this one themselves

                var result = new SwapResult();
                yield return Swap(old, entry.g, planetHash, result, DataTreatments.StringToIntList(entry.panels).ToArray());
                if (result.Made == null)
                    continue;

                newIds[result.OldId] = new KeyValuePair<int, string>(result.Made.GetId(), entry.g);
                done[entry.needs] = (done.TryGetValue(entry.needs, out var n) ? n : 0) + 1;
                yield return null;
            }

            if (newIds.Count == 0)
                yield break;

            try
            {
                RewriteUndo(key, newIds);
                var energy = Managers.GetManager<RequireEnergyHandler>();
                if (energy != null)
                    energy.UpdateAllEnergyRequester();
            }
            catch (Exception e)
            {
                ModLog.Note("Upgrades: could not finish the bookkeeping: " + e.GetType().Name + ": " + e.Message);
            }

            foreach (var entry in done)
                Tell(Readable.GetGroupName(GroupsHandler.GetGroupViaId(entry.Key)) + " unlocked: " + entry.Value + " pieces of the base updated.");
        }

        // ---- stages: pieces added to or taken from the base when a group is unlocked ---------------------------------------------

        private static string StagesFile => ModLog.FileIn("stages.txt");

        private static string EntryId(TemplatePiece p) =>
            p.needs + "|" + p.op + "|" + p.g + "|" + p.x.ToString("F1", CultureInfo.InvariantCulture) + "," + p.y.ToString("F1", CultureInfo.InvariantCulture) + "," + p.z.ToString("F1", CultureInfo.InvariantCulture);

        /// <summary>
        /// The "add" and "remove" entries of Growth.json whose group is unlocked, once per save each (stages.txt remembers them, so a piece the player later deletes does not come
        /// back). Only on Prime and only where the base's anchor foundation stands. An add is skipped when a piece of that kind is already on the spot; a remove only takes a piece
        /// of that kind that is on the spot. Added pieces are put on the undo list, and what they hold (the seeds of a food grower) is filled in as at planetfall.
        /// </summary>
        private IEnumerator RunStages(string key, int planetHash)
        {
            Template growth = null;
            try
            {
                growth = Growth.Get();
            }
            catch (Exception e)
            {
                ModLog.LogOnce("growth-read", "Could not read the growth template: " + e.Message);
            }

            if (growth == null || growth.pieces == null)
                yield break;

            var recorded = new HashSet<string>(ModLog.ReadLines(StagesFile).Where(l => l.StartsWith(key + "|")).Select(l => l.Substring(key.Length + 1)));
            var due = growth.pieces.Where(p => (p.op == "add" || p.op == "remove") && !string.IsNullOrEmpty(p.needs) && !recorded.Contains(EntryId(p)) && Unlocked(p.needs)).ToList();
            if (due.Count == 0 || !WorldState.OnPlanet("Prime"))
                yield break;

            var handler = WorldObjectsHandler.Instance;
            var existing = handler.GetAllWorldObjects().Values.Where(w => w != null && w.GetGroup() != null && w.GetPlanetHash() == planetHash && w.GetPosition().sqrMagnitude > 0f).ToList();
            if (!existing.Any(w => w.GetGroup().GetId() == "Foundation" && (w.GetPosition() - BaseBuilder.Anchor).sqrMagnitude < SameSpot * SameSpot))
                yield break; // no base here to grow

            var added = new Dictionary<string, int>();     // needed group -> pieces made
            var removed = new Dictionary<string, int>();
            var created = new List<KeyValuePair<WorldObject, TemplatePiece>>();
            var finished = new List<string>();
            var sinceFrame = 0;

            // takes first, so a moved piece is gone from its old spot before it is made at the new one
            foreach (var entry in due.OrderBy(p => p.op == "remove" ? 0 : 1))
            {
                var at = BaseBuilder.Anchor + new Vector3(entry.x, entry.y, entry.z);
                var there = existing.FirstOrDefault(w => w.GetGroup().GetId() == entry.g && (w.GetPosition() - at).sqrMagnitude < SameSpot * SameSpot);

                if (entry.op == "remove")
                {
                    if (there != null)
                    {
                        try
                        {
                            handler.DestroyWorldObject(there, true);
                            existing.Remove(there);
                            removed[entry.needs] = (removed.TryGetValue(entry.needs, out var r) ? r : 0) + 1;
                        }
                        catch (Exception e)
                        {
                            ModLog.Note("Stages: could not remove " + entry.g + " #" + there.GetId() + ": " + e.GetType().Name + ": " + e.Message);
                            continue;
                        }
                    }

                    finished.Add(EntryId(entry));
                    continue;
                }

                if (there != null || GroupsHandler.GetGroupViaId(entry.g) == null)
                {
                    finished.Add(EntryId(entry)); // already there, or a piece of a mod that is not installed
                    continue;
                }

                try
                {
                    var wo = Placement.PlaceAt(entry.g, at, DataTreatments.StringToQuaternion(entry.rot), planetHash,
                        string.IsNullOrEmpty(entry.panels) ? null : DataTreatments.StringToIntList(entry.panels).ToArray(),
                        string.IsNullOrEmpty(entry.color) ? null : entry.color, entry.text, entry.groups);
                    created.Add(new KeyValuePair<WorldObject, TemplatePiece>(wo, entry));
                    existing.Add(wo);
                    finished.Add(EntryId(entry));
                    added[entry.needs] = (added.TryGetValue(entry.needs, out var n) ? n : 0) + 1;
                }
                catch (Exception e)
                {
                    ModLog.LogOnce("stage-" + EntryId(entry), "Stages: could not place " + entry.g + " at " + entry.x + "," + entry.z + ": " + e.GetType().Name + ": " + e.Message + " (tried again later).");
                }

                if (++sinceFrame >= PiecesPerFrame)
                {
                    sinceFrame = 0;
                    yield return null;
                }
            }

            foreach (var made in created)
                ModLog.Append(BaseBuilder.UndoFile, key + "|" + made.Key.GetId() + "|" + made.Value.g);
            foreach (var id in finished)
                ModLog.Append(StagesFile, key + "|" + id);

            yield return BaseBuilder.FillContents(created);

            try
            {
                var energy = Managers.GetManager<RequireEnergyHandler>();
                if (energy != null)
                    energy.UpdateAllEnergyRequester();
            }
            catch (Exception e)
            {
                ModLog.Note("Stages: could not refresh the energy network (harmless): " + e.Message);
            }

            foreach (var needs in added.Keys.Union(removed.Keys))
            {
                added.TryGetValue(needs, out var a);
                removed.TryGetValue(needs, out var r);
                var name = Readable.GetGroupName(GroupsHandler.GetGroupViaId(needs));
                ModLog.Note("Stages: " + needs + ": " + a + " pieces added, " + r + " taken away.");
                Tell(name + " unlocked: the base grew (" + a + " added" + (r > 0 ? ", " + r + " taken away" : "") + ").");
            }
        }

        private sealed class SwapResult
        {
            public WorldObject Made;
            public int OldId;
        }

        /// <summary>Swaps one building: place the new one, move what is inside, and only then remove the old one. On any problem the old one stays.</summary>
        private static IEnumerator Swap(WorldObject old, string to, int planetHash, SwapResult result, int[] panels = null, string color = null, string text = null)
        {
            var handler = WorldObjectsHandler.Instance;
            var from = old.GetGroup().GetId();
            result.OldId = old.GetId();

            WorldObject made = null;
            try
            {
                made = Placement.PlaceAt(to, old.GetPosition(), old.GetRotation(), planetHash, panels, color, text);
            }
            catch (Exception e)
            {
                ModLog.Note("Upgrades: could not place " + to + " for " + from + " #" + old.GetId() + ": " + e.GetType().Name + ": " + e.Message + " (the old one stays).");
                yield break;
            }

            // What is stored inside (the seeds of a vegetube) moves to the new building's own inventory.
            var inventories = InventoriesHandler.Instance;
            var oldInventory = old.GetLinkedInventoryId() > 0 ? inventories.GetInventoryById(old.GetLinkedInventoryId()) : null;
            if (oldInventory != null && oldInventory.GetInsideWorldObjects().Count > 0)
            {
                var content = oldInventory.GetInsideWorldObjects().Count;
                Inventory newInventory = null;
                var until = Time.realtimeSinceStartup + WaitSeconds;
                while (newInventory == null && Time.realtimeSinceStartup < until)
                {
                    newInventory = made.GetLinkedInventoryId() > 0 ? inventories.GetInventoryById(made.GetLinkedInventoryId()) : null;
                    if (newInventory == null)
                        yield return null;
                }

                var moved = false;
                if (newInventory != null)
                {
                    try
                    {
                        inventories.TransferAllItems(oldInventory, newInventory);
                    }
                    catch (Exception e)
                    {
                        ModLog.Note("Upgrades: could not start moving the contents of " + from + " #" + old.GetId() + ": " + e.GetType().Name + ": " + e.Message);
                    }

                    until = Time.realtimeSinceStartup + WaitSeconds;
                    while (oldInventory.GetInsideWorldObjects().Count > 0 && Time.realtimeSinceStartup < until)
                        yield return null;

                    moved = oldInventory.GetInsideWorldObjects().Count == 0 && newInventory.GetInsideWorldObjects().Count >= content;
                }

                if (!moved)
                {
                    // Not everything arrived: put the contents back where they were if some moved, remove the new building, keep the old one.
                    ModLog.Note("Upgrades: the contents of " + from + " #" + old.GetId() + " did not all move (" + content + " items); the old building stays.");
                    if (newInventory != null && newInventory.GetInsideWorldObjects().Count > 0)
                    {
                        inventories.TransferAllItems(newInventory, oldInventory);
                        until = Time.realtimeSinceStartup + WaitSeconds;
                        while (newInventory.GetInsideWorldObjects().Count > 0 && Time.realtimeSinceStartup < until)
                            yield return null;
                    }

                    try
                    {
                        if (newInventory == null || newInventory.GetInsideWorldObjects().Count == 0)
                            handler.DestroyWorldObject(made, true);
                    }
                    catch (Exception e)
                    {
                        ModLog.Note("Upgrades: could not remove the new " + to + ": " + e.GetType().Name + ": " + e.Message);
                    }

                    yield break;
                }
            }

            try
            {
                handler.DestroyWorldObject(old, true);
            }
            catch (Exception e)
            {
                ModLog.Note("Upgrades: could not remove the old " + from + " #" + old.GetId() + ": " + e.GetType().Name + ": " + e.Message);
                yield break;
            }

            result.Made = made;
        }
    }
}
