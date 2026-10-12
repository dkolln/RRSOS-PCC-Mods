using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RRSOS.PCC.Shared;
using SpaceCraft;
using UnityEngine;

namespace RRSOS.PCC.Commands
{
    /// <summary>
    /// What a terminal does with a line you typed. Its own commands are /help and /cls; every other command is one of the loaded RRSOS mods' chat commands, run through the
    /// game's chat command list exactly as if typed in the chat, with the replies printed on the screen. Anything else is "Bad command or file name".
    /// </summary>
    internal static class TerminalShell
    {
        private const float ReplyWindowSeconds = 8f;

        public static void Run(TerminalScreen screen, string input)
        {
            screen.Print(TerminalScreen.Prompt + input);

            var line = input.Trim();
            if (!line.StartsWith("/"))
                line = "/" + line;

            var space = line.IndexOf(' ');
            var key = (space < 0 ? line : line.Substring(0, space)).ToLowerInvariant();
            var args = space < 0 ? "" : line.Substring(space + 1).Trim();

            if (key == "/help")
            {
                Help(screen);
                return;
            }

            if (key == "/cls")
            {
                screen.Clear();
                return;
            }

            var commands = ChatCommandList();
            if (commands == null || !ModCatalog.CommandNames().Contains(key) || !commands.TryGetValue(key, out var handler))
            {
                screen.Print("Bad command or file name");
                return;
            }

            // Replies arrive as chat messages from the game; the next few seconds of them belong to this screen.
            TerminalScreen.Active = screen;
            TerminalScreen.ActiveUntil = Time.realtimeSinceStartup + ReplyWindowSeconds;
            handler(args);
        }

        /// <summary>A chat message that is not from a player (a mod's reply) goes to the screen that ran the command, while its window is open.</summary>
        public static void OnChatMessage(PlayerData? from, string message)
        {
            var screen = TerminalScreen.Active;
            if (from != null || screen == null || Time.realtimeSinceStartup > TerminalScreen.ActiveUntil)
                return;

            screen.Print(message);
            TerminalScreen.ActiveUntil = Time.realtimeSinceStartup + ReplyWindowSeconds;
        }

        private static void Help(TerminalScreen screen)
        {
            var commands = ChatCommandList();
            screen.Print("Commands you can use here:");
            var any = false;
            foreach (var mod in ModCatalog.Loaded())
            {
                if (mod.commands == null)
                    continue;

                foreach (var command in mod.commands)
                {
                    // Only a command the mod has actually registered (a mod that is switched off registers none).
                    if (string.IsNullOrEmpty(command.name) || commands == null || !commands.ContainsKey(command.name.ToLowerInvariant()))
                        continue;

                    any = true;
                    screen.Print((string.IsNullOrEmpty(command.usage) ? command.name : command.usage) + (string.IsNullOrEmpty(command.help) ? "" : " - " + command.help));
                }
            }

            if (!any)
                screen.Print("(no mod commands are active)");

            screen.Print("/cls - clear the screen");
            screen.Print("/help - this list");
        }

        /// <summary>The game's chat command list (a private dictionary on ChatHandler), or null if the game no longer has it.</summary>
        private static Dictionary<string, Action<string>> ChatCommandList()
        {
            var chat = ChatHandler.Instance;
            if (chat == null)
                return null;

            var field = typeof(ChatHandler).GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(chat) as Dictionary<string, Action<string>>;
        }
    }
}
