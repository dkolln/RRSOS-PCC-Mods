using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using SpaceCraft;

namespace RRSOS.PCC.Shared
{
    /// <summary>
    /// The welcome message waiting in the escape pod. Each installed RRSOS mod adds its own paragraph (ranked, so they read in a fixed order) and the first mod to
    /// load patches the game's text lookup once; every copy of this class reads the same table, kept in the app domain, so the mods need not know about each other.
    /// With no paragraph added the game's own message is left alone.
    /// </summary>
    internal static class Welcome
    {
        private const string TableKey = "RRSOS.PCC.Welcome";
        private const string MessageKey = "Message_welcome";
        private const string SenderKey = "MessageSender_RedPlanetSolutions";
        private const string Sender = "Red Planet Solutions";

        private const string Greeting = "Greetings Planet Crafter,<br><br>You have been incarcerated by Sentinel Corp. and unfairly sentenced to terraform this world. "
            + "We at Red Planet Solutions feel that though we cannot commute your sentence, we can make your terraformation process easier.";

        // The two paragraphs of the game's own message that stay.
        private const string Mission = "Your mission is to advance the terraformation process of this world. You'll need to generate 0², Heat and Pressure.";
        private const string First = "<br>First, reach 175.000 Ti and create a blue atmosphere.";

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

        /// <summary>Adds a paragraph to the welcome message; lower ranks come first.</summary>
        public static void Add(int rank, string paragraph)
        {
            Table[rank] = paragraph;
            if (Table["patched"] != null)
                return;

            Table["patched"] = true;
            try
            {
                var original = AccessTools.Method(typeof(Localization), "GetLocalizedString", new[] { typeof(string) });
                var harmony = new Harmony("com.rrsos.pcc.welcome");
                harmony.Patch(original, postfix: new HarmonyMethod(typeof(Welcome), nameof(Postfix)));

                // The sign-off: the welcome message is from us, not Sentinel Corp. (the other Sentinel messages keep their sender).
                harmony.Patch(AccessTools.Method(typeof(MessageData), "GetSenderId"), postfix: new HarmonyMethod(typeof(Welcome), nameof(SenderPostfix)));
            }
            catch (Exception e)
            {
                ModLog.Note("Could not change the welcome message: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void SenderPostfix(MessageData __instance, ref string __result)
        {
            if (__instance.GetStringId() == MessageKey && Table.Count > 1)
                __result = SenderKey;
        }

        private static void Postfix(string stringCode, ref string __result)
        {
            if (stringCode == SenderKey)
            {
                __result = Sender;
                return;
            }

            if (stringCode != MessageKey)
                return;

            var ranks = new List<int>();
            foreach (DictionaryEntry e in Table)
                if (e.Key is int)
                    ranks.Add((int)e.Key);
            if (ranks.Count == 0)
                return;

            ranks.Sort();
            var parts = new List<string> { Greeting };
            foreach (var rank in ranks)
                parts.Add((string)Table[rank]);
            parts.Add(Mission);
            parts.Add(First);
            __result = string.Join("<br><br>", parts.ToArray());
        }
    }
}
