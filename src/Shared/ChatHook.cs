using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SpaceCraft;
using Unity.Netcode;
using UnityEngine;

namespace RRSOS.PCC.Shared
{
    /// <summary>
    /// Adds slash commands to the game's own chat. The game keeps its chat commands (/help, /list, /kick, ...) in a private dictionary on ChatHandler; this adds a
    /// mod's commands to it every time a world creates a new ChatHandler. Host only. A mod that only wants to talk in the chat uses Say and does not add the component.
    /// </summary>
    internal sealed class ChatHook : MonoBehaviour
    {
        private static readonly Dictionary<string, Action<string>> Commands = new Dictionary<string, Action<string>>();
        private static string _helpLine = "";

        /// <summary>Whether the mod is switched on; the commands are added only while this says true.</summary>
        public static Func<bool> Enabled = () => true;

        private ChatHandler _registeredOn;

        /// <summary>Adds a command (for example "/build") and a line for /help. Call it before the component starts.</summary>
        public static void Add(string name, Action<string> handler, string help, string usage = null)
        {
            // told to the terminal as a chat command: its /help lists it, and typing it there points the player to the chat; an empty usage keeps it off the list (an alias)
            ForTerminal.Add(new string[] { name, usage ?? name, help });

            Commands[name] = Guarded(handler);
            _helpLine = (_helpLine.Length == 0 ? "" : _helpLine + ", ") + name + " (" + help + ")";
        }

        private static readonly List<string[]> ForTerminal = new List<string[]>();
        private static int _listed;   // how many of ForTerminal the terminal has been told about
        private static int _listTries;

        // Tells the terminal (the Commands mod, found by reflection with plain types) about this mod's chat commands, once it is loaded; its /help then lists them.
        private static void ListOnTerminal()
        {
            if (_listed >= ForTerminal.Count || _listTries > 300)
                return;

            _listTries++;
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("com.rrsos.pcc.commands", out var info) || info.Instance == null)
                return;

            var api = info.Instance.GetType().Assembly.GetType("RRSOS.PCC.Commands.TerminalApi");
            var list = api == null ? null : api.GetMethod("ListChat", BindingFlags.Public | BindingFlags.Static);
            if (list == null)
            {
                _listTries = int.MaxValue / 2; // a terminal from before this; leave it
                return;
            }

            Func<bool> active = () => Enabled();
            for (; _listed < ForTerminal.Count; _listed++)
                list.Invoke(null, new object[] { ForTerminal[_listed][0], ForTerminal[_listed][1], ForTerminal[_listed][2], active });
        }

        public static void Say(string message)
        {
            ModLog.Note("chat: " + message);
            if (ChatHandler.Instance != null)
                ChatHandler.Instance.SendServiceMessage(message);
        }

        private IEnumerator Start()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(1f);

                try
                {
                    ListOnTerminal();
                    var chat = ChatHandler.Instance;
                    if (!Enabled() || chat == null || chat == _registeredOn || Commands.Count == 0)
                        continue;

                    var field = typeof(ChatHandler).GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic);
                    var commands = field == null ? null : field.GetValue(chat) as Dictionary<string, Action<string>>;
                    if (commands == null)
                    {
                        ModLog.Note("Could not reach the chat's command list; the chat commands are not available (the game may have changed).");
                        _registeredOn = chat; // do not repeat every second
                        continue;
                    }

                    foreach (var pair in Commands)
                        commands[pair.Key] = pair.Value;

                    // The game's /help is a fixed text; add this mod's line after it.
                    if (commands.TryGetValue("/help", out var gameHelp))
                    {
                        var line = "RRSOS: " + _helpLine;
                        commands["/help"] = args =>
                        {
                            gameHelp(args);
                            chat.SendServiceMessage(line);
                        };
                    }

                    _registeredOn = chat;
                    ModLog.Note("Chat commands ready: " + string.Join(", ", new List<string>(Commands.Keys).ToArray()) + ".");
                }
                catch (Exception e)
                {
                    ModLog.Note("Could not add the chat commands: " + e.GetType().Name + ": " + e.Message);
                }
            }
        }

        // A command that throws must never break the chat, and only the host places things.
        private static Action<string> Guarded(Action<string> command)
        {
            return args =>
            {
                try
                {
                    if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                    {
                        Say("Only the host can use this.");
                        return;
                    }

                    command(args ?? "");
                }
                catch (Exception e)
                {
                    ModLog.Note("Command failed: " + e.GetType().Name + ": " + e.Message);
                    Say("That did not work (" + e.Message + "). See the mod's log.");
                }
            };
        }
    }
}
