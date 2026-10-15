using System;
using System.Collections.Generic;
using System.Linq;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Commands
{
    /// <summary>
    /// The terminal's registration point. The terminal knows nothing about other mods: any mod that has terminal commands registers them here (by reflection, with only basic
    /// types, so no assembly is shared; see TerminalRegistration in the shared source), and the terminal's /help lists whatever has registered.
    /// </summary>
    public static class TerminalApi
    {
        internal sealed class Entry
        {
            public string Name;
            public string Usage;
            public string Help;
            public Action<string, Action<string>> Handler;
        }

        private static readonly Dictionary<string, Entry> Commands = new Dictionary<string, Entry>();

        /// <summary>
        /// Registers a command, for example "/resupply". The handler gets the text typed after the command and a function that prints one line on the terminal's screen; it may
        /// call that function later, for work that takes a while. A command registered twice keeps the latest handler. /help and /cls belong to the terminal itself.
        /// </summary>
        public static void Register(string name, string usage, string help, Action<string, Action<string>> handler)
        {
            if (string.IsNullOrWhiteSpace(name) || handler == null)
                return;

            var key = (name.StartsWith("/") ? name : "/" + name).Trim().ToLowerInvariant();
            if (key == "/help" || key == "/cls")
                return;

            Commands[key] = new Entry { Name = key, Usage = string.IsNullOrWhiteSpace(usage) ? key : usage, Help = help ?? "", Handler = handler };
            ModLog.Note("Terminal command registered: " + key);
        }

        internal sealed class ChatEntry
        {
            public string Name, Usage, Help;
            public bool Hidden;   // an alias: known, not listed
            public Func<bool> Active;
        }

        private static readonly Dictionary<string, ChatEntry> ChatCommands = new Dictionary<string, ChatEntry>();

        /// <summary>
        /// A chat command of some mod, for /help to list (it is typed in the game's chat, not here). Active says whether that mod is switched on. Called by reflection with plain types
        /// (see ChatHook in the shared source), so a mod needs no reference to this one.
        /// </summary>
        public static void ListChat(string name, string usage, string help, Func<bool> active)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            var key = (name.StartsWith("/") ? name : "/" + name).Trim().ToLowerInvariant();
            ChatCommands[key] = new ChatEntry { Name = key, Usage = string.IsNullOrEmpty(usage) ? key : usage, Help = help ?? "", Hidden = usage == "", Active = active };
        }

        internal static List<ChatEntry> Chat() => ChatCommands.Values.Where(e => !e.Hidden && (e.Active == null || e.Active())).OrderBy(e => e.Name).ToList();

        private const int RecentKept = 20;
        private static readonly List<string> Recent = new List<string>();

        /// <summary>
        /// Posts a message on every terminal screen ("* message"), for example "Unlocked: T2 Backpack". A terminal built or loaded later shows the latest few as well.
        /// Like Register it is meant to be called by reflection with a plain string, so a mod needs no reference to this one.
        /// </summary>
        public static void Notify(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var line = "* " + message.Trim();
            Recent.Add(line);
            if (Recent.Count > RecentKept)
                Recent.RemoveAt(0);

            TerminalScreen.PrintOnAll(line);
        }

        internal static List<string> LatestNotices(int count) => Recent.Skip(System.Math.Max(0, Recent.Count - count)).ToList();

        internal static ChatEntry FindChat(string key) => ChatCommands.TryGetValue(key, out var entry) ? entry : null;

        internal static Entry Find(string key) => Commands.TryGetValue(key, out var entry) ? entry : null;

        internal static List<Entry> All() => Commands.Values.OrderBy(e => e.Name).ToList();
    }
}
