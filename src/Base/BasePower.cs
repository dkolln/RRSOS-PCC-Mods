using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Base
{
    /// <summary>
    /// The [ ON ] and [ OFF ] buttons on the Energy window (PowerButtons) switch the base's power; there is no chat or terminal command, and a click is not reported on the terminal
    /// or in the chat. Off sets the energy output of every machine that makes energy (wind turbines, every solar tier, anything
    /// else with a positive energy output) to 0, one machine at a time through the game's own per-object output (WorldObject.SetUnitGeneration, which is what the energy total is
    /// summed from); machines that use energy are left alone, so with nothing making energy they stop. While it is off, a generator built later is switched off too. Power on puts
    /// each output back to what it was. Nothing is removed from the world. The state is kept per save (power-state.txt) and applied again whenever that world loads: the game does
    /// not save a machine's output, so a restart brings the real values back and a world that is off is switched off again.
    /// Units that an earlier version stored by taking them out of the world (power.txt) are put back when power is switched on.
    /// </summary>
    internal sealed class BasePower : MonoBehaviour
    {
        private const int PiecesPerFrame = 4;
        private const float EnforceEvery = 3f;      // seconds: while off, generators built since are switched off too

        private static string StateFile => ModLog.FileIn("power-state.txt");
        private static string LegacyFile => ModLog.FileIn("power.txt");

        private sealed class Request
        {
            public bool On;
            public Action<string> Say;
        }

        private static readonly Queue<Request> Requests = new Queue<Request>();

        // World object id -> the energy output it had before it was set to 0 (only while this session has it switched off).
        private static readonly Dictionary<int, float> Original = new Dictionary<int, float>();

        private static bool _off;            // the loaded world is switched off

        /// <summary>True while the loaded world is switched off (read by the buttons on the Energy window).</summary>
        internal static bool IsOffNow => _off;

        private static BasePower _instance;

        /// <summary>The planet's energy as the game itself counts it (per second): production and consumption. For checking that a switch took effect.</summary>
        private static string EnergyReading()
        {
            try
            {
                var planet = Managers.GetManager<PlanetLoader>().GetCurrentPlanetData();
                var unit = Managers.GetManager<WorldUnitsHandler>().GetUnit(DataConfig.WorldUnitType.Energy, planet.GetPlanetId());
                return "production " + unit.GetIncreaseValuePersSec().ToString("0.##", CultureInfo.InvariantCulture) + ", consumption " + unit.GetDecreaseValuePersSec().ToString("0.##", CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                return "(could not be read: " + e.GetType().Name + ")";
            }
        }

        /// <summary>A few seconds after a switch, says what the game now counts, next to what it counted before.</summary>
        private static void ReportEnergy(string before, Action<string> say)
        {
            if (_instance != null)
                _instance.StartCoroutine(ReportAfter(before, say));
        }

        private static IEnumerator ReportAfter(string before, Action<string> say)
        {
            yield return new WaitForSecondsRealtime(6f);
            var after = EnergyReading();
            ModLog.Note("Energy counted by the game: before " + before + "; now " + after + ".");
            say("Energy now: " + after + " (before: " + before + ").");
        }

        private string _lastKey;
        private float _nextEnforce;

        /// <summary>The buttons' command: "on" or "off". The work is done on the game's main thread by the loop below.</summary>
        internal static void OnCommand(string args, Action<string> say)
        {
            var word = (args ?? "").Trim().ToLowerInvariant();
            if (word != "on" && word != "off")
            {
                say("Use the [ ON ] and [ OFF ] buttons on the Energy window.");
                return;
            }

            lock (Requests)
                Requests.Enqueue(new Request { On = word == "on", Say = say });
        }

        private IEnumerator Start()
        {
            _instance = this;
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.5f);

                if (!Plugin.Enabled.Value)
                    continue;

                string key = null;
                var planetHash = 0;
                var ready = false;
                try
                {
                    ready = WorldState.Ready(out planetHash, out key);
                }
                catch (Exception)
                {
                    ready = false;
                }

                if (!ready)
                    continue;

                // A world has just loaded: its saved state decides the outputs.
                if (key != _lastKey)
                {
                    _lastKey = key;
                    _off = IsOff(key);
                    Original.Clear(); // the objects of the previous world are gone
                    _nextEnforce = 0f;
                }

                Request request = null;
                lock (Requests)
                    if (Requests.Count > 0)
                        request = Requests.Dequeue();

                if (request != null)
                {
                    if (BaseBuilder.Busy)
                    {
                        request.Say("The base is busy; try again in a moment.");
                    }
                    else
                    {
                        BaseBuilder.Busy = true;
                        try
                        {
                            if (request.On)
                                yield return SwitchOn(key, planetHash, request.Say);
                            else
                                SwitchOff(key, request.Say);
                        }
                        finally
                        {
                            BaseBuilder.Busy = false;
                        }
                    }

                    continue;
                }

                // While off (also right after a load of a world that is off): switch off every generator there is, new ones too.
                if (_off && Time.realtimeSinceStartup >= _nextEnforce)
                {
                    _nextEnforce = Time.realtimeSinceStartup + EnforceEvery;
                    try
                    {
                        var added = ApplyOff(out var byGroup);
                        if (added > 0)
                            ModLog.Note("Power is off: " + added + " more energy outputs set to 0 (" + string.Join(", ", byGroup.Select(g => g.Value + " x " + g.Key).ToArray()) + ").");
                    }
                    catch (Exception e)
                    {
                        ModLog.LogOnce("power-enforce", "Power: could not switch the generators off: " + e.GetType().Name + ": " + e.Message);
                    }
                }
            }
        }

        // ---- the state --------------------------------------------------------------------------------------------------------

        private static bool IsOff(string key) => ModLog.ReadLines(StateFile).Contains(key);

        private static void SetState(string key, bool off)
        {
            var keys = ModLog.ReadLines(StateFile).Where(l => l != key).ToList();
            if (off)
                keys.Add(key);
            System.IO.File.WriteAllLines(StateFile, keys.ToArray());
            _off = off;
        }

        // ---- the outputs ------------------------------------------------------------------------------------------------------

        /// <summary>Sets every machine's positive energy output to 0, remembering what it was. Returns how many were switched off now; byGroup counts them per kind.</summary>
        private static int ApplyOff(out Dictionary<string, int> byGroup)
        {
            byGroup = new Dictionary<string, int>();
            var count = 0;
            foreach (var wo in WorldObjectsHandler.Instance.GetAllWorldObjects().Values.ToList())
            {
                if (wo == null || wo.GetGroup() == null || !wo.HasUnitGeneration())
                    continue;

                var output = wo.GetUnitGeneration(DataConfig.WorldUnitType.Energy);
                if (output <= 0f)
                    continue; // makes none, or uses energy (negative): left alone

                if (!Original.ContainsKey(wo.GetId()))
                    Original[wo.GetId()] = output;
                wo.SetUnitGeneration(DataConfig.WorldUnitType.Energy, 0f);

                var id = wo.GetGroup().GetId();
                byGroup[id] = (byGroup.TryGetValue(id, out var n) ? n : 0) + 1;
                count++;
            }

            if (count > 0)
                Refresh();
            return count;
        }

        private static int ApplyOn()
        {
            var handler = WorldObjectsHandler.Instance;
            var restored = 0;
            foreach (var pair in Original.ToList())
            {
                var wo = handler.GetWorldObjectViaId(pair.Key);
                if (wo == null)
                    continue; // taken down while it was off

                wo.SetUnitGeneration(DataConfig.WorldUnitType.Energy, pair.Value);
                restored++;
            }

            Original.Clear();
            Refresh();
            return restored;
        }

        private static string Describe(Dictionary<string, int> byGroup) => string.Join(", ", byGroup.OrderBy(g => g.Key).Select(g => g.Value + " x " + g.Key).ToArray());

        private static void SwitchOff(string key, Action<string> say)
        {
            try
            {
                var reading = EnergyReading();
                var before = Original.Count;
                var count = ApplyOff(out var byGroup);
                SetState(key, true);
                var total = Original.Count;
                ModLog.Note("Power off: " + count + " energy outputs set to 0 (" + Describe(byGroup) + "). Outputs before: "
                    + string.Join(", ", Original.GroupBy(o => Math.Round(o.Value, 2)).OrderBy(g => g.Key).Select(g => g.Count() + " x " + g.Key.ToString("0.###", CultureInfo.InvariantCulture)).ToArray()) + ".");
                say(total == 0 ? "No machine makes energy yet, so there is nothing to switch off. New generators will stay off until power is switched on."
                    : before == total && count == 0 ? "Power is already off (" + total + " power units)."
                    : "Power off: " + total + " power units now make no energy.");
                ReportEnergy(reading, say);
            }
            catch (Exception e)
            {
                ModLog.Note("Power off failed: " + e.GetType().Name + ": " + e.Message);
                say("Power off failed (see the Base log).");
            }
        }

        private static IEnumerator SwitchOn(string key, int planetHash, Action<string> say)
        {
            var restored = 0;
            var reading = EnergyReading();
            try
            {
                SetState(key, false);
                restored = ApplyOn();
                ModLog.Note("Power on: " + restored + " energy outputs put back.");
            }
            catch (Exception e)
            {
                ModLog.Note("Power on failed: " + e.GetType().Name + ": " + e.Message);
                say("Power on failed (see the Base log).");
                yield break;
            }

            // Units an earlier version took out of the world are put back.
            var all = ModLog.ReadLines(LegacyFile);
            var mine = all.Where(l => l.StartsWith(key + "|")).ToList();
            var placed = 0;
            if (mine.Count > 0)
            {
                var kept = new List<string>();
                var since = 0;
                foreach (var line in mine)
                {
                    var parts = line.Split('|');
                    try
                    {
                        // the save's key holds a "|" itself, so the line is read from its end: ... | group | x | y | z | rotation
                        var n = parts.Length;
                        if (n < 6)
                            throw new FormatException("a short line");

                        var group = parts[n - 5];
                        var pos = new Vector3(P(parts[n - 4]), P(parts[n - 3]), P(parts[n - 2]));
                        var made = Placement.PlaceAt(group, pos, DataTreatments.StringToQuaternion(parts[n - 1]), planetHash);
                        ModLog.Append(BaseBuilder.UndoFile, key + "|" + made.GetId() + "|" + group);
                        placed++;
                    }
                    catch (Exception e)
                    {
                        ModLog.Note("Power on: could not place a stored unit from \"" + line + "\": " + e.GetType().Name + ": " + e.Message);
                        kept.Add(line);
                    }

                    if (++since >= PiecesPerFrame)
                    {
                        since = 0;
                        yield return null;
                    }
                }

                System.IO.File.WriteAllLines(LegacyFile, all.Where(l => !l.StartsWith(key + "|")).Concat(kept).ToArray());
                Refresh();
            }

            say("Power on: " + restored + " power units make energy again." + (mine.Count > 0 ? " " + placed + " stored units put back." : ""));
            ReportEnergy(reading, say);
        }

        private static readonly FieldInfo KnownObjects = typeof(WorldUnit).GetField("_previousKnowWorldObjectsIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo SumObjects = typeof(WorldUnit).GetMethod("SetIncreaseAndDecreaseForWorldObjects", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>
        /// The game sums every machine's output into the planet's energy only when the set of objects has changed, so a changed output alone is not counted. This makes the energy
        /// unit forget which objects it knew and sum again, then has the machines that use energy check it.
        /// </summary>
        private static void Recount()
        {
            try
            {
                var planet = Managers.GetManager<PlanetLoader>().GetCurrentPlanetData();
                var unit = Managers.GetManager<WorldUnitsHandler>().GetUnit(DataConfig.WorldUnitType.Energy, planet.GetPlanetId());
                if (unit == null)
                    return;

                if (KnownObjects != null)
                    KnownObjects.SetValue(unit, -1f);
                if (SumObjects != null)
                    SumObjects.Invoke(unit, null);
            }
            catch (Exception e)
            {
                ModLog.LogOnce("power-recount", "Could not make the game count the energy again: " + e.GetType().Name + ": " + (e.InnerException != null ? e.InnerException.Message : e.Message));
            }
        }

        private static void Refresh()
        {
            Recount();
            try
            {
                var energy = Managers.GetManager<RequireEnergyHandler>();
                if (energy != null)
                    energy.UpdateAllEnergyRequester();
            }
            catch (Exception e)
            {
                ModLog.Note("Could not refresh the energy network (harmless): " + e.Message);
            }
        }

        private static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }
}
