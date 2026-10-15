using System;
using RRSOS.PCC.Shared;

namespace RRSOS.PCC.Commands
{
    /// <summary>
    /// What a terminal does with a line you typed. Its own commands are /help and /cls; every other command is one that some installed mod registered with the terminal
    /// (see TerminalApi). Anything else is "Bad command or file name".
    /// </summary>
    internal static class TerminalShell
    {
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

            var command = TerminalApi.Find(key);
            if (command == null)
            {
                screen.Print("Bad command or file name");
                return;
            }

            try
            {
                command.Handler(args, screen.Print);
            }
            catch (Exception e)
            {
                ModLog.Note("Terminal command " + key + " failed: " + e.GetType().Name + ": " + e.Message);
                screen.Print("That did not work: " + e.Message);
            }
        }

        private static void Help(TerminalScreen screen)
        {
            screen.Print("Commands you can use here:");
            var commands = TerminalApi.All();
            foreach (var command in commands)
                screen.Print(command.Usage + (command.Help.Length == 0 ? "" : " - " + command.Help));

            var chat = TerminalApi.Chat();
            if (chat.Count > 0)
            {
                screen.Print("Chat commands (type these in the game's chat):");
                foreach (var command in chat)
                    screen.Print(command.Usage + (command.Help.Length == 0 ? "" : " - " + command.Help));
            }
            else if (commands.Count == 0)
                screen.Print("(no mod has added a command)");

            screen.Print("/cls - clear the screen");
            screen.Print("/help - this list");
        }
    }
}
