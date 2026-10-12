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

        internal static Entry Find(string key) => Commands.TryGetValue(key, out var entry) ? entry : null;

        internal static List<Entry> All() => Commands.Values.OrderBy(e => e.Name).ToList();
    }
}
