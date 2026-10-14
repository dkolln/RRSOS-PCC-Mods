using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace RRSOS.PCC.Shared
{
    /// <summary>
    /// How a mod puts a command on the terminal (the Commands mod) without depending on it. The terminal knows nothing about other mods; a mod that has terminal commands
    /// looks the terminal up among the loaded plugins and registers each command by reflection, using only basic types so no assembly is shared. When the terminal is not
    /// installed nothing happens, and the mod works without it. A mod using this should declare the terminal as a soft dependency so that it loads first.
    /// </summary>
    internal static class TerminalRegistration
    {
        public const string TerminalGuid = "com.rrsos.pcc.commands";

        /// <summary>
        /// Posts a message on the terminal's screens. Returns false when the terminal is not installed (the caller may say it some other way).
        /// </summary>
        public static bool TryNotify(string message)
        {
            if (!Chainloader.PluginInfos.TryGetValue(TerminalGuid, out var info) || info.Instance == null)
                return false;

            var api = info.Instance.GetType().Assembly.GetType("RRSOS.PCC.Commands.TerminalApi");
            var notify = api == null ? null : api.GetMethod("Notify", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (notify == null)
                return false; // a terminal from before messages

            notify.Invoke(null, new object[] { message });
            return true;
        }

        /// <summary>
        /// A message for the player, shown on the terminal's screens and nowhere else. With no terminal installed (or none built) it is only written to the mod's log, never to the chat.
        /// The chat is kept for the start-up sequence at planetfall and for the answers to commands typed into it.
        /// </summary>
        public static void Notice(string message)
        {
            ModLog.Note("notice: " + message);
            try
            {
                TryNotify(message);
            }
            catch (Exception e)
            {
                ModLog.LogOnce("notice-terminal", "Could not post on the terminal: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>
        /// Registers a terminal command. The handler gets the text typed after the command and a function that prints a line on the terminal's screen (it may be called later,
        /// for work that takes a while). Returns true once registered, false while the terminal is not loaded (try again later).
        /// </summary>
        public static bool TryRegister(string name, string usage, string help, Action<string, Action<string>> handler)
        {
            if (!Chainloader.PluginInfos.TryGetValue(TerminalGuid, out var info) || info.Instance == null)
                return false;

            var api = info.Instance.GetType().Assembly.GetType("RRSOS.PCC.Commands.TerminalApi");
            var register = api == null ? null : api.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
            if (register == null)
                throw new InvalidOperationException("The terminal mod has no TerminalApi.Register (a different version?).");

            register.Invoke(null, new object[] { name, usage, help, handler });
            return true;
        }
    }
}
