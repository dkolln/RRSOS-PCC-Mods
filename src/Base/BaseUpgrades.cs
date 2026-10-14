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
    /// It applies to every such building on the planet, whether or not the base mod placed it.
    /// </summary>
    internal sealed class BaseUpgrades : MonoBehaviour
    {
        private const string DefaultRules = @"# Upgrade chains: when a later step is unlocked, the earlier steps in the world are swapped for it (in the same spot).
# One chain per line, steps separated by >. A building that is not at the start of a chain is upgraded from wherever it stands in it.
# Anything inside the old building (seeds in a vegetube) is moved to the new one. Lines starting with # are notes.
# After the steps a line may add ; label=Text ; color=green ; only=1 : the last step is then made with that label and colour,
# and only=N means only when exactly N buildings of the swapped kind stand in the world (the swap itself happens either way).
# ; turn=90 turns every building made as the last step 90 degrees to the right (clockwise from above; negative turns left).
# ; ore=Iron,Silicon,... is for ore extractors: a building keeps the ore it already extracts (the vein it stands on) through every step; one that has no ore yet gets, when it becomes
# the last step, whichever ore of this list the world has the fewest of, so the extractors spread evenly over the list.
# Colours: green red blue yellow orange purple cyan white.

# Wind Turbine > T1 Solar Panel > T2 Solar Panel
EnergyGenerator1 > EnergyGenerator2 > EnergyGenerator3

# Nuclear Reactor T1 > T2 > Fusion Generator
EnergyGenerator4 > EnergyGenerator5 > EnergyGenerator6

# Heater T1 > T2 > T3
Heater1 > Heater2 > Heater3

# Ore Extractor T1 > T2 > T3 (on a vein it keeps that vein's ore; off a vein the T3 takes the ore that is least mined)
OreExtractor1 > OreExtractor2 > OreExtractor3 ; ore=Iron,Silicon,Titanium,Cobalt,Sulfur,Magnesium

# Heater T4 > T5
Heater4 > Heater5

# Vegetube T1 > T2 (the seeds go with it)
Vegetube1 > Vegetube2

# Drill T1 > T2 > T3 > T4 > T5
Drill0 > Drill1 > Drill2 > Drill3 > Drill4

# Tree Spreader T1 > T2 > T3
TreeSpreader0 > TreeSpreader1 > TreeSpreader2

# Flower Spreader T1 > T2
SeedSpreader1 > SeedSpreader2

# Butterfly Farm T1 > T2 > T3
ButterflyFarm1 > ButterflyFarm2 > ButterflyFarm3

# Beehive T1 > T2
Beehive1 > Beehive2

# Machine Optimizer T1 > T2 (the fuses go with it)
Optimizer1 > Optimizer2

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
            public string[] Ore;     // ore group ids to spread over (ore extractors); null = the chain does not deal with ore
            public float Turn;       // degrees to the right (clockwise seen from above) that the last step is turned when it is made
        }

        // colour names that can be written in upgrades.txt
        private static readonly Dictionary<string, Color> ColorNames = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "green", new Color(0.1f, 0.9f, 0.1f, 1f) }, { "red", new Color(0.95f, 0.1f, 0.1f, 1f) }, { "blue", new Color(0.15f, 0.35f, 1f, 1f) },
            { "yellow", new Color(1f, 0.9f, 0.1f, 1f) }, { "orange", new Color(1f, 0.55f, 0.05f, 1f) }, { "purple", new Color(0.6f, 0.2f, 0.9f, 1f) },
            { "cyan", new Color(0.1f, 0.9f, 0.9f, 1f) }, { "white", Color.white },
        };

        // This session only: ids that survived being destroyed, and the spots (to 0.1) where a swap already happened.
        private static readonly HashSet<int> Untouchable = new HashSet<int>();
        private static readonly HashSet<string> SwappedSpots = new HashSet<string>();

        private static string SpotKey(Vector3 p)
        {
            return Mathf.RoundToInt(p.x * 10f) + "," + Mathf.RoundToInt(p.y * 10f) + "," + Mathf.RoundToInt(p.z * 10f);
        }

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
                    else if (name == "ore")
                    {
                        var ores = value.Split(',').Select(o => o.Trim()).Where(o => o.Length > 0).ToArray();
                        var missing = ores.Where(o => GroupsHandler.GetGroupViaId(o) == null).ToArray();
                        if (ores.Length == 0 || missing.Length > 0)
                            ModLog.Note("upgrades.txt: ore= in \"" + line + "\" ignored (" + (ores.Length == 0 ? "no ores" : "the game has no group " + string.Join(", ", missing)) + ").");
                        else
                            chain.Ore = ores;
                    }
                    else if (name == "turn" && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var turn))
                        chain.Turn = turn;
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

            var todo = new List<WorldObject>();
            foreach (var wo in handler.GetAllWorldObjects().Values.ToList())
            {
                if (wo == null || wo.GetGroup() == null || !targets.ContainsKey(wo.GetGroup().GetId()))
                    continue;
                if (wo.GetPosition().sqrMagnitude <= 0f || wo.GetPlanetHash() != planetHash)
                    continue; // inside an inventory, or on another planet
                if (Untouchable.Contains(wo.GetId()) || SwappedSpots.Contains(SpotKey(wo.GetPosition()) + "|" + wo.GetGroup().GetId()))
                    continue; // could not be removed before, or a building of this kind at this very spot was already swapped (a world item that comes back)
                todo.Add(wo);
            }

            if (todo.Count == 0)
                yield break;

            ModLog.Note("Upgrades: " + todo.Count + " buildings to swap (" + string.Join(", ", todo.GroupBy(w => w.GetGroup().GetId() + " > " + targets[w.GetGroup().GetId()]).Select(g => g.Count() + " x " + g.Key).ToArray()) + ").");

            // How many extractors of each ore the world has, for the chains that spread ore (counted on the last step, and kept up to date as this pass makes more).
            var oreUsage = new Dictionary<Chain, Dictionary<string, int>>();
            foreach (var oreChain in _chainOf.Values.Distinct().Where(c => c.Ore != null))
            {
                var usage = oreChain.Ore.ToDictionary(o => o, o => 0);
                var lastStep = oreChain.Steps[oreChain.Steps.Length - 1];
                foreach (var wo in handler.GetAllWorldObjects().Values)
                {
                    if (wo == null || wo.GetGroup() == null || wo.GetGroup().GetId() != lastStep || wo.GetPlanetHash() != planetHash)
                        continue;
                    var ore = OreOf(wo).FirstOrDefault();
                    if (ore != null && usage.ContainsKey(ore))
                        usage[ore]++;
                }

                oreUsage[oreChain] = usage;
            }

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
                var turn = 0f;
                var chain = _chainOf[from];
                if (to == chain.Steps[chain.Steps.Length - 1])
                {
                    turn = chain.Turn; // the turn applies to every swap into the last step, not only when "only" matches
                    if (chain.OnlyIf == 0 || todo.Count(w => w.GetGroup().GetId() == from) == chain.OnlyIf)
                    {
                        label = chain.Label;
                        color = chain.Color;
                    }
                }

                // Ore extractors: keep the ore of the vein it stands on; when it has none and becomes the last step, take the least-used ore of the chain's list.
                string linked = null, chosenOre = null;
                if (chain.Ore != null)
                {
                    var own = OreOf(old);
                    if (own.Count > 0)
                    {
                        linked = to == chain.Steps[chain.Steps.Length - 1] ? own[0] : string.Join(",", own.ToArray());
                    }
                    else if (to == chain.Steps[chain.Steps.Length - 1])
                    {
                        var usage = oreUsage[chain];
                        chosenOre = chain.Ore.OrderBy(o => usage[o]).First(); // OrderBy is stable: ties go to the earlier ore in the list
                        linked = chosenOre;
                    }
                }

                var result = new SwapResult();
                yield return Swap(old, to, planetHash, result, null, color, label, turn, linked);
                if (result.Made != null)
                {
                    swapped[pair] = (swapped.TryGetValue(pair, out var n) ? n : 0) + 1;
                    if (chain.Ore != null && linked != null && to == chain.Steps[chain.Steps.Length - 1] && oreUsage[chain].ContainsKey(linked.Split(',')[0]))
                        oreUsage[chain][linked.Split(',')[0]]++;
                    if (chosenOre != null)
                        ModLog.Note("Upgrades: " + from + " #" + result.OldId + " is on no vein; the new " + to + " extracts " + chosenOre + ".");
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

        /// <summary>The distinct ores (group ids) a building extracts, in the order the game lists them; empty when it stands on no vein. A T2 extractor lists its ore once per slot.</summary>
        private static List<string> OreOf(WorldObject wo)
        {
            var groups = wo.HasLinkedGroups() ? wo.GetLinkedGroups() : null;
            return groups == null ? new List<string>() : groups.Where(g => g != null).Select(g => g.GetId()).Distinct().ToList();
        }

        private sealed class SwapResult
        {
            public WorldObject Made;
            public int OldId;
        }

        /// <summary>Swaps one building: place the new one, move what is inside, and only then remove the old one. On any problem the old one stays.</summary>
        private static IEnumerator Swap(WorldObject old, string to, int planetHash, SwapResult result, int[] panels = null, string color = null, string text = null, float turn = 0f, string linked = null)
        {
            var handler = WorldObjectsHandler.Instance;
            var from = old.GetGroup().GetId();
            result.OldId = old.GetId();

            WorldObject made = null;
            try
            {
                made = Placement.PlaceAt(to, old.GetPosition(), Quaternion.Euler(0f, turn, 0f) * old.GetRotation(), planetHash, panels, color, text, linked);
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

            // Some world objects (the heaters lying in wrecks) cannot be removed, or come back. Leave those alone from now on instead of piling up copies.
            if (handler.GetWorldObjectViaId(result.OldId) != null)
            {
                Untouchable.Add(result.OldId);
                ModLog.Note("Upgrades: the old " + from + " #" + result.OldId + " could not be removed (a world item?); leaving it alone and removing the new " + to + ".");
                try
                {
                    handler.DestroyWorldObject(made, true);
                }
                catch (Exception e)
                {
                    ModLog.Note("Upgrades: could not remove the new " + to + ": " + e.GetType().Name + ": " + e.Message);
                }

                yield break;
            }

            SwappedSpots.Add(SpotKey(old.GetPosition()) + "|" + from);
            result.Made = made;
        }
    }
}
