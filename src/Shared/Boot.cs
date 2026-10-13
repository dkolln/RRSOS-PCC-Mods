using System;
using System.Collections;
using UnityEngine;

namespace RRSOS.PCC.Shared
{
    /// <summary>
    /// The start-up messages of a new game, told in the game's chat one after the other by whichever RRSOS mods are installed, without the mods knowing about each other.
    /// Each mod announces itself with its place in the order when it loads; the mods share the small table through the app domain, because every mod carries its own copy
    /// of this class. A step starts when every installed mod before it has finished (or has nothing to do), the first step opens the sequence and the last one closes it.
    /// </summary>
    internal static class Boot
    {
        public const int Base = 1, Loadout = 2, Commands = 3, Blueprints = 4;

        private const string TableKey = "RRSOS.PCC.Boot";
        private const float Pause = 0.9f;       // seconds between two lines
        private const float PatienceFor = 180f;  // a step that waits for an earlier one this long goes ahead anyway

        private const string Opening = "New Planet Crafter Detected - Hacking Sentinel Corp.........";
        private const string Closing = "Welcome Planet Crafter.";

        // order -> finished (bool); "next" -> earliest time of the next line (float); "opened"/"closed" -> bool
        private static Hashtable Table
        {
            get
            {
                var table = AppDomain.CurrentDomain.GetData(TableKey) as Hashtable;
                if (table == null)
                {
                    table = new Hashtable();
                    AppDomain.CurrentDomain.SetData(TableKey, table);
                }

                return table;
            }
        }

        /// <summary>Call when the mod loads (and is switched on): the other steps wait for this one.</summary>
        public static void Announce(int order)
        {
            if (!Table.ContainsKey(order))
                Table[order] = false;
        }

        /// <summary>Every mod calls this with the world's key before it decides; the first call for a new world clears what the previous world left in the table.</summary>
        public static void NewWorld(string key)
        {
            var table = Table;
            if (key.Equals(table["world"]))
                return;

            table["world"] = key;
            table.Remove("opened");
            table.Remove("closed");
            var orders = new System.Collections.Generic.List<object>();
            foreach (DictionaryEntry e in table)
                if (e.Key is int)
                    orders.Add(e.Key);
            foreach (var order in orders)
                table[order] = false;
        }

        /// <summary>This mod has nothing to say in this game (not a new game, or already handled).</summary>
        public static void Skip(int order)
        {
            Table[order] = true;
            CloseIfLast();
        }

        /// <summary>Tells the step's lines around the work. "Preparing main base..." before, "Done..." after.</summary>
        public static IEnumerator Step(int order, string title, IEnumerator work, bool onlyIfStarted = false)
        {
            if (onlyIfStarted)
            {
                // a step with no work of its own: it tells its lines only when an earlier step has opened the sequence
                var waited = Time.realtimeSinceStartup;
                while (!EarlierDone(order) && Time.realtimeSinceStartup - waited < PatienceFor)
                    yield return new WaitForSecondsRealtime(0.25f);

                if (!Flag("opened"))
                {
                    Skip(order);
                    yield break;
                }
            }

            yield return Begin(order, title);
            if (work != null)
                yield return work;
            yield return End(order);
        }

        /// <summary>Waits for this step's turn, opens the sequence if it is the first, and tells the step's first line.</summary>
        public static IEnumerator Begin(int order, string title)
        {
            var started = Time.realtimeSinceStartup;
            while (!EarlierDone(order) && Time.realtimeSinceStartup - started < PatienceFor)
                yield return new WaitForSecondsRealtime(0.25f);

            if (!Flag("opened"))
            {
                Table["opened"] = true;
                yield return Line(Opening);
            }

            yield return Line(title);
        }

        /// <summary>Tells "Done...." and, if every installed mod is through, the closing line.</summary>
        public static IEnumerator End(int order)
        {
            yield return Line("Done" + (order == Commands ? "....." : "...."));

            Table[order] = true;
            if (AllDone() && !Flag("closed"))
            {
                Table["closed"] = true;
                yield return Line(Closing);
            }
        }

        private static IEnumerator Line(string text)
        {
            while (Time.realtimeSinceStartup < NextAt())
                yield return null;

            ChatHook.Say(text);
            Table["next"] = Time.realtimeSinceStartup + Pause;
        }

        private static float NextAt() => Table["next"] is float f ? f : 0f;

        private static bool Flag(string name) => Table[name] is bool b && b;

        private static bool EarlierDone(int order)
        {
            foreach (DictionaryEntry e in Table)
                if (e.Key is int other && other < order && !(e.Value is bool done && done))
                    return false;
            return true;
        }

        private static bool AllDone()
        {
            foreach (DictionaryEntry e in Table)
                if (e.Key is int && !(e.Value is bool done && done))
                    return false;
            return true;
        }

        // Closing line when the last mod to finish had nothing to say but an earlier one did.
        private static void CloseIfLast()
        {
            if (Flag("opened") && !Flag("closed") && AllDone())
            {
                Table["closed"] = true;
                ChatHook.Say(Closing);
            }
        }
    }
}
